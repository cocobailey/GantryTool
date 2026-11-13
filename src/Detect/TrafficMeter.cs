using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>
    /// How slow the traffic under a gantry is: how far below the speed limit its cars are driving, 0 (at the limit, or no
    /// cars) to 100 (stopped). The game's own traffic density only counts how much road the cars cover, so a busy road
    /// that's still moving reads as full; this looks at the cars' speeds, as real variable speed limits do.
    /// </summary>
    public static class TrafficMeter
    {
        private const int MinCars = 3;          // fewer cars than this on a road: too few to tell, treated as moving
        private const float CellSize = 32f;     // the vehicle grid's
        private const int GridSize = 540;

        private static readonly Dictionary<ushort, float> Sums = new Dictionary<ushort, float>();
        private static readonly Dictionary<ushort, int> Counts = new Dictionary<ushort, int>();

        /// <summary>Fills <paramref name="slow"/> with each road under the gantry's slowdown, 0-100.</summary>
        public static void Measure(List<GantryLane> lanes, Dictionary<ushort, int> slow)
        {
            slow.Clear();
            Sums.Clear();
            Counts.Clear();
            if (lanes.Count == 0)
            {
                return;
            }

            NetManager net = Singleton<NetManager>.instance;
            VehicleManager vm = Singleton<VehicleManager>.instance;
            PathManager pm = Singleton<PathManager>.instance;
            Vehicle[] vehicles = vm.m_vehicles.m_buffer;
            ushort[] grid = vm.m_vehicleGrid;

            // The speed limit of each lane under the gantry, in the game's speed units (1 = 50 km/h, times 8).
            var limits = new Dictionary<uint, float>();
            var segments = new HashSet<ushort>();
            foreach (GantryLane lane in lanes)
            {
                limits[lane.LaneId] = Mathf.Max(lane.SpeedKmh, 10f) / 50f * 8f;
                segments.Add(lane.Segment);
            }

            foreach (ushort segId in segments)
            {
                Bounds b = net.m_segments.m_buffer[segId].m_bounds;
                int x0 = Mathf.Clamp((int)(b.min.x / CellSize + GridSize / 2), 0, GridSize - 1);
                int z0 = Mathf.Clamp((int)(b.min.z / CellSize + GridSize / 2), 0, GridSize - 1);
                int x1 = Mathf.Clamp((int)(b.max.x / CellSize + GridSize / 2), 0, GridSize - 1);
                int z1 = Mathf.Clamp((int)(b.max.z / CellSize + GridSize / 2), 0, GridSize - 1);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        ushort id = grid[z * GridSize + x];
                        int guard = 0;
                        while (id != 0 && ++guard < 65536)
                        {
                            Count(ref vehicles[id], segId, net, pm, limits);
                            id = vehicles[id].m_nextGridVehicle;
                        }
                    }
                }
            }

            foreach (ushort segId in segments)
            {
                int n;
                float sum;
                bool enough = Counts.TryGetValue(segId, out n) && n >= MinCars && Sums.TryGetValue(segId, out sum);
                slow[segId] = enough ? Mathf.RoundToInt(100f * (1f - Sums[segId] / n)) : 0;
            }
        }

        private static void Count(ref Vehicle v, ushort segId, NetManager net, PathManager pm, Dictionary<uint, float> limits)
        {
            VehicleInfo info = v.Info;
            if ((v.m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Deleted | Vehicle.Flags.Spawned)) != (Vehicle.Flags.Created | Vehicle.Flags.Spawned)
                || v.m_leadingVehicle != 0 || v.m_path == 0 || info == null || info.m_vehicleType != VehicleInfo.VehicleType.Car)
            {
                return;
            }

            PathUnit.Position pos;
            if (!pm.m_pathUnits.m_buffer[v.m_path].GetPosition(v.m_pathPositionIndex >> 1, out pos) || pos.m_segment != segId)
            {
                return;
            }

            uint laneId = PathManager.GetLaneID(pos);
            float limit;
            if (!limits.TryGetValue(laneId, out limit))
            {
                return; // on the same road, but not in a lane under the gantry (the other direction, say)
            }

            float target = Mathf.Min(limit, info.m_maxSpeed);
            float rel = target > 0.1f ? Mathf.Clamp01(v.GetLastFrameVelocity().magnitude / target) : 1f;
            float sum;
            int n;
            Sums[segId] = (Sums.TryGetValue(segId, out sum) ? sum : 0f) + rel;
            Counts[segId] = (Counts.TryGetValue(segId, out n) ? n : 0) + 1;
        }
    }
}
