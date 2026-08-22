using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The HUD's reserved screen geometry, in REAL pixels, published by MatchHud every OnGUI and
    /// consumed by BoardInput. Two kinds:
    ///
    /// - TopPx/BottomPx: the opaque bands. The camera viewport stays out of them, Master-Duel
    ///   style, so board and interface never layer over each other.
    /// - MenuPx/LogPx: IMGUI panels drawn INSIDE the camera viewport (build menu, log). Legacy
    ///   Input cannot see IMGUI consume an event - Update even runs before the frame's GUI
    ///   events - so BoardInput must be told where they are and refuse taps/hover there,
    ///   or a menu tap would also tap the board cell behind it.
    ///
    /// Rects are stored top-left-origin (GUI space, scaled to real pixels); Blocks() flips the
    /// bottom-left-origin mouse position to match.
    /// </summary>
    public static class HudLayout
    {
        public static float TopPx;
        public static float BottomPx;

        /// <summary>
        /// The logical→pixel factor and the hand band, published for the UI Toolkit surfaces that
        /// are replacing IMGUI one at a time (M13). They have to land on exactly the pixels the
        /// IMGUI bands reserve, or the board camera and the new surface disagree about who owns a
        /// strip of screen and taps fall through.
        /// </summary>
        public static float Scale = 1f;
        public static float HandBandPx;
        public static float HandBandBottomPx;

        public static Rect MenuPx;    // Rect zero when no menu is open
        public static Rect LogPx;

        public static bool Blocks(Vector2 mousePos)
        {
            var p = new Vector2(mousePos.x, Screen.height - mousePos.y);
            return MenuPx.Contains(p) || LogPx.Contains(p);
        }
    }
}
