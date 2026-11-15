using System;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// Runs a panel's per-frame code. Panels don't override UIPanel.Update and call base.Update(): FPS Booster renames
    /// the UI's Update methods when the game starts and rewrites each mod to match. If it can't rewrite this mod (it
    /// couldn't while TmpeImpl had constants of a TM:PE type), base.Update() no longer exists: a MissingMethodException
    /// every frame, with no stack trace, which left the sign editor drawn but dead. A separate component on the same
    /// object needs nothing from UIPanel, so it works either way, and UIPanel's own Update still runs untouched.
    /// tests/GantryTool.Tests/DllChecks.cs guards both halves.
    /// </summary>
    internal sealed class FrameTicker : MonoBehaviour
    {
        private Action _tick;
        private bool _failed;

        public static void Attach(Component panel, Action tick)
        {
            panel.gameObject.AddComponent<FrameTicker>()._tick = tick;
        }

        private void Update()
        {
            if (_tick == null)
            {
                return;
            }

            try
            {
                _tick();
            }
            catch (Exception e)
            {
                // Logged once, with where it happened, rather than every frame.
                if (!_failed)
                {
                    _failed = true;
                    Log.Error(name + " per-frame update", e);
                }
            }
        }
    }
}
