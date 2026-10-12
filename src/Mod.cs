using System;
using CitiesHarmony.API;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Render;
using ICities;

namespace GantryTool
{
    public sealed class Mod : IUserMod
    {
        public const string Version = "1.1.0-dev3";

        public string Name => "Gantry Tool";

        public string Description => "Place overhead gantries with lane control signals over roads.";

        public void OnEnabled()
        {
            Log.Info("Mod enabled (v" + Version + ")");
            // Defers patching until the CitiesHarmony workshop mod has installed Harmony 2.x.
            HarmonyHelper.DoOnHarmonyReady(Patcher.PatchAll);
        }

        public void OnDisabled()
        {
            if (HarmonyHelper.IsHarmonyInstalled)
            {
                Patcher.UnpatchAll();
            }

            Log.Info("Mod disabled");
        }

        // Labels are kept to one short line: the slider/checkbox templates have a fixed-height label slot,
        // and wrapped text overlaps the control. Longer explanations go in tooltips.
        public void OnSettingsUI(UIHelperBase helper)
        {
            try
            {
                Settings s = Settings.Current;

                UIHelperBase newGroup = helper.AddGroup("New gantries");
                AddSlider(newGroup, "Height above road", 2f, 15f, 0.1f, s.PostHeight, " m",
                    "Beam underside above the highest road it crosses (above the ground if there's no road), for gantries placed from now on.",
                    v => s.PostHeight = v, false);
                AddSlider(newGroup, "Truss depth", 1f, 2.5f, 0.1f, s.BeamDepth, " m",
                    "Height of the truss beam, for gantries placed from now on.",
                    v => s.BeamDepth = v, false);
                newGroup.AddButton("Apply height and depth to all existing gantries", () =>
                {
                    int n = GantryRenderer.Active ? GantryManager.ApplyDefaultsToAll() : 0;
                    Log.Info("Applied defaults to " + n + " gantries");
                });

                UIHelperBase allGroup = helper.AddGroup("All gantries");
                AddSlider(allGroup, "Signal size", 0.6f, 2f, 0.05f, s.SignalSize, " m",
                    "Edge length of the LED face. Shrunk automatically if the truss is too shallow.",
                    v => s.SignalSize = v, true);
                AddSlider(allGroup, "Steel brightness", 0.15f, 1f, 0.01f, s.TrussBrightness, "",
                    "Grey level of the truss and posts.", v =>
                    {
                        s.TrussBrightness = v;
                        GantryMaterials.SetTrussBrightness(v);
                    }, false);

                UIHelperBase tmpeGroup = helper.AddGroup("TM:PE");
                AddCheckbox(tmpeGroup, "Use TM:PE lane restrictions", s.UseTmpeRestrictions,
                    "Lanes with every vehicle banned show a red X on both sides. Bus-only lanes show a diamond " +
                    "where the green arrow would be.", v =>
                    {
                        s.UseTmpeRestrictions = v;
                        if (GantryRenderer.Active)
                        {
                            GantryManager.MarkAllDirty();
                        }
                    });

                UIHelperBase advGroup = helper.AddGroup("Advanced");
                AddSlider(advGroup, "Road search padding", 64f, 512f, 32f, s.GridPadding, " m",
                    "Raise this if lanes on very long (e.g. Network Anarchy) segments are missed.",
                    v => s.GridPadding = v, true);
                AddCheckbox(advGroup, "Debug logging", s.DebugLogging,
                    "Writes per-lane scan details to output_log.txt.", v => s.DebugLogging = v);
            }
            catch (Exception e)
            {
                Log.Error("OnSettingsUI failed", e);
            }
        }

        /// <summary>Slider whose label shows the current value; saves settings and optionally rebuilds gantries.</summary>
        private static void AddSlider(UIHelperBase group, string text, float min, float max, float step, float value,
            string unit, string tooltip, Action<float> apply, bool rebuildAll)
        {
            UILabel label = null;
            var slider = (UISlider)group.AddSlider(text, min, max, step, value, v =>
            {
                apply(v);
                Settings.Current.Save();
                if (label != null)
                {
                    label.text = text + ": " + v.ToString("0.##") + unit;
                }

                if (rebuildAll && GantryRenderer.Active)
                {
                    GantryManager.MarkAllDirty();
                }
            });

            slider.tooltip = tooltip;

            // UIHelper puts the slider in a template panel next to a UILabel named "Label" (UIHelper.cs:49-51).
            label = slider.parent.Find<UILabel>("Label");
            if (label != null)
            {
                label.wordWrap = false;
                label.text = text + ": " + value.ToString("0.##") + unit;
                label.tooltip = tooltip;
            }
        }

        private static void AddCheckbox(UIHelperBase group, string text, bool value, string tooltip, Action<bool> apply)
        {
            var box = (UICheckBox)group.AddCheckbox(text, value, v =>
            {
                apply(v);
                Settings.Current.Save();
            });
            box.tooltip = tooltip;
        }
    }
}
