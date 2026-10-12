using System;
using System.Collections.Generic;
using System.IO;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Tests
{
    /// <summary>
    /// Save-compatibility checks. The most important ones:
    /// - saves written by 1.0 still load the same;
    /// - the legacy block written now is byte-identical to what 1.0 writes for the same gantries;
    /// - 1.0's own loading code (copied verbatim below from tag v1.0.0) reads saves written now.
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _passed;

        private static int Main()
        {
            Run("v1 save (no overrides) loads", V1Loads);
            Run("v2 save (overrides without direction) loads", V2Loads);
            Run("v3 save written by 1.0 loads identically", V3From10Loads);
            Run("legacy block is byte-identical to 1.0's", LegacyBytesIdenticalTo10);
            Run("1.0's loader reads saves written now", Loader10ReadsNewSaves);
            Run("newer symbols are written as Auto in the legacy block", NewerSymbolsWrittenAsAuto);
            Run("extension settings round-trip", ExtRoundTrip);
            Run("gantries without extension data keep defaults", NoExtKeepsDefaults);
            Run("stale extension data (gantry moved/recreated in 1.0) is ignored", StaleExtIgnored);
            Run("unknown future extension version is ignored", FutureExtIgnored);
            Run("corrupt / truncated data never throws and keeps what was read", CorruptDataIsSafe);
            Run("out-of-range extension values fall back to defaults", InsaneExtValuesIgnored);

            Console.WriteLine();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------------------ tests

        private static void V1Loads()
        {
            byte[] bytes = Bytes(w =>
            {
                w.Write(1);
                w.Write(2);
                WriteGantryCore(w, 3, new Vector3(1, 2, 3), new Vector3(30, 2, 3), 7.5f, 1.5f);
                WriteGantryCore(w, 9, new Vector3(-5, 0, 7), new Vector3(-5, 0, 47), 8f, 1.2f);
            });
            int v;
            string err;
            List<Gantry> g = GantryData.ReadLegacy(bytes, out v, out err);
            Check(err == null, "no error: " + err);
            Check(v == 1 && g.Count == 2, "version 1, 2 gantries");
            Check(g[1].Id == 9 && g[1].End == new Vector3(-5, 0, 47) && g[1].PostHeight == 8f && g[1].BeamDepth == 1.2f, "fields");
            Check(g[0].Overrides.Count == 0, "no overrides");
        }

        private static void V2Loads()
        {
            byte[] bytes = Bytes(w =>
            {
                w.Write(2);
                w.Write(1);
                WriteGantryCore(w, 4, Vector3.zero, new Vector3(20, 0, 0), 7.5f, 1.5f);
                w.Write(1);
                w.Write(6.32f);
                w.Write((byte)SignalSymbol.MergeRight);
                w.Write((byte)SignalSymbol.Auto);
            });
            int v;
            string err;
            List<Gantry> g = GantryData.ReadLegacy(bytes, out v, out err);
            Check(err == null && g.Count == 1, "loads");
            SignalOverride o = g[0].Overrides[0];
            Check(o.Offset == 6.32f && o.Minus == SignalSymbol.MergeRight && o.Plus == SignalSymbol.Auto, "override");
            Check(o.Sign == SignalOverride.UnknownSign, "direction unknown for v2");
        }

        private static void V3From10Loads()
        {
            List<Gantry> original = Sample();
            byte[] bytes = Old10.Write(original);
            int v;
            string err;
            List<Gantry> loaded = GantryData.ReadLegacy(bytes, out v, out err);
            Check(err == null && v == 3, "v3, no error");
            AssertSameCore(original, loaded);
        }

        private static void LegacyBytesIdenticalTo10()
        {
            List<Gantry> gantries = Sample();
            byte[] old = Old10.Write(gantries);
            byte[] now = GantryData.WriteLegacy(gantries, new object());
            Check(old.Length == now.Length, "same length (" + old.Length + " vs " + now.Length + ")");
            for (int i = 0; i < Math.Min(old.Length, now.Length); i++)
            {
                if (old[i] != now[i])
                {
                    Check(false, "first difference at byte " + i);
                    return;
                }
            }
        }

        private static void Loader10ReadsNewSaves()
        {
            List<Gantry> gantries = Sample();
            gantries[0].ClearanceOverride = 7f;          // new settings must not disturb the legacy block
            gantries[1].SignalSizeOverride = 1.8f;
            gantries[1].Style = 3;
            byte[] legacy = GantryData.WriteLegacy(gantries, new object());
            string err;
            List<Gantry> loadedBy10 = Old10.Read(legacy, out err);
            Check(err == null, "1.0 reports no error: " + err);
            AssertSameCore(gantries, loadedBy10);
        }

        private static void NewerSymbolsWrittenAsAuto()
        {
            List<Gantry> gantries = Sample();
            gantries[0].Overrides[0] = new SignalOverride { Offset = 3f, Minus = (SignalSymbol)42, Plus = SignalSymbol.Cross, Sign = 1 };
            byte[] legacy = GantryData.WriteLegacy(gantries, new object());
            string err;
            List<Gantry> loadedBy10 = Old10.Read(legacy, out err);
            Check(loadedBy10[0].Overrides[0].Minus == SignalSymbol.Auto, "unknown-to-1.0 symbol becomes Auto");
            Check(loadedBy10[0].Overrides[0].Plus == SignalSymbol.Cross, "known symbol kept");
        }

        private static void ExtRoundTrip()
        {
            List<Gantry> gantries = Sample();
            gantries[0].ClearanceOverride = 7f;
            gantries[1].SignalSizeOverride = 1.8f;
            gantries[1].Style = 3;
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[0].ClearanceOverride == 7f && float.IsNaN(loaded[0].SignalSizeOverride) && loaded[0].Style == 0, "gantry 1");
            Check(float.IsNaN(loaded[1].ClearanceOverride) && loaded[1].SignalSizeOverride == 1.8f && loaded[1].Style == 3, "gantry 2");
        }

        private static void NoExtKeepsDefaults()
        {
            List<Gantry> loaded = SaveAndLoad(Sample(), false);
            foreach (Gantry g in loaded)
            {
                Check(float.IsNaN(g.ClearanceOverride) && float.IsNaN(g.SignalSizeOverride) && g.Style == 0, "defaults for #" + g.Id);
            }
        }

        private static void StaleExtIgnored()
        {
            List<Gantry> gantries = Sample();
            gantries[0].ClearanceOverride = 7f;
            byte[] ext = GantryData.WriteExt(gantries);

            // Player goes back to 1.0, deletes gantry #3 and places a new one that reuses id 3 elsewhere.
            List<Gantry> after10 = Sample();
            after10[0].Start = new Vector3(500, 10, 500);
            byte[] legacy = Old10.Write(after10);

            int v;
            string err;
            List<Gantry> loaded = GantryData.ReadLegacy(legacy, out v, out err);
            string extErr;
            int applied = GantryData.ApplyExt(ext, loaded, out extErr);
            Check(float.IsNaN(loaded[0].ClearanceOverride), "moved gantry did not inherit the old setting");
            Check(applied == loaded.Count - 1, "unchanged gantries still get their settings (" + applied + ")");
        }

        private static void FutureExtIgnored()
        {
            List<Gantry> gantries = Sample();
            byte[] ext = Bytes(w =>
            {
                w.Write(99);
                w.Write(1);
                w.Write(3);
                w.Write(123.0f);
            });
            string err;
            int applied = GantryData.ApplyExt(ext, gantries, out err);
            Check(applied == 0 && err != null, "skipped with a message");
            Check(float.IsNaN(gantries[0].ClearanceOverride), "defaults kept");
        }

        private static void CorruptDataIsSafe()
        {
            byte[] full = Old10.Write(Sample());
            for (int cut = 0; cut < full.Length; cut += 3)
            {
                var part = new byte[cut];
                Array.Copy(full, part, cut);
                int v;
                string err;
                List<Gantry> g = GantryData.ReadLegacy(part, out v, out err);
                Check(g != null, "never null");
            }

            int ver;
            string e2;
            List<Gantry> partial = GantryData.ReadLegacy(Truncate(full, full.Length - 5), out ver, out e2);
            Check(e2 != null && partial.Count >= 1, "truncated save keeps complete gantries (" + partial.Count + ") and reports an error");

            byte[] ext = GantryData.WriteExt(Sample());
            for (int cut = 0; cut < ext.Length; cut += 2)
            {
                List<Gantry> target = Sample();
                string err;
                GantryData.ApplyExt(Truncate(ext, cut), target, out err);
            }

            string garbageErr;
            GantryData.ApplyExt(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, Sample(), out garbageErr);
            int gv;
            string gErr;
            GantryData.ReadLegacy(new byte[] { 255, 255, 255, 127, 1 }, out gv, out gErr);
            Check(gErr != null, "garbage legacy data reported, not thrown");
        }

        private static void InsaneExtValuesIgnored()
        {
            List<Gantry> gantries = Sample();
            gantries[0].ClearanceOverride = 5000f;
            gantries[1].SignalSizeOverride = -2f;
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(float.IsNaN(loaded[0].ClearanceOverride) && float.IsNaN(loaded[1].SignalSizeOverride), "fall back to default");
        }

        // ------------------------------------------------------------------------------ helpers

        private static List<Gantry> Sample()
        {
            var a = new Gantry { Id = 3, Start = new Vector3(-1117.4f, 117.2f, 551.7f), End = new Vector3(-1148.4f, 116.3f, 572.3f), PostHeight = 7.5f, BeamDepth = 1.5f };
            a.Overrides.Add(new SignalOverride { Offset = 6.32f, Minus = SignalSymbol.MergeRight, Plus = SignalSymbol.Auto, Sign = 1 });
            a.Overrides.Add(new SignalOverride { Offset = 30.18f, Minus = SignalSymbol.Hidden, Plus = SignalSymbol.Hidden, Sign = -1 });
            var b = new Gantry { Id = 7, Start = new Vector3(10, 60, 10), End = new Vector3(10, 60, 50), PostHeight = 9f, BeamDepth = 2f };
            var c = new Gantry { Id = 12, Start = new Vector3(0, 0, 0), End = new Vector3(40, 3, 0), PostHeight = 7.5f, BeamDepth = 1.5f };
            c.Overrides.Add(new SignalOverride { Offset = 2f, Minus = SignalSymbol.Reserved, Plus = SignalSymbol.Cross, Sign = SignalOverride.UnknownSign });
            return new List<Gantry> { a, b, c };
        }

        private static List<Gantry> SaveAndLoad(List<Gantry> gantries, bool withExt)
        {
            byte[] legacy = GantryData.WriteLegacy(gantries, new object());
            byte[] ext = withExt ? GantryData.WriteExt(gantries) : null;
            int v;
            string err;
            List<Gantry> loaded = GantryData.ReadLegacy(legacy, out v, out err);
            Check(err == null, "legacy read ok: " + err);
            string extErr;
            GantryData.ApplyExt(ext, loaded, out extErr);
            Check(extErr == null, "ext read ok: " + extErr);
            return loaded;
        }

        private static void AssertSameCore(List<Gantry> a, List<Gantry> b)
        {
            Check(a.Count == b.Count, "same count");
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                Check(a[i].Id == b[i].Id && a[i].Start == b[i].Start && a[i].End == b[i].End &&
                      a[i].PostHeight == b[i].PostHeight && a[i].BeamDepth == b[i].BeamDepth, "core fields #" + a[i].Id);
                Check(a[i].Overrides.Count == b[i].Overrides.Count, "override count #" + a[i].Id);
                for (int k = 0; k < Math.Min(a[i].Overrides.Count, b[i].Overrides.Count); k++)
                {
                    SignalOverride x = a[i].Overrides[k], y = b[i].Overrides[k];
                    Check(x.Offset == y.Offset && x.Minus == y.Minus && x.Plus == y.Plus && x.Sign == y.Sign, "override " + k + " #" + a[i].Id);
                }
            }
        }

        private static void WriteGantryCore(BinaryWriter w, int id, Vector3 s, Vector3 e, float post, float depth)
        {
            w.Write(id);
            w.Write(s.x); w.Write(s.y); w.Write(s.z);
            w.Write(e.x); w.Write(e.y); w.Write(e.z);
            w.Write(post);
            w.Write(depth);
        }

        private static byte[] Bytes(Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] Truncate(byte[] b, int n)
        {
            var r = new byte[n];
            Array.Copy(b, r, n);
            return r;
        }

        private static string _current;

        private static void Run(string name, Action test)
        {
            _current = name;
            int before = _failed;
            try
            {
                test();
            }
            catch (Exception e)
            {
                _failed++;
                Console.WriteLine("  FAIL " + name + ": threw " + e.GetType().Name + ": " + e.Message);
            }

            if (_failed == before)
            {
                _passed++;
                Console.WriteLine("  ok   " + name);
            }
        }

        private static void Check(bool condition, string what)
        {
            if (!condition)
            {
                _failed++;
                Console.WriteLine("  FAIL " + _current + ": " + what);
            }
        }
    }

    /// <summary>
    /// The save and load code of Gantry Tool 1.0 (tag v1.0.0, src/Data/GantrySerializer.cs), with only the
    /// game calls (serializableDataManager, logging, GantryManager) replaced by parameters/returns.
    /// </summary>
    internal static class Old10
    {
        private const int CurrentVersion = 3;

        public static byte[] Write(List<Gantry> gantries)
        {
            byte[] bytes;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CurrentVersion);
                w.Write(gantries.Count);
                for (int i = 0; i < gantries.Count; i++)
                {
                    Gantry g = gantries[i];
                    w.Write(g.Id);
                    WriteVector(w, g.Start);
                    WriteVector(w, g.End);
                    w.Write(g.PostHeight);
                    w.Write(g.BeamDepth);
                    w.Write(g.Overrides.Count);
                    for (int k = 0; k < g.Overrides.Count; k++)
                    {
                        w.Write(g.Overrides[k].Offset);
                        w.Write((byte)g.Overrides[k].Minus);
                        w.Write((byte)g.Overrides[k].Plus);
                        w.Write(g.Overrides[k].Sign);
                    }
                }

                w.Flush();
                bytes = ms.ToArray();
            }

            return bytes;
        }

        public static List<Gantry> Read(byte[] bytes, out string error)
        {
            error = null;
            var loaded = new List<Gantry>();
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1 || version > CurrentVersion)
                    {
                        error = "Gantry data version " + version + " not supported (max " + CurrentVersion + "); gantries skipped";
                    }
                    else
                    {
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
            }
            catch (Exception e)
            {
                error = "OnLoadData failed after " + loaded.Count + " gantries: " + e.Message;
            }

            return loaded;
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
