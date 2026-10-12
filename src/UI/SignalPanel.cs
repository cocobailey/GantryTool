using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Detect;
using GantryTool.Render;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Per-lane signal editor, opened by clicking a signal head with the gantry tool.
    /// Top: the head's two faces as clickable previews (the face the camera sees is selected first).
    /// Middle: a grid of real LED previews; clicking one puts it on the selected face ("Auto" follows the
    /// lane's direction and TM:PE restrictions). Then "Apply this sign to" all lanes on this side or the whole
    /// gantry, Reset both faces, and Remove / Restore the head. Every change is undoable (Ctrl+Z).
    /// </summary>
    public sealed class SignalPanel : UIPanel
    {
        private const float PanelWidth = 440f;
        private const float Margin = 12f;
        private const float CardWidth = 202f;
        private const float CardHeight = 64f;
        private const float CellWidth = 62f;
        private const float CellHeight = 84f;
        private const float CellGap = 6f;

        private static readonly Color32 Selected = new Color32(150, 205, 255, 255);
        private static readonly Color32 Normal = new Color32(255, 255, 255, 255);

        private UILabel _info;
        private FaceCard _minusCard;
        private FaceCard _plusCard;
        private UILabel _categoryLabel;
        private UIPanel _grid;
        private UILabel _applyLabel;
        private UIButton _applySide;
        private UIButton _applyAll;
        private UIPanel _layoutBox;
        private UIButton _reset;
        private UIButton _remove;
        private UILabel _hint;
        private readonly List<GridCell> _cells = new List<GridCell>();

        private int _gantryId;
        private int _laneIndex;
        private float _offset;
        private int _sign;
        private bool _minusSelected = true;

        private sealed class FaceCard
        {
            public UIPanel Panel;
            public UITextureSprite Preview;
            public UILabel Text;
        }

        private sealed class GridCell
        {
            public UIPanel Panel;
            public SignalSymbol Symbol;
        }

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
            height = 420f;
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
            _info.relativePosition = new Vector3(Margin, 44f);

            _minusCard = CreateCard(new Vector3(Margin, 70f), () => SelectFace(true));
            _plusCard = CreateCard(new Vector3(PanelWidth - Margin - CardWidth, 70f), () => SelectFace(false));

            _categoryLabel = UIUtils.CreateLabel(this, "", 0.8f);
            _categoryLabel.relativePosition = new Vector3(Margin, 146f);

            _grid = AddUIComponent<UIPanel>();
            _grid.relativePosition = new Vector3(Margin, 168f);
            _grid.width = PanelWidth - 2f * Margin;
            _grid.height = CellHeight;

            _applyLabel = UIUtils.CreateLabel(this, "Apply this sign to:", 0.75f);
            _applySide = CreateButton("All lanes on this side", 150f, () => Apply(false));
            _applyAll = CreateButton("Whole gantry", 110f, () => Apply(true));
            _layoutBox = AddUIComponent<UIPanel>();
            _layoutBox.width = PanelWidth - 2f * Margin;
            _layoutBox.autoLayout = true;
            _layoutBox.autoLayoutDirection = LayoutDirection.Vertical;
            _layoutBox.autoLayoutPadding = new RectOffset(0, 0, 0, 2);

            _reset = CreateButton("Reset both to Auto", 160f, ResetToAuto);
            _remove = CreateButton("Remove signal head", 180f, ToggleRemoved);

            _hint = UIUtils.CreateLabel(this, "Click another head to edit it. Removed heads still highlight on hover. Ctrl+Z undoes.", 0.7f);
            _hint.textColor = new Color32(170, 170, 170, 255);
            _hint.autoSize = false;
            _hint.width = PanelWidth - 2f * Margin;
            _hint.wordWrap = true;
            _hint.autoHeight = true;
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

                bool newHead = g.Id != _gantryId || laneIndex != _laneIndex || !isVisible;
                GantryLane lane = g.Lanes[laneIndex];
                _gantryId = g.Id;
                _laneIndex = laneIndex;
                _offset = lane.Offset;
                _sign = lane.TravelSign;
                if (newHead)
                {
                    _minusSelected = CameraSeesMinusFace(g);
                }

                Refresh(g);
                Show();
                BringToFront();
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel.ShowFor failed", e);
            }
        }

        /// <summary>True if the panel is open on this gantry (used to close it when the gantry is deleted).</summary>
        public bool IsShowing(int gantryId)
        {
            return isVisible && _gantryId == gantryId;
        }

        public int LaneIndex => _laneIndex;

        // ------------------------------------------------------------------------------ building

        private FaceCard CreateCard(Vector3 pos, Action onClick)
        {
            var card = new FaceCard { Panel = AddUIComponent<UIPanel>() };
            card.Panel.backgroundSprite = "ButtonMenu";
            card.Panel.size = new Vector2(CardWidth, CardHeight);
            card.Panel.relativePosition = pos;
            card.Panel.eventClick += (c, p) => onClick();

            card.Preview = card.Panel.AddUIComponent<UITextureSprite>();
            card.Preview.size = new Vector2(48f, 48f);
            card.Preview.relativePosition = new Vector3(8f, 8f);
            card.Preview.isInteractive = false;

            card.Text = card.Panel.AddUIComponent<UILabel>();
            card.Text.textScale = 0.65f;
            card.Text.autoSize = false;
            card.Text.wordWrap = true;
            card.Text.size = new Vector2(CardWidth - 66f, CardHeight - 8f);
            card.Text.relativePosition = new Vector3(62f, 6f);
            card.Text.isInteractive = false;
            return card;
        }

        private UIButton CreateButton(string text, float w, Action onClick)
        {
            var b = AddUIComponent<UIButton>();
            b.text = text;
            b.textScale = 0.75f;
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.size = new Vector2(w, 28f);
            b.eventClick += (c, p) => onClick();
            return b;
        }

        /// <summary>Re-reads the gantry and redraws everything.</summary>
        private void Refresh(Gantry g)
        {
            int lane = CurrentLane(g);
            if (lane < 0)
            {
                Hide();
                return;
            }

            GantryLane info = g.Lanes[lane];
            SignStyle style = SignStyles.Resolve(g.Style);

            Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z).normalized;
            Vector2 side = new Vector2(-d.y, d.x);
            string traffic = info.TravelSign == 0 ? "two-way" : "traffic heading " + LaneScanner.Compass(side * info.TravelSign);
            _info.text = "Gantry #" + g.Id + "  ·  lane " + (lane + 1) + " of " + g.Lanes.Count + "  ·  " + traffic;

            SignalSymbol minus, plus, autoMinus, autoPlus;
            GantryManager.GetFaces(g, lane, out minus, out plus);
            SignalRules.Auto(info.TravelSign, info.Restriction, out autoMinus, out autoPlus);
            bool hidden = SignalRules.IsHidden(minus, plus);
            string note = info.Restriction == LaneRestriction.Banned ? " (TM:PE ban)"
                : info.Restriction == LaneRestriction.Reserved ? " (TM:PE bus lane)" : "";

            // Minus face has normal -side: it faces -side and is seen by traffic heading +side.
            FillCard(_minusCard, style, "Face toward " + LaneScanner.Compass(-side), LaneScanner.Compass(side),
                minus, autoMinus, hidden, note, _minusSelected);
            FillCard(_plusCard, style, "Face toward " + LaneScanner.Compass(side), LaneScanner.Compass(-side),
                plus, autoPlus, hidden, note, !_minusSelected);

            SignalSymbol current = _minusSelected ? minus : plus;
            SignalSymbol auto = _minusSelected ? autoMinus : autoPlus;
            _categoryLabel.text = SignCatalog.CategoryName(SignCategory.Lane) + (hidden ? "  ·  pick a sign to restore this head" : "");
            BuildGrid(style, current, auto, hidden);

            // Lay out what follows the grid.
            float y = _grid.relativePosition.y + _grid.height + 10f;
            _applyLabel.relativePosition = new Vector3(Margin, y + 7f);
            _applySide.relativePosition = new Vector3(Margin + 118f, y);
            _applyAll.relativePosition = new Vector3(Margin + 118f + 156f, y);
            y += 36f;
            BuildLayoutSection(g, lane, hidden);
            _layoutBox.relativePosition = new Vector3(Margin, y);
            y += _layoutBox.height + 8f;
            _reset.relativePosition = new Vector3(Margin, y);
            _remove.relativePosition = new Vector3(PanelWidth - Margin - _remove.width, y);
            _remove.text = hidden ? "Restore signal head" : "Remove signal head";
            y += 38f;
            _hint.relativePosition = new Vector3(Margin, y);
            height = y + _hint.height + 10f;
        }

        private static void FillCard(FaceCard card, SignStyle style, string toward, string seenBy, SignalSymbol symbol,
            SignalSymbol auto, bool hidden, string note, bool selected)
        {
            SignalSymbol shown = hidden ? SignalSymbol.Blank : symbol == SignalSymbol.Auto ? auto : symbol;
            card.Preview.texture = GantryMaterials.Preview(style, shown);
            string status = hidden ? "Head removed"
                : symbol == SignalSymbol.Auto ? "Auto: " + SignalRules.PlainName(auto) + note
                : SignalRules.PlainName(symbol);
            card.Text.text = toward + "\nSeen by " + seenBy + "-bound traffic\n" + status;
            card.Panel.backgroundSprite = selected ? "ButtonMenuHovered" : "ButtonMenu";
            card.Panel.color = selected ? Selected : Normal;
        }

        private void BuildGrid(SignStyle style, SignalSymbol current, SignalSymbol auto, bool hidden)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                UnityEngine.Object.Destroy(_cells[i].Panel.gameObject);
            }

            _cells.Clear();

            var items = new List<KeyValuePair<SignalSymbol, string>>
            {
                new KeyValuePair<SignalSymbol, string>(SignalSymbol.Auto, "Auto")
            };
            foreach (SignDef def in SignCatalog.InCategory(style, SignCategory.Lane))
            {
                items.Add(new KeyValuePair<SignalSymbol, string>(def.Symbol, def.GridName));
            }

            int perRow = Mathf.Max(1, (int)((_grid.width + CellGap) / (CellWidth + CellGap)));
            for (int i = 0; i < items.Count; i++)
            {
                SignalSymbol symbol = items[i].Key;
                var cell = new GridCell { Symbol = symbol, Panel = _grid.AddUIComponent<UIPanel>() };
                bool isCurrent = !hidden && symbol == current;
                cell.Panel.backgroundSprite = isCurrent ? "ButtonMenuHovered" : "ButtonMenu";
                cell.Panel.color = isCurrent ? Selected : Normal;
                cell.Panel.size = new Vector2(CellWidth, CellHeight);
                cell.Panel.relativePosition = new Vector3((i % perRow) * (CellWidth + CellGap), (i / perRow) * (CellHeight + CellGap));
                cell.Panel.tooltip = symbol == SignalSymbol.Auto
                    ? "Follow the lane's direction (now: " + SignalRules.PlainName(auto) + ")"
                    : SignalRules.DisplayName(symbol);
                cell.Panel.eventClick += (c, p) => Pick(symbol);

                var preview = cell.Panel.AddUIComponent<UITextureSprite>();
                preview.size = new Vector2(52f, 52f);
                preview.relativePosition = new Vector3(5f, 5f);
                preview.texture = GantryMaterials.Preview(style, symbol == SignalSymbol.Auto ? auto : symbol);
                preview.isInteractive = false;

                var label = cell.Panel.AddUIComponent<UILabel>();
                label.text = items[i].Value;
                label.textScale = 0.6f;
                label.autoSize = false;
                label.wordWrap = true;
                label.textAlignment = UIHorizontalAlignment.Center;
                label.size = new Vector2(CellWidth - 4f, 24f);
                label.relativePosition = new Vector3(2f, 60f);
                label.isInteractive = false;

                _cells.Add(cell);
            }

            int rows = (items.Count + perRow - 1) / perRow;
            _grid.height = rows * CellHeight + (rows - 1) * CellGap;
        }

        /// <summary>Shift along the beam, box size and their reset (also editable with the head's handles).</summary>
        private void BuildLayoutSection(Gantry g, int lane, bool hidden)
        {
            for (int i = _layoutBox.components.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_layoutBox.components[i].gameObject);
            }

            if (hidden)
            {
                _layoutBox.height = 0f;
                return;
            }

            float nudge, size;
            GantryManager.GetHeadLayout(g, lane, out nudge, out size);
            bool ownSize = size > 0f;
            float maxFace = g.BeamDepth - 0.1f - SignalMeshBuilder.Frame;

            var header = _layoutBox.AddUIComponent<UILabel>();
            header.text = "Position and size (or drag the selected head)";
            header.textScale = 0.75f;

            var helper = new UIHelper(_layoutBox);
            UIUtils.StyledSlider(helper, _layoutBox.width, "Shift along beam", -3f, 3f, 0.05f, nudge, " m", "+0.00;-0.00;0.00", true, v =>
            {
                Gantry cur = CurrentGantry();
                int l = cur != null ? CurrentLane(cur) : -1;
                if (l >= 0)
                {
                    float n, sz;
                    GantryManager.GetHeadLayout(cur, l, out n, out sz);
                    GantryManager.SetHeadLayout(cur, l, v, sz, "signal position");
                }
            });
            UIUtils.StyledSlider(helper, _layoutBox.width, "Box size", 0.4f, Mathf.Max(0.45f, maxFace), 0.05f,
                ownSize ? size : g.SignalSize, " m", "0.00", ownSize, v =>
                {
                    Gantry cur = CurrentGantry();
                    int l = cur != null ? CurrentLane(cur) : -1;
                    if (l >= 0)
                    {
                        float n, sz;
                        GantryManager.GetHeadLayout(cur, l, out n, out sz);
                        GantryManager.SetHeadLayout(cur, l, n, v, "signal size");
                    }
                });
            helper.AddCheckbox("Use the gantry's box size (" + g.SignalSize.ToString("0.00") + " m)", !ownSize, on =>
            {
                Gantry cur = CurrentGantry();
                int l = cur != null ? CurrentLane(cur) : -1;
                if (l >= 0)
                {
                    float n, sz;
                    GantryManager.GetHeadLayout(cur, l, out n, out sz);
                    GantryManager.SetHeadLayout(cur, l, n, on ? 0f : cur.SignalSize, "signal size");
                    Refresh(cur);
                }
            });

            var note = _layoutBox.AddUIComponent<UILabel>();
            note.text = "Box size is limited by the truss depth (" + g.BeamDepth.ToString("0.0") + " m); deepen the truss in the gantry panel for bigger boxes.";
            note.textScale = 0.65f;
            note.textColor = new Color32(170, 170, 170, 255);
            note.autoSize = false;
            note.wordWrap = true;
            note.width = _layoutBox.width;
            note.autoHeight = true;

            helper.AddButton("Reset position and size", () =>
            {
                Gantry cur = CurrentGantry();
                int l = cur != null ? CurrentLane(cur) : -1;
                if (l >= 0)
                {
                    GantryManager.SetHeadLayout(cur, l, 0f, 0f, "signal layout");
                    Refresh(cur);
                }
            });

            _layoutBox.FitChildrenVertically();
        }

        // ------------------------------------------------------------------------------ actions

        private Gantry CurrentGantry()
        {
            Gantry g = GantryManager.Find(_gantryId);
            if (g == null)
            {
                Hide();
            }

            return g;
        }

        private void SelectFace(bool minus)
        {
            Gantry g = CurrentGantry();
            if (g == null)
            {
                return;
            }

            _minusSelected = minus;
            Refresh(g);
        }

        private void Pick(SignalSymbol symbol)
        {
            try
            {
                Gantry g = CurrentGantry();
                if (g == null)
                {
                    return;
                }

                GantryManager.SetFace(g, CurrentLane(g), _minusSelected, symbol);
                Refresh(g);
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel pick failed", e);
            }
        }

        private void Apply(bool wholeGantry)
        {
            try
            {
                Gantry g = CurrentGantry();
                if (g == null)
                {
                    return;
                }

                SignalSymbol minus, plus;
                GantryManager.GetFaces(g, CurrentLane(g), out minus, out plus);
                if (SignalRules.IsHidden(minus, plus))
                {
                    return;
                }

                GantryManager.ApplyToAllLanes(g, _minusSelected, _minusSelected ? minus : plus, wholeGantry);
                Refresh(g);
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel apply failed", e);
            }
        }

        private void ResetToAuto()
        {
            Gantry g = CurrentGantry();
            if (g == null)
            {
                return;
            }

            GantryManager.SetOverride(g, CurrentLane(g), SignalSymbol.Auto, SignalSymbol.Auto);
            Refresh(g);
        }

        /// <summary>Removes the head (both faces Hidden) or restores a removed head to Auto.</summary>
        private void ToggleRemoved()
        {
            try
            {
                Gantry g = CurrentGantry();
                if (g == null)
                {
                    return;
                }

                int lane = CurrentLane(g);
                SignalSymbol minus, plus;
                GantryManager.GetFaces(g, lane, out minus, out plus);
                SignalSymbol s = SignalRules.IsHidden(minus, plus) ? SignalSymbol.Auto : SignalSymbol.Hidden;
                GantryManager.SetOverride(g, lane, s, s);
                Refresh(g);
            }
            catch (Exception e)
            {
                Log.Error("SignalPanel remove/restore failed", e);
            }
        }

        // ------------------------------------------------------------------------------ helpers

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

            int found = SignalRules.FindLane(g.Lanes, _offset, _sign);
            if (found >= 0)
            {
                _laneIndex = found;
            }

            return found;
        }

        /// <summary>The face pointing at the camera: the one the player is looking at.</summary>
        private static bool CameraSeesMinusFace(Gantry g)
        {
            try
            {
                Vector3 d = g.End - g.Start;
                Vector3 side = new Vector3(-d.z, 0f, d.x);
                // Looking towards +side means seeing the face whose normal points to -side (the minus face).
                return Camera.main == null || Vector3.Dot(Camera.main.transform.forward, side) >= 0f;
            }
            catch
            {
                return true;
            }
        }
    }
}
