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
        public const string PerLaneKey = "perLane";

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

        public static Dictionary<string, object> NewArrow()
        {
            return new Dictionary<string, object> { { "type", "arrow" }, { "direction", "down" }, { "height", 24.0 } };
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
                items.Add(new Dictionary<string, object>((Dictionary<string, object>)items[items.Count - 1]));
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

            row = new Dictionary<string, object> { { "type", "row" }, { "items", new List<object> { NewArrow() } }, { PerLaneKey, true } };
            int at = items.Count;
            while (at > 0 && Json.Str(items[at - 1] as Dictionary<string, object>, "type") == "panel")
            {
                at--;
            }

            items.Insert(at, row);
            return row;
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
        public static GuidePlacement Resolve(GuideHead h, List<GantryLane> allLanes)
        {
            var free = new GuidePlacement { Design = h.Design, Along = h.Along, Scale = h.Scale, Source = h };
            if (h.FitFirst == 0)
            {
                return free;
            }

            List<GantryLane> lanes = FaceLanes(allLanes, h.Side);
            if (lanes.Count == 0)
            {
                return free;
            }

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

            int first = Mathf.Clamp(h.FitFirst - 1, 0, lanes.Count - 1);
            int last = Mathf.Clamp(h.FitLast - 1, first, lanes.Count - 1);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = first; i <= last; i++)
            {
                lo = Mathf.Min(lo, lanes[i].Offset - lanes[i].Width * 0.5f);
                hi = Mathf.Max(hi, lanes[i].Offset + lanes[i].Width * 0.5f);
            }

            float along = (lo + hi) * 0.5f;
            float metres = Mathf.Max(MinWidth, hi - lo - 2f * Inset);

            // The legend keeps its chosen size where it fits; a sign that can't fit shrinks to the lanes.
            doc.Remove("minLanes");
            doc.Remove("minWidth");
            doc.Remove("width");
            float natural = GuideRenderer.Render(doc, 1024, 1f, true).Width;
            float scale = h.Scale;
            if (natural * MetresPerUnit * scale > metres)
            {
                scale = Mathf.Max(GuideData.MinScale, metres / (natural * MetresPerUnit));
            }

            float m = MetresPerUnit * scale;
            doc["minWidth"] = Math.Round(metres / m, 1);
            if (!Json.Has(doc, "align"))
            {
                doc["align"] = "center";
            }

            if (h.LaneArrows)
            {
                // Sign x runs left to right as the driver sees it, the same way round as the lanes.
                float flip = h.Side < 0 ? 1f : -1f;
                int arrowsAt = LaneRowIndex(doc);
                PadLaneRow(doc, last - first + 1);
                List<object> own = arrowsAt >= 0 ? RowArrows(doc, arrowsAt) : null;
                var at = new List<object>();
                var arrows = new List<object>();
                for (int i = first; i <= last; i++)
                {
                    at.Add(Math.Round((lanes[i].Offset - along) * flip / m, 1));
                    arrows.Add(own != null && i - first < own.Count ? own[i - first] : NewArrow());
                }

                var row = new Dictionary<string, object> { { "type", "laneArrows" }, { "items", arrows }, { "at", at } };
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

            return new GuidePlacement { Design = Json.Write(doc), Along = along, Scale = scale, Fitted = true, Source = h };
        }
    }
}
