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
        private static readonly Color Amber = new Color(1f, 0.62f, 0.08f, 1f);
        private static readonly Color AmberUnlit = new Color(0.22f, 0.13f, 0.03f, 1f);
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

            _printed = new bool[width * height];
            for (int t = 0; t < atlas.Tiles.Count; t++)
            {
                PaintTile(atlas.Tiles[t], atlas.Style, mainPx, aciPx, width, (t % SignAtlas.Columns) * Tile, (t / SignAtlas.Columns) * Tile);
            }

            main = new Texture2D(width, height, TextureFormat.ARGB32, true, false) { name = "GantrySignalMain" + atlas.Style };
            main.wrapMode = TextureWrapMode.Clamp;
            main.filterMode = FilterMode.Trilinear;
            main.anisoLevel = 4;
            SetBrightMips(main, mainPx, width, height, _printed);

            aci = new Texture2D(width, height, TextureFormat.ARGB32, true, true) { name = "GantrySignalACI" + atlas.Style };
            aci.wrapMode = TextureWrapMode.Clamp;
            aci.filterMode = FilterMode.Trilinear;
            SetBrightMips(aci, aciPx, width, height, _printed);
            _printed = null;

            if (previews != null)
            {
                BuildPreviews(atlas, main, previews);
            }
        }

        // ------------------------------------------------------------------------------ painting

        /// <summary>Paints one tile with its bottom-left corner at pixel (x0, y0).</summary>
        private static void PaintTile(TileSpec spec, SignStyle style, Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            if (spec.Kind == TileKind.Speed && style == SignStyle.Canada)
            {
                PaintPrintedMaximum(spec.Shown, mainPx, aciPx, width, x0, y0);
                return;
            }

            if (spec.Kind == TileKind.Custom)
            {
                CustomSign custom = CustomSigns.Get((ushort)spec.Shown);
                if (custom != null && custom.Kind == CustomKind.Image && !custom.LedLook)
                {
                    PaintImage(custom, mainPx, aciPx, width, x0, y0);
                    return;
                }
            }

            if (spec.Kind == TileKind.RampSign)
            {
                PaintPrintedRamp(mainPx, aciPx, width, x0, y0);
                return;
            }

            if (spec.Kind == TileKind.Lamp)
            {
                PaintLamp(spec.Shown == 1, mainPx, aciPx, width, x0, y0);
                return;
            }

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
            int dots = spec.Kind == TileKind.Lane ? LaneDots : SpeedDots;
            var led = new Color[dots, dots];
            for (int dy = 0; dy < dots; dy++)
            {
                for (int dx = 0; dx < dots; dx++)
                {
                    led[dx, dy] = LedColor(spec, style, (dx + 0.5f) / dots, (dy + 0.5f) / dots, dots);
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
        private static Color LedColor(TileSpec spec, SignStyle style, float u, float v, int dots)
        {
            if (spec.Kind == TileKind.Lane)
            {
                return Shape(spec.Symbol, u, v) ? LaneColor(spec.Symbol, style) : Clear;
            }

            // Text signs are laid out in whole LEDs, counted from the top row.
            int col = Mathf.FloorToInt(u * dots), row = dots - 1 - Mathf.FloorToInt(v * dots);

            if (spec.Kind == TileKind.Speed)
            {
                switch (style)
                {
                    case SignStyle.UnitedStates:
                        return TextSpeed("SPEED", "LIMIT", spec.Shown, col, row, dots) ? White : Clear;
                    default:
                        return SpeedRing(spec.Shown, u, v, 1f / dots);
                }
            }

            if (spec.Kind == TileKind.Pictogram)
            {
                return RegionalPictogram(spec.Symbol, style, u, v, col, row, dots);
            }

            if (spec.Kind == TileKind.Custom)
            {
                return CustomLed(CustomSigns.Get((ushort)spec.Shown), col, row, dots);
            }

            return Clear;
        }

        /// <summary>Lane sign colours; Europe keeps the 1.0 colours exactly.</summary>
        private static Color LaneColor(SignalSymbol s, SignStyle style)
        {
            if (s == SignalSymbol.YellowCross)
            {
                return Yellow;
            }

            if ((s == SignalSymbol.MergeLeft || s == SignalSymbol.MergeRight) && SignStyles.WhiteMergeArrows(style))
            {
                return White;
            }

            return SymbolColor(s);
        }

        // ------------------------------------------------------------------------------ regional variants

        /// <summary>North American variable speed sign: a caption ("SPEED LIMIT" / "MAXIMUM") over the number.</summary>
        private static bool TextSpeed(string line1, string line2, int value, int col, int row, int dots)
        {
            int top = line2 == null ? 6 : 3;
            if (LedFont.Lit(line1, col, row, dots / 2f, top, 1))
            {
                return true;
            }

            top += LedFont.Height + 1;
            if (line2 != null)
            {
                if (LedFont.Lit(line2, col, row, dots / 2f, top, 1))
                {
                    return true;
                }

                top += LedFont.Height;
            }

            // Number at double size in the rest of the box.
            int areaTop = top + 1;
            return LedFont.LitBlock(new[] { value.ToString() }, col, row, dots, areaTop, dots - 2 - areaTop, 2, 0);
        }

        /// <summary>Texels of printed (unlit) sign parts while an atlas is built; their mips are plain averages.</summary>
        private static bool[] _printed;

        // Printed sign parts (not lit): white face, black border and lettering.
        private static readonly Color SignWhite = new Color(0.86f, 0.86f, 0.84f, 1f);
        private static readonly Color SignBlack = new Color(0.04f, 0.04f, 0.04f, 1f);

        // Printed white is retroreflective on real signs: headlights make it read white at night. A little
        // self-light imitates that without making the sign glow like the LEDs.
        private const float PrintedWhiteGlow = 0.32f;

        private const float PrintCell = 1f / 54f;            // printed lettering: one font dot, tile-normalised
        private const int WindowCols = 36, WindowRows = 20;  // LED window in the middle
        private const float WindowLeft = 0.08f, WindowRight = 0.92f, WindowBottom = 0.25f, WindowTop = 0.72f;

        /// <summary>
        /// Canadian (BC) variable speed sign: a printed white sign with "MAXIMUM" and "km/h" in black, and a black LED
        /// window in the middle showing the number in amber. Only the window's LEDs light up at night.
        /// </summary>
        private static void PaintPrintedMaximum(int value, Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            string number = value.ToString();
            float pitchU = (WindowRight - WindowLeft) / WindowCols;
            float pitchV = (WindowTop - WindowBottom) / WindowRows;
            float texelsPerDot = Tile * Mathf.Min(pitchU, pitchV);
            int printCols = Mathf.RoundToInt(1f / PrintCell);

            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float u = (x + 0.5f) / Tile, v = (y + 0.5f) / Tile;
                    Color c;
                    float glow = 0f;
                    bool window = false;

                    if (u < FrameWidth || u > 1f - FrameWidth || v < FrameWidth || v > 1f - FrameWidth)
                    {
                        c = FrameColor;
                    }
                    else if (u < 0.055f || u > 0.945f || v < 0.055f || v > 0.945f)
                    {
                        c = SignBlack; // printed border
                    }
                    else if (u >= WindowLeft && u <= WindowRight && v >= WindowBottom && v <= WindowTop)
                    {
                        window = true;
                        // LED window: round dots, lit amber where the number is.
                        float gu = (u - WindowLeft) / pitchU, gv = (WindowTop - v) / pitchV;
                        int col = Mathf.Min(WindowCols - 1, Mathf.FloorToInt(gu));
                        int row = Mathf.Min(WindowRows - 1, Mathf.FloorToInt(gv));
                        float fx = gu - col - 0.5f, fy = gv - row - 0.5f;
                        float cover = Mathf.Clamp01((DotRadius - Mathf.Sqrt(fx * fx + fy * fy)) * texelsPerDot + 0.5f);
                        bool on = LedFont.Lit(number, col, row, WindowCols / 2f, 3, 2);
                        c = Color.Lerp(FaceColor, on ? Amber : UnlitDot, cover);
                        glow = on ? cover : 0f;
                    }
                    else
                    {
                        // Bold lettering: each font dot is drawn on a half-size grid and grown by half a dot.
                        int col = Mathf.FloorToInt(u / (PrintCell * 0.5f)), row = Mathf.FloorToInt((1f - v) / (PrintCell * 0.5f));
                        bool ink = PrintedInk(col, row, printCols) || PrintedInk(col - 1, row, printCols) ||
                                   PrintedInk(col, row - 1, printCols) || PrintedInk(col - 1, row - 1, printCols);
                        c = ink ? SignBlack : SignWhite;
                        glow = ink ? 0f : PrintedWhiteGlow;
                    }

                    int i = (y0 + y) * width + x0 + x;
                    if (_printed != null && !window)
                    {
                        _printed[i] = true;
                    }

                    mainPx[i] = c;
                    aciPx[i] = new Color(0f, 0f, glow, 1f);
                }
            }
        }

        private static bool PrintedInk(int col, int row, int printCols)
        {
            return LedFont.Lit("MAXIMUM", col, row, printCols, 9, 2) || LedFont.Lit("km/h", col, row, printCols, 86, 2);
        }

        // US warning yellow, printed (not lit); retroreflective like the printed white.
        private static readonly Color SignYellow = new Color(0.98f, 0.78f, 0.06f, 1f);
        private const int RampPrintCols = 60;                // lettering grid across the tile, in font dots
        private static readonly string[] RampLines = { "RAMP", "METERED", "WHEN", "FLASHING" };

        /// <summary>
        /// Ramp metering sign: printed black RAMP / METERED / WHEN / FLASHING on yellow with a black border, like the US
        /// roadside sign. Nothing on it lights up; its two beacons are separate boxes on top of the head.
        /// </summary>
        private static void PaintPrintedRamp(Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            const int gap = 3;
            int blockRows = RampLines.Length * LedFont.Height + (RampLines.Length - 1) * gap;
            int top = (RampPrintCols - blockRows) / 2;
            float half = 0.5f / RampPrintCols;

            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float u = (x + 0.5f) / Tile, v = (y + 0.5f) / Tile;
                    Color c;
                    float glow = 0f;

                    if (u < FrameWidth || u > 1f - FrameWidth || v < FrameWidth || v > 1f - FrameWidth)
                    {
                        c = FrameColor;
                    }
                    else if (u < 0.055f || u > 0.945f || v < 0.055f || v > 0.945f)
                    {
                        c = SignBlack; // printed border
                    }
                    else
                    {
                        // Bold lettering, as on the BC sign: half-size grid, each dot grown by half a dot.
                        int col = Mathf.FloorToInt(u / half), row = Mathf.FloorToInt((1f - v) / half);
                        bool ink = RampInk(col, row, top, gap) || RampInk(col - 1, row, top, gap) ||
                                   RampInk(col, row - 1, top, gap) || RampInk(col - 1, row - 1, top, gap);
                        c = ink ? SignBlack : SignYellow;
                        glow = ink ? 0f : PrintedWhiteGlow;
                    }

                    int i = (y0 + y) * width + x0 + x;
                    if (_printed != null)
                    {
                        _printed[i] = true;
                    }

                    mainPx[i] = c;
                    aciPx[i] = new Color(0f, 0f, glow, 1f);
                }
            }
        }

        /// <summary>Lettering of the ramp sign on the half-dot grid (two cells per font dot).</summary>
        private static bool RampInk(int col, int row, int top, int gap)
        {
            for (int i = 0; i < RampLines.Length; i++)
            {
                if (LedFont.Lit(RampLines[i], col, row, RampPrintCols, (top + i * (LedFont.Height + gap)) * 2, 2))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------------------ custom signs

        private static readonly Color[] TextColours =
        {
            White, Amber, Red, Green, Yellow
        };

        private sealed class DecodedImage
        {
            public byte[] Source;
            public Color[] Pixels;
            public int Width, Height;
        }

        private static readonly Dictionary<ushort, DecodedImage> Decoded = new Dictionary<ushort, DecodedImage>();

        /// <summary>LED colour of a custom text sign, or of an image sign drawn as LED dots.</summary>
        private static Color CustomLed(CustomSign sign, int col, int row, int dots)
        {
            if (sign == null)
            {
                return Clear;
            }

            if (sign.Kind == CustomKind.Text)
            {
                return CustomTextLit(sign, col, row, dots) ? TextColours[Mathf.Min(sign.Colour, TextColours.Length - 1)] : Clear;
            }

            DecodedImage img = Decode(sign);
            if (img == null)
            {
                return Clear;
            }

            // Average the picture over this LED, then light it at full brightness in that colour.
            float x0, y0, w, h;
            FitImage(img, 0f, 1f, out x0, out y0, out w, out h);
            Color sum = new Color(0f, 0f, 0f, 0f);
            for (int sy = 0; sy < 3; sy++)
            {
                for (int sx = 0; sx < 3; sx++)
                {
                    float u = (col + (sx + 0.5f) / 3f) / dots, v = 1f - (row + (sy + 0.5f) / 3f) / dots;
                    sum += Sample(img, (u - x0) / w, (v - y0) / h);
                }
            }

            Color c = sum / 9f;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (c.a < 0.5f || peak < 0.15f)
            {
                return Clear;
            }

            return new Color(c.r / peak, c.g / peak, c.b / peak, 1f);
        }

        /// <summary>Text laid out as large as it fits: double size for very short text, else normal, else tight.</summary>
        private static bool CustomTextLit(CustomSign sign, int col, int row, int dots)
        {
            string[] lines = sign.Line2.Length > 0 ? new[] { sign.Line1, sign.Line2 } : new[] { sign.Line1 };
            int widest = 0;
            foreach (string l in lines)
            {
                widest = Mathf.Max(widest, LedFont.Width(l));
            }

            int room = dots - 2;
            if (widest * 2 + 1 <= room && lines.Length * LedFont.Height * 2 + (lines.Length - 1) * 3 <= room)
            {
                return LedFont.LitBlock(lines, col, row, dots, 0, dots, 2, 3);
            }

            int spacing = widest <= room ? 1 : 0;
            int gap = 4;
            int blockHeight = lines.Length * LedFont.Height + (lines.Length - 1) * gap;
            int top = (dots - blockHeight) / 2;
            for (int i = 0; i < lines.Length; i++)
            {
                if (LedFont.Lit(lines[i], col, row, dots / 2f, top, 1, spacing))
                {
                    return true;
                }

                top += LedFont.Height + gap;
            }

            return false;
        }

        /// <summary>A picture shown as it is on the sign face (letterboxed on black), lit like a screen at night.</summary>
        private static void PaintImage(CustomSign sign, Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            DecodedImage img = Decode(sign);
            float ix, iy, iw, ih;
            if (img != null)
            {
                FitImage(img, FrameWidth, 1f - FrameWidth, out ix, out iy, out iw, out ih);
            }
            else
            {
                ix = iy = 0f;
                iw = ih = 1f;
            }

            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float u = (x + 0.5f) / Tile, v = (y + 0.5f) / Tile;
                    Color c = FaceColor;
                    float glow = 0f;
                    if (u < FrameWidth || u > 1f - FrameWidth || v < FrameWidth || v > 1f - FrameWidth)
                    {
                        c = FrameColor;
                    }
                    else if (img != null)
                    {
                        float fx = (u - ix) / iw, fy = (v - iy) / ih;
                        if (fx >= 0f && fx <= 1f && fy >= 0f && fy <= 1f)
                        {
                            Color p = Sample(img, fx, fy);
                            c = Color.Lerp(FaceColor, new Color(p.r, p.g, p.b, 1f), p.a);
                            glow = p.a * Mathf.Max(p.r, Mathf.Max(p.g, p.b)) * 0.9f;
                        }
                    }

                    int i = (y0 + y) * width + x0 + x;
                    mainPx[i] = c;
                    aciPx[i] = new Color(0f, 0f, glow, 1f);
                    if (_printed != null)
                    {
                        _printed[i] = true; // a picture shrinks like a photo, not like LEDs
                    }
                }
            }
        }

        /// <summary>Where a picture goes inside [lo, hi]², keeping its proportions and centred.</summary>
        private static void FitImage(DecodedImage img, float lo, float hi, out float x0, out float y0, out float w, out float h)
        {
            float size = hi - lo;
            float aspect = (float)img.Width / Mathf.Max(1, img.Height);
            w = aspect >= 1f ? size : size * aspect;
            h = aspect >= 1f ? size / aspect : size;
            x0 = lo + (size - w) * 0.5f;
            y0 = lo + (size - h) * 0.5f;
        }

        /// <summary>Bilinear sample at (fx, fy) in [0, 1] (fy = 0 is the bottom); transparent outside.</summary>
        private static Color Sample(DecodedImage img, float fx, float fy)
        {
            if (fx < 0f || fx > 1f || fy < 0f || fy > 1f)
            {
                return new Color(0f, 0f, 0f, 0f);
            }

            float px = fx * img.Width - 0.5f, py = fy * img.Height - 0.5f;
            int xa = Mathf.Clamp(Mathf.FloorToInt(px), 0, img.Width - 1), ya = Mathf.Clamp(Mathf.FloorToInt(py), 0, img.Height - 1);
            int xb = Mathf.Min(xa + 1, img.Width - 1), yb = Mathf.Min(ya + 1, img.Height - 1);
            float tx = Mathf.Clamp01(px - xa), ty = Mathf.Clamp01(py - ya);
            Color bottom = Color.Lerp(img.Pixels[ya * img.Width + xa], img.Pixels[ya * img.Width + xb], tx);
            Color top = Color.Lerp(img.Pixels[yb * img.Width + xa], img.Pixels[yb * img.Width + xb], tx);
            return Color.Lerp(bottom, top, ty);
        }

        /// <summary>Decodes a custom image once (main thread); null if the data isn't a readable PNG/JPG.</summary>
        private static DecodedImage Decode(CustomSign sign)
        {
            if (sign == null || sign.Image == null)
            {
                return null;
            }

            DecodedImage img;
            if (Decoded.TryGetValue(sign.Id, out img) && ReferenceEquals(img.Source, sign.Image))
            {
                return img;
            }

            img = null;
            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            try
            {
                // Saved pictures are at most 256 px; anything far bigger isn't ours (or is damaged), so skip it.
                if (tex.LoadImage(sign.Image) && tex.width > 0 && tex.height > 0 && tex.width <= 1024 && tex.height <= 1024)
                {
                    img = new DecodedImage { Source = sign.Image, Pixels = tex.GetPixels(), Width = tex.width, Height = tex.height };
                }
                else
                {
                    Log.Warn("Custom sign #" + sign.Id + " (" + sign.FileName + "): image could not be read");
                }
            }
            finally
            {
                Object.Destroy(tex);
            }

            Decoded[sign.Id] = img;
            return img;
        }

        /// <summary>A round amber beacon lens: lit (glowing) or unlit, on a housing-coloured square.</summary>
        private static void PaintLamp(bool lit, Color[] mainPx, Color[] aciPx, int width, int x0, int y0)
        {
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float du = (x + 0.5f) / Tile - 0.5f, dv = (y + 0.5f) / Tile - 0.5f;
                    float r = Mathf.Sqrt(du * du + dv * dv);
                    float cover = Mathf.Clamp01((0.42f - r) * Tile + 0.5f);
                    // Lit lenses are brighter in the middle, like a bulb behind a dome.
                    Color lens = lit ? Color.Lerp(Amber, new Color(1f, 0.9f, 0.6f, 1f), Mathf.Clamp01(1f - r / 0.25f)) : AmberUnlit;
                    int i = (y0 + y) * width + x0 + x;
                    mainPx[i] = Color.Lerp(HousingColor, lens, cover);
                    aciPx[i] = new Color(0f, 0f, lit ? cover : 0f, 1f);
                }
            }
        }

        /// <summary>Warnings and restrictions drawn the way each region does them.</summary>
        private static Color RegionalPictogram(SignalSymbol s, SignStyle style, float u, float v, int col, int row, int dots)
        {
            if (SignCatalog.Find(s) != null && SignCatalog.Find(s).Category == SignCategory.Warning &&
                SignStyles.DiamondWarnings(style))
            {
                // Yellow diamond; the pictogram is shrunk and lifted to fit inside it.
                if (Diamond(u, v))
                {
                    return Yellow;
                }

                const float scale = 0.92f;
                float pu = 0.5f + (u - 0.5f) / scale, pv = 0.4f + (v - 0.5f) / scale;
                Color c = Pictogram(s, pu, pv);
                return c.a > 0f && !Triangle(pu, pv) ? Yellow : Clear;
            }

            switch (s)
            {
                case SignalSymbol.NoOvertaking:
                    if (SignStyles.IsNorthAmerica(style))
                    {
                        return TextPanel(new[] { "DO", "NOT", "PASS" }, col, row, dots) ? White : Clear;
                    }

                    if (style == SignStyle.AustraliaNz)
                    {
                        return TextPanel(new[] { "NO", "OVER-", "TAKING" }, col, row, dots) ? White : Clear;
                    }

                    break;
                case SignalSymbol.NoOvertakingTrucks:
                    if (SignStyles.IsNorthAmerica(style))
                    {
                        return TextPanel(new[] { "TRUCKS", "DO NOT", "PASS" }, col, row, dots) ? White : Clear;
                    }

                    if (style == SignStyle.AustraliaNz)
                    {
                        return TextPanel(new[] { "TRUCKS", "NO", "OVER-", "TAKING" }, col, row, dots) ? White : Clear;
                    }

                    break;
                case SignalSymbol.EndRestrictions:
                    if (style == SignStyle.Netherlands)
                    {
                        return LedFont.LitBlock(new[] { "END" }, col, row, dots, 0, dots, 2, 0) ? White : Clear;
                    }

                    if (style == SignStyle.UnitedKingdom)
                    {
                        return NationalSpeedLimit(u, v) ? White : Clear;
                    }

                    break;
            }

            return Pictogram(s, u, v);
        }

        /// <summary>Regulatory text sign: white lines inside a white border, like North American / Australian signs.</summary>
        private static bool TextPanel(string[] lines, int col, int row, int dots)
        {
            bool border = col == 0 || col == dots - 1 || row == 0 || row == dots - 1;
            int gap = lines.Length > 3 ? 2 : 3;
            return border || LedFont.LitBlock(lines, col, row, dots, 2, dots - 4, 1, gap);
        }

        /// <summary>Yellow warning diamond outline.</summary>
        private static bool Diamond(float u, float v)
        {
            const float half = 0.04f;
            return Segment(u, v, 0.5f, 0.96f, 0.96f, 0.5f, half) || Segment(u, v, 0.96f, 0.5f, 0.5f, 0.04f, half) ||
                   Segment(u, v, 0.5f, 0.04f, 0.04f, 0.5f, half) || Segment(u, v, 0.04f, 0.5f, 0.5f, 0.96f, half);
        }

        /// <summary>UK national speed limit: a white ring with one broad diagonal band.</summary>
        private static bool NationalSpeedLimit(float u, float v)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            float r = Mathf.Sqrt(du * du + dv * dv);
            if (Mathf.Abs(r - 0.445f) <= 0.022f)
            {
                return true;
            }

            return r <= 0.41f && Mathf.Abs((du - dv) * 0.7071f) <= 0.07f;
        }

        // ------------------------------------------------------------------------------ warnings and restrictions

        private const float Line = 0.03f;  // half width of pictogram strokes

        /// <summary>European-style warnings (red triangle) and restrictions (red ring), white pictograms.</summary>
        private static Color Pictogram(SignalSymbol s, float u, float v)
        {
            switch (s)
            {
                case SignalSymbol.Roadworks:
                    return Triangle(u, v) ? Red : Roadworks(u, v) ? White : Clear;
                case SignalSymbol.Queue:
                    return Triangle(u, v) ? Red : CarRear(u, v, 0.5f, 0.16f, 0.38f) || CarRear(u, v, 0.5f, 0.47f, 0.24f) ? White : Clear;
                case SignalSymbol.Accident:
                    return Triangle(u, v) ? Red
                        : CarSide(u, v, 0.34f, 0.21f, 0.32f, 12f) || CarSide(1f - u, v, 0.34f, 0.21f, 0.32f, 12f) ? White : Clear;
                case SignalSymbol.Slippery:
                    return Triangle(u, v) ? Red : CarSide(u, v, 0.5f, 0.4f, 0.4f, 0f) || Skid(u, v, 0.4f) || Skid(u, v, 0.6f) ? White : Clear;
                case SignalSymbol.Fog:
                    return Triangle(u, v) ? Red
                        : Segment(u, v, 0.22f, 0.2f, 0.78f, 0.2f, 0.022f) || Segment(u, v, 0.28f, 0.3f, 0.72f, 0.3f, 0.022f) ||
                          Segment(u, v, 0.33f, 0.4f, 0.67f, 0.4f, 0.022f) || Segment(u, v, 0.38f, 0.5f, 0.62f, 0.5f, 0.022f) ? White : Clear;
                case SignalSymbol.Danger:
                    return Triangle(u, v) ? Red
                        : Segment(u, v, 0.5f, 0.37f, 0.5f, 0.64f, 0.042f) || Disc(u, v, 0.5f, 0.24f, 0.048f) ? White : Clear;
                case SignalSymbol.NoOvertaking:
                    return Ring(u, v) ? Red : CarRear(u, v, 0.35f, 0.35f, 0.26f) ? Red : CarRear(u, v, 0.65f, 0.35f, 0.26f) ? White : Clear;
                case SignalSymbol.NoOvertakingTrucks:
                    return Ring(u, v) ? Red : TruckRear(u, v, 0.34f, 0.3f) ? Red : CarRear(u, v, 0.65f, 0.35f, 0.26f) ? White : Clear;
                case SignalSymbol.EndRestrictions:
                    return EndStripes(u, v) ? White : Clear;
                default:
                    return Clear;
            }
        }

        /// <summary>Red warning triangle outline, point up.</summary>
        private static bool Triangle(float u, float v)
        {
            const float top = 0.94f, bottom = 0.12f, left = 0.04f, right = 0.96f, half = 0.042f;
            return Segment(u, v, left, bottom, right, bottom, half) || Segment(u, v, right, bottom, 0.5f, top, half) ||
                   Segment(u, v, 0.5f, top, left, bottom, half);
        }

        /// <summary>Red prohibition ring (same as the speed limit ring).</summary>
        private static bool Ring(float u, float v)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            return Mathf.Abs(Mathf.Sqrt(du * du + dv * dv) - 0.445f) <= 0.035f;
        }

        private static bool Disc(float u, float v, float cx, float cy, float r)
        {
            return (u - cx) * (u - cx) + (v - cy) * (v - cy) <= r * r;
        }

        private static bool Box(float u, float v, float x0, float y0, float x1, float y1)
        {
            return u >= x0 && u <= x1 && v >= y0 && v <= y1;
        }

        /// <summary>A worker digging, with a heap of earth.</summary>
        private static bool Roadworks(float u, float v)
        {
            return Disc(u, v, 0.43f, 0.6f, 0.045f) ||
                   Segment(u, v, 0.43f, 0.54f, 0.39f, 0.4f, 0.036f) ||                // body
                   Segment(u, v, 0.39f, 0.4f, 0.32f, 0.2f, Line) ||                   // legs
                   Segment(u, v, 0.39f, 0.4f, 0.46f, 0.2f, Line) ||
                   Segment(u, v, 0.42f, 0.5f, 0.57f, 0.3f, 0.024f) ||                 // arms and shovel
                   Segment(u, v, 0.56f, 0.3f, 0.6f, 0.24f, 0.036f) ||
                   (v >= 0.18f && v <= 0.3f - 0.9f * Mathf.Abs(u - 0.67f));           // heap
        }

        /// <summary>A car seen from behind; (cx, bottom) = centre of its wheels' bottom edge, w = width.</summary>
        private static bool CarRear(float u, float v, float cx, float bottom, float w)
        {
            float h = w * 0.5f;
            bool window = Box(u, v, cx - 0.26f * w, bottom + 0.5f * w, cx + 0.26f * w, bottom + 0.66f * w);
            return Box(u, v, cx - h, bottom + 0.14f * w, cx + h, bottom + 0.46f * w) ||                 // body
                   (Box(u, v, cx - 0.38f * w, bottom + 0.46f * w, cx + 0.38f * w, bottom + 0.74f * w) && !window) || // cabin
                   Box(u, v, cx - h, bottom, cx - 0.26f * w, bottom + 0.14f * w) ||                     // wheels
                   Box(u, v, cx + 0.26f * w, bottom, cx + h, bottom + 0.14f * w);
        }

        /// <summary>
        /// A truck seen from behind, laid out in whole LEDs (40-dot grid) so each part keeps its own row: twin wheels,
        /// a wide bumper, then a tall cargo box drawn as an outline with the seam between its two doors.
        /// </summary>
        private static bool TruckRear(float u, float v, float cx, float bottom)
        {
            const float L = 1f / SpeedDots;
            float h = 5.5f * L;
            float y0 = bottom + 4f * L, y1 = y0 + 13f * L;
            bool box = Box(u, v, cx - h, y0, cx + h, y1) && !Box(u, v, cx - h + L, y0 + L, cx + h - L, y1 - L);
            bool doors = Mathf.Abs(u - cx) <= 0.5f * L && v >= y0 && v <= y1;
            bool bumper = Box(u, v, cx - h - L, bottom + 2f * L, cx + h + L, bottom + 3f * L);
            bool wheels = Box(u, v, cx - h, bottom, cx - h + 3f * L, bottom + 2f * L) ||
                          Box(u, v, cx + h - 3f * L, bottom, cx + h, bottom + 2f * L);
            return box || doors || bumper || wheels;
        }

        /// <summary>A car from the side, facing right, tilted by <paramref name="degrees"/> around its centre.</summary>
        private static bool CarSide(float u, float v, float cx, float bottom, float len, float degrees)
        {
            float a = -degrees * Mathf.Deg2Rad;
            float du = u - cx, dv = v - bottom;
            float x = du * Mathf.Cos(a) - dv * Mathf.Sin(a);
            float y = du * Mathf.Sin(a) + dv * Mathf.Cos(a);
            float r = 0.12f * len;
            bool window = Box(x, y, -0.18f * len, 0.4f * len, 0.12f * len, 0.54f * len);
            bool wheel = Disc(x, y, -0.3f * len, r, r) || Disc(x, y, 0.3f * len, r, r);
            return wheel ||
                   Box(x, y, -0.5f * len, 0.14f * len, 0.5f * len, 0.36f * len) ||                      // body
                   (Box(x, y, -0.28f * len, 0.36f * len, 0.2f * len, 0.6f * len) && !window);           // cabin
        }

        /// <summary>A wavy skid mark under the car on the slippery-road sign.</summary>
        private static bool Skid(float u, float v, float cx)
        {
            if (v < 0.17f || v > 0.36f)
            {
                return false;
            }

            float centre = cx + 0.05f * Mathf.Sin((v - 0.17f) / 0.19f * 2f * Mathf.PI);
            return Mathf.Abs(u - centre) <= 0.028f;
        }

        /// <summary>End of all restrictions: a thin ring crossed by diagonal stripes (bottom-left to top-right).</summary>
        private static bool EndStripes(float u, float v)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            float r = Mathf.Sqrt(du * du + dv * dv);
            if (Mathf.Abs(r - 0.445f) <= 0.022f)
            {
                return true;
            }

            if (r > 0.4f)
            {
                return false;
            }

            float d = (du - dv) * 0.7071f;   // distance across the stripes
            for (int k = -2; k <= 2; k++)
            {
                if (Mathf.Abs(d - k * 0.075f) <= 0.019f)
                {
                    return true;
                }
            }

            return false;
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
        private static void SetBrightMips(Texture2D tex, Color[] level0, int width, int height, bool[] printed)
        {
            tex.SetPixels(level0, 0);
            Color[] prev = level0;
            bool[] prevPrinted = printed;
            int pw = width, ph = height;
            for (int level = 1; level < tex.mipmapCount; level++)
            {
                int w = Mathf.Max(1, pw >> 1), h = Mathf.Max(1, ph >> 1);
                var cur = new Color[w * h];
                bool[] curPrinted = prevPrinted != null ? new bool[w * h] : null;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Mathf.Min(x * 2, pw - 1), x1 = Mathf.Min(x * 2 + 1, pw - 1);
                        int y0 = Mathf.Min(y * 2, ph - 1), y1 = Mathf.Min(y * 2 + 1, ph - 1);
                        Color a = prev[y0 * pw + x0], b = prev[y0 * pw + x1], c = prev[y1 * pw + x0], d = prev[y1 * pw + x1];
                        Color avg = (a + b + c + d) * 0.25f;
                        Color max = Brightest(Brightest(a, b), Brightest(c, d));
                        // Printed sign parts average normally, or white paper would swallow thin black lettering.
                        bool flat = prevPrinted != null && (prevPrinted[y0 * pw + x0] || prevPrinted[y0 * pw + x1] ||
                                                            prevPrinted[y1 * pw + x0] || prevPrinted[y1 * pw + x1]);
                        if (curPrinted != null)
                        {
                            curPrinted[y * w + x] = flat;
                        }

                        cur[y * w + x] = flat ? avg : Color.Lerp(avg, max, MipBrightBias);
                    }
                }

                tex.SetPixels(cur, level);
                prev = cur;
                prevPrinted = curPrinted;
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
                case SignalSymbol.YellowCross:
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
