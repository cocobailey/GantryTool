using System;
using System.Collections.Generic;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>Copy of everything saved about one gantry, for undo.</summary>
    public sealed class GantryState
    {
        public int Id;
        public Vector3 Start;
        public Vector3 End;
        public float PostHeight;
        public float BeamDepth;
        public float ClearanceOverride;
        public float SignalSizeOverride;
        public float HeightAboveRoad;
        public byte Style;
        public readonly List<SignalOverride> Overrides = new List<SignalOverride>();

        /// <summary>Call under GantryManager.Lock.</summary>
        public static GantryState Capture(Gantry g)
        {
            var s = new GantryState
            {
                Id = g.Id,
                Start = g.Start,
                End = g.End,
                PostHeight = g.PostHeight,
                BeamDepth = g.BeamDepth,
                ClearanceOverride = g.ClearanceOverride,
                SignalSizeOverride = g.SignalSizeOverride,
                HeightAboveRoad = g.HeightAboveRoad,
                Style = g.Style
            };
            s.Overrides.AddRange(g.Overrides);
            return s;
        }

        /// <summary>Call under GantryManager.Lock.</summary>
        public void ApplyTo(Gantry g)
        {
            g.Start = Start;
            g.End = End;
            g.PostHeight = PostHeight;
            g.BeamDepth = BeamDepth;
            g.ClearanceOverride = ClearanceOverride;
            g.SignalSizeOverride = SignalSizeOverride;
            g.HeightAboveRoad = HeightAboveRoad;
            g.Style = Style;
            g.Overrides.Clear();
            g.Overrides.AddRange(Overrides);
            g.MeshDirty = true;
        }

        public bool SameAs(Gantry g)
        {
            if (g.Start != Start || g.End != End || g.BeamDepth != BeamDepth ||
                !SameFloat(g.HeightAboveRoad, HeightAboveRoad) || !SameFloat(g.SignalSizeOverride, SignalSizeOverride) ||
                g.Style != Style || g.Overrides.Count != Overrides.Count)
            {
                return false;
            }

            for (int i = 0; i < Overrides.Count; i++)
            {
                SignalOverride a = Overrides[i], b = g.Overrides[i];
                if (a.Offset != b.Offset || a.Minus != b.Minus || a.Plus != b.Plus || a.Sign != b.Sign)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameFloat(float a, float b)
        {
            return (float.IsNaN(a) && float.IsNaN(b)) || a == b;
        }
    }

    /// <summary>
    /// Undo history for the current city session (cleared on unload, not saved). Records gantries placed,
    /// deleted and changed (moves, settings, signs). Main thread only.
    /// </summary>
    public static class UndoStack
    {
        private const int MaxEntries = 50;
        private const float CoalesceSeconds = 1.5f;

        private enum Kind
        {
            Added,
            Deleted,
            Modified
        }

        private sealed class Entry
        {
            public Kind Kind;
            public GantryState Before;
            public string Key;
            public float Time;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static int Count => Entries.Count;

        public static void Clear()
        {
            Entries.Clear();
        }

        public static void PushAdded(Gantry g)
        {
            Push(new Entry { Kind = Kind.Added, Before = Capture(g) });
        }

        public static void PushDeleted(Gantry g)
        {
            Push(new Entry { Kind = Kind.Deleted, Before = Capture(g) });
        }

        /// <summary>
        /// Records the state of <paramref name="g"/> before a change. Repeated changes with the same
        /// <paramref name="key"/> in quick succession (dragging a slider) collapse into one undo step.
        /// </summary>
        public static void PushModified(Gantry g, string key, bool coalesce = true)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (coalesce && key != null && Entries.Count > 0)
            {
                Entry top = Entries[Entries.Count - 1];
                if (top.Kind == Kind.Modified && top.Before.Id == g.Id && top.Key == key && now - top.Time < CoalesceSeconds)
                {
                    top.Time = now;
                    return;
                }
            }

            Push(new Entry { Kind = Kind.Modified, Before = Capture(g), Key = key, Time = now });
        }

        /// <summary>Drops the last entry if it recorded a change that didn't actually change anything.</summary>
        public static void DiscardIfUnchanged(Gantry g)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            Entry top = Entries[Entries.Count - 1];
            bool same;
            lock (GantryManager.Lock)
            {
                same = top.Kind == Kind.Modified && top.Before.Id == g.Id && top.Before.SameAs(g);
            }

            if (same)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        /// <summary>Undoes the last action. Returns a short description for the log, or null if nothing to undo.</summary>
        public static string Undo()
        {
            if (Entries.Count == 0)
            {
                return null;
            }

            Entry e = Entries[Entries.Count - 1];
            Entries.RemoveAt(Entries.Count - 1);
            try
            {
                switch (e.Kind)
                {
                    case Kind.Added:
                        GantryManager.Remove(e.Before.Id);
                        return "removed placed gantry #" + e.Before.Id;
                    case Kind.Deleted:
                        GantryManager.Restore(e.Before);
                        return "restored deleted gantry #" + e.Before.Id;
                    default:
                        Gantry g = GantryManager.Find(e.Before.Id);
                        if (g == null)
                        {
                            GantryManager.Restore(e.Before);
                        }
                        else
                        {
                            lock (GantryManager.Lock)
                            {
                                e.Before.ApplyTo(g);
                                g.SignalsDirty = true;
                            }
                        }

                        return "reverted " + (e.Key ?? "change") + " on gantry #" + e.Before.Id;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Undo failed", ex);
                return null;
            }
        }

        private static GantryState Capture(Gantry g)
        {
            lock (GantryManager.Lock)
            {
                return GantryState.Capture(g);
            }
        }

        private static void Push(Entry e)
        {
            Entries.Add(e);
            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(0);
            }
        }
    }
}
