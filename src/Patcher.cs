using System;
using HarmonyLib;

namespace GantryTool
{
    /// <summary>
    /// Harmony plumbing. Patches are picked up from [HarmonyPatch]-annotated classes in this assembly.
    /// </summary>
    public static class Patcher
    {
        private const string HarmonyId = "com.cocobailey.gantrytool";

        private static bool _patched;

        public static void PatchAll()
        {
            if (_patched)
            {
                return;
            }

            Log.Info("Harmony ready");
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(Patcher).Assembly);
                _patched = true;
                foreach (var method in harmony.GetPatchedMethods())
                {
                    Log.Info("Patched " + method.DeclaringType.Name + "." + method.Name);
                }

                Log.Info("Harmony patches applied");
            }
            catch (Exception e)
            {
                Log.Error("Harmony patching failed", e);
            }
        }

        public static void UnpatchAll()
        {
            if (!_patched)
            {
                return;
            }

            try
            {
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
                _patched = false;
                Log.Info("Harmony patches removed");
            }
            catch (Exception e)
            {
                Log.Error("Harmony unpatch failed", e);
            }
        }
    }
}
