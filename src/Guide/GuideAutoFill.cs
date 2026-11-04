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
                MatchCollection found = Regex.Matches(Name ?? "", @"(?<![\w-])\d{1,4}(?![\w-])");
                return found.Count > 0 ? found[found.Count - 1].Value : null;
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
        public const string PartKey = "auto";       // on a part: what it shows
        public const string PartShield = "shield";
        public const string PartDirection = "direction";
        public const string PartName = "name";
        public const string PartSecondName = "name2";

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
            if (roads == null || roads.Count == 0 || string.IsNullOrEmpty(roads[0].Name) || !Json.Has(doc, RoadKey))
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
            bool onShield = first.Number != null && HasPart(doc, PartShield);
            Write(doc, first, second, onShield);
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

        private static void Write(Dictionary<string, object> parent, RoadFacts road, string second, bool onShield)
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

                Write(item, road, second, onShield);
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
                            item["text"] = Cardinal(road.Heading);
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
