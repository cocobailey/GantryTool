using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
        public List<GuidePart> Parts;
        public Texture2D Texture;
        public Material Material;
    }

    /// <summary>
    /// Bakes each distinct design once, drawing it on a worker thread so an edit never holds up the game. Until a design
    /// is ready, whoever asked for it keeps showing what it had. Call from the main thread (makes Unity textures).
    /// </summary>
    public static class GuideCache
    {
        private const int CollectAbove = 16;
        private const int MaxPixels = 2048;     // a sign over many lanes is long; GuideRenderer also limits its area
        private const float PixelsPerUnit = 3f;

        private const int ThumbnailPixels = 256;

        private static readonly Dictionary<string, GuideCacheEntry> Entries = new Dictionary<string, GuideCacheEntry>();
        private static readonly Dictionary<string, Texture2D> Thumbnails = new Dictionary<string, Texture2D>();

        private sealed class Drawn
        {
            public string Design;
            public int Generation;
            public GuideSign Sign;
            public Color32[] Pixels;
            public Exception Error;
            public long Milliseconds;
        }

        private static readonly object Sync = new object();
        private static readonly List<string> Wanted = new List<string>();
        private static readonly List<Drawn> Finished = new List<Drawn>();
        private static string _drawing;
        private static bool _working;
        private static int _generation;

        /// <summary>The design's entry, baking it if needed; null if it can't be drawn (logged once).</summary>
        public static GuideCacheEntry Get(string design)
        {
            if (string.IsNullOrEmpty(design))
            {
                return null;
            }

            GuideCacheEntry e;
            TryGet(design, out e);
            return e;
        }

        /// <summary>True once a design has been dealt with (its entry is null if it can't be drawn); if not, starts on it.</summary>
        public static bool TryGet(string design, out GuideCacheEntry entry)
        {
            Pump();
            if (Entries.TryGetValue(design, out entry))
            {
                return true;
            }

            lock (Sync)
            {
                if (design != _drawing && !Wanted.Contains(design))
                {
                    Wanted.Add(design);
                }

                if (!_working)
                {
                    _working = true;
                    ThreadPool.QueueUserWorkItem(Work);
                }
            }

            return false;
        }

        /// <summary>
        /// The worker: draws the design asked for last and forgets the rest. While someone types, only the latest text
        /// matters, and anything still needed is asked for again.
        /// </summary>
        private static void Work(object unused)
        {
            while (true)
            {
                string design;
                int generation;
                lock (Sync)
                {
                    if (Wanted.Count == 0)
                    {
                        _working = false;
                        return;
                    }

                    design = Wanted[Wanted.Count - 1];
                    Wanted.Clear();
                    _drawing = design;
                    generation = _generation;
                }

                var done = new Drawn { Design = design, Generation = generation };
                var watch = Stopwatch.StartNew();
                try
                {
                    var doc = Json.Parse(design) as Dictionary<string, object>;
                    if (doc == null)
                    {
                        throw new FormatException("A guide sign design must be a JSON object");
                    }

                    done.Sign = GuideRenderer.Render(doc, MaxPixels, PixelsPerUnit);
                    done.Pixels = Upright(done.Sign);
                }
                catch (Exception ex)
                {
                    done.Error = ex;
                }

                done.Milliseconds = watch.ElapsedMilliseconds;
                lock (Sync)
                {
                    _drawing = null;
                    Finished.Add(done);
                }
            }
        }

        /// <summary>Takes in what the worker has finished.</summary>
        private static void Pump()
        {
            Drawn[] done;
            lock (Sync)
            {
                if (Finished.Count == 0)
                {
                    return;
                }

                done = Finished.ToArray();
                Finished.Clear();
            }

            foreach (Drawn d in done)
            {
                if (d.Generation != _generation)
                {
                    continue;
                }

                if (Entries.Count >= CollectAbove)
                {
                    Collect();
                }

                GuideCacheEntry e = null;
                if (d.Error != null)
                {
                    Log.Error("Guide sign could not be drawn", d.Error);
                }
                else
                {
                    var watch = Stopwatch.StartNew();
                    e = Bake(d.Sign, d.Pixels);
                    Log.Debug("Baked guide sign: " + d.Sign.PixelWidth + " x " + d.Sign.PixelHeight + " px, drawn in " + d.Milliseconds + " ms, uploaded in " + watch.ElapsedMilliseconds + " ms");
                }

                Entries[d.Design] = e;
            }
        }

        private static GuideCacheEntry Bake(GuideSign sign, Color32[] pixels)
        {
            Texture2D tex = ToTexture(pixels, sign.PixelWidth, sign.PixelHeight);
            return new GuideCacheEntry
            {
                Width = sign.Width,
                Height = sign.Height,
                TabHeight = sign.TabHeight,
                Radius = sign.Radius,
                SquareLeft = sign.SquareLeft,
                SquareRight = sign.SquareRight,
                Tabs = sign.Tabs,
                Parts = sign.Parts,
                Texture = tex,
                Material = GantryMaterials.CreateGuideMaterial(tex)
            };
        }

        /// <summary>A small picture of a template for the sign picker, kept until the level unloads; null if it can't be drawn.</summary>
        public static Texture2D Thumbnail(string theme, string name)
        {
            return Thumbnail(theme + "/" + name, () => GuideAssets.Template(theme, name));
        }

        /// <summary>The same for any design, under a key of the caller's; <see cref="ForgetThumbnail"/> if the design changes.</summary>
        public static Texture2D Thumbnail(string key, Func<Dictionary<string, object>> design)
        {
            Texture2D tex;
            if (Thumbnails.TryGetValue(key, out tex))
            {
                return tex;
            }

            try
            {
                tex = ToTexture(GuideRenderer.Render(design(), ThumbnailPixels, 2f));
            }
            catch (Exception ex)
            {
                Log.Error("Guide sign picture could not be drawn: " + key, ex);
                tex = null;
            }

            Thumbnails[key] = tex;
            return tex;
        }

        public static void ForgetThumbnail(string key)
        {
            Texture2D tex;
            if (Thumbnails.TryGetValue(key, out tex))
            {
                UnityEngine.Object.Destroy(tex);
                Thumbnails.Remove(key);
            }
        }

        /// <summary>A small picture of an arrow for the editor's direction buttons, kept like the thumbnails.</summary>
        public static Texture2D ArrowPicture(string direction)
        {
            string key = "arrow/" + direction;
            Texture2D tex;
            if (Thumbnails.TryGetValue(key, out tex))
            {
                return tex;
            }

            var arrow = new Dictionary<string, object> { { "type", "arrow" }, { "direction", direction } };
            var doc = new Dictionary<string, object>
            {
                { "theme", "us" }, { "border", 0.0 }, { "cornerRadius", 0.0 }, { "margin", 4.0 }, { "align", "center" },
                { "items", new List<object> { arrow } }
            };
            try
            {
                tex = ToTexture(GuideRenderer.Render(doc, 64, 2f));
            }
            catch (Exception ex)
            {
                Log.Error("Arrow picture could not be drawn: " + direction, ex);
                tex = null;
            }

            Thumbnails[key] = tex;
            return tex;
        }

        private static Texture2D ToTexture(GuideSign sign)
        {
            return ToTexture(Upright(sign), sign.PixelWidth, sign.PixelHeight);
        }

        private static Color32[] Upright(GuideSign sign)
        {
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

            return px;
        }

        private static Texture2D ToTexture(Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, true, false) { name = "GantryGuideSign" };
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Frees designs no gantry shows any more (e.g. while a sign is being edited).</summary>
        public static void Collect()
        {
            var used = new HashSet<string>();
            var showing = new HashSet<Material>();   // still on a gantry until its new design is ready
            var gantries = new List<Gantry>();
            GantryManager.GetAll(gantries);
            lock (GantryManager.Lock)
            {
                foreach (Gantry g in gantries)
                {
                    foreach (GuidePlacement p in g.GuidePlacements.Values)
                    {
                        used.Add(p.Design);
                    }

                    foreach (GuideDraw d in g.GuideDraws)
                    {
                        showing.Add(d.Material);
                    }
                }
            }

            var drop = new List<string>();
            foreach (KeyValuePair<string, GuideCacheEntry> kv in Entries)
            {
                if (!used.Contains(kv.Key) && (kv.Value == null || !showing.Contains(kv.Value.Material)))
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
            lock (Sync)
            {
                _generation++;
                Wanted.Clear();
                Finished.Clear();
            }

            foreach (GuideCacheEntry e in Entries.Values)
            {
                Free(e);
            }

            Entries.Clear();
            foreach (Texture2D t in Thumbnails.Values)
            {
                UnityEngine.Object.Destroy(t);
            }

            Thumbnails.Clear();
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
    /// The visible parts cast no shadow: a flat panel shadowing itself comes out striped when the sun is low across it.
    /// A third mesh, drawn only into the shadow map and set back behind the sign, casts it instead.
    /// </summary>
    public static class GuideMeshBuilder
    {
        private const float FacePlane = 0.1f;   // face in front of the truss side
        private const float Depth = 0.08f;      // panel thickness
        private const int CornerSteps = 5;
        private const float OnTopOverlap = 0.15f; // how far a sign on top of the truss comes down over it
        private const float BracketSize = 0.1f;
        private const float CasterBehind = 0.6f;  // shadow sheet behind the face: more than a shadow map texel at most distances

        /// <summary>A sign's size in metres and where its centre is along the beam; false if its design can't be drawn.</summary>
        public static bool Layout(Gantry g, GuideHead h, out float along, out float width, out float height)
        {
            GuidePlacement p = Place(g, h);
            float span = g.Span;
            along = p.Along;
            width = p.WidthMetres;
            height = p.HeightMetres;
            if (width <= 0f)
            {
                return false;
            }

            float half = width * 0.5f;
            along = span > width ? Mathf.Clamp(p.Along, half, span - half) : span * 0.5f;
            return true;
        }

        /// <summary>
        /// Height of a sign's centre. A sign shares the gantry with signals by standing on top of the truss, clear of them;
        /// with no signals it is mounted on the truss itself.
        /// </summary>
        public static float CentreY(Gantry g, float height)
        {
            bool signals = (g.LaneHeads && g.Lanes.Count > 0) || g.ExtraHeads.Count > 0;
            return signals
                ? g.BeamBottomY + g.BeamDepth - OnTopOverlap + height * 0.5f
                : SignalMeshBuilder.HeadCentreY(g.BeamBottomY, g.BeamDepth, height);
        }

        /// <summary>The height of the middle of a whole sign: signs line up by their panels, with any exit tab standing above.</summary>
        public static float MidY(Gantry g, GuideHead h)
        {
            GuidePlacement p = Place(g, h);
            return CentreY(g, p.PanelMetres) + (p.HeightMetres - p.PanelMetres) * 0.5f;
        }

        /// <summary>A sign's placement; all of a gantry's are remembered until a sign, or the lanes, change. Main thread.</summary>
        public static GuidePlacement Place(Gantry g, GuideHead h)
        {
            lock (GantryManager.Lock)
            {
                bool current = g.GuidePlacements.Count == g.GuideSigns.Count;
                GuidePlacement p;
                for (int i = 0; current && i < g.GuideSigns.Count; i++)
                {
                    GuideHead s = g.GuideSigns[i];
                    current = g.GuidePlacements.TryGetValue(s.Id, out p) && GuideFit.SameFit(p.Source, s);
                }

                if (!current)
                {
                    g.GuidePlacements.Clear();
                    foreach (KeyValuePair<byte, GuidePlacement> kv in GuideFit.Arrange(g.GuideSigns, g.Lanes))
                    {
                        g.GuidePlacements[kv.Key] = kv.Value;
                    }
                }

                if (g.GuidePlacements.TryGetValue(h.Id, out p))
                {
                    return p;
                }

                // A sign that has just been removed, still being looked at.
                return GuideFit.Arrange(new List<GuideHead> { h }, g.Lanes)[h.Id];
            }
        }

        /// <summary>True once every sign on the gantry has its picture; until then the old signs stay up.</summary>
        public static bool Ready(Gantry g)
        {
            bool ready = true;
            foreach (GuideHead h in g.GuideSigns)
            {
                GuideCacheEntry e;
                ready &= GuideCache.TryGet(Place(g, h).Design, out e);
            }

            return ready;
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

            if (g.GuideCasterMesh != null)
            {
                UnityEngine.Object.Destroy(g.GuideCasterMesh);
                g.GuideCasterMesh = null;
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
            var caster = new MeshData();
            foreach (GuideHead h in g.GuideSigns)
            {
                float along, width, height;
                if (!Layout(g, h, out along, out width, out height))
                {
                    continue;
                }

                GuidePlacement placed = Place(g, h);
                GuideCacheEntry e = GuideCache.Get(placed.Design);
                if (e == null)
                {
                    continue;
                }

                Vector3 normal = side * h.Side;
                Vector3 right = Vector3.Cross(Vector3.up, -normal).normalized;
                float midY = MidY(g, h);
                Vector3 centre = SignalMeshBuilder.HeadCentre(g.Start, g.End, g.BeamBottomY, g.BeamDepth, along);
                centre.y = midY;
                centre = centre - g.Origin + normal * (SignalMeshBuilder.HeadDepth * 0.5f + FacePlane);

                float m = GuideFit.MetresPerUnit * placed.Scale;
                var face = new MeshData();
                foreach (List<Vector2> outline in Outlines(e))
                {
                    AddSign(face, back, caster, outline, e, centre, right, normal, m);
                }

                if (g.Look != GantryShape.BoxTruss)
                {
                    AddBrackets(back, g, along, width, dir, normal, midY);
                }

                g.GuideDraws.Add(new GuideDraw { Mesh = face.ToMesh("GantryGuideFace"), Material = e.Material });
            }

            g.GuideBackMesh = back.Verts.Count > 0 ? back.ToMesh("GantryGuideBack") : null;
            g.GuideCasterMesh = caster.Verts.Count > 0 ? caster.ToMesh("GantryGuideCaster") : null;
        }

        /// <summary>
        /// A thin beam doesn't reach a sign's back as a truss does, so two brackets do: out from the beam, then up or down
        /// behind the sign.
        /// </summary>
        private static void AddBrackets(MeshData back, Gantry g, float along, float width, Vector3 dir, Vector3 normal, float signY)
        {
            float beamY = g.BeamBottomY + g.BeamDepth * 0.5f;
            float reach = SignalMeshBuilder.HeadDepth * 0.5f + FacePlane - Depth - BracketSize * 0.5f;
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 beam = SignalMeshBuilder.HeadCentre(g.Start, g.End, g.BeamBottomY, g.BeamDepth, along + i * width * 0.28f) - g.Origin;
                beam.y = beamY - g.Origin.y;
                Vector3 out1 = beam + normal * reach;
                back.Bar(beam, out1, BracketSize, dir);
                back.Bar(out1, new Vector3(out1.x, signY - g.Origin.y, out1.z), BracketSize, dir);
            }
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

        private static void AddSign(MeshData face, MeshData back, MeshData caster, List<Vector2> outline, GuideCacheEntry e, Vector3 centre, Vector3 right, Vector3 normal, float m)
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

            // The shadow sheet faces both ways, so it casts whichever side the sun is on.
            Vector3 toCaster = -normal * CasterBehind;
            caster.Fan(place, outline, mid, normal, t, p => new Vector2(0.5f, 0.5f), toCaster);
            caster.Fan(place, outline, mid, -normal, t, p => new Vector2(0.5f, 0.5f), toCaster);

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

            /// <summary>A square bar between two points, <paramref name="across"/> being square to it.</summary>
            public void Bar(Vector3 a, Vector3 b, float size, Vector3 across)
            {
                Vector3 axis = (b - a).normalized;
                if (axis.sqrMagnitude < 0.5f)
                {
                    return;
                }

                Vector3 u = across.normalized * (size * 0.5f), v = Vector3.Cross(axis, across).normalized * (size * 0.5f);
                Quad(a - u - v, b - u - v, b - u + v, a - u + v, -u);
                Quad(a + u - v, b + u - v, b + u + v, a + u + v, u);
                Quad(a - u + v, b - u + v, b + u + v, a + u + v, v);
                Quad(a - u - v, b - u - v, b + u - v, a + u - v, -v);
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
