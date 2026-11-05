using System;
using System.Collections.Generic;
using System.Text;

namespace GantryTool.Guide
{
    /// <summary>
    /// The countries signs can come from. A sign carries its own in its design; this is only which one new signs start
    /// from, kept per city, in a save block of its own that older versions of the mod simply never read.
    /// </summary>
    public static class GuideThemes
    {
        public const string Key = "GantryTool.Country";
        public const string Fallback = "us";
        private const byte Version = 1;

        private static string _city;

        /// <summary>The country of new signs: this city's, or else the one chosen in the mod's settings.</summary>
        public static string Current
        {
            get
            {
                if (IsKnown(_city))
                {
                    return _city;
                }

                string chosen = Settings.Current.DefaultSignCountry;
                return IsKnown(chosen) ? chosen : Fallback;
            }

            set { _city = value; }
        }

        public static bool IsKnown(string id)
        {
            return !string.IsNullOrEmpty(id) && GuideAssets.Ids().Contains(id);
        }

        public static string Name(string id)
        {
            return Json.Str(GuideAssets.Theme(id), "name", id);
        }

        /// <summary>The sign a new guide sign starts as.</summary>
        public static string DefaultTemplate(string id)
        {
            string named = Json.Str(GuideAssets.Theme(id), "defaultSign");
            List<string> names = GuideAssets.TemplateNames(id);
            return named != null && names.Contains(named) ? named : names.Count > 0 ? names[0] : null;
        }

        public static byte[] Write()
        {
            byte[] id = Encoding.UTF8.GetBytes(_city ?? "");
            var data = new byte[id.Length + 2];
            data[0] = Version;
            data[1] = (byte)Math.Min(id.Length, 255);
            Array.Copy(id, 0, data, 2, data[1]);
            return data;
        }

        /// <summary>Takes in a city's choice; anything missing or unreadable means it hasn't made one.</summary>
        public static void Read(byte[] data)
        {
            _city = null;
            if (data != null && data.Length >= 2 && data[0] == Version && data[1] > 0 && data.Length >= data[1] + 2)
            {
                _city = Encoding.UTF8.GetString(data, 2, data[1]);
            }
        }
    }
}
