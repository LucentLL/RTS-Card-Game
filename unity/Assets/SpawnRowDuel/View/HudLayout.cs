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

        /// <summary>
        /// The foe's wall. Taller than the old status strip because it carries their whole side
        /// of the game now - life, mana, piles, workers, the turn read-out and their HAND, which
        /// was never drawn at all.
        /// </summary>
        public const float TopH = 58f;

        /// <summary>
        /// How far the battlements rise PAST their band and into the field, drawn with alpha so
        /// the board shows between the merlons. Without it the wall is a rectangle, and a
        /// rectangle at the edge of the screen is a letterbox bar, not a castle.
        /// </summary>
        public const float WallOverhang = 14f;
        /// <summary>Retired: the action row moved to the right-edge rail (SRD_Tile-era layout).
        /// Kept only so an external reference does not silently resolve to something else.</summary>
        public const float ActionH = 0f;
        /// <summary>
        /// The hand PEEK: how much of a resting card shows. The cards themselves are ~2.9x this
        /// and hang below the edge until one is picked (spec 09 §5.1), so the board only has to
        /// give up a banner's worth of screen rather than a whole card's.
        /// </summary>
        public const float HandH = 44f;
        public const float ModeH = 28f;

        /// <summary>
        /// Your wall: the hand PEEK plus the stone lip it is held in front of.
        ///
        /// It used to be hand + mode row + action row, 120 units of a 480-unit screen. The
        /// reference build does not do that - the turn controls hug the right edge at mid-height
        /// (`#boardBtns`, the Master Duel coin position), so the board keeps its height and the
        /// buttons cost it nothing. What it DOES spend the band on is the wall itself: your
        /// vitals live in its left tower and the hand across its middle span (spec 09 §4.2).
        /// </summary>
        public const float BottomH = 58f;

        public static float TopPx;
        public static float BottomPx;
        public static float Scale = 1f;
        public static float HandBandPx;
        public static float HandBandBottomPx;

        public static Rect MenuPx;    // Rect zero when no menu is open
        public static Rect LogPx;
        public static Rect RailPx;    // the right-edge turn controls, drawn INSIDE the viewport

        /// <summary>Scale by the SHORT side - landscape must not inherit portrait's width math.</summary>
        public static float Recompute()
        {
            Scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
            TopPx = TopH * Scale;
            BottomPx = BottomH * Scale;
            HandBandPx = HandH * Scale;
            // the cards are held IN FRONT of your wall, so a lip of stone shows below them
            HandBandBottomPx = (BottomH - HandH) * Scale;
            return Scale;
        }

        public static bool Blocks(Vector2 mousePos)
        {
            var p = new Vector2(mousePos.x, Screen.height - mousePos.y);
            return MenuPx.Contains(p) || LogPx.Contains(p) || RailPx.Contains(p);
        }
    }
}
