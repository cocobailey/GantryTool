using System.Collections.Generic;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>A lane across the arrangement view, in metres from the view's left edge.</summary>
    public struct ArrangeLane
    {
        public float X0, X1;
        public bool Facing;   // its drivers see this face
    }

    /// <summary>
    /// The arrangement view's sums: a face of the gantry as its drivers see it, and which lanes a dragged sign lands on.
    /// Lane numbers are 0-based here, left to right. Testable outside the game.
    /// </summary>
    public static class GuideArrange
    {
        private const float SnapDistance = 0.4f;

        /// <summary>Distance from the view's left edge of a point along the beam; the same sum goes both ways.</summary>
        public static float ViewX(float along, float span, sbyte side)
        {
            return side < 0 ? along : span - along;
        }

        /// <summary>Every lane under the gantry, left to right as this face's drivers see them.</summary>
        public static List<ArrangeLane> Lanes(List<GantryLane> lanes, float span, sbyte side)
        {
            var result = new List<ArrangeLane>();
            foreach (GantryLane l in lanes)
            {
                float a = ViewX(l.Offset - l.Width * 0.5f, span, side), b = ViewX(l.Offset + l.Width * 0.5f, span, side);
                result.Add(new ArrangeLane
                {
                    X0 = Mathf.Min(a, b),
                    X1 = Mathf.Max(a, b),
                    Facing = l.TravelSign == 0 || l.TravelSign == -side
                });
            }

            result.Sort((p, q) => p.X0.CompareTo(q.X0));
            return result;
        }

        /// <summary>Only the lanes this face's drivers use: the ones a sign's lane numbers count.</summary>
        public static List<ArrangeLane> Facing(List<ArrangeLane> lanes)
        {
            var facing = new List<ArrangeLane>();
            foreach (ArrangeLane l in lanes)
            {
                if (l.Facing)
                {
                    facing.Add(l);
                }
            }

            return facing;
        }

        /// <summary>Slides a run of lanes, keeping how many it covers, so its middle is nearest <paramref name="centre"/>.</summary>
        public static void Move(List<ArrangeLane> facing, float centre, ref int first, ref int last)
        {
            int count = Mathf.Clamp(last - first + 1, 1, facing.Count);
            int best = first;
            float bestDistance = float.MaxValue;
            for (int start = 0; start + count <= facing.Count; start++)
            {
                float distance = Mathf.Abs((facing[start].X0 + facing[start + count - 1].X1) * 0.5f - centre);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = start;
                }
            }

            first = best;
            last = best + count - 1;
        }

        /// <summary>Moves the run's left edge to the lane edge nearest <paramref name="x"/>; it always keeps its last lane.</summary>
        public static int LeftEdge(List<ArrangeLane> facing, float x, int last)
        {
            int best = last;
            for (int i = last; i >= 0; i--)
            {
                if (Mathf.Abs(facing[i].X0 - x) < Mathf.Abs(facing[best].X0 - x))
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Moves the run's right edge to the lane edge nearest <paramref name="x"/>; it always keeps its first lane.</summary>
        public static int RightEdge(List<ArrangeLane> facing, float x, int first)
        {
            int best = first;
            for (int i = first; i < facing.Count; i++)
            {
                if (Mathf.Abs(facing[i].X1 - x) < Mathf.Abs(facing[best].X1 - x))
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>For a sign placed by hand: settles on a lane's middle when it's dragged close to one.</summary>
        public static float SnapToLane(List<ArrangeLane> facing, float x)
        {
            foreach (ArrangeLane l in facing)
            {
                float middle = (l.X0 + l.X1) * 0.5f;
                if (Mathf.Abs(middle - x) <= SnapDistance)
                {
                    return middle;
                }
            }

            return x;
        }
    }
}
