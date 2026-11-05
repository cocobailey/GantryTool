using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.IO;
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
    /// Editor for a guide sign on a gantry, opened by clicking the sign with the gantry tool. A live picture of the sign
    /// stays on top, with two pages under it. On Design you click a part of the picture and change just that part; Placement
    /// says where the sign goes. It hides itself as soon as the sign is no longer the selected head.
    /// </summary>
    public sealed class GuidePanel : UIPanel
    {
        private const float PanelWidth = 680f;
        private const float Margin = 12f;
        private const float PreviewHeight = 210f;
        private const float MaxPickerHeight = 370f;
        private const float InspectorHeight = 178f;
        private const float ControlsY = 52f;
        private static readonly string[] SwatchColours = { "#ffffff", "#000000", "#ffb000", "#006b45", "#1a5ca8", "#c8102e", "#6b3e1e", "#f47920" };
        private const float MinPartSize = 10f;
        private const float DragStart = 6f;
        private const float RowHeight = 34f;
        private const string CopyLabel = "Copy sign";
        private const float CopiedShowsFor = 1f;
        private const float SliderSlack = 0.06f;   // a slider moves in steps of 0.1 m
        private const float MaxSignWidth = 12f, MaxSignHeight = 6f;
        private const float TextCommitDelay = 0.05f;   // just enough to take in keys pressed together
        private string Theme   // the country of the sign being edited
        {
            get { return Json.Str(_doc, "theme", GuideThemes.Current); }
        }


        private static readonly string[] ArrowKeys =
        {
            "down", "up", "left", "right", "down-left", "down-right", "up-left", "up-right", "turn-left", "turn-right", "split-left", "split-right"
        };

        private static readonly string[] ArrowLabels =
        {
            "Down", "Up", "Left", "Right", "Down left", "Down right", "Up left", "Up right", "Turn left", "Turn right", "Split left", "Split right"
        };


        public GantryPlaceTool Tool;

        private UILabel _title;
        private UIPanel _content;
        private int _gantryId;
        private int _head;
        private bool _building;
        private bool _rebuild;
        private float _commitAt = -1f;
        private UIButton _copyButton, _pasteButton;
        private float _copiedUntil = -1f;
        private GuideHead _shown;
        private UISlider _widthSlider, _heightSlider;
        private Dictionary<string, object> _doc;
        private bool _placementPage;
        private bool _picking;
        private UITextureSprite _preview;
        private GuideCacheEntry _entry;
        private float _pictureScale;
        private readonly UISprite[] _selectBox = new UISprite[4];
        private readonly UISprite[] _hoverBox = new UISprite[4];
        private Dictionary<string, object> _selected;   // the part being edited, in _doc
        private string _selectedPath;
        private UITextField _focus;
        private Dictionary<string, object> _pressed;    // the part the mouse went down on, while it's held
        private Vector2 _pressedAt;
        private bool _dragging, _dragged;
        private readonly UISprite[] _dropLine = new UISprite[1];
        private bool _saving;
        private string _copyNote;
        private string _packNote;
        private string _signSearch = "";
        private UIButton _redoButton;
        private bool _choosingPicture;
        private string _pictureNote;
        private bool _changingPicture;                       // the next picture replaces the selected part's; otherwise it's added
        private bool _coverMode;                             // the next picture covers the whole sign
        private string _notice;

        private static string MySignsFolder => Path.Combine(Path.Combine(DataLocation.localApplicationData, "GantryTool"), "GuideSigns");

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
            relativePosition = new Vector3(120f, 90f);
        }

        public override void Update()
        {
            base.Update();
            UIUtils.KeepTooltipHidden(this);
            if (!isVisible)
            {
                return;
            }

            if (Tool == null || !Tool.enabled || Tool.SelectedHeadGantry != _gantryId || Tool.SelectedHead != _head)
            {
                CommitText();
                Hide();
                return;
            }

            if (_commitAt >= 0f && Time.realtimeSinceStartup >= _commitAt)
            {
                CommitText();
            }

            // A change from elsewhere (Ctrl+Z) or one that changes which controls show.
            Gantry g = GantryManager.Find(_gantryId);
            GuideHead now;
            if (g == null || !GantryManager.GetGuideSign(g, _head, out now))
            {
                return;
            }

            // Never while the mouse is held: the button being clicked would be gone before the click finished.
            if (_commitAt < 0f && (_rebuild || Differs(now, _shown)) && !Input.GetMouseButton(0))
            {
                _rebuild = false;
                Show(g);
            }

            CopyPasteKeys(g);
            GuideRoadWatch.PausedUntil = Time.realtimeSinceStartup + 1f;
            LiveSigns.Editing(g.Id, HeadRef.GuideId(_head));
            if (_redoButton != null)
            {
                _redoButton.isEnabled = UndoStack.RedoCount > 0;
            }

            if (_copiedUntil >= 0f && Time.realtimeSinceStartup >= _copiedUntil)
            {
                _copiedUntil = -1f;
                _copyButton.text = CopyLabel;
            }

            DragPart(g);
            UpdatePreview(g, now);
            FollowSize(g, now);
        }

        /// <summary>Ctrl+C and Ctrl+V, unless they're for the text being typed.</summary>
        private void CopyPasteKeys(Gantry g)
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) || UIView.HasInputFocus())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                CopySign(g);
            }
            else if (Input.GetKeyDown(KeyCode.V))
            {
                PasteSign(g);
            }
        }

        private void CopySign(Gantry g)
        {
            CommitText();
            GuideHead sign;
            if (GantryManager.GetGuideSign(g, _head, out sign))
            {
                GuideClipboard.Copy(sign);
                _pasteButton.isEnabled = true;
                _copyButton.text = "Copied";
                _copiedUntil = Time.realtimeSinceStartup + CopiedShowsFor;
            }
        }

        private void PasteSign(Gantry g)
        {
            if (GuideClipboard.HasSign)
            {
                CommitText();
                _selected = null;
                _selectedPath = null;
                _saving = false;
                _choosingPicture = false;
                _picking = false;
                Change(g, "paste sign", GuideClipboard.PasteOnto);
                _rebuild = true;
            }
        }

        /// <summary>Keeps the size sliders true when something else resizes the sign, like dragging it over more lanes.</summary>
        private void FollowSize(Gantry g, GuideHead sign)
        {
            if (_widthSlider == null || (Input.GetMouseButton(0) && (_widthSlider.containsMouse || _heightSlider.containsMouse)))
            {
                return;
            }

            GuidePlacement placed = GuideMeshBuilder.Place(g, sign);
            _building = true;
            if (Mathf.Abs(_widthSlider.value - placed.WidthMetres) > SliderSlack)
            {
                _widthSlider.value = placed.WidthMetres;
            }

            if (Mathf.Abs(_heightSlider.value - placed.PanelMetres) > SliderSlack)
            {
                _heightSlider.value = placed.PanelMetres;
            }

            _building = false;
        }

        /// <summary>Shows the sign as it stands now, as big as fits, keeping its shape.</summary>
        private void UpdatePreview(Gantry g, GuideHead sign)
        {
            if (_preview == null)
            {
                return;
            }

            GuideCacheEntry e = GuideCache.Get(GuideMeshBuilder.Place(g, sign).Design);
            if (e == null || e == _entry && e.Texture == _preview.texture)
            {
                return;
            }

            _entry = e;
            _preview.texture = e.Texture;
            FitPicture(_preview, e.Texture, _content.width - 8f, PreviewHeight - 8f, 4f, 4f);
            _pictureScale = _preview.width / e.Width;
            PlaceBox(_selectBox, FindPart(_selectedPath));
        }

        // ------------------------------------------------------------------------------ clicking on the picture

        private GuidePart? FindPart(string path)
        {
            if (_entry != null && _entry.Parts != null && path != null)
            {
                foreach (GuidePart p in _entry.Parts)
                {
                    if (p.Path == path)
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        // Dragging: a part held and moved a little starts to drag. A line shows where it would snap to, and letting go moves it.

        private void PressPicture(UIMouseEventParameter p)
        {
            _pressed = null;
            _dragging = false;
            _dragged = false;
            if ((p.buttons & UIMouseButton.Left) == 0 || _doc == null)
            {
                return;
            }

            GuidePart? part = PartUnder(p.ray);
            int laneRow = _shown.LaneArrows && _shown.FitFirst > 0 ? GuideFit.LaneRowIndex(_doc) : -1;
            Dictionary<string, object> movable = part.HasValue ? GuideDrag.Movable(_doc, part.Value.Path, laneRow) : null;
            float x, y;
            if (movable != null && PointerOnSign(p.ray, out x, out y))
            {
                _pressed = movable;
                _pressedAt = new Vector2(x, y);
            }
        }

        private void DragPart(Gantry g)
        {
            if (_pressed == null || _preview == null)
            {
                return;
            }

            Ray ray = GetUIView().uiCamera.ScreenPointToRay(Input.mousePosition);
            float x, y;
            bool over = PointerOnSign(ray, out x, out y);
            GuideDrop drop = default(GuideDrop);
            bool canDrop = false;
            if (over)
            {
                if (!_dragging && (new Vector2(x, y) - _pressedAt).magnitude * _pictureScale > DragStart)
                {
                    _dragging = true;
                    _dragged = true;
                    CommitText();
                }

                canDrop = _dragging && _entry != null && _entry.Parts != null && GuideDrag.Find(_doc, _entry.Parts, _pressed, x, y, out drop);
            }

            if (Input.GetMouseButton(0))
            {
                PlaceDropLine(canDrop, drop);
                return;
            }

            Dictionary<string, object> moving = _pressed;
            _pressed = null;
            _dragging = false;
            PlaceDropLine(false, drop);
            if (canDrop && GuideDrag.Apply(_doc, moving, drop))
            {
                Prune(_doc);
                _selected = moving;
                _selectedPath = null;
                Commit(g, "sign content");
                _rebuild = true;
            }
        }

        private void PlaceDropLine(bool show, GuideDrop drop)
        {
            UISprite line = _dropLine[0];
            if (line == null)
            {
                return;
            }

            line.isVisible = show;
            if (!show)
            {
                return;
            }

            const float Thick = 3f;
            Vector3 origin = _preview.relativePosition;
            float x = origin.x + drop.X * _pictureScale, y = origin.y + drop.Y * _pictureScale;
            float w = drop.W * _pictureScale, h = drop.H * _pictureScale;
            switch (drop.Side)
            {
                case DropSide.Above:
                    line.size = new Vector2(w, Thick);
                    line.relativePosition = new Vector3(x, y - Thick - 1f);
                    break;
                case DropSide.Below:
                    line.size = new Vector2(w, Thick);
                    line.relativePosition = new Vector3(x, y + h + 1f);
                    break;
                case DropSide.Left:
                    line.size = new Vector2(Thick, h);
                    line.relativePosition = new Vector3(x - Thick - 1f, y);
                    break;
                default:
                    line.size = new Vector2(Thick, h);
                    line.relativePosition = new Vector3(x + w + 1f, y);
                    break;
            }
        }

        private bool PointerOnSign(Ray ray, out float x, out float y)
        {
            x = y = 0f;
            Vector2 at;
            if (_entry == null || _pictureScale <= 0f || !_preview.GetHitPosition(ray, out at))
            {
                return false;
            }

            x = at.x / _pictureScale;
            y = at.y / _pictureScale;
            return true;
        }

        /// <summary>The smallest part under a point on the picture, so a word wins over the band it sits in.</summary>
        private GuidePart? PartUnder(Ray ray)
        {
            float x, y, best = float.MaxValue;
            if (_entry == null || _entry.Parts == null || !PointerOnSign(ray, out x, out y))
            {
                return null;
            }

            GuidePart? found = null;
            foreach (GuidePart p in _entry.Parts)
            {
                float w = Mathf.Max(p.W, MinPartSize), h = Mathf.Max(p.H, MinPartSize);
                float x0 = p.X + (p.W - w) * 0.5f, y0 = p.Y + (p.H - h) * 0.5f;
                if (x >= x0 && x <= x0 + w && y >= y0 && y <= y0 + h && w * h < best)
                {
                    best = w * h;
                    found = p;
                }
            }

            return found;
        }

        private void ClickPicture(Ray ray)
        {
            if (_dragged)
            {
                _dragged = false;
                return;
            }

            CommitText();
            GuidePart? part = PartUnder(ray);
            _saving = false;
            _choosingPicture = false;
            _notice = null;
            _selectedPath = part.HasValue ? part.Value.Path : null;
            _selected = _selectedPath != null ? ItemAt(_doc, _selectedPath) : null;
            _rebuild = true;
        }

        private void AddBox(UIPanel box, UISprite[] sprites, Color32 colour)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                UISprite s = box.AddUIComponent<UISprite>();
                s.spriteName = "EmptySprite";
                s.color = colour;
                s.isInteractive = false;
                s.isVisible = false;
                sprites[i] = s;
            }
        }

        /// <summary>Draws a box around a part on the picture, or hides it.</summary>
        private void PlaceBox(UISprite[] sprites, GuidePart? part)
        {
            if (sprites[0] == null)
            {
                return;
            }

            foreach (UISprite s in sprites)
            {
                s.isVisible = part.HasValue;
            }

            if (!part.HasValue)
            {
                return;
            }

            const float Line = 2f, Pad = 2f;
            GuidePart p = part.Value;
            Vector3 origin = _preview.relativePosition;
            float x = origin.x + p.X * _pictureScale - Pad, y = origin.y + p.Y * _pictureScale - Pad;
            float w = p.W * _pictureScale + 2f * Pad, h = p.H * _pictureScale + 2f * Pad;
            sprites[0].size = new Vector2(w, Line);
            sprites[0].relativePosition = new Vector3(x, y);
            sprites[1].size = new Vector2(w, Line);
            sprites[1].relativePosition = new Vector3(x, y + h - Line);
            sprites[2].size = new Vector2(Line, h);
            sprites[2].relativePosition = new Vector3(x, y);
            sprites[3].size = new Vector2(Line, h);
            sprites[3].relativePosition = new Vector3(x + w - Line, y);
        }

        private static void FitPicture(UITextureSprite sprite, Texture2D tex, float w, float h, float x, float y)
        {
            float scale = Mathf.Min(w / tex.width, h / tex.height);
            sprite.size = new Vector2(tex.width * scale, tex.height * scale);
            sprite.relativePosition = new Vector3(x + (w - sprite.width) * 0.5f, y + (h - sprite.height) * 0.5f);
        }

        private static bool Differs(GuideHead a, GuideHead b)
        {
            return a.Design != b.Design || a.Side != b.Side || a.Scale != b.Scale || a.FitFirst != b.FitFirst || a.FitLast != b.FitLast || a.LaneArrows != b.LaneArrows;
        }

        public void ShowFor(Gantry g, int head)
        {
            CommitText();
            _gantryId = g.Id;
            _head = head;
            _picking = false;
            _saving = false;
            _choosingPicture = false;
            _notice = null;
            _selected = null;
            _selectedPath = null;
            _title.text = "Guide sign on gantry #" + g.Id;
            Show(g);
            Show();
            BringToFront();
        }

        private void Show(Gantry g)
        {
            try
            {
                _building = true;
                _focus = null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Rebuild(g);
                Log.Debug("Guide sign panel built in " + watch.ElapsedMilliseconds + " ms");
                if (_focus != null)
                {
                    _focus.Focus();
                }
            }
            catch (Exception e)
            {
                Log.Error("GuidePanel failed to build", e);
            }
            finally
            {
                _building = false;
            }
        }

        private void Rebuild(Gantry g)
        {
            UIUtils.ClearTooltips(_content);
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

            // The part being edited is found again by where it is, as the design is read afresh.
            if (_selected != null)
            {
                _selectedPath = PathOf(_doc, _selected, "");
            }

            _shown = sign;
            _doc = ParseDesign(sign.Design);
            if (sign.LaneArrows && sign.FitFirst > 0)
            {
                lock (GantryManager.Lock)
                {
                    GuideFit.PadLaneRow(_doc, GuideFit.LaneCount(sign, g.Lanes));
                }
            }

            _selected = _selectedPath != null ? ItemAt(_doc, _selectedPath) : null;
            if (_selected == null)
            {
                _selectedPath = null;
            }

            _preview = null;
            _widthSlider = _heightSlider = null;
            if (!_choosingPicture)
            {
                ImageImport.ForgetPreviews();
            }

            PreviewBox(g, sign);
            PageButtons();
            if (_placementPage)
            {
                PlacementPage(g, sign);
            }
            else if (_picking)
            {
                SignPicker(g);
            }
            else if (_choosingPicture)
            {
                PicturePicker(g);
            }
            else
            {
                DesignPage(g);
            }

            Heading("This sign");
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, 34f);
            float fifth = (_content.width - 24f) / 5f;
            RowButton(row, 0f, fifth, "Undo", "Undo the last change (Ctrl+Z)", () =>
            {
                if (Tool != null)
                {
                    Tool.UndoLast();
                }
            });
            _redoButton = RowButton(row, fifth + 6f, fifth, "Redo", "Put back what you just undid (Ctrl+Y)", () =>
            {
                if (Tool != null)
                {
                    Tool.RedoLast();
                }
            });
            _redoButton.isEnabled = UndoStack.RedoCount > 0;
            _copyButton = RowButton(row, 2f * (fifth + 6f), fifth, CopyLabel, "Copy this sign, to paste on any gantry (Ctrl+C)", () => CopySign(g));
            _pasteButton = RowButton(row, 3f * (fifth + 6f), fifth, "Paste sign", "Replace this sign with the one you copied; it stays where it is (Ctrl+V, and Ctrl+Z brings this one back)", () => PasteSign(g));
            _pasteButton.isEnabled = GuideClipboard.HasSign;
            RowButton(row, 4f * (fifth + 6f), fifth, "Delete sign", "Delete this guide sign (Ctrl+Z brings it back)", () =>
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

        private static Dictionary<string, object> ParseDesign(string design)
        {
            try
            {
                var doc = Json.Parse(design) as Dictionary<string, object>;
                if (doc != null)
                {
                    return doc;
                }
            }
            catch (FormatException)
            {
            }

            return new Dictionary<string, object> { { "theme", GuideThemes.Current } };
        }

        // ------------------------------------------------------------------------------ the sign, its facing and lanes

        private void PreviewBox(Gantry g, GuideHead sign)
        {
            var box = _content.AddUIComponent<UIPanel>();
            box.size = new Vector2(_content.width, PreviewHeight);
            box.backgroundSprite = "GenericPanel";
            box.color = new Color32(60, 66, 74, 255);
            _preview = box.AddUIComponent<UITextureSprite>();
            _entry = null;
            AddBox(box, _hoverBox, new Color32(255, 255, 255, 200));
            AddBox(box, _selectBox, new Color32(255, 210, 40, 255));
            AddBox(box, _dropLine, new Color32(255, 120, 30, 255));
            if (!_placementPage && !_picking && !_choosingPicture)
            {
                _preview.tooltip = "Click part of the sign to change it, or drag it to move it";
                _preview.eventMouseDown += (c, p) => PressPicture(p);
                _preview.eventMouseMove += (c, p) => PlaceBox(_hoverBox, PartUnder(p.ray));
                _preview.eventMouseLeave += (c, p) => PlaceBox(_hoverBox, null);
                _preview.eventClick += (c, p) => ClickPicture(p.ray);
            }
            else
            {
                _preview.isInteractive = false;
            }

            UpdatePreview(g, sign);
        }

        private void PageButtons()
        {
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            float half = (_content.width - 6f) / 2f;
            PageButton(row, 0f, half, "Design", "Which sign this is, and what it says", !_placementPage, false);
            PageButton(row, half + 6f, half, "Placement", "Which way it faces, the lanes it covers and its size", _placementPage, true);
        }

        /// <summary>A small title over a group of buttons.</summary>
        private void Heading(string text)
        {
            var head = _content.AddUIComponent<UIPanel>();
            head.size = new Vector2(_content.width, 16f);
            UIUtils.CreateLabel(head, text, 0.75f).relativePosition = new Vector3(2f, 2f);
        }

        private void PageButton(UIPanel row, float x, float w, string text, string tooltip, bool current, bool placement)
        {
            RowButton(row, x, w, text, tooltip, () =>
            {
                CommitText();
                _placementPage = placement;
                _copyNote = null;
                _picking = false;
                _saving = false;
                _choosingPicture = false;
                _rebuild = true;
            });
            var b = (UIButton)row.components[row.components.Count - 1];
            b.textScale = 0.85f;
            if (current)
            {
                b.normalBgSprite = "ButtonMenuPressed";
                b.hoveredBgSprite = "ButtonMenuPressed";
            }
        }

        /// <summary>A short welcome the first time, until it's dismissed.</summary>
        private void FirstTip()
        {
            if (Settings.Current.GuideTipSeen)
            {
                return;
            }

            var tip = _content.AddUIComponent<UIPanel>();
            tip.size = new Vector2(_content.width, 48f);
            const float Button = 70f;
            UILabel text = UIUtils.CreateLabel(tip, "Click any part of the sign to change it, or drag it to move it. Add parts with the buttons below. Placement sets its size and lanes.", 0.8f);
            text.autoSize = false;
            text.wordWrap = true;
            text.size = new Vector2(_content.width - Button - 8f, 44f);
            text.relativePosition = new Vector3(2f, 2f);
            RowButton(tip, _content.width - Button, Button, "Got it", "Don't show this again", () =>
            {
                Settings.Current.GuideTipSeen = true;
                Settings.Current.Save();
                _rebuild = true;
            });
        }

        /// <summary>A variable message sign's messages: which one you're editing, and how the sign changes between them.</summary>
        private void MessageControls(Gantry g)
        {
            if (!Json.Has(_doc, GuideRenderer.MessageSignKey))
            {
                return;
            }

            Heading("Messages: the sign changes between them by itself");
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, 40f);
            int count = GuidePages.Count(_doc), current = GuidePages.Current(_doc);
            const float Number = 30f, Gap = 4f;
            float x = 0f;
            for (int i = 0; i < count; i++)
            {
                int page = i;
                UIButton b = RowButton(row, x, Number, (i + 1).ToString(), "Edit message " + (i + 1), () =>
                {
                    CommitText();
                    GuidePages.SwitchTo(_doc, page);
                    ChangedMessages(g);
                });
                if (i == current)
                {
                    b.normalBgSprite = "ButtonMenuPressed";
                }

                x += Number + Gap;
            }

            UIButton add = RowButton(row, x, 52f, "+ Add", "Add another message, up to " + GuidePages.MaxPages, () =>
            {
                CommitText();
                Dictionary<string, object> line = GuidePages.Add(_doc);
                if (line != null)
                {
                    ChangedMessages(g);
                    _selected = line;
                }
            });
            add.isEnabled = count < GuidePages.MaxPages;
            x += 52f + Gap;
            UIButton remove = RowButton(row, x, 60f, "Remove", "Take this message off the sign (Ctrl+Z brings it back)", () =>
            {
                CommitText();
                GuidePages.RemoveCurrent(_doc);
                ChangedMessages(g);
            });
            remove.isEnabled = count > 1;
            float right = 5f * (Number + Gap) + 52f + 60f + 2f * Gap;
            float when = 120f, slider = _content.width - right - when - 12f;
            MiniSlider(row, right + 6f, 0f, slider, "Seconds each", GuidePages.MinSeconds, GuidePages.MaxSeconds, 1f, GuidePages.Seconds(_doc), v =>
            {
                _doc[GuidePages.SecondsKey] = (double)v;
                Commit(g, "message time");
            });
            bool busy = Json.Str(_doc, GuidePages.WhenKey) == GuidePages.WhenBusy;
            UIButton show = RowButton(row, _content.width - when, when, busy ? "In heavy traffic" : "Always on", "Always on, or blank unless the road below has heavy traffic", () =>
            {
                CommitText();
                if (busy)
                {
                    _doc.Remove(GuidePages.WhenKey);
                }
                else
                {
                    _doc[GuidePages.WhenKey] = GuidePages.WhenBusy;
                }

                ChangedMessages(g);
            });
            show.textScale = 0.7f;
            if (busy)
            {
                var heavy = _content.AddUIComponent<UIPanel>();
                heavy.size = new Vector2(_content.width, 40f);
                MiniSlider(heavy, 0f, 0f, _content.width * 0.6f, "Shows when the road is this full (%)", 5f, 100f, 5f, Json.Num(_doc, GuidePages.BusyAtKey, GuidePages.DefaultBusyAt), v =>
                {
                    _doc[GuidePages.BusyAtKey] = (double)v;
                    Commit(g, "message traffic");
                });
                UIUtils.CreateLabel(heavy, "Road below right now: " + g.LiveDensity + "%", 0.75f).relativePosition = new Vector3(_content.width * 0.6f + 12f, 14f);
            }
        }

        private void ChangedMessages(Gantry g)
        {
            _selected = null;
            _selectedPath = null;
            Commit(g, "sign messages");
            _rebuild = true;
        }

        /// <summary>Passing saved signs around as one file.</summary>
        private void PackControls()
        {
            float third = (_content.width - 12f) / 3f;
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            RowButton(row, 0f, third, "Export my signs", "Put all your saved signs and their pictures into one file to share", () =>
            {
                string path = MySigns.ExportPack(MySignsFolder);
                _packNote = path != null ? "Saved " + Path.GetFileName(path) + " in your signs folder." : "No saved signs to export yet.";
                _rebuild = true;
            });
            RowButton(row, third + 6f, third, "Import sign packs", "Add the signs from any " + MySigns.PackExtension + " files in your signs folder", () =>
            {
                int added = MySigns.ImportPacks(MySignsFolder);
                _packNote = added > 0 ? "Added " + added + " sign" + (added == 1 ? "" : "s") + "." : "No new signs found. Put " + MySigns.PackExtension + " files in the folder first.";
                _rebuild = true;
            });
            RowButton(row, 2f * (third + 6f), third, "Open signs folder", MySignsFolder, () =>
            {
                Directory.CreateDirectory(MySignsFolder);
                ColossalFramework.Utils.OpenInFileBrowser(MySignsFolder);
            });
            if (_packNote != null)
            {
                var note = _content.AddUIComponent<UIPanel>();
                note.size = new Vector2(_content.width, 20f);
                UIUtils.CreateLabel(note, _packNote, 0.75f).relativePosition = new Vector3(2f, 2f);
            }
        }

        private void DesignPage(Gantry g)
        {
            FirstTip();
            var top = _content.AddUIComponent<UIPanel>();
            top.size = new Vector2(_content.width, RowHeight);
            float third = (_content.width - 12f) / 3f;
            RowButton(top, 0f, third, "Choose a different sign...", "Pick a sign to start from; it replaces this one (Ctrl+Z brings it back)", () =>
            {
                CommitText();
                _picking = true;
                _saving = false;
                _choosingPicture = false;
                _rebuild = true;
            });
            RowButton(top, third + 6f, third, "Save as my sign...", "Keep this design to reuse on any gantry, in any city", () =>
            {
                CommitText();
                _saving = true;
                _notice = null;
                _rebuild = true;
            });
            if (!Json.Has(_doc, GuideRenderer.MessageSignKey))
            {
                SchemeDropdown(top, 2f * (third + 6f), third, g);
            }

            MessageControls(g);
            if (Json.Has(_doc, GuideAutoFill.RoadKey))
            {
                var fill = _content.AddUIComponent<UIPanel>();
                fill.size = new Vector2(_content.width, RowHeight);
                RowButton(fill, 0f, _content.width, "Fill in from the road", "Write in the road this sign is about, from the map; useful after turning the sign round or renaming a road (Ctrl+Z undoes it)", () => FillFromRoad(g));
            }

            if (_saving)
            {
                SavePrompt(g);
            }
            else
            {
                Inspector(g);
            }

            AddButtons(g);
        }

        private void FillFromRoad(Gantry g)
        {
            CommitText();
            bool exit = Json.Str(_doc, GuideAutoFill.RoadKey) == GuideAutoFill.RoadExit;
            _selected = null;
            _selectedPath = null;
            _saving = false;
            _choosingPicture = false;
            bool partly;
            if (RoadFinder.Fill(g, _shown, _doc, out partly))
            {
                _notice = partly ? "One side has no road to name, so that side wasn't changed." : null;
                Commit(g, "sign content");
            }
            else
            {
                _notice = exit ? "No exit found on the road ahead, so this sign wasn't changed." : "No road with a usable name found below this sign, so it wasn't changed.";
            }

            _rebuild = true;
        }

        /// <summary>Asks for a name in place of the part controls.</summary>
        private void SavePrompt(Gantry g)
        {
            var box = _content.AddUIComponent<UIPanel>();
            box.size = new Vector2(_content.width, InspectorHeight);
            UIUtils.CreateLabel(box, "Name for this sign (saving under a name you already used replaces that sign)", 0.8f).relativePosition = Vector3.zero;
            UITextField name = UIUtils.CreateTextField(box, _content.width, MySigns.MaxNameLength);
            name.relativePosition = new Vector3(0f, 18f);
            string id = Json.Str(_doc, "id", "");
            name.text = id.StartsWith(MySigns.IdPrefix, StringComparison.Ordinal) ? Json.Str(_doc, "name", "") : "";
            name.selectOnFocus = true;
            _focus = name;

            float half = (_content.width - 6f) / 2f;
            RowButton(box, 0f, half, "Save", "Save this sign", () =>
            {
                string clean = MySigns.CleanName(name.text);
                if (clean.Length == 0)
                {
                    name.Focus();
                    return;
                }

                bool saved = MySigns.Save(MySignsFolder, clean, _doc);
                GuideCache.ForgetThumbnail(MyKey(clean));
                _notice = saved
                    ? "Saved \"" + clean + "\". It's under My signs in Choose a different sign."
                    : "That sign couldn't be saved.";
                _saving = false;
                _choosingPicture = false;
                _rebuild = true;
            });
            RowButton(box, half + 6f, half, "Cancel", "Go back without saving", () =>
            {
                _saving = false;
                _choosingPicture = false;
                _rebuild = true;
            });
            box.components[box.components.Count - 1].relativePosition = new Vector3(half + 6f, InspectorHeight - 34f);
            box.components[box.components.Count - 2].relativePosition = new Vector3(0f, InspectorHeight - 34f);
        }

        private static string MyKey(string name)
        {
            return "my/" + name;
        }

        /// <summary>A picture of every sign to start from, the player's own first; clicking one puts it on the gantry.</summary>
        private void SignPicker(Gantry g)
        {
            CountryPicker();
            var searchRow = _content.AddUIComponent<UIPanel>();
            searchRow.size = new Vector2(_content.width, RowHeight);
            UIUtils.CreateLabel(searchRow, "Search", 0.85f).relativePosition = new Vector3(0f, 8f);
            UITextField search = UIUtils.CreateTextField(searchRow, 260f, 30);
            search.relativePosition = new Vector3(90f, 2f);
            search.tooltip = "Type part of a sign's name";
            UIPanel box;
            float width;
            UIScrollablePanel scroll = PickerScroll(out box, out width);
            _signSearch = "";
            float wanted = FillSignPicker(g, scroll, width);
            UIScrollbar bar = FinishPickerScroll(box, scroll, wanted);
            search.eventTextChanged += (c, v) =>
            {
                if (_building)
                {
                    return;
                }

                _signSearch = v.Trim();
                foreach (UIComponent old in new List<UIComponent>(scroll.components))
                {
                    scroll.RemoveUIComponent(old);
                    UnityEngine.Object.Destroy(old.gameObject);
                }

                float found = FillSignPicker(g, scroll, width);
                scroll.scrollPosition = Vector2.zero;
                bar.isVisible = found > scroll.height;
            };

            PackControls();
            var back = _content.AddUIComponent<UIPanel>();
            back.size = new Vector2(_content.width, RowHeight);
            RowButton(back, 0f, _content.width, "Keep the current sign", "Go back without changing the sign", () =>
            {
                _picking = false;
                _rebuild = true;
            });
        }

        /// <summary>The signs to pick from that match what's been searched for; returns the height they take.</summary>
        private float FillSignPicker(Gantry g, UIScrollablePanel scroll, float width)
        {
            float wanted = 0f;
            List<MySign> mine = MySigns.List(MySignsFolder);
            mine.RemoveAll(m => !MatchesSearch(m.Name));
            if (mine.Count > 0)
            {
                var cells = new List<PickerCell>();
                foreach (MySign sign in mine)
                {
                    MySign my = sign;
                    cells.Add(new PickerCell
                    {
                        Label = my.Name,
                        Id = my.Id,
                        Picture = GuideCache.Thumbnail(MyKey(my.Name), () => my.Design),
                        Use = () => UseDesign(g, my.Design),
                        Delete = () => AskDelete(my.Name)
                    });
                }

                wanted += PickerSection(scroll, "My signs", cells, width);
            }

            var builtIn = new List<PickerCell>();
            string country = GuideThemes.Current;
            foreach (string name in GuideAssets.TemplateNames(country))
            {
                string file = name;
                Dictionary<string, object> template = GuideAssets.Template(country, file);
                if (!MatchesSearch(Json.Str(template, "name", file)))
                {
                    continue;
                }

                builtIn.Add(new PickerCell
                {
                    Label = Json.Str(template, "name", file),
                    Id = Json.Str(template, "id"),
                    Picture = GuideCache.Thumbnail(country, file),
                    Use = () => UseTemplate(g, file)
                });
            }

            if (builtIn.Count > 0)
            {
                wanted += PickerSection(scroll, "Ready-made signs", builtIn, width);
            }

            if (wanted <= 0f)
            {
                var none = scroll.AddUIComponent<UIPanel>();
                none.size = new Vector2(width, 24f);
                UIUtils.CreateLabel(none, "No signs match that search", 0.8f).relativePosition = new Vector3(0f, 4f);
                wanted = 24f;
            }

            return wanted;
        }

        private bool MatchesSearch(string name)
        {
            return _signSearch.Length == 0 || name.IndexOf(_signSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private UIScrollablePanel PickerScroll(out UIPanel box, out float width)
        {
            box = _content.AddUIComponent<UIPanel>();
            var scroll = box.AddUIComponent<UIScrollablePanel>();
            scroll.relativePosition = Vector3.zero;
            scroll.autoLayout = true;
            scroll.autoLayoutDirection = LayoutDirection.Vertical;
            scroll.autoLayoutPadding = new RectOffset(0, 0, 0, 4);
            scroll.clipChildren = true;
            scroll.scrollWheelDirection = UIOrientation.Vertical;
            scroll.builtinKeyNavigation = true;
            width = _content.width - 14f;
            scroll.width = width;
            return scroll;
        }

        private UIScrollbar FinishPickerScroll(UIPanel box, UIScrollablePanel scroll, float wanted)
        {
            float h = Mathf.Min(wanted, MaxPickerHeight);
            scroll.height = h;
            box.size = new Vector2(_content.width, h);
            var bar = box.AddUIComponent<UIScrollbar>();
            bar.orientation = UIOrientation.Vertical;
            bar.size = new Vector2(10f, h);
            bar.relativePosition = new Vector3(_content.width - 10f, 0f);
            bar.incrementAmount = 40f;
            var track = bar.AddUIComponent<UISlicedSprite>();
            track.spriteName = "ScrollbarTrack";
            track.size = bar.size;
            track.relativePosition = Vector3.zero;
            bar.trackObject = track;
            var thumb = track.AddUIComponent<UISlicedSprite>();
            thumb.spriteName = "ScrollbarThumb";
            thumb.width = bar.width;
            bar.thumbObject = thumb;
            scroll.verticalScrollbar = bar;
            bar.isVisible = wanted > h;
            return bar;
        }

        /// <summary>The pictures in the Signs folder; clicking one puts it on the sign, or swaps the one being changed.</summary>
        private void PicturePicker(Gantry g)
        {
            List<string> files = ImageImport.ListFiles();
            var top = _content.AddUIComponent<UIPanel>();
            top.size = new Vector2(_content.width, 40f);
            UILabel note = UIUtils.CreateLabel(top, _pictureNote ?? (files.Count > 0
                ? "Click a picture to use it. Transparent PNGs work best. It's saved with your city."
                : "No pictures yet. Put PNG or JPG files in the folder, then click Look again."), 0.8f);
            note.relativePosition = new Vector3(0f, 8f);
            note.width = _content.width;

            if (!_changingPicture)
            {
                float half = (_content.width - 6f) / 2f;
                var how = _content.AddUIComponent<UIPanel>();
                how.size = new Vector2(_content.width, RowHeight);
                UIButton place = RowButton(how, 0f, half, "Add it to the sign", "The picture goes on the sign, alongside what's already there", () =>
                {
                    _coverMode = false;
                    _rebuild = true;
                });
                UIButton cover = RowButton(how, half + 6f, half, "Cover the whole sign", "The picture becomes the whole sign, in place of what's on it, and the sign takes its shape (Ctrl+Z brings it back)", () =>
                {
                    _coverMode = true;
                    _rebuild = true;
                });
                (_coverMode ? cover : place).normalBgSprite = "ButtonMenuPressed";
            }

            if (files.Count > 0)
            {
                UIPanel box;
                float width;
                UIScrollablePanel scroll = PickerScroll(out box, out width);
                var cells = new List<PickerCell>();
                foreach (string name in files)
                {
                    string file = name;
                    cells.Add(new PickerCell
                    {
                        Label = System.IO.Path.GetFileNameWithoutExtension(file),
                        Id = "file:" + file,
                        Tip = "Use " + file,
                        Picture = ImageImport.Preview(file),
                        Use = () => UsePicture(g, file)
                    });
                }

                FinishPickerScroll(box, scroll, PickerSection(scroll, "Your pictures", cells, width));
            }

            float third = (_content.width - 12f) / 3f;
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            RowButton(row, 0f, third, "Open folder", ImageImport.Folder, OpenPictureFolder);
            RowButton(row, third + 6f, third, "Look again", "Show pictures you've just added to the folder", () =>
            {
                ImageImport.ForgetPreviews();
                _pictureNote = null;
                _rebuild = true;
            });
            RowButton(row, 2f * (third + 6f), third, "Back", "Go back without changing the sign", () =>
            {
                _choosingPicture = false;
                _rebuild = true;
            });
        }

        private void OpenPictureFolder()
        {
            try
            {
                Directory.CreateDirectory(ImageImport.Folder);
                ColossalFramework.Utils.OpenInFileBrowser(ImageImport.Folder);
            }
            catch (Exception e)
            {
                Log.Error("Opening the Signs folder failed", e);
                _pictureNote = "Couldn't open the folder: " + ImageImport.Folder;
                _rebuild = true;
            }
        }

        /// <summary>Any colours for the whole sign, on top of its colour scheme.</summary>
        private void SignColours(UIPanel box, Gantry g)
        {
            string[] keys = { "background", "foreground" };
            string[] titles = { "Sign", "Text" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var row = box.AddUIComponent<UIPanel>();
                row.size = new Vector2(_content.width, RowHeight);
                row.relativePosition = new Vector3(0f, i * RowHeight);
                ColourStrip(row, 0f, titles[i], Json.Str(_doc, key), g, v =>
                {
                    if (v == null)
                    {
                        _doc.Remove(key);
                    }
                    else
                    {
                        _doc[key] = v;
                    }
                });
            }
        }

        /// <summary>The picture covering the sign has no part to click, so its buttons show when nothing's selected.</summary>
        private void CoverControls(UIPanel box, Gantry g)
        {
            var row = box.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            row.relativePosition = new Vector3(0f, 118f);
            float half = (_content.width - 6f) / 2f;
            RowButton(row, 0f, half, "Change whole-sign picture...", "Swap in a different picture", () =>
            {
                ChoosePicture(false);
                _coverMode = true;
            });
            RowButton(row, half + 6f, half, "Remove whole-sign picture", "Take the picture off; anything added on top stays (Ctrl+Z brings it back)", () =>
            {
                _doc.Remove(GuidePictures.PartKey);
                Commit(g, "sign content");
                _rebuild = true;
            });
        }

        /// <summary>Puts a picture over the whole sign, and gives the sign the picture's shape, as far as the lanes it spans allow.</summary>
        private void CoverSign(Gantry g, string id)
        {
            GuideImage image = GuidePictures.Image(id);
            float aspect = (float)image.Width / image.Height;
            GuidePlacement placed = GuideMeshBuilder.Place(g, _shown);
            float signWidth = placed.WidthMetres, signHeight = placed.PanelMetres;
            if (_shown.FitFirst > 0)
            {
                _doc.Remove(GuideFit.WidthKey);
                signHeight = Mathf.Clamp(signWidth / aspect, 1f, MaxSignHeight);
            }
            else
            {
                signWidth = Mathf.Clamp(signHeight * aspect, 1f, MaxSignWidth);
                signHeight = Mathf.Clamp(signWidth / aspect, 1f, MaxSignHeight);
                _doc[GuideFit.WidthKey] = Math.Round(signWidth, 1);
            }

            _doc[GuideFit.HeightKey] = Math.Round(signHeight, 1);
            GuidePictures.Cover(_doc, id);
            _selected = null;
            _selectedPath = null;
            string design = Json.Write(_doc);
            Change(g, "sign content", h =>
            {
                h.Design = design;
                h.LaneArrows = false;
                return h;
            });
            _rebuild = true;
        }

        private void ChoosePicture(bool changing)
        {
            CommitText();
            _changingPicture = changing;
            _coverMode = false;
            _pictureNote = null;
            _choosingPicture = true;
            _saving = false;
            _rebuild = true;
        }

        private void UsePicture(Gantry g, string file)
        {
            string error;
            byte[] bytes = ImageImport.LoadForGuideSign(file, out error);
            string id = bytes != null ? GuidePictures.Add(bytes) : null;
            if (id != null && GuidePictures.Image(id) == null)
            {
                id = null;
                error = file + " isn't a PNG or JPG the game can read";
            }

            if (id != null)
            {
                var gantries = new List<Gantry>();
                GantryManager.GetAll(gantries);
                HashSet<string> used = GuidePictures.UsedIds(gantries, GantryManager.Lock);
                if (!used.Contains(id) && used.Count >= GuidePictures.MaxPictures)
                {
                    id = null;
                    error = "This city already has " + GuidePictures.MaxPictures + " different sign pictures, the most it can keep.";
                }
            }

            if (id == null)
            {
                _pictureNote = error ?? "Couldn't use " + file;
                _rebuild = true;
                return;
            }

            _choosingPicture = false;
            if (_coverMode)
            {
                CoverSign(g, id);
            }
            else if (_changingPicture && _selected != null)
            {
                // The design is read afresh each time the panel redraws, so this is the part as it is now.
                GuidePictures.SetPicture(_selected, id);
                Commit(g, "sign content");
                _rebuild = true;
            }
            else
            {
                Dictionary<string, object> part = GuidePictures.NewPart(id);
                List<object> items = Items(_doc);
                Insert(g, NewLineIndex(items), part, part);
            }
        }

        private void PictureControls(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            UIUtils.CreateLabel(box, "Your picture: drag it to move it", 0.8f).relativePosition = new Vector3(0f, 0f);
            var row = box.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            row.relativePosition = new Vector3(0f, 18f);
            float third = (_content.width - 12f) / 3f;
            RowButton(row, 0f, third, "Change picture...", "Swap in a different picture", () => ChoosePicture(true));
            MiniSlider(box, third + 12f, ControlsY - 34f, 2f * third - 6f, "Size", GuidePictures.MinHeight, GuidePictures.MaxHeight, 1f, Json.Num(item, "height", GuidePictures.NewHeight), v =>
            {
                item["height"] = (double)v;
                Commit(g, "sign picture size");
            });
        }

        private sealed class PickerCell
        {
            public string Label, Id, Tip;
            public Texture2D Picture;
            public Action Use, Delete;
        }

        /// <summary>A heading and a grid of signs; returns the height they take.</summary>
        private float PickerSection(UIScrollablePanel scroll, string title, List<PickerCell> cells, float width)
        {
            const int Columns = 5;
            const float CellHeight = 78f, Gap = 4f;
            string current = Json.Str(_doc, "id");
            var head = scroll.AddUIComponent<UIPanel>();
            head.size = new Vector2(width, 18f);
            UIUtils.CreateLabel(head, title, 0.8f).relativePosition = Vector3.zero;

            float cell = (width - Gap * (Columns - 1)) / Columns;
            var grid = scroll.AddUIComponent<UIPanel>();
            grid.size = new Vector2(width, Mathf.Ceil(cells.Count / (float)Columns) * (CellHeight + Gap));
            for (int i = 0; i < cells.Count; i++)
            {
                PickerCell c = cells[i];
                Vector3 at = new Vector3((i % Columns) * (cell + Gap), (i / Columns) * (CellHeight + Gap));
                var b = grid.AddUIComponent<UIButton>();
                b.size = new Vector2(cell, CellHeight);
                b.relativePosition = at;
                b.normalBgSprite = c.Id == current ? "ButtonMenuPressed" : "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.tooltip = c.Tip ?? c.Label + ": use this sign (replaces what's on it now)";
                b.eventClick += (x, p) => c.Use();

                if (c.Picture != null)
                {
                    var picture = b.AddUIComponent<UITextureSprite>();
                    picture.isInteractive = false;
                    picture.texture = c.Picture;
                    FitPicture(picture, c.Picture, cell - 8f, CellHeight - 24f, 4f, 4f);
                }

                UILabel label = UIUtils.CreateLabel(b, c.Label.Length > 20 ? c.Label.Substring(0, 19) + "..." : c.Label, 0.65f);
                label.isInteractive = false;
                label.relativePosition = new Vector3(5f, CellHeight - 18f);

                if (c.Delete != null)
                {
                    var delete = grid.AddUIComponent<UIButton>();
                    delete.normalBgSprite = "buttonclose";
                    delete.hoveredBgSprite = "buttonclosehover";
                    delete.pressedBgSprite = "buttonclosepressed";
                    delete.size = new Vector2(20f, 20f);
                    delete.relativePosition = at + new Vector3(cell - 22f, 2f);
                    delete.tooltip = "Delete this saved sign";
                    delete.eventClick += (x, p) => c.Delete();
                }
            }

            return head.height + grid.height + 8f;
        }

        private void AskDelete(string name)
        {
            ConfirmPanel confirm = UIView.library.ShowModal<ConfirmPanel>("ConfirmPanel", (c, result) =>
            {
                if (result == 1)
                {
                    MySigns.Delete(MySignsFolder, name);
                    GuideCache.ForgetThumbnail(MyKey(name));
                    _rebuild = true;
                }
            });
            confirm.SetMessage("Delete saved sign", "Delete \"" + name + "\"? Signs already on your gantries stay as they are.");
        }

        /// <summary>Whose signs to pick from; this city's new signs then start from there too.</summary>
        private void CountryPicker()
        {
            List<string> ids = GuideAssets.Ids();
            var names = new string[ids.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = GuideThemes.Name(ids[i]);
            }

            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            UIUtils.CreateLabel(row, "Signs from", 0.85f).relativePosition = new Vector3(0f, 8f);
            Dropdown(row, 90f, 260f, names, Mathf.Max(0, ids.IndexOf(GuideThemes.Current)), index =>
            {
                if (index >= 0 && index < ids.Count && ids[index] != GuideThemes.Current)
                {
                    GuideThemes.Current = ids[index];
                    _rebuild = true;
                }
            });
        }

        private void UseTemplate(Gantry g, string name)
        {
            Dictionary<string, object> template = GuideAssets.Template(GuideThemes.Current, name);
            if (Settings.Current.AutoFillSigns)
            {
                bool partly;
                RoadFinder.Fill(g, _shown, template, out partly);
            }

            UseDesign(g, template);
        }

        private void UseDesign(Gantry g, Dictionary<string, object> template)
        {
            string design = Json.Write(template);
            bool arrows = GuideFit.HasArrowRow(template);
            Change(g, "guide sign", h =>
            {
                h.Design = design;
                h.LaneArrows = arrows;
                return h;
            });
            _picking = false;
            _selected = null;
            _selectedPath = null;
            _rebuild = true;
        }

        private void PlacementPage(Gantry g, GuideHead sign)
        {
            var helper = new UIHelper(_content);
            FacingDropdown(helper, g, sign);
            LaneControls(helper, g, sign);
            SizeControls(helper, g, sign);
            UIUtils.StyledSlider(helper, _content.width, "Text and symbol size", GuideData.MinScale * 100f, GuideData.MaxScale * 100f, 5f, sign.Scale * 100f, " %", "0", true, v =>
            {
                Change(g, "sign size", h =>
                {
                    h.Scale = v / 100f;
                    return h;
                });
            });
            CopyControls(g, sign);
        }

        /// <summary>Putting the same sign elsewhere: the other way, along the road, or before it with how far it is.</summary>
        private void CopyControls(Gantry g, GuideHead sign)
        {
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            float third = (_content.width - 12f) / 3f;
            bool room = GantryManager.CanAddGuideSign(g);
            UIButton opposite = RowButton(row, 0f, third, "Copy to the other side", "A copy facing the other way, with road names for that side (Ctrl+Z takes it back)", () => CopyOpposite(g));
            opposite.isEnabled = room && !GuideFit.IsBanner(sign.Design);
            bool banner = GuideFit.IsBanner(sign.Design);
            List<RoadFinder.RoadGantry> road = RoadFinder.SameRoad(g, sign);
            UIButton along = RowButton(row, third + 6f, third, "Copy along this road" + (road.Count > 0 ? " (" + road.Count + ")" : ""), "A copy on every other gantry over this road, the same way (Ctrl+Z takes them all back)", () => AskCopyAlongRoad(g));
            along.isEnabled = road.Count > 0;
            bool before = false;
            foreach (RoadFinder.RoadGantry r in road)
            {
                before |= r.Ahead < 0f;
            }

            UIButton advance = RowButton(row, 2f * (third + 6f), third, "Add advance sign", "A copy on the gantry before this one, saying how far ahead this is (Ctrl+Z takes it back)", () => AddAdvance(g));
            advance.isEnabled = before && !banner;
            if (_copyNote != null)
            {
                var note = _content.AddUIComponent<UIPanel>();
                note.size = new Vector2(_content.width, 20f);
                UIUtils.CreateLabel(note, _copyNote, 0.75f).relativePosition = new Vector3(2f, 2f);
            }
        }

        /// <summary>A copy of this sign on a gantry, on free lanes, with the roads there. False if there's no room for it.</summary>
        private bool AddCopy(Gantry target, sbyte side, Dictionary<string, object> design, float scale, bool laneArrows)
        {
            if (!GantryManager.CanAddGuideSign(target))
            {
                return false;
            }

            byte first, last;
            lock (GantryManager.Lock)
            {
                GuideFit.DefaultRange(target.Lanes, target.GuideSigns, side, out first, out last);
            }

            var doc = (Dictionary<string, object>)Json.Parse(Json.Write(design));
            var placing = new GuideHead { Along = target.Span * 0.5f, Side = side, FitFirst = first, FitLast = last };
            RoadFinder.Refresh(target, placing, doc);
            string written = Json.Write(doc);
            if (written.Length > GuideData.MaxDesignLength)
            {
                return false; // too long to keep in a save
            }

            int head = GantryManager.AddGuideSign(target, target.Span * 0.5f, side, written, first, last);
            if (head < 0)
            {
                return false;
            }

            GantryManager.UpdateGuideSign(target, head, null, h =>
            {
                h.Scale = scale;
                h.LaneArrows = laneArrows && first > 0 && GuideFit.HasArrowRow(doc);
                return h;
            });
            return true;
        }

        private void CopyOpposite(Gantry g)
        {
            CommitText();
            bool added = AddCopy(g, (sbyte)-_shown.Side, _doc, _shown.Scale, _shown.LaneArrows);
            _copyNote = added ? "Copied to the other side of this gantry." : "No room for another sign on this gantry.";
            _rebuild = true;
        }

        private void AskCopyAlongRoad(Gantry g)
        {
            CommitText();
            List<RoadFinder.RoadGantry> road = RoadFinder.SameRoad(g, _shown);
            ConfirmPanel confirm = UIView.library.ShowModal<ConfirmPanel>("ConfirmPanel", (c, result) =>
            {
                if (result == 1)
                {
                    CopyAlongRoad(road);
                }
            });
            confirm.SetMessage("Copy along this road", "Add a copy of this sign to " + road.Count + " other gantr" + (road.Count == 1 ? "y" : "ies") + " over " + RoadFinder.RoadName(g, _shown) + "? Ctrl+Z takes them all back in one go.");
        }

        private void CopyAlongRoad(List<RoadFinder.RoadGantry> road)
        {
            int added = 0, full = 0;
            UndoStack.BeginGroup();
            try
            {
                foreach (RoadFinder.RoadGantry r in road)
                {
                    if (AddCopy(r.Gantry, r.Side, _doc, _shown.Scale, _shown.LaneArrows))
                    {
                        added++;
                    }
                    else
                    {
                        full++;
                    }
                }
            }
            finally
            {
                UndoStack.EndGroup();
            }

            _copyNote = "Added to " + added + " gantr" + (added == 1 ? "y" : "ies") + (full > 0 ? "; " + full + " had no room" : "") + ".";
            _rebuild = true;
        }

        private void AddAdvance(Gantry g)
        {
            CommitText();
            bool found = false;
            RoadFinder.RoadGantry nearest = default(RoadFinder.RoadGantry);
            foreach (RoadFinder.RoadGantry r in RoadFinder.SameRoad(g, _shown))
            {
                if (r.Ahead < 0f && (!found || r.Ahead > nearest.Ahead))
                {
                    nearest = r;
                    found = true;
                }
            }

            if (found)
            {
                bool added = AddCopy(nearest.Gantry, nearest.Side, GuideFit.AdvanceCopy(_doc, -nearest.Ahead), _shown.Scale, _shown.LaneArrows);
                _copyNote = added ? "Added to gantry #" + nearest.Gantry.Id + ", " + GuideFit.DistanceWords(Theme, -nearest.Ahead) + " before this one." : "No room for another sign on the gantry before this one.";
                _rebuild = true;
            }
        }

        /// <summary>The sign's own width and height; left alone, it has the default size and matches the signs beside it.</summary>
        private void SizeControls(UIHelper helper, Gantry g, GuideHead sign)
        {
            GuidePlacement placed = GuideMeshBuilder.Place(g, sign);
            _widthSlider = UIUtils.StyledSlider(helper, _content.width, "Width", 1f, Mathf.Max(MaxSignWidth, g.Span), 0.1f, placed.WidthMetres, " m", "0.0", true, v => SetSize(g, GuideFit.WidthKey, v));
            _heightSlider = UIUtils.StyledSlider(helper, _content.width, "Height", 1f, MaxSignHeight, 0.1f, placed.PanelMetres, " m", "0.0", true, v => SetSize(g, GuideFit.HeightKey, v));
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            RowButton(row, 0f, _content.width, "Reset to default size", "Go back to the default size, matching the signs beside it (Ctrl+Z brings yours back)", () =>
            {
                _doc.Remove(GuideFit.WidthKey);
                _doc.Remove(GuideFit.HeightKey);
                string design = Json.Write(_doc);
                Change(g, "sign dimensions", h =>
                {
                    h.Design = design;
                    h.Scale = 1f;
                    return h;
                });
                _rebuild = true;
            });
        }

        private void SetSize(Gantry g, string key, float metres)
        {
            if (!_building)
            {
                _doc[key] = Math.Round(metres, 1);
                Commit(g, "sign dimensions");
            }
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
                _rebuild = true;
            });
            Fit(drop);
        }

        /// <summary>Where the sign sits: spanning whole lanes or by hand, and the picture to drag it around in.</summary>
        private void LaneControls(UIHelper helper, Gantry g, GuideHead sign)
        {
            List<GantryLane> lanes;
            lock (GantryManager.Lock)
            {
                lanes = GuideFit.FaceLanes(g.Lanes, sign.Side);
            }

            var span = (UIComponent)helper.AddCheckbox("Span whole lanes (off: place it by hand)", sign.FitFirst > 0 && lanes.Count > 0, on =>
            {
                float along = GuideMeshBuilder.Place(g, _shown).Along;
                byte first = 0, last = 0;
                if (on)
                {
                    lock (GantryManager.Lock)
                    {
                        var others = new List<GuideHead>(g.GuideSigns);
                        others.RemoveAll(o => o.Id == HeadRef.GuideId(_head));
                        GuideFit.DefaultRange(g.Lanes, others, _shown.Side, out first, out last);
                    }
                }

                Change(g, "sign fit", h =>
                {
                    h.Along = along;
                    h.FitFirst = first;
                    h.FitLast = last;
                    h.LaneArrows = on && GuideFit.HasArrowRow(_doc);
                    return h;
                });
                _rebuild = true;
            });
            span.isEnabled = lanes.Count > 0;
            span.opacity = lanes.Count > 0 ? 1f : 0.4f;

            bool fitted = lanes.Count > 0 && sign.FitFirst > 0;
            string help = lanes.Count == 0
                ? "No traffic comes this way, so this side has no lanes to span. Drag the sign along the beam."
                : fitted
                    ? "As drivers see it. Drag the sign along the lanes, or by its yellow edges to cover more or fewer."
                    : "As drivers see it. Drag the sign along the beam. Click another sign to edit it.";
            UILabel caption = UIUtils.CreateLabel(_content, help, 0.7f);
            caption.autoHeight = false;
            caption.height = 14f;
            GuideArrangeView.Create(_content, _content.width, g, _head, sign.Side, (what, change) => Change(g, what, change), head =>
            {
                if (Tool != null)
                {
                    Tool.EditHead(g, head);
                }
            });
        }

        // ------------------------------------------------------------------------------ the sign's content

        /// <summary>The controls for the selected part, and nothing else.</summary>
        private void Inspector(Gantry g)
        {
            var box = _content.AddUIComponent<UIPanel>();
            box.size = new Vector2(_content.width, InspectorHeight);
            string type = _selected == null ? null : Json.Str(_selected, "type");
            type = type == "row" && !GuideFit.IsArrows(_selected) ? null : type;
            switch (type)
            {
                case "text":
                    TextControls(box, g);
                    LineControls(box, g);
                    break;
                case "shield":
                    ShieldControls(box, g);
                    LineControls(box, g);
                    break;
                case "arrow":
                    if (InLaneRow())
                    {
                        ArrowControls(box, g, "The arrow over this lane", new List<Dictionary<string, object>> { _selected }, true);
                        ExitOnlyControls(box, g);
                    }
                    else
                    {
                        ArrowControls(box, g, "Which way the arrow points", new List<Dictionary<string, object>> { _selected }, false);
                    }

                    break;
                case "row":
                    var arrows = new List<Dictionary<string, object>>();
                    foreach (object o in Items(_selected))
                    {
                        arrows.Add((Dictionary<string, object>)o);
                    }

                    ArrowControls(box, g, "All the lane arrows: click one arrow on the sign to change just that lane", arrows, false);
                    break;
                case "panel":
                case "chip":
                    PartColour(box, g);
                    break;
                case "symbol":
                    if (Json.Has(_selected, GuidePictures.PartKey))
                    {
                        PictureControls(box, g);
                        LineControls(box, g);
                    }
                    else if (Json.Str(_selected, "name", "").StartsWith("lane-ends", StringComparison.Ordinal))
                    {
                        LaneEndsControls(box, g);
                    }
                    else
                    {
                        UIUtils.CreateLabel(box, "A picture: drag it to move it, or use the buttons below", 0.8f).relativePosition = new Vector3(0f, 8f);
                    }

                    break;
                case "tab":
                    UIUtils.CreateLabel(box, "Exit tab: click its text to change it", 0.8f).relativePosition = new Vector3(0f, 8f);
                    break;
                default:
                    if (Json.Has(_doc, GuidePictures.PartKey))
                    {
                        CoverControls(box, g);
                    }

                    SignColours(box, g);
                    UILabel hint = UIUtils.CreateLabel(box, _notice ?? "Click any part of the sign above to change it, or drag it somewhere else:\nit snaps above, below or beside the other parts. Add something new below.", 0.85f);
                    hint.relativePosition = new Vector3(0f, 72f);
                    return;
            }

            MoveButtons(box, g);
        }

        /// <summary>Left, centre and right for the line this part is on, where that means anything; returns their row.</summary>
        private UIPanel LineControls(UIPanel box, Gantry g)
        {
            var row = box.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            row.relativePosition = new Vector3(0f, ControlsY + 40f);
            _selectedPath = PathOf(_doc, _selected, "");
            string place;
            Dictionary<string, object> line = GuideFit.LineOf(_doc, _selectedPath, out place);
            if (line == null)
            {
                return row;
            }

            UIUtils.CreateLabel(row, "Line up", 0.75f).relativePosition = new Vector3(0f, 10f);
            string[] places = { "start", "center", "end" };
            string[] labels = { "Left", "Centre", "Right" };
            const float X = 50f, W = 62f;
            for (int i = 0; i < places.Length; i++)
            {
                string to = places[i];
                UIButton b = RowButton(row, X + i * (W + 4f), W, labels[i], "Line this up on the " + labels[i].ToLowerInvariant() + " of the sign or its section", () =>
                {
                    line["place"] = to;
                    Commit(g, "sign content");
                    _rebuild = true;
                });
                if (place == to)
                {
                    b.normalBgSprite = "ButtonMenuPressed";
                }
            }

            return row;
        }

        private void TextControls(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            UIUtils.CreateLabel(box, "Text", 0.8f).relativePosition = new Vector3(0f, 0f);
            _focus = Field(box, 0f, 18f, _content.width, Json.Str(item, "text", ""), 48, g, v =>
            {
                item["text"] = v;
                MarkEdited(item);
            });

            float third = (_content.width - 24f) / 3f;
            float height = Json.Num(item, "height", 16f);
            MiniSlider(box, 0f, ControlsY, third, "Size", 8f, 60f, 1f, height, v =>
            {
                item["height"] = (double)v;
                Commit(g, "sign text size");
            });
            MiniSlider(box, third + 12f, ControlsY, third, "Letter spacing", -5f, 40f, 1f, Json.Num(item, "tracking", 0f), v =>
            {
                item["tracking"] = (double)v;
                Commit(g, "sign text spacing");
            });

            _selectedPath = PathOf(_doc, _selected, "");
            string place;
            Dictionary<string, object> line = GuideFit.LineOf(_doc, _selectedPath, out place);
            if (line != null && !IsFirstOfColumn(line))
            {
                MiniSlider(box, 2f * (third + 12f), ControlsY, third, "Gap above", 0f, 60f, 1f, Json.Num(line, "gapBefore", height * 0.75f), v =>
                {
                    line["gapBefore"] = (double)v;
                    Commit(g, "sign line spacing");
                });
            }

            UIPanel row = LineControls(box, g);
            ColourStrip(row, 260f, "Colour", Json.Str(item, "color"), g, v =>
            {
                if (v == null)
                {
                    item.Remove("color");
                }
                else
                {
                    item["color"] = v;
                }
            });
        }

        /// <summary>The player wrote this themselves, so it stops following the road.</summary>
        private static void MarkEdited(Dictionary<string, object> item)
        {
            if (Json.Has(item, GuideAutoFill.PartKey))
            {
                item[GuideAutoFill.EditedKey] = true;
            }
        }

        private bool IsFirstOfColumn(Dictionary<string, object> line)
        {
            Dictionary<string, object> at = _doc;
            foreach (string step in _selectedPath.Split('/'))
            {
                int i;
                List<object> items = Json.Arr(at, "items");
                if (items == null || !int.TryParse(step, out i) || i < 0 || i >= items.Count)
                {
                    return false;
                }

                if (items[i] == line)
                {
                    // Tabs and bands aren't lines of the column: the first line after them has nothing to be a gap from.
                    for (int k = 0; k < i; k++)
                    {
                        string type = Json.Str(items[k] as Dictionary<string, object>, "type");
                        if (type != "tab" && type != "panel")
                        {
                            return false;
                        }
                    }

                    return true;
                }

                at = items[i] as Dictionary<string, object>;
            }

            return false;
        }

        /// <summary>
        /// A small slider with its label over it, placed by hand among the other controls. The label is its own: the one in the
        /// game's slider template is tied to the template's size, so it ends up behind the bar at this height.
        /// </summary>
        private void MiniSlider(UIPanel box, float x, float y, float w, string text, float min, float max, float step, float value, Action<float> apply)
        {
            UILabel title = UIUtils.CreateLabel(box, text + ": " + Mathf.Clamp(value, min, max).ToString("0"), 0.75f);
            title.relativePosition = new Vector3(x, y);
            UISlider slider = UIUtils.StyledSlider(new UIHelper(box), w, text, min, max, step, value, "", "0", true, v =>
            {
                title.text = text + ": " + v.ToString("0");
                if (!_building)
                {
                    apply(v);
                }
            }, 0.75f);
            var holder = slider.parent as UIPanel;
            if (holder != null)
            {
                holder.autoLayout = false;
            }

            UILabel own = slider.parent.Find<UILabel>("Label");
            if (own != null)
            {
                own.isVisible = false;
            }

            slider.anchor = UIAnchorStyle.None;
            slider.parent.relativePosition = new Vector3(x, y + 16f);
            slider.parent.height = 20f;
            slider.width = w - 4f;
            slider.relativePosition = Vector3.zero;
        }

        /// <summary>Small colour squares and a box for any colour; null means back to the sign's own colour.</summary>
        private void ColourStrip(UIPanel row, float x, string title, string current, Gantry g, Action<string> apply)
        {
            UILabel label = UIUtils.CreateLabel(row, title, 0.75f);
            label.relativePosition = new Vector3(x, 10f);
            x += 50f;
            Action<string> use = v =>
            {
                Log.Debug("Sign colour: " + title + " set to " + (v ?? "auto"));
                apply(v);
                Commit(g, "sign colour");
                _rebuild = true;
            };
            foreach (string colour in SwatchColours)
            {
                string c = colour;
                UIButton swatch = RowButton(row, x, 20f, "", c, () => use(c));
                swatch.size = new Vector2(20f, 20f);
                swatch.relativePosition = new Vector3(x, 8f);
                Color32 tint = HexColour(c);
                swatch.normalBgSprite = swatch.hoveredBgSprite = swatch.pressedBgSprite = swatch.focusedBgSprite = "EmptySprite";
                swatch.color = tint;
                swatch.hoveredColor = tint;
                swatch.pressedColor = tint;
                swatch.focusedColor = tint;
                x += 23f;
            }

            UITextField hex = UIUtils.CreateTextField(row, 86f, 7);
            hex.relativePosition = new Vector3(x + 2f, 3f);
            hex.height = 28f;
            hex.textScale = 0.75f;
            hex.padding = new RectOffset(5, 3, 8, 6);
            hex.text = current ?? "";
            hex.tooltip = "Any colour, like #ffb000";
            hex.selectOnFocus = true;
            hex.submitOnFocusLost = true;
            hex.eventTextSubmitted += (c, v) =>
            {
                string typed = v.Trim();
                if (_building || typed == (current ?? ""))
                {
                    return;
                }

                if (typed.Length == 0)
                {
                    use(null);
                }
                else if (GuideRenderer.IsColour(typed))
                {
                    use(typed.ToLowerInvariant());
                }
                else
                {
                    hex.text = current ?? "";
                }
            };
            RowButton(row, x + 92f, 40f, "Auto", "Back to the sign's own colour", () => use(null)).relativePosition = new Vector3(x + 92f, 3f);
        }

        private static Color32 HexColour(string hex)
        {
            int v = int.Parse(hex.Substring(1), System.Globalization.NumberStyles.HexNumber);
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }

        private void ShieldControls(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            var keys = new List<string>();
            var labels = new List<string>();
            Dictionary<string, object> shields = Json.Obj(GuideAssets.Theme(Theme), "shields");
            if (shields != null)
            {
                foreach (KeyValuePair<string, object> kv in shields)
                {
                    keys.Add(kv.Key);
                    labels.Add(Json.Str(kv.Value as Dictionary<string, object>, "label", kv.Key));
                }
            }

            float half = (_content.width - 6f) / 2f;
            UIUtils.CreateLabel(box, "Route type", 0.8f).relativePosition = new Vector3(0f, 0f);
            Dropdown(box, 0f, 18f, half, labels.ToArray(), Mathf.Max(0, keys.IndexOf(Json.Str(item, "network", ""))), index =>
            {
                if (index >= 0 && index < keys.Count)
                {
                    item["network"] = keys[index];
                    Commit(g, "sign content");
                }
            });
            object number;
            string value = item.TryGetValue("number", out number) && number != null ? Convert.ToString(number, System.Globalization.CultureInfo.InvariantCulture) : "";
            UIUtils.CreateLabel(box, "Route number", 0.8f).relativePosition = new Vector3(half + 6f, 0f);
            _focus = Field(box, half + 6f, 18f, half, value, 4, g, v =>
            {
                item["number"] = v;
                MarkEdited(item);
            });
            MiniSlider(box, 0f, ControlsY, half, "Size", 12f, 90f, 1f, Json.Num(item, "height", 30f), v =>
            {
                item["height"] = (double)v;
                Commit(g, "sign shield size");
            });
        }

        /// <summary>Whether the selected part belongs to the row of one arrow per lane.</summary>
        private bool InLaneRow()
        {
            if (_selectedPath == null || !_shown.LaneArrows || _shown.FitFirst == 0)
            {
                return false;
            }

            int line;
            return int.TryParse(_selectedPath.Split('/')[0], out line) && line == GuideFit.LaneRowIndex(_doc);
        }

        /// <summary>Whether this lane is exit only, and what those sections say.</summary>
        private void ExitOnlyControls(UIPanel box, Gantry g)
        {
            const float W = 130f, Y = 78f;
            Dictionary<string, object> arrow = _selected;
            bool on = arrow.ContainsKey(GuideFit.ExitOnlyKey);
            RowButton(box, 0f, W, "Exit only: " + (on ? "on" : "off"), "A yellow section under this lane. Neighbouring exit only lanes share one.", () =>
            {
                if (on)
                {
                    arrow.Remove(GuideFit.ExitOnlyKey);
                }
                else
                {
                    arrow[GuideFit.ExitOnlyKey] = true;
                }

                Commit(g, "sign content");
                _rebuild = true;
            });
            UIComponent toggle = box.components[box.components.Count - 1];
            toggle.relativePosition = new Vector3(0f, Y);
            if (on)
            {
                var button = (UIButton)toggle;
                button.normalBgSprite = "ButtonMenuPressed";
                int line;
                int.TryParse(_selectedPath.Split('/')[0], out line);
                var row = (Dictionary<string, object>)Items(_doc)[line];
                UITextField field = Field(box, W + 6f, Y, _content.width - W - 6f, Json.Str(row, GuideFit.ExitTextKey, GuideThemes.Word(Theme, GuideFit.ExitOnlyWord, GuideFit.DefaultExitText)), 24, g, v => row[GuideFit.ExitTextKey] = v);
                field.tooltip = "What the exit only sections say";
            }
        }

        private void ArrowControls(UIPanel box, Gantry g, string title, List<Dictionary<string, object>> arrows, bool allowNone)
        {
            UIUtils.CreateLabel(box, title, 0.8f).relativePosition = new Vector3(0f, 0f);
            string current = arrows.Count > 0 ? Json.Str(arrows[0], "direction", "down") : "down";
            int count = ArrowKeys.Length + (allowNone ? 1 : 0);
            float gap = 3f, cell = (_content.width - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                string key = i < ArrowKeys.Length ? ArrowKeys[i] : "none";
                var b = box.AddUIComponent<UIButton>();
                b.size = new Vector2(cell, 56f);
                b.relativePosition = new Vector3(i * (cell + gap), 18f);
                b.normalBgSprite = key == current ? "ButtonMenuPressed" : "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.tooltip = i < ArrowKeys.Length ? ArrowLabels[i] : "No arrow over this lane";
                b.eventClick += (c, p) =>
                {
                    foreach (Dictionary<string, object> a in arrows)
                    {
                        a["direction"] = key;
                    }

                    Commit(g, "sign content");
                    _rebuild = true;
                };
                if (key == "none")
                {
                    b.text = "None";
                    b.textScale = 0.7f;
                    continue;
                }

                Texture2D tex = GuideCache.ArrowPicture(key);
                if (tex != null)
                {
                    var picture = b.AddUIComponent<UITextureSprite>();
                    picture.isInteractive = false;
                    picture.texture = tex;
                    FitPicture(picture, tex, cell - 6f, 50f, 3f, 3f);
                }
            }
        }

        private void LaneEndsControls(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            string[] names = { "lane-ends-right", "lane-ends-left" };
            string[] labels = { "Right lane ends", "Left lane ends" };
            UIUtils.CreateLabel(box, "Which lane ends", 0.8f).relativePosition = Vector3.zero;
            Dropdown(box, 0f, 18f, (_content.width - 6f) / 2f, labels, Mathf.Max(0, Array.IndexOf(names, Json.Str(item, "name", ""))), index =>
            {
                if (index >= 0 && index < names.Length)
                {
                    item["name"] = names[index];
                    Commit(g, "sign content");
                }
            });
        }

        private void PartColour(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            var keys = new List<string>();
            var labels = new List<string>();
            Dictionary<string, object> schemes = Json.Obj(GuideAssets.Theme(Theme), "schemes");
            if (schemes != null)
            {
                foreach (KeyValuePair<string, object> kv in schemes)
                {
                    keys.Add(kv.Key);
                    labels.Add(char.ToUpperInvariant(kv.Key[0]) + kv.Key.Substring(1));
                }
            }

            UIUtils.CreateLabel(box, "Colour of this band", 0.8f).relativePosition = new Vector3(0f, 0f);
            Dropdown(box, 0f, 18f, (_content.width - 6f) / 2f, labels.ToArray(), Mathf.Max(0, keys.IndexOf(Json.Str(item, "scheme", ""))), index =>
            {
                if (index >= 0 && index < keys.Count)
                {
                    item["scheme"] = keys[index];
                    Commit(g, "sign content");
                }
            });
        }

        /// <summary>Move the selected part among its neighbours, or take it off the sign.</summary>
        private void MoveButtons(UIPanel box, Gantry g)
        {
            Dictionary<string, object> item = _selected;
            string[] steps = _selectedPath.Split('/');
            float w = (_content.width - 5f * 6f) / 6f, y = InspectorHeight - 34f;
            UIUtils.CreateLabel(box, "Move or remove this part", 0.75f).relativePosition = new Vector3(2f, y - 16f);
            List<object> top = Items(_doc);
            int line;
            int.TryParse(steps[0], out line);
            bool isTab = Json.Str(top[line] as Dictionary<string, object>, "type") == "tab";
            Dictionary<string, object> parent = ItemAt(_doc, _selectedPath.Substring(0, Mathf.Max(0, _selectedPath.LastIndexOf('/'))));
            List<object> siblings = steps.Length > 1 && parent != null ? Items(parent) : null;
            int at = siblings != null ? siblings.IndexOf(item) : -1;

            // In a stack of its own (as beside a map sign's junction), up and down are within that stack.
            bool stacked = at >= 0 && Json.Str(parent, "type") == "column";
            List<object> upDown = stacked ? siblings : top;
            int upDownAt = stacked ? at : line;
            MoveButton(box, 0, w, y, "Up", "Move it up the sign", !isTab && CanMove(upDown, upDownAt, -1), () => Swap(g, upDown, upDownAt, -1));
            MoveButton(box, 1, w, y, "Down", "Move it down the sign", !isTab && CanMove(upDown, upDownAt, 1), () => Swap(g, upDown, upDownAt, 1));
            bool sideways = at >= 0 && !stacked && !InLaneRow();
            MoveButton(box, 2, w, y, "Left", "Move it left in its row", sideways && CanMove(siblings, at, -1), () => Swap(g, siblings, at, -1));
            MoveButton(box, 3, w, y, "Right", "Move it right in its row", sideways && CanMove(siblings, at, 1), () => Swap(g, siblings, at, 1));
            MoveButton(box, 4, w, y, "Duplicate", "Add a copy of this part right after it", !isTab && !InLaneRow(), () =>
            {
                CommitText();
                _selectedPath = PathOf(_doc, _selected, "");
                Dictionary<string, object> copy = GuideFit.Duplicate(_doc, _selectedPath);
                if (copy != null)
                {
                    _selected = copy;
                    Commit(g, "sign content");
                    _rebuild = true;
                }
            });
            MoveButton(box, 5, w, y, InLaneRow() ? "Remove arrows" : "Remove", InLaneRow() ? "Take the row of lane arrows off the sign" : "Take this off the sign", true, () =>
            {
                bool laneRow = InLaneRow();
                RemoveFrom(_doc, laneRow ? (Dictionary<string, object>)top[line] : item);
                Prune(_doc);
                _selected = null;
                _selectedPath = null;
                if (laneRow)
                {
                    SetLaneArrows(g, false, 0, 0);
                }
                else
                {
                    Commit(g, "sign content");
                }

                _rebuild = true;
            });
        }

        private static void MoveButton(UIPanel box, int index, float w, float y, string text, string tooltip, bool enabled, Action onClick)
        {
            RowButton(box, index * (w + 6f), w, text, tooltip, onClick);
            UIComponent b = box.components[box.components.Count - 1];
            b.relativePosition = new Vector3(index * (w + 6f), y);
            b.isEnabled = enabled;
        }

        /// <summary>Whether a neighbour that isn't a tab is there to swap with (tabs stay at the top).</summary>
        private static bool CanMove(List<object> list, int index, int step)
        {
            int other = index + step;
            return index >= 0 && other >= 0 && other < list.Count && Json.Str(list[other] as Dictionary<string, object>, "type") != "tab";
        }

        private void Swap(Gantry g, List<object> list, int index, int step)
        {
            object a = list[index];
            list[index] = list[index + step];
            list[index + step] = a;
            Commit(g, "sign content");
            _rebuild = true;
        }

        private void AddButtons(Gantry g)
        {
            Heading("Add to the sign");
            List<object> items = Items(_doc);
            float w = (_content.width - 3f * 6f) / 4f;
            var row = _content.AddUIComponent<UIPanel>();
            row.size = new Vector2(_content.width, RowHeight);
            RowButton(row, 0f, w, "+ Text", "Add a line of text", () =>
            {
                Dictionary<string, object> text = Text("New text", 0f, null);
                if (Json.Has(_doc, GuideRenderer.MessageSignKey))
                {
                    text[GuideRenderer.LedKey] = true;
                    text["height"] = 22.0;
                    text["color"] = "#ffb000";
                }

                Insert(g, NewLineIndex(items), text, text);
            });
            RowButton(row, w + 6f, w, "+ Shield", "Add a route shield and direction", () =>
            {
                Dictionary<string, object> shield = Shield();
                Insert(g, NewLineIndex(items), new Dictionary<string, object>
                {
                    { "type", "row" },
                    { "items", new List<object> { shield, Text("NORTH", 15f, "RG2014D") } }
                }, shield);
            });
            RowButton(row, 2f * (w + 6f), w, "+ Arrow", "Add a single arrow", () => AddArrow(g, items));
            bool hasLanes;
            lock (GantryManager.Lock)
            {
                hasLanes = GuideFit.FaceLanes(g.Lanes, _shown.Side).Count > 0;
            }

            bool builtIn = GuideFit.HasBuiltInArrows(_doc);
            RowButton(row, 3f * (w + 6f), w, "+ Lane arrows",
                builtIn ? "This sign's arrows are already part of its design"
                    : hasLanes ? "One arrow over the middle of each lane. Click one to change it, or to make that lane exit only."
                    : "No lanes pass under this side of the sign", () => AddLaneArrows(g, items));
            bool laneArrows = _shown.LaneArrows && _shown.FitFirst > 0 && GuideFit.LaneRowIndex(_doc) >= 0;
            row.components[3].isEnabled = hasLanes && !laneArrows && !builtIn;

            var row2 = _content.AddUIComponent<UIPanel>();
            row2.size = new Vector2(_content.width, RowHeight);
            RowButton(row2, 0f, w, "+ Exit tab", "Add an exit number tab on top of the sign", () =>
            {
                Dictionary<string, object> text = Text(GuideThemes.Word(Theme, GuideFit.ExitTabWord, "EXIT 1"), 10f, Json.Str(Json.Obj(GuideAssets.Theme(Theme), "exitTab"), "font", "RG2014E"));
                Insert(g, 0, new Dictionary<string, object>
                {
                    { "type", "tab" },
                    { "align", "right" },
                    { "items", new List<object> { text } }
                }, text);
            });
            row2.components[0].isEnabled = !Contains(items, "tab");
            RowButton(row2, w + 6f, w, "+ Exit only band", "A yellow EXIT ONLY band across the whole sign. For just some lanes, use + Lane arrows and click a lane's arrow.", () =>
            {
                Dictionary<string, object> text = Text(GuideThemes.Word(Theme, GuideFit.ExitOnlyWord, GuideFit.DefaultExitText), 12f, Json.Str(GuideAssets.Theme(Theme), "exitOnlyFont", "RG2014E"));
                Insert(g, items.Count, new Dictionary<string, object>
                {
                    { "type", "panel" },
                    { "scheme", "yellow" },
                    { "align", "center" },
                    { "items", new List<object> { text } }
                }, text);
            });
            row2.components[1].isEnabled = !Contains(items, "panel");
            RowButton(row2, 2f * (w + 6f), w, "+ Exit only plate", "A small EXIT ONLY plate between the sign's arrows", () =>
            {
                CommitText();
                _selected = GuideFit.AddExitPlate(_doc);
                Commit(g, "sign content");
                _rebuild = true;
            });
            row2.components[2].isEnabled = !laneArrows;
            RowButton(row2, 3f * (w + 6f), w, "+ Divider", "Split the sign in two with a line, for a second destination", () =>
            {
                CommitText();
                _selected = GuideFit.SplitSign(_doc);
                Commit(g, "sign content");
                _rebuild = true;
            });
            row2.components[3].isEnabled = !GuideFit.HasDivider(_doc);

            var row3 = _content.AddUIComponent<UIPanel>();
            row3.size = new Vector2(_content.width, RowHeight);
            RowButton(row3, 0f, _content.width, "+ Your own picture...", "Add a PNG or JPG from your Signs folder, on the sign or covering all of it", () => ChoosePicture(false));
        }

        /// <summary>A new line goes under the selected one, or above any arrows and bands at the bottom of the sign.</summary>
        private int NewLineIndex(List<object> items)
        {
            if (_selectedPath != null)
            {
                int line;
                int.TryParse(_selectedPath.Split('/')[0], out line);
                if (Json.Str(items[line] as Dictionary<string, object>, "type") != "tab")
                {
                    return line + 1;
                }
            }

            return ContentIndex(items);
        }

        private void SchemeDropdown(UIPanel row, float x, float w, Gantry g)
        {
            var keys = new List<string>();
            var labels = new List<string>();
            Dictionary<string, object> schemes = Json.Obj(GuideAssets.Theme(Theme), "schemes");
            if (schemes != null)
            {
                foreach (KeyValuePair<string, object> kv in schemes)
                {
                    keys.Add(kv.Key);
                    labels.Add("Colour: " + char.ToUpperInvariant(kv.Key[0]) + kv.Key.Substring(1));
                }
            }

            UIDropDown drop = Dropdown(row, x, w, labels.ToArray(), Mathf.Max(0, keys.IndexOf(Json.Str(_doc, "scheme", "green"))), index =>
            {
                if (index >= 0 && index < keys.Count)
                {
                    _doc["scheme"] = keys[index];
                    _doc.Remove("background");
                    _doc.Remove("foreground");
                    Commit(g, "sign content");
                    _rebuild = true;
                }
            });
            drop.tooltip = "The sign's colour";
        }

        private void Insert(Gantry g, int index, Dictionary<string, object> item, Dictionary<string, object> select)
        {
            CommitText();
            List<object> items = Items(_doc);
            items.Insert(Mathf.Clamp(index, 0, items.Count), item);
            _selected = select;
            Commit(g, "sign content");
            _rebuild = true;
        }

        private void AddArrow(Gantry g, List<object> items)
        {
            Dictionary<string, object> arrow = GuideFit.NewArrow(_doc);
            int laneRow = _shown.LaneArrows && _shown.FitFirst > 0 ? GuideFit.LaneRowIndex(_doc) : -1;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = items[i] as Dictionary<string, object>;
                if (i != laneRow && row != null && Json.Str(row, "type") == "row" && GuideFit.IsArrows(row))
                {
                    Items(row).Add(arrow);
                    row["justify"] = "between";
                    _selected = arrow;
                    Commit(g, "sign content");
                    _rebuild = true;
                    return;
                }
            }

            Insert(g, AboveBands(items), new Dictionary<string, object> { { "type", "row" }, { "items", new List<object> { arrow } } }, arrow);
        }

        /// <summary>Turns on one arrow per lane, using the arrows already on the sign, and spans its lanes if it doesn't yet.</summary>
        private void AddLaneArrows(Gantry g, List<object> items)
        {
            CommitText();
            _selected = GuideFit.MakeLaneRow(_doc);

            byte first = _shown.FitFirst, last = _shown.FitLast;
            if (first == 0)
            {
                lock (GantryManager.Lock)
                {
                    var others = new List<GuideHead>(g.GuideSigns);
                    others.RemoveAll(o => o.Id == HeadRef.GuideId(_head));
                    GuideFit.DefaultRange(g.Lanes, others, _shown.Side, out first, out last);
                }
            }

            SetLaneArrows(g, true, first, last);
            _rebuild = true;
        }

        /// <summary>Changes the lane arrows along with the design, as one undo step.</summary>
        private void SetLaneArrows(Gantry g, bool on, byte first, byte last)
        {
            _commitAt = -1f;
            string design = Json.Write(_doc);
            Change(g, "sign arrows", h =>
            {
                h.Design = design;
                h.LaneArrows = on;
                if (on)
                {
                    h.FitFirst = first;
                    h.FitLast = last;
                }

                return h;
            });
        }

        private static int AboveBands(List<object> items)
        {
            int at = items.Count;
            while (at > 0 && Json.Str(items[at - 1] as Dictionary<string, object>, "type") == "panel")
            {
                at--;
            }

            return at;
        }

        /// <summary>Where new content goes: above any arrows and bands at the bottom of the sign.</summary>
        private static int ContentIndex(List<object> items)
        {
            int at = items.Count;
            while (at > 0)
            {
                var item = items[at - 1] as Dictionary<string, object>;
                if (item == null || (Json.Str(item, "type") != "panel" && !GuideFit.IsArrows(item)))
                {
                    break;
                }

                at--;
            }

            return at;
        }

        /// <summary>The part at a path like "2/0", or null.</summary>
        private static Dictionary<string, object> ItemAt(Dictionary<string, object> doc, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Dictionary<string, object> at = doc;
            foreach (string step in path.Split('/'))
            {
                int i;
                List<object> items = Json.Arr(at, "items");
                if (items == null || !int.TryParse(step, out i) || i < 0 || i >= items.Count)
                {
                    return null;
                }

                at = items[i] as Dictionary<string, object>;
                if (at == null)
                {
                    return null;
                }
            }

            return at;
        }

        private static string PathOf(Dictionary<string, object> parent, Dictionary<string, object> item, string prefix)
        {
            List<object> items = parent == null ? null : Json.Arr(parent, "items");
            if (items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == item)
                {
                    return prefix + i;
                }

                string inner = PathOf(items[i] as Dictionary<string, object>, item, prefix + i + "/");
                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }

        private static bool RemoveFrom(Dictionary<string, object> parent, Dictionary<string, object> item)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return false;
            }

            if (items.Remove(item))
            {
                return true;
            }

            foreach (object o in items)
            {
                var child = o as Dictionary<string, object>;
                if (child != null && RemoveFrom(child, item))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<object> Items(Dictionary<string, object> parent)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                items = new List<object>();
                parent["items"] = items;
            }

            return items;
        }

        private static bool Contains(List<object> items, string type)
        {
            foreach (object o in items)
            {
                if (Json.Str(o as Dictionary<string, object>, "type") == type)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Removes groups left with nothing in them.</summary>
        private static void Prune(Dictionary<string, object> parent)
        {
            List<object> items = Json.Arr(parent, "items");
            if (items == null)
            {
                return;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i] as Dictionary<string, object>;
                string type = Json.Str(item, "type");
                if (type == "row" || type == "column" || type == "chip" || type == "tab" || type == "panel")
                {
                    Prune(item);
                    RemoveLoneDivider(item);
                    if (Items(item).Count == 0)
                    {
                        items.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>A divider with nothing left on one side of it has nothing to divide.</summary>
        private static void RemoveLoneDivider(Dictionary<string, object> row)
        {
            List<object> items = Items(row);
            int others = 0;
            foreach (object o in items)
            {
                others += Json.Str(o as Dictionary<string, object>, "type") != "divider" ? 1 : 0;
            }

            if (others < 2)
            {
                items.RemoveAll(o => Json.Str(o as Dictionary<string, object>, "type") == "divider");
            }
        }

        private static Dictionary<string, object> Text(string text, float height, string font)
        {
            var item = new Dictionary<string, object> { { "type", "text" }, { "text", text } };
            if (height > 0f)
            {
                item["height"] = (double)height;
            }

            if (font != null)
            {
                item["font"] = font;
            }

            return item;
        }

        private static Dictionary<string, object> Shield()
        {
            return new Dictionary<string, object> { { "type", "shield" }, { "network", "I" }, { "number", "95" }, { "height", 36.0 } };
        }

        // ------------------------------------------------------------------------------ changes

        private void CommitText()
        {
            if (_commitAt < 0f)
            {
                return;
            }

            _commitAt = -1f;
            Gantry g = GantryManager.Find(_gantryId);
            if (g != null)
            {
                Commit(g, "sign text");
            }
        }

        private void Commit(Gantry g, string what)
        {
            _commitAt = -1f;
            string design = Json.Write(_doc);
            if (design.Length > GuideData.MaxDesignLength)
            {
                _rebuild = true; // too long to keep: show the sign as it was
                return;
            }

            Change(g, what, h =>
            {
                h.Design = design;
                return h;
            });
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
                GuideHead now;
                if (GantryManager.GetGuideSign(g, _head, out now))
                {
                    _shown = now;
                }
            }
            catch (Exception e)
            {
                Log.Error("Guide sign change failed", e);
            }
        }

        // ------------------------------------------------------------------------------ controls

        private UITextField Field(UIPanel row, float x, float y, float w, string text, int maxLength, Gantry g, Action<string> apply)
        {
            UITextField field = UIUtils.CreateTextField(row, w, maxLength);
            field.relativePosition = new Vector3(x, y);
            field.text = text;
            field.selectOnFocus = true;
            field.submitOnFocusLost = true;
            // Only what's really been changed counts: clicking away from the box changes nothing, and marks nothing as edited.
            string seen = text;
            field.eventTextChanged += (c, v) =>
            {
                if (!_building && v != seen)
                {
                    seen = v;
                    apply(v);
                    _commitAt = Time.realtimeSinceStartup + TextCommitDelay;
                }
            };
            field.eventTextSubmitted += (c, v) =>
            {
                if (!_building && v != seen)
                {
                    seen = v;
                    apply(v);
                    _commitAt = -1f;
                    Commit(g, "sign text");
                }
            };
            return field;
        }

        private UIDropDown Dropdown(UIPanel row, float x, float w, string[] labels, int selected, Action<int> onChange)
        {
            return Dropdown(row, x, 2f, w, labels, selected, onChange);
        }

        private UIDropDown Dropdown(UIPanel row, float x, float y, float w, string[] labels, int selected, Action<int> onChange)
        {
            UIDropDown drop = UIUtils.CreateDropDown(row, w);
            drop.relativePosition = new Vector3(x, y);
            drop.items = labels;
            drop.listHeight = Mathf.Min(480, labels.Length * drop.itemHeight + 8);
            drop.selectedIndex = selected;
            drop.eventSelectedIndexChanged += (c, index) =>
            {
                if (!_building)
                {
                    onChange(index);
                }
            };
            DropdownToggle.Attach(drop);
            return drop;
        }

        private void Fit(UIDropDown drop)
        {
            UIComponent row = drop.parent;
            row.width = _content.width;
            drop.width = _content.width - 16f;
            DropdownToggle.Attach(drop);
        }

        private static UIButton RowButton(UIPanel row, float x, float w, string text, string tooltip, Action onClick)
        {
            var b = row.AddUIComponent<UIButton>();
            b.text = text;
            b.tooltip = tooltip;
            b.textScale = 0.75f;
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.disabledBgSprite = "ButtonMenuDisabled";
            b.size = new Vector2(w, 28f);
            b.relativePosition = new Vector3(x, 4f);
            b.eventClick += (c, p) => onClick();
            return b;
        }
    }
}
