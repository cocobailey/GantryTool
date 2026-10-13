using System;
using ColossalFramework.UI;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// UI construction helpers. The dropdown skinning follows the widely-used
    /// SamsamTS pattern (same sprites the game's own options UI uses).
    /// </summary>
    public static class UIUtils
    {
        public static UIDropDown CreateDropDown(UIComponent parent, float width)
        {
            var dropDown = parent.AddUIComponent<UIDropDown>();
            dropDown.size = new Vector2(width, 30f);
            dropDown.listBackground = "GenericPanelLight";
            dropDown.itemHeight = 24;
            dropDown.itemHover = "ListItemHover";
            dropDown.itemHighlight = "ListItemHighlight";
            dropDown.normalBgSprite = "ButtonMenu";
            dropDown.disabledBgSprite = "ButtonMenuDisabled";
            dropDown.hoveredBgSprite = "ButtonMenuHovered";
            dropDown.focusedBgSprite = "ButtonMenu";
            dropDown.listWidth = (int)width;
            dropDown.listHeight = 480;
            dropDown.foregroundSpriteMode = UIForegroundSpriteMode.Stretch;
            dropDown.popupColor = new Color32(45, 52, 61, 255);
            dropDown.popupTextColor = new Color32(170, 170, 170, 255);
            dropDown.zOrder = 1;
            dropDown.textScale = 0.8f;
            dropDown.verticalAlignment = UIVerticalAlignment.Middle;
            dropDown.horizontalAlignment = UIHorizontalAlignment.Left;
            dropDown.selectedIndex = 0;
            dropDown.textFieldPadding = new RectOffset(8, 0, 8, 0);
            dropDown.itemPadding = new RectOffset(14, 0, 5, 0);

            var button = dropDown.AddUIComponent<UIButton>();
            dropDown.triggerButton = button;
            button.text = "";
            button.size = dropDown.size;
            button.relativePosition = new Vector3(0f, 0f);
            button.textVerticalAlignment = UIVerticalAlignment.Middle;
            button.textHorizontalAlignment = UIHorizontalAlignment.Left;
            button.normalFgSprite = "IconDownArrow";
            button.hoveredFgSprite = "IconDownArrowHovered";
            button.pressedFgSprite = "IconDownArrowPressed";
            button.focusedFgSprite = "IconDownArrowFocused";
            button.disabledFgSprite = "IconDownArrowDisabled";
            button.foregroundSpriteMode = UIForegroundSpriteMode.Fill;
            button.horizontalAlignment = UIHorizontalAlignment.Right;
            button.verticalAlignment = UIVerticalAlignment.Middle;
            button.zOrder = 0;
            button.textScale = 0.8f;

            dropDown.eventSizeChanged += (component, size) =>
            {
                button.size = size;
                dropDown.listWidth = (int)size.x;
            };

            return dropDown;
        }

        /// <summary>
        /// Hides a component's tooltip if it is showing. The game leaves it on screen when the component is destroyed
        /// or hidden while the mouse is over it.
        /// </summary>
        public static void HideTooltip(UIComponent c)
        {
            if (c != null && c.tooltipBox != null && c.tooltipBox.isVisible)
            {
                c.tooltipBox.Hide();
            }
        }

        /// <summary>
        /// A slider from the options-page template (UIHelper), sized to <paramref name="rowWidth"/>, with its value in
        /// the label and colours that read clearly on a MenuPanel2 background. <paramref name="format"/> null =
        /// decimals chosen from the step.
        /// </summary>
        public static UISlider StyledSlider(UIHelper helper, float rowWidth, string text, float min, float max, float step,
            float value, string unit, string format, bool enabled, Action<float> onChange, float labelScale = 0f)
        {
            UILabel label = null;
            string fmt = format ?? (step >= 1f ? "0" : step >= 0.1f ? "0.0" : "0.00");
            var slider = (UISlider)helper.AddSlider(text, min, max, step, Mathf.Clamp(value, min, max), v =>
            {
                if (label != null)
                {
                    label.text = text + ": " + v.ToString(fmt) + unit;
                }

                onChange(v);
            });

            // UIHelper's slider template: a panel holding a UILabel "Label" and the slider (UIHelper.cs:49-51).
            UIComponent row = slider.parent;
            row.width = rowWidth;
            slider.width = rowWidth - 16f;
            label = row.Find<UILabel>("Label");
            if (label != null)
            {
                label.wordWrap = false;
                label.autoSize = true;
                if (labelScale > 0f)
                {
                    label.textScale = labelScale;
                }

                label.text = text + ": " + value.ToString(fmt) + unit;
            }

            // The options-page template is dark on a MenuPanel2 background and reads as disabled. Tint it lighter
            // with a bright handle, and keep the dark look for genuinely disabled ("Use default") sliders.
            slider.color = new Color32(170, 182, 196, 255);
            slider.disabledColor = new Color32(55, 58, 62, 255);
            if (slider.thumbObject != null)
            {
                slider.thumbObject.color = new Color32(255, 255, 255, 255);
                slider.thumbObject.disabledColor = new Color32(95, 98, 102, 255);
            }

            slider.isEnabled = enabled;
            if (label != null && !enabled)
            {
                label.textColor = new Color32(150, 150, 150, 255);
            }

            return slider;
        }

        public static UILabel CreateLabel(UIComponent parent, string text, float textScale)
        {
            var label = parent.AddUIComponent<UILabel>();
            label.text = text;
            label.textScale = textScale;
            label.autoSize = true;
            return label;
        }
    }
}
