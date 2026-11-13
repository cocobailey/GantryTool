using ColossalFramework;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>
    /// Finds the deck of a bridge or raised road running along a gantry line, so a gantry "on a bridge" hangs its bar
    /// just under the deck's edge instead of floating at the usual height.
    /// </summary>
    public static class DeckFinder
    {
        /// <summary>The bar sits this far under the road surface: on the bridge's side, below the deck.</summary>
        public const float BarBelowDeck = 1.1f;

        private const float Clearance = 3f;   // a deck is at least this high above the ground the gantry stands on
        private const float Beside = 4f;      // how far past a road's edge the gantry line may run and still be on it
        private const int Samples = 7;

        /// <summary>The lowest deck along the line that's higher than <paramref name="above"/> plus a clearance; NaN if none.</summary>
        public static float DeckY(Vector3 start, Vector3 end, float above)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segments = nm.m_segments.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            float pad = 40f;
            int cx0 = Mathf.Max((int)((Mathf.Min(start.x, end.x) - pad) / 64f + 135f), 0);
            int cz0 = Mathf.Max((int)((Mathf.Min(start.z, end.z) - pad) / 64f + 135f), 0);
            int cx1 = Mathf.Min((int)((Mathf.Max(start.x, end.x) + pad) / 64f + 135f), 269);
            int cz1 = Mathf.Min((int)((Mathf.Max(start.z, end.z) + pad) / 64f + 135f), 269);

            var found = new bool[Samples];
            float best = float.NaN;
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    ushort id = grid[cz * 270 + cx];
                    int guard = 0;
                    while (id != 0 && ++guard < NetManager.MAX_SEGMENT_COUNT)
                    {
                        ref NetSegment seg = ref segments[id];
                        NetInfo info = seg.Info;
                        if ((seg.m_flags & (NetSegment.Flags.Created | NetSegment.Flags.Deleted)) == NetSegment.Flags.Created && info != null)
                        {
                            for (int i = 0; i < Samples; i++)
                            {
                                Vector3 point = Vector3.Lerp(start, end, (i + 0.5f) / Samples);
                                Vector3 pos, dir;
                                seg.GetClosestPositionAndDirection(point, out pos, out dir);
                                float dx = pos.x - point.x, dz = pos.z - point.z;
                                if (dx * dx + dz * dz <= (info.m_halfWidth + Beside) * (info.m_halfWidth + Beside) && pos.y > above + Clearance)
                                {
                                    best = float.IsNaN(best) ? pos.y : Mathf.Min(best, pos.y);
                                    found[i] = true;
                                }
                            }
                        }

                        id = seg.m_nextGridSegment;
                    }
                }
            }

            // A deck along most of the line: a road just passing over it isn't one to hang from.
            int along = 0;
            foreach (bool f in found)
            {
                along += f ? 1 : 0;
            }

            return along >= Samples - 2 ? best : float.NaN;
        }
    }
}
