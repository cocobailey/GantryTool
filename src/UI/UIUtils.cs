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
