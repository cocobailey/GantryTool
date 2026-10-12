using ColossalFramework.UI;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Makes clicking an open dropdown close it again. The game's UIDropDown closes its list on mouse-down outside
    /// the list (which includes the dropdown itself) and then reopens it on the click (UIDropDown.cs:623-656), so a
    /// second click never closes it. This notices a reopen right after a close and closes it on the next frame
    /// (closing inside the open event would run while the list is still being set up).
    /// </summary>
    public sealed class DropdownToggle : MonoBehaviour
    {
        private const float SameClickSeconds = 0.35f;

        private UIDropDown _dropdown;
        private float _closedAt = -10f;
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
            _closedAt = Time.realtimeSinceStartup;
        }

        private void OnOpen(UIDropDown dropdown, UIListBox popup, ref bool overridden)
        {
            if (Time.realtimeSinceStartup - _closedAt < SameClickSeconds)
            {
                _closeNextFrame = true;
            }
        }

        private void Update()
        {
            if (!_closeNextFrame)
            {
                return;
            }

            _closeNextFrame = false;
            _closedAt = -10f;
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
