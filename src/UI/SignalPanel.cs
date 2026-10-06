using System;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Per-lane signal editor, opened by clicking a signal head with the gantry tool. One dropdown per
    /// physical face; "Auto" follows the detected lane direction. Changes apply immediately and are saved
    /// with the city.
    /// </summary>
    public sealed class SignalPanel : UIPanel
    {
        private const float PanelWidth = 380f;
        private const float Margin = 12f;

        private UILabel _info;
        private UILabel _minusLabel;
        private UILabel _plusLabel;
        private UIDropDown _minusDrop;
        private UIDropDown _plusDrop;
        private bool _suppress;
        private bool _hidden;
        private static string _autoNote = "";
        private UIButton _removeButton;

        private int _gantryId;
        private int _laneIndex;
        private float _offset;
        private int _sign;

        public static SignalPanel Create()
        {
            return (SignalPanel)UIView.GetAView().AddUIComponent(typeof(SignalPanel));
        }

        public override void Awake()
        {
            base.Awake();
            name = "GantryToolSignalPanel";
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;
            height = 262f;
            isVisible = false;

            var title = UIUtils.CreateLabel(this, "Lane signal", 1.0f);
            title.relativePosition = new Vector3(Margin, 10f);

            var drag = AddUIComponent<UIDragHandle>();
            drag.width = PanelWidth - 40f;
            drag.height = 36f;
            drag.relativePosition = Vector3.zero;
            drag.target = this;

            var close = AddUIComponent<UIButton>();
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(PanelWidth - 36f, 4f);
            close.eventClick += (c, p) => Hide();

            _info = UIUtils.CreateLabel(this, "", 0.8f);
            _info.relativePosition = new Vector3(Margin, 46f);

            _minusLabel = UIUtils.CreateLabel(this, "", 0.8f);
            _minusLabel.relativePosition = new Vector3(Margin, 74f);
            _minusDrop = UIUtils.CreateDropDown(this, PanelWidth - 2f * Margin);
            _minusDrop.relativePosition = new Vector3(Margin, 94f);
            _minusDrop.eventSelectedIndexChanged += (c, i) => OnChanged();

            _plusLabel = UIUtils.CreateLabel(this, "", 0.8f);
            _plusLabel.relativePosition = new Vector3(Margin, 134f);
            _plusDrop = UIUtils.CreateDropDown(this, PanelWidth - 2f * Margin);
            _plusDrop.relativePosition = new Vector3(Margin, 154f);
            _plusDrop.eventSelectedIndexChanged += (c, i) => OnChanged();

            var reset = AddUIComponent<UIButton>();
            reset.text = "Reset both to Auto";
            reset.textScale = 0.8f;
            reset.normalBgSprite = "ButtonMenu";
            reset.hoveredBgSprite = "ButtonMenuHovered";
            reset.pressedBgSprite = "ButtonMenuPressed";
            reset.size = new Vector2(160f, 28f);
            reset.relativePosition = new Vector3(Margin, 196f);
            reset.eventClick += (c, p) => ResetToAuto();

            _removeButton = AddUIComponent<UIButton>();
            _removeButton.text = "Remove signal head";
            _removeButton.textScale = 0.8f;
            _removeButton.normalBgSprite = "ButtonMenu";
            _removeButton.hoveredBgSprite = "ButtonMenuHovered";
            _removeButton.pressedBgSprite = "ButtonMenuPressed";
            _removeButton.size = new Vector2(180f, 28f);
            _removeButton.relativePosition = new Vector3(PanelWidth - Margin - 180f, 196f);
            _removeButton.eventClick += (c, p) => ToggleRemoved();

            var hint = UIUtils.CreateLabel(this, "Click another head to edit it. Removed heads still highlight on hover.", 0.7f);
            hint.textColor = new Color32(170, 170, 170, 255);
            hint.relativePosition = new Vector3(Margin, 234f);

            // Wrap inside the panel instead of running past its edge, and grow the panel to fit.
            hint.autoSize = false;
            hint.width = PanelWidth - 2f * Margin;
            hint.wordWrap = true;
            hint.autoHeight = true;
            height = hint.relativePosition.y + hint.height + 10f;
        }

        public override void Start()
        {
            base.Start();
            relativePosition = new Vector3(120f, 140f);
        }

        /// <summary>Opens the editor for lane <paramref name="laneIndex"/> of a gantry.</summary>
        public void ShowFor(Gantry g, int laneIndex)
        {
            try
            {
                if (g == null || laneIndex < 0 || laneIndex >= g.Lanes.Count)
                {
                    return;
                }

                GantryLane lane = g.Lanes[laneIndex];
                _gantryId = g.Id;
                _laneIndex = laneIndex;
                _offset = lane.Offset;
                _sign = lane.TravelSign;

                Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z).normalized;
                Vector2 side = new Vector2(-d.y, d.x);
                string traffic = lane.TravelSign == 0
                    ? "two-way"
                    : "traffic heading " + LaneScanner.Compass(side * lane.TravelSign);
                _info.text = "Gantry #" + g.Id + "  ·  lane " + (laneIndex + 1) + " of " + g.Lanes.Count + "  ·  " + traffic;

                // Minus face has normal -side: it faces -side and is seen by traffic heading +side.
                _minusLabel.text = "Face toward " + LaneScanner.Compass(-side) + " (seen by " +
                                   LaneScanner.Compass(side) + "-bound traffic)";
                _plusLabel.text = "Face toward " + LaneScanner.Compass(side) + " (seen by " +
                                  LaneScanner.Compass(-side) + "-bound traffic)";

                SignalSymbol autoMinus, autoPlus;
                SignalRules.Auto(lane.TravelSign, lane.Restriction, out autoMinus, out autoPlus);
                _autoNote = lane.Restriction == LaneRestriction.Banned ? " - TM:PE ban"
                    : lane.Restriction == LaneRestriction.Reserved ? " - TM:PE bus lane" : "";
                SignalSymbol minus = SignalSymbol.Auto, plus = SignalSymbol.Auto;
                lock (GantryManager.Lock)
                {
                    int idx = SignalRules.FindOverride(g.Lanes, laneIndex, g.Overrides);
                    if (idx >= 0)
                    {
                        minus = g.Overrides[idx].Minus;
                        plus = g.Overrides[idx].Plus;
                    }
                }

                _hidden = SignalRules.IsHidden(minus, plus);
                _suppress = true;
                _minusDrop.items = Items(autoMinus);
                _plusDrop.items = Items(autoPlus);
                _minusDrop.selectedIndex = _hidden || minus > SignalSymbol.Blank ? 0 : (int)minus;
                _plusDrop.selectedIndex = _hidden || plus > SignalSymbol.Blank ? 0 : (int)plus;
                _suppress = false;
                UpdateRemovedState();

                Show();
                BringToFront();
            }
            catch (Exception e)
            {
                _suppress = false;
                Log.Error("SignalPanel.ShowFor failed", e);
            }
        }

        /// <summary>Dropdown items in SignalSymbol order (index == enum value).</summary>
        private static string[] Items(SignalSymbol auto)
        {
            var items = new string[(int)SignalSymbol.Blank + 1];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = i == 0
                    ? "Auto (" + SignalRules.DisplayName(auto) + _autoNote + ")"
                    : SignalRules.DisplayName((SignalSymbol)i);
            }

            return items;
        }

        private void OnChanged()
        {
            if (_suppress)
            {
                return;
            }

            try
            {
                Gantry g = GantryManager.Find(_gantryId);
                if (g == null)
                {
                    Hide();
                    return;
                }

                GantryManager.SetOverride(g, CurrentLane(g), (SignalSymbol)_minusDrop.selectedIndex,
                    (SignalSymbol)_plusDrop.selectedIndex);
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel change failed", e);
            }
        }

        /// <summary>
        /// The edited lane's index now: lanes can be rescanned while the panel is open (road changes), so it is
        /// found again by its remembered position and direction.
        /// </summary>
        private int CurrentLane(Gantry g)
        {
            if (_laneIndex >= 0 && _laneIndex < g.Lanes.Count &&
                Mathf.Abs(g.Lanes[_laneIndex].Offset - _offset) < 0.01f && g.Lanes[_laneIndex].TravelSign == _sign)
            {
                return _laneIndex;
            }

            return SignalRules.FindLane(g.Lanes, _offset, _sign);
        }

        /// <summary>Removes the head (both faces Hidden) or restores a removed head to Auto.</summary>
        private void ToggleRemoved()
        {
            try
            {
                Gantry g = GantryManager.Find(_gantryId);
                if (g == null)
                {
                    Hide();
                    return;
                }

                _hidden = !_hidden;
                SignalSymbol s = _hidden ? SignalSymbol.Hidden : SignalSymbol.Auto;
                _suppress = true;
                _minusDrop.selectedIndex = 0;
                _plusDrop.selectedIndex = 0;
                _suppress = false;
                GantryManager.SetOverride(g, CurrentLane(g), s, s);
                UpdateRemovedState();
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel remove/restore failed", e);
            }
        }

        private void UpdateRemovedState()
        {
            _removeButton.text = _hidden ? "Restore signal head" : "Remove signal head";
            _minusDrop.isEnabled = !_hidden;
            _plusDrop.isEnabled = !_hidden;
        }

        private void ResetToAuto()
        {
            _hidden = false;
            UpdateRemovedState();
            _suppress = true;
            _minusDrop.selectedIndex = 0;
            _plusDrop.selectedIndex = 0;
            _suppress = false;
            OnChanged();
        }

        /// <summary>True if the panel is open on this gantry (used to close it when the gantry is deleted).</summary>
        public bool IsShowing(int gantryId)
        {
            return isVisible && _gantryId == gantryId;
        }

        public int LaneIndex => _laneIndex;
    }
}
