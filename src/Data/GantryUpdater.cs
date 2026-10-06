using System;
using System.Collections.Generic;
using GantryTool.Detect;
using ICities;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Keeps gantries in sync with the roads beneath them.
    ///
    /// Harmony patches (simulation thread) only enqueue segment changes with the segment's bounds. Each frame,
    /// on the main thread, queued changes are matched against gantries (bounds overlap, or the gantry already
    /// references that segment) and matching gantries are scheduled for a rebuild after a short settle delay —
    /// upgrades release and re-create segments, and the delay stops a "no road" rebuild in between.
    /// A slow round-robin re-scan catches anything the patches miss.
    /// </summary>
    public sealed class GantryUpdater : ThreadingExtensionBase
    {
        private const float SettleDelay = 0.3f;     // seconds after the last change before rebuilding
        private const float SweepInterval = 5f;     // seconds between periodic sweeps
        private const int SweepBatch = 10;          // gantries re-scanned per sweep

        private struct Change
        {
            public ushort Segment;
            public Bounds Bounds;
            public bool Released;
        }

        private static readonly object QueueLock = new object();
        private static List<Change> _incoming = new List<Change>();
        private static List<Change> _processing = new List<Change>();

        /// <summary>Set by Loading; changes are ignored while no city is running.</summary>
        public static volatile bool Enabled;

        private static readonly Dictionary<int, float> Due = new Dictionary<int, float>();
        private static readonly List<int> DueReady = new List<int>();
        private static readonly List<Gantry> Scratch = new List<Gantry>();
        private static float _nextSweep;
        private static int _sweepCursor;

        /// <summary>Simulation thread: a segment's lanes were (re)built.</summary>
        public static void EnqueueUpdated(ushort segment, Bounds bounds)
        {
            Enqueue(new Change { Segment = segment, Bounds = bounds, Released = false });
        }

        /// <summary>Simulation thread: a segment is about to be released.</summary>
        public static void EnqueueReleased(ushort segment, Bounds bounds)
        {
            Enqueue(new Change { Segment = segment, Bounds = bounds, Released = true });
        }

        private static void Enqueue(Change change)
        {
            if (!Enabled)
            {
                return;
            }

            lock (QueueLock)
            {
                _incoming.Add(change);
            }
        }

        public static void Reset()
        {
            lock (QueueLock)
            {
                _incoming.Clear();
            }

            Due.Clear();
            _sweepCursor = 0;
            _nextSweep = 0f;
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                float now = Time.realtimeSinceStartup;
                MatchChanges(now);
                FlushDue(now);
                if (now >= _nextSweep)
                {
                    _nextSweep = now + SweepInterval;
                    Sweep();
                }
            }
            catch (Exception e)
            {
                Log.Error("GantryUpdater.OnUpdate failed", e);
            }
        }

        private static void MatchChanges(float now)
        {
            lock (QueueLock)
            {
                if (_incoming.Count == 0)
                {
                    return;
                }

                // Swap buffers so the simulation thread never waits on the matching work.
                List<Change> tmp = _processing;
                _processing = _incoming;
                _incoming = tmp;
            }

            GantryManager.GetAll(Scratch);
            for (int c = 0; c < _processing.Count; c++)
            {
                Change change = _processing[c];
                for (int i = 0; i < Scratch.Count; i++)
                {
                    Gantry g = Scratch[i];
                    if (!Affects(g, change))
                    {
                        continue;
                    }

                    bool already = Due.ContainsKey(g.Id);
                    Due[g.Id] = now + SettleDelay;
                    if (!already)
                    {
                        Log.Debug("Seg " + change.Segment + (change.Released ? " released" : " lanes updated") +
                                 " -> gantry #" + g.Id + " queued");
                    }
                }
            }

            _processing.Clear();
        }

        private static bool Affects(Gantry g, Change change)
        {
            for (int k = 0; k < g.Lanes.Count; k++)
            {
                if (g.Lanes[k].Segment == change.Segment)
                {
                    return true;
                }
            }

            // XZ overlap between the gantry line (padded) and the segment bounds.
            const float pad = 40f; // matches LaneScanner.MaxExtension: lanes are extended into node areas
            Bounds b = change.Bounds;
            float minX = Mathf.Min(g.Start.x, g.End.x) - pad, maxX = Mathf.Max(g.Start.x, g.End.x) + pad;
            float minZ = Mathf.Min(g.Start.z, g.End.z) - pad, maxZ = Mathf.Max(g.Start.z, g.End.z) + pad;
            return b.max.x >= minX && b.min.x <= maxX && b.max.z >= minZ && b.min.z <= maxZ;
        }

        private static void FlushDue(float now)
        {
            if (Due.Count == 0)
            {
                return;
            }

            DueReady.Clear();
            foreach (KeyValuePair<int, float> kv in Due)
            {
                if (now >= kv.Value)
                {
                    DueReady.Add(kv.Key);
                }
            }

            for (int i = 0; i < DueReady.Count; i++)
            {
                int id = DueReady[i];
                Due.Remove(id);
                Gantry g = GantryManager.Find(id);
                if (g != null)
                {
                    g.MeshDirty = true; // renderer rescans + rebuilds next frame
                }
            }
        }

        /// <summary>Fallback: re-scan a few gantries and rebuild any whose lanes no longer match.</summary>
        private static void Sweep()
        {
            GantryManager.GetAll(Scratch);
            if (Scratch.Count == 0)
            {
                return;
            }

            int n = Mathf.Min(SweepBatch, Scratch.Count);
            for (int k = 0; k < n; k++)
            {
                _sweepCursor = (_sweepCursor + 1) % Scratch.Count;
                Gantry g = Scratch[_sweepCursor];
                if (g.MeshDirty || g.Mesh == null || Due.ContainsKey(g.Id))
                {
                    continue;
                }

                LaneScanner.Result scan = LaneScanner.Scan(g.Start, g.End, verbose: false);
                if (!SameLanes(g, scan))
                {
                    Log.Info("Periodic check: gantry #" + g.Id + " out of date (" + g.Lanes.Count + " -> " +
                             scan.Lanes.Count + " lanes); rebuilding");
                    g.MeshDirty = true;
                }
            }
        }

        private static bool SameLanes(Gantry g, LaneScanner.Result scan)
        {
            if (g.Lanes.Count != scan.Lanes.Count)
            {
                return false;
            }

            if (float.IsNaN(g.RoadTopY) != float.IsNaN(scan.RoadTopY) ||
                (!float.IsNaN(scan.RoadTopY) && Mathf.Abs(g.RoadTopY - scan.RoadTopY) > 0.05f))
            {
                return false;
            }

            for (int i = 0; i < scan.Lanes.Count; i++)
            {
                GantryLane a = g.Lanes[i];
                GantryLane b = scan.Lanes[i];
                if (a.LaneId != b.LaneId || a.TravelSign != b.TravelSign || a.Restriction != b.Restriction ||
                    Mathf.Abs(a.Offset - b.Offset) > 0.05f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
