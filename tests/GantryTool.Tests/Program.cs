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
            Run("guide signs: copy and paste", GuideCopyPaste);
            Run("guide signs: other countries", GuideCountries);
            Run("guide signs: on the truss unless a signal is in the way", GuideClearOfSignals);
            Run("guide signs: filled in from the road", GuideAutoFillParts);
            Run("guide signs: finding the next exit", GuideNextExit);
            Run("guide signs: default size, matching heights and the player's own size", GuideSizes);
            Run("guide signs: can be drawn on several threads at once", GuideThreads);
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
            Run("custom text: three lines round-trip; saves stay v1 without a third line", CustomThreeLines);
            Run("custom text: 10 wide letters fit the fine grid", CustomTextFits);
            Run("exit arrows: saved, have tiles, 1.0 sees Auto", ExitArrows);
            Run("guide signs: JSON reads and writes back", GuideJson);
            Run("guide signs: every template renders", GuideTemplates);
            Run("guide signs: layout grows with its content", GuideLayout);
            Run("guide signs: bad designs never throw", GuideBadDesigns);
            Run("guide signs: save data round-trips; old blocks unchanged; bad data is safe", GuideSaveData);
            Run("guide signs: dev saves without lane fitting still load", GuideOldSave);
            Run("guide signs: lanes are ordered as each face's drivers see them", GuideFaceLanes);
            Run("guide signs: a fitted sign spans its lanes with an arrow over each", GuideFitSpan);
            Run("guide signs: a sign too wide for its lanes shrinks; bad designs stay safe", GuideFitLimits);
            Run("guide signs: new signs take the lanes still free", GuideDefaultRange);
            Run("guide signs: signal backs can go blank; the setting is saved", GuideBlankBacks);
            Run("guide signs: every part can be found by where it was drawn", GuideParts);
            Run("guide signs: your own saved signs", GuideMySigns);
            Run("bare gantries: only new ones; old saves keep their signals", BareGantries);
            Run("arrangement view: lanes as drivers see them, and where a dragged sign lands", GuideArrangement);
            Run("guide signs: blank, lane closed and lane ending", GuideSimpleSigns);
            Run("guide signs: exit only sections cover their lanes", GuideExitSections);
            Run("guide signs: divider and exit only plate", GuideDividerAndPlate);
            Run("gantry types: posts, the median post, and saving", GantryTypes);
            Run("guide signs: dragging a part snaps it into place", GuideDragging);
            Run("guide signs: only the marked row becomes lane arrows", GuideLaneRow);
            Run("extra heads: round-trip under their own key", ExtraHeadsRoundTrip);
            Run("extra heads: stale, damaged or excess data is dropped safely", ExtraHeadsBadData);
            Run("extra heads: old save blocks are unchanged by them", ExtraHeadsLeaveOldBlocksAlone);
            Run("extra heads: unlinked shows only its own signs; linked reads its lane", ExtraHeadsResolve);
            Run("extra heads: position and size", ExtraHeadsLayout);
            Run("extra heads: their custom signs are saved", ExtraHeadsKeepCustomSigns);

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
            Check(CustomSigns.CleanLine("hello worlds") == "HELLO WORL", "upper-case, 10 characters");
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

        private static void CustomThreeLines()
        {
            CustomSigns.Clear();
            ushort two = CustomSigns.AddText("left lane", "closed", 1);
            byte[] v1 = CustomSigns.Write(CustomSigns.All(), null);
            Check(BitConverter.ToInt32(v1, 0) == 1, "no third line: still v1, so 1.1 can read it");
            string err;
            List<CustomSign> read = CustomSigns.Read(v1, out err);
            Check(err == null && read[0].Line1 == "LEFT LANE" && read[0].Line2 == "CLOSED" && read[0].Line3 == "", "v1 reads back");

            ushort three = CustomSigns.AddText("accident", "ahead", 2, "slow down");
            byte[] v2 = CustomSigns.Write(CustomSigns.All(), null);
            Check(BitConverter.ToInt32(v2, 0) == 2, "third line: v2");
            read = CustomSigns.Read(v2, out err);
            CustomSign t = read.Find(x => x.Id == three);
            Check(err == null && read.Count == 2 && t.Line1 == "ACCIDENT" && t.Line2 == "AHEAD" && t.Line3 == "SLOW DOWN" && t.Colour == 2, "v2 reads back");
            Check(read.Find(x => x.Id == two).Line3 == "", "other signs unaffected");
            Check(t.Lines.Length == 3 && t.Label == "ACCIDENT AHEAD SLOW DOWN", "lines and label");

            Check(CustomSigns.AddText("accident", "ahead", 2, "slow down") == three, "duplicate reused");
            ushort gap = CustomSigns.AddText("", "only", 0, "this");
            CustomSign g = CustomSigns.Get(gap);
            Check(g.Line1 == "ONLY" && g.Line2 == "THIS" && g.Line3 == "", "empty lines close up");
            CustomSigns.Clear();
        }

        private static void CustomTextFits()
        {
            int wide = GantryTool.Render.LedFont.Width("WWWWWWWWWW");
            Check(wide <= 62, "10 wide letters are " + wide + " dots on a 64-dot grid");
            Check(3 * GantryTool.Render.LedFont.Height + 2 * 3 <= 38, "3 lines fit the normal grid's height");
        }

        private static void ExitArrows()
        {
            List<Gantry> gantries = Sample();
            gantries[1].Overrides.Add(new SignalOverride { Offset = 4f, Minus = SignalSymbol.ExitLeft, Plus = SignalSymbol.ExitRight, Sign = 1 });
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            Check(loaded[1].Overrides[0].Minus == SignalSymbol.ExitLeft && loaded[1].Overrides[0].Plus == SignalSymbol.ExitRight, "round-trip");
            string err;
            List<Gantry> by10 = Old10.Read(GantryData.WriteLegacy(gantries, new object()), out err);
            Check(err == null && by10[1].Overrides[0].Minus == SignalSymbol.Auto && by10[1].Overrides[0].Plus == SignalSymbol.Auto, "1.0 sees Auto");
            foreach (SignStyle style in SignStyles.Available)
            {
                GantryTool.Render.SignAtlas atlas = GantryTool.Render.SignAtlas.For(style);
                int l = atlas.TileOf(new FaceSign(SignalSymbol.ExitLeft, 0)), r = atlas.TileOf(new FaceSign(SignalSymbol.ExitRight, 0));
                Check(l != 5 && r != 5 && l != r, style + ": own tiles");
                Check(atlas.TileOf(new FaceSign(SignalSymbol.Arrow, 0)) == 0, style + ": row 1 unchanged");
            }
        }

        private static void GuideJson()
        {
            string text = "{\"a\":[1,2.5,-3e2,true,false,null],\"b\":{\"c\":\"x\\n\\\"y\\u00e9\"},\"d\":[]}";
            var o = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(text);
            var a = (List<object>)o["a"];
            Check((double)a[1] == 2.5 && (double)a[2] == -300.0 && (bool)a[3] && a[5] == null, "values");
            Check(GantryTool.Guide.Json.Str((Dictionary<string, object>)o["b"], "c") == "x\n\"y\u00e9", "escapes");
            var again = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(GantryTool.Guide.Json.Write(o));
            Check(GantryTool.Guide.Json.Write(again) == GantryTool.Guide.Json.Write(o), "round-trip");
            foreach (string bad in new[] { "", "{", "[1,", "{\"a\"}", "tru", "\"x", "[1 2]", "{}x" })
            {
                bool threw = false;
                try
                {
                    GantryTool.Guide.Json.Parse(bad);
                }
                catch (FormatException)
                {
                    threw = true;
                }

                Check(threw, "rejects: " + bad);
            }
        }

        private static void GuideTemplates()
        {
            List<string> names = GantryTool.Guide.GuideAssets.TemplateNames("us");
            Check(names.Count >= 13, names.Count + " templates");
            foreach (string name in names)
            {
                GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(GantryTool.Guide.GuideAssets.Template("us", name));
                Check(s.Width > 10f && s.Height > 10f, name + " has a size");
                Check(s.PixelWidth <= 1024 && s.PixelHeight <= 1024, name + " fits the texture limit");
                int solid = 0;
                foreach (Color32 c in s.Pixels)
                {
                    solid += c.a == 255 ? 1 : 0;
                }

                Check(solid > s.Pixels.Length / 2, name + " is mostly solid");
            }
        }

        private static Dictionary<string, object> GuideText(string text)
        {
            var item = new Dictionary<string, object> { { "type", "text" }, { "text", text } };
            return new Dictionary<string, object> { { "theme", "us" }, { "items", new List<object> { item } } };
        }

        private static void GuideLayout()
        {
            GantryTool.Guide.GuideSign small = GantryTool.Guide.GuideRenderer.Render(GuideText("Bo"));
            GantryTool.Guide.GuideSign big = GantryTool.Guide.GuideRenderer.Render(GuideText("Boston Downtown"));
            Check(big.Width > small.Width * 2f && big.Height == small.Height, "longer text is wider, not taller");
            Dictionary<string, object> doc = GuideText("Boston");
            doc["minWidth"] = 400.0;
            Check(GantryTool.Guide.GuideRenderer.Render(doc).Width == 400f, "minimum width");
            var tab = new Dictionary<string, object> { { "type", "tab" }, { "items", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", "EXIT 1" }, { "height", 10.0 } } } } };
            Dictionary<string, object> withTab = GuideText("Boston");
            ((List<object>)withTab["items"]).Insert(0, tab);
            GantryTool.Guide.GuideSign t = GantryTool.Guide.GuideRenderer.Render(withTab);
            Check(t.TabHeight > 0f && t.Height > small.Height, "a tab adds height on top");
            Check(t.Pixels[0].a == 0, "the corner beside a right tab is transparent");
        }

        private static void GuideBadDesigns()
        {
            var r = new System.Random(3);
            var designs = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>(),
                new Dictionary<string, object> { { "theme", "nosuchtheme" }, { "items", new List<object> { 5.0, null, "x" } } },
                new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "shield" }, { "network", "NOPE" }, { "number", 12.0 } } } } },
                new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "arrow" }, { "direction", "sideways" }, { "height", 0.0 } } } } },
                new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", "\u2603\u00e9" }, { "font", "NoSuchFont" } } } } },
                new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "panel" } }, new Dictionary<string, object> { { "type", "row" }, { "items", 7.0 } }, new Dictionary<string, object> { { "type", "diagram" } } } } },
                new Dictionary<string, object> { { "minWidth", 99999.0 }, { "height", 99999.0 }, { "items", new List<object>() } },
                new Dictionary<string, object> { { "minWidth", -5.0 }, { "border", -3.0 }, { "cornerRadius", -2.0 }, { "margin", -9.0 } }
            };
            foreach (Dictionary<string, object> d in designs)
            {
                GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(d, 256);
                Check(s.PixelWidth >= 1 && s.PixelHeight >= 1 && s.PixelWidth <= 256 && s.PixelHeight <= 256, "bad design still draws inside the limit");
            }
        }

        private static List<GantryTool.Detect.GantryLane> GuideLanes()
        {
            var lanes = new List<GantryTool.Detect.GantryLane>();
            for (int i = 0; i < 4; i++)
            {
                lanes.Add(new GantryTool.Detect.GantryLane { Offset = 5f + i * 3f, Width = 3f, TravelSign = i < 3 ? 1 : -1 });
            }

            return lanes;
        }

        private static void GuideOldSave()
        {
            byte[] bytes = Bytes(w =>
            {
                List<Gantry> g = Sample();
                w.Write(1);
                w.Write(1);
                w.Write(g[1].Id);
                foreach (float v in new[] { g[1].Start.x, g[1].Start.y, g[1].Start.z, g[1].End.x, g[1].End.y, g[1].End.z })
                {
                    w.Write(v);
                }

                w.Write(true);
                w.Write(1);
                w.Write((byte)1);
                w.Write(2f);
                w.Write(1f);
                w.Write((sbyte)-1);
                w.Write("{}");
            });
            List<Gantry> loaded = Sample();
            string err;
            GuideData.Apply(bytes, loaded, out err);
            Check(err == null, "no error: " + err);
            Check(loaded[1].GuideSigns.Count == 1 && loaded[1].GuideSigns[0].FitFirst == 0 && !loaded[1].GuideSigns[0].LaneArrows, "loads as a hand-placed sign");

            loaded[1].GuideSigns[0] = new GuideHead { Id = 1, Along = 2f, Scale = 1f, Side = 1, Design = "{}", FitFirst = 2, FitLast = 3, LaneArrows = true };
            List<Gantry> again = Sample();
            GuideData.Apply(GuideData.Write(loaded, new object()), again, out err);
            GuideHead h = again[1].GuideSigns[0];
            Check(err == null && h.FitFirst == 2 && h.FitLast == 3 && h.LaneArrows, "lane fitting is saved");
        }

        private static void GuideFaceLanes()
        {
            List<GantryTool.Detect.GantryLane> minus = GantryTool.Guide.GuideFit.FaceLanes(GuideLanes(), -1);
            Check(minus.Count == 3 && minus[0].Offset == 5f && minus[2].Offset == 11f, "-side face: along the beam");
            List<GantryTool.Detect.GantryLane> plus = GantryTool.Guide.GuideFit.FaceLanes(GuideLanes(), 1);
            Check(plus.Count == 1 && plus[0].Offset == 14f, "+side face: the other direction");
            var two = new List<GantryTool.Detect.GantryLane> { new GantryTool.Detect.GantryLane { Offset = 1f }, new GantryTool.Detect.GantryLane { Offset = 2f } };
            Check(GantryTool.Guide.GuideFit.FaceLanes(two, 1)[0].Offset == 2f, "two-way lanes show on both; +side face runs back along the beam");
        }

        /// <summary>Middles of the arrows in a row of pixels through a rendered sign, in sign units from its centre.</summary>
        private static List<float> ArrowCentres(GantryTool.Guide.GuideSign s, int y)
        {
            var found = new List<float>();
            float ppu = s.PixelWidth / s.Width;
            int margin = Mathf.CeilToInt(8f * ppu), start = -1;
            for (int x = margin; x < s.PixelWidth - margin; x++)
            {
                bool light = s.Pixels[y * s.PixelWidth + x].r > 128;
                if (light && start < 0)
                {
                    start = x;
                }
                else if (!light && start >= 0)
                {
                    found.Add(((start + x) * 0.5f) / ppu - s.Width * 0.5f);
                    start = -1;
                }
            }

            return found;
        }

        private static void GuideFitSpan()
        {
            string design = GantryTool.Guide.Json.Write(GantryTool.Guide.GuideAssets.Template("us", "pull-through"));
            var h = new GuideHead { Id = 1, Along = 1f, Scale = 1f, Side = -1, Design = design, FitFirst = 1, FitLast = 3, LaneArrows = true };
            GantryTool.Guide.GuidePlacement p = GantryTool.Guide.GuideFit.Resolve(h, GuideLanes());
            Check(p.Fitted && Mathf.Abs(p.Along - 8f) < 0.01f, "centred over the lanes (" + p.Along + ")");
            var doc = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(p.Design);
            GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(doc, 2048, 3f);
            float metres = s.Width * GantryTool.Guide.GuideFit.MetresPerUnit * p.Scale;
            Check(Mathf.Abs(metres - 8.6f) < 0.05f, "as wide as the lanes, less a small gap (" + metres + ")");

            int y = s.PixelHeight - Mathf.CeilToInt(28f * s.PixelHeight / s.Height); // through the arrows: 16 of border and margin, then 24 tall
            List<float> arrows = ArrowCentres(s, y);
            float unit = 3f / (GantryTool.Guide.GuideFit.MetresPerUnit * p.Scale);
            Check(arrows.Count == 3, "one arrow per lane (" + arrows.Count + ")");
            for (int i = 0; i < 3 && i < arrows.Count; i++)
            {
                Check(Mathf.Abs(arrows[i] - (i - 1) * unit) < 4f, "arrow " + i + " over its lane (" + arrows[i] + " vs " + (i - 1) * unit + ")");
            }

            h.FitFirst = h.FitLast = 2;
            p = GantryTool.Guide.GuideFit.Resolve(h, GuideLanes());
            Check(Mathf.Abs(p.Along - 8f) < 0.01f, "one lane: over that lane");
            h.FitFirst = 9;
            h.FitLast = 9;
            Check(Mathf.Abs(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Along - 11f) < 0.01f, "lanes taken away: falls back to the last one");
            h.FitFirst = 1;
            h.FitLast = 3;
            h.LaneArrows = false;
            string plain = GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design;
            Check(plain.Contains("\"arrow\"") && !plain.Contains("laneArrows"), "without lane arrows the design's own stay");
            h.FitFirst = 0;
            Check(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design == design, "not fitted: untouched");
        }

        private static void GuideFitLimits()
        {
            string wide = GantryTool.Guide.Json.Write(GuideText("A very long place name indeed, far too long for one lane"));
            var h = new GuideHead { Id = 1, Scale = 1f, Side = -1, Design = wide, FitFirst = 1, FitLast = 1 };
            GantryTool.Guide.GuidePlacement p = GantryTool.Guide.GuideFit.Resolve(h, GuideLanes());
            Check(p.Scale < 1f && p.Scale >= GuideData.MinScale, "legend shrinks (" + p.Scale + ")");
            foreach (string bad in new[] { "", "not json", "[1,2]", "{\"items\":7}", "{\"items\":[{\"type\":\"row\",\"items\":[]}]}" })
            {
                h.Design = bad;
                h.LaneArrows = true;
                GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()); // must not throw
            }

            h.Design = wide;
            Check(!GantryTool.Guide.GuideFit.Resolve(h, new List<GantryTool.Detect.GantryLane>()).Fitted, "no lanes: placed by hand");
            var odd = new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "laneArrows" }, { "at", new List<object> { "x", null, 1e9, -1e9 } } } } } };
            GantryTool.Guide.GuideRenderer.Render(odd, 256);
            GantryTool.Guide.GuideSign big = GantryTool.Guide.GuideRenderer.Render(new Dictionary<string, object> { { "minWidth", 2000.0 }, { "height", 800.0 } }, 2048, 3f);
            Check(big.PixelWidth * big.PixelHeight <= 1100000, "area stays limited (" + big.PixelWidth + " x " + big.PixelHeight + ")");
        }

        private static void GuideDefaultRange()
        {
            byte first, last;
            var signs = new List<GuideHead>();
            GantryTool.Guide.GuideFit.DefaultRange(GuideLanes(), signs, -1, out first, out last);
            Check(first == 1 && last == 3, "all lanes of that face");
            signs.Add(new GuideHead { Id = 1, Side = -1, FitFirst = 1, FitLast = 2 });
            GantryTool.Guide.GuideFit.DefaultRange(GuideLanes(), signs, -1, out first, out last);
            Check(first == 3 && last == 3, "what is still free");
            GantryTool.Guide.GuideFit.DefaultRange(GuideLanes(), signs, 1, out first, out last);
            Check(first == 1 && last == 1, "the other face is separate");
            signs.Add(new GuideHead { Id = 2, Side = -1, FitFirst = 3, FitLast = 3 });
            GantryTool.Guide.GuideFit.DefaultRange(GuideLanes(), signs, -1, out first, out last);
            Check(first == 1 && last == 3, "none free: all of them");
            GantryTool.Guide.GuideFit.DefaultRange(new List<GantryTool.Detect.GantryLane>(), signs, -1, out first, out last);
            Check(first == 0 && last == 0, "no lanes: placed by hand");
        }

        private static void GuideLaneRow()
        {
            Func<string, Dictionary<string, object>> row = dir => new Dictionary<string, object>
            {
                { "type", "row" },
                { "items", new List<object> { new Dictionary<string, object> { { "type", "arrow" }, { "direction", dir } } } }
            };
            Dictionary<string, object> first = row("up-right"), second = row("up");
            var doc = new Dictionary<string, object> { { "theme", "us" }, { "items", new List<object> { first, second } } };
            Check(GantryTool.Guide.GuideFit.LaneRowIndex(doc) == 0, "no mark: the first row of arrows");
            second[GantryTool.Guide.GuideFit.PerLaneKey] = true;
            Check(GantryTool.Guide.GuideFit.LaneRowIndex(doc) == 1, "the marked row wins");

            var h = new GuideHead { Id = 1, Scale = 1f, Side = -1, Design = GantryTool.Guide.Json.Write(doc), FitFirst = 1, FitLast = 3, LaneArrows = true };
            var fitted = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design);
            List<object> items = GantryTool.Guide.Json.Arr(fitted, "items");
            var lanes = (Dictionary<string, object>)items[1];
            Check(GantryTool.Guide.Json.Str((Dictionary<string, object>)items[0], "type") == "row", "the single arrow stays");
            Check(GantryTool.Guide.Json.Str(lanes, "type") == "laneArrows", "the marked row takes the lanes");
            Check(GantryTool.Guide.Json.Arr(lanes, "at").Count == 3 && GantryTool.Guide.Json.Arr(lanes, "items").Count == 3, "one per lane");
            Check(GantryTool.Guide.Json.Str((Dictionary<string, object>)GantryTool.Guide.Json.Arr(lanes, "items")[2], "direction") == "up", "new lanes copy the arrow before them");

            // Each lane has its own arrow: an exit lane can split while the others go straight.
            Dictionary<string, object> split = row("up");
            GantryTool.Guide.Json.Arr(split, "items").Add(new Dictionary<string, object> { { "type", "arrow" }, { "direction", "none" } });
            GantryTool.Guide.Json.Arr(split, "items").Add(new Dictionary<string, object> { { "type", "arrow" }, { "direction", "split-right" } });
            split[GantryTool.Guide.GuideFit.PerLaneKey] = true;
            h.Design = GantryTool.Guide.Json.Write(new Dictionary<string, object> { { "theme", "us" }, { "items", new List<object> { split } } });
            fitted = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design);
            var found = new Dictionary<string, GantryTool.Guide.GuidePart>();
            foreach (GantryTool.Guide.GuidePart p in GantryTool.Guide.GuideRenderer.Render(fitted).Parts)
            {
                found[p.Path] = p;
            }

            Check(found.ContainsKey("0/0") && found.ContainsKey("0/1") && found.ContainsKey("0/2"), "each lane's arrow can be clicked, even with none drawn");
            Check(found["0/0"].X < found["0/1"].X && found["0/1"].X < found["0/2"].X, "in lane order");
            Check(found["0"].Type == "laneArrows" && found["0"].W > found["0/0"].W, "the whole row is there too");

            // Arrows mixed with other parts come out into their own row, the other parts staying.
            var chip = new Dictionary<string, object> { { "type", "chip" }, { "items", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", "50" } } } } };
            var mixed = new Dictionary<string, object>
            {
                { "type", "row" },
                { "items", new List<object> { new Dictionary<string, object> { { "type", "arrow" }, { "direction", "turn-left" } }, chip, new Dictionary<string, object> { { "type", "arrow" }, { "direction", "up" } } } }
            };
            var mixedDoc = new Dictionary<string, object> { { "items", new List<object> { mixed } } };
            Dictionary<string, object> made = GantryTool.Guide.GuideFit.MakeLaneRow(mixedDoc);
            Check(GantryTool.Guide.Json.Arr(mixed, "items").Count == 1 && GantryTool.Guide.Json.Arr(mixed, "items")[0] == chip, "the plate stays where it was");
            Check(GantryTool.Guide.Json.Arr(made, "items").Count == 2 && GantryTool.Guide.Json.Str((Dictionary<string, object>)GantryTool.Guide.Json.Arr(made, "items")[0], "direction") == "turn-left", "the arrows move, in order");
            Check(GantryTool.Guide.GuideFit.LaneRowIndex(mixedDoc) == 1, "and become the lane row");
            Check(GantryTool.Guide.GuideFit.MakeLaneRow(mixedDoc) == made, "doing it again changes nothing");
            Check(GantryTool.Guide.GuideFit.LaneRowIndex(new Dictionary<string, object> { { "items", new List<object>() } }) == -1, "no arrows at all");
        }

        private static GuideHead SizedSign(byte id, string template, Action<Dictionary<string, object>> change = null)
        {
            Dictionary<string, object> doc = GantryTool.Guide.GuideAssets.Template("us", template);
            if (change != null)
            {
                change(doc);
            }

            return new GuideHead { Id = id, Along = 10f, Side = -1, Scale = 1f, Design = GantryTool.Guide.Json.Write(doc) };
        }

        private static void GuideSizes()
        {
            var none = new List<GantryTool.Detect.GantryLane>();
            var signs = new List<GuideHead> { SizedSign(1, "lane-ending"), SizedSign(2, "pull-through"), SizedSign(3, "guide-exit-only"), SizedSign(4, "lane-closed"), SizedSign(5, "gore-exit") };
            Dictionary<byte, GantryTool.Guide.GuidePlacement> placed = GantryTool.Guide.GuideFit.Arrange(signs, none);
            float tall = placed[3].PanelMetres;
            Check(tall >= GantryTool.Guide.GuideFit.DefaultHeight, "no sign starts below the default height");
            foreach (GuideHead h in signs)
            {
                Check(Mathf.Abs(placed[h.Id].PanelMetres - tall) < 0.03f, "signs side by side match in height: " + h.Id);
            }

            Check(Mathf.Abs(placed[1].WidthMetres - placed[1].HeightMetres) < 0.03f, "the lane ending sign stays square");
            Check(Mathf.Abs(placed[4].WidthMetres - placed[4].HeightMetres) < 0.03f, "the lane closed sign stays square");
            Check(placed[5].WidthMetres >= GantryTool.Guide.GuideFit.DefaultWidth - 0.03f, "no plain sign starts below the default width");

            signs[1] = SizedSign(2, "pull-through", d =>
            {
                d[GantryTool.Guide.GuideFit.WidthKey] = 8.0;
                d[GantryTool.Guide.GuideFit.HeightKey] = 4.0;
            });
            placed = GantryTool.Guide.GuideFit.Arrange(signs, none);
            Check(Mathf.Abs(placed[2].WidthMetres - 8f) < 0.03f && Mathf.Abs(placed[2].PanelMetres - 4f) < 0.03f, "the player's own size is used");
            Check(Mathf.Abs(placed[3].PanelMetres - tall) < 0.03f, "a sign with its own height doesn't change the others");

            signs[1] = SizedSign(2, "pull-through", d => d[GantryTool.Guide.GuideFit.HeightKey] = 1.2);
            placed = GantryTool.Guide.GuideFit.Arrange(signs, none);
            Check(Mathf.Abs(placed[2].PanelMetres - 1.2f) < 0.03f, "a short sign shrinks its legend to fit");

            GuideHead other = SizedSign(3, "guide-long");
            other.Side = 1;
            GuideHead pull = SizedSign(2, "pull-through");
            placed = GantryTool.Guide.GuideFit.Arrange(new List<GuideHead> { pull, other }, none);
            Check(placed[2].PanelMetres < placed[3].PanelMetres - 0.01f, "signs facing the other way don't count");
        }

        private static void GuideThreads()
        {
            string design = GantryTool.Guide.Json.Write(GantryTool.Guide.GuideAssets.Template("us", "guide-exit-only"));
            GantryTool.Guide.GuideSign alone = GantryTool.Guide.GuideRenderer.Render((Dictionary<string, object>)GantryTool.Guide.Json.Parse(design), 2048, 3f);
            var results = new GantryTool.Guide.GuideSign[6];
            var threads = new System.Threading.Thread[results.Length];
            for (int i = 0; i < threads.Length; i++)
            {
                int n = i;
                threads[i] = new System.Threading.Thread(() => results[n] = GantryTool.Guide.GuideRenderer.Render((Dictionary<string, object>)GantryTool.Guide.Json.Parse(design), 2048, 3f));
                threads[i].Start();
            }

            foreach (System.Threading.Thread t in threads)
            {
                t.Join();
            }

            foreach (GantryTool.Guide.GuideSign r in results)
            {
                bool same = r != null && r.Parts.Count == alone.Parts.Count && r.Pixels.Length == alone.Pixels.Length;
                for (int i = 0; same && i < r.Pixels.Length; i++)
                {
                    same = r.Pixels[i].r == alone.Pixels[i].r && r.Pixels[i].g == alone.Pixels[i].g && r.Pixels[i].b == alone.Pixels[i].b && r.Pixels[i].a == alone.Pixels[i].a;
                }

                Check(same, "a sign drawn alongside others comes out the same");
            }
        }

        private static string TemplateText(Dictionary<string, object> doc)
        {
            return GantryTool.Guide.Json.Write(doc);
        }

        private static void GuideAutoFillParts()
        {
            Func<string, Dictionary<string, object>> t = n => GantryTool.Guide.GuideAssets.Template("us", n);
            Dictionary<string, object> doc = t("pull-through");
            Check(GantryTool.Guide.GuideAutoFill.Fill(doc, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "Highway 47", Heading = new Vector2(0f, 1f) } }), "a numbered road fills in");
            string s = TemplateText(doc);
            Check(s.Contains("\"47\"") && s.Contains("NORTH") && !s.Contains("Albany") && !s.Contains("Highway"), "the number goes on the shield, and the sample town goes");

            doc = t("pull-through");
            GantryTool.Guide.GuideAutoFill.Fill(doc, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "Oak Street", Heading = new Vector2(-1f, 0.2f) } });
            s = TemplateText(doc);
            Check(s.Contains("Oak Street") && s.Contains("WEST") && !s.Contains("shield"), "a road with no number has its name and no shield");
            Check(GantryTool.Guide.GuideRenderer.Render(doc, 1024, 1f).Parts.Count > 0, "it still draws");

            doc = t("guide-exit-tab");
            GantryTool.Guide.GuideAutoFill.Fill(doc, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "12th Street", Heading = Vector2.zero }, new GantryTool.Guide.RoadFacts { Name = "Elm Avenue", Heading = new Vector2(1f, 0f) } });
            s = TemplateText(doc);
            Check(s.Contains("12th Street") && s.Contains("Elm Avenue") && !s.Contains("Portsmouth") && !s.Contains("NORTH"), "two destinations; no direction when it goes both ways");

            doc = t("guide-exit-tab");
            string before = TemplateText(doc);
            Check(!GantryTool.Guide.GuideAutoFill.Fill(doc, new List<GantryTool.Guide.RoadFacts>()) && TemplateText(doc) == before, "nothing found leaves the sign alone");
            Check(!GantryTool.Guide.GuideAutoFill.Fill(doc, null) && TemplateText(doc) == before, "nor does no answer at all");
            Check(!GantryTool.Guide.GuideAutoFill.Fill(doc, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "\u041c\u0438\u0440\u0430" } }) && TemplateText(doc) == before, "letters the sign can't draw leave it alone");

            Dictionary<string, object> plain = t("junction");
            Check(!GantryTool.Guide.GuideAutoFill.Fill(plain, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "Highway 1" } }), "a sign with nothing marked isn't touched");
            foreach (string name in GantryTool.Guide.GuideAssets.TemplateNames("us"))
            {
                Check(GantryTool.Guide.GuideRenderer.Render(t(name), 1024, 1f) != null, name);
            }
        }

        private sealed class FakeRoads : GantryTool.Guide.IRoadNetwork
        {
            public readonly Dictionary<ushort, Vector3> Nodes = new Dictionary<ushort, Vector3>();
            private readonly Dictionary<ushort, ushort[]> _segments = new Dictionary<ushort, ushort[]>();
            private readonly Dictionary<ushort, string> _names = new Dictionary<ushort, string>();
            private readonly HashSet<ushort> _ramps = new HashSet<ushort>();

            public void Road(ushort seg, ushort a, ushort b, string name, bool ramp = false)
            {
                _segments[seg] = new[] { a, b };
                _names[seg] = name;
                if (ramp)
                {
                    _ramps.Add(seg);
                }
            }

            public List<ushort> Out(ushort node)
            {
                var list = new List<ushort>();
                foreach (KeyValuePair<ushort, ushort[]> s in _segments)
                {
                    if (s.Value[0] == node)
                    {
                        list.Add(s.Key);
                    }
                }

                return list;
            }

            public ushort OtherNode(ushort segment, ushort node)
            {
                return _segments[segment][0] == node ? _segments[segment][1] : _segments[segment][0];
            }

            public Vector3 Position(ushort node) { return Nodes[node]; }
            public string Name(ushort segment) { return _names[segment]; }
            public bool SameName(ushort a, ushort b) { return _names[a] == _names[b]; }
            public bool IsRamp(ushort segment) { return _ramps.Contains(segment); }
        }

        private static void GuideNextExit()
        {
            // A highway running north: an on-ramp joins at node 2 (no way out there), then at node 3 an off-ramp leaves,
            // and splits at node 5 onto a street going both ways and a second road.
            var net = new FakeRoads();
            net.Nodes[1] = new Vector3(0f, 0f, 0f);
            net.Nodes[2] = new Vector3(0f, 0f, 300f);
            net.Nodes[3] = new Vector3(0f, 0f, 600f);
            net.Nodes[4] = new Vector3(0f, 0f, 900f);
            net.Nodes[5] = new Vector3(100f, 0f, 700f);
            net.Nodes[6] = new Vector3(300f, 0f, 700f);
            net.Nodes[7] = new Vector3(-300f, 0f, 700f);
            net.Nodes[8] = new Vector3(100f, 0f, 1000f);
            net.Road(1, 1, 2, "Highway 90");
            net.Road(2, 2, 3, "Highway 90");
            net.Road(3, 3, 4, "Highway 90");
            net.Road(4, 3, 5, "Ramp", true);
            net.Road(5, 5, 6, "Oak Street");
            net.Road(6, 5, 7, "Oak Street");
            net.Road(7, 5, 8, "Route 12");
            List<GantryTool.Guide.RoadFacts> found = GantryTool.Guide.RoadWalk.NextExit(net, 1, 2);
            Check(found.Count == 2, "the ramp's split gives both roads: " + found.Count);
            GantryTool.Guide.RoadFacts oak = found.Find(r => r.Name == "Oak Street"), route = found.Find(r => r.Name == "Route 12");
            Check(oak != null && oak.Heading == Vector2.zero, "a road going both ways from the ramp has no one direction");
            Check(route != null && GantryTool.Guide.GuideAutoFill.Cardinal(route.Heading) == "NORTH" && route.Number == "12", "the other has its direction and number");

            // Nothing leaves it before the road ends, or within reach.
            var plain = new FakeRoads();
            plain.Nodes[1] = new Vector3(0f, 0f, 0f);
            plain.Nodes[2] = new Vector3(0f, 0f, 300f);
            plain.Nodes[3] = new Vector3(0f, 0f, 600f);
            plain.Road(1, 1, 2, "Highway 90");
            plain.Road(2, 2, 3, "Highway 90");
            Check(GantryTool.Guide.RoadWalk.NextExit(plain, 1, 2).Count == 0, "no exit on a road that just ends");

            // A loop must not trap it.
            var loop = new FakeRoads();
            loop.Nodes[1] = new Vector3(0f, 0f, 0f);
            loop.Nodes[2] = new Vector3(0f, 0f, 10f);
            loop.Road(1, 1, 2, "Ring");
            loop.Road(2, 2, 1, "Ring");
            Check(GantryTool.Guide.RoadWalk.NextExit(loop, 1, 2).Count == 0, "a loop with no exit ends");

            Check(new GantryTool.Guide.RoadFacts { Name = "12th Street" }.Number == null, "12th Street has no route number");
            Check(new GantryTool.Guide.RoadFacts { Name = "Highway 47" }.Number == "47", "Highway 47 has");
            Check(GantryTool.Guide.GuideAutoFill.Cardinal(new Vector2(0f, -1f)) == "SOUTH" && GantryTool.Guide.GuideAutoFill.Cardinal(new Vector2(1f, 0.1f)) == "EAST", "compass");
        }

        private static void GuideClearOfSignals()
        {
            var g = new Gantry { Start = new Vector3(0f, 0f, 0f), End = new Vector3(30f, 0f, 0f), BeamBottomY = 8f, BeamDepth = 1.5f };
            g.Lanes.Add(new GantryTool.Detect.GantryLane { Offset = 5f, Width = 3.6f, TravelSign = 1 });
            g.Lanes.Add(new GantryTool.Detect.GantryLane { Offset = 25f, Width = 3.6f, TravelSign = 1 });
            GuideHead sign = SizedSign(1, "pull-through");
            sign.Along = 5f;
            g.GuideSigns.Add(sign);
            float over = GantryTool.Render.GuideMeshBuilder.MidY(g, sign);

            g.Overrides.Add(new SignalOverride { Offset = 5f, Minus = SignalSymbol.Hidden, Plus = SignalSymbol.Hidden, Sign = 1 });
            float clear = GantryTool.Render.GuideMeshBuilder.MidY(g, sign);
            Check(clear < over - 0.5f, "with the signal under it deleted, a sign comes down onto the truss");

            sign.Along = 25f;
            g.GuideSigns[0] = sign;
            Check(Mathf.Abs(GantryTool.Render.GuideMeshBuilder.MidY(g, sign) - over) < 0.01f, "over the other signal, it stands on top");

            GuideHead second = SizedSign(2, "pull-through");
            second.Along = 5f;
            g.GuideSigns.Add(second);
            Check(Mathf.Abs(GantryTool.Render.GuideMeshBuilder.MidY(g, second) - over) < 0.01f, "signs facing the same way stay in line");

            second.Side = 1;
            g.GuideSigns[1] = second;
            Check(GantryTool.Render.GuideMeshBuilder.MidY(g, second) < over - 0.5f, "signs facing the other way don't count");
        }

        private static void GuideCountries()
        {
            List<string> ids = GantryTool.Guide.GuideAssets.Ids();
            Check(ids.Contains("us") && ids.Contains("ca"), "the US and Canada are there");
            foreach (string id in ids)
            {
                Check(GantryTool.Guide.GuideThemes.DefaultTemplate(id) != null, id + " has a sign to start from");
                foreach (string name in GantryTool.Guide.GuideAssets.TemplateNames(id))
                {
                    Dictionary<string, object> doc = GantryTool.Guide.GuideAssets.Template(id, name);
                    Check(GantryTool.Guide.Json.Str(doc, "theme") == id, id + "/" + name + " says it's from there");
                    GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(doc, 512, 1f);
                    Check(s != null && s.Width > 0f, id + "/" + name + " draws");
                }
            }

            GantryTool.Guide.GlyphFont shared = GantryTool.Guide.GuideAssets.Font("ca", "RG2014EM");
            Check(shared != null && shared.Has('\u00e9') && shared.Has('A'), "Canada uses the shared fonts, with accents");
            Check(GantryTool.Guide.GuideAssets.Image("ca", "lane-ends-right") != null && GantryTool.Guide.GuideAssets.Image("ca", "bc") != null, "and the shared symbols, with its own shields");

            Dictionary<string, object> auto = GantryTool.Guide.GuideAssets.Template("ca", "autoroute");
            Check(GantryTool.Guide.GuideAutoFill.Fill(auto, new List<GantryTool.Guide.RoadFacts> { new GantryTool.Guide.RoadFacts { Name = "Rue Montr\u00e9al", Heading = new Vector2(0f, -1f) } }), "accents fill in");
            string text = GantryTool.Guide.Json.Write(auto);
            Check(text.Contains("Sud") && !text.Contains("SOUTH"), "directions in the sign's own words");

            Dictionary<string, object> bc = GantryTool.Guide.GuideAssets.Template("ca", "pull-through");
            Check(GantryTool.Guide.Json.Str(GantryTool.Guide.GuideFit.NewArrow(bc), "direction") == "up", "new arrows on a B.C. sign point up");
            Check(GantryTool.Guide.Json.Str(GantryTool.Guide.GuideFit.NewArrow(GantryTool.Guide.GuideAssets.Template("ca", "exit-guide")), "direction") == "down", "and on others down");
            Check(GantryTool.Guide.Json.Str(GantryTool.Guide.GuideFit.NewArrow(bc), "style") == "slim", "and share their shape");
            var slimArrow = new Dictionary<string, object> { { "type", "arrow" }, { "direction", "up" }, { "style", "slim" }, { "height", 40.0 } };
            var wideArrow = new Dictionary<string, object> { { "type", "arrow" }, { "direction", "up" }, { "height", 40.0 } };
            float slimWidth = GantryTool.Guide.GuideRenderer.Render(new Dictionary<string, object> { { "theme", "ca" }, { "margin", 0.0 }, { "border", 0.0 }, { "items", new List<object> { slimArrow } } }, 1024, 1f, true).Width;
            float wideWidth = GantryTool.Guide.GuideRenderer.Render(new Dictionary<string, object> { { "theme", "ca" }, { "margin", 0.0 }, { "border", 0.0 }, { "items", new List<object> { wideArrow } } }, 1024, 1f, true).Width;
            Check(slimWidth < wideWidth, "a slim arrow is narrower than the standard one");
            var lanes = new List<GantryTool.Detect.GantryLane>();
            for (int i = 0; i < 4; i++)
            {
                lanes.Add(new GantryTool.Detect.GantryLane { Offset = 2f + i * 3.6f, Width = 3.6f, TravelSign = 1 });
            }

            var fitted = new GuideHead { Id = 1, Side = -1, Scale = 1f, FitFirst = 1, FitLast = 4, LaneArrows = true, Design = GantryTool.Guide.Json.Write(bc) };
            string drawn = GantryTool.Guide.GuideFit.Resolve(fitted, lanes).Design;
            Check(drawn.Contains("laneArrows") && !drawn.Contains("\"down\""), "spanning four lanes, every arrow still points up");

            GantryTool.Guide.GuideThemes.Read(null);
            string before = GantryTool.Guide.GuideThemes.Current;
            GantryTool.Guide.GuideThemes.Current = "ca";
            byte[] saved = GantryTool.Guide.GuideThemes.Write();
            GantryTool.Guide.GuideThemes.Read(null);
            Check(GantryTool.Guide.GuideThemes.Current == before, "a city with no choice uses the setting");
            GantryTool.Guide.GuideThemes.Read(saved);
            Check(GantryTool.Guide.GuideThemes.Current == "ca", "a city keeps its choice");
            GantryTool.Guide.GuideThemes.Read(new byte[] { 9, 200, 1 });
            Check(GantryTool.Guide.GuideThemes.Current == before, "unreadable data is ignored");
            GantryTool.Guide.GuideThemes.Read(new byte[] { 1, 200, 1 });
            Check(GantryTool.Guide.GuideThemes.Current == before, "and so is a cut-off block");
            GantryTool.Guide.GuideThemes.Current = "atlantis";
            Check(GantryTool.Guide.GuideThemes.Current == before, "and a country the mod doesn't have");
            GantryTool.Guide.GuideThemes.Read(null);
        }

        private static void GuideCopyPaste()
        {
            GantryTool.Guide.GuideClipboard.Clear();
            var there = new GuideHead { Id = 2, Along = 20f, Side = 1, Scale = 1f, Design = "{}", FitFirst = 2, FitLast = 3 };
            GuideHead same = GantryTool.Guide.GuideClipboard.PasteOnto(there);
            Check(!GantryTool.Guide.GuideClipboard.HasSign && same.Design == "{}", "nothing copied changes nothing");

            GantryTool.Guide.GuideClipboard.Copy(new GuideHead { Id = 1, Along = 5f, Side = -1, Scale = 1.5f, Design = "{\"items\":[]}", FitFirst = 1, FitLast = 1, LaneArrows = true });
            GuideHead pasted = GantryTool.Guide.GuideClipboard.PasteOnto(there);
            Check(pasted.Design == "{\"items\":[]}" && pasted.Scale == 1.5f && pasted.LaneArrows, "what it says and its size come across");
            Check(pasted.Id == 2 && pasted.Along == 20f && pasted.Side == 1 && pasted.FitFirst == 2 && pasted.FitLast == 3, "where it is stays");
            GantryTool.Guide.GuideClipboard.Clear();
        }

        private static void GuideSimpleSigns()
        {
            GantryTool.Guide.GuideSign blank = GantryTool.Guide.GuideRenderer.Render(GantryTool.Guide.GuideAssets.Template("us", "blank-green"));
            Check(blank.Width == 200f && blank.Height >= 130f && blank.Parts.Count == 0, "a blank sign has a size and nothing on it");

            Dictionary<string, object> ending = GantryTool.Guide.GuideAssets.Template("us", "lane-ending");
            GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(ending);
            Check(s.Height == s.Width && s.Parts.Count == 1 && s.Parts[0].Type == "symbol" && s.Parts[0].Path == "0", "the symbol can be clicked");
            int black = 0;
            foreach (Color32 c in s.Pixels)
            {
                black += c.a > 200 && c.r < 40 && c.g < 40 && c.b < 40 ? 1 : 0;
            }

            Check(black > s.PixelWidth, "and it draws (" + black + " dark pixels)");
            foreach (string name in new[] { "lane-ends-right", "lane-ends-left" })
            {
                Check(GantryTool.Guide.GuideAssets.Image("us", name) != null, name);
            }

            var missing = new Dictionary<string, object> { { "items", new List<object> { new Dictionary<string, object> { { "type", "symbol" }, { "name", "nope" } } } } };
            GantryTool.Guide.GuideRenderer.Render(missing, 256); // must not throw
        }

        private static void GuideExitSections()
        {
            Func<string, bool, Dictionary<string, object>> arrow = (dir, exit) =>
            {
                var a = new Dictionary<string, object> { { "type", "arrow" }, { "direction", dir } };
                if (exit)
                {
                    a[GantryTool.Guide.GuideFit.ExitOnlyKey] = true;
                }

                return a;
            };
            var row = new Dictionary<string, object>
            {
                { "type", "row" },
                { GantryTool.Guide.GuideFit.PerLaneKey, true },
                { "items", new List<object> { arrow("down", false), arrow("down", false), arrow("down", true) } }
            };
            var doc = new Dictionary<string, object> { { "theme", "us" }, { "items", new List<object> { row } } };
            var h = new GuideHead { Id = 1, Scale = 1f, Side = -1, Design = GantryTool.Guide.Json.Write(doc), FitFirst = 1, FitLast = 3, LaneArrows = true };
            GantryTool.Guide.GuidePlacement placed = GantryTool.Guide.GuideFit.Resolve(h, GuideLanes());
            var fitted = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(placed.Design);
            GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(fitted, 2048, 3f);
            var parts = new Dictionary<string, GantryTool.Guide.GuidePart>();
            foreach (GantryTool.Guide.GuidePart p in s.Parts)
            {
                parts[p.Path] = p;
            }

            // Yellow only in the third lane's share of the sign, along its bottom.
            float ppu = s.PixelWidth / s.Width;
            int y = Mathf.RoundToInt((s.Height - 12f) * ppu);   // in the band along the bottom
            int yellowFirst = -1, yellowLast = -1;
            for (int x = 0; x < s.PixelWidth; x++)
            {
                Color32 c = s.Pixels[y * s.PixelWidth + x];
                if (c.r > 230 && c.g > 170 && c.b < 60)
                {
                    yellowFirst = yellowFirst < 0 ? x : yellowFirst;
                    yellowLast = x;
                }
            }

            float laneUnits = 3f / (GantryTool.Guide.GuideFit.MetresPerUnit * placed.Scale);
            float centre = s.Width * 0.5f;
            Check(yellowFirst >= 0, "a yellow section is drawn");
            Check(Mathf.Abs(yellowFirst / ppu - (centre + laneUnits * 0.5f)) < 6f, "it starts at the lane's left edge (" + yellowFirst / ppu + " vs " + (centre + laneUnits * 0.5f) + ")");
            Check(yellowLast / ppu > centre + laneUnits, "and carries on across the lane");

            // Wording can change, and is carried through fitting.
            row[GantryTool.Guide.GuideFit.ExitTextKey] = "EXIT 12 ONLY";
            h.Design = GantryTool.Guide.Json.Write(doc);
            Check(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design.Contains("EXIT 12 ONLY"), "its wording");
            h.FitFirst = 0;
            GantryTool.Guide.GuideRenderer.Render((Dictionary<string, object>)GantryTool.Guide.Json.Parse(h.Design)); // placed by hand: plain arrows

            // New lanes aren't exit only just because the last one was.
            GantryTool.Guide.GuideFit.PadLaneRow(doc, 5);
            List<object> arrows = GantryTool.Guide.Json.Arr(row, "items");
            Check(arrows.Count == 5 && !((Dictionary<string, object>)arrows[4]).ContainsKey(GantryTool.Guide.GuideFit.ExitOnlyKey), "padding doesn't copy it");

            // Bad data still draws.
            var bad = new Dictionary<string, object>
            {
                { "items", new List<object> { new Dictionary<string, object>
                {
                    { "type", "laneArrows" },
                    { "at", new List<object> { 0.0, 1.0 } },
                    { "edges", new List<object> { "x" } },
                    { "items", new List<object> { arrow("down", true), arrow("none", true) } }
                } } }
            };
            GantryTool.Guide.GuideRenderer.Render(bad, 256);
        }

        private static void GuideDividerAndPlate()
        {
            Dictionary<string, object> doc = GantryTool.Guide.GuideAssets.Template("us", "pull-through");
            Check(!GantryTool.Guide.GuideFit.HasDivider(doc), "no divider yet");
            Dictionary<string, object> text = GantryTool.Guide.GuideFit.SplitSign(doc);
            Check(GantryTool.Guide.GuideFit.HasDivider(doc) && text != null, "split");
            List<object> items = GantryTool.Guide.Json.Arr(doc, "items");
            Check(items.Count == 2 && GantryTool.Guide.GuideFit.IsArrows((Dictionary<string, object>)items[1]), "wording together, arrows still below");
            Check(GantryTool.Guide.GuideRenderer.Render(doc).Parts.Count >= 5, "draws both sides");

            Dictionary<string, object> plate = GantryTool.Guide.GuideFit.AddExitPlate(doc);
            var arrowRow = (Dictionary<string, object>)items[1];
            Check(GantryTool.Guide.Json.Arr(arrowRow, "items").Count == 3 && GantryTool.Guide.Json.Str((Dictionary<string, object>)GantryTool.Guide.Json.Arr(arrowRow, "items")[1], "type") == "chip", "plate goes between the arrows");
            Check(GantryTool.Guide.Json.Str(plate, "text") == "EXIT ONLY", "with its text");

            var empty = new Dictionary<string, object> { { "theme", "us" } };
            GantryTool.Guide.GuideFit.AddExitPlate(empty);
            Check(GantryTool.Guide.Json.Arr((Dictionary<string, object>)GantryTool.Guide.Json.Arr(empty, "items")[0], "items").Count == 3, "with no arrows it brings its own");
            GantryTool.Guide.GuideFit.SplitSign(new Dictionary<string, object>());
            GantryTool.Guide.GuideRenderer.Render(empty);
        }

        private static void GuideDragging()
        {
            Dictionary<string, object> doc = GantryTool.Guide.GuideAssets.Template("us", "guide-exit-only");
            GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(doc);
            Func<string, Dictionary<string, object>> at = path => GantryTool.Guide.GuideDrag.ItemAt(doc, path);
            var parts = new Dictionary<string, GantryTool.Guide.GuidePart>();
            foreach (GantryTool.Guide.GuidePart p in s.Parts)
            {
                parts[p.Path] = p;
            }

            // Items: 0 tab, 1 row(shield, SOUTH), 2 Saugus, 3 band.
            Check(GantryTool.Guide.GuideDrag.Movable(doc, "0/0", -1) == null, "a tab's text stays");
            Check(GantryTool.Guide.GuideDrag.Movable(doc, "3/0", -1) == null, "a band's parts stay");
            Check(GantryTool.Guide.GuideDrag.Movable(doc, "3", -1) == null, "the band itself moves with its buttons");
            Check(GantryTool.Guide.GuideDrag.Movable(doc, "1/0", 1) == null, "lane arrows stay");
            Dictionary<string, object> saugus = GantryTool.Guide.GuideDrag.Movable(doc, "2", -1);
            Check(saugus != null && GantryTool.Guide.Json.Str(saugus, "text") == "Saugus", "text moves");

            GantryTool.Guide.GuidePart south = parts["1/1"];
            GantryTool.Guide.GuideDrop drop;
            Check(GantryTool.Guide.GuideDrag.Find(doc, s.Parts, saugus, south.X + south.W * 0.95f, south.Y + south.H * 0.5f, out drop), "finds a place");
            Check(drop.Path == "1/1" && drop.Side == GantryTool.Guide.DropSide.Right, "the right end of a word: beside it (" + drop.Path + " " + drop.Side + ")");
            Check(GantryTool.Guide.GuideDrag.Find(doc, s.Parts, saugus, south.X + south.W * 0.5f, south.Y + 1f, out drop) && drop.Side == GantryTool.Guide.DropSide.Above, "the top of a word: above it");
            GantryTool.Guide.GuideDrag.Find(doc, s.Parts, saugus, parts["3"].X + parts["3"].W * 0.05f, parts["3"].Y + 1f, out drop);
            Check(drop.Side == GantryTool.Guide.DropSide.Above || drop.Side == GantryTool.Guide.DropSide.Below, "a band is only above or below");
            Check(drop.Path != "0" && drop.Path != "0/0", "never into the tab");

            // Beside a word in a row: it joins the row.
            GantryTool.Guide.GuideDrag.Find(doc, s.Parts, saugus, south.X + south.W * 0.95f, south.Y + south.H * 0.5f, out drop);
            Check(GantryTool.Guide.GuideDrag.Apply(doc, saugus, drop), "moves");
            List<object> top = GantryTool.Guide.Json.Arr(doc, "items");
            List<object> row = GantryTool.Guide.Json.Arr((Dictionary<string, object>)top[1], "items");
            Check(top.Count == 3 && row.Count == 3 && row[2] == saugus, "joined the row, on the right");

            // Above a word in a row: above the whole row.
            s = GantryTool.Guide.GuideRenderer.Render(doc);
            Dictionary<string, object> shield = at("1/0");
            var above = new GantryTool.Guide.GuideDrop { Path = "1/1", Side = GantryTool.Guide.DropSide.Above };
            Check(GantryTool.Guide.GuideDrag.Apply(doc, saugus, above), "moves again");
            top = GantryTool.Guide.Json.Arr(doc, "items");
            Check(top[1] == saugus && GantryTool.Guide.Json.Arr((Dictionary<string, object>)top[2], "items").Count == 2, "above the row, which is left with two");

            // Beside a line of its own: the two become a row.
            var beside = new GantryTool.Guide.GuideDrop { Path = "1", Side = GantryTool.Guide.DropSide.Left };
            Check(GantryTool.Guide.GuideDrag.Apply(doc, shield, beside), "moves a third time");
            top = GantryTool.Guide.Json.Arr(doc, "items");
            var made = (Dictionary<string, object>)top[1];
            Check(GantryTool.Guide.Json.Str(made, "type") == "row" && GantryTool.Guide.Json.Arr(made, "items")[0] == shield && GantryTool.Guide.Json.Arr(made, "items")[1] == saugus, "a new row, shield on the left");
            GantryTool.Guide.GuideRenderer.Render(doc);

            var self = new GantryTool.Guide.GuideDrop { Path = "1/0", Side = GantryTool.Guide.DropSide.Left };
            Check(!GantryTool.Guide.GuideDrag.Apply(doc, shield, self), "not onto itself");
            Check(!GantryTool.Guide.GuideDrag.Apply(doc, shield, new GantryTool.Guide.GuideDrop { Path = "9/9", Side = GantryTool.Guide.DropSide.Left }), "not onto nothing");
        }

        private static void GantryTypes()
        {
            Check(GantryShape.PostAtStart(GantryShape.Span) && GantryShape.PostAtEnd(GantryShape.Span), "span: both ends");
            Check(GantryShape.PostAtStart(GantryShape.CantileverStart) && !GantryShape.PostAtEnd(GantryShape.CantileverStart), "cantilever: first end");
            Check(!GantryShape.PostAtStart(GantryShape.CantileverEnd) && GantryShape.PostAtEnd(GantryShape.CantileverEnd), "cantilever: second end");
            Check(!GantryShape.PostAtStart(GantryShape.MedianT) && !GantryShape.PostAtEnd(GantryShape.MedianT), "median T: neither");
            Check(GantryShape.Valid(200) == GantryShape.Span, "an unknown type is a span");
            Check(GantryShape.Names.Length == GantryShape.Count, "all named");

            // Lanes at 5, 8, 11, then a wide gap to 20 and 23: the post goes in the median.
            var lanes = GuideLanes();
            lanes.RemoveAt(3);
            lanes.Add(new GantryTool.Detect.GantryLane { Offset = 20f, Width = 3f, TravelSign = -1 });
            lanes.Add(new GantryTool.Detect.GantryLane { Offset = 23f, Width = 3f, TravelSign = -1 });
            Check(Mathf.Abs(GantryShape.MedianPost(lanes, 30f) - 15.5f) < 0.01f, "post in the widest gap (" + GantryShape.MedianPost(lanes, 30f) + ")");
            Check(GantryShape.MedianPost(new List<GantryTool.Detect.GantryLane>(), 30f) == 15f, "no lanes: the middle");
            Check(GantryShape.MedianPost(GuideLanes(), 40f) == 20f, "lanes side by side: the middle");
            Check(GantryShape.PostPosition(7f, lanes, 30f) == 7f, "the player's position wins");
            Check(GantryShape.PostPosition(99f, lanes, 30f) == 30f, "kept on the beam");

            List<Gantry> gantries = Sample();
            gantries[0].Type = GantryShape.MedianT;
            gantries[0].PostAlong = 3.5f;
            gantries[1].Type = GantryShape.CantileverEnd;
            List<Gantry> loaded = Sample();
            string err;
            GuideData.Apply(GuideData.Write(gantries, new object()), loaded, out err);
            Check(err == null && loaded[0].Type == GantryShape.MedianT && loaded[0].PostAlong == 3.5f, "saved");
            Check(loaded[1].Type == GantryShape.CantileverEnd && float.IsNaN(loaded[1].PostAlong), "automatic post stays automatic");
            Check(loaded[2].Type == GantryShape.Span && loaded[2].LaneHeads, "others are spans with their signals");
            Check(SameBytes(GantryData.WriteLegacy(Sample(), new object()), GantryData.WriteLegacy(gantries, new object())), "legacy block unchanged");
            Check(SameBytes(GantryData.WriteExt(Sample()), GantryData.WriteExt(gantries)), "extension block unchanged");
            Check(GuideData.Write(Sample(), new object()).Length == 8, "plain gantries add nothing");

            gantries = Sample();
            gantries[2].Look = GantryShape.CurvedTube;
            loaded = Sample();
            GuideData.Apply(GuideData.Write(gantries, new object()), loaded, out err);
            Check(err == null && loaded[2].Look == GantryShape.CurvedTube && loaded[0].Look == GantryShape.BoxTruss, "its look is saved");
            Check(GantryShape.ValidLook(99) == GantryShape.BoxTruss && GantryShape.LookNames.Length == GantryShape.LookCount, "looks are named; unknown is a box truss");

            // The curved tube: up from the ground, over, and down again.
            List<Vector2> arch = GantryShape.CurvedPath(GantryShape.Span, 30f, 8f, 1f, 2f, 3f);
            Check(arch[0] == new Vector2(0f, 1f) && arch[arch.Count - 1] == new Vector2(30f, 2f), "from foot to foot");
            float high = 0f;
            for (int i = 1; i < arch.Count; i++)
            {
                high = Mathf.Max(high, arch[i].y);
                Check(arch[i].x >= arch[i - 1].x - 0.001f, "always moving along");
            }

            Check(Mathf.Abs(high - 8f) < 0.01f, "reaching the beam");
            Check(Mathf.Abs(arch[5].x - arch[0].x) < 3f && arch[9].y > 7.9f, "after a bend at the start");
            List<Vector2> arm = GantryShape.CurvedPath(GantryShape.CantileverStart, 20f, 8f, 0f, 0f, 3f);
            Check(arm[arm.Count - 1] == new Vector2(20f, 8f), "a cantilever's free end stays up");
            List<Vector2> other = GantryShape.CurvedPath(GantryShape.CantileverEnd, 20f, 8f, 0f, 0f, 3f);
            Check(other[0] == new Vector2(0f, 8f) && other[other.Count - 1] == new Vector2(20f, 0f), "or the other way round");
            List<Vector2> tiny = GantryShape.CurvedPath(GantryShape.Span, 4f, 8f, 0f, 0f, 3f);
            for (int i = 1; i < tiny.Count; i++)
            {
                Check(tiny[i].x >= tiny[i - 1].x - 0.001f, "bends don't cross on a short span");
            }

            // A save from before types.
            List<Gantry> older = Sample();
            Check(older[0].Type == GantryShape.Span, "a gantry starts as a span");
        }

        private static void GuideArrangement()
        {
            // Beam 20 m; lanes centred at 5, 8 and 11 m run towards +side, and the one at 14 m the other way.
            List<GantryTool.Guide.ArrangeLane> minus = GantryTool.Guide.GuideArrange.Lanes(GuideLanes(), 20f, -1);
            Check(minus.Count == 4 && Mathf.Abs(minus[0].X0 - 3.5f) < 0.01f && minus[0].Facing && !minus[3].Facing, "-side face: left to right along the beam");
            List<GantryTool.Guide.ArrangeLane> plus = GantryTool.Guide.GuideArrange.Lanes(GuideLanes(), 20f, 1);
            Check(Mathf.Abs(plus[0].X0 - 4.5f) < 0.01f && plus[0].Facing && !plus[1].Facing, "+side face: mirrored, with the other direction facing");

            List<GantryTool.Guide.ArrangeLane> facing = GantryTool.Guide.GuideArrange.Facing(minus);
            Check(facing.Count == 3, "only lanes this face's drivers use");

            // Same numbering as a fitted sign's lanes.
            List<GantryTool.Detect.GantryLane> numbered = GantryTool.Guide.GuideFit.FaceLanes(GuideLanes(), 1);
            Check(numbered.Count == GantryTool.Guide.GuideArrange.Facing(plus).Count, "the same lanes as fitting uses");
            Check(Mathf.Abs(GantryTool.Guide.GuideArrange.ViewX(numbered[0].Offset, 20f, 1) - 6f) < 0.01f, "and the same way round");

            int first = 0, last = 1;
            GantryTool.Guide.GuideArrange.Move(facing, 8.6f, ref first, ref last);
            Check(first == 1 && last == 2, "moves over, still covering two (" + first + "-" + last + ")");
            GantryTool.Guide.GuideArrange.Move(facing, -50f, ref first, ref last);
            Check(first == 0 && last == 1, "stops at the left end");
            GantryTool.Guide.GuideArrange.Move(facing, 500f, ref first, ref last);
            Check(first == 1 && last == 2, "stops at the right end");
            first = 0;
            last = 8;
            GantryTool.Guide.GuideArrange.Move(facing, 8f, ref first, ref last);
            Check(first == 0 && last == 2, "never more lanes than there are");

            Check(GantryTool.Guide.GuideArrange.LeftEdge(facing, 6.4f, 2) == 1, "left edge goes to the nearest lane edge");
            Check(GantryTool.Guide.GuideArrange.LeftEdge(facing, 500f, 1) == 1, "left edge can't pass the last lane");
            Check(GantryTool.Guide.GuideArrange.RightEdge(facing, 9.6f, 0) == 1, "right edge goes to the nearest lane edge");
            Check(GantryTool.Guide.GuideArrange.RightEdge(facing, -500f, 1) == 1, "right edge can't pass the first lane");

            Check(GantryTool.Guide.GuideArrange.SnapToLane(facing, 8.3f) == 8f, "a hand-placed sign settles on a lane's middle");
            Check(GantryTool.Guide.GuideArrange.SnapToLane(facing, 9.5f) == 9.5f, "and stays put between them");
        }

        private static void BareGantries()
        {
            // A gantry from any save older than this block has no record in it, and must come back with its signals.
            List<Gantry> old = Sample();
            string err;
            Check(GuideData.Apply(null, old, out err) == 0 && err == null, "no block in the save");
            foreach (Gantry g in old)
            {
                Check(g.LaneHeads && !g.IsBare, "signals stay on");
            }

            int version;
            List<Gantry> read = GantryData.ReadLegacy(GantryData.WriteLegacy(Sample(), new object()), out version, out err);
            Check(read.Count > 0, "read back");
            foreach (Gantry g in read)
            {
                Check(g.LaneHeads, "the old block alone gives signals");
            }

            // A gantry saved bare comes back bare; the older blocks don't change for it.
            List<Gantry> bare = Sample();
            bare[0].LaneHeads = false;
            Check(bare[0].IsBare, "no signals, no signs: bare");
            List<Gantry> loaded = Sample();
            GuideData.Apply(GuideData.Write(bare, new object()), loaded, out err);
            Check(err == null && loaded[0].IsBare && !loaded[1].IsBare, "stays bare");
            Check(SameBytes(GantryData.WriteLegacy(Sample(), new object()), GantryData.WriteLegacy(bare, new object())), "legacy block unchanged");
            Check(SameBytes(GantryData.WriteExt(Sample()), GantryData.WriteExt(bare)), "extension block unchanged");

            bare[0].GuideSigns.Add(new GuideHead { Id = 1, Scale = 1f, Side = 1, Design = "{}" });
            Check(!bare[0].IsBare, "a guide sign counts");
            bare[0].GuideSigns.Clear();
            bare[0].ExtraHeads.Add(new ExtraHead { Id = 1 });
            Check(!bare[0].IsBare, "an added sign counts");
        }

        private static void GuideMySigns()
        {
            string folder = Path.Combine(Path.GetTempPath(), "GantryToolTest" + Guid.NewGuid().ToString("N"));
            try
            {
                Check(GantryTool.Guide.MySigns.List(folder).Count == 0, "no folder yet: no signs");
                Dictionary<string, object> design = GantryTool.Guide.GuideAssets.Template("us", "pull-through");
                Check(!GantryTool.Guide.MySigns.Save(folder, "   ", design), "a name is needed");
                Check(GantryTool.Guide.MySigns.Save(folder, " My exit: sign/2 ", design), "saved despite odd characters");
                Check(GantryTool.Guide.MySigns.Save(folder, "CON", design), "a name Windows reserves");
                List<GantryTool.Guide.MySign> signs = GantryTool.Guide.MySigns.List(folder);
                Check(signs.Count == 2 && signs[0].Name == "CON" && signs[1].Name == "My exit: sign/2", "listed by name");
                Check(signs[1].Id == GantryTool.Guide.MySigns.IdPrefix + "My exit: sign/2", "marked as the player's own");
                Check(GantryTool.Guide.Json.Str(design, "id") == "pull-through", "the design handed in is left alone");
                Check(GantryTool.Guide.GuideRenderer.Render(signs[1].Design).Width > 0f, "draws");

                Dictionary<string, object> other = GuideText("Changed");
                Check(GantryTool.Guide.MySigns.Save(folder, "CON", other), "saved again");
                signs = GantryTool.Guide.MySigns.List(folder);
                Check(signs.Count == 2 && GantryTool.Guide.Json.Write(signs[0].Design).Contains("Changed"), "the same name replaces it");

                File.WriteAllText(Path.Combine(folder, "sign-broken.json"), "{ not json");
                File.WriteAllText(Path.Combine(folder, "sign-list.json"), "[1,2]");
                File.WriteAllText(Path.Combine(folder, "other.txt"), "x");
                Check(GantryTool.Guide.MySigns.List(folder).Count == 2, "files it can't use are skipped");

                GantryTool.Guide.MySigns.Delete(folder, "CON");
                GantryTool.Guide.MySigns.Delete(folder, "never existed");
                Check(GantryTool.Guide.MySigns.List(folder).Count == 1, "deleted");
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        private static void GuideParts()
        {
            Dictionary<string, object> doc = GantryTool.Guide.GuideAssets.Template("us", "guide-exit-only");
            GantryTool.Guide.GuideSign s = GantryTool.Guide.GuideRenderer.Render(doc);
            var found = new Dictionary<string, GantryTool.Guide.GuidePart>();
            foreach (GantryTool.Guide.GuidePart p in s.Parts)
            {
                found[p.Path] = p;
                Check(p.W > 0f && p.H > 0f && p.X >= -0.01f && p.Y >= -0.01f && p.X + p.W <= s.Width + 0.01f && p.Y + p.H <= s.Height + 0.01f, "part " + p.Path + " inside the sign");
            }

            Check(found.ContainsKey("0") && found["0"].Type == "tab" && found.ContainsKey("0/0") && found["0/0"].Type == "text", "the tab and its text");
            Check(found["0/0"].Y + found["0/0"].H <= s.TabHeight + 0.01f, "the tab's text is in the tab row");
            Check(found["1/0"].Type == "shield" && found["1/1"].Type == "text", "shield and direction in their row");
            Check(found["2"].Type == "text" && found["2"].Y > found["1/0"].Y, "place name below them");
            Check(found["3"].Type == "panel" && Mathf.Abs(found["3"].Y + found["3"].H - s.Height) < 6f, "band at the bottom");
            Check(!found.ContainsKey("1"), "rows aren't parts themselves");

            // Fitting to lanes doesn't move anything the editor knows about.
            string design = GantryTool.Guide.Json.Write(GantryTool.Guide.GuideAssets.Template("us", "pull-through"));
            var h = new GuideHead { Id = 1, Scale = 1f, Side = -1, Design = design, FitFirst = 1, FitLast = 3, LaneArrows = true };
            var fitted = (Dictionary<string, object>)GantryTool.Guide.Json.Parse(GantryTool.Guide.GuideFit.Resolve(h, GuideLanes()).Design);
            bool place = false, arrows = false;
            foreach (GantryTool.Guide.GuidePart p in GantryTool.Guide.GuideRenderer.Render(fitted).Parts)
            {
                place |= p.Path == "1" && p.Type == "text";
                arrows |= p.Path == "2" && p.Type == "laneArrows";
            }

            Check(place && arrows, "same paths on a fitted sign");
        }

        private static void GuideBlankBacks()
        {
            List<GantryTool.Detect.GantryLane> lanes = GuideLanes();
            FaceSign minus = new FaceSign(SignalSymbol.Arrow, 0), plus = new FaceSign(SignalSymbol.Cross, 0);
            SignalRules.BlankBack(lanes, null, 0, ref minus, ref plus);
            Check(minus.Symbol == SignalSymbol.Arrow && plus.Symbol == SignalSymbol.Blank, "+side traffic: the plus face goes blank");
            minus = new FaceSign(SignalSymbol.Cross, 0);
            plus = new FaceSign(SignalSymbol.Arrow, 0);
            SignalRules.BlankBack(lanes, null, 3, ref minus, ref plus);
            Check(minus.Symbol == SignalSymbol.Blank && plus.Symbol == SignalSymbol.Arrow, "-side traffic: the minus face goes blank");
            minus = new FaceSign(SignalSymbol.Cross, 0);
            plus = new FaceSign(SignalSymbol.Reserved, 0);
            SignalRules.BlankBack(lanes, null, 0, ref minus, ref plus);
            Check(minus.Symbol == SignalSymbol.Cross && plus.Symbol == SignalSymbol.Reserved, "the driver's own face, and other symbols, stay");

            List<Gantry> g = Sample();
            g[2].BlankBacks = true;
            List<Gantry> loaded = Sample();
            string err;
            GuideData.Apply(GuideData.Write(g, new object()), loaded, out err);
            Check(err == null && loaded[2].BlankBacks && !loaded[0].BlankBacks && loaded[2].LaneHeads, "saved");
            List<Gantry> plain = Sample();
            Check(SameBytes(GantryData.WriteExt(plain), GantryData.WriteExt(g)), "extension block unchanged");
        }

        private static void GuideSaveData()
        {
            List<Gantry> gantries = Sample();
            gantries[0].LaneHeads = false;
            gantries[1].GuideSigns.Add(new GuideHead { Id = 2, Along = 7.5f, Scale = 1.25f, Side = -1, Design = "{\"id\":\"x\"}" });
            gantries[1].GuideSigns.Add(new GuideHead { Id = 2, Along = 1f, Scale = 1f, Side = 1, Design = "{}" });   // duplicate id
            gantries[1].GuideSigns.Add(new GuideHead { Id = 3, Along = 1e9f, Scale = 99f, Side = 5, Design = "{}" }); // out of range
            gantries[1].GuideSigns.Add(new GuideHead { Id = 4, Along = 1f, Scale = 1f, Side = 1, Design = "" });     // empty

            List<Gantry> plain = Sample();
            Check(SameBytes(GantryData.WriteLegacy(plain, new object()), GantryData.WriteLegacy(gantries, new object())), "legacy block unchanged");
            Check(SameBytes(GantryData.WriteExt(plain), GantryData.WriteExt(gantries)), "extension block unchanged");
            Check(SameBytes(ExtraHeadData.Write(plain, new object()), ExtraHeadData.Write(gantries, new object())), "added signs block unchanged");

            byte[] bytes = GuideData.Write(gantries, new object());
            List<Gantry> loaded = Sample();
            string err;
            GuideData.Apply(bytes, loaded, out err);
            Check(err == null, "no error: " + err);
            Check(!loaded[0].LaneHeads && loaded[1].LaneHeads && loaded[2].LaneHeads, "lane signals switch");
            Check(loaded[1].GuideSigns.Count == 2, "duplicate and empty dropped (" + loaded[1].GuideSigns.Count + ")");
            GuideHead a = loaded[1].GuideSigns[0], b = loaded[1].GuideSigns[1];
            Check(a.Id == 2 && a.Along == 7.5f && a.Scale == 1.25f && a.Side == -1 && a.Design == "{\"id\":\"x\"}", "sign kept");
            Check(b.Along <= loaded[1].Span && b.Scale == GuideData.MaxScale && b.Side == 1, "values repaired");

            List<Gantry> moved = Sample();
            moved[0].End = moved[0].End + new Vector3(5f, 0f, 0f);
            GuideData.Apply(bytes, moved, out err);
            Check(moved[0].LaneHeads, "stale data ignored");

            for (int cut = 0; cut < bytes.Length; cut += 3)
            {
                var part = new byte[cut];
                Array.Copy(bytes, part, cut);
                GuideData.Apply(part, Sample(), out err); // must not throw
            }

            var garbage = new byte[64];
            new System.Random(4).NextBytes(garbage);
            GuideData.Apply(garbage, Sample(), out err);
            Check(err != null, "garbage reported, not thrown");
            Check(GuideData.Write(Sample(), new object()).Length == 8, "nothing to store for ordinary gantries");
            Check(HeadRef.IsGuide(HeadRef.OfGuide(3)) && !HeadRef.IsExtra(HeadRef.OfGuide(3)) && HeadRef.GuideId(HeadRef.OfGuide(3)) == 3, "numbering");
        }

        private static ExtraHead Extra(byte id, float along, SignalSymbol minus, SignalSymbol plus)
        {
            return new ExtraHead { Id = id, Along = along, Minus = minus, Plus = plus };
        }

        private static void ExtraHeadsRoundTrip()
        {
            List<Gantry> gantries = Sample();
            ExtraHead first = Extra(1, 12.5f, SignalSymbol.Custom, SignalSymbol.Roadworks);
            first.MinusValue = 4;
            gantries[0].ExtraHeads.Add(first);
            ExtraHead linked = Extra(3, 20f, SignalSymbol.SpeedAuto, SignalSymbol.Auto);
            linked.Linked = true;
            linked.LinkOffset = 6.32f;
            linked.LinkSign = 1;
            linked.Nudge = -0.5f;
            linked.Size = 1.6f;
            gantries[0].ExtraHeads.Add(linked);

            byte[] bytes = ExtraHeadData.Write(gantries, new object());
            List<Gantry> loaded = SaveAndLoad(gantries, true);
            string err;
            int added = ExtraHeadData.Apply(bytes, loaded, out err);
            Check(err == null && added == 2, "2 heads, no error: " + err);
            ExtraHead a = loaded[0].ExtraHeads[0], b = loaded[0].ExtraHeads[1];
            Check(a.Id == 1 && a.Along == 12.5f && a.Minus == SignalSymbol.Custom && a.MinusValue == 4 &&
                  a.Plus == SignalSymbol.Roadworks && !a.Linked, "unlinked head");
            Check(b.Id == 3 && b.Linked && b.LinkOffset == 6.32f && b.LinkSign == 1 && b.Nudge == -0.5f && b.Size == 1.6f &&
                  b.Minus == SignalSymbol.SpeedAuto && b.Plus == SignalSymbol.Auto, "linked head");
            Check(loaded[1].ExtraHeads.Count == 0 && loaded[2].ExtraHeads.Count == 0, "other gantries untouched");
        }

        private static void ExtraHeadsBadData()
        {
            List<Gantry> gantries = Sample();
            gantries[0].ExtraHeads.Add(Extra(1, 5f, SignalSymbol.Arrow, SignalSymbol.Auto));   // Auto without a lane
            gantries[0].ExtraHeads.Add(Extra(1, 6f, SignalSymbol.Cross, SignalSymbol.Cross));  // duplicate id
            gantries[0].ExtraHeads.Add(Extra(2, float.NaN, SignalSymbol.Hidden, (SignalSymbol)500));
            for (byte i = 10; i < 20; i++)
            {
                gantries[1].ExtraHeads.Add(Extra(i, 3f, SignalSymbol.Blank, SignalSymbol.Blank)); // over the limit
            }

            byte[] bytes = ExtraHeadData.Write(gantries, new object());
            List<Gantry> target = SaveAndLoad(gantries, true);
            string err;
            ExtraHeadData.Apply(bytes, target, out err);
            Check(err == null, "no error: " + err);
            Check(target[0].ExtraHeads.Count == 2, "duplicate id dropped (" + target[0].ExtraHeads.Count + ")");
            Check(target[0].ExtraHeads[0].Plus == SignalSymbol.Blank, "Auto without a lane becomes blank");
            ExtraHead repaired = target[0].ExtraHeads[1];
            Check(!float.IsNaN(repaired.Along), "NaN position repaired");
            Check(repaired.Minus == SignalSymbol.Blank && repaired.Plus == SignalSymbol.Blank, "Hidden and unknown signs become blank");
            Check(target[1].ExtraHeads.Count == HeadRef.MaxExtraHeads, "capped at " + HeadRef.MaxExtraHeads);

            // A gantry moved or recreated in an older version: its heads are not attached to the new one.
            List<Gantry> moved = Sample();
            moved[0].End = moved[0].End + new Vector3(5f, 0f, 0f);
            ExtraHeadData.Apply(bytes, moved, out err);
            Check(moved[0].ExtraHeads.Count == 0, "stale heads ignored");

            for (int cut = 0; cut < bytes.Length; cut += 5)
            {
                var part = new byte[cut];
                Array.Copy(bytes, part, cut);
                ExtraHeadData.Apply(part, Sample(), out err); // must not throw
            }

            var garbage = new byte[80];
            new System.Random(9).NextBytes(garbage);
            ExtraHeadData.Apply(garbage, Sample(), out err);
            Check(err != null, "garbage reported, not thrown");
        }

        private static void ExtraHeadsLeaveOldBlocksAlone()
        {
            List<Gantry> plain = Sample();
            List<Gantry> withHeads = Sample();
            withHeads[0].ExtraHeads.Add(Extra(1, 8f, SignalSymbol.Cross, SignalSymbol.Cross));
            Check(SameBytes(GantryData.WriteLegacy(plain, new object()), GantryData.WriteLegacy(withHeads, new object())),
                  "legacy block identical (1.0 reads it as before)");
            Check(SameBytes(GantryData.WriteExt(plain), GantryData.WriteExt(withHeads)),
                  "extension block identical (1.1.0 reads it as before)");
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static List<GantryTool.Detect.GantryLane> TwoLanes()
        {
            return new List<GantryTool.Detect.GantryLane>
            {
                new GantryTool.Detect.GantryLane { Offset = 4f, TravelSign = 1, SpeedKmh = 100f },
                new GantryTool.Detect.GantryLane { Offset = 8f, TravelSign = -1, SpeedKmh = 80f }
            };
        }

        private static void ExtraHeadsResolve()
        {
            var lanes = TwoLanes();
            FaceSign m, p;
            ExtraHead free = Extra(1, 6f, SignalSymbol.Auto, SignalSymbol.SpeedAuto);
            SignalRules.ResolveExtra(lanes, free, out m, out p);
            Check(m.Symbol == SignalSymbol.Blank && p.Symbol == SignalSymbol.Blank, "unlinked: Auto and Road speed show blank");

            ExtraHead linked = free;
            linked.Linked = true;
            linked.LinkOffset = 8.3f; // lane 2 has moved a little
            linked.LinkSign = -1;
            Check(SignalRules.LinkedLane(lanes, linked) == 1, "finds its lane");
            SignalRules.ResolveExtra(lanes, linked, out m, out p);
            Check(m.Symbol == SignalSymbol.Cross && p.Symbol == SignalSymbol.SpeedLimit && p.Value == 80,
                  "linked: Auto reads the lane's direction, Road speed its limit");

            linked.LinkOffset = 30f;
            Check(SignalRules.LinkedLane(lanes, linked) == -1, "lane gone: no link");
            SignalRules.ResolveExtra(lanes, linked, out m, out p);
            Check(m.Symbol == SignalSymbol.Blank, "lane gone: blank");

            ExtraHead own = Extra(2, 6f, SignalSymbol.Roadworks, SignalSymbol.Custom);
            own.PlusValue = 3;
            SignalRules.ResolveExtra(lanes, own, out m, out p);
            Check(m.Symbol == SignalSymbol.Roadworks && p.Symbol == SignalSymbol.Custom && p.Value == 3, "own signs kept");

            SignalRules.ResolveHead(lanes, new List<SignalOverride>(), new List<ExtraHead> { own }, HeadRef.Of(2), out m, out p);
            Check(m.Symbol == SignalSymbol.Roadworks, "resolved by head number");
            SignalRules.ResolveHead(lanes, new List<SignalOverride>(), new List<ExtraHead>(), HeadRef.Of(9), out m, out p);
            Check(SignalRules.IsHidden(m.Symbol, p.Symbol), "missing head draws nothing");
        }

        private static void ExtraHeadsLayout()
        {
            var lanes = TwoLanes();
            float along, size;
            GantryTool.Render.SignalMeshBuilder.ExtraLayout(lanes, Extra(1, 6f, SignalSymbol.Blank, SignalSymbol.Blank),
                1.2f, 1.5f, 20f, out along, out size);
            Check(along == 6f && Mathf.Abs(size - 1.4f) < 0.001f, "unlinked: its own position, gantry box size (" + along + ", " + size + ")");
            GantryTool.Render.SignalMeshBuilder.ExtraLayout(lanes, Extra(1, 50f, SignalSymbol.Blank, SignalSymbol.Blank),
                1.2f, 1.5f, 20f, out along, out size);
            Check(along <= 20f - size * 0.5f + 0.001f, "kept on the beam");
            ExtraHead linked = Extra(1, 0f, SignalSymbol.Blank, SignalSymbol.Blank);
            linked.Linked = true;
            linked.LinkOffset = 4f;
            linked.LinkSign = 1;
            linked.Nudge = 1.5f;
            linked.Size = 2f;
            GantryTool.Render.SignalMeshBuilder.ExtraLayout(lanes, linked, 1.2f, 1.5f, 20f, out along, out size);
            Check(along == 5.5f && Mathf.Abs(size - 2.2f) < 0.001f, "linked: lane + nudge, own size (" + along + ", " + size + ")");
            Check(HeadRef.IsExtra(HeadRef.Of(1)) && !HeadRef.IsExtra(7) && HeadRef.ExtraId(HeadRef.Of(5)) == 5, "head numbering");
        }

        private static void ExtraHeadsKeepCustomSigns()
        {
            List<Gantry> gantries = Sample();
            ExtraHead h = Extra(1, 5f, SignalSymbol.Custom, SignalSymbol.Blank);
            h.MinusValue = 7;
            gantries[2].ExtraHeads.Add(h);
            Check(CustomSigns.UsedIds(gantries, new object()).Contains(7), "custom sign on an added head counts as used");
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
