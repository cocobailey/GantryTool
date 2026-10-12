using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>Vehicle restriction on a lane, from TM:PE when it's enabled.</summary>
    public enum LaneRestriction : byte
    {
        None = 0,      // normal lane
        Banned = 1,    // no road vehicle allowed at all -> red X both faces
        Reserved = 2   // buses (no private cars / trucks) -> diamond where the green arrow would be
    }

    /// <summary>One car lane crossing under a gantry.</summary>
    public struct GantryLane
    {
        public ushort Segment;
        public uint LaneId;
        public int LaneIndex;

        /// <summary>Distance from the start post along the gantry (m).</summary>
        public float Offset;

        /// <summary>Lane surface height at the crossing (world y).</summary>
        public float SurfaceY;

        /// <summary>
        /// Travel direction across the gantry: +1 = towards the gantry's +side, -1 = towards -side,
        /// 0 = both directions (two-way single lane).
        /// </summary>
        public int TravelSign;

        public float Width;

        /// <summary>
        /// 0 for a crossing on the lane's own bezier; otherwise how far past the lane's end (into the node
        /// area) the straight extension reached the gantry line.
        /// </summary>
        public float Extension;

        /// <summary>TM:PE restriction of the lane this crossing belongs to (None without TM:PE).</summary>
        public LaneRestriction Restriction;

        /// <summary>Speed limit of the lane in km/h (TM:PE's if it's enabled, else the road's own).</summary>
        public float SpeedKmh;
    }

    /// <summary>
    /// Finds every car lane passing beneath a gantry by intersecting the gantry line (2D, XZ) with each
    /// lane's own bezier.
    ///
    /// Game facts relied on:
    /// - Segments are filed in the 270x270 / 64 m grid by their midpoint only (NetManager.cs:3636), so the
    ///   search area is padded.
    /// - Travel direction: effective = Invert ? InvertDirection(finalDirection) : finalDirection, and
    ///   Forward means travelling bezier a -> d (PathFind.cs:779, NetSegment.CountLanes). LHT is already
    ///   folded into m_finalDirection (NetInfo.cs:1101).
    /// - Tunnels: NetAI.BuildUnderground() (true for tunnels, false for open-cut slopes).
    ///
    /// Reads NetManager buffers from the main thread without locking, as vanilla rendering does.
    /// </summary>
    public static class LaneScanner
    {
        public static float GridPadding => Settings.Current.GridPadding; // covers long (Network Anarchy) segments
        public const float StackTolerance = 2f;      // lanes within this of the highest road get signals
        private const float ParallelCos = 0.9397f;   // cos(20°): reject lanes nearly parallel to the gantry
        private const int Samples = 16;              // bezier pieces before bisection refinement
        private const int BisectSteps = 12;
        private const float MaxExtension = 40f;      // longest lane extension into a node area (m)
        private const float DedupeOffset = 1.5f;     // crossings closer than this (same direction) are one lane

        public sealed class Result
        {
            public readonly List<GantryLane> Lanes = new List<GantryLane>();
            public float RoadTopY = float.NaN;
            public readonly List<string> Notes = new List<string>();

            public bool HasRoad => !float.IsNaN(RoadTopY);
        }

        private static readonly List<GantryLane> Candidates = new List<GantryLane>();
        private static readonly List<GantryLane> Kept = new List<GantryLane>();
        private static readonly HashSet<ushort> SeenSegments = new HashSet<ushort>();

        /// <summary>
        /// Scans the gantry between ground points <paramref name="start"/> and <paramref name="end"/>.
        /// <paramref name="verbose"/> collects human-readable notes for the log. Never throws.
        /// </summary>
        public static Result Scan(Vector3 start, Vector3 end, bool verbose)
        {
            var result = new Result();
            try
            {
                ScanInternal(start, end, verbose, result);
            }
            catch (Exception e)
            {
                Log.Error("Lane scan failed", e);
            }

            return result;
        }

        private static void ScanInternal(Vector3 start, Vector3 end, bool verbose, Result result)
        {
            Candidates.Clear();
            SeenSegments.Clear();

            Vector2 g0 = new Vector2(start.x, start.z);
            Vector2 g1 = new Vector2(end.x, end.z);
            Vector2 gd = g1 - g0;
            float span = gd.magnitude;
            if (span < 0.1f)
            {
                return;
            }

            Vector2 gDir = gd / span;
            Vector2 gSide = new Vector2(-gDir.y, gDir.x); // matches GantryMeshBuilder side = (-dir.z, 0, dir.x)

            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segments = nm.m_segments.m_buffer;
            NetLane[] lanes = nm.m_lanes.m_buffer;
            ushort[] grid = nm.m_segmentGrid;

            float minX = Mathf.Min(g0.x, g1.x), maxX = Mathf.Max(g0.x, g1.x);
            float minZ = Mathf.Min(g0.y, g1.y), maxZ = Mathf.Max(g0.y, g1.y);
            int cx0 = Mathf.Max((int)((minX - GridPadding) / 64f + 135f), 0);
            int cz0 = Mathf.Max((int)((minZ - GridPadding) / 64f + 135f), 0);
            int cx1 = Mathf.Min((int)((maxX + GridPadding) / 64f + 135f), 269);
            int cz1 = Mathf.Min((int)((maxZ + GridPadding) / 64f + 135f), 269);

            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    ushort segId = grid[cz * 270 + cx];
                    int guard = 0;
                    while (segId != 0)
                    {
                        ScanSegment(segId, ref segments[segId], lanes, g0, g1, gDir, gSide, span, minX, maxX, minZ, maxZ, verbose, result);
                        segId = segments[segId].m_nextGridSegment;
                        if (++guard >= NetManager.MAX_SEGMENT_COUNT)
                        {
                            Log.Error("Invalid segment grid list at cell " + cx + "," + cz);
                            break;
                        }
                    }
                }
            }

            if (Candidates.Count == 0)
            {
                return;
            }

            Dedupe();

            // Stacked roads: only the top layer gets signals.
            float top = float.MinValue;
            for (int i = 0; i < Candidates.Count; i++)
            {
                top = Mathf.Max(top, Candidates[i].SurfaceY);
            }

            result.RoadTopY = top;
            for (int i = 0; i < Candidates.Count; i++)
            {
                GantryLane l = Candidates[i];
                if (l.SurfaceY >= top - StackTolerance)
                {
                    result.Lanes.Add(l);
                }
                else if (verbose)
                {
                    result.Notes.Add("ignored seg " + l.Segment + " lane#" + l.LaneIndex + " at y=" +
                                     l.SurfaceY.ToString("F1") + " (below top road " + top.ToString("F1") + ")");
                }
            }

            result.Lanes.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        private static void ScanSegment(ushort segId, ref NetSegment seg, NetLane[] lanes,
            Vector2 g0, Vector2 g1, Vector2 gDir, Vector2 gSide, float span,
            float minX, float maxX, float minZ, float maxZ, bool verbose, Result result)
        {
            if ((seg.m_flags & (NetSegment.Flags.Created | NetSegment.Flags.Deleted)) != NetSegment.Flags.Created)
            {
                return;
            }

            if (!SeenSegments.Add(segId))
            {
                return;
            }

            // Cheap XZ bounds rejection before touching lanes.
            // Padded by MaxExtension: a gantry over a node area lies outside the segment's own bounds.
            Bounds b = seg.m_bounds;
            if (b.max.x < minX - MaxExtension || b.min.x > maxX + MaxExtension ||
                b.max.z < minZ - MaxExtension || b.min.z > maxZ + MaxExtension)
            {
                return;
            }

            NetNode[] nodes = Singleton<NetManager>.instance.m_nodes.m_buffer;
            Vector3 startNodePos = nodes[seg.m_startNode].m_position;
            Vector3 endNodePos = nodes[seg.m_endNode].m_position;

            NetInfo info = seg.Info;
            if (info == null || info.m_lanes == null || info.m_netAI == null)
            {
                return;
            }

            // Real tunnels only. Slopes also use RoadTunnelAI (IsUnderground() == true) but are open cuts
            // with flattened terrain; BuildUnderground() == !m_flattenTerrain tells them apart
            // (RoadTunnelAI.cs:62-70).
            if (info.m_netAI.BuildUnderground())
            {
                if (verbose && SegmentCrossesLine(seg, g0, g1))
                {
                    result.Notes.Add("skipped seg " + segId + " (" + info.name + ", tunnel)");
                }

                return;
            }

            bool invert = (seg.m_flags & NetSegment.Flags.Invert) != 0;
            uint laneId = seg.m_lanes;
            for (int i = 0; i < info.m_lanes.Length && laneId != 0; i++)
            {
                NetInfo.Lane laneInfo = info.m_lanes[i];
                if (IsCarLane(laneInfo))
                {
                    LaneRestriction restriction = Integration.Tmpe.GetRestriction(segId, info, i, laneInfo);
                    AddCrossings(segId, laneId, i, laneInfo, invert, restriction, ref lanes[laneId], startNodePos, endNodePos,
                        g0, gDir, gSide, span, verbose, result);
                }

                laneId = lanes[laneId].m_nextLane;
            }
        }

        /// <summary>The lane's speed limit in km/h: TM:PE's effective limit when TM:PE is enabled, else the road's.</summary>
        private static float LaneSpeedKmh(uint laneId, NetInfo.Lane laneInfo)
        {
            float tmpe = Integration.Tmpe.GetSpeedKmh(laneId, laneInfo);
            return float.IsNaN(tmpe) ? laneInfo.m_speedLimit * 50f : tmpe;
        }

        /// <summary>Vehicle or transport lane that carries cars.</summary>
        public static bool IsCarLane(NetInfo.Lane lane)
        {
            return (lane.m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) != 0 &&
                   (lane.m_vehicleType & VehicleInfo.VehicleType.Car) != 0;
        }

        private static void AddCrossings(ushort segId, uint laneId, int laneIndex, NetInfo.Lane laneInfo, bool invert, LaneRestriction restriction,
            ref NetLane lane, Vector3 startNodePos, Vector3 endNodePos,
            Vector2 g0, Vector2 gDir, Vector2 gSide, float span, bool verbose, Result result)
        {
            Bezier3 bez = lane.m_bezier;

            // Signed lateral distance of the bezier from the gantry line; a sign change is a crossing.
            float prevT = 0f;
            float prevF = SideDistance(bez.Position(0f), g0, gSide);
            for (int s = 1; s <= Samples; s++)
            {
                float t = (float)s / Samples;
                float f = SideDistance(bez.Position(t), g0, gSide);
                if ((prevF <= 0f && f > 0f) || (prevF >= 0f && f < 0f) || (f == 0f && s == Samples))
                {
                    float tc = Bisect(bez, g0, gSide, prevT, t, prevF);
                    TryAdd(segId, laneId, laneIndex, laneInfo, invert, restriction, bez.Position(tc), bez.Tangent(tc), 0f,
                        g0, gDir, gSide, span, verbose, result);
                }

                prevT = t;
                prevF = f;
            }

            // Node areas (junctions, lane-count transitions, splits) have no segment lanes: lanes end at the
            // node's corners, set back from the node. Extend each lane straight past both ends so a gantry
            // over the node still finds it. The lane on the other side of the node usually yields the same
            // crossing; Dedupe() keeps one.
            Vector3 endTan = bez.Tangent(1f);
            Vector3 startTan = bez.Tangent(0f);
            Extend(segId, laneId, laneIndex, laneInfo, invert, restriction, bez.d, endTan.normalized, endTan, endNodePos,
                g0, gDir, gSide, span, verbose, result);
            Extend(segId, laneId, laneIndex, laneInfo, invert, restriction, bez.a, -startTan.normalized, startTan, startNodePos,
                g0, gDir, gSide, span, verbose, result);
        }

        /// <summary>
        /// Straight extension from lane end <paramref name="p"/> along <paramref name="outDir"/> into the node
        /// area, limited to about twice the distance from the lane end to the node centre.
        /// </summary>
        private static void Extend(ushort segId, uint laneId, int laneIndex, NetInfo.Lane laneInfo, bool invert, LaneRestriction restriction,
            Vector3 p, Vector3 outDir, Vector3 tangentAtoD, Vector3 nodePos,
            Vector2 g0, Vector2 gDir, Vector2 gSide, float span, bool verbose, Result result)
        {
            if (outDir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float toNode = new Vector2(nodePos.x - p.x, nodePos.z - p.z).magnitude;
            float maxLen = Mathf.Min(2f * toNode + 1f, MaxExtension);

            float f0 = SideDistance(p, g0, gSide);
            float rate = outDir.x * gSide.x + outDir.z * gSide.y;
            if (Mathf.Abs(rate) < 1e-4f)
            {
                return;
            }

            float len = -f0 / rate;
            if (len <= 0.01f)
            {
                return;
            }

            if (len > maxLen)
            {
                if (verbose && len <= MaxExtension * 2f)
                {
                    Vector3 hit = p + outDir * len;
                    float along = Vector2.Dot(new Vector2(hit.x, hit.z) - g0, gDir);
                    if (along >= 0f && along <= span)
                    {
                        result.Notes.Add("seg " + segId + " lane#" + laneIndex + ": node extension would need " +
                                         len.ToString("F1") + "m past lane end (limit " + maxLen.ToString("F1") + "m)");
                    }
                }

                return;
            }

            TryAdd(segId, laneId, laneIndex, laneInfo, invert, restriction, p + outDir * len, tangentAtoD, len,
                g0, gDir, gSide, span, verbose, result);
        }

        /// <summary>Adds a crossing at <paramref name="p"/> if it is within the span and not near-parallel.</summary>
        private static void TryAdd(ushort segId, uint laneId, int laneIndex, NetInfo.Lane laneInfo, bool invert, LaneRestriction restriction,
            Vector3 p, Vector3 tangentAtoD, float extension,
            Vector2 g0, Vector2 gDir, Vector2 gSide, float span, bool verbose, Result result)
        {
            float along = Vector2.Dot(new Vector2(p.x, p.z) - g0, gDir);
            if (along < 0f || along > span)
            {
                return;
            }

            Vector2 tan = new Vector2(tangentAtoD.x, tangentAtoD.z);
            if (tan.sqrMagnitude <= 1e-6f)
            {
                return;
            }

            tan.Normalize();
            if (Mathf.Abs(Vector2.Dot(tan, gDir)) > ParallelCos)
            {
                if (verbose && extension == 0f)
                {
                    result.Notes.Add("ignored seg " + segId + " lane#" + laneIndex + " (runs within 20° of the gantry line)");
                }

                return;
            }

            Candidates.Add(new GantryLane
            {
                Segment = segId,
                LaneId = laneId,
                LaneIndex = laneIndex,
                Offset = along,
                SurfaceY = p.y,
                TravelSign = TravelSign(laneInfo, invert, tan, gSide),
                Width = laneInfo.m_width,
                Extension = extension,
                Restriction = restriction,
                SpeedKmh = LaneSpeedKmh(laneId, laneInfo)
            });
        }

        /// <summary>
        /// Merges crossings of the same physical lane (within <see cref="DedupeOffset"/> sideways and 2 m
        /// vertically, whatever their direction): a real bezier crossing beats an extension, a shorter extension beats
        /// a longer one.
        /// </summary>
        private static void Dedupe()
        {
            Candidates.Sort((a, b) => a.Extension.CompareTo(b.Extension)); // best first
            Kept.Clear();
            for (int i = 0; i < Candidates.Count; i++)
            {
                GantryLane c = Candidates[i];
                bool duplicate = false;
                for (int k = 0; k < Kept.Count; k++)
                {
                    GantryLane q = Kept[k];
                    // Direction is deliberately ignored: lanes are ~3 m wide, so two crossings closer than
                    // DedupeOffset are one physical lane (e.g. a node extension landing next to a real lane).
                    if (Mathf.Abs(q.Offset - c.Offset) < DedupeOffset &&
                        Mathf.Abs(q.SurfaceY - c.SurfaceY) < 2f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    Kept.Add(c);
                }
            }

            Candidates.Clear();
            Candidates.AddRange(Kept);
        }

        /// <summary>
        /// +1 if traffic in this lane moves towards the gantry's +side, -1 towards -side, 0 if both ways.
        /// </summary>
        private static int TravelSign(NetInfo.Lane laneInfo, bool invert, Vector2 tangentAtoD, Vector2 gSide)
        {
            NetInfo.Direction dir = invert ? NetInfo.InvertDirection(laneInfo.m_finalDirection) : laneInfo.m_finalDirection;
            dir &= NetInfo.Direction.Both;
            int bezierSign = Vector2.Dot(tangentAtoD, gSide) >= 0f ? 1 : -1;
            switch (dir)
            {
                case NetInfo.Direction.Forward:
                    return bezierSign;
                case NetInfo.Direction.Backward:
                    return -bezierSign;
                default:
                    return 0;
            }
        }

        private static float SideDistance(Vector3 p, Vector2 g0, Vector2 gSide)
        {
            return Vector2.Dot(new Vector2(p.x, p.z) - g0, gSide);
        }

        private static float Bisect(Bezier3 bez, Vector2 g0, Vector2 gSide, float t0, float t1, float f0)
        {
            for (int i = 0; i < BisectSteps; i++)
            {
                float tm = (t0 + t1) * 0.5f;
                float fm = SideDistance(bez.Position(tm), g0, gSide);
                if ((f0 <= 0f) == (fm <= 0f))
                {
                    t0 = tm;
                    f0 = fm;
                }
                else
                {
                    t1 = tm;
                }
            }

            return (t0 + t1) * 0.5f;
        }

        /// <summary>Rough check used only to decide whether a skipped tunnel is worth mentioning in the log.</summary>
        private static bool SegmentCrossesLine(NetSegment seg, Vector2 g0, Vector2 g1)
        {
            Bounds b = seg.m_bounds;
            return b.max.x >= Mathf.Min(g0.x, g1.x) && b.min.x <= Mathf.Max(g0.x, g1.x) &&
                   b.max.z >= Mathf.Min(g0.y, g1.y) && b.min.z <= Mathf.Max(g0.y, g1.y);
        }

        /// <summary>Multi-line description of a scan for the log.</summary>
        public static string Describe(int gantryId, Result r, float beamBottomY, Vector2 gSide)
        {
            var sb = new StringBuilder();
            if (r.Lanes.Count == 0)
            {
                sb.Append("Gantry #").Append(gantryId).Append(": no roads, beam y=").Append(beamBottomY.ToString("F1"));
            }
            else
            {
                sb.Append("Gantry #").Append(gantryId).Append(": ").Append(r.Lanes.Count).Append(" lanes, beam y=")
                  .Append(beamBottomY.ToString("F1")).Append(" (road top ").Append(r.RoadTopY.ToString("F1")).Append(')');
                for (int i = 0; i < r.Lanes.Count; i++)
                {
                    GantryLane l = r.Lanes[i];
                    sb.Append("\n  lane ").Append(i).Append(": seg ").Append(l.Segment).Append(" lane#").Append(l.LaneIndex)
                      .Append(" id=").Append(l.LaneId)
                      .Append(" at ").Append(l.Offset.ToString("F2")).Append("m")
                      .Append(" y=").Append(l.SurfaceY.ToString("F1"))
                      .Append(" w=").Append(l.Width.ToString("F1"))
                      .Append(" travel=").Append(TravelText(l.TravelSign, gSide));
                    if (l.Restriction != LaneRestriction.None)
                    {
                        sb.Append(" TM:PE=").Append(l.Restriction);
                    }

                    if (l.Extension > 0f)
                    {
                        sb.Append(" (via node, ").Append(l.Extension.ToString("F1")).Append("m past lane end)");
                    }
                }
            }

            for (int i = 0; i < r.Notes.Count; i++)
            {
                sb.Append("\n  note: ").Append(r.Notes[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// e.g. "+side (heading NE) -> green on face toward SW". Assumes +z = north, +x = east.
        /// </summary>
        public static string TravelText(int sign, Vector2 gSide)
        {
            if (sign == 0)
            {
                return "both ways";
            }

            Vector2 heading = gSide * sign;
            return (sign > 0 ? "+side" : "-side") + " (heading " + Compass(heading) + ") -> green on face toward " +
                   Compass(-heading);
        }

        public static string Compass(Vector2 v)
        {
            string[] names = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            int idx = Mathf.RoundToInt(angle / 45f);
            idx = ((idx % 8) + 8) % 8;
            return names[idx];
        }
    }
}
