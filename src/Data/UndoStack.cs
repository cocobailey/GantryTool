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
        public readonly List<ExtraHead> ExtraHeads = new List<ExtraHead>();
        public readonly List<GuideHead> GuideSigns = new List<GuideHead>();
        public bool LaneHeads = true;
        public bool BlankBacks;
        public byte Type;
        public byte Look;
        public byte Paint;
        public bool Catwalk;
        public bool SignLights;
        public bool LiveSpeed;
        public readonly List<FaceMessages> Messages = new List<FaceMessages>();
        public float PostAlong = float.NaN;

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
            s.ExtraHeads.AddRange(g.ExtraHeads);
            s.GuideSigns.AddRange(g.GuideSigns);
            s.LaneHeads = g.LaneHeads;
            s.BlankBacks = g.BlankBacks;
            s.Type = g.Type;
            s.Look = g.Look;
            s.Paint = g.Paint;
            s.Catwalk = g.Catwalk;
            s.SignLights = g.SignLights;
            s.LiveSpeed = g.LiveSpeed;
            foreach (FaceMessages m in g.Messages)
            {
                s.Messages.Add(m.Copy());
            }

            s.PostAlong = g.PostAlong;
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
            g.ExtraHeads.Clear();
            g.ExtraHeads.AddRange(ExtraHeads);
            g.GuideSigns.Clear();
            g.GuideSigns.AddRange(GuideSigns);
            g.LaneHeads = LaneHeads;
            g.BlankBacks = BlankBacks;
            g.Type = Type;
            g.Look = Look;
            g.Paint = Paint;
            g.Catwalk = Catwalk;
            g.SignLights = SignLights;
            g.LiveSpeed = LiveSpeed;
            g.Messages.Clear();
            foreach (FaceMessages m in Messages)
            {
                g.Messages.Add(m.Copy());
            }

            g.PostAlong = PostAlong;
            g.MeshDirty = true;
        }

        public bool SameAs(Gantry g)
        {
            if (g.Start != Start || g.End != End || g.BeamDepth != BeamDepth ||
                !SameFloat(g.HeightAboveRoad, HeightAboveRoad) || !SameFloat(g.SignalSizeOverride, SignalSizeOverride) ||
                g.Style != Style || g.Overrides.Count != Overrides.Count || g.ExtraHeads.Count != ExtraHeads.Count ||
                g.GuideSigns.Count != GuideSigns.Count || g.LaneHeads != LaneHeads || g.BlankBacks != BlankBacks ||
                g.Type != Type || g.Look != Look || g.Paint != Paint || g.Catwalk != Catwalk || g.SignLights != SignLights || g.LiveSpeed != LiveSpeed || g.Messages.Count != Messages.Count || !SameFloat(g.PostAlong, PostAlong))
            {
                return false;
            }

            for (int i = 0; i < Messages.Count; i++)
            {
                if (!Messages[i].Same(g.Messages[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < Overrides.Count; i++)
            {
                SignalOverride a = Overrides[i], b = g.Overrides[i];
                if (a.Offset != b.Offset || a.Minus != b.Minus || a.Plus != b.Plus || a.Sign != b.Sign ||
                    a.Nudge != b.Nudge || a.Size != b.Size || a.MinusValue != b.MinusValue || a.PlusValue != b.PlusValue)
                {
                    return false;
                }
            }

            for (int i = 0; i < ExtraHeads.Count; i++)
            {
                ExtraHead a = ExtraHeads[i], b = g.ExtraHeads[i];
                if (a.Id != b.Id || a.Along != b.Along || a.Size != b.Size || a.Linked != b.Linked || a.Nudge != b.Nudge ||
                    a.LinkOffset != b.LinkOffset || a.LinkSign != b.LinkSign || a.Minus != b.Minus || a.Plus != b.Plus ||
                    a.MinusValue != b.MinusValue || a.PlusValue != b.PlusValue)
                {
                    return false;
                }
            }

            for (int i = 0; i < GuideSigns.Count; i++)
            {
                GuideHead a = GuideSigns[i], b = g.GuideSigns[i];
                if (a.Id != b.Id || a.Along != b.Along || a.Scale != b.Scale || a.Side != b.Side || a.Design != b.Design ||
                    a.FitFirst != b.FitFirst || a.FitLast != b.FitLast || a.LaneArrows != b.LaneArrows)
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
    /// deleted and changed (moves, settings, signs), and what was undone, until something new is done. Main thread only.
    /// </summary>
    public static class UndoStack
    {
        private const int MaxEntries = 50;
        private const float CoalesceSeconds = 1.5f;
        private const float NotRecent = -1000000f;

        /// <summary>The time now; tests outside the game give their own.</summary>
        public static Func<float> Clock = () => UnityEngine.Time.realtimeSinceStartup;

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
            public int Group;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly List<Entry> Redos = new List<Entry>();
        private static List<Entry> _redosBeforePush;

        private static int _group, _lastGroup;

        public static int Count => Entries.Count;

        /// <summary>Everything recorded until <see cref="EndGroup"/> undoes and redoes as one step.</summary>
        public static void BeginGroup()
        {
            _group = ++_lastGroup;
        }

        public static void EndGroup()
        {
            _group = 0;
        }
        public static int RedoCount => Redos.Count;

        public static void Clear()
        {
            Entries.Clear();
            Redos.Clear();
            _redosBeforePush = null;
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
            float now = Clock();
            if (coalesce && key != null && Entries.Count > 0)
            {
                Entry top = Entries[Entries.Count - 1];
                if (top.Kind == Kind.Modified && top.Before.Id == g.Id && top.Key == key && now - top.Time < CoalesceSeconds)
                {
                    top.Time = now;
                    Redos.Clear();
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
                // Nothing happened after all, so what was undone can still be redone.
                Entries.RemoveAt(Entries.Count - 1);
                if (_redosBeforePush != null)
                {
                    Redos.AddRange(_redosBeforePush);
                    _redosBeforePush = null;
                }
            }
        }

        /// <summary>Undoes the last action. Returns a short description for the log, or null if nothing to undo.</summary>
        public static string Undo()
        {
            return Step(Entries, Redos);
        }

        /// <summary>Puts back the last thing undone. Returns a short description for the log, or null if there's nothing to redo.</summary>
        public static string Redo()
        {
            return Step(Redos, Entries);
        }

        /// <summary>Takes back the last step of one list, and notes in the other how to take that back in turn.</summary>
        private static string Step(List<Entry> from, List<Entry> to)
        {
            if (from.Count == 0)
            {
                return null;
            }

            // Changes made together go back together.
            int group = from[from.Count - 1].Group;
            string what = StepOne(from, to);
            while (group != 0 && from.Count > 0 && from[from.Count - 1].Group == group)
            {
                what = StepOne(from, to) ?? what;
            }

            return what;
        }

        private static string StepOne(List<Entry> from, List<Entry> to)
        {
            Entry e = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            if (from.Count > 0)
            {
                from[from.Count - 1].Time = NotRecent;  // the next change is a step of its own
            }

            try
            {
                Gantry g = GantryManager.Find(e.Before.Id);
                GantryState now = g != null ? Capture(g) : e.Before;
                string what;
                switch (e.Kind)
                {
                    case Kind.Added:
                        GantryManager.Remove(e.Before.Id);
                        to.Add(new Entry { Kind = Kind.Deleted, Before = now, Key = e.Key, Time = NotRecent, Group = e.Group });
                        what = "removed gantry #" + e.Before.Id;
                        break;
                    case Kind.Deleted:
                        GantryManager.Restore(e.Before);
                        to.Add(new Entry { Kind = Kind.Added, Before = e.Before, Key = e.Key, Time = NotRecent, Group = e.Group });
                        what = "restored gantry #" + e.Before.Id;
                        break;
                    default:
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

                        to.Add(new Entry { Kind = Kind.Modified, Before = now, Key = e.Key, Time = NotRecent, Group = e.Group });
                        what = "changed back " + (e.Key ?? "change") + " on gantry #" + e.Before.Id;
                        break;
                }

                if (to.Count > MaxEntries)
                {
                    to.RemoveAt(0);
                }

                return what;
            }
            catch (Exception ex)
            {
                Log.Error("Undo or redo failed", ex);
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
            _redosBeforePush = new List<Entry>(Redos);
            Redos.Clear();
            e.Group = _group;
            Entries.Add(e);
            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(0);
            }
        }
    }
}
