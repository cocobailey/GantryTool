using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Detect;
using GantryTool.Guide;
using GantryTool.Render;
using GantryTool.Tool;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Editor for a guide sign on a gantry, opened by clicking the sign with the gantry tool: which sign, which way it
    /// faces, its position and size. It hides itself as soon as the sign is no longer the selected head.
    /// </summary>
    public sealed class GuidePanel : UIPanel
    {
        private const float PanelWidth = 420f;
        private const float Margin = 12f;
        private const string Theme = "us";

        public GantryPlaceTool Tool;

        private UILabel _title;
        private UIPanel _content;
        private int _gantryId;
        private int _head;
        private bool _building;

        public static GuidePanel Create()
        {
            return (GuidePanel)UIView.GetAView().AddUIComponent(typeof(GuidePanel));
        }

        public override void Awake()
        {
            base.Awake();
            name = "GantryToolGuidePanel";
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;
            height = 300f;
            isVisible = false;

            _title = UIUtils.CreateLabel(this, "Guide sign", 1.0f);
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
                    Tool.ClearHeadSelection();
                }
            };

            _content = AddUIComponent<UIPanel>();
            _content.relativePosition = new Vector3(Margin, 44f);
            _content.width = PanelWidth - 2f * Margin;
        }

        public override void Start()
        {
            base.Start();
            relativePosition = new Vector3(120f, 140f);
        }

        public override void Update()
        {
            base.Update();
            if (isVisible && (Tool == null || !Tool.enabled || Tool.SelectedHeadGantry != _gantryId || Tool.SelectedHead != _head))
            {
                Hide();
            }
        }

        public void ShowFor(Gantry g, int head)
        {
            try
            {
                _building = true;
                _gantryId = g.Id;
                _head = head;
                _title.text = "Guide sign on gantry #" + g.Id;
                Rebuild(g);
                Show();
                BringToFront();
            }
            catch (Exception e)
            {
                Log.Error("GuidePanel.ShowFor failed", e);
            }
            finally
            {
                _building = false;
            }
        }

        private void Rebuild(Gantry g)
        {
            for (int i = _content.components.Count - 1; i >= 0; i--)
            {
                UIComponent old = _content.components[i];
                _content.RemoveUIComponent(old);
                UnityEngine.Object.Destroy(old.gameObject);
            }

            GuideHead sign;
            if (!GantryManager.GetGuideSign(g, _head, out sign))
            {
                return;
            }

            var helper = new UIHelper(_content);
            TemplateDropdown(helper, g, sign);
            FacingDropdown(helper, g, sign);

            UIUtils.StyledSlider(helper, _content.width, "Position along beam", 0f, Mathf.Max(0.5f, g.Span), 0.05f, sign.Along, " m", "0.00", true, v =>
            {
                Change(g, "sign position", h =>
                {
                    h.Along = v;
                    return h;
                });
            });
            UIUtils.StyledSlider(helper, _content.width, "Sign size", GuideData.MinScale * 100f, GuideData.MaxScale * 100f, 5f, sign.Scale * 100f, " %", "0", true, v =>
            {
                Change(g, "sign size", h =>
                {
                    h.Scale = v / 100f;
                    return h;
                });
            });

            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, 34f);
            float half = (_content.width - 6f) / 2f;
            RowButton(row, 0, half, "Undo", "Undo the last change (Ctrl+Z)", () =>
            {
                if (Tool != null)
                {
                    Tool.UndoLast();
                }
            });
            RowButton(row, 1, half, "Delete sign", "Delete this guide sign (Ctrl+Z brings it back)", () =>
            {
                Gantry cur = GantryManager.Find(_gantryId);
                if (cur != null)
                {
                    GantryManager.RemoveGuideSign(cur, _head);
                }

                Hide();
                if (Tool != null)
                {
                    Tool.ClearHeadSelection();
                }
            });

            float bottom = 0f;
            foreach (UIComponent child in _content.components)
            {
                child.relativePosition = new Vector3(0f, bottom);
                bottom += child.height + 4f;
            }

            _content.height = bottom;
            height = _content.relativePosition.y + bottom + 12f;
        }

        private void TemplateDropdown(UIHelper helper, Gantry g, GuideHead sign)
        {
            List<string> names = GuideAssets.TemplateNames(Theme);
            var labels = new string[names.Count];
            string current = null;
            try
            {
                current = Json.Str(Json.Parse(sign.Design) as Dictionary<string, object>, "id");
            }
            catch (FormatException)
            {
            }

            int selected = 0;
            for (int i = 0; i < names.Count; i++)
            {
                Dictionary<string, object> t = GuideAssets.Template(Theme, names[i]);
                labels[i] = Json.Str(t, "name", names[i]);
                if (Json.Str(t, "id") == current)
                {
                    selected = i;
                }
            }

            var drop = (UIDropDown)helper.AddDropdown("Sign", labels, selected, index =>
            {
                if (index < 0 || index >= names.Count)
                {
                    return;
                }

                string design = Json.Write(GuideAssets.Template(Theme, names[index]));
                Change(g, "guide sign", h =>
                {
                    h.Design = design;
                    return h;
                });
            });
            Fit(drop);
        }

        private void FacingDropdown(UIHelper helper, Gantry g, GuideHead sign)
        {
            Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z).normalized;
            Vector2 side = new Vector2(-d.y, d.x);
            string[] labels =
            {
                "Faces " + LaneScanner.Compass(-side) + " (" + LaneScanner.Compass(side) + "-bound traffic)",
                "Faces " + LaneScanner.Compass(side) + " (" + LaneScanner.Compass(-side) + "-bound traffic)"
            };
            var drop = (UIDropDown)helper.AddDropdown("Facing", labels, sign.Side < 0 ? 0 : 1, index =>
            {
                Change(g, "sign facing", h =>
                {
                    h.Side = index == 0 ? (sbyte)-1 : (sbyte)1;
                    return h;
                });
            });
            Fit(drop);
        }

        private void Fit(UIDropDown drop)
        {
            UIComponent row = drop.parent;
            row.width = _content.width;
            drop.width = _content.width - 16f;
            DropdownToggle.Attach(drop);
        }

        private void Change(Gantry g, string what, Func<GuideHead, GuideHead> change)
        {
            if (_building)
            {
                return;
            }

            try
            {
                GantryManager.UpdateGuideSign(g, _head, what, change);
            }
            catch (Exception e)
            {
                Log.Error("Guide sign change failed", e);
            }
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
