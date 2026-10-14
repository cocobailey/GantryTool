using System;
using UnityEngine;

namespace GantryTool.Tool
{
    /// <summary>
    /// Ctrl+Shift+G toggles the gantry tool. Lives on its own GameObject so it works whether the tool's button
    /// is our floating one or Unified UI's.
    /// </summary>
    public sealed class HotkeyListener : MonoBehaviour
    {
        public GantryPlaceTool Tool;

        public static HotkeyListener Create(GantryPlaceTool tool)
        {
            var go = new GameObject("GantryToolHotkey");
            var listener = go.AddComponent<HotkeyListener>();
            listener.Tool = tool;
            return listener;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G) && !ColossalFramework.UI.UIView.HasInputFocus() &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                Toggle(Tool);
            }
        }

        /// <summary>Activates the tool, or returns to the default tool if it's already active.</summary>
        public static void Toggle(GantryPlaceTool tool)
        {
            try
            {
                if (tool == null)
                {
                    return;
                }

                ToolController controller = ToolsModifierControl.toolController;
                if (controller.CurrentTool == tool)
                {
                    ToolsModifierControl.SetTool<DefaultTool>();
                }
                else
                {
                    controller.CurrentTool = tool;
                }
            }
            catch (Exception e)
            {
                Log.Error("Toggle tool failed", e);
            }
        }
    }
}
