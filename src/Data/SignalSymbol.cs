using System.Collections.Generic;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// What one face of a signal head shows. Values are saved — append only, never renumber. 0-7 are the 1.0 signs
    /// (the only ones the 1.0-format save block can hold); everything after lives in the extension block.
    /// </summary>
    public enum SignalSymbol : ushort
    {
        Auto = 0,        // overrides only: use the detected direction
        Arrow = 1,       // green down arrow: lane open
        Cross = 2,       // red X: lane closed
        MergeLeft = 3,   // yellow arrow down-left
        MergeRight = 4,  // yellow arrow down-right
        Reserved = 5,    // white diamond: reserved lane
        Blank = 6,       // all LEDs off
        Hidden = 7,      // overrides only, set on both faces: no signal head at all (removed by the player)

        // Added in 1.1 (extension block only)
        SpeedAuto = 8,   // overrides only: the speed limit of the lane underneath (TM:PE-aware)
        SpeedLimit = 9,  // speed limit; value = km/h

        // Warnings and restrictions (extension block only)
        Roadworks = 10,
        Queue = 11,              // congestion ahead
        Accident = 12,
        Slippery = 13,
        Fog = 14,
        Danger = 15,             // general danger "!"
        NoOvertaking = 16,
        NoOvertakingTrucks = 17,
        EndRestrictions = 18,    // end of all restrictions

        YellowCross = 19         // North America: lane closing ahead
    }

    /// <summary>A face's sign plus its value (km/h for speed limits, 0 otherwise).</summary>
    public struct FaceSign
    {
        public SignalSymbol Symbol;
        public ushort Value;

        public FaceSign(SignalSymbol symbol, ushort value)
        {
            Symbol = symbol;
            Value = value;
        }

        public bool Equals(FaceSign o)
        {
            return o.Symbol == Symbol && o.Value == Value;
        }
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

        // Values of the faces' signs (km/h for SpeedLimit); extension block only.
        public ushort MinusValue;
        public ushort PlusValue;

        public FaceSign MinusFace => new FaceSign(Minus, MinusValue);
        public FaceSign PlusFace => new FaceSign(Plus, PlusValue);

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
                case SignalSymbol.SpeedAuto: return "SA";
                case SignalSymbol.SpeedLimit: return "S";
                case SignalSymbol.Roadworks: return "WR";
                case SignalSymbol.Queue: return "WQ";
                case SignalSymbol.Accident: return "WA";
                case SignalSymbol.Slippery: return "WS";
                case SignalSymbol.Fog: return "WF";
                case SignalSymbol.Danger: return "W!";
                case SignalSymbol.NoOvertaking: return "NO";
                case SignalSymbol.NoOvertakingTrucks: return "NT";
                case SignalSymbol.EndRestrictions: return "E";
                case SignalSymbol.YellowCross: return "Y";
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
                case SignalSymbol.SpeedAuto: return "Speed limit (road)";
                case SignalSymbol.SpeedLimit: return "Speed limit";
                case SignalSymbol.Roadworks: return "Roadworks";
                case SignalSymbol.Queue: return "Queue ahead";
                case SignalSymbol.Accident: return "Accident";
                case SignalSymbol.Slippery: return "Slippery road";
                case SignalSymbol.Fog: return "Fog";
                case SignalSymbol.Danger: return "Danger";
                case SignalSymbol.NoOvertaking: return "No overtaking";
                case SignalSymbol.NoOvertakingTrucks: return "No overtaking (trucks)";
                case SignalSymbol.EndRestrictions: return "End of restrictions";
                case SignalSymbol.YellowCross: return "Yellow X";
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
                case SignalSymbol.Roadworks: return "Warning: roadworks";
                case SignalSymbol.Queue: return "Warning: queue / congestion ahead";
                case SignalSymbol.Accident: return "Warning: accident ahead";
                case SignalSymbol.Slippery: return "Warning: slippery road";
                case SignalSymbol.Fog: return "Warning: fog";
                case SignalSymbol.Danger: return "Warning: other danger";
                case SignalSymbol.NoOvertaking: return "No overtaking";
                case SignalSymbol.NoOvertakingTrucks: return "No overtaking for trucks";
                case SignalSymbol.EndRestrictions: return "End of all restrictions";
                case SignalSymbol.YellowCross: return "Yellow X (lane closing ahead)";
                default: return PlainName(s);
            }
        }

        /// <summary>
        /// Automatic symbols: from the detected direction (two-way lanes show green on both sides), then the
        /// TM:PE restriction. Banned -> red X on both faces. Reserved (bus-only) -> diamond on the face(s) that
        /// would show the green arrow; the oncoming face keeps its red X.
        /// </summary>
        public static void Auto(int travelSign, LaneRestriction restriction, out SignalSymbol minus, out SignalSymbol plus,
            SignStyle style = SignStyle.Europe)
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

            if (SignStyles.BlankOpenLanes(style))
            {
                minus = minus == SignalSymbol.Arrow ? SignalSymbol.Blank : minus;
                plus = plus == SignalSymbol.Arrow ? SignalSymbol.Blank : plus;
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

        /// <summary>
        /// Final signs (with values) for a lane: auto, with any override applied per face. Auto speed becomes the
        /// lane's actual speed limit.
        /// </summary>
        public static void ResolveFaces(List<GantryLane> lanes, int laneIndex, List<SignalOverride> overrides,
            out FaceSign minus, out FaceSign plus, SignStyle style = SignStyle.Europe)
        {
            GantryLane lane = lanes[laneIndex];
            SignalSymbol am, ap;
            Auto(lane.TravelSign, lane.Restriction, out am, out ap, style);
            minus = new FaceSign(am, 0);
            plus = new FaceSign(ap, 0);
            int idx = FindOverride(lanes, laneIndex, overrides);
            if (idx >= 0)
            {
                if (overrides[idx].Minus != SignalSymbol.Auto)
                {
                    minus = overrides[idx].MinusFace;
                }

                if (overrides[idx].Plus != SignalSymbol.Auto)
                {
                    plus = overrides[idx].PlusFace;
                }
            }

            minus = ResolveSpeed(minus, lane);
            plus = ResolveSpeed(plus, lane);
        }

        private static FaceSign ResolveSpeed(FaceSign f, GantryLane lane)
        {
            if (f.Symbol != SignalSymbol.SpeedAuto)
            {
                return f;
            }

            int kmh = Mathf.RoundToInt(lane.SpeedKmh);
            return new FaceSign(SignalSymbol.SpeedLimit, (ushort)Mathf.Clamp(kmh, 0, 999));
        }

        /// <summary>Short name with value, e.g. "Speed 80 km/h".</summary>
        public static string PlainName(FaceSign f, bool mph)
        {
            if (f.Symbol == SignalSymbol.SpeedLimit)
            {
                return "Speed " + SpeedUnits.Display(f.Value, mph) + (mph ? " mph" : " km/h");
            }

            if (f.Symbol == SignalSymbol.SpeedAuto)
            {
                return "Road speed";
            }

            return PlainName(f.Symbol);
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
