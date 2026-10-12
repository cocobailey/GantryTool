using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Speed values. Signs store km/h whatever their style, and show it in the style's units rounded to the values
    /// real signs use, so switching a gantry between km/h and mph styles keeps the same real speed.
    /// </summary>
    public static class SpeedUnits
    {
        public const float KmhPerMph = 1.609344f;

        /// <summary>Speeds offered in km/h styles.</summary>
        public static readonly int[] Kmh = { 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130 };

        /// <summary>Speeds offered in mph styles.</summary>
        public static readonly int[] Mph = { 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80 };

        public static int[] Values(bool mph)
        {
            return mph ? Mph : Kmh;
        }

        /// <summary>The value a sign shows for <paramref name="kmh"/>: nearest offered value in the units.</summary>
        public static int Display(int kmh, bool mph)
        {
            int[] values = Values(mph);
            float v = mph ? kmh / KmhPerMph : kmh;
            int best = values[0];
            float bestD = float.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                float d = Mathf.Abs(values[i] - v);
                if (d < bestD)
                {
                    bestD = d;
                    best = values[i];
                }
            }

            return best;
        }

        /// <summary>The km/h to store for a value picked in the style's units.</summary>
        public static ushort ToKmh(int shown, bool mph)
        {
            return (ushort)Mathf.RoundToInt(mph ? shown * KmhPerMph : shown);
        }
    }
}
