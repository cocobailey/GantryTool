using System;
using System.Collections.Generic;
using ColossalFramework;
using GantryTool.Data;
using GantryTool.Guide;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>Works out which road a sign on a gantry is about, from the game's road network. Main thread.</summary>
    public static class RoadFinder
    {
        private sealed class GameNetwork : IRoadNetwork
        {
            public List<ushort> Out(ushort node)
            {
                NetManager nm = Singleton<NetManager>.instance;
                var list = new List<ushort>();
                for (int i = 0; i < 8; i++)
                {
                    ushort s = nm.m_nodes.m_buffer[node].GetSegment(i);
                    if (s != 0 && LeavesBy(ref nm.m_segments.m_buffer[s], node))
                    {
                        list.Add(s);
                    }
                }

                return list;
            }

            private static bool LeavesBy(ref NetSegment seg, ushort node)
            {
                NetInfo info = seg.Info;
                if (info == null || !(info.m_netAI is RoadBaseAI))
                {
                    return false;
                }

                bool invert = (seg.m_flags & NetSegment.Flags.Invert) != 0;
                return (seg.m_startNode == node) != invert ? info.m_forwardVehicleLaneCount > 0 : info.m_backwardVehicleLaneCount > 0;
            }

            public ushort OtherNode(ushort segment, ushort node)
            {
                NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
                return segs[segment].m_startNode == node ? segs[segment].m_endNode : segs[segment].m_startNode;
            }

            public Vector3 Position(ushort node)
            {
                return Singleton<NetManager>.instance.m_nodes.m_buffer[node].m_position;
            }

            public string Name(ushort segment)
            {
                return Singleton<NetManager>.instance.GetSegmentName(segment);
            }

            public bool SameName(ushort a, ushort b)
            {
                return a == b || Singleton<NetManager>.instance.IsSameName(a, b);
            }

            public bool IsRamp(ushort segment)
            {
                NetInfo info = Singleton<NetManager>.instance.m_segments.m_buffer[segment].Info;
                return info != null && info.name != null && info.name.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>The roads a sign like this should name; null if it doesn't say, or none can be found.</summary>
        public static List<RoadFacts> Find(Gantry g, GuideHead sign, Dictionary<string, object> design)
        {
            return Find(g, sign, Json.Str(design, GuideAutoFill.RoadKey));
        }

        /// <summary>Writes the roads a sign is about into its design. False if nothing changed; "partly" if one side stayed as it was.</summary>
        public static bool Fill(Gantry g, GuideHead sign, Dictionary<string, object> design, out bool partly)
        {
            partly = false;
            GuideAutoFill.ForgetEdits(design);
            string which = Json.Str(design, GuideAutoFill.RoadKey);
            if (which != GuideAutoFill.RoadBoth)
            {
                return GuideAutoFill.Fill(design, Find(g, sign, which));
            }

            int missing;
            bool filled = GuideAutoFill.FillSections(design, Find(g, sign, GuideAutoFill.RoadHere), Find(g, sign, GuideAutoFill.RoadExit), out missing);
            partly = filled && missing > 0;
            return filled;
        }

        /// <summary>Brings a sign's design up to date with the map. True if that changed anything.</summary>
        public static bool Refresh(Gantry g, GuideHead sign, Dictionary<string, object> design)
        {
            string before = Json.Write(design);
            var found = new Dictionary<string, List<RoadFacts>>();
            GuideAutoFill.Refresh(design, which =>
            {
                List<RoadFacts> roads;
                if (!found.TryGetValue(which, out roads))
                {
                    roads = Find(g, sign, which);
                    found[which] = roads;
                }

                return roads;
            });
            return Json.Write(design) != before;
        }

        /// <summary>The way traffic under this side of a gantry is going.</summary>
        public static Vector2 Heading(Gantry g, sbyte side)
        {
            Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z).normalized;
            return new Vector2(-d.y, d.x) * -side;
        }

        /// <summary>The name of the road under a sign; null if there's no named road there.</summary>
        public static string RoadName(Gantry g, GuideHead sign)
        {
            List<GantryLane> lanes;
            lock (GantryManager.Lock)
            {
                lanes = GuideFit.FaceLanes(g.Lanes, sign.Side);
            }

            if (lanes.Count == 0)
            {
                return null;
            }

            string name = new GameNetwork().Name(LaneUnder(lanes, sign).Segment);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>Another gantry over the same road, the same way: how far ahead of the first it is (behind it if negative), and which side faces that traffic.</summary>
        public struct RoadGantry
        {
            public Gantry Gantry;
            public sbyte Side;
            public float Ahead;
        }

        /// <summary>The other gantries over the road under a sign, for traffic going the same way.</summary>
        public static List<RoadGantry> SameRoad(Gantry g, GuideHead sign)
        {
            var result = new List<RoadGantry>();
            List<GantryLane> own;
            lock (GantryManager.Lock)
            {
                own = GuideFit.FaceLanes(g.Lanes, sign.Side);
            }

            if (own.Count == 0 || string.IsNullOrEmpty(new GameNetwork().Name(LaneUnder(own, sign).Segment)))
            {
                return result;
            }

            ushort road = LaneUnder(own, sign).Segment;
            var net = new GameNetwork();
            Vector2 heading = Heading(g, sign.Side);
            Vector3 here = (g.Start + g.End) * 0.5f;
            var all = new List<Gantry>();
            GantryManager.GetAll(all);
            foreach (Gantry other in all)
            {
                if (other == g)
                {
                    continue;
                }

                foreach (sbyte side in new sbyte[] { -1, 1 })
                {
                    if (Vector2.Dot(Heading(other, side), heading) < 0.3f)
                    {
                        continue;
                    }

                    List<GantryLane> lanes;
                    lock (GantryManager.Lock)
                    {
                        lanes = GuideFit.FaceLanes(other.Lanes, side);
                    }

                    foreach (GantryLane lane in lanes)
                    {
                        if (net.SameName(lane.Segment, road))
                        {
                            Vector3 there = (other.Start + other.End) * 0.5f - here;
                            result.Add(new RoadGantry { Gantry = other, Side = side, Ahead = Vector2.Dot(new Vector2(there.x, there.z), heading) });
                            break;
                        }
                    }
                }
            }

            return result;
        }

        private static List<RoadFacts> Find(Gantry g, GuideHead sign, string which)
        {
            if (which == null)
            {
                return null;
            }

            List<GantryLane> lanes;
            lock (GantryManager.Lock)
            {
                lanes = GuideFit.FaceLanes(g.Lanes, sign.Side);
            }

            if (lanes.Count == 0)
            {
                return null;
            }

            GantryLane lane = LaneUnder(lanes, sign);
            var net = new GameNetwork();
            Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z).normalized;
            Vector2 heading = new Vector2(-d.y, d.x) * -sign.Side;

            List<RoadFacts> found;
            if (which == GuideAutoFill.RoadExit)
            {
                NetSegment seg = Singleton<NetManager>.instance.m_segments.m_buffer[lane.Segment];
                Vector3 a = net.Position(seg.m_startNode), b = net.Position(seg.m_endNode);
                ushort ahead = Vector2.Dot(new Vector2(b.x - a.x, b.z - a.z), heading) >= 0f ? seg.m_endNode : seg.m_startNode;
                found = RoadWalk.NextExit(net, lane.Segment, ahead);
            }
            else
            {
                found = new List<RoadFacts> { new RoadFacts { Name = net.Name(lane.Segment), Heading = heading } };
            }

            Log.Debug("Road for a guide sign (" + which + "): " + (found.Count > 0 ? found[0].Name : "none found") + (found.Count > 1 ? ", " + found[1].Name : ""));
            return found;
        }

        private static GantryLane LaneUnder(List<GantryLane> lanes, GuideHead sign)
        {
            if (sign.FitFirst > 0)
            {
                int first = Mathf.Clamp(sign.FitFirst - 1, 0, lanes.Count - 1);
                int last = Mathf.Clamp(sign.FitLast - 1, first, lanes.Count - 1);
                return lanes[(first + last) / 2];
            }

            GantryLane best = lanes[0];
            foreach (GantryLane l in lanes)
            {
                if (Mathf.Abs(l.Offset - sign.Along) < Mathf.Abs(best.Offset - sign.Along))
                {
                    best = l;
                }
            }

            return best;
        }
    }
}
