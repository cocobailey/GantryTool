using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColossalFramework;
using ColossalFramework.Plugins;
using ColossalFramework.UI;
using UnifiedUI.Helpers;
using UnityEngine;

namespace GantryTool.Integration
{
    /// <summary>
    /// Optional Unified UI support. When the UUI mod is enabled, the tool gets a button in UUI's shared
    /// toolbar instead of our floating one. UnifiedUILib.dll ships with this mod (as with every UUI-aware
    /// mod); it only does anything when the UUI mod itself is enabled.
    ///
    /// Only RegisterToolButton(name, group, tooltip, ToolBase, Texture2D, UUIHotKeys) is used: it exists in both
    /// lib versions found among the installed mods (2.1.10 and 2.2.12), whichever one the game loads first.
    /// Any failure falls back to the floating button.
    /// </summary>
    public static class Uui
    {
        private static UIComponent _button;
        private static Texture2D _icon;

        /// <summary>Registers the tool with UUI. Returns false if UUI isn't available (use the floating button).</summary>
        public static bool TryRegister(ToolBase tool)
        {
            try
            {
                if (!IsEnabled())
                {
                    Log.Info("Unified UI not enabled; using the floating button");
                    return false;
                }

                _icon = LoadIcon();
                _button = Register(tool, _icon);
                if (_button == null)
                {
                    Log.Warn("Unified UI registration returned no button; using the floating button");
                    return false;
                }

                Log.Info("Tool button added to Unified UI");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Unified UI registration failed; using the floating button", e);
                return false;
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_button != null)
                {
                    DestroyButton(_button);
                }
            }
            catch (Exception e)
            {
                Log.Error("Unified UI unregister failed", e);
            }

            _button = null;
            if (_icon != null)
            {
                UnityEngine.Object.Destroy(_icon);
                _icon = null;
            }
        }

        // Calls into UnifiedUILib are kept in their own non-inlined methods so a missing/odd lib version
        // surfaces as a catchable exception at the call site.
        /// <summary>
        /// UUIHelpers.IsUUIEnabled() can't be trusted: it looks for UnifiedUI.API.UUIAPI in the UUI mod's main
        /// assembly, but UUI 2.2.13 moved that type into its UnifiedUILib.dll, so it returns false even with UUI
        /// enabled (other mods skip the check and just register). So: our own check for an enabled plugin whose
        /// assembly is UnifiedUIMod, or the helper's answer for older UUI versions.
        /// </summary>
        private static bool IsEnabled()
        {
            foreach (PluginManager.PluginInfo plugin in Singleton<PluginManager>.instance.GetPluginsInfo())
            {
                if (!plugin.isEnabled)
                {
                    continue;
                }

                foreach (Assembly assembly in plugin.GetAssemblies())
                {
                    if (assembly.GetName().Name == "UnifiedUIMod")
                    {
                        return true;
                    }
                }
            }

            return HelperSaysEnabled();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HelperSaysEnabled()
        {
            try
            {
                return UUIHelpers.IsUUIEnabled();
            }
            catch
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UIComponent Register(ToolBase tool, Texture2D icon)
        {
            // No UUI hotkey: Ctrl+Shift+G is handled by HotkeyListener so it works with or without UUI.
            return UUIHelpers.RegisterToolButton("GantryTool", null, "Gantry Tool (Ctrl+Shift+G)", tool, icon);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DestroyButton(UIComponent button)
        {
            button.Destroy();
        }

        /// <summary>The toolbar icon embedded in the DLL (Resources/uui_icon.png).</summary>
        private static Texture2D LoadIcon()
        {
            using (Stream s = typeof(Uui).Assembly.GetManifestResourceStream("GantryTool.uui_icon.png"))
            {
                if (s == null)
                {
                    throw new InvalidOperationException("embedded icon GantryTool.uui_icon.png not found");
                }

                var bytes = new byte[s.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = s.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false) { name = "GantryToolUuiIcon" };
                tex.LoadImage(bytes);
                return tex;
            }
        }
    }
}
