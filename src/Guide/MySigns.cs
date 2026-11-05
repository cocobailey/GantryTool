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
    /// signs keep their own copy of the design, so nothing placed depends on these files. A sign's imported pictures go
    /// inside its file, so the one file is all there is to pass around. Testable outside the game.
    /// </summary>
    public static class MySigns
    {
        public const string IdPrefix = "my:";
        public const int MaxNameLength = 40;
        private const string FilePrefix = "sign-";
        private const string PicturesKey = "pictures";
        public const string PackExtension = ".signpack";
        private const long MaxPackLength = 64L * 1024 * 1024;
        private const long MaxFileLength = 8L * 1024 * 1024;

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
                if (new FileInfo(file).Length > MaxFileLength)
                {
                    return null;
                }

                var doc = Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (doc == null)
                {
                    return null;
                }

                TakePictures(doc);
                if (Json.Write(doc).Length > GuideData.MaxDesignLength)
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

                if (PutPictures(copy))
                {
                    text = Json.Write(copy);
                    if (text.Length > MaxFileLength)
                    {
                        return false;
                    }
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

        private static bool PutPictures(Dictionary<string, object> doc)
        {
            var ids = new HashSet<string>();
            GuidePictures.IdsIn(doc, ids);
            var pictures = new Dictionary<string, object>();
            foreach (string id in ids)
            {
                byte[] bytes = GuidePictures.Bytes(id);
                if (bytes != null)
                {
                    pictures[id] = Convert.ToBase64String(bytes);
                }
            }

            if (pictures.Count > 0)
            {
                doc[PicturesKey] = pictures;
            }

            return pictures.Count > 0;
        }

        /// <summary>Moves the pictures in a sign's file out of its design and into the city's pictures.</summary>
        private static void TakePictures(Dictionary<string, object> doc)
        {
            Dictionary<string, object> pictures = Json.Obj(doc, PicturesKey);
            doc.Remove(PicturesKey);
            if (pictures == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> picture in pictures)
            {
                try
                {
                    GuidePictures.Add(Convert.FromBase64String(picture.Value as string ?? ""));
                }
                catch (FormatException)
                {
                    Log.Warn("Skipped a damaged picture in a saved sign");
                }
            }
        }

        /// <summary>Writes all the saved signs, with their pictures, into one file to pass around. Returns its path, or null if there's nothing to put in one.</summary>
        public static string ExportPack(string folder)
        {
            try
            {
                List<MySign> signs = List(folder);
                if (signs.Count == 0)
                {
                    return null;
                }

                var all = new List<object>();
                foreach (MySign sign in signs)
                {
                    var copy = (Dictionary<string, object>)Json.Parse(Json.Write(sign.Design));
                    PutPictures(copy);
                    all.Add(copy);
                }

                string path = Path.Combine(folder, "signs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + PackExtension);
                File.WriteAllText(path, Json.Write(new Dictionary<string, object> { { "signs", all } }), Encoding.UTF8);
                return path;
            }
            catch (Exception e)
            {
                Log.Error("Could not write a sign pack", e);
                return null;
            }
        }

        /// <summary>
        /// Takes in the signs from every pack in the folder. A sign it already has is left alone; a different one with a
        /// name in use gets a number after its name. Returns how many were new. Never throws.
        /// </summary>
        public static int ImportPacks(string folder)
        {
            int added = 0;
            try
            {
                if (!Directory.Exists(folder))
                {
                    return 0;
                }

                foreach (string file in Directory.GetFiles(folder, "*" + PackExtension))
                {
                    added += ImportPack(folder, file);
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not read sign packs", e);
            }

            return added;
        }

        private static int ImportPack(string folder, string file)
        {
            int added = 0;
            try
            {
                if (new FileInfo(file).Length > MaxPackLength)
                {
                    return 0;
                }

                var pack = Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                List<object> signs = pack != null ? Json.Arr(pack, "signs") : null;
                if (signs == null)
                {
                    return 0;
                }

                List<MySign> have = List(folder);
                foreach (object o in signs)
                {
                    var doc = o as Dictionary<string, object>;
                    if (doc == null)
                    {
                        continue;
                    }

                    TakePictures(doc);
                    string name = CleanName(Json.Str(doc, "name", "Imported sign"));
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    string free = name;
                    bool same = false;
                    for (int n = 2; ; n++)
                    {
                        MySign existing = have.Find(s => string.Equals(s.Name, free, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            break;
                        }

                        if (SameDesign(existing.Design, doc))
                        {
                            same = true;
                            break;
                        }

                        string suffix = " " + n;
                        free = CleanName(name.Substring(0, Math.Min(name.Length, MaxNameLength - suffix.Length)) + suffix);
                    }

                    if (!same && Save(folder, free, doc))
                    {
                        added++;
                        have = List(folder);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("Skipped sign pack " + Path.GetFileName(file) + ": " + e.Message);
            }

            return added;
        }

        private static bool SameDesign(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            var x = (Dictionary<string, object>)Json.Parse(Json.Write(a));
            var y = (Dictionary<string, object>)Json.Parse(Json.Write(b));
            foreach (Dictionary<string, object> d in new[] { x, y })
            {
                d.Remove("name");
                d.Remove("id");
            }

            return Json.Write(Sorted(x)) == Json.Write(Sorted(y));
        }

        private static object Sorted(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                var keys = new List<string>(map.Keys);
                keys.Sort(StringComparer.Ordinal);
                var sorted = new Dictionary<string, object>();
                foreach (string k in keys)
                {
                    sorted[k] = Sorted(map[k]);
                }

                return sorted;
            }

            var list = value as List<object>;
            return list != null ? list.ConvertAll(Sorted) : value;
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
