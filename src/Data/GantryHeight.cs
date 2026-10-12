using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Beam height rules, kept free of game state so they can be tested.
    ///
    /// 1.0 used two settings: the beam underside was the higher of (higher post base + post height) and
    /// (highest road crossed + road clearance). From 1.1 there is one setting, the height above the road:
    /// the beam underside sits that far above the highest road crossed, or above the higher post base when no
    /// road is crossed, but never lower than <see cref="MinPost"/> above the posts.
    ///
    /// A gantry from 1.0 is converted once, from its beam height at the time it loads, so it doesn't move.
    /// </summary>
    public static class GantryHeight
    {
        /// <summary>Posts never get shorter than this (e.g. over a road in a cutting).</summary>
        public const float MinPost = 1f;

        /// <summary>The 1.0 rule.</summary>
        public static float LegacyBeamBottom(float groundMax, float postHeight, float roadTopY, float clearance)
        {
            float y = groundMax + postHeight;
            if (!float.IsNaN(roadTopY))
            {
                y = Mathf.Max(y, roadTopY + clearance);
            }

            return y;
        }

        /// <summary>The 1.1 rule.</summary>
        public static float BeamBottom(float groundMax, float roadTopY, float heightAboveRoad)
        {
            float y = (float.IsNaN(roadTopY) ? groundMax : roadTopY) + heightAboveRoad;
            return Mathf.Max(y, groundMax + MinPost);
        }

        /// <summary>
        /// The height-above-road that puts a 1.0 gantry's beam exactly where 1.0 put it, given the roads it
        /// crosses right now.
        /// </summary>
        public static float Convert(float groundMax, float postHeight, float roadTopY, float clearance)
        {
            float legacy = LegacyBeamBottom(groundMax, postHeight, roadTopY, clearance);
            return legacy - (float.IsNaN(roadTopY) ? groundMax : roadTopY);
        }

        /// <summary>
        /// The post height 1.0 would need to draw this beam (written to the 1.0-format save block, so a save
        /// opened in 1.0 shows practically the same heights).
        /// </summary>
        public static float EquivalentPostHeight(float groundMax, float beamBottom)
        {
            return Mathf.Clamp(beamBottom - groundMax, MinPost, 100f);
        }
    }
}
