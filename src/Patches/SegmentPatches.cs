using System;
using GantryTool.Data;
using HarmonyLib;

namespace GantryTool.Patches
{
    /// <summary>
    /// NetManager.SimulationStepImpl runs CalculateSegment -> UpdateBounds -> UpdateLanes for every updated
    /// segment (NetManager.cs:4539-4600), so this postfix fires after creation, upgrade, reverse and moves,
    /// with lane beziers and bounds already valid. Simulation thread: enqueue only.
    /// </summary>
    [HarmonyPatch(typeof(NetSegment), nameof(NetSegment.UpdateLanes))]
    public static class UpdateLanesPatch
    {
        public static void Postfix(ref NetSegment __instance, ushort segmentID, bool loading)
        {
            try
            {
                if (!loading && __instance.m_flags != NetSegment.Flags.None)
                {
                    GantryUpdater.EnqueueUpdated(segmentID, __instance.m_bounds);
                }
            }
            catch (Exception e)
            {
                Log.Error("UpdateLanes postfix failed", e);
            }
        }
    }

    /// <summary>
    /// Every segment release funnels into this private overload (NetManager.cs:3746). Prefix, because it
    /// clears the segment's flags; the bounds are captured before that. Simulation thread: enqueue only.
    /// </summary>
    [HarmonyPatch(typeof(NetManager), "ReleaseSegmentImplementation",
        new[] { typeof(ushort), typeof(NetSegment), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class ReleaseSegmentPatch
    {
        public static void Prefix(ushort segment, ref NetSegment data)
        {
            try
            {
                if (data.m_flags != NetSegment.Flags.None)
                {
                    GantryUpdater.EnqueueReleased(segment, data.m_bounds);
                }
            }
            catch (Exception e)
            {
                Log.Error("ReleaseSegment prefix failed", e);
            }
        }
    }
}
