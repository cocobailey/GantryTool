using System;
using ColossalFramework;
using GantryTool.Data;
using GantryTool.Render;
using GantryTool.Tool;
using GantryTool.UI;
using ICities;
using UnityEngine;

namespace GantryTool
{
    public sealed class Loading : LoadingExtensionBase
    {
        private GantryPlaceTool _tool;
        private ToolButton _button;
        private SignalPanel _panel;
        private GantryPanel _gantryPanel;
        private HotkeyListener _hotkey;

        public override void OnLevelLoaded(LoadMode mode)
        {
            try
            {
                // NetInfo already folds LHT into m_finalDirection (NetInfo.cs:1101); logged so LHT maps can be verified.
                bool lht = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True;
                int segments = Singleton<NetManager>.instance.m_segmentCount;
                Log.Info("Level loaded: mode=" + mode + ", LHT=" + lht + ", segments=" + segments);

                if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                {
                    Log.Info("Not a city game mode; tool not installed");
                    return;
                }

                GantryMaterials.Init();
                GantryRenderer.EnsureRegistered();
                GantryRenderer.Active = true;
                GantryUpdater.Reset();
                GantryUpdater.Enabled = true;
                Integration.Tmpe.Init();

                // Tools must live on the ToolController's GameObject to be usable as CurrentTool.
                _tool = ToolsModifierControl.toolController.gameObject.AddComponent<GantryPlaceTool>();
                _tool.enabled = false;

                _panel = SignalPanel.Create();
                _tool.Panel = _panel;

                _gantryPanel = GantryPanel.Create();
                _gantryPanel.Tool = _tool;
                _tool.GantryPanel = _gantryPanel;

                _hotkey = HotkeyListener.Create(_tool);

                // Unified UI toolbar if the UUI mod is enabled, otherwise our own floating button.
                if (!Integration.Uui.TryRegister(_tool))
                {
                    _button = ToolButton.Create();
                    _button.Tool = _tool;
                }

                Log.Info("Tool installed");
            }
            catch (Exception e)
            {
                Log.Error("OnLevelLoaded failed", e);
            }
        }

        public override void OnLevelUnloading()
        {
            try
            {
                GantryRenderer.Active = false;
                GantryUpdater.Enabled = false;
                GantryUpdater.Reset();
                Integration.Tmpe.Shutdown();

                if (_tool != null && ToolsModifierControl.toolController != null &&
                    ToolsModifierControl.toolController.CurrentTool == _tool)
                {
                    ToolsModifierControl.SetTool<DefaultTool>();
                }

                if (_tool != null)
                {
                    UnityEngine.Object.Destroy(_tool);
                    _tool = null;
                }

                if (_panel != null)
                {
                    UnityEngine.Object.Destroy(_panel.gameObject);
                    _panel = null;
                }

                if (_gantryPanel != null)
                {
                    UnityEngine.Object.Destroy(_gantryPanel.gameObject);
                    _gantryPanel = null;
                }

                if (_button != null)
                {
                    UnityEngine.Object.Destroy(_button.gameObject);
                    _button = null;
                }

                Integration.Uui.Unregister();

                if (_hotkey != null)
                {
                    UnityEngine.Object.Destroy(_hotkey.gameObject);
                    _hotkey = null;
                }

                GantryManager.Clear();
                UndoStack.Clear();
                GantryMaterials.Release();
                Log.Info("Level unloading; tool removed, gantries cleared");
            }
            catch (Exception e)
            {
                Log.Error("OnLevelUnloading failed", e);
            }
        }
    }
}
