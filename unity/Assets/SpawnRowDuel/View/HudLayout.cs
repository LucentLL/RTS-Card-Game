using System.Collections.Generic;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The HUD's reserved screen geometry, in REAL pixels - and the one place that decides it.
    ///
    /// Three kinds:
    ///
    /// - The WALL RAILS (TopPx/BottomPx): how much of each castle wall shows when it is retracted,
    ///   which is the state the board is played in. These are the margins the board is framed
    ///   inside, NOT a camera viewport: the camera renders the whole screen and the walls are
    ///   drawn over it, so the field runs behind the battlements instead of stopping at a bar.
    /// - The HAND PEEKS: how much of a resting card shows. Bigger than the rail it hangs off, so
    ///   the cards stand a little proud of the wall the way a held hand does.
    /// - MenuPx/LogPx/RailPx: IMGUI panels drawn over the field (build menu, log, turn rail).
    ///   Legacy Input cannot see IMGUI consume an event - Update even runs before the frame's GUI
    ///   events - so BoardInput must be told where they are and refuse taps/hover there, or a
    ///   menu tap would also tap the board cell behind it.
    ///
    /// The sizes live here rather than in MatchHud because the hand and the walls are UI Toolkit
    /// surfaces and lay themselves out in LateUpdate, before OnGUI has run at all. When MatchHud
    /// owned the numbers, the first frames of the hand used a fallback guess and the cards sat
    /// clipped against the bottom of the screen.
    ///
    /// Rects are stored top-left-origin (GUI space, scaled to real pixels); Blocks() flips the
    /// bottom-left-origin mouse position to match.
    /// </summary>
    public static class HudLayout
    {
        // ── logical units; Scale turns them into pixels ──────────────────────────────────

        /// <summary>
        /// The foe's wall, RETRACTED: just enough to read their life pool off the rail. Their
        /// hand hangs from it and stands proud of it, so the wall itself needs no more height
        /// than the number it carries.
        /// </summary>
        public const float FoeRailH = 30f;

        /// <summary>Your wall, retracted. The hand is held in front of it, not inside it.</summary>
        public const float YouRailH = 30f;

        /// <summary>
        /// A wall EXTENDED: the full tower windows - life, mana, piles, workers (spec 09 §4.2).
        /// A wall opens when it is looked at and closes when it is not, so this height is only
        /// ever borrowed from the field for as long as you are reading it.
        /// </summary>
        public const float WallFullH = 104f;

        /// <summary>
        /// How far the battlements rise PAST the rail and into the field, drawn with alpha so the
        /// board shows between the merlons. Without it the wall is a rectangle, and a rectangle at
        /// the edge of the screen is a letterbox bar, not a castle.
        /// </summary>
        public const float WallOverhang = 14f;

        /// <summary>
        /// The hand PEEK: how much of a resting card shows, measured from the screen edge. The
        /// cards themselves are ~2.9x this and hang off the edge until one is picked (spec 09
        /// §5.1), so the board gives up a banner's worth of screen rather than a whole card's.
        ///
        /// It is deliberately TALLER than the rail: a hand held at a wall stands proud of it, and
        /// a card stopped short of the screen edge with a lip of stone under it looks like a card
        /// stuck to a wall.
        /// </summary>
        public const float HandH = 48f;

        /// <summary>Their hand, hanging from their rail. Backs only, so it needs less.</summary>
        public const float FoeHandH = 44f;

        public const float ModeH = 28f;

        /// <summary>Retired: the action row moved to the right-edge rail (SRD_Tile-era layout).
        /// Kept only so an external reference does not silently resolve to something else.</summary>
        public const float ActionH = 0f;

        // ── resolved pixels ─────────────────────────────────────────────────────────────

        public static float Scale = 1f;

        /// <summary>The board's framing margins: the hand peeks, which are the deepest thing at
        /// each edge that the board must stay clear of.</summary>
        public static float TopPx;
        public static float BottomPx;

        public static float HandBandPx;
        public static float FoeHandBandPx;
        public static float RailTopPx;
        public static float RailBottomPx;

        /// <summary>
        /// What the walls are CURRENTLY covering, published by WallBands each frame. Taps inside
        /// these never reach the board - and unlike the rails they grow when a wall opens, so a
        /// tap on an extended wall does not also tap the cell it is covering.
        /// </summary>
        public static float TopBlockPx;
        public static float BottomBlockPx;

        public static Rect MenuPx;    // Rect zero when no menu is open
        public static Rect LogPx;
        public static Rect RailPx;    // the right-edge turn controls

        /// <summary>
        /// What the SHELL is holding over the battle - today the ↩ abandon / ↩ menu row it hangs
        /// off the top right corner. Rect zero when there is nothing there.
        ///
        /// It is published for the same reason the three above are, plus one more. Those are IMGUI
        /// panels that legacy Input cannot see; this is a UI TOOLKIT button that IMGUI cannot see
        /// either - the two are separate input paths and neither learns about the other's handled
        /// events. So an IMGUI control drawn under it runs on the same tap that presses it, which
        /// is exactly how closing the match log also abandoned the match. MatchHud keeps its own
        /// panels clear of this rect, and Blocks() stops the board taking the tap as well.
        /// </summary>
        public static Rect ShellPx;

        /// <summary>
        /// Every IMGUI CONTROL drawn over the field this frame, in real pixels.
        ///
        /// The three rects above cover PANELS, and a panel is easy to remember. A loose button is
        /// not: the attack row's ⚔ WALL sat straight on the board, was never published, and a tap
        /// on it went through to the cell underneath - so aiming at the wall selected whatever was
        /// standing in front of it. Naming one more rect would have fixed that one button and left
        /// the next one to be discovered the same way, so the registration moved into the drawing
        /// itself: MatchHud.Btn registers what it draws, and a control that is drawn is blocked.
        ///
        /// Rebuilt on every OnGUI pass, so it is never a frame stale in the direction that matters
        /// (a control that has gone away stops blocking on the same pass it stops being drawn).
        /// </summary>
        static readonly List<Rect> Controls = new List<Rect>();

        public static void ClearControls() { Controls.Clear(); }

        /// <summary>Register a control's rect, in the GUI's own scaled units.</summary>
        public static void Control(Rect logical)
        {
            Controls.Add(new Rect(logical.x * Scale, logical.y * Scale,
                                  logical.width * Scale, logical.height * Scale));
        }

        /// <summary>Scale by the SHORT side - landscape must not inherit portrait's width math.</summary>
        public static float Recompute()
        {
            Scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
            RailTopPx = FoeRailH * Scale;
            RailBottomPx = YouRailH * Scale;
            HandBandPx = HandH * Scale;
            FoeHandBandPx = FoeHandH * Scale;
            TopPx = FoeHandBandPx;
            BottomPx = HandBandPx;
            if (TopBlockPx < RailTopPx) TopBlockPx = RailTopPx;
            if (BottomBlockPx < RailBottomPx) BottomBlockPx = RailBottomPx;
            return Scale;
        }

        public static bool Blocks(Vector2 mousePos)
        {
            var p = new Vector2(mousePos.x, Screen.height - mousePos.y);
            if (p.y <= TopBlockPx || p.y >= Screen.height - BottomBlockPx) return true;
            if (MenuPx.Contains(p) || LogPx.Contains(p) || RailPx.Contains(p)) return true;
            if (ShellPx.width > 0f && ShellPx.Contains(p)) return true;
            for (int i = 0; i < Controls.Count; i++) if (Controls[i].Contains(p)) return true;
            return false;
        }
    }
}
