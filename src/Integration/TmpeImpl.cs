using System;
using System.Runtime.CompilerServices;
using GantryTool.Detect;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.API.Notifier;
using TrafficManager.API.Traffic.Enums;

namespace GantryTool.Integration
{
    /// <summary>
    /// The only code touching TMPE.API. Every method is NoInlining so the JIT never pulls TMPE types into a
    /// caller before <see cref="Tmpe"/> has checked TM:PE is enabled.
    ///
    /// API facts (reference/TMPE, TM:PE build installed 2026-03): Implementations.ManagerFactory
    /// .VehicleRestrictionsManager.GetAllowedVehicleTypes(segment, info, laneIndex, laneInfo, mode) returns
    /// the allowed ExtVehicleType flags; restriction edits call NotifyStartEndNode, which raises
    /// Notifier.EventModified with InstanceID = the segment (via SimulationManager.AddAction).
    /// </summary>
    internal static class TmpeImpl
    {
        // Anything that drives on the road surface. Trams count: a car-banned lane with trams still has traffic.
        private const ExtVehicleType RoadTraffic = ExtVehicleType.RoadVehicle | ExtVehicleType.Trolleybus | ExtVehicleType.Tram;
        private const ExtVehicleType GeneralTraffic = ExtVehicleType.PassengerCar | ExtVehicleType.CargoTruck;
        private const ExtVehicleType Buses = ExtVehicleType.Bus | ExtVehicleType.Trolleybus;

        private static Action<ushort> _callback;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LaneRestriction GetRestriction(ushort segmentId, NetInfo info, int laneIndex, NetInfo.Lane laneInfo)
        {
            IVehicleRestrictionsManager vrm = Implementations.ManagerFactory.VehicleRestrictionsManager;
            ExtVehicleType allowed = vrm.GetAllowedVehicleTypes(segmentId, info, (uint)laneIndex, laneInfo,
                VehicleRestrictionsMode.Configured);

            if ((allowed & RoadTraffic) == ExtVehicleType.None)
            {
                return LaneRestriction.Banned;
            }

            // Bus-only: buses allowed, no private cars or trucks (taxis / emergency / service may still be).
            if ((allowed & GeneralTraffic) == ExtVehicleType.None && (allowed & Buses) != ExtVehicleType.None)
            {
                return LaneRestriction.Reserved;
            }

            return LaneRestriction.None;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Subscribe(Action<ushort> onSegmentModified)
        {
            _callback = onSegmentModified;
            Implementations.Notifier.EventModified += OnModified;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Unsubscribe(Action<ushort> onSegmentModified)
        {
            Implementations.Notifier.EventModified -= OnModified;
            _callback = null;
        }

        private static void OnModified(OnModifiedEventArgs args)
        {
            try
            {
                if (args != null && args.InstanceID.Type == InstanceType.NetSegment && _callback != null)
                {
                    _callback(args.InstanceID.NetSegment);
                }
            }
            catch (Exception e)
            {
                Log.Error("TM:PE EventModified handler failed", e);
            }
        }
    }
}
