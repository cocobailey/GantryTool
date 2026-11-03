using System;
using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Guide;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>One rendered design: its size and shape, and the material showing it. Shared by signs with the same design.</summary>
    public sealed class GuideCacheEntry
    {
        public float Width, Height, TabHeight, Radius;
        public bool SquareLeft, SquareRight;
        public List<GuideTab> Tabs;
        public Texture2D Texture;
        public Material Material;
    }

    /// <summary>Bakes each distinct design once. Main thread only (makes Unity textures).</summary>
    public static class GuideCache
    {
        private const int CollectAbove = 16;
        private const int MaxPixels = 1024;
        private const float PixelsPerUnit = 3f;

        private static readonly Dictionary<string, GuideCacheEntry> Entries = new Dictionary<string, GuideCacheEntry>();

        /// <summary>The design's entry, baking it if needed; null if it can't be drawn (logged once).</summary>
        public static GuideCacheEntry Get(string design)
        {
            if (string.IsNullOrEmpty(design))
            {
                return null;
            }

            GuideCacheEntry e;
            if (Entries.TryGetValue(design, out e))
            {
                return e;
            }

            if (Entries.Count >= CollectAbove)
            {
                Collect();
            }

            try
            {
                e = Bake(design);
            }
            catch (Exception ex)
            {
                Log.Error("Guide sign could not be drawn", ex);
                e = null;
            }

            Entries[design] = e;
            return e;
        }

        private static GuideCacheEntry Bake(string design)
        {
            var doc = Json.Parse(design) as Dictionary<string, object>;
            if (doc == null)
            {
                throw new FormatException("A guide sign design must be a JSON object");
            }

            GuideSign sign = GuideRenderer.Render(doc, MaxPixels, PixelsPerUnit);
            int w = sign.PixelWidth, h = sign.PixelHeight;

            // Bottom row first for Unity. The mesh has the sign's shape, so the texture is opaque; what lies outside the
            // shape takes the panel's edge colour, so nothing dark bleeds in as the texture shrinks with distance.
            Color32 edge = sign.Pixels[(h - 1) * w + w / 2];
            edge.a = 255;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color32 c = sign.Pixels[y * w + x];
                    if (c.a == 0)
                    {
                        c = edge;
                    }

                    c.a = 255;
                    px[(h - 1 - y) * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.ARGB32, true, false) { name = "GantryGuideSign" };
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels32(px);
            tex.Apply(true, true);

            Log.Debug("Baked guide sign: " + w + " x " + h + " px");
            return new GuideCacheEntry
            {
                Width = sign.Width,
                Height = sign.Height,
                TabHeight = sign.TabHeight,
                Radius = sign.Radius,
                SquareLeft = sign.SquareLeft,
                SquareRight = sign.SquareRight,
                Tabs = sign.Tabs,
                Texture = tex,
                Material = GantryMaterials.CreateGuideMaterial(tex)
            };
        }

        /// <summary>Frees designs no gantry shows any more (e.g. while a sign is being edited).</summary>
        public static void Collect()
        {
            var used = new HashSet<string>();
            var gantries = new List<Gantry>();
            GantryManager.GetAll(gantries);
            lock (GantryManager.Lock)
            {
                foreach (Gantry g in gantries)
                {
                    foreach (GuideHead h in g.GuideSigns)
                    {
                        if (h.Design != null)
                        {
                            used.Add(h.Design);
                        }
                    }
                }
            }

            var drop = new List<string>();
            foreach (KeyValuePair<string, GuideCacheEntry> kv in Entries)
            {
                if (!used.Contains(kv.Key))
                {
                    drop.Add(kv.Key);
                }
            }

            foreach (string key in drop)
            {
                Free(Entries[key]);
                Entries.Remove(key);
            }
        }

        public static void Release()
        {
            foreach (GuideCacheEntry e in Entries.Values)
            {
                Free(e);
            }

            Entries.Clear();
        }

        private static void Free(GuideCacheEntry e)
        {
            if (e != null)
            {
                UnityEngine.Object.Destroy(e.Material);
                UnityEngine.Object.Destroy(e.Texture);
            }
        }
    }

    /// <summary>
    /// Builds a gantry's guide signs: one mesh per sign face (with its own material) and one shared mesh for the plain
    /// backs and edges. The mesh has the sign's rounded shape and its exit tabs, so no transparency is needed.
    /// </summary>
    public static class GuideMeshBuilder
    {
        /// <summary>Theme units are roughly inches of a full-size sign; 70% of real size looks right beside the game's roads.</summary>
        public const float MetresPerUnit = 0.0254f * 0.7f;

        private const float FacePlane = 0.1f;   // face in front of the truss side
        private const float Depth = 0.08f;      // panel thickness
        private const int CornerSteps = 5;

        /// <summary>A sign's size in metres and where its centre is along the beam; false if its design can't be drawn.</summary>
        public static bool Layout(GuideHead h, float span, out float along, out float width, out float height)
        {
            along = h.Along;
            width = height = 0f;
            GuideCacheEntry e = GuideCache.Get(h.Design);
            if (e == null)
            {
                return false;
            }

            float m = MetresPerUnit * h.Scale;
            width = e.Width * m;
            height = e.Height * m;
            float half = width * 0.5f;
            along = span > width ? Mathf.Clamp(h.Along, half, span - half) : span * 0.5f;
            return true;
        }

        public static void Build(Gantry g)
        {
            foreach (GuideDraw d in g.GuideDraws)
            {
                UnityEngine.Object.Destroy(d.Mesh);
            }

            g.GuideDraws.Clear();
            if (g.GuideBackMesh != null)
            {
                UnityEngine.Object.Destroy(g.GuideBackMesh);
                g.GuideBackMesh = null;
            }

            if (g.GuideSigns.Count == 0)
            {
                return;
            }

            Vector3 flat = g.End - g.Start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            var back = new MeshData();
            foreach (GuideHead h in g.GuideSigns)
            {
                float along, width, height;
                if (!Layout(h, span, out along, out width, out height))
                {
                    continue;
                }

                GuideCacheEntry e = GuideCache.Get(h.Design);
                Vector3 normal = side * h.Side;
                Vector3 right = Vector3.Cross(Vector3.up, -normal).normalized;
                float midY = SignalMeshBuilder.HeadCentreY(g.BeamBottomY, g.BeamDepth, height);
                Vector3 centre = SignalMeshBuilder.HeadCentre(g.Start, g.End, g.BeamBottomY, g.BeamDepth, along);
                centre.y = midY;
                centre = centre - g.Origin + normal * (SignalMeshBuilder.HeadDepth * 0.5f + FacePlane);

                float m = MetresPerUnit * h.Scale;
                var face = new MeshData();
                foreach (List<Vector2> outline in Outlines(e))
                {
                    AddSign(face, back, outline, e, centre, right, normal, m);
                }

                g.GuideDraws.Add(new GuideDraw { Mesh = face.ToMesh("GantryGuideFace"), Material = e.Material });
            }

            g.GuideBackMesh = back.Verts.Count > 0 ? back.ToMesh("GantryGuideBack") : null;
        }

        /// <summary>The sign's convex parts in sign units (y down): the panel, then each tab.</summary>
        private static List<List<Vector2>> Outlines(GuideCacheEntry e)
        {
            float r = e.Radius, top = e.TabHeight;
            var list = new List<List<Vector2>>
            {
                RoundRect(0f, top, e.Width, e.Height, e.SquareLeft ? 0f : r, e.SquareRight ? 0f : r, r, r)
            };
            if (e.Tabs != null)
            {
                foreach (GuideTab t in e.Tabs)
                {
                    list.Add(RoundRect(t.X, top - t.Height, t.X + t.Width, top, r, r, 0f, 0f));
                }
            }

            return list;
        }

        private static List<Vector2> RoundRect(float x0, float y0, float x1, float y1, float tl, float tr, float br, float bl)
        {
            var pts = new List<Vector2>();
            Corner(pts, x0 + tl, y0 + tl, tl, 180f);
            Corner(pts, x1 - tr, y0 + tr, tr, 270f);
            Corner(pts, x1 - br, y1 - br, br, 0f);
            Corner(pts, x0 + bl, y1 - bl, bl, 90f);
            return pts;
        }

        private static void Corner(List<Vector2> pts, float cx, float cy, float r, float startDeg)
        {
            if (r <= 0f)
            {
                pts.Add(new Vector2(cx, cy));
                return;
            }

            for (int i = 0; i <= CornerSteps; i++)
            {
                float a = (startDeg + 90f * i / CornerSteps) * Mathf.Deg2Rad;
                pts.Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
            }
        }

        private static void AddSign(MeshData face, MeshData back, List<Vector2> outline, GuideCacheEntry e, Vector3 centre, Vector3 right, Vector3 normal, float m)
        {
            Func<Vector2, Vector3> place = p => centre + right * ((p.x - e.Width * 0.5f) * m) + Vector3.up * ((e.Height * 0.5f - p.y) * m);
            var t = new Vector4(right.x, right.y, right.z, 1f);

            // Face: a fan from the middle, texture upright.
            Vector2 mid = Vector2.zero;
            foreach (Vector2 p in outline)
            {
                mid += p;
            }

            mid /= outline.Count;
            face.Fan(place, outline, mid, normal, t, p => new Vector2(p.x / e.Width, 1f - p.y / e.Height), Vector3.zero);

            // Back and edges, in the truss's steel.
            Vector3 toBack = -normal * Depth;
            back.Fan(place, outline, mid, -normal, new Vector4(-right.x, -right.y, -right.z, 1f), p => new Vector2(0.5f, 0.5f), toBack);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector3 a = place(outline[i]), b = place(outline[(i + 1) % outline.Count]);
                Vector3 outward = Vector3.Cross(b - a, normal);
                if (outward.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                outward.Normalize();
                if (Vector3.Dot(outward, (a + b) * 0.5f - place(mid)) < 0f)
                {
                    outward = -outward;
                }

                back.Quad(a, b, b + toBack, a + toBack, outward);
            }
        }

        private sealed class MeshData
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector4> _tangents = new List<Vector4>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _tris = new List<int>();

            private int Add(Vector3 v, Vector3 n, Vector4 t, Vector2 uv)
            {
                Verts.Add(v);
                _normals.Add(n);
                _tangents.Add(t);
                _uvs.Add(uv);
                return Verts.Count - 1;
            }

            /// <summary>Triangles wound to face <paramref name="n"/> (same rule as the signal faces).</summary>
            private void Tri(int a, int b, int c, Vector3 n)
            {
                Vector3 geom = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
                if (Vector3.Dot(geom, n) >= 0f)
                {
                    _tris.Add(a);
                    _tris.Add(b);
                    _tris.Add(c);
                }
                else
                {
                    _tris.Add(a);
                    _tris.Add(c);
                    _tris.Add(b);
                }
            }

            public void Fan(Func<Vector2, Vector3> place, List<Vector2> outline, Vector2 mid, Vector3 n, Vector4 t, Func<Vector2, Vector2> uv, Vector3 offset)
            {
                int c = Add(place(mid) + offset, n, t, uv(mid));
                int first = Verts.Count;
                foreach (Vector2 p in outline)
                {
                    Add(place(p) + offset, n, t, uv(p));
                }

                for (int i = 0; i < outline.Count; i++)
                {
                    Tri(c, first + i, first + (i + 1) % outline.Count, n);
                }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                var t = new Vector4(1f, 0f, 0f, 1f);
                var uv = new Vector2(0.5f, 0.5f);
                int ia = Add(a, n, t, uv), ib = Add(b, n, t, uv), ic = Add(c, n, t, uv), id = Add(d, n, t, uv);
                Tri(ia, ib, ic, n);
                Tri(ia, ic, id, n);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetNormals(_normals);
                mesh.SetTangents(_tangents);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
