using System;
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
    /// Two-click gantry placement, like the road tool. Left-click sets the first post, left-click again
    /// places the gantry; right-click (or Esc) cancels the first point. With no first point set, the
    /// gantry under the cursor is highlighted red and right-click deletes it; otherwise right-click / Esc
    /// leaves the tool.
    ///
    /// Threading follows NetTool: the mouse ray is captured on the main thread (OnToolLateUpdate) and
    /// raycast on the simulation thread (SimulationStep).
    /// </summary>
    public sealed class GantryPlaceTool : ToolBase
    {
        /// <summary>The live tool instance while a city is loaded (used by the Esc patch).</summary>
        public static GantryPlaceTool Instance;

        private static readonly Color ValidColor = new Color(0.25f, 0.85f, 1f, 0.8f);
        private static readonly Color InvalidColor = new Color(1f, 0.25f, 0.2f, 0.8f);
        private static readonly Color ValidTint = new Color(0.55f, 0.9f, 1f, 1f);
        private static readonly Color InvalidTint = new Color(1f, 0.45f, 0.4f, 1f);

        private Ray _mouseRay;
        private float _mouseRayLength;
        private volatile bool _mouseRayValid;

        // Written on the simulation thread, read on the main thread. A torn Vector3 read only causes a
        // one-frame preview glitch, never a wrong placement (placement re-reads after the click).
        private Vector3 _hitPos;
        private volatile bool _hitValid;

        // True when the hit is on a road surface (bridge deck, elevated road, street) rather than terrain.
        private volatile bool _hitOnRoad;

        private bool _hasStart;
        private Vector3 _start;

        private Mesh _previewMesh;
        private Vector3 _previewOrigin;
        private Vector3 _previewEnd;
        private bool _previewBuilt;

        private int _hoveredId;
        private Mesh _previewSignals;
        private static readonly System.Collections.Generic.List<SignalOverride> NoOverrides =
            new System.Collections.Generic.List<SignalOverride>();

        // Signal head under the cursor (delete/edit mode only).
        private int _headGantryId;
        private int _headLane = -1;

        /// <summary>Per-lane signal editor; set by Loading.</summary>
        public SignalPanel Panel;
        private static readonly Color HeadHoverColor = new Color(1f, 1f, 1f, 0.6f);
        private readonly System.Collections.Generic.List<Vector3> _previewLanePoints = new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<int> _previewLaneSigns = new System.Collections.Generic.List<int>();
        private static readonly Color PlusSideColor = new Color(0.2f, 1f, 0.3f, 0.9f);
        private static readonly Color MinusSideColor = new Color(1f, 0.6f, 0.1f, 0.9f);

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
            SetHovered(0);
            Log.Debug("Tool enabled");
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _hasStart = false;
            _previewBuilt = false;
            SetHovered(0);
            _headGantryId = 0;
            _headLane = -1;
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
                // Delete-mode hover: only when not in the middle of placing a gantry.
                if (!_hasStart && _mouseRayValid)
                {
                    SetHovered(GantryPicker.Pick(_mouseRay, _mouseRayLength));
                    if (!GantryPicker.PickHead(_mouseRay, _mouseRayLength, out _headGantryId, out _headLane))
                    {
                        _headLane = -1;
                    }
                }
                else
                {
                    SetHovered(0);
                    _headGantryId = 0;
                    _headLane = -1;
                }
            }
            catch (Exception e)
            {
                SetHovered(0);
                Log.Error("Hover pick failed", e);
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
                if (!_mouseRayValid)
                {
                    _hitValid = false;
                    return;
                }

                // Terrain plus road surfaces. Including roads means a click on a bridge deck lands on the deck
                // instead of passing through to the riverbed below (ToolBase.RayCast only tests nets when the
                // ignore flags aren't All; the nearer of terrain/road wins).
                var input = new RaycastInput(_mouseRay, _mouseRayLength)
                {
                    m_netService = new RaycastService(ItemClass.Service.Road, ItemClass.SubService.None, ItemClass.Layer.Default),
                    m_ignoreSegmentFlags = NetSegment.Flags.None,
                    m_ignoreNodeFlags = NetNode.Flags.None
                };
                RaycastOutput output;
                if (RayCast(input, out output))
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

        protected override void OnToolGUI(Event e)
        {
            base.OnToolGUI(e);
            try
            {
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
            // Clicking a signal head (when not placing) opens its editor instead of starting a gantry.
            if (!_hasStart && _headGantryId != 0 && _headLane >= 0)
            {
                Gantry edited = GantryManager.Find(_headGantryId);
                if (edited != null && Panel != null)
                {
                    Log.Debug("Editing signal: gantry #" + edited.Id + " lane " + _headLane);
                    Panel.ShowFor(edited, _headLane);
                }

                return;
            }

            if (!_hitValid)
            {
                return;
            }

            Vector3 pos = GroundPoint(_hitPos, _hitOnRoad);
            if (!_hasStart)
            {
                _start = pos;
                _hasStart = true;
                _previewBuilt = false;
                Log.Debug("First point set at " + pos);
                return;
            }

            float span = FlatDistance(_start, pos);
            if (!SpanValid(span))
            {
                Log.Info("Placement rejected: span " + span.ToString("F1") + "m outside " +
                         GantryManager.MinSpan + "-" + GantryManager.MaxSpan + "m");
                return;
            }

            Gantry g = GantryManager.Add(_start, pos, GantryManager.DefaultPostHeight, GantryManager.DefaultBeamDepth);
            Log.Info("Placed gantry " + g);
            _hasStart = false;
            _previewBuilt = false;
        }

        private void OnRightClick()
        {
            if (!_hasStart && _hoveredId != 0)
            {
                int id = _hoveredId;
                SetHovered(0);
                if (Panel != null && Panel.IsShowing(id))
                {
                    Panel.Hide();
                }

                if (GantryManager.Remove(id))
                {
                    Log.Info("Deleted gantry #" + id + " (" + GantryManager.Count + " left)");
                }

                return;
            }

            CancelOrExit();
        }

        /// <summary>Esc / right-click: cancel the first point, or leave the tool if none is set.</summary>
        public void CancelOrExit()
        {
            if (_hasStart)
            {
                _hasStart = false;
                _previewBuilt = false;
                Log.Info("First point cancelled");
            }
            else
            {
                ToolsModifierControl.SetTool<DefaultTool>();
            }
        }

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
                    float bottom = GantryMeshBuilder.BeamBottomY(_start, end, GantryManager.DefaultPostHeight, scan.RoadTopY);
                    _previewMesh = GantryMeshBuilder.Build(_start, end, bottom,
                        GantryManager.DefaultBeamDepth, _previewMesh, out _previewOrigin);
                    _previewSignals = SignalMeshBuilder.Build(_start, end, _previewOrigin, bottom,
                        GantryManager.DefaultBeamDepth, scan.Lanes, NoOverrides, SignalMeshBuilder.DefaultSignalSize,
                        _previewSignals);
                    _previewEnd = end;
                    _previewBuilt = true;
                }

                GantryRenderer.DrawStructure(_previewMesh, _previewOrigin, SpanValid(span) ? ValidTint : InvalidTint);
                GantryRenderer.DrawSignals(_previewSignals, _previewOrigin);
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

                // Highlight the hovered signal head: overlays project onto any surface inside [minY, maxY].
                if (!_hasStart && _headGantryId != 0 && _headLane >= 0)
                {
                    Gantry hg = GantryManager.Find(_headGantryId);
                    if (hg != null && _headLane < hg.Lanes.Count)
                    {
                        Vector3 c = SignalMeshBuilder.HeadCentre(hg.Start, hg.End, hg.BeamBottomY, hg.BeamDepth,
                            hg.Lanes[_headLane].Offset);
                        float half = SignalMeshBuilder.HeadSize(SignalMeshBuilder.DefaultSignalSize, hg.BeamDepth) * 0.5f;
                        rm.OverlayEffect.DrawCircle(cameraInfo, HeadHoverColor, c, half * 2.6f,
                            c.y - half - 0.05f, c.y + half + 0.05f, false, true);
                    }
                }

                if (!_hitValid)
                {
                    return;
                }
                Vector3 hover = _hitPos;
                if (!_hasStart)
                {
                    rm.OverlayEffect.DrawCircle(cameraInfo, ValidColor, hover, 2f, -1f, 1280f, false, true);
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
