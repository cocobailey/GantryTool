using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>What a sign can say about a road.</summary>
    public sealed class RoadFacts
    {
        public string Name;
        public Vector2 Heading;     // zero if it goes both ways from here

        /// <summary>The road's route number, if its name has one ("Highway 90" but not "12th Street").</summary>
        public string Number
        {
            get
            {
                MatchCollection found = Regex.Matches(Name ?? "", @"(?<![\w-])[A-Za-z]{0,2}(\d{1,4})(?![\w-])");
                return found.Count > 0 ? found[found.Count - 1].Groups[1].Value : null;
            }
        }
    }

    /// <summary>
    /// Fills in the parts of a sign marked to take a road's details. It only ever runs when asked (a new sign, or the
    /// button): what it writes is ordinary text the player can change. Testable outside the game.
    /// </summary>
    public static class GuideAutoFill
    {
        public const string RoadKey = "autoRoad";   // on the sign: which road it's about
        public const string RoadHere = "here";      // the one under the gantry
        public const string RoadExit = "exit";      // the one the next exit leads to
        public const string RoadBoth = "both";      // parts of the sign say which of those they're about
        public const string WordsKey = "directionWords";
        public const string PartKey = "auto";       // on a part: what it shows
        public const string PartShield = "shield";
        public const string PartDirection = "direction";
        public const string PartName = "name";
        public const string PartSecondName = "name2";
        public const string FilledKey = "autoFilled"; // on the sign: its words came from the map, so it can follow the map
        public const string EditedKey = "autoEdited"; // on a part: the player wrote this themselves, so leave it be
        public const string PartSample = "sample";  // only there to show what the sign can look like; goes once it's filled in

        public static string Cardinal(Vector2 heading)
        {
            string[] names = { "EAST", "NORTH", "WEST", "SOUTH" };
            int i = Mathf.RoundToInt(Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg / 90f);
            return names[((i % 4) + 4) % 4];
        }

        /// <summary>
        /// Writes the first road (and a second destination, if there's one and room for it) into the design. A route
        /// number goes on the shield, and the name is then left off; with no number, the shield goes and the name stays.
        /// False if there's nothing to write, which leaves the design as it was.
        /// </summary>
        public static bool Fill(Dictionary<string, object> doc, List<RoadFacts> roads)
        {
            if (!Json.Has(doc, RoadKey) || Json.Str(doc, RoadKey) == RoadBoth)
            {
                return false;
            }

            bool filled = FillPart(doc, doc, roads);
            if (filled)
            {
                doc[FilledKey] = true;
            }

            return filled;
        }

        /// <summary>
        /// For a sign about two roads: each part of it marked with one gets that road. Sides with nothing to write stay as
        /// they were, and are counted in <paramref name="missing"/>. False if nothing was written at all.
        /// </summary>
        public static bool FillSections(Dictionary<string, object> doc, List<RoadFacts> here, List<RoadFacts> exit, out int missing)
        {
            missing = 0;
            int filled = 0;
            FillSections(doc, doc, here, exit, ref filled, ref missing);
            if (filled > 0)
            {
                doc[FilledKey] = true;
            }

            return filled > 0;
        }

        private static void FillSections(Dictionary<string, object> doc, Dictionary<string, object> parent, List<RoadFacts> here, List<RoadFacts> exit, ref int filled, ref int missing)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            foreach (object o in new List<object>(items))
            {
                var item = o as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                if (!Json.Has(item, RoadKey))
                {
                    FillSections(doc, item, here, exit, ref filled, ref missing);
                }
                else if (FillPart(doc, item, Json.Str(item, RoadKey) == RoadExit ? exit : here))
                {
                    filled++;
                }
                else
                {
                    missing++;
                }
            }
        }

        /// <summary>
        /// Brings a filled-in sign up to date with the map: what's on it follows the roads, except what the player wrote
        /// themselves. Nothing's added or taken off. Which roads to use is asked per part of the sign.
        /// </summary>
        public static void Refresh(Dictionary<string, object> doc, Func<string, List<RoadFacts>> roads)
        {
            string which = Json.Str(doc, RoadKey);
            if (which == null || !Json.Has(doc, FilledKey))
            {
                return;
            }

            if (which == RoadBoth)
            {
                RefreshSections(doc, doc, roads);
            }
            else
            {
                RefreshPart(doc, doc, roads(which));
            }
        }

        private static void RefreshSections(Dictionary<string, object> doc, Dictionary<string, object> parent, Func<string, List<RoadFacts>> roads)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            foreach (object o in items)
            {
                var item = o as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                if (Json.Has(item, RoadKey))
                {
                    RefreshPart(doc, item, roads(Json.Str(item, RoadKey)));
                }
                else
                {
                    RefreshSections(doc, item, roads);
                }
            }
        }

        private static void RefreshPart(Dictionary<string, object> doc, Dictionary<string, object> part, List<RoadFacts> roads)
        {
            if (roads == null || roads.Count == 0 || string.IsNullOrEmpty(roads[0].Name))
            {
                return;
            }

            string theme = Json.Str(doc, "theme", "us");
            if (CanWrite(theme, roads[0].Name))
            {
                string second = roads.Count > 1 && CanWrite(theme, roads[1].Name) ? roads[1].Name : null;
                Update(doc, part, roads[0], second);
            }
        }

        private static void Update(Dictionary<string, object> root, Dictionary<string, object> parent, RoadFacts road, string second)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            foreach (object o in items)
            {
                var item = o as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                Update(root, item, road, second);
                if (Json.Has(item, EditedKey))
                {
                    continue;
                }

                switch (Json.Str(item, PartKey))
                {
                    case PartShield:
                        if (road.Number != null)
                        {
                            item["number"] = road.Number;
                        }

                        break;
                    case PartDirection:
                        if (road.Heading != Vector2.zero)
                        {
                            item["text"] = Direction(root, road.Heading);
                        }

                        break;
                    case PartName:
                        item["text"] = road.Name;
                        break;
                    case PartSecondName:
                        if (second != null)
                        {
                            item["text"] = second;
                        }

                        break;
                }
            }
        }

        /// <summary>Asked to fill in from the road again: it follows the road again everywhere.</summary>
        public static void ForgetEdits(Dictionary<string, object> item)
        {
            item.Remove(EditedKey);
            List<object> items = Json.Arr(item, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    var child = o as Dictionary<string, object>;
                    if (child != null)
                    {
                        ForgetEdits(child);
                    }
                }
            }
        }

        private static bool FillPart(Dictionary<string, object> doc, Dictionary<string, object> part, List<RoadFacts> roads)
        {
            if (roads == null || roads.Count == 0 || string.IsNullOrEmpty(roads[0].Name))
            {
                return false;
            }

            string theme = Json.Str(doc, "theme", "us");
            if (!CanWrite(theme, roads[0].Name))
            {
                return false;   // a language the sign's letters don't cover
            }

            RoadFacts first = roads[0];
            string second = roads.Count > 1 && CanWrite(theme, roads[1].Name) ? roads[1].Name : null;
            bool onShield = first.Number != null && HasPart(part, PartShield);
            Write(doc, part, first, second, onShield);
            return true;
        }

        private static bool CanWrite(string theme, string text)
        {
            Dictionary<string, object> def = GuideAssets.Theme(theme);
            GlyphFont font = GuideAssets.Font(theme, Json.Str(Json.Obj(def, "defaults"), "font", "RG2014EM"));
            if (font == null || string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char c in text)
            {
                if (!font.Has(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPart(Dictionary<string, object> parent, string what)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    var item = o as Dictionary<string, object>;
                    if (item != null && (Json.Str(item, PartKey) == what || HasPart(item, what)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>A heading in the sign's own words, if it has some ("Est", "Nord", ...: east, north, west, south).</summary>
        private static string Direction(Dictionary<string, object> doc, Vector2 heading)
        {
            string english = Cardinal(heading);
            List<object> words = Json.Arr(doc, WordsKey);
            int i = Array.IndexOf(new[] { "EAST", "NORTH", "WEST", "SOUTH" }, english);
            return words != null && words.Count == 4 && words[i] is string ? (string)words[i] : english;
        }

        private static void Write(Dictionary<string, object> root, Dictionary<string, object> parent, RoadFacts road, string second, bool onShield)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i] as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                Write(root, item, road, second, onShield);
                bool keep = true;
                switch (Json.Str(item, PartKey))
                {
                    case PartShield:
                        keep = onShield;
                        if (keep)
                        {
                            item["number"] = road.Number;
                        }

                        break;
                    case PartDirection:
                        keep = road.Heading != Vector2.zero;
                        if (keep)
                        {
                            item["text"] = Direction(root, road.Heading);
                        }

                        break;
                    case PartName:
                        keep = !onShield;
                        if (keep)
                        {
                            item["text"] = road.Name;
                        }

                        break;
                    case PartSecondName:
                        keep = second != null;
                        if (keep)
                        {
                            item["text"] = second;
                        }

                        break;
                    case PartSample:
                        keep = false;
                        break;
                }

                // A row left with nothing in it goes too.
                List<object> inner = Json.Arr(item, "items");
                if (!keep || (inner != null && inner.Count == 0 && Json.Str(item, "type") != "laneArrows"))
                {
                    items.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>The road network as finding an exit needs it; the game's is in RoadFinder.</summary>
    public interface IRoadNetwork
    {
        /// <summary>The road segments that traffic can leave a node by.</summary>
        List<ushort> Out(ushort node);
        ushort OtherNode(ushort segment, ushort node);
        Vector3 Position(ushort node);
        string Name(ushort segment);
        bool SameName(ushort a, ushort b);
        bool IsRamp(ushort segment);
    }

    /// <summary>Follows a road ahead to the next place traffic can leave it, and on down any ramp to the roads it reaches.</summary>
    public static class RoadWalk
    {
        private const float MaxDistance = 2500f;    // further than this, it isn't what a sign here would be about
        private const int MaxSteps = 200;
        private const int MaxRampSteps = 40;

        /// <summary>The roads the next exit ahead leads to, at most two; empty if there isn't one in reach.</summary>
        public static List<RoadFacts> NextExit(IRoadNetwork net, ushort segment, ushort ahead)
        {
            var result = new List<RoadFacts>();
            ushort cur = segment, node = ahead;
            float travelled = 0f;
            for (int step = 0; step < MaxSteps && travelled < MaxDistance; step++)
            {
                List<ushort> outs = net.Out(node);
                outs.Remove(cur);
                if (outs.Count == 0)
                {
                    break;
                }

                ushort from = net.OtherNode(cur, node);
                Vector3 inDir = net.Position(node) - net.Position(from);
                ushort next = Continuation(net, cur, node, outs, inDir);
                foreach (ushort branch in outs)
                {
                    if (branch != next)
                    {
                        Destinations(net, branch, node, segment, result);
                    }
                }

                if (result.Count > 0)
                {
                    return result;
                }

                travelled += inDir.magnitude;
                node = net.OtherNode(next, node);
                cur = next;
            }

            return result;
        }

        /// <summary>The way the road itself goes on: the same name, or failing that the straightest.</summary>
        private static ushort Continuation(IRoadNetwork net, ushort cur, ushort node, List<ushort> outs, Vector3 inDir)
        {
            ushort best = outs[0];
            float bestDot = float.MinValue;
            foreach (ushort o in outs)
            {
                Vector3 d = (net.Position(net.OtherNode(o, node)) - net.Position(node)).normalized;
                float dot = Vector3.Dot(d, inDir.normalized) + (net.SameName(o, cur) ? 10f : 0f);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = o;
                }
            }

            return best;
        }

        private static void Destinations(IRoadNetwork net, ushort branch, ushort node, ushort road, List<RoadFacts> result)
        {
            var seen = new HashSet<ushort>();
            var ways = new Stack<KeyValuePair<ushort, ushort>>();
            ways.Push(new KeyValuePair<ushort, ushort>(branch, node));
            for (int step = 0; ways.Count > 0 && step < MaxRampSteps; step++)
            {
                KeyValuePair<ushort, ushort> way = ways.Pop();
                ushort seg = way.Key, from = way.Value;
                if (!seen.Add(seg) || net.SameName(seg, road))
                {
                    continue;
                }

                ushort to = net.OtherNode(seg, from);
                if (!net.IsRamp(seg))
                {
                    Vector3 d = net.Position(to) - net.Position(from);
                    AddRoad(result, net.Name(seg), new Vector2(d.x, d.z).normalized);
                    continue;
                }

                foreach (ushort o in net.Out(to))
                {
                    if (o != seg)
                    {
                        ways.Push(new KeyValuePair<ushort, ushort>(o, to));
                    }
                }
            }
        }

        private static void AddRoad(List<RoadFacts> result, string name, Vector2 heading)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            foreach (RoadFacts r in result)
            {
                if (r.Name == name)
                {
                    // The same road both ways, as where a ramp meets a street: no one direction to give.
                    if (Vector2.Dot(r.Heading, heading) < 0f)
                    {
                        r.Heading = Vector2.zero;
                    }

                    return;
                }
            }

            if (result.Count < 2)
            {
                result.Add(new RoadFacts { Name = name, Heading = heading });
            }
        }
    }
}
