using System;
using GantryTool.Tool;
using HarmonyLib;

namespace GantryTool.Patches
{
    /// <summary>
    /// GameKeyShortcuts.Escape() (GameKeyShortcuts.cs:1040) only knows vanilla tools and otherwise opens
    /// the pause menu. While the gantry tool is active, Esc cancels the first point or leaves the tool.
    /// </summary>
    [HarmonyPatch(typeof(GameKeyShortcuts), "Escape")]
    public static class EscapePatch
    {
        public static bool Prefix()
        {
            try
            {
                GantryPlaceTool tool = GantryPlaceTool.Instance;
                if (tool != null && tool.enabled && ToolsModifierControl.toolController != null &&
                    ToolsModifierControl.toolController.CurrentTool == tool)
                {
                    tool.CancelOrExit();
                    return false; // skip vanilla (pause menu)
                }
            }
            catch (Exception e)
            {
                Log.Error("EscapePatch failed", e);
            }

            return true;
        }
    }
}
