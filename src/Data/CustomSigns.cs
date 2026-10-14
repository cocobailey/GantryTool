using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GantryTool.Data
{
    public enum CustomKind : byte
    {
        Text = 1,
        Image = 2
    }

    /// <summary>A player-made sign: up to two short lines of LED text, or a picture.</summary>
    public sealed class CustomSign
    {
        public ushort Id;
        public CustomKind Kind;
        public string Line1 = "";
        public string Line2 = "";
        public byte Colour;          // CustomSigns.Colours index (text)
        public string FileName = ""; // image: the file it came from (shown in the picker only)
        public byte[] Image;         // image: PNG/JPG bytes, stored in the save so the sign travels with it
        public bool LedLook;         // image: redraw as LED dots instead of showing the picture as is

        public string Label => Kind == CustomKind.Text
            ? (Line2.Length > 0 ? Line1 + " " + Line2 : Line1)
            : Path.GetFileNameWithoutExtension(FileName);
    }

    /// <summary>
    /// The save-wide library of custom signs. A face shows one with <see cref="SignalSymbol.Custom"/> and the sign's
    /// id as its value. Saved under its own key: only signs some face uses are written, so unused ones drop out.
    /// 1.0 never reads the key (those faces show Auto there).
    ///
    /// Format, key "GantryTool.Custom": int version(=1), int count, then per sign: ushort id, byte kind,
    /// string line1, string line2, byte colour, string fileName, bool ledLook, int imageLength, imageLength bytes.
    /// </summary>
    public static class CustomSigns
    {
        public const string Key = "GantryTool.Custom";
        public const int Version = 1;
        public const int MaxChars = 7;
        public const int MaxImageBytes = 64 * 1024;
        public const int MaxSigns = 24; // keeps each style's sign texture at 2048 x 2048 or less

        public static readonly string[] ColourNames = { "White", "Amber", "Red", "Green", "Yellow" };

        private static readonly object Sync = new object();
        private static readonly Dictionary<ushort, CustomSign> Signs = new Dictionary<ushort, CustomSign>();

        /// <summary>Bumped on every change, so sign textures know to rebuild.</summary>
        public static int Revision { get; private set; }

        public static CustomSign Get(ushort id)
        {
            lock (Sync)
            {
                CustomSign s;
                return Signs.TryGetValue(id, out s) ? s : null;
            }
        }

        /// <summary>All signs, oldest first.</summary>
        public static List<CustomSign> All()
        {
            lock (Sync)
            {
                var list = new List<CustomSign>(Signs.Values);
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                return list;
            }
        }

        public static int Count
        {
            get
            {
                lock (Sync)
                {
                    return Signs.Count;
                }
            }
        }

        /// <summary>Upper-case, only characters the LED font has, at most <see cref="MaxChars"/>.</summary>
        public static string CleanLine(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            var sb = new StringBuilder();
            foreach (char ch in s.ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == ' ' || ch == '-' || ch == '/' ||
                    ch == '.' || ch == '!')
                {
                    sb.Append(ch);
                }

                if (sb.Length == MaxChars)
                {
                    break;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// A text sign (an identical existing one is reused). Returns its id, or 0 if the text is empty or the
        /// library is full.
        /// </summary>
        public static ushort AddText(string line1, string line2, byte colour)
        {
            line1 = CleanLine(line1);
            line2 = CleanLine(line2);
            if (line1.Length == 0)
            {
                line1 = line2;
                line2 = "";
            }

            if (line1.Length == 0)
            {
                return 0;
            }

            colour = (byte)Math.Min(colour, ColourNames.Length - 1);
            lock (Sync)
            {
                foreach (CustomSign s in Signs.Values)
                {
                    if (s.Kind == CustomKind.Text && s.Line1 == line1 && s.Line2 == line2 && s.Colour == colour)
                    {
                        return s.Id;
                    }
                }

                return Add(new CustomSign { Kind = CustomKind.Text, Line1 = line1, Line2 = line2, Colour = colour });
            }
        }

        /// <summary>An image sign from already size-checked bytes. Returns its id, or 0 if not possible.</summary>
        public static ushort AddImage(string fileName, byte[] image, bool ledLook)
        {
            if (image == null || image.Length == 0 || image.Length > MaxImageBytes)
            {
                return 0;
            }

            lock (Sync)
            {
                foreach (CustomSign s in Signs.Values)
                {
                    if (s.Kind == CustomKind.Image && s.LedLook == ledLook && SameBytes(s.Image, image))
                    {
                        return s.Id;
                    }
                }

                return Add(new CustomSign { Kind = CustomKind.Image, FileName = fileName ?? "", Image = image, LedLook = ledLook });
            }
        }

        private static ushort Add(CustomSign sign)
        {
            if (Signs.Count >= MaxSigns)
            {
                return 0;
            }

            ushort id = 1;
            foreach (ushort k in Signs.Keys)
            {
                if (k >= id)
                {
                    id = (ushort)(k + 1);
                }
            }

            sign.Id = id;
            Signs[id] = sign;
            Revision++;
            return id;
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Signs.Clear();
                Revision++;
            }
        }

        /// <summary>Replaces the library (after loading a save).</summary>
        public static void ReplaceAll(List<CustomSign> signs)
        {
            lock (Sync)
            {
                Signs.Clear();
                foreach (CustomSign s in signs)
                {
                    Signs[s.Id] = s;
                }

                Revision++;
            }
        }

        // ----------------------------------------------------------------- save data

        /// <summary>Ids of the custom signs some face of these gantries shows.</summary>
        public static HashSet<ushort> UsedIds(List<Gantry> gantries, object overridesLock)
        {
            var used = new HashSet<ushort>();
            lock (overridesLock)
            {
                foreach (Gantry g in gantries)
                {
                    foreach (SignalOverride o in g.Overrides)
                    {
                        if (o.Minus == SignalSymbol.Custom)
                        {
                            used.Add(o.MinusValue);
                        }

                        if (o.Plus == SignalSymbol.Custom)
                        {
                            used.Add(o.PlusValue);
                        }
                    }
                }
            }

            return used;
        }

        public static byte[] Write(List<CustomSign> signs, HashSet<ushort> only)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                var keep = new List<CustomSign>();
                foreach (CustomSign s in signs)
                {
                    if (only == null || only.Contains(s.Id))
                    {
                        keep.Add(s);
                    }
                }

                w.Write(Version);
                w.Write(keep.Count);
                foreach (CustomSign s in keep)
                {
                    w.Write(s.Id);
                    w.Write((byte)s.Kind);
                    w.Write(s.Line1 ?? "");
                    w.Write(s.Line2 ?? "");
                    w.Write(s.Colour);
                    w.Write(s.FileName ?? "");
                    w.Write(s.LedLook);
                    byte[] img = s.Image ?? new byte[0];
                    w.Write(img.Length);
                    w.Write(img);
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Reads the library; returns what was read before any error (never throws).</summary>
        public static List<CustomSign> Read(byte[] bytes, out string error)
        {
            var list = new List<CustomSign>();
            error = null;
            if (bytes == null || bytes.Length == 0)
            {
                return list;
            }

            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1 || version > Version)
                    {
                        error = "custom sign data version " + version + " not supported (max " + Version + ")";
                        return list;
                    }

                    int count = r.ReadInt32();
                    if (count < 0 || count > 4096)
                    {
                        error = "custom sign count " + count + " out of range";
                        return list;
                    }

                    var seen = new HashSet<ushort>();
                    for (int i = 0; i < count; i++)
                    {
                        var s = new CustomSign
                        {
                            Id = r.ReadUInt16(),
                            Kind = (CustomKind)r.ReadByte(),
                            Line1 = CleanLine(r.ReadString()),
                            Line2 = CleanLine(r.ReadString()),
                            Colour = r.ReadByte(),
                            FileName = r.ReadString(),
                            LedLook = r.ReadBoolean()
                        };
                        int length = r.ReadInt32();
                        if (length < 0 || length > MaxImageBytes)
                        {
                            error = "custom sign #" + s.Id + " image size " + length + " out of range";
                            return list;
                        }

                        s.Image = r.ReadBytes(length);
                        if (s.Image.Length != length)
                        {
                            error = "custom sign data cut short";
                            return list;
                        }

                        s.Colour = (byte)Math.Min(s.Colour, ColourNames.Length - 1);
                        bool valid = s.Id != 0 && seen.Add(s.Id) &&
                                     (s.Kind == CustomKind.Text ? s.Line1.Length > 0 : s.Kind == CustomKind.Image && length > 0);
                        if (valid)
                        {
                            list.Add(s);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "custom sign read failed after " + list.Count + " signs: " + e.Message;
            }

            return list;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
