using System;
using ColossalFramework.UI;
using UnityEngine;

namespace GantryTool.Tool
{
    /// <summary>
    /// Floating toggle button, used when Unified UI isn't enabled. Drag it by its top strip.
    /// (Ctrl+Shift+G is handled by HotkeyListener.)
    /// </summary>
    public sealed class ToolButton : UIButton
    {
        public GantryPlaceTool Tool;

        private UIDragHandle _drag;

        public static ToolButton Create()
        {
            return (ToolButton)UIView.GetAView().AddUIComponent(typeof(ToolButton));
        }

        public override void Awake()
        {
            base.Awake();
            name = "GantryToolButton";
            size = new Vector2(46f, 46f);
            normalBgSprite = "OptionBase";
            hoveredBgSprite = "OptionBaseHovered";
            pressedBgSprite = "OptionBasePressed";
            focusedBgSprite = "OptionBase";
            text = "Π";
            textScale = 1.6f;
            textVerticalAlignment = UIVerticalAlignment.Middle;
            textHorizontalAlignment = UIHorizontalAlignment.Center;
            tooltip = "Gantry Tool (Ctrl+Shift+G)";

            _drag = AddUIComponent<UIDragHandle>();
            _drag.width = width;
            _drag.height = 12f;
            _drag.relativePosition = Vector3.zero;
            _drag.target = this;
        }

        public override void Start()
        {
            base.Start();
            // Next to where Reversible Lanes puts its button (260,10) so both stay visible.
            relativePosition = new Vector3(316f, 10f);
        }

        protected override void OnClick(UIMouseEventParameter p)
        {
            base.OnClick(p);
            ToggleTool();
        }

        private void ToggleTool()
        {
            HotkeyListener.Toggle(Tool);
        }
    }
}
