using System;
using System.Collections.Generic;
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
        }

        public override void Start()
        {
            base.Start();
            relativePosition = new Vector3(120f, 140f);
        }

        public int GantryId => isVisible ? _gantryId : 0;

        /// <summary>Opens (or rebuilds) the panel for a gantry.</summary>
        public override void Update()
        {
            base.Update();
            UIUtils.KeepTooltipHidden(this);
        }

        public void ShowFor(Gantry g)
        {
            if (g == null)
            {
                Hide();
                return;
            }

            try
            {
                UIUtils.ClearTooltips(_content);
                _building = true;
                _gantryId = g.Id;
                _title.text = "Gantry #" + g.Id;
                _info.text = "Span " + g.Span.ToString("F1") + " m, " + g.Lanes.Count + " lanes. Drag the handles to move it.";

                // Detach before destroying: Destroy only happens at the end of the frame, and the rows are measured below.
                for (int i = _content.components.Count - 1; i >= 0; i--)
                {
                    UIComponent old = _content.components[i];
                    _content.RemoveUIComponent(old);
                    UnityEngine.Object.Destroy(old.gameObject);
                }

                var helper = new UIHelper(_content);
                Settings s = Settings.Current;

                bool bare;
                lock (GantryManager.Lock)
                {
                    bare = g.IsBare;
                }

                TypeButtons(g);
                LookButtons(g);
                if (g.Type == GantryShape.MedianT)
                {
                    float post = GantryShape.PostPosition(g.PostAlong, g.Lanes, g.Span);
                    Slider(helper, "Post position", 0f, Mathf.Max(0.5f, g.Span), 0.1f, post, " m", true, v => Change(g, "post position", () => g.PostAlong = v));
                }

                if (!bare)
                {
                    StyleDropdown(helper, g);
                }

                string heightLabel = float.IsNaN(g.RoadTopY) ? "Height above ground" : "Height above road";
                float aboveRoad = float.IsNaN(g.HeightAboveRoad) ? s.HeightAboveRoad : g.HeightAboveRoad;
                Slider(helper, heightLabel, 2f, 15f, 0.1f, aboveRoad, " m", true, v => Change(g, "height", () => g.HeightAboveRoad = v));
                Slider(helper, "Truss depth", 1f, 2.5f, 0.1f, g.BeamDepth, " m", true, v => Change(g, "truss depth", () => g.BeamDepth = v));

                if (!bare)
                {
                    bool sizeDefault = float.IsNaN(g.SignalSizeOverride);
                    Slider(helper, "Signal box size", 0.6f, 2f, 0.05f, g.SignalSize, " m", !sizeDefault,
                        v => Change(g, "signal size", () => g.SignalSizeOverride = v));
                    Checkbox(helper, "Use default signal box size (" + s.SignalSize.ToString("0.00") + " m)", sizeDefault, on =>
                    {
                        Change(g, "signal size", () => g.SignalSizeOverride = on ? float.NaN : s.SignalSize);
                        Rebuild();
                    });
                }

                if (bare)
                {
                    Chooser(g);
                }
                else
                {
                    ContentControls(helper, g);
                }

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

                // Stack the rows ourselves so the panel's height never depends on when auto layout runs.
                float bottom = 0f;
                foreach (UIComponent child in _content.components)
                {
                    child.relativePosition = new Vector3(0f, bottom);
                    bottom += child.height + 4f;
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

        /// <summary>A little picture of each kind of gantry to pick from.</summary>
        private void TypeButtons(Gantry g)
        {
            const float Height = 56f, Gap = 4f;
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, Height + 2f);
            float w = (_content.width - Gap * (GantryShape.Count - 1)) / GantryShape.Count;
            for (byte type = 0; type < GantryShape.Count; type++)
            {
                byte chosen = type;
                var b = row.AddUIComponent<UIButton>();
                b.size = new Vector2(w, Height);
                b.relativePosition = new Vector3(type * (w + Gap), 0f);
                b.normalBgSprite = g.Type == type ? "ButtonMenuPressed" : "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.tooltip = GantryShape.Names[type];
                b.eventClick += (c, p) =>
                {
                    Change(g, "gantry type", () => g.Type = chosen);
                    Rebuild();
                };

                // Beam across, posts below it, seen as drivers see it.
                float x0 = 12f, x1 = w - 12f, beamY = 10f, ground = 40f;
                TypeBlock(b, x0, beamY, x1 - x0, 5f);
                if (GantryShape.PostAtStart(type))
                {
                    TypeBlock(b, x0, beamY, 4f, ground - beamY);
                }

                if (GantryShape.PostAtEnd(type))
                {
                    TypeBlock(b, x1 - 4f, beamY, 4f, ground - beamY);
                }

                if (type == GantryShape.MedianT)
                {
                    TypeBlock(b, (x0 + x1) * 0.5f - 2f, beamY, 4f, ground - beamY);
                }

                UILabel label = UIUtils.CreateLabel(b, type == GantryShape.CantileverEnd ? "Cantilever 2" : GantryShape.Names[type], 0.6f);
                label.isInteractive = false;
                label.relativePosition = new Vector3(5f, Height - 14f);
            }
        }

        /// <summary>What the structure is built from.</summary>
        private void LookButtons(Gantry g)
        {
            const float Gap = 4f;
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, 30f);
            float w = (_content.width - Gap * (GantryShape.LookCount - 1)) / GantryShape.LookCount;
            for (byte look = 0; look < GantryShape.LookCount; look++)
            {
                byte chosen = look;
                var b = row.AddUIComponent<UIButton>();
                b.text = GantryShape.LookNames[look];
                b.textScale = 0.65f;
                b.size = new Vector2(w, 28f);
                b.relativePosition = new Vector3(look * (w + Gap), 0f);
                b.normalBgSprite = g.Look == look ? "ButtonMenuPressed" : "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.eventClick += (c, p) =>
                {
                    Change(g, "gantry look", () => g.Look = chosen);
                    Rebuild();
                };
            }
        }

        private static void TypeBlock(UIComponent parent, float x, float y, float w, float h)
        {
            var s = parent.AddUIComponent<UISprite>();
            s.spriteName = "EmptySprite";
            s.color = new Color32(225, 225, 215, 255);
            s.isInteractive = false;
            s.size = new Vector2(w, h);
            s.relativePosition = new Vector3(x, y);
        }

        /// <summary>What goes on a gantry that has nothing on it yet. Both can go on later, from the normal controls.</summary>
        private void Chooser(Gantry g)
        {
            var box = _content.AddUIComponent<UIPanel>();
            box.size = new Vector2(_content.width, 92f);
            UIUtils.CreateLabel(box, "What goes on this gantry?", 0.9f).relativePosition = Vector3.zero;
            float half = (_content.width - 6f) / 2f;
            ChoiceButton(box, 0f, half, "Lane signals", "Signals over each lane, set up automatically from the road", () =>
            {
                Change(g, "lane signals", () => g.LaneHeads = true);
                Rebuild();
            });
            ChoiceButton(box, half + 6f, half, "Guide signs", "A big highway sign, which you design", () => AddGuideSign(g));
            UILabel hint = UIUtils.CreateLabel(box, "You can add the other later, or leave it empty for now.", 0.65f);
            hint.relativePosition = new Vector3(0f, 74f);
            PasteRow(g, true);
        }

        /// <summary>Puts the sign that was copied on this gantry as a new one, if there is one and there's room.</summary>
        private void PasteRow(Gantry g, bool canAdd)
        {
            if (!GantryTool.Guide.GuideClipboard.HasSign)
            {
                return;
            }

            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, 34f);
            RowButton(row, 0, _content.width, "Paste guide sign",
                canAdd ? "Add the sign you copied to this gantry" : "This gantry has the most guide signs allowed", () =>
                {
                    Gantry cur = GantryManager.Find(_gantryId);
                    if (cur != null)
                    {
                        AddGuideSign(cur, true);
                    }
                });
            row.components[0].isEnabled = canAdd;
        }

        private static void ChoiceButton(UIPanel box, float x, float w, string text, string tooltip, Action onClick)
        {
            var b = box.AddUIComponent<UIButton>();
            b.text = text;
            b.tooltip = tooltip;
            b.textScale = 1f;
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.size = new Vector2(w, 46f);
            b.relativePosition = new Vector3(x, 24f);
            b.eventClick += (c, p) => onClick();
        }

        /// <summary>Puts a new guide sign on a gantry, over the lanes still free on the side the camera sees, and opens it.</summary>
        public void AddGuideSign(Gantry g, bool pasted = false)
        {
            if (Tool == null || !GantryManager.CanAddGuideSign(g))
            {
                return;
            }

            sbyte facing = SignalPanel.CameraSeesMinusFace(g) ? (sbyte)-1 : (sbyte)1;
            byte first, last;
            lock (GantryManager.Lock)
            {
                GantryTool.Guide.GuideFit.DefaultRange(g.Lanes, g.GuideSigns, facing, out first, out last);
            }

            if (pasted)
            {
                var copy = GantryTool.Guide.GuideClipboard.PasteOnto(new GuideHead());
                int added = GantryManager.AddGuideSign(g, g.Span * 0.5f, facing, copy.Design, first, last);
                if (added >= 0)
                {
                    GantryManager.UpdateGuideSign(g, added, null, GantryTool.Guide.GuideClipboard.PasteOnto);
                    UIUtils.ClearTooltips(_content);
                    Tool.EditHead(g, added);
                }

                return;
            }

            Dictionary<string, object> template = GantryTool.Guide.GuideAssets.Template(GantryTool.Guide.GuideThemes.Current, GantryTool.Guide.GuideThemes.DefaultTemplate(GantryTool.Guide.GuideThemes.Current));
            if (Settings.Current.AutoFillSigns)
            {
                var placing = new GuideHead { Along = g.Span * 0.5f, Side = facing, FitFirst = first, FitLast = last };
                bool partly;
                GantryTool.Detect.RoadFinder.Fill(g, placing, template, out partly);
            }

            string design = GantryTool.Guide.Json.Write(template);
            int head = GantryManager.AddGuideSign(g, g.Span * 0.5f, facing, design, first, last);
            if (head >= 0)
            {
                UIUtils.ClearTooltips(_content);
                Tool.EditHead(g, head);
            }
        }

        /// <summary>The controls for what's on the gantry.</summary>
        private void ContentControls(UIHelper helper, Gantry g)
        {
            // Extra signal heads, placed anywhere on the beam.
            int extras;
            lock (GantryManager.Lock)
            {
                extras = g.ExtraHeads.Count;
            }

            // With no signals at all, there are no backs to blank: added signs count as signals here.
            UIComponent backs = null;
            int extraCount = extras;
            Action<bool> showBacks = signals =>
            {
                backs.isEnabled = signals || extraCount > 0;
                backs.opacity = backs.isEnabled ? 1f : 0.4f;
            };
            Checkbox(helper, "Lane signals (off: a signs-only gantry)", g.LaneHeads, on =>
            {
                Change(g, "lane signals", () => g.LaneHeads = on);
                showBacks(on);
                Rebuild(); // with nothing left on it, the gantry goes back to the chooser
            });
            backs = (UIComponent)helper.AddCheckbox("No red Xs on the back of signals", g.BlankBacks, on =>
            {
                Change(g, "signal backs", () => g.BlankBacks = on);
            });
            showBacks(g.LaneHeads);

            var addRow = _content.AddUIComponent<UIPanel>();
            addRow.size = new Vector2(_content.width, 34f);
            bool canAdd = extras < HeadRef.MaxExtraHeads;
            float halfRow = (_content.width - 6f) / 2f;
            RowButton(addRow, 0, halfRow, "Add signal (" + extras + " of " + HeadRef.MaxExtraHeads + ")",
                canAdd ? "Add a signal to this gantry: drag it anywhere on the beam, or link it to a lane"
                       : "This gantry has the most added signals allowed", () =>
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

            int guides;
            lock (GantryManager.Lock)
            {
                guides = g.GuideSigns.Count;
            }

            bool canGuide = guides < GuideData.MaxSigns;
            RowButton(addRow, 1, halfRow, "Add guide sign (" + guides + " of " + GuideData.MaxSigns + ")",
                canGuide ? "Add a big printed highway sign to this gantry" : "This gantry has the most guide signs allowed", () =>
                {
                    Gantry cur = GantryManager.Find(_gantryId);
                    if (cur != null)
                    {
                        AddGuideSign(cur);
                    }
                });
            addRow.components[1].isEnabled = canGuide;
            PasteRow(g, canGuide);
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

            var drop = (UIDropDown)helper.AddDropdown("Signal style", items, selected, index =>
            {
                byte style = index <= 0 || index > styles.Length ? (byte)SignStyle.Default : (byte)styles[index - 1];
                Change(g, "signal style", () =>
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
