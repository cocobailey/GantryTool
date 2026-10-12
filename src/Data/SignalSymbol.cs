using System.Collections.Generic;
using GantryTool.Detect;

namespace GantryTool.Data
{
    /// <summary>What one face of a signal head shows. Values are saved — append only, never renumber.</summary>
    public enum SignalSymbol : byte
    {
        Auto = 0,        // overrides only: use the detected direction
        Arrow = 1,       // green down arrow: lane open
        Cross = 2,       // red X: lane closed
        MergeLeft = 3,   // yellow arrow down-left
        MergeRight = 4,  // yellow arrow down-right
        Reserved = 5,    // white diamond: reserved lane
        Blank = 6,       // all LEDs off
        Hidden = 7       // overrides only, set on both faces: no signal head at all (removed by the player)
    }

    /// <summary>
    /// Manual symbol choice for the lane at <see cref="Offset"/> metres from the start post. Matched to
    /// lanes by position (not lane id) so it survives roads being reversed / rebuilt with the same layout.
    /// Faces are physical: Minus = face whose normal is -side (seen by traffic heading +side).
    /// </summary>
    public struct SignalOverride
    {
        public const sbyte UnknownSign = 2; // overrides from v2 saves didn't record the lane direction

        public float Offset;
        public SignalSymbol Minus;
        public SignalSymbol Plus;

        /// <summary>TravelSign of the lane when the override was set; breaks ties between nearby lanes.</summary>
        public sbyte Sign;

        // Per-head layout (extension block only; 1.0 ignores them). 0 = default for both.
        public float Nudge;   // metres along the beam from the lane centre
        public float Size;    // box size in metres; 0 = the gantry's size

        public bool HasLayout => Nudge != 0f || Size > 0f;

        /// <summary>True when the entry carries nothing (Auto faces, default layout) and can be dropped.</summary>
        public bool IsEmpty => Minus == SignalSymbol.Auto && Plus == SignalSymbol.Auto && !HasLayout;
    }

    public static class SignalRules
    {
        /// <summary>Max distance between an override's offset and a lane for them to match (m).</summary>
        public const float MatchTolerance = 1.0f;

        public static string ShortName(SignalSymbol s)
        {
            switch (s)
            {
                case SignalSymbol.Arrow: return "G";
                case SignalSymbol.Cross: return "R";
                case SignalSymbol.MergeLeft: return "ML";
                case SignalSymbol.MergeRight: return "MR";
                case SignalSymbol.Reserved: return "D";
                case SignalSymbol.Blank: return "-";
                case SignalSymbol.Hidden: return "x";
                default: return "?";
            }
        }

        /// <summary>Short name for status lines ("Auto: Green arrow").</summary>
        public static string PlainName(SignalSymbol s)
        {
            switch (s)
            {
                case SignalSymbol.Arrow: return "Green arrow";
                case SignalSymbol.Cross: return "Red X";
                case SignalSymbol.MergeLeft: return "Merge left";
                case SignalSymbol.MergeRight: return "Merge right";
                case SignalSymbol.Reserved: return "Diamond";
                case SignalSymbol.Blank: return "Blank";
                case SignalSymbol.Hidden: return "Removed";
                default: return "Auto";
            }
        }

        public static string DisplayName(SignalSymbol s)
        {
            switch (s)
            {
                case SignalSymbol.Auto: return "Auto";
                case SignalSymbol.Arrow: return "Green arrow (open)";
                case SignalSymbol.Cross: return "Red X (closed)";
                case SignalSymbol.MergeLeft: return "Yellow arrow down-left (merge)";
                case SignalSymbol.MergeRight: return "Yellow arrow down-right (merge)";
                case SignalSymbol.Reserved: return "White diamond (reserved)";
                case SignalSymbol.Blank: return "Blank (off)";
                case SignalSymbol.Hidden: return "Removed";
                default: return s.ToString();
            }
        }

        /// <summary>
        /// Automatic symbols: from the detected direction (two-way lanes show green on both sides), then the
        /// TM:PE restriction. Banned -> red X on both faces. Reserved (bus-only) -> diamond on the face(s) that
        /// would show the green arrow; the oncoming face keeps its red X.
        /// </summary>
        public static void Auto(int travelSign, LaneRestriction restriction, out SignalSymbol minus, out SignalSymbol plus)
        {
            minus = travelSign >= 0 ? SignalSymbol.Arrow : SignalSymbol.Cross;
            plus = travelSign <= 0 ? SignalSymbol.Arrow : SignalSymbol.Cross;

            if (restriction == LaneRestriction.Banned)
            {
                minus = SignalSymbol.Cross;
                plus = SignalSymbol.Cross;
            }
            else if (restriction == LaneRestriction.Reserved)
            {
                if (minus == SignalSymbol.Arrow)
                {
                    minus = SignalSymbol.Reserved;
                }

                if (plus == SignalSymbol.Arrow)
                {
                    plus = SignalSymbol.Reserved;
                }
            }
        }

        /// <summary>Index of the override matching a lane offset, or -1.</summary>
        private const float SignMismatchPenalty = 0.5f;

        /// <summary>How well an override fits a lane (lower is better); float.MaxValue = no match.</summary>
        private static float MatchCost(SignalOverride o, GantryLane lane)
        {
            float d = System.Math.Abs(o.Offset - lane.Offset);
            if (d > MatchTolerance)
            {
                return float.MaxValue;
            }

            if (o.Sign != SignalOverride.UnknownSign && o.Sign != lane.TravelSign)
            {
                d += SignMismatchPenalty;
            }

            return d;
        }

        /// <summary>
        /// Index of the override that belongs to lane <paramref name="laneIndex"/>, or -1. An override belongs
        /// to exactly one lane — the one it matches best — so editing or removing one head never affects a
        /// neighbouring head, however close.
        /// </summary>
        public static int FindOverride(List<GantryLane> lanes, int laneIndex, List<SignalOverride> overrides)
        {
            int best = -1;
            float bestCost = float.MaxValue;
            for (int k = 0; k < overrides.Count; k++)
            {
                float cost = MatchCost(overrides[k], lanes[laneIndex]);
                if (cost >= bestCost)
                {
                    continue;
                }

                // Only if no other lane is a better fit for this override.
                bool ownedByOther = false;
                for (int j = 0; j < lanes.Count; j++)
                {
                    if (j != laneIndex && MatchCost(overrides[k], lanes[j]) < cost)
                    {
                        ownedByOther = true;
                        break;
                    }
                }

                if (!ownedByOther)
                {
                    best = k;
                    bestCost = cost;
                }
            }

            return best;
        }

        /// <summary>Lane nearest to a remembered offset, preferring the same direction; -1 if none in range.</summary>
        public static int FindLane(List<GantryLane> lanes, float offset, int travelSign)
        {
            var probe = new SignalOverride { Offset = offset, Sign = (sbyte)travelSign };
            int best = -1;
            float bestCost = float.MaxValue;
            for (int j = 0; j < lanes.Count; j++)
            {
                float cost = MatchCost(probe, lanes[j]);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = j;
                }
            }

            return best;
        }

        /// <summary>True when the player removed this lane's head (both faces Hidden).</summary>
        public static bool IsHidden(SignalSymbol minus, SignalSymbol plus)
        {
            return minus == SignalSymbol.Hidden && plus == SignalSymbol.Hidden;
        }

        /// <summary>Final symbols for a lane: auto, with any override applied per face.</summary>
        public static void Resolve(List<GantryLane> lanes, int laneIndex, List<SignalOverride> overrides,
            out SignalSymbol minus, out SignalSymbol plus)
        {
            GantryLane lane = lanes[laneIndex];
            Auto(lane.TravelSign, lane.Restriction, out minus, out plus);
            int idx = FindOverride(lanes, laneIndex, overrides);
            if (idx >= 0)
            {
                if (overrides[idx].Minus != SignalSymbol.Auto)
                {
                    minus = overrides[idx].Minus;
                }

                if (overrides[idx].Plus != SignalSymbol.Auto)
                {
                    plus = overrides[idx].Plus;
                }
            }
        }
    }
}
