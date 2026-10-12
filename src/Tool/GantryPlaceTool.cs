using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using GantryTool.Data;
using GantryTool.Detect;
using GantryTool.Render;
using GantryTool.UI;
using UnityEngine;

namespace GantryTool.Tool
{
    /// <summary>
    /// The gantry tool: placing and editing in one mode.
    /// - Click empty ground twice to place a gantry (like the road tool).
    /// - Click a gantry's truss or posts to select it: handles appear (posts, middle, rotate) and the gantry
    ///   panel opens. Click a signal head to change its sign. Right-click a hovered gantry to delete it.
    /// - Ctrl+Z undoes placing, deleting, moving, settings and sign changes.
    /// - Right-click / Esc cancel the current step (drag, first post, selection), then leave the tool.
    ///
    /// Threading follows NetTool: the mouse ray is captured on the main thread (OnToolLateUpdate) and raycast
    /// on the simulation thread (SimulationStep).
    /// </summary>
    public sealed class GantryPlaceTool : ToolBase
    {
        /// <summary>The live tool instance while a city is loaded (used by the Esc patch).</summary>
        public static GantryPlaceTool Instance;

        private static readonly Color ValidColor = new Color(0.25f, 0.85f, 1f, 0.8f);
        private static readonly Color InvalidColor = new Color(1f, 0.25f, 0.2f, 0.8f);
        private static readonly Color ValidTint = new Color(0.55f, 0.9f, 1f, 1f);
        private static readonly Color InvalidTint = new Color(1f, 0.45f, 0.4f, 1f);
        private static readonly Color HandleColor = new Color(0.25f, 0.85f, 1f, 0.75f);
        private static readonly Color HandleHotColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color HeadHoverColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color PlusSideColor = new Color(0.2f, 1f, 0.3f, 0.9f);
        private static readonly Color MinusSideColor = new Color(1f, 0.6f, 0.1f, 0.9f);

        private const float PostHandleRadius = 2.5f;
        private const float MiddleHandleRadius = 2f;
        private const float RotateHandleRadius = 1.8f;
        private const float RotateHandleDistance = 8f;

        private enum Handle
        {
            None,
            StartPost,
            EndPost,
            Middle,
            Rotate
        }

        /// <summary>Per-lane signal editor; set by Loading.</summary>
        public SignalPanel Panel;

        /// <summary>Per-gantry settings; set by Loading.</summary>
        public GantryPanel GantryPanel;

        private Ray _mouseRay;
        private float _mouseRayLength;
        private volatile bool _mouseRayValid;

        // Written on the simulation thread, read on the main thread. A torn Vector3 read only causes a
        // one-frame preview glitch, never a wrong placement (placement re-reads after the click).
        private Vector3 _hitPos;
        private volatile bool _hitValid;

        // True when the hit is on a road surface (bridge deck, elevated road, street) rather than terrain.
        private volatile bool _hitOnRoad;

        // Placement
        private bool _hasStart;
        private Vector3 _start;
        private Mesh _previewMesh;
        private Vector3 _previewOrigin;
        private Vector3 _previewEnd;
        private bool _previewBuilt;
        private Mesh _previewSignals;
        private static readonly List<SignalOverride> NoOverrides = new List<SignalOverride>();
        private readonly List<Vector3> _previewLanePoints = new List<Vector3>();
        private readonly List<int> _previewLaneSigns = new List<int>();

        // Hover / selection
        private int _hoveredId;
        private int _headGantryId;
        private int _headLane = -1;
        private int _selectedId;
        private Handle _hoverHandle;

        // Dragging a handle
        private Handle _dragHandle;
        private Vector3 _dragStartHit;
        private Vector3 _dragOrigStart;
        private Vector3 _dragOrigEnd;
        private bool _dragStartOnDeck;
        private bool _dragEndOnDeck;
        private bool _dragInvalid;

        // Vertical probes for posts standing on a road deck while moving/rotating: requested on the main thread,
        // answered on the simulation thread (where raycasts belong). [0] = start post, [1] = end post.
        private readonly Vector3[] _probeFrom = new Vector3[2];
        private readonly float[] _probeY = new float[2];
        private readonly bool[] _probeWanted = new bool[2];
        private volatile int _probeRequest;
        private volatile int _probeDone;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _hasStart = false;
            _hitValid = false;
            _dragHandle = Handle.None;
            SetHovered(0);
            Log.Debug("Tool enabled");
        }

        protected override void OnDisable()
        {
            if (_dragHandle != Handle.None)
            {
                CancelDrag();
            }

            base.OnDisable();
            _hasStart = false;
            _previewBuilt = false;
            SetHovered(0);
            _headGantryId = 0;
            _headLane = -1;
            Deselect();
            if (Panel != null)
            {
                Panel.Hide();
            }

            Log.Debug("Tool disabled");
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            GantryRenderer.HoveredId = 0;
            GantryRenderer.SelectedId = 0;
            if (_previewMesh != null)
            {
                Destroy(_previewMesh);
                _previewMesh = null;
            }

            if (_previewSignals != null)
            {
                Destroy(_previewSignals);
                _previewSignals = null;
            }

            base.OnDestroy();
        }

        protected override void OnToolLateUpdate()
        {
            base.OnToolLateUpdate();
            try
            {
                _mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
                _mouseRayLength = Camera.main.farClipPlane;
                _mouseRayValid = !UIView.IsInsideUI() && Cursor.visible;
            }
            catch (Exception e)
            {
                _mouseRayValid = false;
                Log.Error("OnToolLateUpdate failed", e);
            }
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();
            try
            {
                if (_dragHandle != Handle.None)
                {
                    if (!Input.GetMouseButton(0))
                    {
                        EndDrag();
                    }
                    else
                    {
                        UpdateDrag();
                    }
                }
                else
                {
                    UpdateHover();
                }

                UpdateCursorInfo();
            }
            catch (Exception e)
            {
                SetHovered(0);
                Log.Error("Tool update failed", e);
            }
        }

        private void UpdateHover()
        {
            _hoverHandle = Handle.None;
            if (_hasStart || !_mouseRayValid)
            {
                SetHovered(0);
                _headGantryId = 0;
                _headLane = -1;
                return;
            }

            Gantry selected = SelectedGantry();
            if (selected != null && _hitValid)
            {
                _hoverHandle = HandleAt(selected, _hitPos);
            }

            if (_hoverHandle != Handle.None)
            {
                SetHovered(0);
                _headGantryId = 0;
                _headLane = -1;
                return;
            }

            SetHovered(GantryPicker.Pick(_mouseRay, _mouseRayLength));
            if (!GantryPicker.PickHead(_mouseRay, _mouseRayLength, out _headGantryId, out _headLane))
            {
                _headLane = -1;
            }
        }

        private void SetHovered(int id)
        {
            _hoveredId = id;
            GantryRenderer.HoveredId = id;
        }

        public override void SimulationStep()
        {
            base.SimulationStep();
            try
            {
                RunProbes();

                if (!_mouseRayValid)
                {
                    _hitValid = false;
                    return;
                }

                RaycastOutput output;
                if (RayCast(RoadAndTerrainInput(_mouseRay, _mouseRayLength), out output))
                {
                    _hitPos = output.m_hitPos;
                    _hitOnRoad = output.m_netSegment != 0 || output.m_netNode != 0;
                    _hitValid = true;
                }
                else
                {
                    _hitValid = false;
                }
            }
            catch (Exception e)
            {
                _hitValid = false;
                Log.Error("SimulationStep raycast failed", e);
            }
        }

        /// <summary>
        /// Terrain plus road surfaces. Including roads means a click on a bridge deck lands on the deck instead of
        /// passing through to the riverbed below (ToolBase.RayCast only tests nets when the ignore flags aren't All;
        /// the nearer of terrain/road wins).
        /// </summary>
        private static RaycastInput RoadAndTerrainInput(Ray ray, float length)
        {
            return new RaycastInput(ray, length)
            {
                m_netService = new RaycastService(ItemClass.Service.Road, ItemClass.SubService.None, ItemClass.Layer.Default),
                m_ignoreSegmentFlags = NetSegment.Flags.None,
                m_ignoreNodeFlags = NetNode.Flags.None
            };
        }

        /// <summary>Simulation thread: straight-down raycasts for deck posts being moved.</summary>
        private void RunProbes()
        {
            int request = _probeRequest;
            if (request == _probeDone)
            {
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (!_probeWanted[i])
                {
                    continue;
                }

                Vector3 from = _probeFrom[i];
                RaycastOutput output;
                if (RayCast(RoadAndTerrainInput(new Ray(from, Vector3.down), 400f), out output))
                {
                    _probeY[i] = output.m_hitPos.y;
                }
            }

            _probeDone = request;
        }

        protected override void OnToolGUI(Event e)
        {
            base.OnToolGUI(e);
            try
            {
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command))
                {
                    UndoLast();
                    e.Use();
                    return;
                }

                if (e.type == EventType.MouseDown && !UIView.IsInsideUI())
                {
                    if (e.button == 0)
                    {
                        OnLeftClick();
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        OnRightClick();
                        e.Use();
                    }
                }
                // Esc is intercepted earlier by GameKeyShortcuts; see Patches/EscapePatch.
            }
            catch (Exception ex)
            {
                Log.Error("OnToolGUI failed", ex);
            }
        }

        private void OnLeftClick()
        {
            if (_dragHandle != Handle.None)
            {
                return;
            }

            // Second click of a placement.
            if (_hasStart)
            {
                PlaceSecondPoint();
                return;
            }

            // Grab a handle of the selected gantry.
            if (_hoverHandle != Handle.None && SelectedGantry() != null)
            {
                BeginDrag(_hoverHandle);
                return;
            }

            // Click a signal head: select its gantry and open the signal editor.
            if (_headGantryId != 0 && _headLane >= 0)
            {
                Gantry edited = GantryManager.Find(_headGantryId);
                if (edited != null)
                {
                    Select(edited, openPanel: false);
                    if (Panel != null)
                    {
                        Log.Debug("Editing signal: gantry #" + edited.Id + " lane " + _headLane);
                        Panel.ShowFor(edited, _headLane);
                    }
                }

                return;
            }

            // Click a gantry: select it and open the gantry panel.
            if (_hoveredId != 0)
            {
                Gantry g = GantryManager.Find(_hoveredId);
                if (g != null)
                {
                    Select(g, openPanel: true);
                }

                return;
            }

            // Click empty ground with a gantry selected: just deselect (the next click starts a gantry).
            if (_selectedId != 0)
            {
                Deselect();
                return;
            }

            // Click empty ground: first post of a new gantry.
            if (_hitValid)
            {
                _start = GroundPoint(_hitPos, _hitOnRoad);
                _hasStart = true;
                _previewBuilt = false;
                Log.Debug("First point set at " + _start);
            }
        }

        private void PlaceSecondPoint()
        {
            if (!_hitValid)
            {
                return;
            }

            Vector3 pos = GroundPoint(_hitPos, _hitOnRoad);
            float span = FlatDistance(_start, pos);
            if (!SpanValid(span))
            {
                Log.Info("Placement rejected: span " + span.ToString("F1") + "m outside " +
                         GantryManager.MinSpan + "-" + GantryManager.MaxSpan + "m");
                return;
            }

            Gantry g = GantryManager.Add(_start, pos, GantryManager.DefaultHeight, GantryManager.DefaultBeamDepth);
            lock (GantryManager.Lock)
            {
                g.HeightAboveRoad = GantryManager.DefaultHeight;
            }

            UndoStack.PushAdded(g);
            Log.Info("Placed gantry " + g);
            _hasStart = false;
            _previewBuilt = false;
        }

        private void OnRightClick()
        {
            if (_dragHandle != Handle.None)
            {
                CancelDrag();
                return;
            }

            if (!_hasStart && _hoveredId != 0)
            {
                DeleteGantry(_hoveredId);
                return;
            }

            CancelOrExit();
        }

        /// <summary>Esc / right-click: cancel the drag, the first post or the selection, else leave the tool.</summary>
        public void CancelOrExit()
        {
            if (_dragHandle != Handle.None)
            {
                CancelDrag();
            }
            else if (_hasStart)
            {
                _hasStart = false;
                _previewBuilt = false;
                Log.Info("First point cancelled");
            }
            else if (_selectedId != 0)
            {
                Deselect();
            }
            else
            {
                ToolsModifierControl.SetTool<DefaultTool>();
            }
        }

        // ------------------------------------------------------------------------------ selection & actions

        private Gantry SelectedGantry()
        {
            return _selectedId == 0 ? null : GantryManager.Find(_selectedId);
        }

        private void Select(Gantry g, bool openPanel)
        {
            _selectedId = g.Id;
            GantryRenderer.SelectedId = g.Id;
            if (Panel != null && Panel.isVisible && !Panel.IsShowing(g.Id))
            {
                Panel.Hide();
            }

            if (GantryPanel != null && (openPanel || GantryPanel.isVisible))
            {
                GantryPanel.ShowFor(g);
            }
        }

        public void Deselect()
        {
            _selectedId = 0;
            GantryRenderer.SelectedId = 0;
            _hoverHandle = Handle.None;
            if (GantryPanel != null && GantryPanel.isVisible)
            {
                GantryPanel.Hide();
            }
        }

        /// <summary>Deletes a gantry with an undo step (right-click or the gantry panel).</summary>
        public void DeleteGantry(int id)
        {
            Gantry g = GantryManager.Find(id);
            if (g == null)
            {
                return;
            }

            UndoStack.PushDeleted(g);
            SetHovered(0);
            if (Panel != null && Panel.IsShowing(id))
            {
                Panel.Hide();
            }

            if (_selectedId == id)
            {
                Deselect();
            }

            if (GantryManager.Remove(id))
            {
                Log.Info("Deleted gantry #" + id + " (" + GantryManager.Count + " left)");
            }
        }

        /// <summary>Ctrl+Z or the panel's Undo button.</summary>
        public void UndoLast()
        {
            if (_dragHandle != Handle.None)
            {
                return;
            }

            string what = UndoStack.Undo();
            if (what == null)
            {
                Log.Debug("Nothing to undo");
                return;
            }

            Log.Info("Undo: " + what);
            if (_selectedId != 0 && GantryManager.Find(_selectedId) == null)
            {
                Deselect();
            }

            if (GantryPanel != null)
            {
                GantryPanel.Rebuild();
            }

            if (Panel != null && Panel.isVisible)
            {
                Panel.Hide();
            }
        }

        // ------------------------------------------------------------------------------ handles & dragging

        private static Vector3 Middle(Gantry g)
        {
            return (g.Start + g.End) * 0.5f;
        }

        private static Vector3 RotateHandlePos(Gantry g)
        {
            Vector3 d = g.End - g.Start;
            d.y = 0f;
            Vector3 side = d.sqrMagnitude > 1e-4f ? new Vector3(-d.z, 0f, d.x).normalized : Vector3.forward;
            return Middle(g) + side * RotateHandleDistance;
        }

        private static Handle HandleAt(Gantry g, Vector3 p)
        {
            Handle best = Handle.None;
            float bestD = float.MaxValue;
            Consider(ref best, ref bestD, Handle.StartPost, FlatDistance(p, g.Start), PostHandleRadius);
            Consider(ref best, ref bestD, Handle.EndPost, FlatDistance(p, g.End), PostHandleRadius);
            Consider(ref best, ref bestD, Handle.Middle, FlatDistance(p, Middle(g)), MiddleHandleRadius);
            Consider(ref best, ref bestD, Handle.Rotate, FlatDistance(p, RotateHandlePos(g)), RotateHandleRadius);
            return best;
        }

        private static void Consider(ref Handle best, ref float bestD, Handle h, float d, float radius)
        {
            if (d <= radius && d < bestD)
            {
                best = h;
                bestD = d;
            }
        }

        private void BeginDrag(Handle handle)
        {
            Gantry g = SelectedGantry();
            if (g == null || !_hitValid)
            {
                return;
            }

            UndoStack.PushModified(g, "move", coalesce: false);
            _dragHandle = handle;
            _dragStartHit = _hitPos;
            _dragOrigStart = g.Start;
            _dragOrigEnd = g.End;
            _dragStartOnDeck = OnDeck(g.Start);
            _dragEndOnDeck = OnDeck(g.End);
            _dragInvalid = false;
            _probeY[0] = g.Start.y;
            _probeY[1] = g.End.y;
            GantryRenderer.QuietId = g.Id;
        }

        private void UpdateDrag()
        {
            Gantry g = SelectedGantry();
            if (g == null)
            {
                _dragHandle = Handle.None;
                GantryRenderer.QuietId = 0;
                return;
            }

            if (!_hitValid)
            {
                return;
            }

            Vector3 newStart = _dragOrigStart;
            Vector3 newEnd = _dragOrigEnd;
            switch (_dragHandle)
            {
                case Handle.StartPost:
                    newStart = GroundPoint(_hitPos, _hitOnRoad);
                    newEnd = g.End;
                    break;
                case Handle.EndPost:
                    newStart = g.Start;
                    newEnd = GroundPoint(_hitPos, _hitOnRoad);
                    break;
                case Handle.Middle:
                {
                    Vector3 delta = _hitPos - _dragStartHit;
                    delta.y = 0f;
                    newStart = PostAt(_dragOrigStart + delta, _dragOrigStart, _dragStartOnDeck, 0);
                    newEnd = PostAt(_dragOrigEnd + delta, _dragOrigEnd, _dragEndOnDeck, 1);
                    break;
                }

                case Handle.Rotate:
                {
                    Vector3 mid = (_dragOrigStart + _dragOrigEnd) * 0.5f;
                    float a0 = Mathf.Atan2(_dragStartHit.z - mid.z, _dragStartHit.x - mid.x);
                    float a1 = Mathf.Atan2(_hitPos.z - mid.z, _hitPos.x - mid.x);
                    float angle = a1 - a0;
                    newStart = PostAt(RotateXZ(_dragOrigStart, mid, angle), _dragOrigStart, _dragStartOnDeck, 0);
                    newEnd = PostAt(RotateXZ(_dragOrigEnd, mid, angle), _dragOrigEnd, _dragEndOnDeck, 1);
                    break;
                }
            }

            _dragInvalid = !SpanValid(FlatDistance(newStart, newEnd));
            if (_dragInvalid)
            {
                return; // keep the last valid position
            }

            if ((newStart - g.Start).sqrMagnitude > 0.0025f || (newEnd - g.End).sqrMagnitude > 0.0025f)
            {
                GantryManager.SetEnds(g, newStart, newEnd);
            }
        }

        /// <summary>
        /// Where a moved/rotated post stands. Posts on the ground follow the terrain; posts on a road deck stay on
        /// whatever surface is under them near their old height (from a straight-down probe), so a gantry moved
        /// along a bridge stays on the deck and one moved off it drops to the ground.
        /// </summary>
        private Vector3 PostAt(Vector3 target, Vector3 original, bool onDeck, int probe)
        {
            if (!onDeck)
            {
                target.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(target);
                return target;
            }

            _probeFrom[probe] = new Vector3(target.x, original.y + 3f, target.z);
            _probeWanted[probe] = true;
            _probeRequest++;
            target.y = _probeY[probe];
            return target;
        }

        private void EndDrag()
        {
            Gantry g = SelectedGantry();
            Handle was = _dragHandle;
            _dragHandle = Handle.None;
            _probeWanted[0] = _probeWanted[1] = false;
            GantryRenderer.QuietId = 0;
            if (g == null)
            {
                return;
            }

            UndoStack.DiscardIfUnchanged(g);
            if ((g.Start - _dragOrigStart).sqrMagnitude > 1e-6f || (g.End - _dragOrigEnd).sqrMagnitude > 1e-6f)
            {
                Log.Info("Moved gantry " + g + " (" + was + ")");
                if (GantryPanel != null)
                {
                    GantryPanel.Rebuild();
                }
            }
        }

        private void CancelDrag()
        {
            Gantry g = SelectedGantry();
            _dragHandle = Handle.None;
            _probeWanted[0] = _probeWanted[1] = false;
            GantryRenderer.QuietId = 0;
            if (g != null)
            {
                GantryManager.SetEnds(g, _dragOrigStart, _dragOrigEnd);
                UndoStack.DiscardIfUnchanged(g);
                Log.Debug("Drag cancelled");
            }
        }

        private static bool OnDeck(Vector3 post)
        {
            return post.y > Singleton<TerrainManager>.instance.SampleRawHeightSmooth(post) + 0.5f;
        }

        private static Vector3 RotateXZ(Vector3 p, Vector3 centre, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float x = p.x - centre.x, z = p.z - centre.z;
            return new Vector3(centre.x + x * c - z * s, p.y, centre.z + x * s + z * c);
        }

        // ------------------------------------------------------------------------------ cursor info

        private void UpdateCursorInfo()
        {
            string text = null;
            Gantry selected = SelectedGantry();

            if (_dragHandle != Handle.None && selected != null)
            {
                text = "Span " + selected.Span.ToString("F1") + " m" + (_dragInvalid ? "\nSpan must be 6–80 m" : "");
            }
            else if (_hasStart && _hitValid)
            {
                float span = FlatDistance(_start, GroundPoint(_hitPos, _hitOnRoad));
                text = "Span " + span.ToString("F1") + " m" + (SpanValid(span) ? "" : "\nSpan must be 6–80 m");
            }
            else if (_hoverHandle == Handle.StartPost || _hoverHandle == Handle.EndPost)
            {
                text = "Drag to move this post";
            }
            else if (_hoverHandle == Handle.Middle)
            {
                text = "Drag to move the gantry";
            }
            else if (_hoverHandle == Handle.Rotate)
            {
                text = "Drag to rotate";
            }
            else if (_headGantryId != 0 && _headLane >= 0)
            {
                text = "Click to change this signal";
            }
            else if (_hoveredId != 0)
            {
                text = "Click to edit · Right-click to delete";
            }

            if (text != null && _mouseRayValid)
            {
                ShowToolInfo(true, text, _hitPos);
            }
            else
            {
                ShowToolInfo(false, null, Vector3.zero);
            }
        }

        // ------------------------------------------------------------------------------ rendering

        public override void RenderGeometry(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderGeometry(cameraInfo);
            try
            {
                if (!_hasStart || !_hitValid)
                {
                    _previewLanePoints.Clear();
                    _previewLaneSigns.Clear();
                    return;
                }

                Vector3 end = GroundPoint(_hitPos, _hitOnRoad);
                float span = FlatDistance(_start, end);
                if (span < 1f)
                {
                    return;
                }

                // Rebuild the preview only when the mouse has actually moved.
                if (!_previewBuilt || (end - _previewEnd).sqrMagnitude > 0.0025f)
                {
                    // Scan roads so the preview shows the real beam height (no logging per frame).
                    LaneScanner.Result scan = LaneScanner.Scan(_start, end, verbose: false);
                    _previewLanePoints.Clear();
                    _previewLaneSigns.Clear();
                    for (int i = 0; i < scan.Lanes.Count; i++)
                    {
                        Vector3 p = _start + (end - _start) * (scan.Lanes[i].Offset / span);
                        p.y = scan.Lanes[i].SurfaceY;
                        _previewLanePoints.Add(p);
                        _previewLaneSigns.Add(scan.Lanes[i].TravelSign);
                    }

                    float bottom = GantryHeight.BeamBottom(Mathf.Max(_start.y, end.y), scan.RoadTopY, GantryManager.DefaultHeight);
                    _previewMesh = GantryMeshBuilder.Build(_start, end, bottom,
                        GantryManager.DefaultBeamDepth, _previewMesh, out _previewOrigin);
                    _previewSignals = SignalMeshBuilder.Build(_start, end, _previewOrigin, bottom,
                        GantryManager.DefaultBeamDepth, scan.Lanes, NoOverrides, SignalMeshBuilder.DefaultSignalSize,
                        _previewSignals);
                    _previewEnd = end;
                    _previewBuilt = true;
                }

                GantryRenderer.DrawStructure(_previewMesh, _previewOrigin, SpanValid(span) ? ValidTint : InvalidTint);
                GantryRenderer.DrawSignals(_previewSignals, _previewOrigin, SignStyles.Resolve((byte)SignStyle.Default));
            }
            catch (Exception e)
            {
                _previewBuilt = false;
                Log.Error("Preview render failed", e);
            }
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);
            try
            {
                RenderManager rm = Singleton<RenderManager>.instance;

                // Handles of the selected gantry.
                Gantry selected = SelectedGantry();
                if (selected != null && !_hasStart)
                {
                    DrawHandles(cameraInfo, rm, selected);
                }

                // Highlight the hovered signal head: overlays project onto any surface inside [minY, maxY].
                if (!_hasStart && _dragHandle == Handle.None && _headGantryId != 0 && _headLane >= 0)
                {
                    Gantry hg = GantryManager.Find(_headGantryId);
                    if (hg != null && _headLane < hg.Lanes.Count)
                    {
                        Vector3 c = SignalMeshBuilder.HeadCentre(hg.Start, hg.End, hg.BeamBottomY, hg.BeamDepth,
                            hg.Lanes[_headLane].Offset);
                        float half = SignalMeshBuilder.HeadSize(hg.SignalSize, hg.BeamDepth) * 0.5f;
                        rm.OverlayEffect.DrawCircle(cameraInfo, HeadHoverColor, c, half * 2.6f,
                            c.y - half - 0.05f, c.y + half + 0.05f, false, true);
                    }
                }

                if (!_hitValid || _dragHandle != Handle.None)
                {
                    return;
                }

                Vector3 hover = _hitPos;
                if (!_hasStart)
                {
                    if (_hoveredId == 0 && _hoverHandle == Handle.None && _headLane < 0)
                    {
                        rm.OverlayEffect.DrawCircle(cameraInfo, ValidColor, hover, 2f, -1f, 1280f, false, true);
                    }

                    return;
                }

                bool valid = SpanValid(FlatDistance(_start, hover));
                Color color = valid ? ValidColor : InvalidColor;
                rm.OverlayEffect.DrawCircle(cameraInfo, color, _start, 2f, -1f, 1280f, false, true);
                rm.OverlayEffect.DrawCircle(cameraInfo, color, hover, 2f, -1f, 1280f, false, true);
                rm.OverlayEffect.DrawSegment(cameraInfo, color, new Segment3(_start, hover), 1f, 0f, -1f, 1280f, false, true);

                // Detected lanes from the last preview scan: green = traffic towards +side, orange = -side.
                if (_previewBuilt)
                {
                    for (int i = 0; i < _previewLanePoints.Count; i++)
                    {
                        int sign = _previewLaneSigns[i];
                        Color c = sign > 0 ? PlusSideColor : sign < 0 ? MinusSideColor : Color.white;
                        rm.OverlayEffect.DrawCircle(cameraInfo, c, _previewLanePoints[i], 2.5f, -1f, 1280f, false, true);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Overlay render failed", e);
            }
        }

        private void DrawHandles(RenderManager.CameraInfo cameraInfo, RenderManager rm, Gantry g)
        {
            Handle hot = _dragHandle != Handle.None ? _dragHandle : _hoverHandle;
            RenderManager.CameraInfo cam = cameraInfo;

            rm.OverlayEffect.DrawCircle(cam, HandleCol(Handle.StartPost, hot), g.Start, PostHandleRadius * 2f, -1f, 1280f, false, true);
            rm.OverlayEffect.DrawCircle(cam, HandleCol(Handle.EndPost, hot), g.End, PostHandleRadius * 2f, -1f, 1280f, false, true);

            Vector3 mid = Middle(g);
            Vector3 rot = RotateHandlePos(g);
            rm.OverlayEffect.DrawSegment(cam, HandleColor, new Segment3(mid, rot), 0.3f, 0.6f, -1f, 1280f, false, true);

            Vector3 d = g.End - g.Start;
            d.y = 0f;
            Vector3 along = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right;
            Vector3 across = new Vector3(-along.z, 0f, along.x);
            float h = MiddleHandleRadius * 0.8f;
            var square = new Quad3(mid - along * h - across * h, mid + along * h - across * h,
                                   mid + along * h + across * h, mid - along * h + across * h);
            rm.OverlayEffect.DrawQuad(cam, HandleCol(Handle.Middle, hot), square, -1f, 1280f, false, true);
            rm.OverlayEffect.DrawCircle(cam, HandleCol(Handle.Rotate, hot), rot, RotateHandleRadius * 2f, -1f, 1280f, false, true);
        }

        private Color HandleCol(Handle h, Handle hot)
        {
            if (_dragInvalid && h == _dragHandle)
            {
                return InvalidColor;
            }

            return h == hot ? HandleHotColor : HandleColor;
        }

        // ------------------------------------------------------------------------------ helpers

        /// <summary>
        /// Where a post stands. Clicked on a road (e.g. a bridge deck): on the road surface, so the gantry sits on
        /// the bridge. Clicked on the ground: on the terrain, and the beam rises to clear any raised road it spans.
        /// </summary>
        private static Vector3 GroundPoint(Vector3 pos, bool onRoad)
        {
            if (!onRoad)
            {
                pos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(pos);
            }

            return pos;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static bool SpanValid(float span)
        {
            return span >= GantryManager.MinSpan && span <= GantryManager.MaxSpan;
        }
    }
}
