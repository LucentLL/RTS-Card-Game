using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Where a board cell LANDS ON SCREEN, and the one place that knows it.
    ///
    /// The drag-select gestures need to ask "is this cell under my finger", and that question has
    /// three traps in it that are each easy to get wrong separately:
    ///
    /// 1. TWO COORDINATE SPACES. Unity's Input.mousePosition is bottom-left origin in DEVICE
    ///    pixels; UI Toolkit's panel - which is what HandBar.TryProject answers in, and what the
    ///    vitals chips are already positioned in - is top-left origin in PANEL units. On WebGL
    ///    with a devicePixelRatio those are not even the same scale. Everything here works in
    ///    PANEL space and <see cref="ScreenToPanel"/> is the only bridge.
    ///
    /// 2. THE CELL IS NOT WHERE THE UNIT IS. A card lies flat and fills its tile; the figure
    ///    standing on it is planted a third of a tile toward the camera and rises about one and a
    ///    half tile-heights. A player aiming at a creature aims at the FIGURE. Hit-testing the
    ///    tile alone misses the thing they are looking at by most of its height.
    ///
    /// 3. THE CORNERS ARE NOT IN A KNOWN ORDER. The camera pitches between 84 and 42 degrees and
    ///    yaws a half turn for the guest seat, so "top-left" and "bottom-right" swap. Every rect
    ///    here is built with Min/Max over all four projected corners; nothing indexes them.
    /// </summary>
    public static class BoardProjection
    {
        /// <summary>
        /// Device pixels (bottom-left origin) to panel units (top-left origin).
        ///
        /// The same conversion UnitVitals does when it places a chip - kept in one place rather
        /// than re-derived, because getting the Y flip wrong is silent: the selection simply
        /// picks the wrong half of the board and looks like a hit-test bug.
        /// </summary>
        public static Vector2 ScreenToPanel(Vector2 devicePx, Vector2 panel)
        {
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            return new Vector2(devicePx.x / w * panel.x, (1f - devicePx.y / h) * panel.y);
        }

        /// <summary>The tile's own face, projected. False when any corner is behind the camera.</summary>
        public static bool TryCellRect(HandBar hand, Camera cam, BoardView board, CellRef cell,
                                       out Rect rect)
        {
            rect = default(Rect);
            if (hand == null || cam == null || board == null || !hand.PanelReady) return false;

            var foot = CardPlateLayer.Footprint(board);
            var c = board.WorldOf(cell);
            float hx = foot.x * 0.5f, hz = foot.y * 0.5f;

            Vector2 a, b, d, e;
            if (!hand.TryProject(cam, c + new Vector3(-hx, 0f, -hz), out a)) return false;
            if (!hand.TryProject(cam, c + new Vector3(hx, 0f, -hz), out b)) return false;
            if (!hand.TryProject(cam, c + new Vector3(hx, 0f, hz), out d)) return false;
            if (!hand.TryProject(cam, c + new Vector3(-hx, 0f, hz), out e)) return false;

            rect = Bounds(a, b, d, e);
            return true;
        }

        /// <summary>
        /// The tile UNION the figure standing on it - what the player is actually pointing at.
        ///
        /// The figure's height is measured the way StandeeLayer sizes it, through the camera at
        /// the figure's own depth, because an upright billboard's screen height is not its world
        /// height at this tilt.
        /// </summary>
        public static bool TryUnitBox(HandBar hand, Camera cam, BoardView board, CellRef cell,
                                      bool structure, out Rect rect)
        {
            if (!TryCellRect(hand, cam, board, cell, out rect)) return false;

            float tileW = board.CellSize, tileD = board.CellSize * board.RowStretch;
            var ground = board.WorldOf(cell) + StandeeLayer.FeetShift(board, structure);

            float screenH, screenW, upPerWorld, rightPerWorld;
            if (!StandeeLayer.MeasureTile(cam, ground, tileW, tileD,
                                          out screenH, out screenW, out upPerWorld, out rightPerWorld))
                return true;                                  // the tile alone is still honest

            // the sizes StandeeLayer draws a creature at, in world units, then back to screen
            float h = Mathf.Min(1.50f * screenH, 1.20f * screenW);
            float w = Mathf.Min(1.65f * screenW, h);

            Vector2 foot;
            if (!hand.TryProject(cam, ground, out foot)) return true;

            // MeasureTile answers in device pixels; the rect is in panel units
            var panel = hand.PanelSize();
            float k = panel.y / Mathf.Max(1f, Screen.height);
            float ph = h * k, pw = w * k;

            var fig = new Rect(foot.x - pw * 0.5f, foot.y - ph, pw, ph);   // stands UP from its feet
            rect = Union(rect, fig);
            return true;
        }

        /// <summary>A cell hidden behind an open castle wall is not selectable - the player cannot
        /// see it, so a gesture must not silently take it.</summary>
        public static bool HiddenByWalls(float panelY, Vector2 panel)
        {
            float k = panel.y / Mathf.Max(1f, Screen.height);
            return panelY < HudLayout.TopBlockPx * k || panelY > panel.y - HudLayout.BottomBlockPx * k;
        }

        static Rect Bounds(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float y0 = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float y1 = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        static Rect Union(Rect a, Rect b)
        {
            float x0 = Mathf.Min(a.xMin, b.xMin), x1 = Mathf.Max(a.xMax, b.xMax);
            float y0 = Mathf.Min(a.yMin, b.yMin), y1 = Mathf.Max(a.yMax, b.yMax);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }
    }
}
