using System;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
using GantryTool.Data;
using GantryTool.Detect;

namespace GantryTool.Integration
{
    /// <summary>
    /// Optional TM:PE integration, safe to call whether or not TM:PE is installed. This class never
    /// mentions TMPE.API types; everything that does lives in <see cref="TmpeImpl"/>, which is only
    /// JIT-compiled after we've confirmed TM:PE is enabled, so a missing TMPE.API.dll can't break loading.
    /// </summary>
    public static class Tmpe
    {
        private static bool _active;
        private static bool _subscribed;
        private static bool _errorLogged;

        /// <summary>True while TM:PE is enabled, the option is on, and the bridge hasn't failed.</summary>
        public static bool Active => _active && Settings.Current.UseTmpeRestrictions;

        /// <summary>Called on level load: detects TM:PE and subscribes to its change events.</summary>
        public static void Init()
        {
            _active = false;
            _errorLogged = false;
            try
            {
                if (!IsTmpeEnabled())
                {
                    Log.Info("TM:PE not enabled; lane restriction signals off");
                    return;
                }

                TmpeImpl.Subscribe(OnSegmentModified);
                _subscribed = true;
                _active = true;
                Log.Info("TM:PE detected; banned lanes show red X, bus-only lanes show diamond");
            }
            catch (Exception e)
            {
                Log.Error("TM:PE integration failed to start; continuing without it", e);
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (_subscribed)
                {
                    TmpeImpl.Unsubscribe(OnSegmentModified);
                }
            }
            catch (Exception e)
            {
                Log.Error("TM:PE unsubscribe failed", e);
            }

            _subscribed = false;
            _active = false;
        }

        /// <summary>
        /// The lane's effective speed limit in km/h per TM:PE, or NaN when TM:PE isn't enabled (then the caller uses the
        /// road's own limit). Independent of the restrictions option. Main thread.
        /// </summary>
        public static float GetSpeedKmh(uint laneId, NetInfo.Lane laneInfo)
        {
            if (!_active)
            {
                return float.NaN;
            }

            try
            {
                return TmpeImpl.GetSpeedLimit(laneId, laneInfo) * 50f;
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("TM:PE speed limit lookup failed; disabling TM:PE integration", e);
                }

                _active = false;
                return float.NaN;
            }
        }

        /// <summary>Restriction of one lane per TM:PE (None when TM:PE is off). Main thread.</summary>
        public static LaneRestriction GetRestriction(ushort segmentId, NetInfo info, int laneIndex, NetInfo.Lane laneInfo)
        {
            if (!Active)
            {
                return LaneRestriction.None;
            }

            try
            {
                return TmpeImpl.GetRestriction(segmentId, info, laneIndex, laneInfo);
            }
            catch (Exception e)
            {
                // A TM:PE API change shouldn't take gantries down with it: disable and keep going.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("TM:PE restriction lookup failed; disabling TM:PE integration", e);
                }

                _active = false;
                return LaneRestriction.None;
            }
        }

        /// <summary>TM:PE change event (fired on the simulation thread). Enqueue only.</summary>
        private static void OnSegmentModified(ushort segmentId)
        {
            try
            {
                if (!Active || segmentId == 0)
                {
                    return;
                }

                NetSegment[] buffer = Singleton<NetManager>.instance.m_segments.m_buffer;
                if (buffer[segmentId].m_flags != NetSegment.Flags.None)
                {
                    GantryUpdater.EnqueueUpdated(segmentId, buffer[segmentId].m_bounds);
                }
            }
            catch (Exception e)
            {
                Log.Error("TM:PE change handler failed", e);
            }
        }

        /// <summary>TM:PE's assembly is loaded AND its mod is enabled in the Content Manager.</summary>
        private static bool IsTmpeEnabled()
        {
            foreach (PluginManager.PluginInfo plugin in Singleton<PluginManager>.instance.GetPluginsInfo())
            {
                if (!plugin.isEnabled)
                {
                    continue;
                }

                foreach (Assembly assembly in plugin.GetAssemblies())
                {
                    if (assembly.GetName().Name == "TrafficManager")
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
