using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>One letter of a baked font: a coverage bitmap and its metrics in pixels at the font's em size.</summary>
    public sealed class Glyph
    {
        public int Width, Height, Left, Top;
        public float Advance;
        public byte[] Coverage; // one byte per pixel, decoded when first used
        public byte[] Encoded;
    }

    /// <summary>A font baked by tools/guide/make_assets.py (see there for the file format).</summary>
    public sealed class GlyphFont
    {
        public int Em;
        private readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();
        private readonly Dictionary<int, float> _kerns = new Dictionary<int, float>();

        public static GlyphFont Read(Stream stream)
        {
            using (var r = new BinaryReader(stream))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "GBF1")
                {
                    throw new InvalidDataException("Not a guide sign font");
                }

                var f = new GlyphFont { Em = r.ReadInt32() };
                int count = r.ReadInt32();
                var order = new List<KeyValuePair<Glyph, int>>(count);
                for (int i = 0; i < count; i++)
                {
                    char ch = (char)r.ReadUInt16();
                    var g = new Glyph { Width = r.ReadInt16(), Height = r.ReadInt16(), Left = r.ReadInt16(), Top = r.ReadInt16(), Advance = r.ReadSingle() };
                    order.Add(new KeyValuePair<Glyph, int>(g, r.ReadInt32()));
                    f._glyphs[ch] = g;
                }

                int kerns = r.ReadInt32();
                for (int i = 0; i < kerns; i++)
                {
                    int a = r.ReadUInt16(), b = r.ReadUInt16();
                    f._kerns[(a << 16) | b] = r.ReadSingle();
                }

                foreach (KeyValuePair<Glyph, int> g in order)
                {
                    g.Key.Encoded = r.ReadBytes(g.Value);
                }

                return f;
            }
        }

        public Glyph Get(char c)
        {
            Glyph g;
            if (!_glyphs.TryGetValue(c, out g) && !_glyphs.TryGetValue('?', out g))
            {
                return null;
            }

            lock (g)
            {
                if (g.Coverage == null)
                {
                    g.Coverage = Decode(g);
                }
            }

            return g;
        }

        public bool Has(char c)
        {
            return _glyphs.ContainsKey(c);
        }

        public float Kern(char a, char b)
        {
            float k;
            return _kerns.TryGetValue((a << 16) | b, out k) ? k : 0f;
        }

        /// <summary>Width of a line in pixels at the font's em size.</summary>
        public float Measure(string text)
        {
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                Glyph g = Get(text[i]);
                w += g != null ? g.Advance : 0f;
                if (i + 1 < text.Length)
                {
                    w += Kern(text[i], text[i + 1]);
                }
            }

            return w;
        }

        private static byte[] Decode(Glyph g)
        {
            var px = new byte[g.Width * g.Height];
            int o = 0;
            foreach (byte b in g.Encoded)
            {
                int level = b >> 4, run = (b & 15) + 1;
                byte v = (byte)(level * 17);
                for (int k = 0; k < run && o < px.Length; k++)
                {
                    px[o++] = v;
                }
            }

            return px;
        }
    }

    /// <summary>An RGBA picture baked by tools/guide/make_assets.py, top row first.</summary>
    public sealed class GuideImage
    {
        public int Width, Height;
        public Color32[] Pixels;

        public static GuideImage Read(Stream stream)
        {
            using (var r = new BinaryReader(stream))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "GIM1")
                {
                    throw new InvalidDataException("Not a guide sign image");
                }

                var img = new GuideImage { Width = r.ReadInt32(), Height = r.ReadInt32() };
                img.Pixels = new Color32[img.Width * img.Height];
                int o = 0;
                while (o < img.Pixels.Length)
                {
                    int run = r.ReadByte() + 1;
                    var c = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    for (int k = 0; k < run && o < img.Pixels.Length; k++)
                    {
                        img.Pixels[o++] = c;
                    }
                }

                return img;
            }
        }
    }

    /// <summary>Loads fonts, shields, themes and templates embedded in the DLL, caching them.</summary>
    public static class GuideAssets
    {
        private const string Prefix = "GantryTool.Guide.";
        private const string Shared = "us";     // other countries' signs use its fonts and symbols unless they have their own
        private static List<string> _ids;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, GlyphFont> Fonts = new Dictionary<string, GlyphFont>();
        private static readonly Dictionary<string, GuideImage> Images = new Dictionary<string, GuideImage>();

        private static Stream Open(string theme, string file)
        {
            return typeof(GuideAssets).Assembly.GetManifestResourceStream(Prefix + theme + "." + file);
        }

        public static GlyphFont Font(string theme, string family)
        {
            lock (Sync)
            {
                string key = theme + "/" + family;
                GlyphFont f;
                if (!Fonts.TryGetValue(key, out f))
                {
                    Stream s = Open(theme, family + ".gbf") ?? Open(Shared, family + ".gbf");
                    f = s != null ? GlyphFont.Read(s) : null;
                    Fonts[key] = f;
                }

                return f;
            }
        }

        public static GuideImage Image(string theme, string name)
        {
            lock (Sync)
            {
                string key = theme + "/" + name;
                GuideImage i;
                if (!Images.TryGetValue(key, out i))
                {
                    Stream s = Open(theme, name + ".gim") ?? Open(Shared, name + ".gim");
                    i = s != null ? GuideImage.Read(s) : null;
                    Images[key] = i;
                }

                return i;
            }
        }

        public static Dictionary<string, object> Theme(string theme)
        {
            return ReadJson(Open(theme, "theme.json"));
        }

        /// <summary>Every country's signs in the mod, by their folder name.</summary>
        public static List<string> Ids()
        {
            if (_ids == null)
            {
                const string End = ".theme.json";
                var ids = new List<string>();
                foreach (string r in typeof(GuideAssets).Assembly.GetManifestResourceNames())
                {
                    if (r.StartsWith(Prefix, StringComparison.Ordinal) && r.EndsWith(End, StringComparison.Ordinal))
                    {
                        ids.Add(r.Substring(Prefix.Length, r.Length - Prefix.Length - End.Length));
                    }
                }

                ids.Sort(StringComparer.Ordinal);
                _ids = ids;
            }

            return _ids;
        }

        /// <summary>The templates of a theme, by file name.</summary>
        public static List<string> TemplateNames(string theme)
        {
            var names = new List<string>();
            string start = Prefix + theme + ".templates.";
            foreach (string r in typeof(GuideAssets).Assembly.GetManifestResourceNames())
            {
                if (r.StartsWith(start, StringComparison.Ordinal) && r.EndsWith(".json", StringComparison.Ordinal))
                {
                    names.Add(r.Substring(start.Length, r.Length - start.Length - 5));
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public static Dictionary<string, object> Template(string theme, string name)
        {
            return ReadJson(Open(theme, "templates." + name + ".json"));
        }

        private static Dictionary<string, object> ReadJson(Stream s)
        {
            if (s == null)
            {
                return null;
            }

            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                return Json.Parse(r.ReadToEnd()) as Dictionary<string, object>;
            }
        }
    }
}
