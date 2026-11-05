using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GantryTool.Data;

namespace GantryTool.Guide
{
    /// <summary>
    /// Pictures the player brought in for guide signs. A sign's design only names a picture, by a hash of its bytes, so
    /// the same picture on many signs is stored once and copying a sign stays cheap. The bytes go in the save under their
    /// own key, so a problem there can never touch the gantries; only pictures some sign uses are written.
    ///
    /// Format, key "GantryTool.GuidePictures": byte version, int count, then per picture: int length, length bytes.
    /// Each picture's name is worked out again from its bytes on loading.
    /// </summary>
    public static class GuidePictures
    {
        public const string Key = "GantryTool.GuidePictures";
        public const string PartKey = "picture";
        public const string AspectKey = "aspect";
        public const int MaxBytes = 128 * 1024;
        public const int MaxPictures = 64;
        public const float MinHeight = 8f, MaxHeight = 400f, NewHeight = 48f;
        private const byte Version = 1;

        private sealed class Entry
        {
            public byte[] Bytes;
            public GuideImage Image;
            public bool Tried;
        }

        private static readonly Dictionary<string, Entry> Pictures = new Dictionary<string, Entry>();
        private static volatile bool _pending;

        /// <summary>Turns stored bytes into pixels. Set by the game side, which alone can read PNG and JPG files.</summary>
        public static Func<byte[], GuideImage> Decoder;

        public static string IdFor(byte[] bytes)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(bytes);
                var id = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    id.Append(hash[i].ToString("x2"));
                }

                return id.ToString();
            }
        }

        /// <summary>Keeps a picture and returns its name, or null if it's empty or too big. Main thread only.</summary>
        public static string Add(byte[] bytes)
        {
            string id = Keep(bytes);
            if (id != null)
            {
                DecodePending();
            }

            return id;
        }

        /// <summary>
        /// Turns kept pictures into pixels. Main thread only: reading a picture uses a Unity texture, and doing that on the
        /// thread a city loads on crashes the game outright, past any try/catch. So loading only keeps the bytes, and
        /// whatever draws signs calls this first.
        /// </summary>
        public static void DecodePending()
        {
            if (!_pending || Decoder == null)
            {
                return;
            }

            var waiting = new List<Entry>();
            lock (Pictures)
            {
                _pending = false;
                foreach (Entry e in Pictures.Values)
                {
                    if (e.Image == null && !e.Tried)
                    {
                        waiting.Add(e);
                    }
                }
            }

            foreach (Entry e in waiting)
            {
                GuideImage image = null;
                try
                {
                    if (TooBig(e.Bytes))
                    {
                        Log.Warn("Skipped a guide sign picture too big to be one of ours");
                    }
                    else
                    {
                        image = Decoder(e.Bytes);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("A guide sign picture could not be read: " + ex.Message);
                }

                lock (Pictures)
                {
                    e.Image = image;
                    e.Tried = true;
                }
            }
        }

        /// <summary>
        /// A small file can claim to be a huge picture, and reading one would run the game out of memory. Pictures brought
        /// in here are never over 1024 px, so one in a shared city or sign pack that says it is gets left out.
        /// </summary>
        public static bool TooBig(byte[] b)
        {
            const int Most = 2048;
            int w = 0, h = 0;
            if (b.Length > 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G')
            {
                w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                return w <= 0 || h <= 0 || w > Most || h > Most;
            }

            if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int at = 2;
                while (at + 8 < b.Length && b[at] == 0xFF)
                {
                    byte marker = b[at + 1];
                    int length = (b[at + 2] << 8) | b[at + 3];
                    if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        h = (b[at + 5] << 8) | b[at + 6];
                        w = (b[at + 7] << 8) | b[at + 8];
                        return w > Most || h > Most;
                    }

                    if (length < 2)
                    {
                        break;
                    }

                    at += 2 + length;
                }
            }

            return false;
        }

        /// <summary>Keeps a picture's bytes, on any thread. Returns its name, or null if it's empty or too big.</summary>
        private static string Keep(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                return null;
            }

            string id = IdFor(bytes);
            lock (Pictures)
            {
                if (!Pictures.ContainsKey(id))
                {
                    Pictures[id] = new Entry { Bytes = bytes };
                    _pending = true;
                }
            }

            return id;
        }

        /// <summary>The pixels, or null if the picture isn't here; the sign then leaves its space empty.</summary>
        public static GuideImage Image(string id)
        {
            lock (Pictures)
            {
                Entry entry;
                return id != null && Pictures.TryGetValue(id, out entry) ? entry.Image : null;
            }
        }

        public static byte[] Bytes(string id)
        {
            lock (Pictures)
            {
                Entry entry;
                return id != null && Pictures.TryGetValue(id, out entry) ? entry.Bytes : null;
            }
        }

        public static void Clear()
        {
            lock (Pictures)
            {
                Pictures.Clear();
            }
        }

        /// <summary>A new part of a sign showing a picture, keeping the picture's shape.</summary>
        public static Dictionary<string, object> NewPart(string id)
        {
            var part = new Dictionary<string, object> { { "type", "symbol" }, { "height", (double)NewHeight } };
            SetPicture(part, id);
            return part;
        }

        public static void SetPicture(Dictionary<string, object> part, string id)
        {
            GuideImage image = Image(id);
            part[PartKey] = id;
            part[AspectKey] = image != null && image.Height > 0 ? Math.Round((double)image.Width / image.Height, 3) : 1.0;
        }

        /// <summary>Makes a picture cover the whole sign in place of what's on it; a sign's own "picture" key does that.</summary>
        public static void Cover(Dictionary<string, object> doc, string id)
        {
            doc[PartKey] = id;
            doc["items"] = new List<object>();
        }

        /// <summary>Adds the names of the pictures in a design.</summary>
        public static void IdsIn(string design, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(design) || design.IndexOf("\"" + PartKey + "\"", StringComparison.Ordinal) < 0)
            {
                return;
            }

            try
            {
                IdsIn(Json.Parse(design) as Dictionary<string, object>, ids);
            }
            catch (Exception e)
            {
                Log.Warn("Skipped a guide sign design while listing pictures: " + e.Message);
            }
        }

        public static void IdsIn(Dictionary<string, object> item, HashSet<string> ids)
        {
            if (item == null)
            {
                return;
            }

            string id = Json.Str(item, PartKey);
            if (!string.IsNullOrEmpty(id))
            {
                ids.Add(id);
            }

            List<object> items = Json.Arr(item, "items");
            if (items != null)
            {
                foreach (object o in items)
                {
                    IdsIn(o as Dictionary<string, object>, ids);
                }
            }

            // A message sign's other messages.
            List<object> pages = Json.Arr(item, GuidePages.PagesKey);
            if (pages != null)
            {
                foreach (object page in pages)
                {
                    var lines = page as List<object>;
                    if (lines != null)
                    {
                        foreach (object o in lines)
                        {
                            IdsIn(o as Dictionary<string, object>, ids);
                        }
                    }
                }
            }
        }

        /// <summary>The names of the pictures on these gantries' signs.</summary>
        public static HashSet<string> UsedIds(List<Gantry> gantries, object sync)
        {
            var ids = new HashSet<string>();
            lock (sync)
            {
                foreach (Gantry g in gantries)
                {
                    foreach (GuideHead sign in g.GuideSigns)
                    {
                        IdsIn(sign.Design, ids);
                    }
                }
            }

            return ids;
        }

        public static byte[] Write(HashSet<string> ids)
        {
            var found = new List<byte[]>();
            foreach (string id in ids)
            {
                byte[] bytes = Bytes(id);
                if (bytes == null)
                {
                    continue;
                }

                if (found.Count == MaxPictures)
                {
                    Log.Warn("More than " + MaxPictures + " guide sign pictures in this city; the rest aren't saved");
                    break;
                }

                found.Add(bytes);
            }

            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Version);
                w.Write(found.Count);
                foreach (byte[] bytes in found)
                {
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }

                w.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Takes in saved pictures, keeping whatever could be read. Returns how many; never throws. Safe on the thread a city loads on.</summary>
        public static int Read(byte[] data, out string error)
        {
            error = null;
            if (data == null || data.Length == 0)
            {
                return 0;
            }

            int added = 0;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    byte version = r.ReadByte();
                    if (version != Version)
                    {
                        error = "unknown picture data version " + version;
                        return 0;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count && i < MaxPictures; i++)
                    {
                        int length = r.ReadInt32();
                        if (length <= 0 || length > MaxBytes || length > data.Length - r.BaseStream.Position)
                        {
                            error = "picture " + i + " has a bad length";
                            return added;
                        }

                        if (Keep(r.ReadBytes(length)) != null)
                        {
                            added++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "picture data is cut short: " + e.Message;
            }

            return added;
        }
    }
}
