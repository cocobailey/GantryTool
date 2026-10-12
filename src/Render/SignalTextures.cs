using System.Collections.Generic;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Generates a style's sign atlas in code (layout in <see cref="SignAtlas"/>). Faces are dot-matrix: a grey frame,
    /// a black face and a grid of round LEDs; lit LEDs are flagged as illumination in the ACI map, the rest are
    /// drawn as faint unlit dots. Lane signs use an 18x18 grid (as in 1.0); speed limits a finer one so numbers
    /// stay readable inside the ring.
    /// </summary>
    public static class SignalTextures
    {
        private const int Tile = SignAtlas.Tile;

        private const int LaneDots = 18;         // 1.0 grid, unchanged
        private const int SpeedDots = 40;
        private const float FaceMin = 0.08f;     // LED area inside the frame, tile-normalised
        private const float FaceMax = 0.92f;
        private const float FrameWidth = 0.035f; // grey frame around the black face
        private const float DotRadius = 0.4f;    // fraction of dot pitch

        // Mip levels are built here instead of by Unity: plain averaging mixes the lit dots with the black gaps,
        // so distant signs fade to a fraction of their brightness. Leaning towards the brightest texel keeps
        // distant symbols bright and solid, like real LED signs seen from afar.
        private const float MipBrightBias = 0.65f;

        private static readonly Color FrameColor = new Color(0.30f, 0.31f, 0.32f, 1f);
        private static readonly Color FaceColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        private static readonly Color UnlitDot = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color HousingColor = new Color(0.2f, 0.21f, 0.22f, 1f);

        private static readonly Color Green = new Color(0.25f, 1f, 0.4f, 1f);
        private static readonly Color Red = new Color(1f, 0.15f, 0.1f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.82f, 0.1f, 1f);
        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f); // "not lit" marker in LED colour grids

        private const int PreviewMip = 2; // 256 px tiles -> 64 px previews

        /// <summary>
        /// Builds a style's atlas, plus a 64 px preview per face tile (keyed by tile index) for the signal panel.
        /// </summary>
        public static void Build(SignAtlas atlas, out Texture2D main, out Texture2D aci, Dictionary<int, Texture2D> previews)
        {
            int width = atlas.Width;
            int height = atlas.Height;
            var mainPx = new Color[width * height];
            var aciPx = new Color[width * height];
            for (int i = 0; i < mainPx.Length; i++)
            {
                mainPx[i] = FaceColor;
                aciPx[i] = new Color(0f, 0f, 0f, 1f);
            }

            for (int t = 0; t < atlas.Tiles.Count; t++)
            {
                PaintTile(atlas.Tiles[t], mainPx, aciPx, width, (t % SignAtlas.Columns) * Tile, (t / SignAtlas.Columns) * Tile);
            }

            main = new Texture2D(width, height, TextureFormat.ARGB32, true, false) { name = "GantrySignalMain" + atlas.Style };
            main.wrapMode = TextureWrapMode.Clamp;
            main.filterMode = FilterMode.Trilinear;
            main.anisoLevel = 4;
            SetBrightMips(main, mainPx, width, height);

            aci = new Texture2D(width, height, TextureFormat.ARGB32, true, true) { name = "GantrySignalACI" + atlas.Style };
            aci.wrapMode = TextureWrapMode.Clamp;
            aci.filterMode = FilterMode.Trilinear;
            SetBrightMips(aci, aciPx, width, height);

            if (previews != null)
            {
                BuildPreviews(atlas, main, previews);
            }
        }

        // ------------------------------------------------------------------------------ painting

        /// <summary>Paints one tile with its bottom-left corner at pixel (x0, y0).</summary>
        private static void PaintTile(TileSpec spec, Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            if (spec.Kind == TileKind.Housing || spec.Kind == TileKind.Empty)
            {
                Color fill = spec.Kind == TileKind.Housing ? HousingColor : FaceColor;
                for (int y = 0; y < Tile; y++)
                {
                    for (int x = 0; x < Tile; x++)
                    {
                        mainPx[(y0 + y) * width + x0 + x] = fill;
                    }
                }

                return;
            }

            // Decide once per LED which ones are lit and in what colour.
            int dots = spec.Kind == TileKind.Speed ? SpeedDots : LaneDots;
            var led = new Color[dots, dots];
            for (int dy = 0; dy < dots; dy++)
            {
                for (int dx = 0; dx < dots; dx++)
                {
                    led[dx, dy] = LedColor(spec, (dx + 0.5f) / dots, (dy + 0.5f) / dots, dots);
                }
            }

            float texelsPerDot = (float)Tile * (FaceMax - FaceMin) / dots;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float u = (x + 0.5f) / Tile;
                    float v = (y + 0.5f) / Tile;
                    Color c;
                    float glow = 0f;

                    if (u < FrameWidth || u > 1f - FrameWidth || v < FrameWidth || v > 1f - FrameWidth)
                    {
                        c = FrameColor;
                    }
                    else
                    {
                        c = FaceColor;
                        float gu = (u - FaceMin) / (FaceMax - FaceMin) * dots;
                        float gv = (v - FaceMin) / (FaceMax - FaceMin) * dots;
                        int dx = Mathf.FloorToInt(gu);
                        int dy = Mathf.FloorToInt(gv);
                        if (dx >= 0 && dx < dots && dy >= 0 && dy < dots)
                        {
                            float fx = gu - dx - 0.5f;
                            float fy = gv - dy - 0.5f;
                            float dist = Mathf.Sqrt(fx * fx + fy * fy);
                            // Soft circular edge about one texel wide.
                            float cover = Mathf.Clamp01((DotRadius - dist) * texelsPerDot + 0.5f);
                            if (cover > 0f)
                            {
                                bool on = led[dx, dy].a > 0f;
                                Color dot = on ? led[dx, dy] : UnlitDot;
                                c = Color.Lerp(FaceColor, dot, cover);
                                if (on)
                                {
                                    glow = cover;
                                }
                            }
                        }
                    }

                    int i = (y0 + y) * width + x0 + x;
                    mainPx[i] = c;
                    // ACI: R = 1 - alpha (opaque), G = 1 - colour mask (tintable), B = illumination.
                    aciPx[i] = new Color(0f, 0f, glow, 1f);
                }
            }
        }

        /// <summary>Colour of the LED at grid-normalised (u, v) (v = 0 is the bottom row), or Clear when unlit.</summary>
        private static Color LedColor(TileSpec spec, float u, float v, int dots)
        {
            if (spec.Kind == TileKind.Lane)
            {
                return Shape(spec.Symbol, u, v) ? SymbolColor(spec.Symbol) : Clear;
            }

            if (spec.Kind == TileKind.Speed)
            {
                return SpeedRing(spec.Shown, u, v, 1f / dots);
            }

            return Clear;
        }

        // ------------------------------------------------------------------------------ speed limits

        /// <summary>European-style speed limit: red ring, white digits.</summary>
        private static Color SpeedRing(int value, float u, float v, float pitch)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            float r = Mathf.Sqrt(du * du + dv * dv);
            if (Mathf.Abs(r - 0.445f) <= 0.035f)
            {
                return Red;
            }

            return Digits(value.ToString(), u, v, 0.5f, 0.5f, 0.6f, 0.42f, pitch) ? White : Clear;
        }

        // Block digits on a 4 x 6 grid, as polylines (x, y pairs; a NaN pair starts a new stroke).
        private static readonly float[][] DigitStrokes =
        {
            new float[] { 0, 0, 4, 0, 4, 6, 0, 6, 0, 0 },                                  // 0
            new float[] { 2, 0, 2, 6, float.NaN, float.NaN, 0.8f, 4.8f, 2, 6 },            // 1
            new float[] { 0, 6, 4, 6, 4, 3, 0, 3, 0, 0, 4, 0 },                            // 2
            new float[] { 0, 6, 4, 6, 4, 0, 0, 0, float.NaN, float.NaN, 1, 3, 4, 3 },      // 3
            new float[] { 0, 6, 0, 2.5f, 4, 2.5f, float.NaN, float.NaN, 3, 4.5f, 3, 0 },   // 4
            new float[] { 4, 6, 0, 6, 0, 3, 4, 3, 4, 0, 0, 0 },                            // 5
            new float[] { 4, 6, 0, 6, 0, 0, 4, 0, 4, 3, 0, 3 },                            // 6
            new float[] { 0, 6, 4, 6, 1.6f, 0 },                                           // 7
            new float[] { 0, 0, 4, 0, 4, 6, 0, 6, 0, 0, float.NaN, float.NaN, 0, 3, 4, 3 }, // 8
            new float[] { 4, 3, 0, 3, 0, 6, 4, 6, 4, 0, 0, 0 }                             // 9
        };

        /// <summary>
        /// Whether (u, v) lies on <paramref name="text"/> drawn in block digits centred at (cx, cy) and fitting
        /// maxW x maxH. Strokes are at least about one LED wide so small numbers don't break up.
        /// </summary>
        private static bool Digits(string text, float u, float v, float cx, float cy, float maxW, float maxH, float pitch)
        {
            // Three-digit numbers use narrower digits so "100"/"130" fit the ring like on real signs.
            bool narrow = text.Length >= 3;
            float digitW = narrow ? 3.2f : 4f, gap = narrow ? 1.2f : 1.6f;
            const float digitH = 6f;
            float squeeze = digitW / 4f;   // glyphs are drawn on a 4-unit-wide grid
            float totalW = text.Length * digitW + (text.Length - 1) * gap;
            float scale = Mathf.Min(maxW / totalW, maxH / digitH);
            float half = Mathf.Max(0.34f * scale, 0.55f * pitch);
            float left = cx - totalW * scale * 0.5f;
            float bottom = cy - digitH * scale * 0.5f;

            for (int c = 0; c < text.Length; c++)
            {
                int d = text[c] - '0';
                if (d < 0 || d > 9)
                {
                    continue;
                }

                float ox = left + c * (digitW + gap) * scale;
                float[] pts = DigitStrokes[d];
                for (int k = 0; k + 3 < pts.Length; k += 2)
                {
                    if (float.IsNaN(pts[k]) || float.IsNaN(pts[k + 2]))
                    {
                        continue;
                    }

                    if (Segment(u, v, ox + pts[k] * squeeze * scale, bottom + pts[k + 1] * scale,
                            ox + pts[k + 2] * squeeze * scale, bottom + pts[k + 3] * scale, half))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------------------------ previews and mips

        /// <summary>Cuts each face tile out of a small mip level (same bright mips the game sees at a distance).</summary>
        private static void BuildPreviews(SignAtlas atlas, Texture2D main, Dictionary<int, Texture2D> previews)
        {
            Color[] level = main.GetPixels(PreviewMip);
            int w = Mathf.Max(1, main.width >> PreviewMip);
            int tile = Mathf.Max(1, Tile >> PreviewMip);
            for (int t = 0; t < atlas.Tiles.Count; t++)
            {
                TileKind kind = atlas.Tiles[t].Kind;
                if (kind == TileKind.Housing || kind == TileKind.Empty)
                {
                    continue;
                }

                int x0 = (t % SignAtlas.Columns) * tile;
                int y0 = (t / SignAtlas.Columns) * tile;
                var px = new Color[tile * tile];
                for (int y = 0; y < tile; y++)
                {
                    for (int x = 0; x < tile; x++)
                    {
                        px[y * tile + x] = level[(y0 + y) * w + x0 + x];
                    }
                }

                var tex = new Texture2D(tile, tile, TextureFormat.ARGB32, false, false) { name = "GantrySignPreview" + t };
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.SetPixels(px);
                tex.Apply(false);
                previews[t] = tex;
            }
        }

        /// <summary>Fills every mip level, each texel a blend of its 2x2 parent block's average and brightest texel.</summary>
        private static void SetBrightMips(Texture2D tex, Color[] level0, int width, int height)
        {
            tex.SetPixels(level0, 0);
            Color[] prev = level0;
            int pw = width, ph = height;
            for (int level = 1; level < tex.mipmapCount; level++)
            {
                int w = Mathf.Max(1, pw >> 1), h = Mathf.Max(1, ph >> 1);
                var cur = new Color[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Mathf.Min(x * 2, pw - 1), x1 = Mathf.Min(x * 2 + 1, pw - 1);
                        int y0 = Mathf.Min(y * 2, ph - 1), y1 = Mathf.Min(y * 2 + 1, ph - 1);
                        Color a = prev[y0 * pw + x0], b = prev[y0 * pw + x1], c = prev[y1 * pw + x0], d = prev[y1 * pw + x1];
                        Color avg = (a + b + c + d) * 0.25f;
                        Color max = Brightest(Brightest(a, b), Brightest(c, d));
                        cur[y * w + x] = Color.Lerp(avg, max, MipBrightBias);
                    }
                }

                tex.SetPixels(cur, level);
                prev = cur;
                pw = w;
                ph = h;
            }

            tex.Apply(false);
        }

        private static Color Brightest(Color a, Color b)
        {
            return a.r + a.g + a.b >= b.r + b.g + b.b ? a : b;
        }

        // ------------------------------------------------------------------------------ 1.0 lane signs (unchanged)

        private static Color SymbolColor(SignalSymbol s)
        {
            switch (s)
            {
                case SignalSymbol.Arrow: return Green;
                case SignalSymbol.Cross: return Red;
                case SignalSymbol.MergeLeft:
                case SignalSymbol.MergeRight: return Yellow;
                case SignalSymbol.Reserved: return White;
                default: return UnlitDot;
            }
        }

        /// <summary>Whether the LED at grid-normalised (u, v) is lit; v = 0 is the bottom row.</summary>
        private static bool Shape(SignalSymbol s, float u, float v)
        {
            switch (s)
            {
                case SignalSymbol.Arrow:
                    return DownArrow(u, v);
                case SignalSymbol.Cross:
                {
                    if (u < 0.08f || u > 0.92f || v < 0.08f || v > 0.92f)
                    {
                        return false;
                    }

                    const float half = 0.075f;
                    return Mathf.Abs(u - v) * 0.7071f <= half || Mathf.Abs(u + v - 1f) * 0.7071f <= half;
                }
                case SignalSymbol.MergeLeft:
                    return MergeLeftArrow(u, v);
                case SignalSymbol.MergeRight:
                    return MergeLeftArrow(1f - u, v); // mirror image
                case SignalSymbol.Reserved:
                    // Tall, narrow diamond outline like real reserved-lane signs.
                    return Stroke1(u, v, 0.5f, 0.92f, 0.76f, 0.5f) || Stroke1(u, v, 0.76f, 0.5f, 0.5f, 0.08f) ||
                           Stroke1(u, v, 0.5f, 0.08f, 0.24f, 0.5f) || Stroke1(u, v, 0.24f, 0.5f, 0.5f, 0.92f);
                default:
                    return false;
            }
        }

        private const float Stroke = 0.075f; // half stroke width for line-drawn symbols

        /// <summary>Down arrow drawn with strokes like real LCS signals: a shaft and a V-shaped (open) head.</summary>
        private static bool DownArrow(float u, float v)
        {
            return Stroke1(u, v, 0.5f, 0.12f, 0.5f, 0.9f) ||
                   Stroke1(u, v, 0.5f, 0.12f, 0.18f, 0.44f) ||
                   Stroke1(u, v, 0.5f, 0.12f, 0.82f, 0.44f);
        }

        /// <summary>
        /// Diagonal arrow pointing down-left (as seen by the viewer; the face mesh maps u to the viewer's
        /// right): a diagonal shaft with an open L-shaped head in the bottom-left corner.
        /// </summary>
        private static bool MergeLeftArrow(float u, float v)
        {
            return Stroke1(u, v, 0.17f, 0.17f, 0.85f, 0.85f) ||
                   Stroke1(u, v, 0.17f, 0.17f, 0.62f, 0.17f) ||
                   Stroke1(u, v, 0.17f, 0.17f, 0.17f, 0.62f);
        }

        /// <summary>True if (u, v) lies within <see cref="Stroke"/> of the line segment a-b.</summary>
        private static bool Stroke1(float u, float v, float ax, float ay, float bx, float by)
        {
            return Segment(u, v, ax, ay, bx, by, Stroke);
        }

        /// <summary>True if (u, v) lies within <paramref name="half"/> of the line segment a-b.</summary>
        private static bool Segment(float u, float v, float ax, float ay, float bx, float by, float half)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 > 0f ? Mathf.Clamp01(((u - ax) * dx + (v - ay) * dy) / len2) : 0f;
            float px = ax + t * dx - u;
            float py = ay + t * dy - v;
            return px * px + py * py <= half * half;
        }
    }
}
