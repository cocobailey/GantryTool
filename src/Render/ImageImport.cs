using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.IO;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Pictures for custom signs: listed from a folder in the game's local data, and shrunk until they fit the save
    /// (<see cref="CustomSigns.MaxImageBytes"/>). Main thread only (uses Unity textures).
    /// </summary>
    public static class ImageImport
    {
        private static readonly int[] Sizes = { 256, 192, 128, 96, 64, 48 };

        /// <summary>%LOCALAPPDATA%\Colossal Order\Cities_Skylines\GantryTool\Signs (created when needed).</summary>
        public static string Folder => Path.Combine(Path.Combine(DataLocation.localApplicationData, "GantryTool"), "Signs");

        /// <summary>PNG and JPG files in the folder, by name.</summary>
        public static List<string> ListFiles()
        {
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (string f in Directory.GetFiles(Folder))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        files.Add(Path.GetFileName(f));
                    }
                }

                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Warn("Listing custom sign pictures failed: " + e.Message);
            }

            return files;
        }

        /// <summary>
        /// Reads a picture from the folder, shrinking it if needed. Returns the bytes to store, or null with a reason.
        /// </summary>
        public static byte[] Load(string fileName, out string error)
        {
            error = null;
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(Path.Combine(Folder, fileName));
            }
            catch (Exception e)
            {
                error = "Couldn't read " + fileName + ": " + e.Message;
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            try
            {
                if (!tex.LoadImage(raw) || tex.width < 1 || tex.height < 1)
                {
                    error = fileName + " isn't a PNG or JPG the game can read";
                    return null;
                }

                int longest = Mathf.Max(tex.width, tex.height);
                if (raw.Length <= CustomSigns.MaxImageBytes && longest <= Sizes[0])
                {
                    return raw; // small enough as it is
                }

                Color[] src = tex.GetPixels();
                foreach (int size in Sizes)
                {
                    if (size > longest)
                    {
                        continue;
                    }

                    byte[] png = Shrunk(src, tex.width, tex.height, size);
                    if (png != null && png.Length <= CustomSigns.MaxImageBytes)
                    {
                        Log.Info("Custom sign picture " + fileName + ": " + tex.width + "x" + tex.height + ", " + raw.Length +
                                 " bytes -> " + size + " px, " + png.Length + " bytes");
                        return png;
                    }
                }

                error = fileName + " is too detailed to fit in the save, even shrunk";
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>Box-filtered copy whose longest side is <paramref name="size"/>, as PNG.</summary>
        private static byte[] Shrunk(Color[] src, int w, int h, int size)
        {
            float scale = (float)size / Mathf.Max(w, h);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            var dst = new Color[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int y0 = y * h / nh, y1 = Mathf.Max(y0 + 1, (y + 1) * h / nh);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = x * w / nw, x1 = Mathf.Max(x0 + 1, (x + 1) * w / nw);
                    Color sum = new Color(0f, 0f, 0f, 0f);
                    for (int sy = y0; sy < y1; sy++)
                    {
                        for (int sx = x0; sx < x1; sx++)
                        {
                            sum += src[sy * w + sx];
                        }
                    }

                    dst[y * nw + x] = sum / ((x1 - x0) * (y1 - y0));
                }
            }

            var tex = new Texture2D(nw, nh, TextureFormat.ARGB32, false);
            try
            {
                tex.SetPixels(dst);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
