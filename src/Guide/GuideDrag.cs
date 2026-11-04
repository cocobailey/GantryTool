using System.Collections.Generic;

namespace GantryTool.Guide
{
    public enum DropSide
    {
        Above,
        Below,
        Left,
        Right
    }

    /// <summary>Where a dragged part would land: beside or around another part, named by its path in the design.</summary>
    public struct GuideDrop
    {
        public string Path;
        public DropSide Side;
        public float X, Y, W, H;   // the part it lands against, sign units
    }

    /// <summary>
    /// Dragging parts around on the sign's picture. A sign lays itself out, so a part doesn't go to an exact spot: it snaps
    /// above, below or beside another part, and the layout does the rest. Testable outside the game.
    /// </summary>
    public static class GuideDrag
    {
        private const float SideShare = 0.3f;   // how much of a part's width at each end means "beside it"

        private static bool Holds(string type)
        {
            return type == "row" || type == "panel" || type == "chip" || type == "tab";
        }

        /// <summary>The part at a path like "2/0", or null.</summary>
        public static Dictionary<string, object> ItemAt(Dictionary<string, object> doc, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Dictionary<string, object> at = doc;
            foreach (string step in path.Split('/'))
            {
                int i;
                List<object> items = Json.Arr(at, "items");
                if (items == null || !int.TryParse(step, out i) || i < 0 || i >= items.Count)
                {
                    return null;
                }

                at = items[i] as Dictionary<string, object>;
                if (at == null)
                {
                    return null;
                }
            }

            return at;
        }

        private static Dictionary<string, object> ParentOf(Dictionary<string, object> parent, Dictionary<string, object> item)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return null;
            }

            foreach (object o in items)
            {
                if (o == item)
                {
                    return parent;
                }

                var child = o as Dictionary<string, object>;
                Dictionary<string, object> found = child != null ? ParentOf(child, item) : null;
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// What dragging the part at a path really moves: text on a plate brings its plate; parts of a tab, a band or the
        /// lane arrows stay put (null).
        /// </summary>
        public static Dictionary<string, object> Movable(Dictionary<string, object> doc, string path, int laneRow)
        {
            Dictionary<string, object> item = ItemAt(doc, path);
            if (item == null)
            {
                return null;
            }

            string[] steps = path.Split('/');
            int line;
            if (int.TryParse(steps[0], out line) && line == laneRow)
            {
                return null;
            }

            Dictionary<string, object> parent = ParentOf(doc, item);
            string parentType = Json.Str(parent, "type");
            if (parentType == "chip")
            {
                return parent;
            }

            string type = Json.Str(item, "type");
            if (parentType == "tab" || parentType == "panel" || type == "tab" || type == "panel")
            {
                return null;
            }

            return type == "text" || type == "shield" || type == "arrow" || type == "symbol" || type == "chip" ? item : null;
        }

        /// <summary>Where the part being dragged would land with the pointer at (x, y) in sign units; false if nowhere.</summary>
        public static bool Find(Dictionary<string, object> doc, List<GuidePart> parts, Dictionary<string, object> moving, float x, float y, out GuideDrop drop)
        {
            drop = default(GuideDrop);
            float best = float.MaxValue;
            bool found = false;
            foreach (GuidePart p in parts)
            {
                Dictionary<string, object> item = ItemAt(doc, p.Path);
                if (item == null || item == moving || Inside(doc, item, moving) || p.Type == "tab" || p.W <= 0f || p.H <= 0f)
                {
                    continue;
                }

                string parentType = Json.Str(ParentOf(doc, item), "type");
                if (parentType == "tab" || parentType == "panel" || parentType == "chip" || parentType == "laneArrows")
                {
                    continue;
                }

                // A lane arrow's part is named through its row in the design: only the whole row counts.
                if (p.Type == "arrow" && p.Path.Contains("/") && Json.Arr(item, "items") == null && IsLaneArrow(parts, p.Path))
                {
                    continue;
                }

                float dx = x < p.X ? p.X - x : x > p.X + p.W ? x - p.X - p.W : 0f;
                float dy = y < p.Y ? p.Y - y : y > p.Y + p.H ? y - p.Y - p.H : 0f;
                float distance = dx * dx + dy * dy;
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                found = true;
                bool level = p.Type != "panel" && p.Type != "laneArrows";
                DropSide side;
                if (level && x < p.X + p.W * SideShare)
                {
                    side = DropSide.Left;
                }
                else if (level && x > p.X + p.W * (1f - SideShare))
                {
                    side = DropSide.Right;
                }
                else
                {
                    side = y < p.Y + p.H * 0.5f ? DropSide.Above : DropSide.Below;
                }

                drop = new GuideDrop { Path = p.Path, Side = side, X = p.X, Y = p.Y, W = p.W, H = p.H };
            }

            return found;
        }

        private static bool IsLaneArrow(List<GuidePart> parts, string path)
        {
            string parent = path.Substring(0, path.LastIndexOf('/'));
            foreach (GuidePart p in parts)
            {
                if (p.Path == parent && p.Type == "laneArrows")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Inside(Dictionary<string, object> doc, Dictionary<string, object> item, Dictionary<string, object> container)
        {
            for (Dictionary<string, object> up = ParentOf(doc, item); up != null && up != doc; up = ParentOf(doc, up))
            {
                if (up == container)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Moves a part to where it was dropped; false if that changes nothing or can't be done.</summary>
        public static bool Apply(Dictionary<string, object> doc, Dictionary<string, object> moving, GuideDrop drop)
        {
            Dictionary<string, object> target = ItemAt(doc, drop.Path);
            Dictionary<string, object> from = ParentOf(doc, moving);
            if (target == null || from == null || target == moving || Inside(doc, target, moving))
            {
                return false;
            }

            Json.Arr(from, "items").Remove(moving);
            Dictionary<string, object> parent = ParentOf(doc, target);
            bool vertical = drop.Side == DropSide.Above || drop.Side == DropSide.Below;
            bool after = drop.Side == DropSide.Below || drop.Side == DropSide.Right;

            // Above or below something in a row means above or below the whole row.
            while (vertical && parent != doc && Holds(Json.Str(parent, "type")))
            {
                target = parent;
                parent = ParentOf(doc, target);
            }

            List<object> list = Json.Arr(parent, "items");
            int at = list.IndexOf(target);
            if (vertical || (parent != doc && Holds(Json.Str(parent, "type"))))
            {
                list.Insert(after ? at + 1 : at, moving);
                return true;
            }

            // Beside a line of its own: the two become a row.
            list[at] = new Dictionary<string, object>
            {
                { "type", "row" },
                { "items", new List<object> { after ? target : moving, after ? moving : target } }
            };
            return true;
        }
    }
}
