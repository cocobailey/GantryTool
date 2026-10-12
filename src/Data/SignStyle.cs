using System.Collections.Generic;

namespace GantryTool.Data
{
    /// <summary>Regional look of a gantry's signals. Values are saved — append only, never renumber.</summary>
    public enum SignStyle : byte
    {
        Default = 0,        // per gantry: follow the "Default sign style" option
        Europe = 1,         // the 1.0 look
        Netherlands = 2,
        UnitedKingdom = 3,
        UnitedStates = 4,
        Canada = 5,
        AustraliaNz = 6
    }

    public static class SignStyles
    {
        /// <summary>Styles that can be picked. Grows as styles are drawn; anything else falls back.</summary>
        public static readonly SignStyle[] Available = { SignStyle.Europe };

        public static bool IsAvailable(SignStyle s)
        {
            for (int i = 0; i < Available.Length; i++)
            {
                if (Available[i] == s)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The style a gantry actually draws with. "Default", unknown values (e.g. from a newer version) and
        /// styles not available in this version all resolve to the default option, then to Europe.
        /// </summary>
        public static SignStyle Resolve(byte gantryStyle)
        {
            var s = (SignStyle)gantryStyle;
            if (s == SignStyle.Default || !IsAvailable(s))
            {
                s = (SignStyle)Settings.Current.DefaultStyle;
            }

            return IsAvailable(s) ? s : SignStyle.Europe;
        }

        /// <summary>Styles that show speeds in mph.</summary>
        public static bool UsesMph(SignStyle s)
        {
            return s == SignStyle.UnitedKingdom || s == SignStyle.UnitedStates;
        }

        public static string Name(SignStyle s)
        {
            switch (s)
            {
                case SignStyle.Europe: return "Europe (DE/AT/CH)";
                case SignStyle.Netherlands: return "Netherlands";
                case SignStyle.UnitedKingdom: return "United Kingdom";
                case SignStyle.UnitedStates: return "United States";
                case SignStyle.Canada: return "Canada";
                case SignStyle.AustraliaNz: return "Australia / NZ";
                default: return "Default";
            }
        }
    }

    /// <summary>Groups of signs in the signal picker.</summary>
    public enum SignCategory
    {
        Lane,
        Speed
    }

    /// <summary>One pickable sign.</summary>
    public sealed class SignDef
    {
        public SignalSymbol Symbol;
        public SignCategory Category;
        public string GridName;   // short label under the preview
    }

    /// <summary>Every sign that can be picked, and which styles have it.</summary>
    public static class SignCatalog
    {
        public static readonly SignDef[] All =
        {
            new SignDef { Symbol = SignalSymbol.Arrow, Category = SignCategory.Lane, GridName = "Open" },
            new SignDef { Symbol = SignalSymbol.Cross, Category = SignCategory.Lane, GridName = "Closed" },
            new SignDef { Symbol = SignalSymbol.MergeLeft, Category = SignCategory.Lane, GridName = "Merge left" },
            new SignDef { Symbol = SignalSymbol.MergeRight, Category = SignCategory.Lane, GridName = "Merge right" },
            new SignDef { Symbol = SignalSymbol.Reserved, Category = SignCategory.Lane, GridName = "Reserved" },
            new SignDef { Symbol = SignalSymbol.Blank, Category = SignCategory.Lane, GridName = "Blank" }
        };

        public static bool IsAvailable(SignStyle style, SignalSymbol symbol)
        {
            return true; // every style has every lane sign so far
        }

        public static List<SignDef> InCategory(SignStyle style, SignCategory category)
        {
            var list = new List<SignDef>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Category == category && IsAvailable(style, All[i].Symbol))
                {
                    list.Add(All[i]);
                }
            }

            return list;
        }

        /// <summary>Categories shown in the picker's dropdown, in order.</summary>
        public static readonly SignCategory[] Categories = { SignCategory.Lane, SignCategory.Speed };

        public static string CategoryName(SignCategory c)
        {
            switch (c)
            {
                case SignCategory.Lane: return "Lane signals";
                case SignCategory.Speed: return "Speed limits";
                default: return c.ToString();
            }
        }

        /// <summary>Which category a face's sign belongs to (to open the picker on it).</summary>
        public static SignCategory CategoryOf(SignalSymbol s)
        {
            return s == SignalSymbol.SpeedAuto || s == SignalSymbol.SpeedLimit ? SignCategory.Speed : SignCategory.Lane;
        }
    }
}
