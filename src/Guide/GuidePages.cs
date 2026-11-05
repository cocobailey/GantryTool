using System;
using System.Collections.Generic;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>
    /// A variable message sign's messages. The message being edited is the design's own items, as on any sign; the design
    /// also holds them all, and which one that is, so the sign can change between them by itself. A sign with one message
    /// that always shows has none of this in its design. Testable outside the game.
    /// </summary>
    public static class GuidePages
    {
        public const int MaxPages = 5;
        public const float MinSeconds = 2f, MaxSeconds = 60f, DefaultSeconds = 8f;
        public const string PagesKey = "pages", PageKey = "page", SecondsKey = "pageSeconds", WhenKey = "pageWhen", WhenBusy = "busy", BusyAtKey = "pageBusyAt";
        public const float DefaultBusyAt = 50f;

        /// <summary>True if a design might change by itself; quick, for looking through many.</summary>
        public static bool Changes(string design)
        {
            return design != null && (design.IndexOf("\"" + PagesKey + "\"", StringComparison.Ordinal) >= 0 ||
                                      design.IndexOf("\"" + WhenKey + "\"", StringComparison.Ordinal) >= 0);
        }

        public static int Count(Dictionary<string, object> doc)
        {
            List<object> pages = Json.Arr(doc, PagesKey);
            return pages != null && pages.Count > 0 ? pages.Count : 1;
        }

        /// <summary>Which message is being edited.</summary>
        public static int Current(Dictionary<string, object> doc)
        {
            return Mathf.Clamp((int)Json.Num(doc, PageKey, 0f), 0, Count(doc) - 1);
        }

        public static float Seconds(Dictionary<string, object> doc)
        {
            return Mathf.Clamp(Json.Num(doc, SecondsKey, DefaultSeconds), MinSeconds, MaxSeconds);
        }

        private static List<object> Items(Dictionary<string, object> doc, int page)
        {
            List<object> pages = Json.Arr(doc, PagesKey);
            if (pages == null || page == Current(doc) || page < 0 || page >= pages.Count)
            {
                return Json.Arr(doc, "items") ?? new List<object>();
            }

            return pages[page] as List<object> ?? new List<object>();
        }

        private static List<object> Copy(List<object> items)
        {
            return (List<object>)Json.Parse(Json.Write(items));
        }

        /// <summary>Starts editing another message.</summary>
        public static void SwitchTo(Dictionary<string, object> doc, int page)
        {
            List<object> pages = Json.Arr(doc, PagesKey);
            if (pages == null || page < 0 || page >= pages.Count || page == Current(doc))
            {
                return;
            }

            pages[Current(doc)] = Copy(Json.Arr(doc, "items") ?? new List<object>());
            doc["items"] = Copy(pages[page] as List<object> ?? new List<object>());
            doc[PageKey] = (double)page;
        }

        /// <summary>Adds a message after the last and starts editing it; null if there are already as many as a sign can have.</summary>
        public static Dictionary<string, object> Add(Dictionary<string, object> doc)
        {
            List<object> pages = Json.Arr(doc, PagesKey);
            if (pages == null)
            {
                pages = new List<object> { Copy(Json.Arr(doc, "items") ?? new List<object>()) };
                doc[PagesKey] = pages;
                doc[PageKey] = 0.0;
            }

            if (pages.Count >= MaxPages)
            {
                return null;
            }

            // New words in the same lettering as the message it follows.
            var line = new Dictionary<string, object> { { "type", "text" }, { "text", "NEW MESSAGE" } };
            Dictionary<string, object> like = FirstText(Json.Arr(doc, "items"));
            if (like != null)
            {
                foreach (string key in new[] { "height", "color", GuideRenderer.LedKey })
                {
                    object value;
                    if (like.TryGetValue(key, out value))
                    {
                        line[key] = value;
                    }
                }
            }

            pages[Current(doc)] = Copy(Json.Arr(doc, "items") ?? new List<object>());
            pages.Add(new List<object> { line });
            doc["items"] = new List<object> { line };
            doc[PageKey] = (double)(pages.Count - 1);
            return line;
        }

        private static Dictionary<string, object> FirstText(List<object> items)
        {
            if (items != null)
            {
                foreach (object o in items)
                {
                    var item = o as Dictionary<string, object>;
                    if (Json.Str(item, "type") == "text")
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        /// <summary>Takes off the message being edited and edits the one before it. The last one stays.</summary>
        public static void RemoveCurrent(Dictionary<string, object> doc)
        {
            List<object> pages = Json.Arr(doc, PagesKey);
            if (pages == null || pages.Count <= 1)
            {
                return;
            }

            int at = Current(doc);
            pages.RemoveAt(at);
            int next = Mathf.Max(0, at - 1);
            doc["items"] = Copy(pages[next] as List<object> ?? new List<object>());
            if (pages.Count == 1)
            {
                doc.Remove(PagesKey);
                doc.Remove(PageKey);
            }
            else
            {
                doc[PageKey] = (double)next;
            }
        }

        /// <summary>Which message shows at a time and the road's traffic density; -1 for none, a sign that only shows in heavy traffic when there isn't any.</summary>
        public static int PageAt(Dictionary<string, object> doc, float now, int density)
        {
            if (Json.Str(doc, WhenKey) == WhenBusy && density < Json.Num(doc, BusyAtKey, DefaultBusyAt))
            {
                return -1;
            }

            return (int)(now / Seconds(doc)) % Count(doc);
        }

        /// <summary>A design as it shows at one message, with no more changing in it.</summary>
        public static string DesignOf(Dictionary<string, object> doc, int page)
        {
            var one = new Dictionary<string, object>(doc);
            one["items"] = page < 0 ? new List<object>() : Items(doc, page);
            one.Remove(PagesKey);
            one.Remove(PageKey);
            one.Remove(SecondsKey);
            one.Remove(WhenKey);
            one.Remove(BusyAtKey);
            return Json.Write(one);
        }

        private static Dictionary<string, object> Read(string design)
        {
            try
            {
                return Json.Parse(design) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>What a design shows now; the message being edited while it's being edited. Designs that don't change come back as they are.</summary>
        public static string Shown(string design, float now, int density, bool editing)
        {
            if (!Changes(design))
            {
                return design;
            }

            Dictionary<string, object> doc = Read(design);
            return doc == null ? design : DesignOf(doc, editing ? Current(doc) : PageAt(doc, now, density));
        }

        /// <summary>Every way a design can look, so none of them is thrown away and redrawn on each turn.</summary>
        public static List<string> AllShown(string design)
        {
            var all = new List<string>();
            Dictionary<string, object> doc = Changes(design) ? Read(design) : null;
            if (doc != null)
            {
                for (int i = Json.Str(doc, WhenKey) == WhenBusy ? -1 : 0; i < Count(doc); i++)
                {
                    all.Add(DesignOf(doc, i));
                }
            }

            return all;
        }

        /// <summary>The same sign size for every message: as wide and as tall as the biggest needs, so the sign doesn't jump about.</summary>
        public static string Equalize(string design)
        {
            if (!Changes(design))
            {
                return design;
            }

            Dictionary<string, object> doc = Read(design);
            if (doc == null)
            {
                return design;
            }

            float width = 0f, panel = 0f;
            for (int i = 0; i < Count(doc); i++)
            {
                var one = (Dictionary<string, object>)Json.Parse(DesignOf(doc, i));
                GuideSign s = GuideRenderer.Render(one, 1024, 1f, true);
                width = Mathf.Max(width, s.Width);
                panel = Mathf.Max(panel, s.Height - s.TabHeight);
            }

            doc["minWidth"] = Math.Round((double)width, 1);
            doc["height"] = Math.Round((double)panel, 1);
            return Json.Write(doc);
        }
    }
}
