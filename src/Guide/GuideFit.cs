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

            int arrowsAt = -1;
            string direction = "down";
            object arrowHeight = null;
            if (h.LaneArrows)
            {
                arrowsAt = TakeArrows(doc, ref direction, ref arrowHeight);
            }

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
                var at = new List<object>();
                for (int i = first; i <= last; i++)
                {
                    at.Add(Math.Round((lanes[i].Offset - along) * flip / m, 1));
                }

                var row = new Dictionary<string, object> { { "type", "laneArrows" }, { "direction", direction }, { "at", at } };
                if (arrowHeight != null)
                {
                    row["height"] = arrowHeight;
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

        /// <summary>
        /// Blanks out the rows of arrows, keeping the first one's look; returns where the first was, or -1. They stay as
        /// parts that draw nothing, so everything else in the design keeps its place for the editor.
        /// </summary>
        private static int TakeArrows(Dictionary<string, object> doc, ref string direction, ref object height)
        {
            List<object> items = Json.Arr(doc, "items");
            int where = -1;
            for (int i = items == null ? -1 : items.Count - 1; i >= 0; i--)
            {
                var item = items[i] as Dictionary<string, object>;
                if (!IsArrows(item))
                {
                    continue;
                }

                Dictionary<string, object> arrow = item;
                if (Json.Str(item, "type") == "row")
                {
                    arrow = (Dictionary<string, object>)Json.Arr(item, "items")[0];
                }

                direction = Json.Str(arrow, "direction", "down");
                object value;
                height = arrow.TryGetValue("height", out value) ? value : null;
                items[i] = new Dictionary<string, object> { { "type", "none" } };
                where = i;
            }

            return where;
        }
    }
}
