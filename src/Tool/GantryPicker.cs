using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Render;
using UnityEngine;

namespace GantryTool.Tool
{
    /// <summary>
    /// Ray picking against gantries: each gantry is tested as three oriented boxes (two posts + beam)
    /// matching GantryMeshBuilder's layout, slightly inflated so thin members are easy to hit.
    /// </summary>
    public static class GantryPicker
    {
        private const float Inflate = 0.5f;

        private static readonly List<Gantry> Scratch = new List<Gantry>();

        /// <summary>Returns the id of the nearest gantry hit by the ray, or 0. Main thread.</summary>
        public static int Pick(Ray ray, float maxDistance)
        {
            GantryManager.GetAll(Scratch);
            int best = 0;
            float bestT = maxDistance;
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].Mesh == null)
                {
                    continue; // not scanned/built yet, BeamBottomY unknown
                }

                float t;
                if (HitGantry(Scratch[i], ray, out t) && t < bestT)
                {
                    bestT = t;
                    best = Scratch[i].Id;
                }
            }

            return best;
        }

        /// <summary>
        /// Nearest signal head hit by the ray. Returns false if none; heads are tested slightly inflated.
        /// Main thread (reads Gantry.Lanes).
        /// </summary>
        public static bool PickHead(Ray ray, float maxDistance, out int gantryId, out int laneIndex)
        {
            GantryManager.GetAll(Scratch);
            gantryId = 0;
            laneIndex = -1;
            float bestT = maxDistance;
            for (int i = 0; i < Scratch.Count; i++)
            {
                Gantry g = Scratch[i];
                if (g.Mesh == null || g.Lanes.Count == 0)
                {
                    continue;
                }

                Vector3 flat = g.End - g.Start;
                flat.y = 0f;
                float span = flat.magnitude;
                if (span < 0.01f)
                {
                    continue;
                }

                Vector3 dir = flat / span;
                Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                float halfDepth = SignalMeshBuilder.HeadDepth * 0.5f + 0.1f;

                for (int k = 0; k < g.Lanes.Count; k++)
                {
                    float off, size;
                    lock (GantryManager.Lock)
                    {
                        SignalMeshBuilder.HeadLayout(g.Lanes, k, g.Overrides, g.SignalSize, g.BeamDepth, span, out off, out size);
                    }

                    float half = size * 0.5f + 0.1f;
                    float midY = SignalMeshBuilder.HeadCentreY(g.BeamBottomY, g.BeamDepth, size);
                    float t;
                    if (RayBox(ray, g.Start, dir, side, off - half, off + half, midY - half, midY + half, halfDepth, out t) &&
                        t < bestT)
                    {
                        bestT = t;
                        gantryId = g.Id;
                        laneIndex = k;
                    }
                }
            }

            return gantryId != 0;
        }

        private static bool HitGantry(Gantry g, Ray ray, out float t)
        {
            t = float.MaxValue;
            Vector3 flat = g.End - g.Start;
            flat.y = 0f;
            float span = flat.magnitude;
            if (span < 0.01f)
            {
                return false;
            }

            Vector3 dir = flat / span;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            float bottom = g.BeamBottomY;
            float top = bottom + g.BeamDepth;
            float halfPost = 0.3f + Inflate;
            float halfWidth = GantryMeshBuilder.BeamWidth * 0.5f + 0.1f + Inflate;

            bool hit = false;
            float ti;

            // Posts (box centred on each ground point, from slightly below ground to beam top).
            if (RayBox(ray, g.Start, dir, side, -halfPost, halfPost, g.Start.y - 0.5f, top + Inflate, halfWidth, out ti))
            {
                t = Mathf.Min(t, ti);
                hit = true;
            }

            if (RayBox(ray, g.End, dir, side, -halfPost, halfPost, g.End.y - 0.5f, top + Inflate, halfWidth, out ti))
            {
                t = Mathf.Min(t, ti);
                hit = true;
            }

            // Beam.
            if (RayBox(ray, g.Start, dir, side, 0f, span, bottom - Inflate, top + Inflate, halfWidth, out ti))
            {
                t = Mathf.Min(t, ti);
                hit = true;
            }

            return hit;
        }

        /// <summary>
        /// Slab test against a box in a frame at <paramref name="anchor"/> (x along dir, z along side,
        /// world y). x in [x0,x1], y in [y0,y1] (absolute), z in [-halfZ, halfZ].
        /// </summary>
        private static bool RayBox(Ray ray, Vector3 anchor, Vector3 dir, Vector3 side,
            float x0, float x1, float y0, float y1, float halfZ, out float t)
        {
            t = 0f;
            Vector3 o = ray.origin - anchor;
            Vector3 d = ray.direction;

            float tMin = 0f;
            float tMax = float.MaxValue;

            if (!Slab(Vector3.Dot(o, dir), Vector3.Dot(d, dir), x0, x1, ref tMin, ref tMax) ||
                !Slab(ray.origin.y, d.y, y0, y1, ref tMin, ref tMax) ||
                !Slab(Vector3.Dot(o, side), Vector3.Dot(d, side), -halfZ, halfZ, ref tMin, ref tMax))
            {
                return false;
            }

            t = tMin;
            return true;
        }

        private static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(d) < 1e-6f)
            {
                return o >= min && o <= max;
            }

            float t1 = (min - o) / d;
            float t2 = (max - o) / d;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
