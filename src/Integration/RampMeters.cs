using System;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Integration
{
    /// <summary>
    /// Optional Ramp Meter integration for the "RAMP METERED WHEN FLASHING" sign. Ramp Meter's public API
    /// (RampMeter.RampMeterApi, static, read-only) is reached through reflection only, so Gantry Tool loads and runs
    /// the same with or without it. Calls go through cached delegates: the per-frame flash check doesn't allocate.
    /// </summary>
    public static class RampMeters
    {
        private const string AssemblyName = "RampMeter";
        private const string ApiType = "RampMeter.RampMeterApi";

        /// <summary>A sign only links to a meter at most this far downstream of it (m).</summary>
        public const float MaxLinkDistance = 2000f;

        private delegate bool NodeTest(ushort node);
        private delegate string NodeState(ushort node);
        private delegate int NodeList(ushort[] output);

        private static NodeTest _isMetered;
        private static NodeTest _isMetering;
        private static NodeState _getState;
        private static NodeList _getMeteredNodes;
        private static bool _errorLogged;
        private static readonly ushort[] NodeBuffer = new ushort[NetManager.MAX_NODE_COUNT];

        /// <summary>True while Ramp Meter is enabled and its API answered.</summary>
        public static bool Present => _isMetering != null;

        /// <summary>Called on level load: finds Ramp Meter and binds its API.</summary>
        public static void Init()
        {
            Shutdown();
            try
            {
                Type api = FindApi();
                if (api == null)
                {
                    Log.Info("Ramp Meter not enabled; ramp metering signs stay dark");
                    return;
                }

                _isMetered = Bind<NodeTest>(api, "IsMetered");
                _getState = Bind<NodeState>(api, "GetState");
                _getMeteredNodes = Bind<NodeList>(api, "GetMeteredNodes");
                _isMetering = Bind<NodeTest>(api, "IsMetering"); // last: Present only once everything is bound

                FieldInfo version = api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                Log.Info("Ramp Meter detected (API v" + (version != null ? version.GetValue(null) : "?") +
                         "); ramp metering signs flash while their meter is metering");
            }
            catch (Exception e)
            {
                Shutdown();
                Log.Error("Ramp Meter integration failed to start; continuing without it", e);
            }
        }

        public static void Shutdown()
        {
            _isMetered = null;
            _isMetering = null;
            _getState = null;
            _getMeteredNodes = null;
            _errorLogged = false;
        }

        /// <summary>True while the meter at <paramref name="node"/> is metering. Safe every frame; false without Ramp Meter.</summary>
        public static bool IsMetering(ushort node)
        {
            NodeTest call = _isMetering;
            if (call == null || node == 0)
            {
                return false;
            }

            try
            {
                return call(node);
            }
            catch (Exception e)
            {
                Fail("IsMetering", e);
                return false;
            }
        }

        /// <summary>The meter's state for the signal panel, e.g. "Metering"; null without a meter there.</summary>
        public static string GetState(ushort node)
        {
            NodeState call = _getState;
            if (call == null || node == 0)
            {
                return null;
            }

            try
            {
                return call(node);
            }
            catch (Exception e)
            {
                Fail("GetState", e);
                return null;
            }
        }

        /// <summary>
        /// The metered node a sign on this face should follow: the nearest meter downstream of the head, i.e. ahead of
        /// the traffic that reads the face, within <see cref="MaxLinkDistance"/>. 0 when there is none. Main thread.
        /// </summary>
        public static ushort FindLink(Gantry g, int laneIndex, bool minusFace)
        {
            NodeList list = _getMeteredNodes;
            NodeTest metered = _isMetered;
            if (list == null || metered == null || !GantryManager.HeadExists(g, laneIndex))
            {
                return 0;
            }

            try
            {
                Vector3 flat = g.End - g.Start;
                flat.y = 0f;
                Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.right;
                Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                Vector3 head = g.Start + dir * GantryManager.HeadAlong(g, laneIndex); // lane or extra head

                // The minus face is seen by traffic heading +side, so downstream of it is +side.
                Vector3 travel = minusFace ? side : -side;
                return Nearest(head, travel, list(NodeBuffer), NodeBuffer, metered);
            }
            catch (Exception e)
            {
                Fail("GetMeteredNodes", e);
                return 0;
            }
        }

        private static ushort Nearest(Vector3 head, Vector3 travel, int count, ushort[] nodes, NodeTest metered)
        {
            NetNode[] buffer = Singleton<NetManager>.instance.m_nodes.m_buffer;
            ushort best = 0;
            float bestDist = MaxLinkDistance;
            for (int i = 0; i < count; i++)
            {
                ushort n = nodes[i];
                if (n == 0 || (buffer[n].m_flags & NetNode.Flags.Created) == 0 || !metered(n))
                {
                    continue;
                }

                Vector3 d = buffer[n].m_position - head;
                d.y = 0f;
                float dist = d.magnitude;
                if (Vector3.Dot(d, travel) > 0f && dist < bestDist)
                {
                    best = n;
                    bestDist = dist;
                }
            }

            return best;
        }

        private static void Fail(string what, Exception e)
        {
            // An API change in Ramp Meter shouldn't take gantries down with it: switch off and keep going.
            if (!_errorLogged)
            {
                Log.Error("Ramp Meter " + what + " failed; ramp metering signs switched off", e);
            }

            Shutdown();
            _errorLogged = true;
        }

        private static T Bind<T>(Type api, string name) where T : class
        {
            MethodInfo invoke = typeof(T).GetMethod("Invoke");
            ParameterInfo[] ps = invoke.GetParameters();
            var types = new Type[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                types[i] = ps[i].ParameterType;
            }

            MethodInfo m = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            if (m == null || m.ReturnType != invoke.ReturnType)
            {
                throw new MissingMethodException(ApiType, name);
            }

            return (T)(object)Delegate.CreateDelegate(typeof(T), m);
        }

        /// <summary>Ramp Meter's API type, if its mod is enabled in the Content Manager; else null.</summary>
        private static Type FindApi()
        {
            foreach (PluginManager.PluginInfo plugin in Singleton<PluginManager>.instance.GetPluginsInfo())
            {
                if (!plugin.isEnabled)
                {
                    continue;
                }

                foreach (Assembly assembly in plugin.GetAssemblies())
                {
                    if (assembly.GetName().Name == AssemblyName)
                    {
                        return assembly.GetType(ApiType, false);
                    }
                }
            }

            return null;
        }
    }
}
