using ColossalFramework.UI;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Makes clicking an open dropdown close it again. The game's UIDropDown closes its list on mouse-down outside
    /// the list (which includes the dropdown itself) and then reopens it on the click (UIDropDown.cs:623-656), so a
    /// second click never closes it. This notices a reopen by the same click that closed it, with no new press of the mouse in between, and closes it on the next frame
    /// (closing inside the open event would run while the list is still being set up).
    /// </summary>
    public sealed class DropdownToggle : MonoBehaviour
    {
        private UIDropDown _dropdown;
        private int _closedFrame = -1, _pressedFrame = -1;
        private bool _closeNextFrame;

        public static void Attach(UIDropDown dropdown)
        {
            if (dropdown == null || dropdown.GetComponent<DropdownToggle>() != null)
            {
                return;
            }

            var toggle = dropdown.gameObject.AddComponent<DropdownToggle>();
            toggle._dropdown = dropdown;
            dropdown.eventDropdownClose += toggle.OnClose;
            dropdown.eventDropdownOpen += toggle.OnOpen;
        }

        private void OnClose(UIDropDown dropdown, UIListBox popup, ref bool overridden)
        {
            _closedFrame = Time.frameCount;
        }

        private void OnOpen(UIDropDown dropdown, UIListBox popup, ref bool overridden)
        {
            if (_closedFrame >= 0 && _pressedFrame <= _closedFrame)
            {
                _closeNextFrame = true;
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _pressedFrame = Time.frameCount;
            }

            if (!_closeNextFrame)
            {
                return;
            }

            _closeNextFrame = false;
            _closedFrame = -1;
            if (_dropdown != null)
            {
                _dropdown.ClosePopup();
            }
        }

        private void OnDestroy()
        {
            if (_dropdown != null)
            {
                _dropdown.eventDropdownClose -= OnClose;
                _dropdown.eventDropdownOpen -= OnOpen;
            }
        }
    }
}
