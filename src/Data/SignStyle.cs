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
        Speed,
        Warning,
        Restriction
    }

    /// <summary>One pickable sign.</summary>
    public sealed class SignDef
    {
        public SignalSymbol Symbol;
        public SignCategory Category;
        public string GridName;   // short label under the preview
        public SignStyle[] Styles; // styles that have this sign; null = every style
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
            new SignDef { Symbol = SignalSymbol.Blank, Category = SignCategory.Lane, GridName = "Blank" },

            new SignDef { Symbol = SignalSymbol.Roadworks, Category = SignCategory.Warning, GridName = "Roadworks" },
            new SignDef { Symbol = SignalSymbol.Queue, Category = SignCategory.Warning, GridName = "Queue" },
            new SignDef { Symbol = SignalSymbol.Accident, Category = SignCategory.Warning, GridName = "Accident" },
            new SignDef { Symbol = SignalSymbol.Slippery, Category = SignCategory.Warning, GridName = "Slippery" },
            new SignDef { Symbol = SignalSymbol.Fog, Category = SignCategory.Warning, GridName = "Fog" },
            new SignDef { Symbol = SignalSymbol.Danger, Category = SignCategory.Warning, GridName = "Danger" },

            new SignDef { Symbol = SignalSymbol.NoOvertaking, Category = SignCategory.Restriction, GridName = "No overtaking" },
            new SignDef { Symbol = SignalSymbol.NoOvertakingTrucks, Category = SignCategory.Restriction, GridName = "No overtaking trucks" },
            new SignDef { Symbol = SignalSymbol.EndRestrictions, Category = SignCategory.Restriction, GridName = "End of limits" }
        };

        public static SignDef Find(SignalSymbol symbol)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Symbol == symbol)
                {
                    return All[i];
                }
            }

            return null;
        }

        public static bool IsAvailable(SignStyle style, SignalSymbol symbol)
        {
            SignDef def = Find(symbol);
            if (def == null || def.Styles == null)
            {
                return true;
            }

            return System.Array.IndexOf(def.Styles, style) >= 0;
        }

        /// <summary>
        /// What a style shows for a sign it doesn't have (e.g. after the gantry's style was changed). The face keeps
        /// its sign, so switching back shows it again.
        /// </summary>
        public static SignalSymbol Fallback(SignStyle style, SignalSymbol symbol)
        {
            if (IsAvailable(style, symbol))
            {
                return symbol;
            }

            SignDef def = Find(symbol);
            if (def != null && def.Category == SignCategory.Warning && IsAvailable(style, SignalSymbol.Danger))
            {
                return SignalSymbol.Danger;
            }

            return SignalSymbol.Blank;
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
        public static readonly SignCategory[] Categories =
        {
            SignCategory.Lane, SignCategory.Speed, SignCategory.Warning, SignCategory.Restriction
        };

        public static string CategoryName(SignCategory c)
        {
            switch (c)
            {
                case SignCategory.Lane: return "Lane signals";
                case SignCategory.Speed: return "Speed limits";
                case SignCategory.Warning: return "Warnings";
                case SignCategory.Restriction: return "Restrictions";
                default: return c.ToString();
            }
        }

        /// <summary>Which category a face's sign belongs to (to open the picker on it).</summary>
        public static SignCategory CategoryOf(SignalSymbol s)
        {
            if (s == SignalSymbol.SpeedAuto || s == SignalSymbol.SpeedLimit)
            {
                return SignCategory.Speed;
            }

            SignDef def = Find(s);
            return def != null ? def.Category : SignCategory.Lane;
        }
    }
}
