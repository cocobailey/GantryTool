using System;
using System.Collections.Generic;
using UnityEngine;

namespace GantryTool.Guide
{
    /// <summary>
    /// A pixel buffer guide signs are drawn into, top row first, in plain managed code so it needs no Unity objects
    /// (and can run on any thread and outside the game). Coordinates are pixels; shapes are anti-aliased.
    /// </summary>
    public sealed class SignCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public SignCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
        }

        /// <summary>Blends <paramref name="c"/> over pixel (x, y) with coverage 0-1.</summary>
        public void Blend(int x, int y, Color32 c, float coverage)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || coverage <= 0f)
            {
                return;
            }

            float a = coverage * c.a / 255f;
            if (a <= 0f)
            {
                return;
            }

            int i = y * Width + x;
            Color32 d = Pixels[i];
            float da = d.a / 255f;
            float oa = a + da * (1f - a);
            if (oa <= 0f)
            {
                return;
            }

            float k = da * (1f - a);
            Pixels[i] = new Color32(
                (byte)Mathf.Clamp((c.r * a + d.r * k) / oa + 0.5f, 0f, 255f),
                (byte)Mathf.Clamp((c.g * a + d.g * k) / oa + 0.5f, 0f, 255f),
                (byte)Mathf.Clamp((c.b * a + d.b * k) / oa + 0.5f, 0f, 255f),
                (byte)Mathf.Clamp(oa * 255f + 0.5f, 0f, 255f));
        }

        /// <summary>A rectangle with its own radius on each corner (top-left, top-right, bottom-right, bottom-left).</summary>
        public void FillRoundRect(float x0, float y0, float x1, float y1, float tl, float tr, float br, float bl, Color32 c)
        {
            int ix0 = Mathf.FloorToInt(x0), iy0 = Mathf.FloorToInt(y0);
            int ix1 = Mathf.CeilToInt(x1), iy1 = Mathf.CeilToInt(y1);
            bool opaque = c.a == 255;
            float corners = Mathf.Max(Mathf.Max(tl, tr), Mathf.Max(br, bl));
            int solidX0 = Mathf.Max(0, Mathf.CeilToInt(x0 + 0.5f + corners)), solidX1 = Mathf.Min(Width, Mathf.FloorToInt(x1 - 0.5f - corners));
            for (int y = iy0; y < iy1; y++)
            {
                float py = y + 0.5f;
                bool solidRow = opaque && py - y0 >= 0.5f && y1 - py >= 0.5f && y >= 0 && y < Height;
                bool clearOfCorners = py - y0 >= corners && y1 - py >= corners;
                for (int x = ix0; x < ix1; x++)
                {
                    // Most of a sign is the inside of a rectangle: no edge or corner to work out there.
                    if (solidRow && x >= solidX0 && x < solidX1)
                    {
                        Pixels[y * Width + x] = c;
                        continue;
                    }

                    if (solidRow && clearOfCorners && x >= 0 && x < Width && x + 0.5f - x0 >= 0.5f && x1 - x - 0.5f >= 0.5f)
                    {
                        Pixels[y * Width + x] = c;
                        continue;
                    }

                    float px = x + 0.5f;
                    float d = Mathf.Min(Mathf.Min(px - x0, x1 - px), Mathf.Min(py - y0, y1 - py));
                    float cover = Mathf.Clamp01(d + 0.5f);
                    cover = Mathf.Min(cover, Corner(px, py, x0 + tl, y0 + tl, tl, px < x0 + tl && py < y0 + tl));
                    cover = Mathf.Min(cover, Corner(px, py, x1 - tr, y0 + tr, tr, px > x1 - tr && py < y0 + tr));
                    cover = Mathf.Min(cover, Corner(px, py, x1 - br, y1 - br, br, px > x1 - br && py > y1 - br));
                    cover = Mathf.Min(cover, Corner(px, py, x0 + bl, y1 - bl, bl, px < x0 + bl && py > y1 - bl));
                    Blend(x, y, c, cover);
                }
            }
        }

        private static float Corner(float px, float py, float cx, float cy, float r, bool inCorner)
        {
            if (!inCorner || r <= 0f)
            {
                return 1f;
            }

            float dx = px - cx, dy = py - cy;
            return Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }

        public void FillRect(float x0, float y0, float x1, float y1, Color32 c)
        {
            FillRoundRect(x0, y0, x1, y1, 0f, 0f, 0f, 0f, c);
        }

        /// <summary>An ellipse filling the box.</summary>
        public void FillEllipse(float x0, float y0, float x1, float y1, Color32 c)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, rx = (x1 - x0) * 0.5f, ry = (y1 - y0) * 0.5f;
            if (rx <= 0f || ry <= 0f)
            {
                return;
            }

            for (int y = Mathf.FloorToInt(y0); y < Mathf.CeilToInt(y1); y++)
            {
                for (int x = Mathf.FloorToInt(x0); x < Mathf.CeilToInt(x1); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float dist = (1f - Mathf.Sqrt(dx * dx + dy * dy)) * Mathf.Min(rx, ry);
                    Blend(x, y, c, Mathf.Clamp01(dist + 0.5f));
                }
            }
        }

        /// <summary>Filled polygons (even-odd), anti-aliased by sampling each pixel on a 4 x 4 grid.</summary>
        public void FillPolygon(List<Vector2> points, Color32 c)
        {
            if (points.Count < 3)
            {
                return;
            }

            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vector2 p in points)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
            }

            int iy0 = Mathf.Max(0, Mathf.FloorToInt(minY)), iy1 = Mathf.Min(Height, Mathf.CeilToInt(maxY));
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(minX)), ix1 = Mathf.Min(Width, Mathf.CeilToInt(maxX));
            if (ix1 <= ix0)
            {
                return;
            }

            const int Sub = 4;
            var cover = new float[ix1 - ix0];
            var hits = new List<float>();
            for (int y = iy0; y < iy1; y++)
            {
                Array.Clear(cover, 0, cover.Length);
                for (int s = 0; s < Sub; s++)
                {
                    float sy = y + (s + 0.5f) / Sub;
                    hits.Clear();
                    for (int i = 0; i < points.Count; i++)
                    {
                        Vector2 a = points[i], b = points[(i + 1) % points.Count];
                        if ((a.y <= sy) != (b.y <= sy))
                        {
                            hits.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                        }
                    }

                    hits.Sort();
                    for (int h = 0; h + 1 < hits.Count; h += 2)
                    {
                        float l = Mathf.Max(hits[h], ix0), r = Mathf.Min(hits[h + 1], ix1);
                        for (int x = Mathf.FloorToInt(l); x < Mathf.CeilToInt(r) && x < ix1; x++)
                        {
                            float seg = Mathf.Min(r, x + 1f) - Mathf.Max(l, x);
                            if (seg > 0f)
                            {
                                cover[x - ix0] += seg / Sub;
                            }
                        }
                    }
                }

                for (int x = ix0; x < ix1; x++)
                {
                    Blend(x, y, c, Mathf.Clamp01(cover[x - ix0]));
                }
            }
        }

        /// <summary>
        /// Draws a coverage bitmap (one byte per pixel, 0-255) scaled into the box, averaging the source under each
        /// target pixel so small text stays clean.
        /// </summary>
        public void DrawCoverage(byte[] src, int sw, int sh, float x0, float y0, float x1, float y1, Color32 c)
        {
            float dw = x1 - x0, dh = y1 - y0;
            if (dw <= 0f || dh <= 0f || sw <= 0 || sh <= 0)
            {
                return;
            }

            for (int y = Mathf.FloorToInt(y0); y < Mathf.CeilToInt(y1); y++)
            {
                float sy0 = (y - y0) / dh * sh, sy1 = (y + 1 - y0) / dh * sh;
                for (int x = Mathf.FloorToInt(x0); x < Mathf.CeilToInt(x1); x++)
                {
                    float sx0 = (x - x0) / dw * sw, sx1 = (x + 1 - x0) / dw * sw;
                    Blend(x, y, c, Average(src, sw, sh, sx0, sy0, sx1, sy1) / 255f);
                }
            }
        }

        private static float Average(byte[] src, int sw, int sh, float x0, float y0, float x1, float y1)
        {
            float sum = 0f, weight = (x1 - x0) * (y1 - y0);
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0)), ix1 = Mathf.Min(sw, Mathf.CeilToInt(x1));
            int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0)), iy1 = Mathf.Min(sh, Mathf.CeilToInt(y1));
            for (int y = iy0; y < iy1; y++)
            {
                float wy = Mathf.Min(y + 1f, y1) - Mathf.Max((float)y, y0);
                for (int x = ix0; x < ix1; x++)
                {
                    float wx = Mathf.Min(x + 1f, x1) - Mathf.Max((float)x, x0);
                    sum += src[y * sw + x] * wx * wy;
                }
            }

            return weight > 0f ? sum / weight : 0f;
        }

        /// <summary>Draws an RGBA image (top row first) scaled into the box.</summary>
        public void DrawImage(Color32[] src, int sw, int sh, float x0, float y0, float x1, float y1)
        {
            float dw = x1 - x0, dh = y1 - y0;
            if (dw <= 0f || dh <= 0f)
            {
                return;
            }

            for (int y = Mathf.FloorToInt(y0); y < Mathf.CeilToInt(y1); y++)
            {
                int sy0 = Mathf.Clamp(Mathf.FloorToInt((y - y0) / dh * sh), 0, sh - 1);
                int sy1 = Mathf.Clamp(Mathf.CeilToInt((y + 1 - y0) / dh * sh), sy0 + 1, sh);
                for (int x = Mathf.FloorToInt(x0); x < Mathf.CeilToInt(x1); x++)
                {
                    int sx0 = Mathf.Clamp(Mathf.FloorToInt((x - x0) / dw * sw), 0, sw - 1);
                    int sx1 = Mathf.Clamp(Mathf.CeilToInt((x + 1 - x0) / dw * sw), sx0 + 1, sw);
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    int n = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            Color32 p = src[sy * sw + sx];
                            r += p.r * p.a;
                            g += p.g * p.a;
                            b += p.b * p.a;
                            a += p.a;
                            n++;
                        }
                    }

                    if (a <= 0f)
                    {
                        continue;
                    }

                    Blend(x, y, new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), 255), a / n / 255f);
                }
            }
        }
    }
}
