using GantryTool.Data;

namespace GantryTool.Guide
{
    /// <summary>
    /// A copied sign: what it says and how it's sized, not where it is. It lasts until the game closes, so it can go on
    /// any gantry, even in another city. A design is self-contained, and a pasted one is a copy with no link to the first.
    /// </summary>
    public static class GuideClipboard
    {
        private static string _design;
        private static float _scale;
        private static bool _laneArrows;

        public static bool HasSign
        {
            get { return _design != null; }
        }

        public static void Copy(GuideHead sign)
        {
            _design = sign.Design;
            _scale = sign.Scale;
            _laneArrows = sign.LaneArrows;
        }

        /// <summary>A sign with the copied contents, staying where it is, facing the way it faces, over the lanes it covers.</summary>
        public static GuideHead PasteOnto(GuideHead sign)
        {
            if (_design != null)
            {
                sign.Design = _design;
                sign.Scale = _scale;
                sign.LaneArrows = _laneArrows;
            }

            return sign;
        }

        public static void Clear()
        {
            _design = null;
        }
    }
}
