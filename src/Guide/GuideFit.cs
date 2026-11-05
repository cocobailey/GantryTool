using System;
using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>Where a guide sign really goes, and what it really shows, once it has been fitted to its lanes.</summary>
    public struct GuidePlacement
    {
        public string Design;   // the design to draw (the stored one, widened and with lane arrows when fitted)
        public float Along;
        public float Scale;
        public bool Fitted;
        public GuideHead Source;
        public float WidthMetres, HeightMetres, PanelMetres;   // as drawn; the panel leaves out any exit tab above it
    }

    /// <summary>
    /// Fits a guide sign to the lanes it covers: the sign spans them, centred, and can put one arrow over the middle of
    /// each lane. Worked out from the stored design and the lanes under the gantry, so a sign follows a road upgrade.
    /// Testable outside the game.
    /// </summary>
    public static class GuideFit
    {
        /// <summary>Theme units are roughly inches of a full-size sign; 70% of real size looks right beside the game's roads.</summary>
        public const float MetresPerUnit = 0.0254f * 0.7f;

        private const float Inset = 0.2f;      // clear of the next sign, each side
        private const float MinWidth = 1.5f;

        /// <summary>Marks the row of arrows in a design that becomes one arrow per lane.</summary>
        public const string BarKey = "bar";
        public const string OwnWidthKey = "ownWidth";  // a sign that isn't made to stretch across the lanes it's over
        public const string CompactKey = "compact";    // a small plate: keeps its own size, not the default or its neighbours'
        public const string ExitOnlyWord = "exitOnly";
        public const string ExitTabWord = "exitTab";
        public const string ArrowKey = "arrowDirection";
        public const string ArrowStyleKey = "arrowStyle";
        public const string WidthKey = "signWidth";    // the player's own size, metres; in the design, so it needs no save data
        public const string HeightKey = "signHeight";
        public const string SquareKey = "square";       // the sign stays square, growing with the gantry's other signs
        public const float DefaultWidth = 3f, DefaultHeight = 2.4f; // metres: no sign starts smaller

        public const string PerLaneKey = "perLane";

        /// <summary>On a lane's arrow: that lane is exit only, shown as a coloured section under its arrow.</summary>
        public const string ExitOnlyKey = "exitOnly";

        /// <summary>On the lane row: the wording of its exit only sections.</summary>
        public const string ExitTextKey = "exitText";
        public const string DefaultExitText = "EXIT ONLY";

        /// <summary>The lanes whose drivers see this face (<paramref name="side"/> as in GuideHead), left to right as they see them.</summary>
        public static List<GantryLane> FaceLanes(List<GantryLane> lanes, sbyte side)
        {
            var seen = new List<GantryLane>();
            foreach (GantryLane l in lanes)
            {
                if (l.TravelSign == 0 || l.TravelSign == -side)
                {
                    seen.Add(l);
                }
            }

            // Lanes are sorted by distance along the beam, which runs left to right on the -side face.
            if (side > 0)
            {
                seen.Reverse();
            }

            return seen;
        }

        /// <summary>
        /// Lanes for a new sign (1-based, left to right): the first run not yet under a fitted sign on this face, or all
        /// of them if none is free. 0, 0 when no lanes cross this face (the sign is then placed by hand).
        /// </summary>
        public static void DefaultRange(List<GantryLane> lanes, List<GuideHead> signs, sbyte side, out byte first, out byte last)
        {
            int n = Mathf.Min(FaceLanes(lanes, side).Count, 255);
            first = last = 0;
            if (n == 0)
            {
                return;
            }

            var taken = new bool[n];
            foreach (GuideHead h in signs)
            {
                if (h.FitFirst > 0 && h.Side == side)
                {
                    for (int i = h.FitFirst - 1; i < h.FitLast && i < n; i++)
                    {
                        taken[i] = true;
                    }
                }
            }

            int a = Array.IndexOf(taken, false);
            if (a < 0)
            {
                first = 1;
                last = (byte)n;
                return;
            }

            int b = a;
            while (b + 1 < n && !taken[b + 1])
            {
                b++;
            }

            first = (byte)(a + 1);
            last = (byte)(b + 1);
        }

        public static bool SameFit(GuideHead a, GuideHead b)
        {
            return a.FitFirst == b.FitFirst && a.FitLast == b.FitLast && a.LaneArrows == b.LaneArrows && a.Scale == b.Scale &&
                a.Side == b.Side && a.Along == b.Along && a.Design == b.Design;
        }

        /// <summary>True if the design has a row of nothing but arrows, which a fitted sign replaces with one per lane.</summary>
        public static bool HasArrowRow(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    if (IsArrows(o as Dictionary<string, object>))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Which line of the design becomes the lane arrows: the row marked for it, or the first row of arrows in a design
        /// that has no mark (the ready-made signs); -1 if there's neither. Any other arrows stay as they are.
        /// </summary>
        public static int LaneRowIndex(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            int first = -1;
            for (int i = 0; items != null && i < items.Count; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                if (IsArrows(item))
                {
                    if (item.ContainsKey(PerLaneKey))
                    {
                        return i;
                    }

                    first = first < 0 ? i : first;
                }
            }

            return first;
        }

        public static bool HasDivider(Dictionary<string, object> parent)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    var item = o as Dictionary<string, object>;
                    if (item != null && (Json.Str(item, "type") == "divider" || HasDivider(item)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Splits the sign's wording into two sides with a line between them: what's there goes left, and the right starts
        /// with a new line of text, which is returned. Tabs, bands and arrows stay as they are.
        /// </summary>
        public static Dictionary<string, object> SplitSign(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            if (items == null)
            {
                items = new List<object>();
                doc["items"] = items;
            }

            var left = new List<object>();
            int at = -1;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i] as Dictionary<string, object>;
                string type = Json.Str(item, "type");
                if (item != null && type != "tab" && type != "panel" && !IsArrows(item))
                {
                    left.Insert(0, item);
                    items.RemoveAt(i);
                    at = i;
                }
            }

            if (at < 0)
            {
                at = 0;
                while (at < items.Count && Json.Str(items[at] as Dictionary<string, object>, "type") == "tab")
                {
                    at++;
                }
            }

            var text = new Dictionary<string, object> { { "type", "text" }, { "text", "New text" } };
            items.Insert(at, new Dictionary<string, object>
            {
                { "type", "row" },
                { "align", "center" },
                { "justify", "even" },
                {
                    "items", new List<object>
                    {
                        new Dictionary<string, object> { { "type", "column" }, { "align", "center" }, { "items", left } },
                        new Dictionary<string, object> { { "type", "divider" } },
                        new Dictionary<string, object> { { "type", "column" }, { "align", "center" }, { "items", new List<object> { text } } }
                    }
                }
            });
            return text;
        }

        /// <summary>
        /// Puts a small EXIT ONLY plate between the arrows of the sign's last row of arrows, making that row if there isn't
        /// one. Returns the plate's text.
        /// </summary>
        public static Dictionary<string, object> AddExitPlate(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            if (items == null)
            {
                items = new List<object>();
                doc["items"] = items;
            }

            var text = new Dictionary<string, object> { { "type", "text" }, { "text", GuideThemes.Word(Json.Str(doc, "theme", GuideThemes.Fallback), ExitOnlyWord, DefaultExitText) }, { "height", 12.0 }, { "font", Json.Str(GuideAssets.Theme(Json.Str(doc, "theme", GuideThemes.Fallback)), "exitOnlyFont", "RG2014D") } };
            var chip = new Dictionary<string, object> { { "type", "chip" }, { "scheme", "yellow" }, { "radius", 2.0 }, { "items", new List<object> { text } } };
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = items[i] as Dictionary<string, object>;
                if (row != null && Json.Str(row, "type") == "row" && IsArrows(row))
                {
                    List<object> arrows = Json.Arr(row, "items");
                    arrows.Insert((arrows.Count + 1) / 2, chip);
                    row["justify"] = "between";
                    return text;
                }
            }

            int at = items.Count;
            while (at > 0 && Json.Str(items[at - 1] as Dictionary<string, object>, "type") == "panel")
            {
                at--;
            }

            items.Insert(at, new Dictionary<string, object>
            {
                { "type", "row" },
                { "justify", "between" },
                { "items", new List<object> { NewArrow(doc), chip, NewArrow(doc) } }
            });
            return text;
        }

        /// <summary>An arrow for a sign, pointing the way that sign's arrows point (down, unless it says otherwise).</summary>
        public static Dictionary<string, object> NewArrow(Dictionary<string, object> doc)
        {
            var arrow = new Dictionary<string, object> { { "type", "arrow" }, { "direction", Json.Str(doc, ArrowKey, "down") }, { "height", 24.0 } };
            if (Json.Has(doc, ArrowStyleKey))
            {
                arrow["style"] = Json.Str(doc, ArrowStyleKey);
            }

            return arrow;
        }

        /// <summary>The lanes a fitted sign covers on its face.</summary>
        public static int LaneCount(GuideHead h, List<GantryLane> allLanes)
        {
            int n = FaceLanes(allLanes, h.Side).Count;
            if (h.FitFirst == 0 || n == 0)
            {
                return 0;
            }

            int first = Mathf.Clamp(h.FitFirst - 1, 0, n - 1);
            return Mathf.Clamp(h.FitLast - 1, first, n - 1) - first + 1;
        }

        /// <summary>Gives the lane row one arrow per lane, in lane order, new lanes copying the arrow before them.</summary>
        public static void PadLaneRow(Dictionary<string, object> doc, int lanes)
        {
            int where = LaneRowIndex(doc);
            List<object> items = where >= 0 ? Json.Arr((Dictionary<string, object>)Json.Arr(doc, "items")[where], "items") : null;
            while (items != null && items.Count < lanes)
            {
                var copy = new Dictionary<string, object>((Dictionary<string, object>)items[items.Count - 1]);
                copy.Remove(ExitOnlyKey);
                items.Add(copy);
            }
        }

        private static List<object> RowArrows(Dictionary<string, object> doc, int index)
        {
            var item = (Dictionary<string, object>)Json.Arr(doc, "items")[index];
            return Json.Str(item, "type") == "row" ? Json.Arr(item, "items") : new List<object> { item };
        }

        /// <summary>
        /// Makes a design ready for one arrow per lane; returns the row. A row of arrows is marked for it; arrows in a row
        /// with other parts move to a new row below it, keeping their order, and the other parts stay. Otherwise it's a new row.
        /// </summary>
        public static Dictionary<string, object> MakeLaneRow(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            if (items == null)
            {
                items = new List<object>();
                doc["items"] = items;
            }

            int where = LaneRowIndex(doc);
            Dictionary<string, object> row = where >= 0 ? items[where] as Dictionary<string, object> : null;
            if (row != null && Json.Str(row, "type") == "row")
            {
                row[PerLaneKey] = true;
                return row;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var mixed = items[i] as Dictionary<string, object>;
                List<object> children = mixed != null && Json.Str(mixed, "type") == "row" ? Json.Arr(mixed, "items") : null;
                var arrows = new List<object>();
                if (children != null)
                {
                    foreach (object c in children)
                    {
                        if (Json.Str(c as Dictionary<string, object>, "type") == "arrow")
                        {
                            arrows.Add(c);
                        }
                    }
                }

                if (arrows.Count > 0)
                {
                    foreach (object a in arrows)
                    {
                        children.Remove(a);
                    }

                    row = new Dictionary<string, object> { { "type", "row" }, { "items", arrows }, { PerLaneKey, true } };
                    items.Insert(i + 1, row);
                    return row;
                }
            }

            row = new Dictionary<string, object> { { "type", "row" }, { "items", new List<object> { NewArrow(doc) } }, { PerLaneKey, true } };
            int at = items.Count;
            while (at > 0 && Json.Str(items[at - 1] as Dictionary<string, object>, "type") == "panel")
            {
                at--;
            }

            items.Insert(at, row);
            return row;
        }

        /// <summary>True if arrows are built into the sign's layout, each under what it points to, so lane arrows would repeat them.</summary>
        public static bool HasBuiltInArrows(Dictionary<string, object> doc)
        {
            List<object> items = Json.Arr(doc, "items");
            if (items == null)
            {
                return false;
            }

            foreach (object o in items)
            {
                var line = o as Dictionary<string, object>;
                List<object> parts = line != null && !IsArrows(line) ? Json.Arr(line, "items") : null;
                if (parts == null)
                {
                    continue;
                }

                foreach (object part in parts)
                {
                    if (HoldsArrow(part as Dictionary<string, object>))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HoldsArrow(Dictionary<string, object> item)
        {
            List<object> inner = item != null ? Json.Arr(item, "items") : null;
            if (inner == null)
            {
                return false;
            }

            foreach (object o in inner)
            {
                var child = o as Dictionary<string, object>;
                if (Json.Str(child, "type") == "arrow" || HoldsArrow(child))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsArrows(Dictionary<string, object> item)
        {
            if (item == null)
            {
                return false;
            }

            string type = Json.Str(item, "type");
            if (type == "arrow")
            {
                return true;
            }

            List<object> children = Json.Arr(item, "items");
            if (type != "row" || children == null || children.Count == 0)
            {
                return false;
            }

            foreach (object c in children)
            {
                var d = c as Dictionary<string, object>;
                if (d == null || Json.Str(d, "type") != "arrow")
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Works out a sign's placement; a sign not fitted, or with no lanes to fit to, stays as stored. Never throws on a bad design.</summary>
        public static GuidePlacement Resolve(GuideHead h, List<GantryLane> allLanes, float wantedHeight = 0f)
        {
            var free = new GuidePlacement { Design = h.Design, Along = h.Along, Scale = h.Scale, Source = h };
            List<GantryLane> lanes = h.FitFirst > 0 ? FaceLanes(allLanes, h.Side) : new List<GantryLane>();
            bool fit = lanes.Count > 0;

            Dictionary<string, object> doc;
            try
            {
                doc = Json.Parse(h.Design) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return free;
            }

            if (doc == null)
            {
                return free;
            }

            float customWidth = Json.Num(doc, WidthKey, 0f), customHeight = Json.Num(doc, HeightKey, 0f);
            bool square = Json.Has(doc, SquareKey);
            if (Json.Has(doc, CompactKey))
            {
                wantedHeight = 0f;
            }

            if (!fit && wantedHeight <= 0f && customWidth <= 0f && customHeight <= 0f)
            {
                return free;
            }

            int first = 0, last = 0;
            float along = h.Along, limit = 0f;
            if (fit)
            {
                first = Mathf.Clamp(h.FitFirst - 1, 0, lanes.Count - 1);
                last = Mathf.Clamp(h.FitLast - 1, first, lanes.Count - 1);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = first; i <= last; i++)
                {
                    lo = Mathf.Min(lo, lanes[i].Offset - lanes[i].Width * 0.5f);
                    hi = Mathf.Max(hi, lanes[i].Offset + lanes[i].Width * 0.5f);
                }

                along = (lo + hi) * 0.5f;
                limit = Mathf.Max(MinWidth, hi - lo - 2f * Inset);
            }

            // A square sign stays square over any lanes, unless the player gives it a width. Lane arrows need all their lanes.
            float laneWidth = limit;
            if (customWidth > 0f)
            {
                limit = fit && h.LaneArrows ? Mathf.Max(customWidth, laneWidth) : customWidth;
                doc.Remove(SquareKey);
                square = false;
            }
            else if (square || Json.Has(doc, OwnWidthKey))
            {
                limit = 0f;
            }

            if ((fit && !Json.Has(doc, OwnWidthKey)) || square || customWidth > 0f)
            {
                doc.Remove("minLanes");
                doc.Remove("minWidth");
                doc.Remove("width");
            }

            doc.Remove("height");
            GuideSign natural = GuideRenderer.Render(doc, 1024, 1f, true);
            float scale = h.Scale;
            float target = customHeight > 0f ? customHeight : wantedHeight;
            if (target > 0f)
            {
                float content = (natural.Height - natural.TabHeight) * MetresPerUnit * scale;
                if (content > target || square)
                {
                    scale = Mathf.Max(GuideData.MinScale, scale * target / content);
                }
            }

            // The legend keeps its chosen size where it fits; a sign that can't fit shrinks to the width it has.
            if (limit > 0f && natural.Width * MetresPerUnit * scale > limit)
            {
                scale = Mathf.Max(GuideData.MinScale, limit / (natural.Width * MetresPerUnit));
            }

            float m = MetresPerUnit * scale;
            if (limit > 0f)
            {
                doc["minWidth"] = Math.Round(limit / m, 1);
            }
            else if (wantedHeight > 0f && !square && !Json.Has(doc, "minLanes"))
            {
                doc["minWidth"] = Math.Round(Mathf.Max(Json.Num(doc, "minWidth", 0f), DefaultWidth / m), 1);
            }

            if (target > 0f)
            {
                doc["height"] = Math.Round(target / m, 1);
            }

            if (fit && !Json.Has(doc, "align"))
            {
                doc["align"] = "center";
            }

            if (fit && h.LaneArrows && !Json.Has(doc, OwnWidthKey))
            {
                // Sign x runs left to right as the driver sees it, the same way round as the lanes.
                float flip = h.Side < 0 ? 1f : -1f;
                int arrowsAt = LaneRowIndex(doc);
                PadLaneRow(doc, last - first + 1);
                List<object> own = arrowsAt >= 0 ? RowArrows(doc, arrowsAt) : null;
                var at = new List<object>();
                var edges = new List<object>();
                var arrows = new List<object>();
                for (int i = first; i <= last; i++)
                {
                    at.Add(Math.Round((lanes[i].Offset - along) * flip / m, 1));
                    float half = lanes[i].Width * 0.5f;
                    edges.Add(Math.Round((lanes[i].Offset - along) * flip / m - half / m, 1));
                    edges.Add(Math.Round((lanes[i].Offset - along) * flip / m + half / m, 1));
                    arrows.Add(own != null && i - first < own.Count ? own[i - first] : NewArrow(doc));
                }

                var row = new Dictionary<string, object> { { "type", "laneArrows" }, { "items", arrows }, { "at", at }, { "edges", edges } };
                string exitText = arrowsAt >= 0 ? Json.Str((Dictionary<string, object>)Json.Arr(doc, "items")[arrowsAt], ExitTextKey) : null;
                if (exitText != null)
                {
                    row[ExitTextKey] = exitText;
                }

                if (arrowsAt >= 0 && Json.Has((Dictionary<string, object>)Json.Arr(doc, "items")[arrowsAt], BarKey))
                {
                    row[BarKey] = true;
                }

                if (own != null && own.Count > 0)
                {
                    object height;
                    if (((Dictionary<string, object>)own[0]).TryGetValue("height", out height))
                    {
                        row["height"] = height;
                    }
                }

                List<object> items = Json.Arr(doc, "items");
                if (items == null)
                {
                    items = new List<object>();
                    doc["items"] = items;
                }

                if (arrowsAt >= 0)
                {
                    items[arrowsAt] = row;
                }
                else
                {
                    items.Add(row);
                }
            }

            return new GuidePlacement { Design = Json.Write(doc), Along = along, Scale = scale, Fitted = fit, Source = h };
        }

        /// <summary>
        /// Lays out all the signs on a gantry. Every sign starts from the default size, and the signs facing one way match
        /// the tallest of them, unless the player has set a height of their own.
        /// </summary>
        public static Dictionary<byte, GuidePlacement> Arrange(List<GuideHead> signs, List<GantryLane> lanes)
        {
            float minus = DefaultHeight, plus = DefaultHeight;
            foreach (GuideHead h in signs)
            {
                float panel = NaturalPanel(h);
                if (h.Side < 0)
                {
                    minus = Mathf.Max(minus, panel);
                }
                else
                {
                    plus = Mathf.Max(plus, panel);
                }
            }

            var placed = new Dictionary<byte, GuidePlacement>();
            foreach (GuideHead h in signs)
            {
                GuidePlacement p = Resolve(h, lanes, h.Side < 0 ? minus : plus);
                Measure(ref p);
                placed[h.Id] = p;
            }

            return placed;
        }

        /// <summary>How tall a sign's panel wants to be, in metres; 0 if the player has set its height, or it can't be read.</summary>
        private static float NaturalPanel(GuideHead h)
        {
            try
            {
                var doc = Json.Parse(h.Design) as Dictionary<string, object>;
                if (doc == null || Json.Num(doc, HeightKey, 0f) > 0f || Json.Has(doc, CompactKey))
                {
                    return 0f;
                }

                // A square sign is drawn to fit, so it follows the others and only its size slider can raise them.
                if (Json.Has(doc, SquareKey))
                {
                    return DefaultHeight * h.Scale;
                }

                doc.Remove("height");
                GuideSign s = GuideRenderer.Render(doc, 1024, 1f, true);
                return (s.Height - s.TabHeight) * MetresPerUnit * h.Scale;
            }
            catch (FormatException)
            {
                return 0f;
            }
        }

        private static void Measure(ref GuidePlacement p)
        {
            try
            {
                var doc = Json.Parse(p.Design) as Dictionary<string, object>;
                if (doc != null)
                {
                    GuideSign s = GuideRenderer.Render(doc, 1024, 1f, true);
                    float m = MetresPerUnit * p.Scale;
                    p.WidthMetres = s.Width * m;
                    p.HeightMetres = s.Height * m;
                    p.PanelMetres = (s.Height - s.TabHeight) * m;
                }
            }
            catch (FormatException)
            {
                p.WidthMetres = p.HeightMetres = p.PanelMetres = 0f;
            }
        }
    }
}
