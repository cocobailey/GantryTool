using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Generates the signal atlas in code: 8 square tiles side by side, one per <see cref="SignalSymbol"/>
    /// (tile = symbol - 1) plus a plain housing tile. Faces are dot-matrix: a grey frame, a black face and a
    /// grid of round LEDs; symbol LEDs are lit (and flagged as illumination in the ACI map), the rest are
    /// drawn as faint unlit dots.
    /// </summary>
    public static class SignalTextures
    {
        public const int Tile = 256;
        public const int Tiles = 8;
        public const int HousingTile = 6;

        private const int Dots = 18;             // LED grid is Dots x Dots
        private const float FaceMin = 0.08f;     // LED area inside the frame, tile-normalised
        private const float FaceMax = 0.92f;
        private const float FrameWidth = 0.035f; // grey frame around the black face
        private const float DotRadius = 0.36f;   // fraction of dot pitch

        private static readonly Color FrameColor = new Color(0.30f, 0.31f, 0.32f, 1f);
        private static readonly Color FaceColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        private static readonly Color UnlitDot = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color HousingColor = new Color(0.2f, 0.21f, 0.22f, 1f);

        private static readonly Color Green = new Color(0.25f, 1f, 0.4f, 1f);
        private static readonly Color Red = new Color(1f, 0.15f, 0.1f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.82f, 0.1f, 1f);
        private static readonly Color White = new Color(1f, 1f, 1f, 1f);

        /// <summary>Atlas tile for a face symbol (Auto falls back to Blank; never drawn in practice).</summary>
        public static int TileFor(SignalSymbol symbol)
        {
            int s = (int)symbol;
            return s >= 1 && s <= 6 ? s - 1 : (int)SignalSymbol.Blank - 1;
        }

        /// <summary>UV rect of a tile (x, y, width, height).</summary>
        public static Rect TileRect(int tile)
        {
            float w = 1f / Tiles;
            return new Rect(tile * w, 0f, w, 1f);
        }

        public static void Build(out Texture2D main, out Texture2D aci)
        {
            int width = Tile * Tiles;
            var mainPx = new Color[width * Tile];
            var aciPx = new Color[width * Tile];

            for (int tile = 0; tile < Tiles; tile++)
            {
                SignalSymbol symbol = (SignalSymbol)(tile + 1);
                bool isFace = tile < HousingTile;
                Color lit = SymbolColor(symbol);

                // Decide once per LED which ones are lit.
                var on = new bool[Dots, Dots];
                if (isFace)
                {
                    for (int dy = 0; dy < Dots; dy++)
                    {
                        for (int dx = 0; dx < Dots; dx++)
                        {
                            on[dx, dy] = Shape(symbol, (dx + 0.5f) / Dots, (dy + 0.5f) / Dots);
                        }
                    }
                }

                for (int y = 0; y < Tile; y++)
                {
                    for (int x = 0; x < Tile; x++)
                    {
                        int i = y * width + tile * Tile + x;
                        if (!isFace)
                        {
                            mainPx[i] = tile == HousingTile ? HousingColor : FaceColor;
                            aciPx[i] = new Color(0f, 0f, 0f, 1f);
                            continue;
                        }

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
                            float gu = (u - FaceMin) / (FaceMax - FaceMin) * Dots;
                            float gv = (v - FaceMin) / (FaceMax - FaceMin) * Dots;
                            int dx = Mathf.FloorToInt(gu);
                            int dy = Mathf.FloorToInt(gv);
                            if (dx >= 0 && dx < Dots && dy >= 0 && dy < Dots)
                            {
                                float fx = gu - dx - 0.5f;
                                float fy = gv - dy - 0.5f;
                                float dist = Mathf.Sqrt(fx * fx + fy * fy);
                                // Soft circular edge about one texel wide.
                                float texelsPerDot = (float)Tile * (FaceMax - FaceMin) / Dots;
                                float cover = Mathf.Clamp01((DotRadius - dist) * texelsPerDot + 0.5f);
                                if (cover > 0f)
                                {
                                    Color dot = on[dx, dy] ? lit : UnlitDot;
                                    c = Color.Lerp(FaceColor, dot, cover);
                                    if (on[dx, dy])
                                    {
                                        glow = cover;
                                    }
                                }
                            }
                        }

                        mainPx[i] = c;
                        // ACI: R = 1 - alpha (opaque), G = 1 - colour mask (tintable), B = illumination.
                        aciPx[i] = new Color(0f, 0f, glow, 1f);
                    }
                }
            }

            main = new Texture2D(width, Tile, TextureFormat.ARGB32, true, false) { name = "GantrySignalMain" };
            main.SetPixels(mainPx);
            main.wrapMode = TextureWrapMode.Clamp;
            main.filterMode = FilterMode.Trilinear;
            main.anisoLevel = 4;
            main.Apply(true);

            aci = new Texture2D(width, Tile, TextureFormat.ARGB32, true, true) { name = "GantrySignalACI" };
            aci.SetPixels(aciPx);
            aci.wrapMode = TextureWrapMode.Clamp;
            aci.filterMode = FilterMode.Trilinear;
            aci.Apply(true);
        }

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
            float dx = bx - ax;
            float dy = by - ay;
            float t = Mathf.Clamp01(((u - ax) * dx + (v - ay) * dy) / (dx * dx + dy * dy));
            float px = ax + t * dx - u;
            float py = ay + t * dy - v;
            return px * px + py * py <= Stroke * Stroke;
        }
    }
}
