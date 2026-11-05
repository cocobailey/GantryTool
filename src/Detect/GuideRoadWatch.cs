using System;
using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Guide;
using UnityEngine;

namespace GantryTool.Detect
{
    /// <summary>
    /// Keeps filled-in guide signs up to date when a road is renamed, looking at one sign at a time so it never shows.
    /// Main thread. These changes aren't undo steps: they follow the map, not the player.
    /// </summary>
    public static class GuideRoadWatch
    {
        private const float Interval = 0.25f;
        private const int MaxLookedAt = 64;
        private static readonly List<Gantry> Gantries = new List<Gantry>();
        private static int _gantry, _sign;
        private static float _next;

        /// <summary>Not while a sign's being edited: it would redraw the panel under the player's hands.</summary>
        public static float PausedUntil;

        public static void Tick(float now)
        {
            if (now < _next || now < PausedUntil || !Settings.Current.LiveRoadNames)
            {
                return;
            }

            _next = now + Interval;
            Gantries.Clear();
            GantryManager.GetAll(Gantries);
            for (int looked = 0; looked < MaxLookedAt && Gantries.Count > 0; looked++)
            {
                if (_gantry >= Gantries.Count)
                {
                    _gantry = 0;
                }

                Gantry g = Gantries[_gantry];
                GuideHead sign = default(GuideHead);
                bool any;
                lock (GantryManager.Lock)
                {
                    any = _sign < g.GuideSigns.Count;
                    if (any)
                    {
                        sign = g.GuideSigns[_sign];
                    }
                }

                if (!any)
                {
                    _gantry++;
                    _sign = 0;
                    continue;
                }

                _sign++;
                if (sign.Design != null && sign.Design.IndexOf("\"" + GuideAutoFill.FilledKey + "\"", StringComparison.Ordinal) >= 0)
                {
                    Check(g, sign);
                    return;
                }
            }
        }

        private static void Check(Gantry g, GuideHead sign)
        {
            Dictionary<string, object> doc;
            try
            {
                doc = Json.Parse(sign.Design) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return;
            }

            if (doc == null || !RoadFinder.Refresh(g, sign, doc))
            {
                return;
            }

            string design = Json.Write(doc);
            if (design.Length > GuideData.MaxDesignLength)
            {
                return;
            }

            GantryManager.UpdateGuideSign(g, HeadRef.OfGuide(sign.Id), null, h =>
            {
                h.Design = design;
                return h;
            });
            Log.Debug("A road changed name under guide sign " + sign.Id + " on gantry #" + g.Id);
        }
    }
}
