using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Save data for extra heads, under its own key so versions that don't know them (1.0, 1.1) skip it and keep the
    /// rest of the save. Testable outside the game.
    ///
    /// Format, key "GantryTool.Heads": int version(=1), int count, then per gantry with extra heads: int id,
    /// float startX/Y/Z, float endX/Y/Z (stale-data guard, as in the extension block), int headCount, then per head:
    /// byte id, float along, float size, bool linked, float linkOffset, sbyte linkSign, float nudge,
    /// ushort minusSymbol, ushort minusValue, ushort plusSymbol, ushort plusValue.
    /// </summary>
    public static class ExtraHeadData
    {
        public const string Key = "GantryTool.Heads";
        public const int Version = 1;

        public static byte[] Write(List<Gantry> gantries, object overridesLock)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                var withHeads = new List<Gantry>();
                var heads = new List<List<ExtraHead>>();
                lock (overridesLock)
                {
                    foreach (Gantry g in gantries)
                    {
                        if (g.ExtraHeads.Count > 0)
                        {
                            withHeads.Add(g);
                            heads.Add(new List<ExtraHead>(g.ExtraHeads));
                        }
                    }
                }

                w.Write(Version);
                w.Write(withHeads.Count);
                for (int i = 0; i < withHeads.Count; i++)
                {
                    Gantry g = withHeads[i];
                    w.Write(g.Id);
                    GantryData.WriteVector(w, g.Start);
                    GantryData.WriteVector(w, g.End);
                    w.Write(heads[i].Count);
                    foreach (ExtraHead h in heads[i])
                    {
                        w.Write(h.Id);
                        w.Write(h.Along);
                        w.Write(h.Size);
                        w.Write(h.Linked);
                        w.Write(h.LinkOffset);
                        w.Write(h.LinkSign);
                        w.Write(h.Nudge);
                        w.Write((ushort)h.Minus);
                        w.Write(h.MinusValue);
                        w.Write((ushort)h.Plus);
                        w.Write(h.PlusValue);
                    }
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Adds saved extra heads to the matching gantries (same id and posts). Returns how many heads were added;
        /// never throws, and keeps what was read before an error.
        /// </summary>
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
                        error = "extra head data version " + version + " not supported (max " + Version + ")";
                        return 0;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int id = r.ReadInt32();
                        Vector3 start = GantryData.ReadVector(r);
                        Vector3 end = GantryData.ReadVector(r);
                        int n = r.ReadInt32();
                        if (n < 0 || n > 255)
                        {
                            error = "extra head count " + n + " out of range";
                            return added;
                        }

                        Gantry g;
                        bool match = byId.TryGetValue(id, out g) && GantryData.Same(g.Start, start) && GantryData.Same(g.End, end);
                        for (int k = 0; k < n; k++)
                        {
                            var h = new ExtraHead
                            {
                                Id = r.ReadByte(),
                                Along = r.ReadSingle(),
                                Size = r.ReadSingle(),
                                Linked = r.ReadBoolean(),
                                LinkOffset = r.ReadSingle(),
                                LinkSign = r.ReadSByte(),
                                Nudge = r.ReadSingle(),
                                Minus = GantryData.ExtSymbol(r.ReadUInt16()),
                                MinusValue = r.ReadUInt16(),
                                Plus = GantryData.ExtSymbol(r.ReadUInt16()),
                                PlusValue = r.ReadUInt16()
                            };

                            if (match && Valid(ref h, g))
                            {
                                g.ExtraHeads.Add(h);
                                added++;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "extra head read failed after " + added + " heads: " + e.Message;
            }

            return added;
        }

        /// <summary>Drops duplicates and heads over the limit, and repairs out-of-range values.</summary>
        private static bool Valid(ref ExtraHead h, Gantry g)
        {
            if (h.Id == 0 || g.ExtraHeads.Count >= HeadRef.MaxExtraHeads || HeadRef.IndexOf(g.ExtraHeads, h.Id) >= 0)
            {
                return false;
            }

            float span = g.Span;
            h.Along = float.IsNaN(h.Along) ? span * 0.5f : Mathf.Clamp(h.Along, 0f, Mathf.Max(0f, span));
            h.Size = float.IsNaN(h.Size) || h.Size < 0f || h.Size > 10f ? 0f : h.Size;
            h.Nudge = float.IsNaN(h.Nudge) || Mathf.Abs(h.Nudge) > 100f ? 0f : h.Nudge;
            if (float.IsNaN(h.LinkOffset))
            {
                h.Linked = false;
            }

            // Hidden is for lane heads only; Auto / Road speed need a lane.
            if (h.Minus == SignalSymbol.Hidden || (!h.Linked && (h.Minus == SignalSymbol.Auto || h.Minus == SignalSymbol.SpeedAuto)))
            {
                h.Minus = SignalSymbol.Blank;
                h.MinusValue = 0;
            }

            if (h.Plus == SignalSymbol.Hidden || (!h.Linked && (h.Plus == SignalSymbol.Auto || h.Plus == SignalSymbol.SpeedAuto)))
            {
                h.Plus = SignalSymbol.Blank;
                h.PlusValue = 0;
            }

            return true;
        }
    }
}
