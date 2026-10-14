using System.Collections.Generic;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    public enum TileKind
    {
        Lane,      // one of the 1.0 lane signs (row 1, unchanged since 1.0)
        Housing,   // plain housing colour for the box sides
        Empty,
        Speed,     // speed limit; Shown = number on the sign in the style's units
        Pictogram, // warning or restriction sign
        Lamp       // beacon lens above a speed sign; Shown = 1 when lit
    }

    public struct TileSpec
    {
        public TileKind Kind;
        public SignalSymbol Symbol;
        public int Shown;
    }

    /// <summary>
    /// Layout of one style's sign atlas: 8 columns of 256 px tiles, row by row from the bottom of the texture.
    /// Row 1 is always the 1.0 layout (Open, Closed, Merge left, Merge right, Reserved, Blank, housing, empty), so
    /// the 1.0 signs keep their exact pixels; the style's speed limits follow.
    /// </summary>
    public sealed class SignAtlas
    {
        public const int Tile = 256;
        public const int Columns = 8;
        public const int HousingTile = 6;
        private const int BlankTile = 5;

        public readonly SignStyle Style;
        public readonly bool Mph;
        public readonly List<TileSpec> Tiles = new List<TileSpec>();
        private readonly Dictionary<int, int> _speedTile = new Dictionary<int, int>();
        private readonly Dictionary<SignalSymbol, int> _pictogramTile = new Dictionary<SignalSymbol, int>();
        private readonly Dictionary<SignalSymbol, int> _extraLaneTile = new Dictionary<SignalSymbol, int>();

        /// <summary>Beacon lens tiles (unlit / lit), or -1 when the style has no beacon.</summary>
        public readonly int LampOffTile = -1;
        public readonly int LampOnTile = -1;

        public int Width => Tile * Columns;
        public int Rows => (Tiles.Count + Columns - 1) / Columns;

        /// <summary>Texture height: rows rounded up to a power of two (mip chains want power-of-two sizes).</summary>
        public int Height
        {
            get
            {
                int h = Tile;
                while (h < Rows * Tile)
                {
                    h *= 2;
                }

                return h;
            }
        }

        private SignAtlas(SignStyle style)
        {
            Style = style;
            Mph = SignStyles.UsesMph(style);

            // Row 1: exactly the 1.0 atlas.
            for (int s = (int)SignalSymbol.Arrow; s <= (int)SignalSymbol.Blank; s++)
            {
                Tiles.Add(new TileSpec { Kind = TileKind.Lane, Symbol = (SignalSymbol)s });
            }

            Tiles.Add(new TileSpec { Kind = TileKind.Housing });
            Tiles.Add(new TileSpec { Kind = TileKind.Empty });

            // Lane signs only some styles have.
            if (SignCatalog.IsAvailable(style, SignalSymbol.YellowCross))
            {
                _extraLaneTile[SignalSymbol.YellowCross] = Tiles.Count;
                Tiles.Add(new TileSpec { Kind = TileKind.Lane, Symbol = SignalSymbol.YellowCross });
            }

            // Speed limits in the style's units.
            foreach (int v in SpeedUnits.Values(Mph))
            {
                _speedTile[v] = Tiles.Count;
                Tiles.Add(new TileSpec { Kind = TileKind.Speed, Symbol = SignalSymbol.SpeedLimit, Shown = v });
            }

            if (SignStyles.HasBeacon(style))
            {
                LampOffTile = Tiles.Count;
                Tiles.Add(new TileSpec { Kind = TileKind.Lamp, Shown = 0 });
                LampOnTile = Tiles.Count;
                Tiles.Add(new TileSpec { Kind = TileKind.Lamp, Shown = 1 });
            }

            // Warnings and restrictions this style has.
            foreach (SignDef def in SignCatalog.All)
            {
                if ((def.Category == SignCategory.Warning || def.Category == SignCategory.Restriction) &&
                    SignCatalog.IsAvailable(style, def.Symbol))
                {
                    _pictogramTile[def.Symbol] = Tiles.Count;
                    Tiles.Add(new TileSpec { Kind = TileKind.Pictogram, Symbol = def.Symbol });
                }
            }
        }

        public static SignAtlas For(SignStyle style)
        {
            return new SignAtlas(style);
        }

        /// <summary>The tile showing a resolved face sign (anything without a tile draws as Blank).</summary>
        public int TileOf(FaceSign f)
        {
            // Signs this style doesn't have show its nearest equivalent; the face keeps its own sign.
            f = new FaceSign(SignCatalog.Fallback(Style, f.Symbol), f.Value);
            int s = (int)f.Symbol;
            if (s >= (int)SignalSymbol.Arrow && s <= (int)SignalSymbol.Blank)
            {
                return s - 1;
            }

            int tile;
            if (_pictogramTile.TryGetValue(f.Symbol, out tile) || _extraLaneTile.TryGetValue(f.Symbol, out tile))
            {
                return tile;
            }

            if (f.Symbol == SignalSymbol.SpeedLimit)
            {
                if (_speedTile.TryGetValue(SpeedUnits.Display(f.Value, Mph), out tile))
                {
                    return tile;
                }
            }

            return BlankTile;
        }

        /// <summary>UV rect of a tile (x, y, width, height).</summary>
        public Rect TileRect(int tile)
        {
            float h = (float)Tile / Height;
            return new Rect((tile % Columns) / (float)Columns, (tile / Columns) * h, 1f / Columns, h);
        }
    }
}
