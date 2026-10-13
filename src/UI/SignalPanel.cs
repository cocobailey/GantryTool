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
        private const float ButtonGap = 6f;
        private const float ContentWidth = PanelWidth - 2f * Margin;
        private const float HalfWidth = (ContentWidth - ButtonGap) / 2f;
        private const float ThirdWidth = (ContentWidth - 2f * ButtonGap) / 3f;

        private static readonly Color32 Selected = new Color32(150, 205, 255, 255);
        private static readonly Color32 Normal = new Color32(255, 255, 255, 255);

        private UILabel _info;
        private FaceCard _minusCard;
        private FaceCard _plusCard;
        private UILabel _categoryLabel;
        private UIDropDown _categoryDrop;
        private SignCategory _category = SignCategory.Lane;
        private bool _suppressCategory;
        private int _relabelFrames;
        private UIPanel _grid;
        private UILabel _applyLabel;
        private UIButton _applySide;
        private UIButton _applyAll;
        private UIPanel _layoutBox;
        private UIButton _resetLayout;
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
        }

        /// <summary>One sign in the picker grid.</summary>
        private struct PickItem
        {
            public FaceSign Sign;      // what gets stored on the face
            public FaceSign Preview;   // what the preview shows (Auto items show what they resolve to now)
            public string Label;
            public string Tooltip;
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

            _categoryDrop = UIUtils.CreateDropDown(this, 200f);
            _categoryDrop.relativePosition = new Vector3(Margin, 142f);
            var names = new string[SignCatalog.Categories.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = SignCatalog.CategoryName(SignCatalog.Categories[i]);
            }

            _categoryDrop.items = names;
            _categoryDrop.selectedIndex = 0;
            _categoryDrop.eventSelectedIndexChanged += (c, i) =>
            {
                if (_suppressCategory || i < 0 || i >= SignCatalog.Categories.Length)
                {
                    return;
                }

                _category = SignCatalog.Categories[i];
                Gantry g = CurrentGantry();
                if (g != null)
                {
                    Refresh(g);
                }
            };
            DropdownToggle.Attach(_categoryDrop);

            _categoryLabel = UIUtils.CreateLabel(this, "", 0.7f);
            _categoryLabel.textColor = new Color32(185, 185, 185, 255);
            _categoryLabel.relativePosition = new Vector3(Margin + 210f, 150f);

            _grid = AddUIComponent<UIPanel>();
            _grid.relativePosition = new Vector3(Margin, 182f);
            _grid.width = PanelWidth - 2f * Margin;
            _grid.height = CellHeight;

            _applyLabel = UIUtils.CreateLabel(this, "Apply this sign to:", 0.75f);
            _applySide = CreateButton("All lanes on this side", HalfWidth, () => Apply(false));
            _applySide.tooltip = "Put this face's sign on the same side of every lane";
            _applyAll = CreateButton("Whole gantry", HalfWidth, () => Apply(true));
            _applyAll.tooltip = "Put this sign on both sides of every lane";
            _layoutBox = AddUIComponent<UIPanel>();
            _layoutBox.width = PanelWidth - 2f * Margin;

            _reset = CreateButton("Reset signs", ThirdWidth, ResetToAuto);
            _reset.tooltip = "Set both faces back to Auto";
            _resetLayout = CreateButton("Reset position", ThirdWidth, ResetHeadLayout);
            _resetLayout.tooltip = "Centre this head over its lane at the gantry's box size";
            _remove = CreateButton("Remove head", ThirdWidth, ToggleRemoved);

            _hint = UIUtils.CreateLabel(this, "Click another head to edit it. Removed heads still highlight on hover. Ctrl+Z undoes.", 0.7f);
            _hint.textColor = new Color32(170, 170, 170, 255);
            _hint.autoSize = false;
            _hint.width = PanelWidth - 2f * Margin;
            _hint.wordWrap = true;
            _hint.autoHeight = true;
        }

        public override void Update()
        {
            base.Update();

            // Labels filled while the panel was hidden can show blank on its first open (their text is measured before
            // the font has those letters at that size). Redraw them once the panel has been on screen for a frame.
            if (_relabelFrames > 0 && --_relabelFrames == 0)
            {
                foreach (UILabel label in GetComponentsInChildren<UILabel>())
                {
                    label.Invalidate();
                }
            }
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
                    FaceSign m, p;
                    GantryManager.GetFaces(g, laneIndex, out m, out p);
                    SetCategory(SignCatalog.CategoryOf((_minusSelected ? m : p).Symbol));
                }

                Refresh(g);
                _relabelFrames = 2;
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
            _info.text = "Gantry #" + g.Id + ", lane " + (lane + 1) + " of " + g.Lanes.Count + ", " + traffic;

            FaceSign minus, plus, shownMinus, shownPlus;
            SignalSymbol autoMinus, autoPlus;
            GantryManager.GetFaces(g, lane, out minus, out plus);
            lock (GantryManager.Lock)
            {
                SignalRules.ResolveFaces(g.Lanes, lane, g.Overrides, out shownMinus, out shownPlus);
            }

            SignalRules.Auto(info.TravelSign, info.Restriction, out autoMinus, out autoPlus);
            bool hidden = SignalRules.IsHidden(minus.Symbol, plus.Symbol);
            bool mph = SignStyles.UsesMph(style);
            string note = info.Restriction == LaneRestriction.Banned ? " (TM:PE ban)"
                : info.Restriction == LaneRestriction.Reserved ? " (TM:PE bus lane)" : "";

            // Minus face has normal -side: it faces -side and is seen by traffic heading +side.
            FillCard(_minusCard, style, mph, "Faces " + LaneScanner.Compass(-side), LaneScanner.Compass(side),
                minus, shownMinus, autoMinus, hidden, note, _minusSelected);
            FillCard(_plusCard, style, mph, "Faces " + LaneScanner.Compass(side), LaneScanner.Compass(-side),
                plus, shownPlus, autoPlus, hidden, note, !_minusSelected);

            FaceSign current = _minusSelected ? minus : plus;
            SignalSymbol auto = _minusSelected ? autoMinus : autoPlus;
            _categoryLabel.text = hidden ? "Pick a sign to restore this head" : "";
            BuildGrid(style, Items(style, mph, auto, info.SpeedKmh), current, hidden, mph);

            // Lay out what follows the grid: Apply row, position and size, then one row of actions.
            float y = _grid.relativePosition.y + _grid.height + 12f;
            _applyLabel.relativePosition = new Vector3(Margin, y);
            y += 18f;
            _applySide.relativePosition = new Vector3(Margin, y);
            _applyAll.relativePosition = new Vector3(Margin + HalfWidth + ButtonGap, y);
            _applySide.isEnabled = _applyAll.isEnabled = !hidden;
            y += 40f;

            BuildLayoutSection(g, lane, hidden);
            _layoutBox.relativePosition = new Vector3(Margin, y);
            y += _layoutBox.height + (hidden ? 0f : 10f);

            _reset.relativePosition = new Vector3(Margin, y);
            _resetLayout.relativePosition = new Vector3(Margin + ThirdWidth + ButtonGap, y);
            _remove.relativePosition = new Vector3(Margin + 2f * (ThirdWidth + ButtonGap), y);
            _remove.text = hidden ? "Restore head" : "Remove head";
            _remove.tooltip = hidden ? "Bring this head back (signs on Auto)" : "Remove this head from the gantry";
            _resetLayout.isEnabled = !hidden;
            y += 40f;

            // The hint wraps to two lines; leave room for both (its height isn't final until it's laid out).
            _hint.relativePosition = new Vector3(Margin, y);
            height = y + Mathf.Max(_hint.height, 32f) + 12f;
        }

        /// <summary>One log line about a label's state (diagnoses labels that render blank in some sessions).</summary>
        private static void LogLabel(string name, UILabel l)
        {
            if (l == null)
            {
                return;
            }

            Log.Info("Label " + name + ": visible=" + l.isVisible + " len=" + (l.text ?? "").Length + " size=" + l.size +
                     " scale=" + l.textScale + " opacity=" + l.opacity + " color=" + l.textColor +
                     " font=" + (l.font != null ? l.font.name + (l.font.isValid ? "" : " (invalid)") : "null"));
        }

        private static void FillCard(FaceCard card, SignStyle style, bool mph, string toward, string seenBy, FaceSign sign,
            FaceSign shown, SignalSymbol auto, bool hidden, string note, bool selected)
        {
            card.Preview.texture = GantryMaterials.Preview(style, hidden ? new FaceSign(SignalSymbol.Blank, 0) : shown);
            string status = hidden ? "Head removed"
                : sign.Symbol == SignalSymbol.Auto ? "Auto: " + SignalRules.PlainName(auto) + note
                : sign.Symbol == SignalSymbol.SpeedAuto ? "Road speed: " + SignalRules.PlainName(shown, mph).Replace("Speed ", "")
                : SignalRules.PlainName(sign, mph);
            card.Text.text = toward + "\n" + seenBy + "-bound traffic\n" + status;
            card.Panel.backgroundSprite = selected ? "ButtonMenuHovered" : "ButtonMenu";
            card.Panel.color = selected ? Selected : Normal;
        }

        /// <summary>The picker's signs for the selected category.</summary>
        private List<PickItem> Items(SignStyle style, bool mph, SignalSymbol autoLane, float laneKmh)
        {
            var items = new List<PickItem>();
            string unit = mph ? " mph" : " km/h";
            if (_category == SignCategory.Speed)
            {
                int road = SpeedUnits.Display(Mathf.RoundToInt(laneKmh), mph);
                items.Add(new PickItem
                {
                    Sign = new FaceSign(SignalSymbol.SpeedAuto, 0),
                    Preview = new FaceSign(SignalSymbol.SpeedLimit, (ushort)Mathf.RoundToInt(laneKmh)),
                    Label = "Road (" + road + ")",
                    Tooltip = "The speed limit of the road underneath (now " + road + unit + "); follows changes, including TM:PE"
                });
                foreach (int v in SpeedUnits.Values(mph))
                {
                    var sign = new FaceSign(SignalSymbol.SpeedLimit, SpeedUnits.ToKmh(v, mph));
                    items.Add(new PickItem { Sign = sign, Preview = sign, Label = v.ToString(), Tooltip = "Speed limit " + v + unit });
                }

                return items;
            }

            if (_category == SignCategory.Lane)
            {
                items.Add(new PickItem
                {
                    Sign = new FaceSign(SignalSymbol.Auto, 0),
                    Preview = new FaceSign(autoLane, 0),
                    Label = "Auto",
                    Tooltip = "Follow the lane's direction (now: " + SignalRules.PlainName(autoLane) + ")"
                });
            }

            foreach (SignDef def in SignCatalog.InCategory(style, _category))
            {
                items.Add(new PickItem
                {
                    Sign = new FaceSign(def.Symbol, 0),
                    Preview = new FaceSign(def.Symbol, 0),
                    Label = def.GridName,
                    Tooltip = SignalRules.DisplayName(def.Symbol)
                });
            }

            return items;
        }

        /// <summary>Whether a grid item is what the face shows now (speeds compare by the number on the sign).</summary>
        private static bool IsCurrent(PickItem item, FaceSign current, bool mph)
        {
            if (item.Sign.Symbol != current.Symbol)
            {
                return false;
            }

            return item.Sign.Symbol != SignalSymbol.SpeedLimit ||
                   SpeedUnits.Display(item.Sign.Value, mph) == SpeedUnits.Display(current.Value, mph);
        }

        private void BuildGrid(SignStyle style, List<PickItem> items, FaceSign current, bool hidden, bool mph)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                UIUtils.HideTooltip(_cells[i].Panel); // a tile destroyed under the mouse leaves its tooltip on screen
                UnityEngine.Object.Destroy(_cells[i].Panel.gameObject);
            }

            _cells.Clear();

            int perRow = Mathf.Max(1, (int)((_grid.width + CellGap) / (CellWidth + CellGap)));
            for (int i = 0; i < items.Count; i++)
            {
                PickItem item = items[i];
                var cell = new GridCell { Panel = _grid.AddUIComponent<UIPanel>() };
                bool isCurrent = !hidden && IsCurrent(item, current, mph);
                cell.Panel.backgroundSprite = isCurrent ? "ButtonMenuHovered" : "ButtonMenu";
                cell.Panel.color = isCurrent ? Selected : Normal;
                cell.Panel.size = new Vector2(CellWidth, CellHeight);
                cell.Panel.relativePosition = new Vector3((i % perRow) * (CellWidth + CellGap), (i / perRow) * (CellHeight + CellGap));
                cell.Panel.tooltip = item.Tooltip;
                cell.Panel.eventClick += (c, p) => Pick(item.Sign);

                var preview = cell.Panel.AddUIComponent<UITextureSprite>();
                preview.size = new Vector2(52f, 52f);
                preview.relativePosition = new Vector3(5f, 5f);
                preview.texture = GantryMaterials.Preview(style, item.Preview);
                preview.isInteractive = false;

                var label = cell.Panel.AddUIComponent<UILabel>();
                label.text = item.Label;
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

        private void SetCategory(SignCategory category)
        {
            _category = category;
            int index = System.Array.IndexOf(SignCatalog.Categories, category);
            _suppressCategory = true;
            _categoryDrop.selectedIndex = index < 0 ? 0 : index;
            _suppressCategory = false;
        }

        /// <summary>Shift along the beam, box size and their reset (also editable with the head's handles).</summary>
        private void BuildLayoutSection(Gantry g, int lane, bool hidden)
        {
            // Detach before destroying: Destroy only happens at the end of the frame, and a second refresh in the same
            // frame would otherwise measure the old controls too (a big empty gap under the section).
            for (int i = _layoutBox.components.Count - 1; i >= 0; i--)
            {
                UIComponent old = _layoutBox.components[i];
                _layoutBox.RemoveUIComponent(old);
                UnityEngine.Object.Destroy(old.gameObject);
            }

            if (hidden)
            {
                _layoutBox.height = 0f;
                return;
            }

            float nudge, size;
            GantryManager.GetHeadLayout(g, lane, out nudge, out size);
            bool ownSize = size > 0f;

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
            }, 0.8f);
            UIUtils.StyledSlider(helper, _layoutBox.width, "Box size", 0.4f, Tool.GantryPlaceTool.MaxHeadFace, 0.05f,
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
                }, 0.8f);
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
            note.textScale = 0.65f;
            note.textColor = new Color32(170, 170, 170, 255);
            note.autoSize = false;
            note.wordWrap = true;
            note.width = _layoutBox.width;
            note.autoHeight = true;
            note.text = "Boxes taller than the truss stand on its bottom edge and rise above it, so road clearance doesn't change.";
            if (note.height < 20f)
            {
                LogLabel("note", note);
                note.autoHeight = false;
                note.height = 26f; // two lines; the text was not measured
            }

            // Stack the controls ourselves so the height never depends on when auto layout runs.
            float y = 0f;
            for (int i = 0; i < _layoutBox.components.Count; i++)
            {
                UIComponent c = _layoutBox.components[i];
                c.relativePosition = new Vector3(0f, y);
                y += c.height + 2f;
            }

            _layoutBox.height = y;
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

        private void Pick(FaceSign sign)
        {
            try
            {
                Gantry g = CurrentGantry();
                if (g == null)
                {
                    return;
                }

                GantryManager.SetFace(g, CurrentLane(g), _minusSelected, sign);
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

                FaceSign minus, plus;
                GantryManager.GetFaces(g, CurrentLane(g), out minus, out plus);
                if (SignalRules.IsHidden(minus.Symbol, plus.Symbol))
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

        private void ResetHeadLayout()
        {
            Gantry g = CurrentGantry();
            int l = g != null ? CurrentLane(g) : -1;
            if (l >= 0)
            {
                GantryManager.SetHeadLayout(g, l, 0f, 0f, "signal layout");
                Refresh(g);
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
