using System;
using System.Collections.Generic;
using System.IO;
using ICities;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Stores gantries in the savegame under key "GantryTool". Only placement data is saved; meshes,
    /// lanes and signals are always recomputed.
    ///
    /// Format (little-endian BinaryWriter):
    ///   int version, int count, then per gantry:
    ///   int id, float startX/Y/Z, float endX/Y/Z, float postHeight, float beamDepth
    ///   v2+: int overrideCount, then per override: float offset, byte minusSymbol, byte plusSymbol
    ///   v3+: ... followed by sbyte travelSign (lane direction when the override was set)
    ///
    /// OnSaveData runs on the simulation thread (SimulationManager.Data.Serialize), OnLoadData during
    /// loading (SimulationManager.LateUpdateData) — neither may touch Unity objects.
    /// </summary>
    public sealed class GantrySerializer : SerializableDataExtensionBase
    {
        public const string DataKey = "GantryTool";
        public const int CurrentVersion = 3;

        public override void OnSaveData()
        {
            try
            {
                var gantries = new List<Gantry>();
                GantryManager.GetAll(gantries);

                byte[] bytes;
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(CurrentVersion);
                    w.Write(gantries.Count);
                    for (int i = 0; i < gantries.Count; i++)
                    {
                        Gantry g = gantries[i];
                        w.Write(g.Id);
                        WriteVector(w, g.Start);
                        WriteVector(w, g.End);
                        w.Write(g.PostHeight);
                        w.Write(g.BeamDepth);

                        // GetAll copied the gantry list under the lock, but overrides can still be edited
                        // on the main thread, so read them under the lock too.
                        lock (GantryManager.Lock)
                        {
                            w.Write(g.Overrides.Count);
                            for (int k = 0; k < g.Overrides.Count; k++)
                            {
                                w.Write(g.Overrides[k].Offset);
                                w.Write((byte)g.Overrides[k].Minus);
                                w.Write((byte)g.Overrides[k].Plus);
                                w.Write(g.Overrides[k].Sign);
                            }
                        }
                    }

                    w.Flush();
                    bytes = ms.ToArray();
                }

                serializableDataManager.SaveData(DataKey, bytes);
                Log.Info("Saved " + gantries.Count + " gantries (" + bytes.Length + " bytes, v" + CurrentVersion + ")");
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
                byte[] bytes = serializableDataManager.LoadData(DataKey);
                if (bytes == null || bytes.Length == 0)
                {
                    Log.Info("No gantry data in this save");
                }
                else
                {
                    using (var ms = new MemoryStream(bytes))
                    using (var r = new BinaryReader(ms))
                    {
                        int version = r.ReadInt32();
                        if (version < 1 || version > CurrentVersion)
                        {
                            Log.Error("Gantry data version " + version + " not supported (max " + CurrentVersion +
                                      "); gantries skipped");
                        }
                        else
                        {
                            int count = r.ReadInt32();
                            for (int i = 0; i < count; i++)
                            {
                                var g = new Gantry
                                {
                                    Id = r.ReadInt32(),
                                    Start = ReadVector(r),
                                    End = ReadVector(r),
                                    PostHeight = r.ReadSingle(),
                                    BeamDepth = r.ReadSingle()
                                };
                                if (version >= 2)
                                {
                                    int overrides = r.ReadInt32();
                                    for (int k = 0; k < overrides; k++)
                                    {
                                        g.Overrides.Add(new SignalOverride
                                        {
                                            Offset = r.ReadSingle(),
                                            Minus = ReadSymbol(r),
                                            Plus = ReadSymbol(r),
                                            Sign = version >= 3 ? r.ReadSByte() : SignalOverride.UnknownSign
                                        });
                                    }
                                }

                                loaded.Add(g);
                            }

                            Log.Info("Loaded " + loaded.Count + " gantries (v" + version + ")");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Keep whatever was read before the failure rather than losing everything.
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

        private static SignalSymbol ReadSymbol(BinaryReader r)
        {
            byte b = r.ReadByte();
            // Unknown symbols (from a newer version) fall back to Auto rather than failing the load.
            return b <= (byte)SignalSymbol.Hidden ? (SignalSymbol)b : SignalSymbol.Auto;
        }

        private static void WriteVector(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static Vector3 ReadVector(BinaryReader r)
        {
            float x = r.ReadSingle();
            float y = r.ReadSingle();
            float z = r.ReadSingle();
            return new Vector3(x, y, z);
        }
    }
}
