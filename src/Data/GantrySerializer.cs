using System;
using System.Collections.Generic;
using ICities;

namespace GantryTool.Data
{
    /// <summary>
    /// Stores gantries in the savegame. The encoding lives in <see cref="GantryData"/>: a legacy block in the
    /// 1.0 format (so 1.0 can still load saves made by newer versions) plus an extension block for everything
    /// added later. Only placement and settings are saved; meshes, lanes and signals are always recomputed.
    ///
    /// OnSaveData runs on the simulation thread (SimulationManager.Data.Serialize), OnLoadData during
    /// loading (SimulationManager.LateUpdateData) — neither may touch Unity objects.
    /// </summary>
    public sealed class GantrySerializer : SerializableDataExtensionBase
    {
        public override void OnSaveData()
        {
            try
            {
                var gantries = new List<Gantry>();
                GantryManager.GetAll(gantries);

                byte[] legacy = GantryData.WriteLegacy(gantries, GantryManager.Lock);
                byte[] ext = GantryData.WriteExt(gantries);
                serializableDataManager.SaveData(GantryData.LegacyKey, legacy);
                serializableDataManager.SaveData(GantryData.ExtKey, ext);
                Log.Info("Saved " + gantries.Count + " gantries (" + legacy.Length + " + " + ext.Length + " bytes, v" +
                         GantryData.LegacyVersion + " + ext v" + GantryData.ExtVersion + ")");
            }
            catch (Exception e)
            {
                Log.Error("OnSaveData failed; gantries not saved", e);
            }
        }

        public override void OnLoadData()
        {
            var loaded = new List<Gantry>();
            try
            {
                byte[] legacy = serializableDataManager.LoadData(GantryData.LegacyKey);
                if (legacy == null || legacy.Length == 0)
                {
                    Log.Info("No gantry data in this save");
                }
                else
                {
                    int version;
                    string error;
                    loaded = GantryData.ReadLegacy(legacy, out version, out error);
                    if (error != null)
                    {
                        // Keep whatever was read before the failure rather than losing everything.
                        Log.Error("Gantry data: " + error);
                    }

                    Log.Info("Loaded " + loaded.Count + " gantries (v" + version + ")");

                    byte[] ext = serializableDataManager.LoadData(GantryData.ExtKey);
                    string extError;
                    int applied = GantryData.ApplyExt(ext, loaded, out extError);
                    if (extError != null)
                    {
                        Log.Warn("Gantry extension data: " + extError);
                    }

                    if (ext != null && ext.Length > 0)
                    {
                        Log.Info("Per-gantry settings for " + applied + " of " + loaded.Count + " gantries");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("OnLoadData failed after " + loaded.Count + " gantries", e);
            }

            try
            {
                GantryManager.ReplaceAll(loaded);
            }
            catch (Exception e)
            {
                Log.Error("ReplaceAll failed", e);
            }
        }
    }
}
