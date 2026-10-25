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
            Run("height above road round-trips (extension v2)", HeightRoundTrip);
            Run("extension v1 (1.1 dev builds) still loads", ExtV1Loads);
            Run("converting a 1.0 gantry never moves its beam", ConversionKeepsBeam);
            Run("posts never shorter than the minimum", MinimumPost);
            Run("equivalent post height redraws the same beam in 1.0", EquivalentPostHeightMatches10);
            Run("head position and size round-trip (extension v3)", HeadLayoutRoundTrip);
            Run("1.0 reads heads with only a position/size (Auto signs)", Loader10ReadsLayoutOnlyHeads);
            Run("head layout without a matching sign entry is dropped", UnmatchedLayoutDropped);
            Run("out-of-range head layout falls back to default", InsaneLayoutIgnored);
            Run("speed limit faces round-trip (extension v4)", SpeedFacesRoundTrip);
            Run("1.0 reads speed limit faces as Auto", Loader10ReadsSpeedAsAuto);
            Run("a face changed in 1.0 keeps that change", FaceChangedIn10Wins);
            Run("a plain face reset in 1.0 is not undone", PlainFaceResetIn10Kept);
            Run("unknown future signs load as Auto", UnknownExtSymbolIsAuto);
            Run("warning and restriction signs round-trip; 1.0 sees Auto", WarningsRoundTrip);
            Run("ramp metering signs keep their meter node; 1.0 sees Auto; 1.1.0 can still read the block", RampSignRoundTrip);
            Run("every style: each sign has its own tile, missing ones fall back", AtlasTiles);
            Run("custom signs: library round-trips; only used signs are saved", CustomRoundTrip);
            Run("custom signs: faces keep their sign; 1.0 sees Auto", CustomFaces);
            Run("custom signs: bad or cut-short data never throws", CustomCorrupt);
            Run("custom signs: text is cleaned; duplicates reused; library capped", CustomLibraryRules);
            Run("custom signs: a full library still fits every style's texture", CustomAtlasFits);
            Run("ramp metering sign is only offered while Ramp Meter runs", RampSignNeedsRampMeter);

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

        private static void HeightRoundTrip()
        {
            List<Gantry> gantries = Sample();
            gantries[0].HeightAboveRoad = 6.25f;
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[0].HeightAboveRoad == 6.25f, "height kept");
            Check(float.IsNaN(loaded[1].HeightAboveRoad), "unset stays NaN (converted on load)");
        }

        private static void ExtV1Loads()
        {
            List<Gantry> gantries = Sample();
            byte[] ext = Bytes(w =>
            {
                w.Write(1);
                w.Write(1);
                Gantry g = gantries[1];
                w.Write(g.Id);
                w.Write(g.Start.x); w.Write(g.Start.y); w.Write(g.Start.z);
                w.Write(g.End.x); w.Write(g.End.y); w.Write(g.End.z);
                w.Write(7f);
                w.Write(1.5f);
                w.Write((byte)2);
            });
            string err;
            int applied = GantryData.ApplyExt(ext, gantries, out err);
            Check(err == null && applied == 1, "applied: " + err);
            Check(gantries[1].ClearanceOverride == 7f && gantries[1].SignalSizeOverride == 1.5f && gantries[1].Style == 2, "v1 fields");
            Check(float.IsNaN(gantries[1].HeightAboveRoad), "height not set by v1 (converted later)");
        }

        private static void ConversionKeepsBeam()
        {
            float[] grounds = { 0f, 60f, 117.6f, -3f };
            float[] posts = { 4f, 7.5f, 9f, 15f };
            float[] clearances = { 4f, 5.5f, 9f };
            // Road below, level with, slightly above and far above the posts (elevated), plus no road at all.
            float[] roadOffsets = { float.NaN, -12f, -0.3f, 0f, 1.5f, 7f, 20f };
            int cases = 0;
            foreach (float g in grounds)
            foreach (float post in posts)
            foreach (float clr in clearances)
            foreach (float off in roadOffsets)
            {
                float road = float.IsNaN(off) ? float.NaN : g + off;
                float legacy = GantryHeight.LegacyBeamBottom(g, post, road, clr);
                float h = GantryHeight.Convert(g, post, road, clr);
                float now = GantryHeight.BeamBottom(g, road, h);
                cases++;
                if (Mathf.Abs(now - legacy) > 0.001f)
                {
                    Check(false, "beam moved: ground " + g + " post " + post + " clr " + clr + " road " + road + ": " + legacy + " -> " + now);
                    return;
                }
            }

            Check(cases > 0, cases + " cases");
        }

        private static void MinimumPost()
        {
            float bottom = GantryHeight.BeamBottom(100f, 80f, 7.5f); // road in a deep cutting
            Check(bottom >= 100f + GantryHeight.MinPost, "floor applied: " + bottom);
        }

        private static void EquivalentPostHeightMatches10()
        {
            float g = 117.6f, road = 130f, h = 5f;
            float bottom = GantryHeight.BeamBottom(g, road, h);
            float post = GantryHeight.EquivalentPostHeight(g, bottom);
            float in10 = GantryHeight.LegacyBeamBottom(g, post, road, 5.5f);
            Check(Mathf.Abs(in10 - Mathf.Max(bottom, road + 5.5f)) < 0.001f, "1.0 draws the same beam (or its own clearance minimum)");
        }

        private static void HeadLayoutRoundTrip()
        {
            List<Gantry> gantries = Sample();
            SignalOverride o = gantries[0].Overrides[0];
            o.Nudge = 0.75f;
            o.Size = 1.6f;
            gantries[0].Overrides[0] = o;
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Auto, Plus = SignalSymbol.Auto, Sign = 1, Nudge = -1.25f });
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[0].Overrides[0].Nudge == 0.75f && loaded[0].Overrides[0].Size == 1.6f, "gantry 1 head");
            Check(loaded[0].Overrides[1].Nudge == 0f && loaded[0].Overrides[1].Size == 0f, "untouched head stays default");
            Check(loaded[1].Overrides.Count == 1 && loaded[1].Overrides[0].Nudge == -1.25f, "layout-only head kept");
        }

        private static void Loader10ReadsLayoutOnlyHeads()
        {
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Auto, Plus = SignalSymbol.Auto, Sign = 1, Nudge = 2f, Size = 1f });
            byte[] legacy = GantryData.WriteLegacy(gantries, new object());
            string err;
            List<Gantry> by10 = Old10.Read(legacy, out err);
            Check(err == null, "1.0 reads it: " + err);
            Check(by10[1].Overrides.Count == 1 && by10[1].Overrides[0].Minus == SignalSymbol.Auto && by10[1].Overrides[0].Plus == SignalSymbol.Auto,
                  "1.0 sees a harmless Auto/Auto entry");
        }

        private static void UnmatchedLayoutDropped()
        {
            List<Gantry> gantries = Sample();
            SignalOverride o = gantries[0].Overrides[0];
            o.Nudge = 1f;
            gantries[0].Overrides[0] = o;
            byte[] ext = GantryData.WriteExt(gantries);

            // Same gantry, but its sign entry is gone (e.g. reset in 1.0 before saving).
            List<Gantry> target = Sample();
            target[0].Overrides.RemoveAt(0);
            string err;
            GantryData.ApplyExt(ext, target, out err);
            Check(err == null, "no error: " + err);
            foreach (SignalOverride x in target[0].Overrides)
            {
                Check(x.Nudge == 0f && x.Size == 0f, "no layout attached to another head");
            }
        }

        private static void InsaneLayoutIgnored()
        {
            List<Gantry> gantries = Sample();
            SignalOverride o = gantries[0].Overrides[0];
            o.Nudge = 500f;
            o.Size = 99f;
            gantries[0].Overrides[0] = o;
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[0].Overrides[0].Nudge == 0f && loaded[0].Overrides[0].Size == 0f, "fell back to default");
        }

        private static List<Gantry> SpeedSample()
        {
            List<Gantry> gantries = Sample();
            SignalOverride o = gantries[0].Overrides[0];
            o.Minus = SignalSymbol.SpeedLimit;
            o.MinusValue = 80;
            o.Plus = SignalSymbol.Cross;
            gantries[0].Overrides[0] = o;
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Arrow, Plus = SignalSymbol.SpeedAuto, Sign = 1 });
            return gantries;
        }

        private static void SpeedFacesRoundTrip()
        {
            List<Gantry> loaded = SaveAndLoad(SpeedSample(), true);
            SignalOverride a = loaded[0].Overrides[0], b = loaded[1].Overrides[0];
            Check(a.Minus == SignalSymbol.SpeedLimit && a.MinusValue == 80 && a.Plus == SignalSymbol.Cross, "speed limit 80 + Cross");
            Check(b.Minus == SignalSymbol.Arrow && b.Plus == SignalSymbol.SpeedAuto, "road speed face");
            Check(loaded[0].Overrides[1].Minus == SignalSymbol.Hidden, "other heads untouched");
        }

        private static void Loader10ReadsSpeedAsAuto()
        {
            byte[] legacy = GantryData.WriteLegacy(SpeedSample(), new object());
            string err;
            List<Gantry> by10 = Old10.Read(legacy, out err);
            Check(err == null, "1.0 reads it: " + err);
            Check(by10[0].Overrides[0].Minus == SignalSymbol.Auto && by10[0].Overrides[0].Plus == SignalSymbol.Cross, "speed face is Auto, Cross kept");
            Check(by10[1].Overrides[0].Minus == SignalSymbol.Arrow && by10[1].Overrides[0].Plus == SignalSymbol.Auto, "road speed is Auto");
        }

        private static List<Gantry> ReloadAfter10(List<Gantry> saved, Action<List<Gantry>> changeIn10)
        {
            byte[] ext = GantryData.WriteExt(saved);
            int v;
            string err;
            List<Gantry> in10 = GantryData.ReadLegacy(GantryData.WriteLegacy(saved, new object()), out v, out err);
            changeIn10(in10);
            // 1.0 keeps the extension key untouched; the next 1.1 load sees 1.0's legacy block plus the old ext block.
            List<Gantry> loaded = GantryData.ReadLegacy(GantryData.WriteLegacy(in10, new object()), out v, out err);
            GantryData.ApplyExt(ext, loaded, out err);
            Check(err == null, "ext read ok: " + err);
            return loaded;
        }

        private static void FaceChangedIn10Wins()
        {
            List<Gantry> loaded = ReloadAfter10(SpeedSample(), g =>
            {
                SignalOverride o = g[0].Overrides[0];
                o.Minus = SignalSymbol.MergeLeft;
                g[0].Overrides[0] = o;
            });
            Check(loaded[0].Overrides[0].Minus == SignalSymbol.MergeLeft && loaded[0].Overrides[0].MinusValue == 0, "1.0's MergeLeft kept");
            Check(loaded[1].Overrides[0].Plus == SignalSymbol.SpeedAuto, "untouched speed face restored");
        }

        private static void PlainFaceResetIn10Kept()
        {
            List<Gantry> loaded = ReloadAfter10(SpeedSample(), g =>
            {
                SignalOverride o = g[0].Overrides[0];
                o.Plus = SignalSymbol.Auto;
                g[0].Overrides[0] = o;
            });
            Check(loaded[0].Overrides[0].Plus == SignalSymbol.Auto, "Cross reset to Auto in 1.0 stays Auto");
            Check(loaded[0].Overrides[0].Minus == SignalSymbol.SpeedLimit && loaded[0].Overrides[0].MinusValue == 80, "speed face restored");
        }

        private static void UnknownExtSymbolIsAuto()
        {
            List<Gantry> gantries = Sample();
            SignalOverride o = gantries[0].Overrides[0];
            o.Plus = (SignalSymbol)300;
            o.PlusValue = 5;
            gantries[0].Overrides[0] = o;
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[0].Overrides[0].Plus == SignalSymbol.Auto, "future sign shows Auto");
            Check(loaded[0].Overrides[0].Minus == SignalSymbol.MergeRight, "known face kept");
        }

        private static void WarningsRoundTrip()
        {
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Roadworks, Plus = SignalSymbol.EndRestrictions, Sign = 1 });
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[1].Overrides[0].Minus == SignalSymbol.Roadworks && loaded[1].Overrides[0].Plus == SignalSymbol.EndRestrictions, "kept");
            string err;
            List<Gantry> by10 = Old10.Read(GantryData.WriteLegacy(gantries, new object()), out err);
            Check(err == null && by10[1].Overrides[0].Minus == SignalSymbol.Auto && by10[1].Overrides[0].Plus == SignalSymbol.Auto, "1.0 sees Auto");
        }

        private static void RampSignRoundTrip()
        {
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride
            {
                Offset = 4f, Minus = SignalSymbol.RampMetered, MinusValue = 4321, Plus = SignalSymbol.Cross, Sign = 1
            });
            gantries[1].Overrides.Add(new SignalOverride { Offset = 8f, Minus = SignalSymbol.RampMetered, Plus = SignalSymbol.Auto, Sign = -1 });
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            SignalOverride a = loaded[1].Overrides[0], b = loaded[1].Overrides[1];
            Check(a.Minus == SignalSymbol.RampMetered && a.MinusValue == 4321, "linked sign keeps its node");
            Check(a.Plus == SignalSymbol.Cross, "other face kept");
            Check(b.Minus == SignalSymbol.RampMetered && b.MinusValue == 0, "unlinked sign stays unlinked");
            AssertSameCore(gantries, loaded);

            string err;
            List<Gantry> by10 = Old10.Read(GantryData.WriteLegacy(gantries, new object()), out err);
            Check(err == null && by10[1].Overrides[0].Minus == SignalSymbol.Auto && by10[1].Overrides[0].Plus == SignalSymbol.Cross,
                "1.0 sees Auto beside the Cross");

            // 1.1.0 reads extension versions up to 4 and turns symbols it doesn't know into Auto, so the block must stay v4.
            byte[] ext = GantryData.WriteExt(gantries);
            Check(BitConverter.ToInt32(ext, 0) == 4, "extension block still version 4");
        }

        private static void AtlasTiles()
        {
            foreach (SignStyle style in SignStyles.Available)
            {
                GantryTool.Render.SignAtlas atlas = GantryTool.Render.SignAtlas.For(style);
                var seen = new HashSet<int>();
                foreach (SignDef def in SignCatalog.All)
                {
                    int t = atlas.TileOf(new FaceSign(def.Symbol, 0));
                    if (SignCatalog.IsAvailable(style, def.Symbol))
                    {
                        Check(def.Symbol == SignalSymbol.Blank || t != 5, style + ": " + def.Symbol + " has a tile");
                        Check(seen.Add(t), style + ": " + def.Symbol + " tile is unique");
                    }
                    else
                    {
                        int expected = atlas.TileOf(new FaceSign(SignCatalog.Fallback(style, def.Symbol), 0));
                        Check(t == expected, style + ": missing " + def.Symbol + " shows its fallback");
                    }
                }

                foreach (int v in SpeedUnits.Values(SignStyles.UsesMph(style)))
                {
                    int t = atlas.TileOf(new FaceSign(SignalSymbol.SpeedLimit, SpeedUnits.ToKmh(v, SignStyles.UsesMph(style))));
                    Check(t != 5 && seen.Add(t), style + ": speed " + v + " has its own tile");
                }

                Check(atlas.TileOf(new FaceSign((SignalSymbol)300, 0)) == 5, style + ": unknown sign draws blank");
                Check(atlas.Height <= 2048, style + ": atlas fits (" + atlas.Height + " px)");
            }

            GantryTool.Render.SignAtlas eu = GantryTool.Render.SignAtlas.For(SignStyle.Europe);
            for (int s = 1; s <= 6; s++)
            {
                Check(eu.TileOf(new FaceSign((SignalSymbol)s, 0)) == s - 1, "1.0 tile layout unchanged for " + (SignalSymbol)s);
            }

            SignalSymbol am, ap;
            SignalRules.Auto(1, GantryTool.Detect.LaneRestriction.None, out am, out ap, SignStyle.UnitedKingdom);
            Check(am == SignalSymbol.Blank && ap == SignalSymbol.Cross, "UK: open lanes blank on Auto");
            Check(SignCatalog.IsAvailable(SignStyle.UnitedKingdom, SignalSymbol.Arrow), "UK: green arrow can be picked");
            SignalRules.Auto(1, GantryTool.Detect.LaneRestriction.None, out am, out ap, SignStyle.Europe);
            Check(am == SignalSymbol.Arrow, "Europe: Auto unchanged");
            Check(SignCatalog.Fallback(SignStyle.Europe, SignalSymbol.YellowCross) == SignalSymbol.Cross, "yellow X -> red X");
            Check(SignCatalog.Fallback(SignStyle.Netherlands, SignalSymbol.Roadworks) == SignalSymbol.Blank, "NL: no warnings");
        }

        private static void CustomRoundTrip()
        {
            CustomSigns.Clear();
            ushort a = CustomSigns.AddText("stau", "5 km", 1);
            ushort b = CustomSigns.AddImage("logo.png", new byte[] { 1, 2, 3, 4 }, true);
            ushort unused = CustomSigns.AddText("unused", "", 0);
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Custom, MinusValue = a, Plus = SignalSymbol.Custom, PlusValue = b, Sign = 1 });

            byte[] bytes = CustomSigns.Write(CustomSigns.All(), CustomSigns.UsedIds(gantries, new object()));
            string err;
            List<CustomSign> read = CustomSigns.Read(bytes, out err);
            Check(err == null, "no error: " + err);
            Check(read.Count == 2, "unused sign dropped (" + read.Count + ")");
            CustomSign t = read.Find(x => x.Id == a), i = read.Find(x => x.Id == b);
            Check(t != null && t.Kind == CustomKind.Text && t.Line1 == "STAU" && t.Line2 == "5 KM" && t.Colour == 1, "text sign");
            Check(i != null && i.Kind == CustomKind.Image && i.LedLook && i.FileName == "logo.png" && i.Image.Length == 4 && i.Image[3] == 4, "image sign");
            Check(read.Find(x => x.Id == unused) == null, "unused not saved");
            CustomSigns.Clear();
        }

        private static void CustomFaces()
        {
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.Custom, MinusValue = 7, Plus = SignalSymbol.Arrow, Sign = 1 });
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[1].Overrides[0].Minus == SignalSymbol.Custom && loaded[1].Overrides[0].MinusValue == 7, "custom face and id kept");
            string err;
            List<Gantry> by10 = Old10.Read(GantryData.WriteLegacy(gantries, new object()), out err);
            Check(err == null && by10[1].Overrides[0].Minus == SignalSymbol.Auto && by10[1].Overrides[0].Plus == SignalSymbol.Arrow, "1.0 sees Auto");
            GantryTool.Render.SignAtlas atlas = GantryTool.Render.SignAtlas.For(SignStyle.Europe);
            Check(atlas.TileOf(new FaceSign(SignalSymbol.Custom, 999)) == 5, "a missing custom sign draws blank");
        }

        private static void CustomCorrupt()
        {
            CustomSigns.Clear();
            CustomSigns.AddText("A", "B", 0);
            CustomSigns.AddImage("x.png", new byte[300], false);
            byte[] good = CustomSigns.Write(CustomSigns.All(), null);
            string err;
            for (int cut = 0; cut < good.Length; cut += 7)
            {
                var part = new byte[cut];
                Array.Copy(good, part, cut);
                List<CustomSign> r = CustomSigns.Read(part, out err);
                Check(r != null, "never null");
            }

            var garbage = new byte[64];
            new System.Random(5).NextBytes(garbage);
            Check(CustomSigns.Read(garbage, out err) != null && err != null, "garbage reported, not thrown");
            CustomSigns.Clear();
        }

        private static void CustomLibraryRules()
        {
            CustomSigns.Clear();
            Check(CustomSigns.CleanLine("hello world") == "HELLO W", "upper-case, 7 characters");
            Check(CustomSigns.CleanLine("a{b}c") == "ABC", "unknown characters dropped");
            Check(CustomSigns.AddText("", "  ", 0) == 0, "empty text refused");
            ushort x = CustomSigns.AddText("exit", "12", 2);
            Check(x != 0 && CustomSigns.AddText("EXIT", "12", 2) == x, "duplicate reused");
            Check(CustomSigns.AddText("exit", "12", 3) != x, "different colour is a new sign");
            Check(CustomSigns.AddImage("big.png", new byte[CustomSigns.MaxImageBytes + 1], false) == 0, "oversized picture refused");
            for (int i = 0; i < CustomSigns.MaxSigns + 5; i++)
            {
                CustomSigns.AddText("S" + i, "", 0);
            }

            Check(CustomSigns.Count == CustomSigns.MaxSigns, "library capped at " + CustomSigns.MaxSigns);
            Check(CustomSigns.AddText("MORE", "", 0) == 0, "full library refuses new signs");
            CustomSigns.Clear();
        }

        private static void CustomAtlasFits()
        {
            CustomSigns.Clear();
            for (int i = 0; i < CustomSigns.MaxSigns; i++)
            {
                CustomSigns.AddText("T" + i, "", 0);
            }

            foreach (SignStyle style in SignStyles.Available)
            {
                GantryTool.Render.SignAtlas atlas = GantryTool.Render.SignAtlas.For(style);
                Check(atlas.Height <= 2048, style + ": " + atlas.Height + " px");
                Check(atlas.TileOf(new FaceSign(SignalSymbol.Arrow, 0)) == 0, style + ": row 1 unchanged");
                var seen = new HashSet<int>();
                foreach (CustomSign c in CustomSigns.All())
                {
                    Check(seen.Add(atlas.TileOf(new FaceSign(SignalSymbol.Custom, c.Id))), style + ": custom tiles unique");
                }
            }

            CustomSigns.Clear();
        }

        private static void RampSignNeedsRampMeter()
        {
            SignCatalog.RampMeterPresent = false;
            foreach (SignStyle style in SignStyles.Available)
            {
                Check(!SignCatalog.InCategory(style, SignCategory.Warning).Exists(d => d.Symbol == SignalSymbol.RampMetered),
                      style + ": hidden without Ramp Meter");
            }

            SignCatalog.RampMeterPresent = true;
            Check(SignCatalog.InCategory(SignStyle.Europe, SignCategory.Warning).Exists(d => d.Symbol == SignalSymbol.RampMetered),
                  "offered with Ramp Meter");
            SignCatalog.RampMeterPresent = false;
            GantryTool.Render.SignAtlas atlas = GantryTool.Render.SignAtlas.For(SignStyle.Europe);
            Check(atlas.TileOf(new FaceSign(SignalSymbol.RampMetered, 0)) != 5, "faces already showing it still draw it");
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
