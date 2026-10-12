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
    /// Post height and truss depth are stored per gantry (as in 1.0). Clearance and signal size follow the
    /// global options unless "Use default" is unticked.
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
                _info.text = "Span " + g.Span.ToString("F1") + " m  ·  " + g.Lanes.Count + " lanes  ·  drag the handles to move";

                for (int i = _content.components.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(_content.components[i].gameObject);
                }

                var helper = new UIHelper(_content);
                Settings s = Settings.Current;

                Slider(helper, "Post height", 4f, 15f, 0.5f, g.PostHeight, " m", true, v => Change(g, "post height", () => g.PostHeight = v));
                Slider(helper, "Truss depth", 1f, 2.5f, 0.1f, g.BeamDepth, " m", true, v => Change(g, "truss depth", () => g.BeamDepth = v));

                bool clrDefault = float.IsNaN(g.ClearanceOverride);
                Slider(helper, "Road clearance", 4f, 9f, 0.1f, g.Clearance, " m", !clrDefault,
                    v => Change(g, "road clearance", () => g.ClearanceOverride = v));
                Checkbox(helper, "Use default road clearance (" + s.RoadClearance.ToString("0.#") + " m)", clrDefault, on =>
                {
                    Change(g, "road clearance", () => g.ClearanceOverride = on ? float.NaN : s.RoadClearance);
                    Rebuild();
                });

                bool sizeDefault = float.IsNaN(g.SignalSizeOverride);
                Slider(helper, "Signal box size", 0.6f, 2f, 0.05f, g.SignalSize, " m", !sizeDefault,
                    v => Change(g, "signal size", () => g.SignalSizeOverride = v));
                Checkbox(helper, "Use default signal box size (" + s.SignalSize.ToString("0.##") + " m)", sizeDefault, on =>
                {
                    Change(g, "signal size", () => g.SignalSizeOverride = on ? float.NaN : s.SignalSize);
                    Rebuild();
                });

                Button(helper, "Reset height and depth to defaults (" + s.PostHeight.ToString("0.#") + " m / " +
                               s.BeamDepth.ToString("0.#") + " m)", () =>
                {
                    Change(g, "height and depth", () =>
                    {
                        g.PostHeight = s.PostHeight;
                        g.BeamDepth = s.BeamDepth;
                    });
                    Rebuild();
                });
                Button(helper, "Undo (Ctrl+Z)", () =>
                {
                    if (Tool != null)
                    {
                        Tool.UndoLast();
                    }
                });
                Button(helper, "Delete gantry", () =>
                {
                    if (Tool != null)
                    {
                        Tool.DeleteGantry(_gantryId);
                    }
                });

                _content.FitChildrenVertically();
                height = _content.relativePosition.y + _content.height + 10f;
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
            UILabel label = null;
            var slider = (UISlider)helper.AddSlider(text, min, max, step, Mathf.Clamp(value, min, max), v =>
            {
                if (label != null)
                {
                    label.text = text + ": " + v.ToString("0.##") + unit;
                }

                onChange(v);
            });

            // UIHelper's slider template: a panel holding a UILabel "Label" and the slider (UIHelper.cs:49-51).
            UIComponent row = slider.parent;
            row.width = _content.width;
            slider.width = _content.width - 16f;
            label = row.Find<UILabel>("Label");
            if (label != null)
            {
                label.wordWrap = false;
                label.text = text + ": " + value.ToString("0.##") + unit;
            }

            slider.isEnabled = enabled;
            return slider;
        }

        private static void Checkbox(UIHelper helper, string text, bool value, Action<bool> onChange)
        {
            helper.AddCheckbox(text, value, v => onChange(v));
        }

        private static void Button(UIHelper helper, string text, Action onClick)
        {
            var button = (UIButton)helper.AddButton(text, () => onClick());
            button.textScale = 0.8f;
        }
    }
}
