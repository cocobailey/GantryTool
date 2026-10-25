using System;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Tool;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Per-gantry settings, opened by clicking a gantry's truss or posts with the tool. Built with the game's own
    /// option controls (UIHelper templates) so it looks like the options page. Every change is undoable.
    ///
    /// Height above road and truss depth are per gantry. Signal size follows the global option unless
    /// "Use default" is unticked.
    /// </summary>
    public sealed class GantryPanel : UIPanel
    {
        private const float PanelWidth = 420f;
        private const float Margin = 12f;

        public GantryPlaceTool Tool;

        private UILabel _title;
        private UILabel _info;
        private UIPanel _content;
        private int _gantryId;
        private bool _building;

        public static GantryPanel Create()
        {
            return (GantryPanel)UIView.GetAView().AddUIComponent(typeof(GantryPanel));
        }

        public override void Awake()
        {
            base.Awake();
            name = "GantryToolGantryPanel";
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;
            height = 400f;
            isVisible = false;

            _title = UIUtils.CreateLabel(this, "Gantry", 1.0f);
            _title.relativePosition = new Vector3(Margin, 10f);

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
            close.eventClick += (c, p) =>
            {
                Hide();
                if (Tool != null)
                {
                    Tool.Deselect();
                }
            };

            _info = UIUtils.CreateLabel(this, "", 0.8f);
            _info.relativePosition = new Vector3(Margin, 44f);

            _content = AddUIComponent<UIPanel>();
            _content.relativePosition = new Vector3(Margin, 68f);
            _content.width = PanelWidth - 2f * Margin;
            _content.autoLayout = true;
            _content.autoLayoutDirection = LayoutDirection.Vertical;
            _content.autoLayoutPadding = new RectOffset(0, 0, 0, 4);
        }

        public override void Start()
        {
            base.Start();
            relativePosition = new Vector3(120f, 140f);
        }

        public int GantryId => isVisible ? _gantryId : 0;

        /// <summary>Opens (or rebuilds) the panel for a gantry.</summary>
        public void ShowFor(Gantry g)
        {
            if (g == null)
            {
                Hide();
                return;
            }

            try
            {
                _building = true;
                _gantryId = g.Id;
                _title.text = "Gantry #" + g.Id;
                _info.text = "Span " + g.Span.ToString("F1") + " m, " + g.Lanes.Count + " lanes. Drag the handles to move it.";

                for (int i = _content.components.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(_content.components[i].gameObject);
                }

                var helper = new UIHelper(_content);
                Settings s = Settings.Current;

                StyleDropdown(helper, g);

                string heightLabel = float.IsNaN(g.RoadTopY) ? "Height above ground" : "Height above road";
                float height = float.IsNaN(g.HeightAboveRoad) ? s.HeightAboveRoad : g.HeightAboveRoad;
                Slider(helper, heightLabel, 2f, 15f, 0.1f, height, " m", true, v => Change(g, "height", () => g.HeightAboveRoad = v));
                Slider(helper, "Truss depth", 1f, 2.5f, 0.1f, g.BeamDepth, " m", true, v => Change(g, "truss depth", () => g.BeamDepth = v));

                bool sizeDefault = float.IsNaN(g.SignalSizeOverride);
                Slider(helper, "Signal box size", 0.6f, 2f, 0.05f, g.SignalSize, " m", !sizeDefault,
                    v => Change(g, "signal size", () => g.SignalSizeOverride = v));
                Checkbox(helper, "Use default signal box size (" + s.SignalSize.ToString("0.00") + " m)", sizeDefault, on =>
                {
                    Change(g, "signal size", () => g.SignalSizeOverride = on ? float.NaN : s.SignalSize);
                    Rebuild();
                });

                // Extra signal heads, placed anywhere on the beam.
                int extras;
                lock (GantryManager.Lock)
                {
                    extras = g.ExtraHeads.Count;
                }

                var addRow = _content.AddUIComponent<UIPanel>();
                addRow.size = new Vector2(_content.width, 34f);
                bool canAdd = extras < HeadRef.MaxExtraHeads;
                RowButton(addRow, 0, _content.width, "Add sign (" + extras + " of " + HeadRef.MaxExtraHeads + " added)",
                    canAdd ? "Add a signal head to this gantry: drag it anywhere on the beam, or link it to a lane"
                           : "This gantry has the most added signs allowed", () =>
                    {
                        Gantry cur = GantryManager.Find(_gantryId);
                        if (cur == null || Tool == null)
                        {
                            return;
                        }

                        int head = GantryManager.AddExtraHead(cur, cur.Span * 0.5f);
                        if (head >= 0)
                        {
                            Tool.EditHead(cur, head);
                        }
                    });
                addRow.components[0].isEnabled = canAdd;

                // One row of equal buttons, like the signal panel.
                var row = _content.AddUIComponent<UIPanel>();
                row.size = new Vector2(_content.width, 34f);
                float third = (_content.width - 12f) / 3f;
                RowButton(row, 0, third, "Reset to defaults",
                    "Height above road " + s.HeightAboveRoad.ToString("0.0") + " m and truss depth " + s.BeamDepth.ToString("0.0") + " m", () =>
                    {
                        Change(g, "height and depth", () =>
                        {
                            g.HeightAboveRoad = s.HeightAboveRoad;
                            g.BeamDepth = s.BeamDepth;
                        });
                        Rebuild();
                    });
                RowButton(row, 1, third, "Undo", "Undo the last change (Ctrl+Z)", () =>
                {
                    if (Tool != null)
                    {
                        Tool.UndoLast();
                    }
                });
                RowButton(row, 2, third, "Delete gantry", "Delete this gantry (Ctrl+Z brings it back)", () =>
                {
                    if (Tool != null)
                    {
                        Tool.DeleteGantry(_gantryId);
                    }
                });

                // Size the panel to its last row (FitChildrenVertically can leave a gap with template controls).
                float bottom = 0f;
                foreach (UIComponent child in _content.components)
                {
                    bottom = Mathf.Max(bottom, child.relativePosition.y + child.height);
                }

                _content.height = bottom;
                height = _content.relativePosition.y + bottom + 12f;
                Show();
                BringToFront();
            }
            catch (Exception e)
            {
                Log.Error("GantryPanel.ShowFor failed", e);
            }
            finally
            {
                _building = false;
            }
        }

        /// <summary>Re-reads the gantry (after undo or a move). Hides the panel if the gantry is gone.</summary>
        public void Rebuild()
        {
            if (!isVisible)
            {
                return;
            }

            Gantry g = GantryManager.Find(_gantryId);
            if (g == null)
            {
                Hide();
                return;
            }

            ShowFor(g);
        }

        /// <summary>Applies a change with an undo step; slider drags collapse into one step per setting.</summary>
        private void Change(Gantry g, string what, Action apply)
        {
            if (_building)
            {
                return;
            }

            try
            {
                UndoStack.PushModified(g, what);
                lock (GantryManager.Lock)
                {
                    apply();
                    g.MeshDirty = true;
                }
            }
            catch (Exception e)
            {
                Log.Error("Gantry setting change failed", e);
            }
        }

        private UISlider Slider(UIHelper helper, string text, float min, float max, float step, float value, string unit,
            bool enabled, Action<float> onChange)
        {
            return UIUtils.StyledSlider(helper, _content.width, text, min, max, step, value, unit, null, enabled, onChange);
        }

        /// <summary>"Default (Europe)" plus every available style; changing it is undoable.</summary>
        private void StyleDropdown(UIHelper helper, Gantry g)
        {
            SignStyle[] styles = SignStyles.Available;
            var items = new string[styles.Length + 1];
            items[0] = "Default: " + SignStyles.Name(SignStyles.Resolve((byte)SignStyle.Default));
            int selected = 0;
            for (int i = 0; i < styles.Length; i++)
            {
                items[i + 1] = SignStyles.Name(styles[i]);
                if ((byte)styles[i] == g.Style)
                {
                    selected = i + 1;
                }
            }

            var drop = (UIDropDown)helper.AddDropdown("Sign style", items, selected, index =>
            {
                byte style = index <= 0 || index > styles.Length ? (byte)SignStyle.Default : (byte)styles[index - 1];
                Change(g, "sign style", () =>
                {
                    g.Style = style;
                    g.SignalsDirty = true;
                });
                Log.Info("Gantry #" + g.Id + " sign style: " + SignStyles.Name((SignStyle)style));
            });
            UIComponent row = drop.parent;
            row.width = _content.width;
            drop.width = _content.width - 16f;
            DropdownToggle.Attach(drop);
        }

        private static void Checkbox(UIHelper helper, string text, bool value, Action<bool> onChange)
        {
            helper.AddCheckbox(text, value, v => onChange(v));
        }

        private static void RowButton(UIPanel row, int index, float w, string text, string tooltip, Action onClick)
        {
            var b = row.AddUIComponent<UIButton>();
            b.text = text;
            b.tooltip = tooltip;
            b.textScale = 0.75f;
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.size = new Vector2(w, 28f);
            b.relativePosition = new Vector3(index * (w + 6f), 4f);
            b.eventClick += (c, p) => onClick();
        }
    }
}
