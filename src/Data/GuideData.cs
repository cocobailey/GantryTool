using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// A big printed guide sign on a gantry (one-sided, with a plain back). It isn't tied to a lane: it stays
    /// <see cref="Along"/> metres from the start post. Guarded by GantryManager.Lock (saved on the simulation thread).
    /// </summary>
    public struct GuideHead
    {
        public byte Id;         // unique on its gantry, never 0
        public float Along;     // centre along the beam
        public float Scale;     // size relative to the default
        public sbyte Side;      // which way it faces: -1 = the gantry's -side, +1 = +side
        public string Design;   // the sign, as design JSON
    }

    /// <summary>
    /// Save data for guide signs and the per-gantry "automatic lane signals" switch, under its own key so older versions
    /// skip it and load everything else as before (there, a gantry simply has its lane signals and no guide signs).
    /// Testable outside the game.
    ///
    /// Format, key "GantryTool.Guide": int version(=1), int count, then per gantry that isn't at the defaults: int id,
    /// float startX/Y/Z, float endX/Y/Z (stale-data guard), bool laneHeads, int signCount, then per sign: byte id,
    /// float along, float scale, sbyte side, string design.
    /// </summary>
    public static class GuideData
    {
        public const string Key = "GantryTool.Guide";
        public const int Version = 1;
        public const int MaxSigns = 8;
        public const int MaxDesignLength = 32 * 1024;
        public const float MinScale = 0.3f;
        public const float MaxScale = 2f;

        public static byte[] Write(List<Gantry> gantries, object overridesLock)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                var keep = new List<Gantry>();
                var signs = new List<List<GuideHead>>();
                var laneHeads = new List<bool>();
                lock (overridesLock)
                {
                    foreach (Gantry g in gantries)
                    {
                        if (!g.LaneHeads || g.GuideSigns.Count > 0)
                        {
                            keep.Add(g);
                            signs.Add(new List<GuideHead>(g.GuideSigns));
                            laneHeads.Add(g.LaneHeads);
                        }
                    }
                }

                w.Write(Version);
                w.Write(keep.Count);
                for (int i = 0; i < keep.Count; i++)
                {
                    w.Write(keep[i].Id);
                    GantryData.WriteVector(w, keep[i].Start);
                    GantryData.WriteVector(w, keep[i].End);
                    w.Write(laneHeads[i]);
                    w.Write(signs[i].Count);
                    foreach (GuideHead h in signs[i])
                    {
                        w.Write(h.Id);
                        w.Write(h.Along);
                        w.Write(h.Scale);
                        w.Write(h.Side);
                        w.Write(h.Design ?? "");
                    }
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Applies saved data to the matching gantries; never throws, and keeps what was read before an error.</summary>
        public static int Apply(byte[] bytes, List<Gantry> gantries, out string error)
        {
            error = null;
            if (bytes == null || bytes.Length == 0)
            {
                return 0;
            }

            var byId = new Dictionary<int, Gantry>();
            foreach (Gantry g in gantries)
            {
                byId[g.Id] = g;
            }

            int added = 0;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1 || version > Version)
                    {
                        error = "guide sign data version " + version + " not supported (max " + Version + ")";
                        return 0;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int id = r.ReadInt32();
                        Vector3 start = GantryData.ReadVector(r);
                        Vector3 end = GantryData.ReadVector(r);
                        bool laneHeads = r.ReadBoolean();
                        int n = r.ReadInt32();
                        if (n < 0 || n > 255)
                        {
                            error = "guide sign count " + n + " out of range";
                            return added;
                        }

                        Gantry g;
                        bool match = byId.TryGetValue(id, out g) && GantryData.Same(g.Start, start) && GantryData.Same(g.End, end);
                        if (match)
                        {
                            g.LaneHeads = laneHeads;
                        }

                        for (int k = 0; k < n; k++)
                        {
                            var h = new GuideHead { Id = r.ReadByte(), Along = r.ReadSingle(), Scale = r.ReadSingle(), Side = r.ReadSByte(), Design = r.ReadString() };
                            if (match && Valid(ref h, g))
                            {
                                g.GuideSigns.Add(h);
                                added++;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "guide sign read failed after " + added + " signs: " + e.Message;
            }

            return added;
        }

        /// <summary>Drops duplicates, oversized designs and signs over the limit, and repairs out-of-range values.</summary>
        private static bool Valid(ref GuideHead h, Gantry g)
        {
            if (h.Id == 0 || g.GuideSigns.Count >= MaxSigns || string.IsNullOrEmpty(h.Design) || h.Design.Length > MaxDesignLength ||
                IndexOf(g.GuideSigns, h.Id) >= 0)
            {
                return false;
            }

            float span = g.Span;
            h.Along = float.IsNaN(h.Along) ? span * 0.5f : Mathf.Clamp(h.Along, 0f, Mathf.Max(0f, span));
            h.Scale = float.IsNaN(h.Scale) ? 1f : Mathf.Clamp(h.Scale, MinScale, MaxScale);
            h.Side = h.Side >= 0 ? (sbyte)1 : (sbyte)-1;
            return true;
        }

        public static int IndexOf(List<GuideHead> signs, byte id)
        {
            for (int i = 0; i < signs.Count; i++)
            {
                if (signs[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
