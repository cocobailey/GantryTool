using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>A rendered guide sign: pixels (top row first, transparent outside the sign) and its size in sign units.</summary>
    public sealed class GuideSign
    {
        public int PixelWidth, PixelHeight;
        public Color32[] Pixels;
        public float Width, Height;   // theme units, roughly inches of a full-size sign
        public float TabHeight;       // how much of the top is the tab row (the panel starts below it)
        public float Radius;          // corner radius
        public bool SquareLeft, SquareRight;   // the panel's top corner under a tab at that edge is square
        public List<GuideTab> Tabs = new List<GuideTab>();
        public List<GuidePart> Parts = new List<GuidePart>();
    }

    /// <summary>A tab on top of a sign, in sign units.</summary>
    /// <summary>Where one part of a design was drawn, so an editor can tell what was clicked. Sign units, y down.</summary>
    public struct GuidePart
    {
        public string Path;   // indices through "items" from the design's top, like "2/0"
        public string Type;
        public float X, Y, W, H;
    }

    public struct GuideTab
    {
        public float X, Width, Height;
    }

    /// <summary>
    /// Lays out and draws a sign design in plain managed code. The design format, layout rules and theme data follow
    /// Sign Builder for Cities: Skylines II by Andy Smith (MIT, see THIRD-PARTY-NOTICES.md): parts are text, shields,
    /// arrows, dividers, spacers, rows, columns, chips, full-width panels and tabs, sized in theme units.
    /// </summary>
    public static class GuideRenderer
    {
        [ThreadStatic]
        private static List<KeyValuePair<Dictionary<string, object>, Node>> _tracked;   // signs are also drawn on a worker thread

        private static List<KeyValuePair<Dictionary<string, object>, Node>> Tracked
        {
            get { return _tracked ?? (_tracked = new List<KeyValuePair<Dictionary<string, object>, Node>>()); }
        }

        private const string DefaultArrow = "M32 0 H68 V88 H100 L50 150 L0 88 H32 Z";

        private sealed class Ctx
        {
            public string Font;
            public Color32 Fg, Bg;
        }

        private sealed class Theme
        {
            public string Id;
            public Dictionary<string, object> Def, Defaults;
            public float TextHeight, ShieldHeight, ArrowHeight, Border, CornerRadius, LineGap, Margin, ItemGap;
            public string DefaultFont, DefaultScheme;

            public Theme(string id)
            {
                Id = id;
                Def = GuideAssets.Theme(id) ?? new Dictionary<string, object>();
                Defaults = Json.Obj(Def, "defaults");
                TextHeight = Json.Num(Defaults, "textHeight", 16f);
                ShieldHeight = Json.Num(Defaults, "shieldHeight", 36f);
                ArrowHeight = Json.Num(Defaults, "arrowHeight", 24f);
                Border = Json.Num(Defaults, "border", 2f);
                CornerRadius = Json.Num(Defaults, "cornerRadius", 12f);
                LineGap = Json.Num(Defaults, "lineGap", 0.75f);
                Margin = Json.Num(Defaults, "margin", 0.75f);
                ItemGap = Json.Num(Defaults, "itemGap", 0.6f);
                DefaultFont = Json.Str(Defaults, "font", "RG2014EM");
                DefaultScheme = Json.Str(Defaults, "scheme", "green");
            }

            public void Scheme(string name, out Color32 bg, out Color32 fg)
            {
                Dictionary<string, object> schemes = Json.Obj(Def, "schemes");
                Dictionary<string, object> s = Json.Obj(schemes, name ?? DefaultScheme) ?? Json.Obj(schemes, DefaultScheme);
                bg = ParseColour(Json.Str(s, "bg"), new Color32(0, 107, 69, 255));
                fg = ParseColour(Json.Str(s, "fg"), new Color32(255, 255, 255, 255));
            }

            public GlyphFont Font(string family)
            {
                return GuideAssets.Font(Id, family) ?? GuideAssets.Font(Id, DefaultFont);
            }

            /// <summary>Font size per letter height, from the font's cap height.</summary>
            public float FontScale(string family)
            {
                List<object> fonts = Json.Arr(Def, "fonts");
                if (fonts != null)
                {
                    foreach (object o in fonts)
                    {
                        var f = o as Dictionary<string, object>;
                        if (Json.Str(f, "family") == family)
                        {
                            return Json.Num(f, "scale", 1.75f);
                        }
                    }
                }

                return 1.75f;
            }
        }

        private static Color32 ParseColour(string s, Color32 fallback)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '#')
            {
                return fallback;
            }

            int v;
            if (s.Length == 7 && int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
            {
                return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            }

            if (s.Length == 4 && int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
            {
                return new Color32((byte)(((v >> 8) & 15) * 17), (byte)(((v >> 4) & 15) * 17), (byte)((v & 15) * 17), 255);
            }

            return fallback;
        }

        private static List<Dictionary<string, object>> Children(Dictionary<string, object> item)
        {
            var list = new List<Dictionary<string, object>>();
            List<object> items = Json.Arr(item, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    var d = o as Dictionary<string, object>;
                    if (d != null)
                    {
                        list.Add(d);
                    }
                }
            }

            return list;
        }

        private static float SpacingHeight(Theme t, Dictionary<string, object> item)
        {
            switch (Json.Str(item, "type"))
            {
                case "text": return Json.Num(item, "height", t.TextHeight);
                case "shield": return Mathf.Min(Json.Num(item, "height", t.ShieldHeight) * 0.45f, t.TextHeight);
                case "arrow":
                case "laneArrows": return Mathf.Min(Json.Num(item, "height", t.ArrowHeight) * 0.6f, t.TextHeight);
                case "row":
                case "column":
                case "panel":
                case "chip":
                case "tab":
                    float h = 0f;
                    foreach (Dictionary<string, object> c in Children(item))
                    {
                        h = Mathf.Max(h, SpacingHeight(t, c));
                    }

                    return h;
                default: return 0f;
            }
        }

        private static float NominalHeight(Theme t, Dictionary<string, object> item)
        {
            switch (Json.Str(item, "type"))
            {
                case "text": return Json.Num(item, "height", t.TextHeight);
                case "shield": return Json.Num(item, "height", t.ShieldHeight);
                case "arrow":
                case "laneArrows": return Json.Num(item, "height", t.ArrowHeight);
                case "symbol": return Json.Num(item, "height", t.ShieldHeight);
                case "spacer": return Json.Num(item, "height", 0f);
                case "row":
                case "column":
                case "panel":
                case "chip":
                case "tab":
                    float h = 0f;
                    foreach (Dictionary<string, object> c in Children(item))
                    {
                        h = Mathf.Max(h, NominalHeight(t, c));
                    }

                    return h;
                default: return 0f;
            }
        }

        // ------------------------------------------------------------------------------ layout tree

        private abstract class Node
        {
            public float W, H;           // natural size, sign units
            public float X, Y, AW, AH;   // arranged box
            public float MarginTop, MarginRight, MarginBottom, MarginLeft;
            public bool Fill;            // takes the column's width
            public bool Grow;            // takes the width left over in a row
            public bool Divider;
            public string Place;

            public virtual void Arrange(float x, float y, float w, float h)
            {
                X = x;
                Y = y;
                AW = w;
                AH = h;
            }

            public abstract void Draw(SignCanvas c, float ppi);
        }

        private sealed class TextNode : Node
        {
            public string Text;
            public GlyphFont Font;
            public float Em, Tracking;
            public Color32 Colour;

            public override void Draw(SignCanvas c, float ppi)
            {
                if (Font == null)
                {
                    return;
                }

                float s = Em / Font.Em;
                float pen = X;
                float baseline = Y + H; // capitals fill the box: the baseline is its bottom edge
                for (int i = 0; i < Text.Length; i++)
                {
                    Glyph g = Font.Get(Text[i]);
                    if (g == null)
                    {
                        continue;
                    }

                    if (g.Width > 0)
                    {
                        float x0 = pen + g.Left * s, y0 = baseline - g.Top * s;
                        c.DrawCoverage(g.Coverage, g.Width, g.Height, x0 * ppi, y0 * ppi, (x0 + g.Width * s) * ppi, (y0 + g.Height * s) * ppi, Colour);
                    }

                    pen += g.Advance * s + Tracking;
                    if (i + 1 < Text.Length)
                    {
                        pen += Font.Kern(Text[i], Text[i + 1]) * s;
                    }
                }
            }
        }

        private sealed class ShieldNode : Node
        {
            public string Shape;
            public GuideImage Image;
            public Color32 Bg, BorderColour;
            public bool HasBorder;
            public float Radius, BorderWidth;
            public TextNode Number;
            public float NumberTop;   // from the shield's top (image shields) or centred (plates)

            public override void Arrange(float x, float y, float w, float h)
            {
                base.Arrange(x, y, W, H);
                if (Number != null)
                {
                    Number.Arrange(x + (W - Number.W) * 0.5f, y + NumberTop, Number.W, Number.H);
                }
            }

            public override void Draw(SignCanvas c, float ppi)
            {
                float x0 = X * ppi, y0 = Y * ppi, x1 = (X + W) * ppi, y1 = (Y + H) * ppi;
                if (Shape == "img" && Image != null)
                {
                    c.DrawImage(Image.Pixels, Image.Width, Image.Height, x0, y0, x1, y1);
                }
                else if (Shape == "ellipse")
                {
                    c.FillEllipse(x0, y0, x1, y1, Bg);
                    if (HasBorder)
                    {
                        float inset = H * 0.06f * ppi, ring = H * 0.02f * ppi;
                        c.FillEllipse(x0 + inset, y0 + inset, x1 - inset, y1 - inset, BorderColour);
                        c.FillEllipse(x0 + inset + ring, y0 + inset + ring, x1 - inset - ring, y1 - inset - ring, Bg);
                    }
                }
                else if (Shape == "rect")
                {
                    float r = Radius * ppi;
                    if (HasBorder)
                    {
                        c.FillRoundRect(x0, y0, x1, y1, r, r, r, r, BorderColour);
                        float b = BorderWidth * ppi;
                        float ir = Mathf.Max(0f, r - b);
                        c.FillRoundRect(x0 + b, y0 + b, x1 - b, y1 - b, ir, ir, ir, ir, Bg);
                    }
                    else
                    {
                        c.FillRoundRect(x0, y0, x1, y1, r, r, r, r, Bg);
                    }
                }

                if (Number != null)
                {
                    Number.Draw(c, ppi);
                }
            }
        }

        private sealed class ArrowNode : Node
        {
            public List<Vector2> Points; // already fitted to (0, 0)-(W, H)
            public Color32 Colour;

            public override void Draw(SignCanvas c, float ppi)
            {
                var pts = new List<Vector2>(Points.Count);
                foreach (Vector2 p in Points)
                {
                    pts.Add(new Vector2((X + p.x) * ppi, (Y + p.y) * ppi));
                }

                c.FillPolygon(pts, Colour);
            }
        }

        /// <summary>Arrows at set distances from the sign's centre, over the lanes they stand for; takes the full width.</summary>
        private sealed class LaneArrowsNode : Node
        {
            public readonly List<Node> Arrows = new List<Node>();
            public readonly List<float> At = new List<float>();
            public readonly List<ExitSection> Sections = new List<ExitSection>();

            public override void Arrange(float x, float y, float w, float h)
            {
                base.Arrange(x, y, w, h);
                float mid = x + w * 0.5f;
                for (int i = 0; i < Arrows.Count; i++)
                {
                    Node a = Arrows[i];
                    float left = Mathf.Clamp(mid + At[i] - a.W * 0.5f, x, Mathf.Max(x, x + w - a.W));
                    a.Arrange(left, y + (H - a.H) * 0.5f, a.W, a.H);
                }

                foreach (ExitSection s in Sections)
                {
                    s.Arrange(mid, x, x + w, y, H, Arrows);
                }
            }

            public override void Draw(SignCanvas c, float ppi)
            {
                foreach (ExitSection s in Sections)
                {
                    s.DrawPlate(c, ppi);
                }

                foreach (Node a in Arrows)
                {
                    a.Draw(c, ppi);
                }

                foreach (ExitSection s in Sections)
                {
                    s.DrawText(c, ppi);
                }
            }
        }

        /// <summary>
        /// A coloured section under neighbouring exit only lanes, as wide as those lanes, its wording in the widest space
        /// the arrows leave.
        /// </summary>
        private sealed class ExitSection
        {
            private const float TextHeight = 12f;
            private const float MinTextHeight = 7f;
            private const float Inset = 3f;

            public Theme Theme;
            public string Wording, Font;
            public Color32 Bg, Fg;
            public int First, Last;
            public float Left, Right;   // from the sign's centre
            private float _x0, _x1, _y, _h;
            private TextNode _text;

            public void Arrange(float mid, float minX, float maxX, float y, float h, List<Node> arrows)
            {
                _x0 = Mathf.Max(minX, mid + Left + Inset);
                _x1 = Mathf.Min(maxX, mid + Right - Inset);
                _y = y;
                _h = h;

                // The widest space between this section's arrows.
                float best = -1f, from = _x0, gapStart = _x0, gapEnd = _x1;
                for (int i = First; i <= Last + 1; i++)
                {
                    bool end = i > Last;
                    Node a = end ? null : arrows[i];
                    if (a is BlockNode)
                    {
                        continue; // a lane with no arrow leaves its space free
                    }

                    float to = end ? _x1 : a.X;
                    if (to - from > best)
                    {
                        best = to - from;
                        gapStart = from;
                        gapEnd = to;
                    }

                    if (!end)
                    {
                        from = a.X + a.W;
                    }
                }

                _text = null;
                if (string.IsNullOrEmpty(Wording) || best <= 0f)
                {
                    return;
                }

                const float Margin = 3f;
                TextNode text = BuildText(Theme, Wording, TextHeight, Font, Fg, 0f);
                float room = best - 2f * Margin;
                if (text.W > room && text.W > 0f)
                {
                    text = BuildText(Theme, Wording, Mathf.Max(MinTextHeight, TextHeight * room / text.W), Font, Fg, 0f);
                }

                text.Arrange((gapStart + gapEnd - text.W) * 0.5f, y + (h - text.H) * 0.5f, text.W, text.H);
                _text = text;
            }

            public void DrawPlate(SignCanvas c, float ppi)
            {
                float r = 2f * ppi;
                c.FillRoundRect(_x0 * ppi, _y * ppi, _x1 * ppi, (_y + _h) * ppi, r, r, r, r, Bg);
            }

            public void DrawText(SignCanvas c, float ppi)
            {
                if (_text != null)
                {
                    _text.Draw(c, ppi);
                }
            }
        }

        private sealed class BlockNode : Node
        {
            public bool Painted;
            public Color32 Colour;

            public override void Draw(SignCanvas c, float ppi)
            {
                if (Painted)
                {
                    c.FillRect(X * ppi, Y * ppi, (X + AW) * ppi, (Y + AH) * ppi, Colour);
                }
            }
        }

        private sealed class StackNode : Node
        {
            public bool Horizontal;
            public string Align;     // cross axis
            public string Justify;   // between, even
            public string Main = "start";
            public readonly List<Node> Items = new List<Node>();
            public readonly List<float> Gaps = new List<float>(); // before each item

            public void Finish()
            {
                W = H = 0f;
                for (int i = 0; i < Items.Count; i++)
                {
                    Node n = Items[i];
                    float mw = n.W + n.MarginLeft + n.MarginRight, mh = n.H + n.MarginTop + n.MarginBottom;
                    if (Horizontal)
                    {
                        W += mw + Gaps[i];
                        H = Mathf.Max(H, mh);
                    }
                    else
                    {
                        H += mh + Gaps[i];
                        W = Mathf.Max(W, mw);
                    }
                }
            }

            public override void Arrange(float x, float y, float w, float h)
            {
                base.Arrange(x, y, w, h);
                if (Horizontal)
                {
                    ArrangeRow(x, y, w, h);
                }
                else
                {
                    ArrangeColumn(x, y, w);
                }
            }

            private void ArrangeRow(float x, float y, float w, float h)
            {
                float extra = Mathf.Max(0f, w - W);
                float between = 0f, cellShare = 0f, growShare = 0f, pen = x;
                int cells = 0, grow = 0;
                foreach (Node n in Items)
                {
                    cells += n.Divider ? 0 : 1;
                    grow += n.Grow ? 1 : 0;
                }

                if (Justify == "between" && Items.Count > 1)
                {
                    between = extra / (Items.Count - 1);
                }
                else if (Justify == "even" && cells > 0)
                {
                    cellShare = extra / cells;
                }
                else if (grow > 0)
                {
                    growShare = extra / grow;
                }
                else if (Main == "center")
                {
                    pen += extra * 0.5f;
                }
                else if (Main == "end")
                {
                    pen += extra;
                }

                for (int i = 0; i < Items.Count; i++)
                {
                    Node n = Items[i];
                    pen += Gaps[i] + (i > 0 ? between : 0f) + n.MarginLeft;
                    float cell = n.W + (n.Divider ? 0f : cellShare) + (n.Grow ? growShare : 0f);
                    float nw = n.Grow ? cell : n.W;
                    float cross = n.Divider ? h : n.H;
                    float room = h - n.MarginTop - n.MarginBottom;
                    float ny = y + n.MarginTop;
                    if (!n.Divider)
                    {
                        ny += Align == "start" ? 0f : Align == "end" ? room - n.H : (room - n.H) * 0.5f;
                    }

                    n.Arrange(pen + (cell - nw) * 0.5f, ny, nw, cross);
                    pen += cell + n.MarginRight;
                }
            }

            private void ArrangeColumn(float x, float y, float w)
            {
                float pen = y;
                for (int i = 0; i < Items.Count; i++)
                {
                    Node n = Items[i];
                    pen += Gaps[i] + n.MarginTop;
                    float room = w - n.MarginLeft - n.MarginRight;
                    float nw = n.Fill ? room : n.W;
                    string a = n.Place ?? Align;
                    float nx = x + n.MarginLeft;
                    if (!n.Fill)
                    {
                        nx += a == "center" ? (room - n.W) * 0.5f : a == "end" || a == "right" ? room - n.W : 0f;
                    }

                    n.Arrange(nx, pen, nw, n.H);
                    pen += n.H + n.MarginBottom;
                }
            }

            public override void Draw(SignCanvas c, float ppi)
            {
                foreach (Node n in Items)
                {
                    n.Draw(c, ppi);
                }
            }
        }

        /// <summary>A coloured box around its parts: a chip, or a full-width panel band.</summary>
        private sealed class BoxNode : Node
        {
            public StackNode Inner;
            public Color32 Bg;
            public float PadX, PadY, Radius;
            public float RadiusTL, RadiusTR, RadiusBR, RadiusBL;
            public bool HasBorder;
            public Color32 BorderColour;
            public float BorderWidth;

            public void Finish()
            {
                W = Inner.W + 2f * (PadX + BorderWidth);
                H = Inner.H + 2f * (PadY + BorderWidth);
            }

            public override void Arrange(float x, float y, float w, float h)
            {
                base.Arrange(x, y, w, H);
                float b = BorderWidth;
                Inner.Arrange(x + PadX + b, y + PadY + b, w - 2f * (PadX + b), Inner.H);
            }

            public override void Draw(SignCanvas c, float ppi)
            {
                float x0 = X * ppi, y0 = Y * ppi, x1 = (X + AW) * ppi, y1 = (Y + AH) * ppi;
                if (HasBorder)
                {
                    c.FillRoundRect(x0, y0, x1, y1, Radius * ppi, Radius * ppi, Radius * ppi, Radius * ppi, BorderColour);
                    float b = BorderWidth * ppi, ir = Mathf.Max(0f, Radius - BorderWidth) * ppi;
                    c.FillRoundRect(x0 + b, y0 + b, x1 - b, y1 - b, ir, ir, ir, ir, Bg);
                }
                else
                {
                    c.FillRoundRect(x0, y0, x1, y1, RadiusTL * ppi, RadiusTR * ppi, RadiusBR * ppi, RadiusBL * ppi, Bg);
                }

                Inner.Draw(c, ppi);
            }
        }

        // ------------------------------------------------------------------------------ building nodes

        private static Node Build(Theme t, Dictionary<string, object> item, Ctx ctx, bool inRow)
        {
            Node n = BuildNode(t, item, ctx, inRow);
            string type = Json.Str(item, "type");
            if (n != null && type != "row" && type != "column" && type != "spacer" && type != "divider")
            {
                Track(item, n);
            }

            return n;
        }

        private static void Track(Dictionary<string, object> item, Node n)
        {
            Tracked.Add(new KeyValuePair<Dictionary<string, object>, Node>(item, n));
        }

        /// <summary>The parts drawn, in the order built, found in the design's tree to name them by path.</summary>
        private static List<GuidePart> CollectParts(Dictionary<string, object> doc)
        {
            var paths = new Dictionary<Dictionary<string, object>, string>();
            AddPaths(doc, "", paths);
            var parts = new List<GuidePart>();
            foreach (KeyValuePair<Dictionary<string, object>, Node> kv in Tracked)
            {
                string path;
                if (paths.TryGetValue(kv.Key, out path))
                {
                    Node n = kv.Value;
                    parts.Add(new GuidePart { Path = path, Type = Json.Str(kv.Key, "type"), X = n.X, Y = n.Y, W = n.AW, H = n.AH });
                }
            }

            return parts;
        }

        private static void AddPaths(Dictionary<string, object> parent, string prefix, Dictionary<Dictionary<string, object>, string> paths)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                if (item != null)
                {
                    string path = prefix + i;
                    paths[item] = path;
                    AddPaths(item, path + "/", paths);
                }
            }
        }

        private static Node BuildNode(Theme t, Dictionary<string, object> item, Ctx ctx, bool inRow)
        {
            switch (Json.Str(item, "type"))
            {
                case "text": return BuildText(t, Json.Str(item, "text", ""), Json.Num(item, "height", t.TextHeight), Json.Str(item, "font") ?? ctx.Font,
                    ParseColour(Json.Str(item, "color"), ctx.Fg), Json.Num(item, "tracking", 0f));
                case "shield": return BuildShield(t, item, ctx);
                case "arrow": return BuildArrow(t, item, ctx);
                case "laneArrows": return BuildLaneArrows(t, item, ctx);
                case "symbol":
                {
                    float size = Json.Num(item, "height", t.ShieldHeight);
                    return new ShieldNode { Shape = "img", Image = GuideAssets.Image(t.Id, Json.Str(item, "name", "")), W = size, H = size };
                }

                case "row":
                {
                    StackNode s = BuildStack(t, item, Children(item), ctx, true);
                    bool spread = Json.Str(item, "justify") == "between" || Json.Str(item, "justify") == "even";
                    s.Fill = spread;
                    s.Grow = spread && inRow;
                    return s;
                }

                case "column": return BuildStack(t, item, Children(item), ctx, false);
                case "chip": return BuildChip(t, item, ctx);
                case "panel":
                {
                    BoxNode p = BuildPanel(t, item, ctx);
                    p.Fill = true;
                    return p;
                }

                case "spacer": return new BlockNode { W = Json.Num(item, "width", 0f), H = Json.Num(item, "height", 0f) };
                case "divider":
                {
                    float th = Json.Num(item, "thickness", t.Border);
                    return new BlockNode
                    {
                        Painted = true,
                        Colour = ParseColour(Json.Str(item, "color"), ctx.Fg),
                        W = inRow ? th : 0f,
                        H = inRow ? 0f : th,
                        Fill = !inRow,
                        Divider = inRow
                    };
                }

                default: return null; // parts this version doesn't draw (diagrams, LED text) are skipped
            }
        }

        private static TextNode BuildText(Theme t, string text, float h, string family, Color32 colour, float tracking)
        {
            GlyphFont font = t.Font(family);
            float em = h * t.FontScale(family);
            float track = h * tracking / 100f;
            float w = font != null ? font.Measure(text) * em / font.Em : 0f;
            w += track * Mathf.Max(0, text.Length - 1);
            return new TextNode { Text = text, Font = font, Em = em, Tracking = track, Colour = colour, W = w, H = h };
        }

        private static Node BuildShield(Theme t, Dictionary<string, object> item, Ctx ctx)
        {
            Dictionary<string, object> shields = Json.Obj(t.Def, "shields");
            Dictionary<string, object> def = Json.Obj(shields, Json.Str(item, "network", ""));
            if (def == null && shields != null)
            {
                foreach (KeyValuePair<string, object> kv in shields)
                {
                    def = kv.Value as Dictionary<string, object>;
                    break;
                }
            }

            float h = Json.Num(item, "height", t.ShieldHeight);
            object numberValue;
            string number = item.TryGetValue("number", out numberValue) && numberValue != null
                ? Convert.ToString(numberValue, CultureInfo.InvariantCulture)
                : "";
            string prefix = Json.Str(def, "prefix", "");
            string shape = Json.Str(def, "shape") ?? (Json.Has(def, "img") ? "img" : "rect");
            float wide = number.Length >= 3 ? Json.Num(def, "wide", 1f) : 1f;
            string family = (wide > 1f ? Json.Str(def, "wideFont") : null) ?? Json.Str(def, "font") ?? ctx.Font;
            float nh = h * Json.Num(def, "num", 0.6f) * (wide > 1f && shape == "img" ? 0.9f : 1f);
            Color32 fg = ParseColour(Json.Str(def, "fg"), shape == "plain" ? ctx.Fg : new Color32(0, 0, 0, 255));
            TextNode num = BuildText(t, prefix + number, nh, family, fg, 0f);
            var node = new ShieldNode
            {
                Shape = shape,
                Number = num,
                Bg = ParseColour(Json.Str(def, "bg"), new Color32(255, 255, 255, 255)),
                HasBorder = Json.Has(def, "border"),
                BorderColour = ParseColour(Json.Str(def, "border"), new Color32(0, 0, 0, 255)),
                Radius = h * Json.Num(def, "radius", 0.12f),
                BorderWidth = h * Json.Num(def, "borderWidth", 0.06f),
                H = h
            };
            if (shape == "rect" || shape == "plain")
            {
                float padX = h * Json.Num(def, "padX", 0.25f);
                node.W = Mathf.Max(num.W + 2f * padX, h * Json.Num(def, "minAspect", 0f));
                node.NumberTop = (h - nh) * 0.5f;
            }
            else
            {
                node.W = h * Json.Num(def, "aspect", 1f) * wide;
                node.NumberTop = h * Json.Num(def, "top", 0.3f);
                if (shape == "img")
                {
                    string img = Json.Str(def, "img", "");
                    int slash = img.LastIndexOf('/');
                    int dot = img.LastIndexOf('.');
                    node.Image = GuideAssets.Image(t.Id, dot > slash ? img.Substring(slash + 1, dot - slash - 1) : img);
                }
            }

            return node;
        }

        private static readonly Dictionary<string, float> Rotations = new Dictionary<string, float>
        {
            { "down", 0f }, { "up", 180f }, { "left", 90f }, { "right", -90f }, { "down-left", 45f }, { "down-right", -45f },
            { "up-left", 135f }, { "up-right", -135f }
        };

        private const string TurnPath = "M54 150 V82 A24 24 0 0 0 30 58 V78 L0 46 L30 14 V34 A48 48 0 0 1 78 82 V150 Z";
        private const string SplitPath = "M54 150 V128 A24 24 0 0 0 30 104 V124 L0 92 L30 60 V80 A48 48 0 0 1 54 86.4 V34 H34 L66 0 L98 34 H78 V150 Z";

        private static Node BuildLaneArrows(Theme t, Dictionary<string, object> item, Ctx ctx)
        {
            const float SectionPad = 4f;
            var node = new LaneArrowsNode { Fill = true, H = Json.Num(item, "height", t.ArrowHeight) };
            List<object> at = Json.Arr(item, "at");
            List<object> edges = Json.Arr(item, "edges");
            List<Dictionary<string, object>> arrows = Children(item);
            Color32 exitBg, exitFg;
            t.Scheme("yellow", out exitBg, out exitFg);
            var exitCtx = new Ctx { Font = ctx.Font, Fg = exitFg, Bg = exitBg };
            ExitSection section = null;
            for (int i = 0; at != null && i < at.Count && i < arrows.Count && node.At.Count < 64; i++)
            {
                bool exitOnly = arrows[i].ContainsKey(GuideFit.ExitOnlyKey) && edges != null && 2 * i + 1 < edges.Count &&
                    edges[2 * i] is double && edges[2 * i + 1] is double;
                Node a = at[i] is double ? Build(t, arrows[i], exitOnly ? exitCtx : ctx, true) : null;
                if (a == null)
                {
                    section = null;
                    continue;
                }

                int index = node.Arrows.Count;
                node.At.Add((float)(double)at[i]);
                node.Arrows.Add(a);
                node.H = Mathf.Max(node.H, a.H);
                if (!exitOnly)
                {
                    section = null;
                    continue;
                }

                if (section == null)
                {
                    section = new ExitSection
                    {
                        Theme = t,
                        Wording = Json.Str(item, GuideFit.ExitTextKey, GuideFit.DefaultExitText),
                        Font = "RG2014D",
                        Bg = exitBg,
                        Fg = exitFg,
                        First = index,
                        Left = (float)(double)edges[2 * i]
                    };
                    node.Sections.Add(section);
                }

                section.Last = index;
                section.Right = (float)(double)edges[2 * i + 1];
            }

            if (node.Sections.Count > 0)
            {
                node.H += 2f * SectionPad;
            }

            return node;
        }

        private static Node BuildArrow(Theme t, Dictionary<string, object> item, Ctx ctx)
        {
            float h = Json.Num(item, "height", t.ArrowHeight);
            string dir = Json.Str(item, "direction", "down");
            if (dir == "none")
            {
                return new BlockNode { W = h * 100f / 150f, H = h }; // a lane with no arrow keeps its place
            }

            // A theme can draw its arrows its own way, under a name the sign's arrow asks for.
            Dictionary<string, object> style = Json.Obj(Json.Obj(t.Def, "arrowStyles"), Json.Str(item, "style", ""));
            float unit = h / 150f;
            List<Vector2> pts;
            float vx = 0f, vy = 0f, vw = 100f, vh = 150f;
            if (dir.StartsWith("turn-", StringComparison.Ordinal) || dir.StartsWith("split-", StringComparison.Ordinal))
            {
                pts = PathPoints(dir.StartsWith("turn-", StringComparison.Ordinal) ? Json.Str(style, "turn", TurnPath) : Json.Str(style, "split", SplitPath));
                if (dir.EndsWith("-right", StringComparison.Ordinal))
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        pts[i] = new Vector2(100f - pts[i].x, pts[i].y);
                    }
                }
            }
            else
            {
                pts = PathPoints(Json.Str(style, "arrow", Json.Str(t.Def, "arrow", DefaultArrow)));
                float rot;
                if (!Rotations.TryGetValue(dir, out rot))
                {
                    rot = 0f;
                }

                float r = rot * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    float dx = pts[i].x - 50f, dy = pts[i].y - 75f;
                    var p = new Vector2(50f + dx * cos - dy * sin, 75f + dx * sin + dy * cos);
                    pts[i] = p;
                    x0 = Mathf.Min(x0, p.x);
                    y0 = Mathf.Min(y0, p.y);
                    x1 = Mathf.Max(x1, p.x);
                    y1 = Mathf.Max(y1, p.y);
                }

                vx = x0;
                vy = y0;
                vw = x1 - x0;
                vh = y1 - y0;
            }

            for (int i = 0; i < pts.Count; i++)
            {
                pts[i] = new Vector2((pts[i].x - vx) * unit, (pts[i].y - vy) * unit);
            }

            return new ArrowNode { Points = pts, Colour = ParseColour(Json.Str(item, "color"), ctx.Fg), W = vw * unit, H = vh * unit };
        }

        private static StackNode BuildStack(Theme t, Dictionary<string, object> item, List<Dictionary<string, object>> children, Ctx ctx, bool horizontal)
        {
            var s = new StackNode
            {
                Horizontal = horizontal,
                Align = Json.Str(item, "align") ?? (horizontal ? "center" : "start"),
                Justify = horizontal ? Json.Str(item, "justify") : null
            };
            Dictionary<string, object> previous = null;
            foreach (Dictionary<string, object> child in children)
            {
                Node n = Build(t, child, ctx, horizontal);
                if (n == null)
                {
                    continue;
                }

                float gap = 0f;
                if (previous != null)
                {
                    float reference = Mathf.Max(SpacingHeight(t, previous), SpacingHeight(t, child));
                    gap = Json.Has(child, "gapBefore") ? Json.Num(child, "gapBefore", 0f)
                        : Json.Has(item, "gap") ? Json.Num(item, "gap", 0f)
                        : reference * (horizontal ? t.ItemGap : t.LineGap);
                }

                Dictionary<string, object> m = Json.Obj(child, "margin");
                n.MarginTop = Json.Num(m, "top", 0f);
                n.MarginRight = Json.Num(m, "right", 0f);
                n.MarginBottom = Json.Num(m, "bottom", 0f);
                n.MarginLeft = Json.Num(m, "left", 0f);
                n.Place = Json.Str(child, "place");
                s.Items.Add(n);
                s.Gaps.Add(gap);
                previous = child;
            }

            s.Finish();
            return s;
        }

        private static void BoxColours(Theme t, Dictionary<string, object> item, Ctx ctx, out Color32 bg, out Color32 fg)
        {
            if (Json.Has(item, "scheme"))
            {
                t.Scheme(Json.Str(item, "scheme"), out bg, out fg);
            }
            else
            {
                bg = ctx.Fg;
                fg = ctx.Bg;
            }

            bg = ParseColour(Json.Str(item, "background"), bg);
            fg = ParseColour(Json.Str(item, "foreground"), fg);
        }

        private static Node BuildChip(Theme t, Dictionary<string, object> item, Ctx ctx)
        {
            Color32 bg, fg;
            BoxColours(t, item, ctx, out bg, out fg);
            float h = SpacingHeight(t, item);
            h = h > 0f ? h : t.TextHeight;
            float pad = Json.Num(item, "padding", h * 0.3f);
            bool vertical = item.ContainsKey("vertical") && item["vertical"] is bool && (bool)item["vertical"];
            var inner = new Dictionary<string, object> { { "type", vertical ? "column" : "row" } };
            if (Json.Has(item, "align"))
            {
                inner["align"] = item["align"];
            }

            StackNode stack = BuildStack(t, inner, Children(item), new Ctx { Font = ctx.Font, Fg = fg, Bg = bg }, !vertical);
            stack.Main = Json.Str(item, "align") == "end" ? "end" : Json.Str(item, "align") == "start" ? "start" : "center";
            float radius = Json.Num(item, "radius", h * 0.15f);
            var box = new BoxNode
            {
                Inner = stack,
                Bg = bg,
                PadX = pad,
                PadY = pad * 0.7f,
                Radius = radius,
                RadiusTL = radius,
                RadiusTR = radius,
                RadiusBR = radius,
                RadiusBL = radius,
                Fill = Json.Str(item, "span") == "full"
            };
            object border;
            if (item.TryGetValue("border", out border) && border != null && !(border is bool && !(bool)border))
            {
                box.HasBorder = true;
                box.BorderWidth = Json.Num(item, "borderWidth", t.Border);
                box.BorderColour = border is string ? ParseColour((string)border, fg) : fg;
            }

            box.Finish();
            return box;
        }

        private static BoxNode BuildPanel(Theme t, Dictionary<string, object> item, Ctx ctx)
        {
            Color32 bg, fg;
            BoxColours(t, item, ctx, out bg, out fg);
            float h = NominalHeight(t, item);
            h = h > 0f ? h : t.TextHeight;
            float pad = Json.Num(item, "padding", h * 0.35f);
            float spacing = SpacingHeight(t, item);
            float gap = Json.Num(item, "gap", (spacing > 0f ? spacing : h) * 0.75f);
            var inner = new Dictionary<string, object> { { "type", "row" }, { "gap", (double)gap } };
            StackNode stack = BuildStack(t, inner, Children(item), new Ctx { Font = ctx.Font, Fg = fg, Bg = bg }, true);
            string align = Json.Str(item, "align");
            stack.Main = align == "start" || align == "left" ? "start" : align == "end" || align == "right" ? "end" : "center";
            var box = new BoxNode { Inner = stack, Bg = bg, PadX = pad * 2f, PadY = pad };
            box.Finish();
            if (stack.Items.Count == 0)
            {
                box.H = Mathf.Max(box.H, t.TextHeight * 0.9f);
            }

            return box;
        }

        // ------------------------------------------------------------------------------ paths

        /// <summary>Points of a path of absolute M, L, H, V, A and Z commands; arcs are flattened.</summary>
        private static List<Vector2> PathPoints(string d)
        {
            var pts = new List<Vector2>();
            float x = 0f, y = 0f;
            int i = 0;
            char cmd = 'M';
            Func<float> num = () =>
            {
                while (i < d.Length && (d[i] == ' ' || d[i] == ','))
                {
                    i++;
                }

                int s = i;
                while (i < d.Length && "+-.0123456789eE".IndexOf(d[i]) >= 0)
                {
                    i++;
                }

                return float.Parse(d.Substring(s, i - s), CultureInfo.InvariantCulture);
            };
            while (i < d.Length)
            {
                char ch = d[i];
                if (ch == ' ' || ch == ',')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(ch) && ch != 'e' && ch != 'E')
                {
                    cmd = char.ToUpperInvariant(ch);
                    i++;
                    if (cmd == 'Z')
                    {
                        continue;
                    }
                }

                switch (cmd)
                {
                    case 'M':
                    case 'L':
                        x = num();
                        y = num();
                        pts.Add(new Vector2(x, y));
                        break;
                    case 'H':
                        x = num();
                        pts.Add(new Vector2(x, y));
                        break;
                    case 'V':
                        y = num();
                        pts.Add(new Vector2(x, y));
                        break;
                    case 'A':
                    {
                        float rx = num(), ry = num(), rot = num(), large = num(), sweep = num(), ex = num(), ey = num();
                        Arc(pts, x, y, rx, ry, rot, large != 0f, sweep != 0f, ex, ey);
                        x = ex;
                        y = ey;
                        break;
                    }

                    default:
                        throw new FormatException("Unsupported path command " + cmd);
                }
            }

            return pts;
        }

        /// <summary>SVG elliptical arc, as points (endpoint to centre conversion from the SVG specification).</summary>
        private static void Arc(List<Vector2> pts, float x0, float y0, float rx, float ry, float rotation, bool large, bool sweep, float x1, float y1)
        {
            double phi = rotation * Math.PI / 180.0, cp = Math.Cos(phi), sp = Math.Sin(phi);
            double dx = (x0 - x1) / 2.0, dy = (y0 - y1) / 2.0;
            double xp = cp * dx + sp * dy, yp = -sp * dx + cp * dy;
            double a = rx, b = ry;
            double lambda = xp * xp / (a * a) + yp * yp / (b * b);
            if (lambda > 1.0)
            {
                a *= Math.Sqrt(lambda);
                b *= Math.Sqrt(lambda);
            }

            double num = a * a * b * b - a * a * yp * yp - b * b * xp * xp;
            double den = a * a * yp * yp + b * b * xp * xp;
            double co = den > 0.0 ? Math.Sqrt(Math.Max(0.0, num / den)) : 0.0;
            if (large == sweep)
            {
                co = -co;
            }

            double cxp = co * a * yp / b, cyp = -co * b * xp / a;
            double cx = cp * cxp - sp * cyp + (x0 + x1) / 2.0, cy = sp * cxp + cp * cyp + (y0 + y1) / 2.0;
            double th0 = Math.Atan2((yp - cyp) / b, (xp - cxp) / a), th1 = Math.Atan2((-yp - cyp) / b, (-xp - cxp) / a);
            double delta = th1 - th0;
            if (sweep && delta < 0.0)
            {
                delta += 2.0 * Math.PI;
            }
            else if (!sweep && delta > 0.0)
            {
                delta -= 2.0 * Math.PI;
            }

            const int Steps = 16;
            for (int i = 1; i <= Steps; i++)
            {
                double t = th0 + delta * i / Steps;
                double ux = a * Math.Cos(t), uy = b * Math.Sin(t);
                pts.Add(new Vector2((float)(cp * ux - sp * uy + cx), (float)(sp * ux + cp * uy + cy)));
            }
        }

        // ------------------------------------------------------------------------------ the sign

        private sealed class TabLayout
        {
            public StackNode Row;
            public Color32 Bg, Fg;
            public float W, H, Pad, Inner;
        }

        /// <summary>
        /// Renders a design; the texture's longest side is at most <paramref name="maxPixels"/>, and its area at most
        /// 1024 x 1024. With <paramref name="measureOnly"/> just the sign's size is worked out, with no pixels.
        /// </summary>
        public static GuideSign Render(Dictionary<string, object> doc, int maxPixels = 1024, float maxPixelsPerUnit = 4f, bool measureOnly = false)
        {
            Tracked.Clear();
            var t = new Theme(Json.Str(doc, "theme", "us"));
            Color32 bg, fg;
            t.Scheme(Json.Str(doc, "scheme"), out bg, out fg);
            bg = ParseColour(Json.Str(doc, "background"), bg);
            fg = ParseColour(Json.Str(doc, "foreground"), fg);
            float border = Json.Num(doc, "border", t.Border);
            float radius = Json.Num(doc, "cornerRadius", t.CornerRadius);
            List<Dictionary<string, object>> items = Children(doc);

            float legend = 0f;
            foreach (Dictionary<string, object> i in items)
            {
                string type = Json.Str(i, "type");
                if (type != "tab" && type != "panel")
                {
                    legend = Mathf.Max(legend, SpacingHeight(t, i));
                }
            }

            legend = legend > 0f ? legend : t.TextHeight;
            float margin = Json.Num(doc, "margin", legend * t.Margin);
            var ctx = new Ctx { Font = Json.Str(doc, "font") ?? t.DefaultFont, Fg = fg, Bg = bg };
            float minWidth = Json.Has(doc, "minLanes")
                ? Json.Num(doc, "minLanes", 0f) * (Json.Num(t.Def, "laneWidth", 157.48f) / 0.7f) * 0.92f
                : Json.Num(doc, "minWidth", 0f);

            // Tabs on top of the sign, by side.
            var left = new List<TabLayout>();
            var centre = new List<TabLayout>();
            var right = new List<TabLayout>();
            Dictionary<string, object> exitTab = Json.Obj(t.Def, "exitTab");
            foreach (Dictionary<string, object> i in items)
            {
                if (Json.Str(i, "type") != "tab")
                {
                    continue;
                }

                TabLayout tab = BuildTab(t, i, ctx, Json.Str(exitTab, "font") ?? ctx.Font, bg, fg, border);
                Track(i, tab.Row);
                string side = Json.Str(i, "align");
                (side == "left" || side == "start" ? left : side == "center" ? centre : right).Add(tab);
            }

            // The body: stretches between panels, each with the sign's margin around it.
            var parts = new List<Node>();
            var stretch = new List<Dictionary<string, object>>();
            var column = new Dictionary<string, object> { { "type", "column" }, { "align", Json.Str(doc, "align", "start") } };
            Action flush = () =>
            {
                if (stretch.Count > 0)
                {
                    parts.Add(BuildStack(t, column, new List<Dictionary<string, object>>(stretch), ctx, false));
                    stretch.Clear();
                }
            };
            foreach (Dictionary<string, object> i in items)
            {
                string type = Json.Str(i, "type");
                if (type == "tab")
                {
                    continue;
                }

                if (type == "panel")
                {
                    flush();
                    BoxNode band = BuildPanel(t, i, ctx);
                    band.Fill = true;
                    Track(i, band);
                    parts.Add(band);
                }
                else
                {
                    stretch.Add(i);
                }
            }

            flush();

            // Sizes.
            float content = 0f, bodyH = 0f;
            foreach (Node p in parts)
            {
                bool band = p is BoxNode;
                content = Mathf.Max(content, p.W + (band ? 0f : 2f * margin));
                bodyH += p.H + (band ? 0f : 2f * margin);
            }

            float tabsW = 0f, tabsH = 0f;
            foreach (List<TabLayout> side in new[] { left, centre, right })
            {
                foreach (TabLayout tab in side)
                {
                    tabsW += tab.W;
                    tabsH = Mathf.Max(tabsH, tab.H);
                }
            }

            float width = Mathf.Max(content + 4f * border, Mathf.Max(tabsW, Mathf.Max(minWidth, Json.Num(doc, "width", 0f))));
            float extraH = Mathf.Max(0f, Json.Num(doc, "height", 0f) - (bodyH + 4f * border));
            if (Json.Has(doc, GuideFit.SquareKey))
            {
                float sideLength = Mathf.Max(width, bodyH + extraH + 4f * border);
                width = sideLength;
                extraH = sideLength - (bodyH + 4f * border);
            }

            float panelH = bodyH + extraH + 4f * border;
            float height = tabsH + panelH;

            if (measureOnly)
            {
                return new GuideSign { Width = width, Height = height, TabHeight = tabsH };
            }

            // Bake size.
            float ppu = Mathf.Min(Mathf.Min(maxPixelsPerUnit, maxPixels / Mathf.Max(width, height)), 1024f / Mathf.Sqrt(width * height));
            int pw = Mathf.Max(1, Mathf.CeilToInt(width * ppu)), ph = Mathf.Max(1, Mathf.CeilToInt(height * ppu));
            var canvas = new SignCanvas(pw, ph);

            // Panel: background, the legend border inside it, then the content.
            float r = radius, ir = Mathf.Max(0f, radius - border), iir = Mathf.Max(0f, ir - border);
            float rTL = left.Count > 0 ? 0f : 1f, rTR = right.Count > 0 ? 0f : 1f;
            float y0 = tabsH, y1 = height;
            canvas.FillRoundRect(0f, y0 * ppu, width * ppu, y1 * ppu, r * rTL * ppu, r * rTR * ppu, r * ppu, r * ppu, bg);
            canvas.FillRoundRect(border * ppu, (y0 + border) * ppu, (width - border) * ppu, (y1 - border) * ppu,
                ir * rTL * ppu, ir * rTR * ppu, ir * ppu, ir * ppu, fg);
            canvas.FillRoundRect(2f * border * ppu, (y0 + 2f * border) * ppu, (width - 2f * border) * ppu, (y1 - 2f * border) * ppu,
                iir * rTL * ppu, iir * rTR * ppu, iir * ppu, iir * ppu, bg);

            float inner = width - 4f * border, pen = y0 + 2f * border;

            // Spare height goes around the first run of content, so it stays centred above any band.
            int spareAt = parts.FindIndex(n => !(n is BoxNode));
            for (int k = 0; k < parts.Count; k++)
            {
                var band = parts[k] as BoxNode;
                if (band != null)
                {
                    band.RadiusTL = k == 0 ? iir * rTL : 0f;
                    band.RadiusTR = k == 0 ? iir * rTR : 0f;
                    band.RadiusBR = band.RadiusBL = k == parts.Count - 1 ? iir : 0f;
                    band.Arrange(2f * border, pen, inner, band.H);
                    band.Draw(canvas, ppu);
                    pen += band.H;
                }
                else
                {
                    float extra = k == spareAt ? extraH : 0f;
                    parts[k].Arrange(2f * border + margin, pen + margin + extra * 0.5f, inner - 2f * margin, parts[k].H);
                    parts[k].Draw(canvas, ppu);
                    pen += parts[k].H + 2f * margin + extra;
                }
            }

            // Tabs sit on the panel's top edge.
            var tabShapes = new List<GuideTab>();
            float lx = 0f;
            foreach (TabLayout tab in left)
            {
                DrawTab(canvas, tab, lx, tabsH, radius, border, ppu);
                tabShapes.Add(new GuideTab { X = lx, Width = tab.W, Height = tab.H });
                lx += tab.W;
            }

            float cw = 0f;
            foreach (TabLayout tab in centre)
            {
                cw += tab.W;
            }

            float cx = (width - cw) * 0.5f;
            foreach (TabLayout tab in centre)
            {
                DrawTab(canvas, tab, cx, tabsH, radius, border, ppu);
                tabShapes.Add(new GuideTab { X = cx, Width = tab.W, Height = tab.H });
                cx += tab.W;
            }

            float rx = width;
            for (int k = right.Count - 1; k >= 0; k--)
            {
                rx -= right[k].W;
                DrawTab(canvas, right[k], rx, tabsH, radius, border, ppu);
                tabShapes.Add(new GuideTab { X = rx, Width = right[k].W, Height = right[k].H });
            }

            return new GuideSign
            {
                PixelWidth = pw,
                PixelHeight = ph,
                Pixels = canvas.Pixels,
                Width = width,
                Height = height,
                TabHeight = tabsH,
                Radius = Mathf.Max(0f, radius),
                SquareLeft = left.Count > 0,
                SquareRight = right.Count > 0,
                Tabs = tabShapes,
                Parts = CollectParts(doc)
            };
        }

        private static TabLayout BuildTab(Theme t, Dictionary<string, object> item, Ctx ctx, string font, Color32 bg, Color32 fg, float border)
        {
            if (Json.Has(item, "scheme"))
            {
                t.Scheme(Json.Str(item, "scheme"), out bg, out fg);
            }

            float h = SpacingHeight(t, item);
            h = h > 0f ? h : 10f;
            var row = new Dictionary<string, object> { { "type", "row" }, { "align", "center" } };
            StackNode stack = BuildStack(t, row, Children(item), new Ctx { Font = font, Fg = fg, Bg = bg }, true);
            float padX = h * 0.7f, padY = h * 0.45f;
            return new TabLayout { Row = stack, Bg = bg, Fg = fg, Pad = padX, Inner = padY, W = stack.W + 2f * padX + 4f * border, H = stack.H + 2f * padY + 2f * border };
        }

        private static void DrawTab(SignCanvas c, TabLayout tab, float x, float tabsH, float radius, float border, float ppu)
        {
            float y = tabsH - tab.H, ir = Mathf.Max(0f, radius - border), iir = Mathf.Max(0f, ir - border);
            c.FillRoundRect(x * ppu, y * ppu, (x + tab.W) * ppu, tabsH * ppu, radius * ppu, radius * ppu, 0f, 0f, tab.Bg);
            c.FillRoundRect((x + border) * ppu, (y + border) * ppu, (x + tab.W - border) * ppu, tabsH * ppu, ir * ppu, ir * ppu, 0f, 0f, tab.Fg);
            c.FillRoundRect((x + 2f * border) * ppu, (y + 2f * border) * ppu, (x + tab.W - 2f * border) * ppu, tabsH * ppu, iir * ppu, iir * ppu, 0f, 0f, tab.Bg);
            tab.Row.Arrange(x + 2f * border + tab.Pad, y + 2f * border + tab.Inner, tab.Row.W, tab.Row.H);
            tab.Row.Draw(c, ppu);
        }
    }
}
