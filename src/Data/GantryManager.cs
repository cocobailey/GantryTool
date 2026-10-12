using System;
using System.Collections.Generic;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Owns all gantries of the loaded city. The list is guarded by <see cref="Lock"/> because
    /// it is also touched from the simulation thread (dirty marking) and the save thread.
    /// </summary>
    public static class GantryManager
    {
        /// <summary>Defaults for new gantries, from the options panel. "Post height" is the height above the road.</summary>
        public static float DefaultHeight => Settings.Current.HeightAboveRoad;
        public static float DefaultBeamDepth => Settings.Current.BeamDepth;
        public const float MinSpan = 6f;
        public const float MaxSpan = 80f;

        public static readonly object Lock = new object();

        private static readonly List<Gantry> _gantries = new List<Gantry>();
        private static int _nextId = 1;

        /// <summary>Copies the current gantries into <paramref name="output"/> (cleared first).</summary>
        public static void GetAll(List<Gantry> output)
        {
            output.Clear();
            lock (Lock)
            {
                output.AddRange(_gantries);
            }
        }

        public static int Count
        {
            get
            {
                lock (Lock)
                {
                    return _gantries.Count;
                }
            }
        }

        public static Gantry Add(Vector3 start, Vector3 end, float postHeight, float beamDepth)
        {
            var gantry = new Gantry
            {
                Start = start,
                End = end,
                PostHeight = postHeight,
                BeamDepth = beamDepth
            };

            lock (Lock)
            {
                gantry.Id = _nextId++;
                _gantries.Add(gantry);
            }

            return gantry;
        }

        /// <summary>Re-creates a gantry from a saved state under its original id (undo of a delete).</summary>
        public static Gantry Restore(GantryState state)
        {
            var gantry = new Gantry { Id = state.Id };
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == state.Id)
                    {
                        return _gantries[i];
                    }
                }

                state.ApplyTo(gantry);
                _gantries.Add(gantry);
                if (_nextId <= state.Id)
                {
                    _nextId = state.Id + 1;
                }
            }

            return gantry;
        }

        /// <summary>Moves a gantry's posts. Main thread; locked because the save thread reads positions.</summary>
        public static void SetEnds(Gantry g, Vector3 start, Vector3 end)
        {
            lock (Lock)
            {
                g.Start = start;
                g.End = end;
                g.MeshDirty = true;
            }
        }

        public static Gantry Find(int id)
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == id)
                    {
                        return _gantries[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the manual symbols for lane <paramref name="laneIndex"/>. Auto on both faces removes the
        /// override. Main thread; the save thread reads overrides under the same lock.
        /// </summary>
        public static void SetOverride(Gantry g, int laneIndex, SignalSymbol minus, SignalSymbol plus)
        {
            if (laneIndex < 0 || laneIndex >= g.Lanes.Count)
            {
                return;
            }

            float offset = g.Lanes[laneIndex].Offset;
            UndoStack.PushModified(g, "sign", coalesce: false);
            lock (Lock)
            {
                int idx = SignalRules.FindOverride(g.Lanes, laneIndex, g.Overrides);
                if (minus == SignalSymbol.Auto && plus == SignalSymbol.Auto)
                {
                    if (idx >= 0)
                    {
                        g.Overrides.RemoveAt(idx);
                    }
                }
                else
                {
                    var o = new SignalOverride
                    {
                        Offset = offset,
                        Minus = minus,
                        Plus = plus,
                        Sign = (sbyte)g.Lanes[laneIndex].TravelSign
                    };
                    if (idx >= 0)
                    {
                        g.Overrides[idx] = o;
                    }
                    else
                    {
                        g.Overrides.Add(o);
                    }
                }

                g.SignalsDirty = true;
            }

            Log.Info("Override gantry #" + g.Id + " lane @" + offset.ToString("F2") + "m: -side face=" + minus +
                     ", +side face=" + plus + " (" + g.Overrides.Count + " overrides on this gantry)");
        }

        /// <summary>Schedules every gantry for a rescan + rebuild (option changes). Main thread.</summary>
        public static void MarkAllDirty()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    _gantries[i].MeshDirty = true;
                }
            }
        }

        /// <summary>Applies the current default post height / beam depth to every existing gantry.</summary>
        public static int ApplyDefaultsToAll()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    _gantries[i].HeightAboveRoad = DefaultHeight;
                    _gantries[i].BeamDepth = DefaultBeamDepth;
                    _gantries[i].MeshDirty = true;
                }

                return _gantries.Count;
            }
        }

        /// <summary>Removes a gantry. Main thread only (destroys its mesh).</summary>
        public static bool Remove(int id)
        {
            Gantry removed = null;
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == id)
                    {
                        removed = _gantries[i];
                        _gantries.RemoveAt(i);
                        break;
                    }
                }
            }

            if (removed == null)
            {
                return false;
            }

            DestroyMesh(removed);
            return true;
        }

        /// <summary>
        /// Replaces the whole set with gantries read from a save; meshes are built lazily by the renderer.
        /// Runs on the loading/simulation thread, so it must not call UnityEngine.Object.Destroy. The set is
        /// normally already empty (cleared on the main thread in OnLevelUnloading).
        /// </summary>
        public static void ReplaceAll(List<Gantry> loaded)
        {
            lock (Lock)
            {
                if (_gantries.Count > 0)
                {
                    Log.Warn("ReplaceAll: dropping " + _gantries.Count + " gantries still in memory");
                    _gantries.Clear();
                }

                int maxId = 0;
                for (int i = 0; i < loaded.Count; i++)
                {
                    loaded[i].MeshDirty = true;
                    _gantries.Add(loaded[i]);
                    maxId = Math.Max(maxId, loaded[i].Id);
                }

                _nextId = maxId + 1;
            }
        }

        /// <summary>Destroys meshes and forgets every gantry. Called on level unload.</summary>
        public static void Clear()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    DestroyMesh(_gantries[i]);
                }

                _gantries.Clear();
                _nextId = 1;
            }
        }

        public static void DestroyMesh(Gantry gantry)
        {
            try
            {
                if (gantry.Mesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.Mesh);
                    gantry.Mesh = null;
                }

                if (gantry.SignalMesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.SignalMesh);
                    gantry.SignalMesh = null;
                }

                if (gantry.BlinkMesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.BlinkMesh);
                    gantry.BlinkMesh = null;
                }
            }
            catch (Exception e)
            {
                Log.Error("DestroyMesh failed for gantry #" + gantry.Id, e);
            }
        }
    }
}
