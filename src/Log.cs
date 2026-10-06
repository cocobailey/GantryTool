using System;
using UnityEngine;

namespace GantryTool
{
    /// <summary>
    /// Thin logging wrapper. Output lands in Cities_Data\output_log.txt prefixed with [GantryTool].
    /// Never throws, so it is safe to call from simulation and render loops.
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[GantryTool] ";

        public static void Info(string message)
        {
            try { UnityEngine.Debug.Log(Prefix + message); } catch { }
        }

        /// <summary>Only written when "Debug logging" is enabled in the options.</summary>
        public static void Debug(string message)
        {
            try
            {
                if (Settings.Current.DebugLogging)
                {
                    UnityEngine.Debug.Log(Prefix + message);
                }
            }
            catch
            {
            }
        }

        public static void Warn(string message)
        {
            try { UnityEngine.Debug.LogWarning(Prefix + message); } catch { }
        }

        public static void Error(string message)
        {
            try { UnityEngine.Debug.LogError(Prefix + message); } catch { }
        }

        public static void Error(string context, Exception e)
        {
            try { UnityEngine.Debug.LogError(Prefix + context + ": " + e); } catch { }
        }
    }
}
