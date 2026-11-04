using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Detect;
using GantryTool.Guide;
using GantryTool.Render;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// A to-scale picture of one face of a gantry, as its drivers see it: the lanes along the bottom with the signs on that
    /// face above them. Drag the sign being edited along the lanes, or by its edges to cover more or fewer; click another
    /// sign to edit that one.
    /// </summary>
    public sealed class GuideArrangeView : UIPanel
    {
        private const float Pad = 8f;
        private const float SignZone = 112f;
        private const float RoadHeight = 30f;
        private const float EdgeGrab = 9f;
        private const float HandleWidth = 5f;
        private const float OtherSignOpacity = 0.55f;
        private const float MinAlongChange = 0.02f;

        private enum Drag
        {
            None,
            Move,
            LeftEdge,
            RightEdge
        }

        private struct SignRect
        {
            public byte Id;
            public float X0, X1, Y0, Y1;
        }

        private Gantry _gantry;
        private int _head;
        private sbyte _side;
        private float _span, _scale, _x0, _roadY;
        private List<ArrangeLane> _facing;
        private Action<string, Func<GuideHead, GuideHead>> _change;
        private Action<int> _open;

        private readonly List<GuideHead> _signs = new List<GuideHead>();
        private readonly List<SignRect> _rects = new List<SignRect>();
        private readonly List<UITextureSprite> _pictures = new List<UITextureSprite>();
        private readonly UISprite[] _outline = new UISprite[4];
        private readonly UISprite[] _handles = new UISprite[2];

        private Drag _drag;
        private float _grab;
        private int _openNext = -1;

        public static GuideArrangeView Create(UIComponent parent, float width, Gantry g, int head, sbyte side,
            Action<string, Func<GuideHead, GuideHead>> change, Action<int> open)
        {
            var view = parent.AddUIComponent<GuideArrangeView>();
            view.Setup(width, g, head, side, change, open);
            return view;
        }

        private void Setup(float viewWidth, Gantry g, int head, sbyte side, Action<string, Func<GuideHead, GuideHead>> change, Action<int> open)
        {
            _gantry = g;
            _head = head;
            _side = side;
            _change = change;
            _open = open;
            _span = Mathf.Max(1f, g.Span);
            size = new Vector2(viewWidth, SignZone + RoadHeight + 2f * Pad);
            backgroundSprite = "GenericPanel";
            color = new Color32(60, 66, 74, 255);
            clipChildren = true;
            _roadY = Pad + SignZone;

            List<ArrangeLane> lanes;
            float tallest = 1f;
            lock (GantryManager.Lock)
            {
                lanes = GuideArrange.Lanes(g.Lanes, _span, side);
                _signs.AddRange(g.GuideSigns);
            }

            foreach (GuideHead h in _signs)
            {
                float along, w, signHeight;
                if (GuideMeshBuilder.Layout(g, h, out along, out w, out signHeight))
                {
                    tallest = Mathf.Max(tallest, signHeight);
                }
            }

            _facing = GuideArrange.Facing(lanes);
            _scale = Mathf.Min((viewWidth - 2f * Pad) / _span, (SignZone - 6f) / tallest);
            _x0 = (viewWidth - _span * _scale) * 0.5f;

            Block(_x0, _roadY, _span * _scale, RoadHeight, new Color32(32, 35, 39, 255));
            Block(_x0 - 3f, Pad, 3f, SignZone + RoadHeight, new Color32(170, 170, 165, 255));
            Block(_x0 + _span * _scale, Pad, 3f, SignZone + RoadHeight, new Color32(170, 170, 165, 255));

            int number = 0;
            foreach (ArrangeLane l in lanes)
            {
                float x = _x0 + l.X0 * _scale, w = (l.X1 - l.X0) * _scale;
                Block(x + 1f, _roadY + 2f, Mathf.Max(1f, w - 2f), RoadHeight - 4f,
                    l.Facing ? new Color32(104, 110, 118, 255) : new Color32(52, 56, 62, 255));
                if (l.Facing)
                {
                    number++;
                    UILabel label = UIUtils.CreateLabel(this, number.ToString(), 0.7f);
                    label.isInteractive = false;
                    label.relativePosition = new Vector3(x + w * 0.5f - 4f, _roadY + 8f);
                }
            }

            for (int i = 0; i < _outline.Length; i++)
            {
                _outline[i] = Block(0f, 0f, 1f, 1f, new Color32(255, 210, 40, 255));
            }

            for (int i = 0; i < _handles.Length; i++)
            {
                _handles[i] = Block(0f, 0f, 1f, 1f, new Color32(255, 210, 40, 255));
            }

            eventMouseDown += (c, p) => Press(p);
            Refresh();
        }

        private UISprite Block(float x, float y, float w, float h, Color32 colour)
        {
            var s = AddUIComponent<UISprite>();
            s.spriteName = "EmptySprite";
            s.color = colour;
            s.isInteractive = false;
            s.size = new Vector2(w, h);
            s.relativePosition = new Vector3(x, y);
            return s;
        }

        public override void Update()
        {
            base.Update();
            if (_gantry == null)
            {
                return;
            }

            if (_openNext >= 0)
            {
                int next = _openNext;
                _openNext = -1;
                _open(next);
                return;
            }

            if (_drag != Drag.None)
            {
                Dragging();
            }

            Refresh();
        }

        /// <summary>Puts the signs where they are now.</summary>
        private void Refresh()
        {
            _signs.Clear();
            lock (GantryManager.Lock)
            {
                foreach (GuideHead h in _gantry.GuideSigns)
                {
                    if (h.Side == _side)
                    {
                        _signs.Add(h);
                    }
                }
            }

            // The sign being edited last, on top.
            byte selectedId = HeadRef.GuideId(_head);
            _signs.Sort((a, b) => (a.Id == selectedId ? 1 : 0).CompareTo(b.Id == selectedId ? 1 : 0));

            while (_pictures.Count < _signs.Count)
            {
                var picture = AddUIComponent<UITextureSprite>();
                picture.isInteractive = false;
                _pictures.Add(picture);
                foreach (UISprite s in _outline)
                {
                    s.BringToFront();
                }

                foreach (UISprite s in _handles)
                {
                    s.BringToFront();
                }
            }

            _rects.Clear();
            bool shown = false;
            bool fitted = false;
            SignRect mine = default(SignRect);
            for (int i = 0; i < _pictures.Count; i++)
            {
                UITextureSprite picture = _pictures[i];
                float along, w, h;
                if (i >= _signs.Count || !GuideMeshBuilder.Layout(_gantry, _signs[i], out along, out w, out h))
                {
                    picture.isVisible = false;
                    continue;
                }

                GuideCacheEntry entry = GuideCache.Get(GuideMeshBuilder.Place(_gantry, _signs[i]).Design);
                var rect = new SignRect { Id = _signs[i].Id };
                rect.X0 = _x0 + (GuideArrange.ViewX(along, _span, _side) - w * 0.5f) * _scale;
                rect.X1 = rect.X0 + w * _scale;
                rect.Y1 = _roadY - 2f;
                rect.Y0 = rect.Y1 - h * _scale;
                _rects.Add(rect);

                // While a change is being drawn, the picture it had stands in.
                picture.texture = entry != null ? entry.Texture : picture.texture;
                picture.isVisible = picture.texture != null;
                picture.size = new Vector2(rect.X1 - rect.X0, rect.Y1 - rect.Y0);
                picture.relativePosition = new Vector3(rect.X0, rect.Y0);
                picture.opacity = _signs[i].Id == selectedId ? 1f : OtherSignOpacity;

                if (_signs[i].Id == selectedId)
                {
                    shown = true;
                    mine = rect;
                    fitted = _signs[i].FitFirst > 0 && _facing.Count > 0;
                }
            }

            PlaceOutline(mine, shown, fitted);
        }

        private void PlaceOutline(SignRect r, bool shown, bool fitted)
        {
            foreach (UISprite s in _outline)
            {
                s.isVisible = shown;
            }

            foreach (UISprite s in _handles)
            {
                s.isVisible = shown && fitted;
            }

            if (!shown)
            {
                return;
            }

            const float Line = 2f;
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            Set(_outline[0], r.X0, r.Y0 - Line, w, Line);
            Set(_outline[1], r.X0, r.Y1, w, Line);
            Set(_outline[2], r.X0 - Line, r.Y0 - Line, Line, h + 2f * Line);
            Set(_outline[3], r.X1, r.Y0 - Line, Line, h + 2f * Line);
            float middle = r.Y0 + h * 0.5f - 12f;
            Set(_handles[0], r.X0 - HandleWidth, middle, HandleWidth, 24f);
            Set(_handles[1], r.X1, middle, HandleWidth, 24f);
        }

        private static void Set(UISprite s, float x, float y, float w, float h)
        {
            s.size = new Vector2(w, h);
            s.relativePosition = new Vector3(x, y);
        }

        private void Press(UIMouseEventParameter p)
        {
            Vector2 at;
            if ((p.buttons & UIMouseButton.Left) == 0 || !GetHitPosition(p.ray, out at))
            {
                return;
            }

            byte selectedId = HeadRef.GuideId(_head);
            GuideHead sign;
            if (!GantryManager.GetGuideSign(_gantry, _head, out sign))
            {
                return;
            }

            // The sign being edited first, then the others from the top down.
            for (int i = _rects.Count - 1; i >= 0; i--)
            {
                SignRect r = _rects[i];
                bool selected = r.Id == selectedId;
                float grab = selected ? EdgeGrab : 0f;
                if (at.x < r.X0 - grab || at.x > r.X1 + grab || at.y < r.Y0 || at.y > r.Y1)
                {
                    continue;
                }

                if (!selected)
                {
                    _openNext = HeadRef.OfGuide(r.Id);
                    return;
                }

                bool fitted = sign.FitFirst > 0 && _facing.Count > 0;
                if (fitted && at.x <= r.X0 + EdgeGrab)
                {
                    _drag = Drag.LeftEdge;
                }
                else if (fitted && at.x >= r.X1 - EdgeGrab)
                {
                    _drag = Drag.RightEdge;
                }
                else
                {
                    _drag = Drag.Move;
                    _grab = at.x - (r.X0 + r.X1) * 0.5f;
                }

                return;
            }
        }

        private void Dragging()
        {
            if (!Input.GetMouseButton(0))
            {
                _drag = Drag.None;
                return;
            }

            // The sum uses the mouse's sideways position only, so the drag carries on if it strays above or below the view.
            Camera camera = GetUIView().uiCamera;
            Vector3 mouse = Input.mousePosition;
            mouse.y = camera.WorldToScreenPoint(GetCenter()).y;
            Vector2 at;
            GuideHead sign;
            if (!GetHitPosition(camera.ScreenPointToRay(mouse), out at))
            {
                return;
            }

            if (!GantryManager.GetGuideSign(_gantry, _head, out sign))
            {
                return;
            }

            float metres = (at.x - _x0) / _scale;
            if (sign.FitFirst > 0 && _facing.Count > 0)
            {
                DragFitted(sign, metres);
            }
            else if (_drag == Drag.Move)
            {
                float view = GuideArrange.SnapToLane(_facing, metres - _grab / _scale);
                float along = Mathf.Clamp(GuideArrange.ViewX(view, _span, _side), 0f, _span);
                if (Mathf.Abs(along - sign.Along) >= MinAlongChange)
                {
                    _change("sign position", h =>
                    {
                        h.Along = along;
                        return h;
                    });
                }
            }
        }

        private void DragFitted(GuideHead sign, float metres)
        {
            int count = _facing.Count;
            int first = Mathf.Clamp(sign.FitFirst - 1, 0, count - 1);
            int last = Mathf.Clamp(sign.FitLast - 1, first, count - 1);
            int oldFirst = first, oldLast = last;
            switch (_drag)
            {
                case Drag.Move:
                    GuideArrange.Move(_facing, metres - _grab / _scale, ref first, ref last);
                    break;
                case Drag.LeftEdge:
                    first = GuideArrange.LeftEdge(_facing, metres, last);
                    break;
                case Drag.RightEdge:
                    last = GuideArrange.RightEdge(_facing, metres, first);
                    break;
            }

            if (first != oldFirst || last != oldLast)
            {
                byte newFirst = (byte)(first + 1), newLast = (byte)(last + 1);
                _change("sign lanes", h =>
                {
                    h.FitFirst = newFirst;
                    h.FitLast = newLast;
                    return h;
                });
            }
        }
    }
}
