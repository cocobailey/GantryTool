using System;
using System.IO;
using System.Xml.Serialization;
using ColossalFramework.IO;

namespace GantryTool
{
    /// <summary>
    /// Global mod options, stored in %LOCALAPPDATA%\Colossal Order\Cities_Skylines\GantryTool.xml.
    /// Post height and beam depth are defaults for NEW gantries (each gantry saves its own); the rest apply
    /// to every gantry immediately.
    /// </summary>
    [XmlRoot("GantryToolSettings")]
    public sealed class Settings
    {
        public float PostHeight = 7.5f;        // 1.0 setting (post height); no longer used
        public float HeightAboveRoad = 5.5f;   // beam underside above the highest road crossed, for new gantries
        public float BeamDepth = 1.5f;         // truss depth (new gantries)
        public float RoadClearance = 5.5f;     // 1.0 setting; only used to convert 1.0 gantries
        public float SignalSize = 1.2f;        // LED face edge (m), clamped to fit the truss
        public float TrussBrightness = 0.62f;  // grey level of the steel
        public float GridPadding = 128f;       // road search padding (m) for long modded segments
        public bool DebugLogging;              // per-lane scan details in the log
        public bool UseTmpeRestrictions = true; // TM:PE: banned lanes -> red X, bus-only lanes -> diamond
        public byte DefaultStyle = 1;          // SignStyle used by gantries set to "Default" (1 = Europe)

        private static Settings _current;

        public static Settings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Load();
                }

                return _current;
            }
        }

        private static string FilePath => Path.Combine(DataLocation.localApplicationData, "GantryTool.xml");

        private static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var reader = new StreamReader(FilePath))
                    {
                        var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
                        Log.Info("Settings loaded from " + FilePath);
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Settings load failed; using defaults", e);
            }

            return new Settings();
        }

        public void Save()
        {
            try
            {
                using (var writer = new StreamWriter(FilePath))
                {
                    new XmlSerializer(typeof(Settings)).Serialize(writer, this);
                }
            }
            catch (Exception e)
            {
                Log.Error("Settings save failed", e);
            }
        }
    }
}
