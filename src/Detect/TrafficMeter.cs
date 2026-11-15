using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>
    /// How slow the traffic under a gantry is: how far below the speed limit its cars are driving, 0 (at the limit, or no
    /// cars) to 100 (stopped). The game's own traffic density only counts how much road the cars cover, so a busy road
    /// that's still moving reads as full; this looks at the cars' speeds, as real variable speed limits do.
    ///
    /// Measured for each road and each direction on it (see <see cref="Key"/>), so a queue one way on a two-way road
    /// doesn't lower the signs for the other way.
    /// </summary>
    public static class TrafficMeter
    {
        private const int MinCars = 3;          // fewer cars than this: too few to tell, treated as moving
        private const float CellSize = 32f;     // the vehicle grid's
        private const int GridSize = 540;

        private static readonly Dictionary<int, float> Sums = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> Counts = new Dictionary<int, int>();
        private static readonly Dictionary<uint, float> Limits = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, int> Keys = new Dictionary<uint, int>();
        private static readonly HashSet<ushort> Segments = new HashSet<ushort>();

        /// <summary>One direction of one road under a gantry.</summary>
        public static int Key(ushort segment, int travelSign)
        {
            return segment * 4 + Mathf.Clamp(travelSign, -1, 1) + 1;
        }

        /// <summary>The travel direction of a <see cref="Key"/>: +1, -1, or 0 for a lane used both ways.</summary>
        public static int TravelSign(int key)
        {
            return key % 4 - 1;
        }

        /// <summary>Fills <paramref name="slow"/> with the slowdown, 0-100, of each direction of each road under the gantry.</summary>
        public static void Measure(List<GantryLane> lanes, Dictionary<int, int> slow)
        {
            slow.Clear();
            Sums.Clear();
            Counts.Clear();
            Limits.Clear();
            Keys.Clear();
            Segments.Clear();
            if (lanes.Count == 0)
            {
                return;
            }

            NetManager net = Singleton<NetManager>.instance;
            VehicleManager vm = Singleton<VehicleManager>.instance;
            PathManager pm = Singleton<PathManager>.instance;
            Vehicle[] vehicles = vm.m_vehicles.m_buffer;
            ushort[] grid = vm.m_vehicleGrid;

            foreach (GantryLane lane in lanes)
            {
                // The lane's speed limit in the game's speed units (1 = 50 km/h, times 8).
                Limits[lane.LaneId] = Mathf.Max(lane.SpeedKmh, 10f) / 50f * 8f;
                int key = Key(lane.Segment, lane.TravelSign);
                Keys[lane.LaneId] = key;
                slow[key] = 0;
                Segments.Add(lane.Segment);
            }

            foreach (ushort segId in Segments)
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
                            Count(ref vehicles[id], segId, pm);
                            id = vehicles[id].m_nextGridVehicle;
                        }
                    }
                }
            }

            foreach (KeyValuePair<int, int> kv in Counts)
            {
                if (kv.Value >= MinCars)
                {
                    slow[kv.Key] = Mathf.RoundToInt(100f * (1f - Sums[kv.Key] / kv.Value));
                }
            }
        }

        private static void Count(ref Vehicle v, ushort segId, PathManager pm)
        {
            const Vehicle.Flags Driving = Vehicle.Flags.Created | Vehicle.Flags.Spawned;
            if ((v.m_flags & (Driving | Vehicle.Flags.Deleted)) != Driving || v.m_leadingVehicle != 0 || v.m_path == 0)
            {
                return;
            }

            VehicleInfo info = v.Info;
            if (info == null || info.m_vehicleType != VehicleInfo.VehicleType.Car)
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
            if (!Limits.TryGetValue(laneId, out limit))
            {
                return; // on the same road, but not in a lane under the gantry
            }

            float target = Mathf.Min(limit, info.m_maxSpeed);
            float rel = target > 0.1f ? Mathf.Clamp01(v.GetLastFrameVelocity().magnitude / target) : 1f;
            int key = Keys[laneId];
            float sum;
            int n;
            Sums[key] = (Sums.TryGetValue(key, out sum) ? sum : 0f) + rel;
            Counts[key] = (Counts.TryGetValue(key, out n) ? n : 0) + 1;
        }
    }
}
