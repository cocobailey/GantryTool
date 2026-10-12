using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Save-data encoding, kept free of game state so it can be tested outside the game.
    ///
    /// Two blocks, so a save made by a newer version still loads in 1.0:
    ///
    /// LEGACY block, key "GantryTool" — exactly the 1.0 format (v3), which 1.0 reads. Never change it:
    ///   int version(=3), int count, then per gantry:
    ///   int id, float startX/Y/Z, float endX/Y/Z, float postHeight, float beamDepth,
    ///   int overrideCount, then per override: float offset, byte minus, byte plus, sbyte travelSign
    ///   (v1 had no overrides, v2 had no travelSign; both still load.)
    ///
    /// EXTENSION block, key "GantryTool.Ext" — everything added after 1.0. 1.0 never reads it. Format:
    ///   int extVersion, int count, then per gantry: int id, float startX/Y/Z, float endX/Y/Z, then the fields
    ///   of each version in order:
    ///   v1: float clearanceOverride (NaN = default), float signalSizeOverride (NaN = default), byte style
    ///   v2: float heightAboveRoad (NaN = not converted yet)
    ///   v3: int layoutCount, then per head: float offset, sbyte sign (identify the legacy override), float nudge,
    ///       float size. Matched to the legacy block's overrides by exact offset and sign; unmatched entries are
    ///       dropped.
    ///   The positions guard against stale data: if a player saved in 1.0 (which rewrites only the legacy
    ///   block), ids may have been reused, so an entry is only applied when id AND both posts still match.
    ///   New fields are only ever appended in a new extVersion. Unknown newer versions are skipped
    ///   entirely (gantries still load from the legacy block).
    /// </summary>
    public static class GantryData
    {
        public const string LegacyKey = "GantryTool";
        public const string ExtKey = "GantryTool.Ext";
        public const int LegacyVersion = 3;
        public const int ExtVersion = 3;

        // ----------------------------------------------------------------- legacy block (1.0 format)

        public static byte[] WriteLegacy(List<Gantry> gantries, object overridesLock)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(LegacyVersion);
                w.Write(gantries.Count);
                for (int i = 0; i < gantries.Count; i++)
                {
                    Gantry g = gantries[i];
                    w.Write(g.Id);
                    WriteVector(w, g.Start);
                    WriteVector(w, g.End);
                    w.Write(g.PostHeight);
                    w.Write(g.BeamDepth);

                    // Overrides can be edited on the main thread while the save runs on the simulation thread.
                    lock (overridesLock)
                    {
                        w.Write(g.Overrides.Count);
                        for (int k = 0; k < g.Overrides.Count; k++)
                        {
                            w.Write(g.Overrides[k].Offset);
                            w.Write((byte)LegacySymbol(g.Overrides[k].Minus));
                            w.Write((byte)LegacySymbol(g.Overrides[k].Plus));
                            w.Write(g.Overrides[k].Sign);
                        }
                    }
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Reads the legacy block. Returns the gantries read so far even if the data is cut short, and reports
        /// the version and any error through the out parameters (never throws).
        /// </summary>
        public static List<Gantry> ReadLegacy(byte[] bytes, out int version, out string error)
        {
            var loaded = new List<Gantry>();
            version = 0;
            error = null;
            if (bytes == null || bytes.Length == 0)
            {
                return loaded;
            }

            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    version = r.ReadInt32();
                    if (version < 1 || version > LegacyVersion)
                    {
                        error = "gantry data version " + version + " not supported (max " + LegacyVersion + ")";
                        return loaded;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var g = new Gantry
                        {
                            Id = r.ReadInt32(),
                            Start = ReadVector(r),
                            End = ReadVector(r),
                            PostHeight = r.ReadSingle(),
                            BeamDepth = r.ReadSingle()
                        };
                        if (version >= 2)
                        {
                            int overrides = r.ReadInt32();
                            for (int k = 0; k < overrides; k++)
                            {
                                g.Overrides.Add(new SignalOverride
                                {
                                    Offset = r.ReadSingle(),
                                    Minus = ReadSymbol(r),
                                    Plus = ReadSymbol(r),
                                    Sign = version >= 3 ? r.ReadSByte() : SignalOverride.UnknownSign
                                });
                            }
                        }

                        loaded.Add(g);
                    }
                }
            }
            catch (Exception e)
            {
                error = "read failed after " + loaded.Count + " gantries: " + e.Message;
            }

            return loaded;
        }

        // ----------------------------------------------------------------- extension block

        public static byte[] WriteExt(List<Gantry> gantries)
        {
            return WriteExt(gantries, new object());
        }

        public static byte[] WriteExt(List<Gantry> gantries, object overridesLock)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(ExtVersion);
                w.Write(gantries.Count);
                for (int i = 0; i < gantries.Count; i++)
                {
                    Gantry g = gantries[i];
                    w.Write(g.Id);
                    WriteVector(w, g.Start);
                    WriteVector(w, g.End);
                    w.Write(g.ClearanceOverride);
                    w.Write(g.SignalSizeOverride);
                    w.Write(g.Style);
                    w.Write(g.HeightAboveRoad);

                    var layouts = new List<SignalOverride>();
                    lock (overridesLock)
                    {
                        for (int k = 0; k < g.Overrides.Count; k++)
                        {
                            if (g.Overrides[k].HasLayout)
                            {
                                layouts.Add(g.Overrides[k]);
                            }
                        }
                    }

                    w.Write(layouts.Count);
                    for (int k = 0; k < layouts.Count; k++)
                    {
                        w.Write(layouts[k].Offset);
                        w.Write(layouts[k].Sign);
                        w.Write(layouts[k].Nudge);
                        w.Write(layouts[k].Size);
                    }
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Applies the extension block to gantries loaded from the legacy block (matched by id). Missing or
        /// unreadable data leaves the defaults in place. Returns how many gantries got extension data.
        /// </summary>
        public static int ApplyExt(byte[] bytes, List<Gantry> gantries, out string error)
        {
            error = null;
            if (bytes == null || bytes.Length == 0)
            {
                return 0;
            }

            var byId = new Dictionary<int, Gantry>();
            for (int i = 0; i < gantries.Count; i++)
            {
                byId[gantries[i].Id] = gantries[i];
            }

            int applied = 0;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1 || version > ExtVersion)
                    {
                        error = "extension data version " + version + " not supported (max " + ExtVersion + "); per-gantry settings use defaults";
                        return 0;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int id = r.ReadInt32();
                        Vector3 start = ReadVector(r);
                        Vector3 end = ReadVector(r);
                        float clearance = r.ReadSingle();
                        float signalSize = r.ReadSingle();
                        byte style = r.ReadByte();
                        float height = version >= 2 ? r.ReadSingle() : float.NaN;

                        Gantry g;
                        if (byId.TryGetValue(id, out g) && Same(g.Start, start) && Same(g.End, end))
                        {
                            g.ClearanceOverride = Sane(clearance, 1f, 30f);
                            g.SignalSizeOverride = Sane(signalSize, 0.2f, 5f);
                            g.Style = style;
                            g.HeightAboveRoad = Sane(height, 0.5f, 60f);
                            applied++;
                        }

                        if (version >= 3)
                        {
                            int layoutCount = r.ReadInt32();
                            for (int k = 0; k < layoutCount; k++)
                            {
                                float offset = r.ReadSingle();
                                sbyte sign = r.ReadSByte();
                                float nudge = r.ReadSingle();
                                float size = r.ReadSingle();
                                if (g != null && Same(g.Start, start) && Same(g.End, end))
                                {
                                    ApplyLayout(g, offset, sign, nudge, size);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "extension read failed after " + applied + " gantries: " + e.Message;
            }

            return applied;
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>Puts a saved head layout on the matching legacy override (same offset and direction).</summary>
        private static void ApplyLayout(Gantry g, float offset, sbyte sign, float nudge, float size)
        {
            if (float.IsNaN(nudge) || Mathf.Abs(nudge) > 20f)
            {
                nudge = 0f;
            }

            if (float.IsNaN(size) || size < 0.2f || size > 5f)
            {
                size = 0f;
            }

            for (int i = 0; i < g.Overrides.Count; i++)
            {
                SignalOverride o = g.Overrides[i];
                if (Mathf.Abs(o.Offset - offset) < 0.001f && o.Sign == sign)
                {
                    o.Nudge = nudge;
                    o.Size = size;
                    g.Overrides[i] = o;
                    return;
                }
            }
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f && Mathf.Abs(a.z - b.z) < 0.01f;
        }

        /// <summary>Out-of-range or corrupt override values fall back to "use default".</summary>
        private static float Sane(float v, float min, float max)
        {
            return float.IsNaN(v) || v < min || v > max ? float.NaN : v;
        }

        /// <summary>The legacy block only knows symbols 1.0 knows (0-7); anything newer is written as Auto.</summary>
        private static SignalSymbol LegacySymbol(SignalSymbol s)
        {
            return (byte)s <= (byte)SignalSymbol.Hidden ? s : SignalSymbol.Auto;
        }

        private static SignalSymbol ReadSymbol(BinaryReader r)
        {
            byte b = r.ReadByte();
            return b <= (byte)SignalSymbol.Hidden ? (SignalSymbol)b : SignalSymbol.Auto;
        }

        private static void WriteVector(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static Vector3 ReadVector(BinaryReader r)
        {
            float x = r.ReadSingle();
            float y = r.ReadSingle();
            float z = r.ReadSingle();
            return new Vector3(x, y, z);
        }
    }
}
