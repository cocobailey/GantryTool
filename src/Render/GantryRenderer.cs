using System;
using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Draws all gantries. A plain object (not a MonoBehaviour): RenderManager's renderables list is
    /// static and never cleared (RenderManager.cs:273,475), so this is registered exactly once per game
    /// session and simply does nothing while no level is loaded.
    /// </summary>
    public sealed class GantryRenderer : IRenderableManager
    {
        private const float MaxRenderDistance = 3000f;
        private const int MaxRebuildsPerFrame = 8;
        private const float BlinkPeriod = 1.0f;

        private static GantryRenderer _instance;

        public static bool Active;

        /// <summary>Gantry under the cursor while the tool is in delete mode (0 = none); drawn red.</summary>
        public static volatile int HoveredId;

        private static readonly Color HoverTint = new Color(0.75f, 0.92f, 1f, 1f);
        private static readonly Color SelectedTint = new Color(0.45f, 0.8f, 1f, 1f);

        /// <summary>Gantry selected in the tool (0 = none); drawn tinted blue.</summary>
        public static volatile int SelectedId;

        /// <summary>Gantry being dragged (0 = none): its rebuilds log at debug level only.</summary>
        public static volatile int QuietId;

        private readonly List<Gantry> _scratch = new List<Gantry>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly int _idColor = Shader.PropertyToID("_Color");
        private readonly int _idObjectIndex = Shader.PropertyToID("_ObjectIndex");
        private DrawCallData _drawCallData;
        private bool _errorLogged;

        public static void EnsureRegistered()
        {
            if (_instance != null)
            {
                return;
            }

            _instance = new GantryRenderer();
            RenderManager.RegisterRenderableManager(_instance);
            Log.Info("Renderer registered");
        }

        public string GetName() => "GantryTool";

        public DrawCallData GetDrawCallData() => _drawCallData;

        public void BeginRendering(RenderManager.CameraInfo cameraInfo)
        {
            _drawCallData.m_defaultCalls = 0;
        }

        public void EndRendering(RenderManager.CameraInfo cameraInfo)
        {
            if (!Active || !GantryMaterials.Ready)
            {
                return;
            }

            try
            {
                GantryManager.GetAll(_scratch);
                // Merge arrows: 0.5 s on / 0.5 s off on real time, so they keep blinking while paused.
                bool blinkOn = (Time.realtimeSinceStartup % BlinkPeriod) < BlinkPeriod * 0.5f;
                int rebuilds = 0;
                for (int i = 0; i < _scratch.Count; i++)
                {
                    Gantry g = _scratch[i];
                    if (g.MeshDirty && rebuilds < MaxRebuildsPerFrame)
                    {
                        Rebuild(g);
                        rebuilds++;
                    }
                    else if (g.SignalsDirty && !g.MeshDirty && g.Mesh != null)
                    {
                        RebuildSignals(g);
                    }

                    if (g.Mesh == null)
                    {
                        continue;
                    }

                    if (!cameraInfo.Intersect(g.Bounds) ||
                        !cameraInfo.CheckRenderDistance(g.Bounds.center, MaxRenderDistance))
                    {
                        continue;
                    }

                    DrawStructure(g.Mesh, g.Origin, g.Id == SelectedId ? SelectedTint : g.Id == HoveredId ? HoverTint : Color.white);
                    SignStyle style = SignStyles.Resolve(g.Style);
                    DrawSignals(g.SignalMesh, g.Origin, style);
                    if (blinkOn)
                    {
                        DrawSignals(g.BlinkMesh, g.Origin, style);
                    }

                    // Ramp metering signs: the two beacons take turns while their meter is metering.
                    for (int k = 0; k < g.RampBeacons.Count; k++)
                    {
                        if (Integration.RampMeters.IsMetering(g.RampBeacons[k].Node))
                        {
                            DrawSignals(g.RampBeacons[k].Mesh, g.Origin, style, blinkOn ? 0 : 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Never throw from the render loop; log the first failure only to avoid spamming every frame.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("EndRendering failed", e);
                }
            }
        }

        /// <summary>Draws one structure mesh. Also used by the tool for its preview.</summary>
        public static void DrawStructure(Mesh mesh, Vector3 origin, Color tint)
        {
            if (_instance == null || !GantryMaterials.Ready || mesh == null)
            {
                return;
            }

            MaterialPropertyBlock block = _instance._block;
            block.Clear();
            block.SetColor(_instance._idColor, tint);
            block.SetVector(_instance._idObjectIndex, RenderManager.DefaultColorLocation);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one),
                GantryMaterials.Structure, GantryMaterials.Layer, null, 0, block);
            _instance._drawCallData.m_defaultCalls++;
        }

        /// <summary>Draws a signal mesh with illumination forced on (objectIndex.z = 1, PropInstance.cs:337-360).</summary>
        public static void DrawSignals(Mesh mesh, Vector3 origin, SignStyle style, int submesh = 0)
        {
            Material material = GantryMaterials.Ready ? GantryMaterials.SignalFor(style) : null;
            if (_instance == null || material == null || mesh == null ||
                mesh.vertexCount == 0)
            {
                return;
            }

            MaterialPropertyBlock block = _instance._block;
            block.Clear();
            block.SetColor(_instance._idColor, Color.white);
            Vector4 index = RenderManager.DefaultColorLocation;
            index.z = 1f;
            block.SetVector(_instance._idObjectIndex, index);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one),
                material, GantryMaterials.Layer, null, submesh, block);
            _instance._drawCallData.m_defaultCalls++;
        }

        private static void Rebuild(Gantry g)
        {
            try
            {
                LaneScanner.Result scan = LaneScanner.Scan(g.Start, g.End, verbose: Settings.Current.DebugLogging);
                if (g.Mesh != null)
                {
                    // Option sliders rebuild everything on each tick, so only real lane changes go to the normal log.
                    string msg = "Gantry #" + g.Id + " recomputed: " + g.Lanes.Count + " -> " + scan.Lanes.Count + " lanes";
                    if (g.Lanes.Count != scan.Lanes.Count && g.Id != QuietId)
                    {
                        Log.Info(msg);
                    }
                    else
                    {
                        Log.Debug(msg);
                    }
                }

                g.Lanes.Clear();
                g.Lanes.AddRange(scan.Lanes);
                g.RoadTopY = scan.RoadTopY;
                float groundMax = Mathf.Max(g.Start.y, g.End.y);
                lock (GantryManager.Lock)
                {
                    if (float.IsNaN(g.HeightAboveRoad))
                    {
                        // A gantry from 1.0: keep its beam exactly where 1.0 put it.
                        g.HeightAboveRoad = GantryHeight.Convert(groundMax, g.PostHeight, scan.RoadTopY, g.Clearance);
                        Log.Debug("Gantry #" + g.Id + " converted to height above road " + g.HeightAboveRoad.ToString("F2") + " m");
                    }

                    g.BeamBottomY = GantryHeight.BeamBottom(groundMax, scan.RoadTopY, g.HeightAboveRoad);

                    // Kept in step so the 1.0-format save block draws the same beam if opened in 1.0.
                    g.PostHeight = GantryHeight.EquivalentPostHeight(groundMax, g.BeamBottomY);
                }
                if (Settings.Current.DebugLogging)
                {
                    Log.Debug(LaneScanner.Describe(g.Id, scan, g.BeamBottomY, SideOf(g)));
                }

                g.Mesh = GantryMeshBuilder.Build(g.Start, g.End, g.BeamBottomY, g.BeamDepth, g.Mesh, out g.Origin);
                Bounds b = g.Mesh.bounds;
                b.center += g.Origin;
                g.Bounds = b;
                RebuildSignals(g); // may grow the bounds for boxes that rise above the truss
                g.MeshDirty = false;
                if (Settings.Current.DebugLogging)
                {
                    Log.Debug("Mesh built for gantry #" + g.Id + ": " + g.Mesh.vertexCount + " verts, " +
                             (g.Mesh.triangles.Length / 3) + " tris");
                }
            }
            catch (Exception e)
            {
                g.MeshDirty = false; // don't retry a broken build every frame
                Log.Error("Mesh build failed for gantry #" + g.Id, e);
            }
        }

        private static void RebuildSignals(Gantry g)
        {
            try
            {
                SignAtlas atlas = GantryMaterials.AtlasFor(SignStyles.Resolve(g.Style));
                lock (GantryManager.Lock)
                {
                    g.SignalsDirty = false;
                    GantryManager.RefreshExtraLinks(g);
                    g.SignalMesh = SignalMeshBuilder.Build(g.Start, g.End, g.Origin, g.BeamBottomY, g.BeamDepth,
                        g.Lanes, g.Overrides, g.ExtraHeads, g.SignalSize, g.SignalMesh, atlas);
                    if (g.SignalMesh != null && g.SignalMesh.vertexCount > 0 && g.Mesh != null)
                    {
                        Bounds b = g.Mesh.bounds;
                        b.Encapsulate(g.SignalMesh.bounds);
                        b.center += g.Origin;
                        g.Bounds = b;
                    }

                    g.BlinkMesh = SignalMeshBuilder.Build(g.Start, g.End, g.Origin, g.BeamBottomY, g.BeamDepth,
                        g.Lanes, g.Overrides, g.ExtraHeads, g.SignalSize, g.BlinkMesh, atlas, blinkLayer: true);
                    SignalMeshBuilder.BuildRampBeacons(g.Start, g.End, g.Origin, g.BeamBottomY, g.BeamDepth,
                        g.Lanes, g.Overrides, g.ExtraHeads, g.SignalSize, atlas, g.RampBeacons);
                }

                if (Settings.Current.DebugLogging)
                {
                    Log.Debug("Gantry #" + g.Id + " signals: " + SignalSummary(g));
                }
            }
            catch (Exception e)
            {
                g.SignalsDirty = false;
                Log.Error("Signal build failed for gantry #" + g.Id, e);
            }
        }

        /// <summary>e.g. "-side face [G,G,R,R], +side face [R,R,G,G]" in lane order from the start post.</summary>
        private static string SignalSummary(Gantry g)
        {
            if (g.Lanes.Count == 0)
            {
                return "none (bare gantry)";
            }

            var minus = new System.Text.StringBuilder();
            var plus = new System.Text.StringBuilder();
            for (int i = 0; i < g.Lanes.Count; i++)
            {
                SignalSymbol m, p;
                SignalRules.Resolve(g.Lanes, i, g.Overrides, out m, out p);
                if (i > 0)
                {
                    minus.Append(',');
                    plus.Append(',');
                }

                minus.Append(SignalRules.ShortName(m));
                plus.Append(SignalRules.ShortName(p));
            }

            return "-side face [" + minus + "], +side face [" + plus + "]";
        }

        private static Vector2 SideOf(Gantry g)
        {
            Vector2 d = new Vector2(g.End.x - g.Start.x, g.End.z - g.Start.z);
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.right;
            return new Vector2(-d.y, d.x);
        }

        public void BeginOverlay(RenderManager.CameraInfo cameraInfo)
        {
        }

        public void EndOverlay(RenderManager.CameraInfo cameraInfo)
        {
        }

        public void UndergroundOverlay(RenderManager.CameraInfo cameraInfo)
        {
        }

        public void CheckReferences()
        {
        }

        public void InitRenderData()
        {
        }

        public bool CalculateGroupData(int groupX, int groupZ, int layer, ref int vertexCount, ref int triangleCount,
            ref int objectCount, ref RenderGroup.VertexArrays vertexArrays)
        {
            return false;
        }

        public void PopulateGroupData(int groupX, int groupZ, int layer, ref int vertexIndex, ref int triangleIndex,
            Vector3 groupPosition, RenderGroup.MeshData data, ref Vector3 min, ref Vector3 max,
            ref float maxRenderDistance, ref float maxInstanceDistance, ref bool requireSurfaceMaps)
        {
        }
    }
}
