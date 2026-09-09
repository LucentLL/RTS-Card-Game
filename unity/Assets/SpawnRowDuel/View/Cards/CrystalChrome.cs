using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The crystal skin's frame: a faceted stone with a terminated point hanging below the card.
    ///
    /// Drawn with Painter2D rather than a texture because the shape is not a rectangle and it is
    /// element-tinted. A texture would need one bake per element (nine) per termination (three),
    /// or a greyscale sheet tinted flat - and a flat tint cannot give the bezel a different hue
    /// from the body, which is the whole read of the material.
    ///
    /// THE TANG IS ADDITIONAL. This element hangs `bottom` BELOW the card by the tang's height,
    /// so the card body keeps the size it has always had and the point costs the layout nothing.
    /// Nothing is ever drawn in the tang but stone: it is flavour, it carries no value, and
    /// CardFace never lays a band into it.
    /// </summary>
    public sealed class CrystalChrome : VisualElement
    {
        /// <summary>Tang height as a fraction of card WIDTH, by termination.</summary>
        public const float TangCreature = 0.300f;
        public const float TangStructure = 0.235f;
        public const float TangSpell = 0.375f;

        public enum Point { Creature, Structure, Spell }

        Color _body, _accent, _deep;
        Point _point = Point.Creature;
        float _tang;          // pixels
        float _shine = 0.45f;
        readonly bool _front;

        /// <param name="front">
        /// A front pass draws only the bezel, the glint and the fringe, and is added to the card
        /// LAST so it lies over the bands. Without it the whole stone sat behind opaque paper
        /// windows and the only crystal you could see was the point - reported 2026-09-09, "they
        /// look quite different than what you proposed".
        /// </param>
        public CrystalChrome(bool front = false)
        {
            _front = front;
            pickingMode = PickingMode.Ignore;      // the card's own children own every press
            // INSET TO THE CARD, and nothing else. An earlier build hung this element past the
            // card's bottom edge with a negative `bottom` and switched the card's own overflow to
            // Visible to let the point show. That put an element outside its parent's box inside
            // two ancestors that also overflow, and the game froze on summon. The card is TALLER
            // by the tang now (CardFace reserves it with a spacer band), so the point is drawn
            // inside this element and nothing overflows anything.
            style.position = Position.Absolute;
            style.left = 0;
            style.right = 0;
            style.top = 0;
            style.bottom = 0;
            generateVisualContent += Paint;
        }

        /// <summary>Re-tint and re-shape. Only touches the painter when something actually moved.</summary>
        public void Set(Color body, Color accent, Color deep, Point point, float tangPx, float shine)
        {
            if (_body == body && _accent == accent && _deep == deep
                && _point == point && Mathf.Approximately(_tang, tangPx)
                && Mathf.Approximately(_shine, shine)) return;

            _body = body; _accent = accent; _deep = deep;
            _point = point; _tang = tangPx; _shine = shine;
            MarkDirtyRepaint();
        }

        static void Poly(Painter2D p, Color fill, params Vector2[] pts)
        {
            if (pts.Length < 3) return;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
        }

        void Paint(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            // A degenerate or not-yet-resolved rect produces NaN geometry, and NaN in a painter
            // path is not a visual bug, it is a hang.
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 1f || h <= 1f) return;

            float bodyH = h - _tang;               // where the card ends and the flavour begins
            if (float.IsNaN(bodyH) || bodyH <= 1f) return;

            var p = ctx.painter2D;

            if (_front) { PaintFront(p, w, bodyH); return; }

            // ── the stone ──────────────────────────────────────────────────────────────────
            // Body first, in the element's shadow tone. Everything above this is either a
            // lighter facet or one of the card's own bands drawn by CardFace on top.
            Poly(p, _deep,
                 new Vector2(0f, 0f), new Vector2(w, 0f),
                 new Vector2(w, bodyH), new Vector2(0f, bodyH));

            // ── internal fracture planes ───────────────────────────────────────────────────
            // Deterministic, so a card does not shimmer between rebuilds. Kept under 10% or they
            // eat the ability text - the same ceiling the frame study landed on.
            uint seed = 7u;
            for (int i = 0; i < 9; i++)
            {
                seed = seed * 1103515245u + 12345u;
                float fx = ((seed >> 8) & 0xFFFF) / 65535f;
                seed = seed * 1103515245u + 12345u;
                float fy = ((seed >> 8) & 0xFFFF) / 65535f;
                seed = seed * 1103515245u + 12345u;
                float fs = ((seed >> 8) & 0xFFFF) / 65535f;

                float cx = fx * w, cy = fy * bodyH;
                float hw = w * (0.16f + fs * 0.24f), hh = bodyH * (0.07f + fs * 0.11f);
                var tint = _body;
                tint.a = 0.045f + fs * 0.05f;
                Poly(p, tint,
                     new Vector2(cx - hw, cy), new Vector2(cx, cy - hh),
                     new Vector2(cx + hw, cy), new Vector2(cx, cy + hh));
            }

            // ── the tang ───────────────────────────────────────────────────────────────────
            // Three visible faces, so the point reads as cut rather than as a paper triangle:
            // a darker left, a lit centre, a mid right.
            if (_tang > 1f)
            {
                float apex = h;
                float midL = w * 0.34f, midR = w * 0.66f;
                float shelf = _point == Point.Structure ? bodyH + _tang * 0.86f : apex;
                float tipL = _point == Point.Structure ? w * 0.30f : w * 0.5f;
                float tipR = _point == Point.Structure ? w * 0.70f : w * 0.5f;

                var left = Color.Lerp(_deep, Color.black, 0.28f); left.a = 1f;
                var mid = Color.Lerp(_body, _accent, 0.35f); mid.a = 1f;
                var right = Color.Lerp(_deep, _body, 0.45f); right.a = 1f;

                Poly(p, left, new Vector2(0f, bodyH), new Vector2(midL, bodyH), new Vector2(tipL, shelf));
                Poly(p, mid, new Vector2(midL, bodyH), new Vector2(midR, bodyH),
                             new Vector2(tipR, shelf), new Vector2(tipL, shelf));
                Poly(p, right, new Vector2(midR, bodyH), new Vector2(w, bodyH), new Vector2(tipR, shelf));
            }

            // Body shade, lower right. Depth, and it belongs BEHIND the bands - it is the stone
            // turning away from the light, not something laid over the card.
            var shade = _deep; shade.a = 0.34f;
            Poly(p, shade, new Vector2(w, 0f), new Vector2(w, bodyH), new Vector2(w * 0.42f, bodyH));
        }

        /// <summary>
        /// The pass that lies OVER the bands, so the picture and the ability box read as things
        /// suspended in the stone rather than paper windows stuck on top of it.
        ///
        /// Encasement is depth, not gloss: the dark work is all in the back pass, and the only
        /// white here is a corner glint that answers to the shine setting, because white over
        /// colour is exactly what dulls a card. Full-height prism seams were tried in the frame
        /// study and cut - they read as two white lines down the middle.
        /// </summary>
        void PaintFront(Painter2D p, float w, float bodyH)
        {
            if (_shine > 0.004f)
            {
                var glint = Color.white; glint.a = 0.16f * _shine;
                Poly(p, glint, new Vector2(0f, 0f), new Vector2(w * 0.46f, 0f), new Vector2(0f, bodyH * 0.44f));

                var fringe = _accent; fringe.a = 0.5f * _shine;
                float t = Mathf.Max(1f, w * 0.012f);
                Poly(p, fringe, new Vector2(0f, 0f), new Vector2(w, 0f),
                                new Vector2(w, t), new Vector2(0f, t));
            }

            // The bezel: the glass edge, and the one place the accent runs at full strength.
            // Stroked around the BODY only - a bezel that followed the tang would outline the
            // point and make it read as a second, separate object bolted to the card.
            float lw = Mathf.Max(1.5f, w * 0.018f);
            float i = lw * 0.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(i, i));
            p.LineTo(new Vector2(w - i, i));
            p.LineTo(new Vector2(w - i, bodyH - i));
            p.LineTo(new Vector2(i, bodyH - i));
            p.ClosePath();
            p.strokeColor = _accent;
            p.lineWidth = lw;
            p.Stroke();
        }
    }
}
