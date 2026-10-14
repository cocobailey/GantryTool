using System.Collections.Generic;

namespace GantryTool.Render
{
    /// <summary>
    /// A 7-row LED dot font (most glyphs 5 dots wide) for text on signs: "SPEED LIMIT", "MAXIMUM", "END", "DO NOT
    /// PASS". Text is laid out in whole LEDs so every stroke is exactly one dot (times the scale) wide.
    /// </summary>
    public static class LedFont
    {
        public const int Height = 7;

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { 'A', new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" } },
            { 'B', new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " } },
            { 'C', new[] { " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### " } },
            { 'D', new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " } },
            { 'E', new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" } },
            { 'F', new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " } },
            { 'G', new[] { " ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####" } },
            { 'H', new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" } },
            { 'I', new[] { "###", " # ", " # ", " # ", " # ", " # ", "###" } },
            { 'J', new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " } },
            { 'K', new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" } },
            { 'L', new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" } },
            { 'M', new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" } },
            { 'N', new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" } },
            { 'O', new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " } },
            { 'P', new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " } },
            { 'Q', new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" } },
            { 'R', new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" } },
            { 'S', new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " } },
            { 'T', new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " } },
            { 'U', new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " } },
            { 'V', new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " } },
            { 'W', new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #" } },
            { 'X', new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" } },
            { 'Y', new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " } },
            { 'Z', new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" } },
            { '0', new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " } },
            { '1', new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " } },
            { '2', new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" } },
            { '3', new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " } },
            { '4', new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " } },
            { '5', new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " } },
            { '6', new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " } },
            { '7', new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " } },
            { '8', new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " } },
            { '9', new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " } },
            { '-', new[] { "   ", "   ", "   ", "###", "   ", "   ", "   " } },
            { '/', new[] { "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    " } },
            { '.', new[] { " ", " ", " ", " ", " ", " ", "#" } },
            { '!', new[] { "#", "#", "#", "#", "#", " ", "#" } },
            { ' ', new[] { "  ", "  ", "  ", "  ", "  ", "  ", "  " } },
            { 'k', new[] { "#   ", "#   ", "#  #", "# # ", "##  ", "# # ", "#  #" } },
            { 'm', new[] { "     ", "     ", "## # ", "# # #", "# # #", "#   #", "#   #" } },
            { 'h', new[] { "#   ", "#   ", "### ", "#  #", "#  #", "#  #", "#  #" } }
        };

        private static string[] Glyph(char c)
        {
            string[] g;
            if (Glyphs.TryGetValue(c, out g) || Glyphs.TryGetValue(char.ToUpperInvariant(c), out g))
            {
                return g;
            }

            return Glyphs[' '];
        }

        /// <summary>Width of a line in LEDs at scale 1 (one dot between glyphs).</summary>
        public static int Width(string line, int spacing = 1)
        {
            int w = 0;
            for (int i = 0; i < line.Length; i++)
            {
                w += Glyph(line[i])[0].Length + (i > 0 ? spacing : 0);
            }

            return w;
        }

        /// <summary>
        /// Whether LED (col, row) is lit by <paramref name="line"/> drawn centred on <paramref name="centreCol"/> with
        /// its top row at <paramref name="top"/> (rows counted from the top), each glyph dot <paramref name="scale"/>
        /// LEDs square.
        /// </summary>
        public static bool Lit(string line, int col, int row, float centreCol, int top, int scale, int spacing = 1)
        {
            int rowIn = row - top;
            if (rowIn < 0 || rowIn >= Height * scale)
            {
                return false;
            }

            int left = (int)System.Math.Round(centreCol - Width(line, spacing) * scale / 2f);
            int x = col - left;
            if (x < 0)
            {
                return false;
            }

            for (int i = 0; i < line.Length; i++)
            {
                string[] g = Glyph(line[i]);
                int w = g[0].Length * scale;
                if (x < w)
                {
                    return g[rowIn / scale][x / scale] == '#';
                }

                x -= w + spacing * scale;
                if (x < 0)
                {
                    return false; // the gap between glyphs
                }
            }

            return false;
        }

        /// <summary>
        /// Whether LED (col, row) is lit by a block of lines, centred horizontally in a grid <paramref name="dots"/>
        /// wide and vertically in rows [areaTop, areaTop + areaHeight).
        /// </summary>
        public static bool LitBlock(string[] lines, int col, int row, int dots, int areaTop, int areaHeight, int scale, int lineGap)
        {
            int blockHeight = lines.Length * Height * scale + (lines.Length - 1) * lineGap;
            int top = areaTop + (areaHeight - blockHeight) / 2;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Lit(lines[i], col, row, (dots - 1) / 2f + 0.5f, top, scale))
                {
                    return true;
                }

                top += Height * scale + lineGap;
            }

            return false;
        }
    }
}
