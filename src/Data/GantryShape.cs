using System.Collections.Generic;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>The kinds of gantry: where its posts stand along the beam. Testable outside the game.</summary>
    public static class GantryShape
    {
        public const byte Span = 0;            // a post at each end
        public const byte CantileverStart = 1; // one post, at the end placed first
        public const byte CantileverEnd = 2;   // one post, at the end placed second
        public const byte MedianT = 3;         // one post part way along, the beam reaching out both sides
        public const byte Count = 4;

        public static readonly string[] Names = { "Span", "Cantilever", "Cantilever, other end", "Median T" };

        // What the structure is built from, on any kind of gantry.
        public const byte BoxTruss = 0;    // four chords
        public const byte FlatTruss = 1;   // two chords, one flat frame
        public const byte DoubleTube = 2;  // two pipes, one above the other, with light bracing
        public const byte SingleTube = 3;  // one thick pipe ("monotube")
        public const byte CurvedTube = 4;  // one pipe that bends down into its posts
        public const byte LookCount = 5;

        public static readonly string[] LookNames = { "Box truss", "Flat truss", "Double tube", "Single tube", "Curved tube" };

        public static byte ValidLook(byte look)
        {
            return look < LookCount ? look : BoxTruss;
        }

        /// <summary>
        /// The line of a curved tube, as (distance along the beam, height): up from the ground at each post, bending over
        /// into the beam. A free end just stops. Keeps clear of the bends meeting on a short gantry.
        /// </summary>
        public static List<Vector2> CurvedPath(byte type, float span, float beamY, float groundStart, float groundEnd, float bend)
        {
            const int Steps = 8;
            bool start = PostAtStart(type), end = PostAtEnd(type);
            float r = Mathf.Max(0.1f, Mathf.Min(bend, span * (start && end ? 0.25f : 0.5f)));
            var path = new List<Vector2>();
            if (start)
            {
                r = Mathf.Min(r, Mathf.Max(0.1f, beamY - groundStart));
                path.Add(new Vector2(0f, groundStart));
                for (int i = 0; i <= Steps; i++)
                {
                    float a = 0.5f * Mathf.PI * i / Steps;
                    path.Add(new Vector2(r - r * Mathf.Cos(a), beamY - r + r * Mathf.Sin(a)));
                }
            }
            else
            {
                path.Add(new Vector2(0f, beamY));
            }

            if (end)
            {
                r = Mathf.Min(r, Mathf.Max(0.1f, beamY - groundEnd));
                for (int i = Steps; i >= 0; i--)
                {
                    float a = 0.5f * Mathf.PI * i / Steps;
                    path.Add(new Vector2(span - r + r * Mathf.Cos(a), beamY - r + r * Mathf.Sin(a)));
                }

                path.Add(new Vector2(span, groundEnd));
            }
            else
            {
                path.Add(new Vector2(span, beamY));
            }

            return path;
        }

        public static byte Valid(byte type)
        {
            return type < Count ? type : Span;
        }

        public static bool PostAtStart(byte type)
        {
            return type == Span || type == CantileverStart;
        }

        public static bool PostAtEnd(byte type)
        {
            return type == Span || type == CantileverEnd;
        }

        /// <summary>Where a median T's post goes by itself: the middle of the widest gap between lanes, or else the beam's middle.</summary>
        public static float MedianPost(List<GantryLane> lanes, float span)
        {
            const float MinGap = 1f;
            float best = MinGap, at = span * 0.5f;
            var sorted = new List<GantryLane>(lanes);
            sorted.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            for (int i = 1; i < sorted.Count; i++)
            {
                float left = sorted[i - 1].Offset + sorted[i - 1].Width * 0.5f;
                float right = sorted[i].Offset - sorted[i].Width * 0.5f;
                if (right - left > best)
                {
                    best = right - left;
                    at = (left + right) * 0.5f;
                }
            }

            return Mathf.Clamp(at, 0f, Mathf.Max(0f, span));
        }

        /// <summary>A median T's post position: the player's, or the automatic one.</summary>
        public static float PostPosition(float postAlong, List<GantryLane> lanes, float span)
        {
            return float.IsNaN(postAlong) ? MedianPost(lanes, span) : Mathf.Clamp(postAlong, 0f, Mathf.Max(0f, span));
        }
    }
}
