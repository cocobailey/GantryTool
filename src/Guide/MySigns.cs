using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GantryTool.Data;

namespace GantryTool.Guide
{
    /// <summary>A sign the player saved to reuse.</summary>
    public sealed class MySign
    {
        public string Name;
        public string Id;
        public Dictionary<string, object> Design;
    }

    /// <summary>
    /// The player's own saved signs: one design file each, so they work in every city and can be passed around. Placed
    /// signs keep their own copy of the design, so nothing placed depends on these files. Testable outside the game.
    /// </summary>
    public static class MySigns
    {
        public const string IdPrefix = "my:";
        public const int MaxNameLength = 40;
        private const string FilePrefix = "sign-";

        public static string CleanName(string name)
        {
            string clean = (name ?? "").Trim();
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength).Trim() : clean;
        }

        /// <summary>The file for a name. Odd characters become "_", and the prefix keeps it clear of names Windows reserves.</summary>
        private static string FilePath(string folder, string name)
        {
            var file = new StringBuilder(FilePrefix);
            foreach (char c in CleanName(name))
            {
                file.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_');
            }

            return Path.Combine(folder, file + ".json");
        }

        public static bool Exists(string folder, string name)
        {
            return File.Exists(FilePath(folder, name));
        }

        /// <summary>Every readable sign in the folder, by name; files that can't be used are skipped. Never throws.</summary>
        public static List<MySign> List(string folder)
        {
            var signs = new List<MySign>();
            try
            {
                if (!Directory.Exists(folder))
                {
                    return signs;
                }

                foreach (string file in Directory.GetFiles(folder, FilePrefix + "*.json"))
                {
                    MySign sign = Read(file);
                    if (sign != null)
                    {
                        signs.Add(sign);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not list saved signs", e);
            }

            signs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return signs;
        }

        private static MySign Read(string file)
        {
            try
            {
                if (new FileInfo(file).Length > GuideData.MaxDesignLength)
                {
                    return null;
                }

                var doc = Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (doc == null)
                {
                    return null;
                }

                string name = CleanName(Json.Str(doc, "name", Path.GetFileNameWithoutExtension(file).Substring(FilePrefix.Length)));
                doc["name"] = name;
                doc["id"] = IdPrefix + name;
                return new MySign { Name = name, Id = IdPrefix + name, Design = doc };
            }
            catch (Exception e)
            {
                Log.Warn("Skipped saved sign " + Path.GetFileName(file) + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Saves a design under a name, replacing a sign of that name; false if it can't be saved.</summary>
        public static bool Save(string folder, string name, Dictionary<string, object> design)
        {
            string clean = CleanName(name);
            if (clean.Length == 0)
            {
                return false;
            }

            try
            {
                var copy = Json.Parse(Json.Write(design)) as Dictionary<string, object>;
                copy["name"] = clean;
                copy["id"] = IdPrefix + clean;
                string text = Json.Write(copy);
                if (text.Length > GuideData.MaxDesignLength)
                {
                    return false;
                }

                Directory.CreateDirectory(folder);
                File.WriteAllText(FilePath(folder, clean), text, Encoding.UTF8);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not save sign " + clean, e);
                return false;
            }
        }

        public static void Delete(string folder, string name)
        {
            try
            {
                File.Delete(FilePath(folder, name));
            }
            catch (Exception e)
            {
                Log.Error("Could not delete sign " + name, e);
            }
        }
    }
}
