using System.Collections.Generic;
using GantryTool.Detect;

namespace GantryTool.Data
{
    /// <summary>
    /// A signal head the player added on top of the automatic one-per-lane heads. Unlinked (the default) it stays
    /// <see cref="Along"/> metres from the start post and only shows signs picked by hand. Linked to a lane it follows
    /// that lane (<see cref="Nudge"/> metres from its centre) and Auto / Road speed read the lane, like its own head.
    /// Guarded by GantryManager.Lock (saved on the simulation thread).
    /// </summary>
    public struct ExtraHead
    {
        public byte Id;           // unique on its gantry, never 0
        public float Along;       // unlinked: position along the beam; linked: last position (kept if the lane goes)
        public float Size;        // box size, 0 = the gantry's
        public bool Linked;
        public float LinkOffset;  // linked: the lane's offset and direction, matched like a lane head's signs
        public sbyte LinkSign;
        public float Nudge;       // linked: offset from the lane centre
        public SignalSymbol Minus;
        public SignalSymbol Plus;
        public ushort MinusValue;
        public ushort PlusValue;

        public FaceSign MinusFace => new FaceSign(Minus, MinusValue);
        public FaceSign PlusFace => new FaceSign(Plus, PlusValue);
    }

    /// <summary>
    /// Heads are numbered so one int names any head: a lane's own head is its lane index, an extra head is
    /// <see cref="ExtraBase"/> + its id, a guide sign <see cref="GuideBase"/> + its id.
    /// </summary>
    public static class HeadRef
    {
        public const int ExtraBase = 1000;
        public const int GuideBase = 2000;
        public const int MaxExtraHeads = 8;

        public static bool IsExtra(int head)
        {
            return head >= ExtraBase && head < GuideBase;
        }

        public static bool IsGuide(int head)
        {
            return head >= GuideBase;
        }

        public static byte GuideId(int head)
        {
            return (byte)(head - GuideBase);
        }

        public static int OfGuide(byte id)
        {
            return GuideBase + id;
        }

        public static byte ExtraId(int head)
        {
            return (byte)(head - ExtraBase);
        }

        public static int Of(byte extraId)
        {
            return ExtraBase + extraId;
        }

        /// <summary>Index of the extra head with <paramref name="id"/> in the list, or -1.</summary>
        public static int IndexOf(List<ExtraHead> extras, byte id)
        {
            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Every signal head of a gantry: lane heads in lane order (when on), then extra heads.</summary>
        public static void All(List<GantryLane> lanes, List<ExtraHead> extras, List<int> output, bool laneHeads = true)
        {
            output.Clear();
            for (int i = 0; laneHeads && i < lanes.Count; i++)
            {
                output.Add(i);
            }

            if (extras != null)
            {
                for (int i = 0; i < extras.Count; i++)
                {
                    output.Add(Of(extras[i].Id));
                }
            }
        }
    }
}
