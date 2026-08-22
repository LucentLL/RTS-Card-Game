using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The HUD's reserved screen geometry, in REAL pixels - and the one place that decides it.
    ///
    /// Two kinds:
    ///
    /// - TopPx/BottomPx: the opaque bands. The camera viewport stays out of them, Master-Duel
    ///   style, so board and interface never layer over each other.
    /// - MenuPx/LogPx: IMGUI panels drawn INSIDE the camera viewport (build menu, log). Legacy
    ///   Input cannot see IMGUI consume an event - Update even runs before the frame's GUI
    ///   events - so BoardInput must be told where they are and refuse taps/hover there,
    ///   or a menu tap would also tap the board cell behind it.
    ///
    /// The band SIZES live here rather than in MatchHud because the hand is a UI Toolkit surface
    /// now and lays itself out in LateUpdate, before OnGUI has run at all. When MatchHud owned the
    /// numbers, the first frames of the hand used a fallback guess and the cards sat clipped
    /// against the bottom of the screen - which is exactly what the first capture showed.
    ///
    /// Rects are stored top-left-origin (GUI space, scaled to real pixels); Blocks() flips the
    /// bottom-left-origin mouse position to match.
    /// </summary>
    public static class HudLayout
    {
        // logical units; the scale below turns them into pixels
        public const float TopH = 42f;
        public const float ActionH = 46f;
        /// <summary>
        /// The hand PEEK: how much of a resting card shows. The cards themselves are ~2.9x this
        /// and hang below the edge until one is picked (spec 09 §5.1), so the board only has to
        /// give up a banner's worth of screen rather than a whole card's.
        /// </summary>
        public const float HandH = 46f;
        public const float ModeH = 28f;
        public const float BottomH = ActionH + HandH + ModeH;

        public static float TopPx;
        public static float BottomPx;
        public static float Scale = 1f;
        public static float HandBandPx;
        public static float HandBandBottomPx;

        public static Rect MenuPx;    // Rect zero when no menu is open
        public static Rect LogPx;

        /// <summary>Scale by the SHORT side - landscape must not inherit portrait's width math.</summary>
        public static float Recompute()
        {
            Scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
            TopPx = TopH * Scale;
            BottomPx = BottomH * Scale;
            HandBandPx = HandH * Scale;
            HandBandBottomPx = ActionH * Scale;
            return Scale;
        }

        public static bool Blocks(Vector2 mousePos)
        {
            var p = new Vector2(mousePos.x, Screen.height - mousePos.y);
            return MenuPx.Contains(p) || LogPx.Contains(p);
        }
    }
}
