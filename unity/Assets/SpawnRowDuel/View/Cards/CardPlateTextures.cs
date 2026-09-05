using System.Collections.Generic;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The board plate's frame, rastered - the same DM card anatomy as <see cref="CardFace"/>, at
    /// the size a card actually occupies lying on a tile.
    ///
    /// Why a raster and not the real CardFace: a plate is ~80 screen pixels tall in the tilted
    /// view, where the banner is 12 px and the ability box 17. No text in that band is legible, so
    /// the frame carries the card's SHAPE - ivory banner, element ring, art window, ruled ability
    /// box, dark stat bar - and the name and stats stay where they can be read, in the overlay
    /// above the unit. Nothing here duplicates CardFace's typography, only its proportions, and
    /// those come from the same numbers (spec 09 6.1): banner .155 of the height, art .479,
    /// rules .211, stats .155 - which is what CardFace's 3.3 : 1.45 flex split resolves to.
    ///
    /// One texture per element, not per card: the art is a separate quad laid into the window, so
    /// nine textures cover the whole registry. The face-down sleeve is the reference build's
    /// procedural back (05_overlays_screens.css) - tinted body, diagonal weave, haloed emblem,
    /// double border - tinted by its OWNER's element, never the hidden card's, because that is a
    /// secret and a texture key is a place a secret can leak from.
    /// </summary>
    public static class CardPlateTextures
    {
        public const int W = 96;
        public const int H = 133;                  // 96 * 1033/744, the physical card proportion

        // fractions of the card's HEIGHT, top to bottom
        public const float BannerH = 0.155f;
        public const float ArtH = 0.479f;
        public const float RulesH = 0.211f;
        public const float StatsH = 0.155f;
        /// <summary>
        /// How far the art window is inset from each edge, as a fraction of the WIDTH.
        ///
        /// 0.168, which is what makes the window SQUARE: ArtH * H = 0.479 * 133 = 63.7 texels
        /// tall, and (1 - 2 * 0.168) * 96 = 63.7 wide. Every card illustration in this project is
        /// square, and at 0.035 the window was 89 x 64 - a third of every picture cropped away to
        /// fill a letterbox nothing was drawn for. A real trading card insets its art box and
        /// shows frame either side for exactly this reason.
        /// </summary>
        public const float ArtInsetX = 0.168f;

        static readonly Dictionary<string, Texture2D> _fronts = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, Texture2D> _backs = new Dictionary<int, Texture2D>();
        static readonly Dictionary<Texture2D, Sprite> _sprites = new Dictionary<Texture2D, Sprite>();

        public static Sprite Front(ElementPalette.Swatch sw)
        {
            Texture2D tex;
            if (!_fronts.TryGetValue(sw.Name, out tex) || tex == null)
            {
                tex = BuildFront(sw);
                _fronts[sw.Name] = tex;
            }
            return SpriteOf(tex);
        }

        public static Sprite Back(Color sleeve)
        {
            int key = ((Color32)sleeve).GetHashCode();
            Texture2D tex;
            if (!_backs.TryGetValue(key, out tex) || tex == null)
            {
                tex = BuildBack(sleeve);
                _backs[key] = tex;
            }
            return SpriteOf(tex);
        }

        /// <summary>
        /// The sprite over a cached texture - and the reason every lookup above ends in
        /// `|| tex == null`.
        ///
        /// These caches hand the SAME Sprite object to a SpriteRenderer over and over, for the
        /// life of the session. A destroyed entry is therefore not a cache miss that costs a
        /// rebuild, it is a live SpriteRenderer pointing at freed native memory - and the thing
        /// that walks those is Unity's own render-node preparation, in a job, during culling.
        /// It does not fault where the mistake was made; on WebGL it comes back as
        ///
        ///     RuntimeError: index out of bounds
        ///       at PrepareSpriteRenderNodes&lt;true&gt;(RenderNodeQueuePrepareThreadContext&amp;)
        ///
        /// which is a wasm TABLE index - a call through a vtable that is no longer there - and
        /// reads like a bug in the renderer rather than in a dictionary.
        ///
        /// WallTextures.Band already learned this exact lesson (see its `_bands` comment: a freed
        /// band left UI Toolkit sampling a stale texture slot and put white shards through the
        /// hand). Same family, different renderer. A Unity Object compares == null once destroyed,
        /// so rebuilding on that is the whole fix; nothing here ever hands back a corpse.
        /// </summary>
        static Sprite SpriteOf(Texture2D tex)
        {
            if (tex == null) return null;          // a build that failed is not a sprite

            Sprite s;
            if (_sprites.TryGetValue(tex, out s) && s != null) return s;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), H);
            s.name = tex.name;
            s.hideFlags = HideFlags.HideAndDontSave;
            _sprites[tex] = s;
            return s;
        }

        // -- the face-up frame -------------------------------------------------------------

        static Texture2D BuildFront(ElementPalette.Swatch sw)
        {
            var ec = sw.Color;
            var tex = New("SRD Plate " + sw.Name);
            var px = new Color[W * H];

            var edge = ElementPalette.Mix(ec, Color.black, 0.55f);      // the outer border
            var ring = ElementPalette.Mix(ec, Color.black, 0.6f);       // the art window's ring
            var wash = ElementPalette.Mix(sw.Deep, Color.black, 0.55f); // behind missing art (G1)
            var bar = new Color(0.078f, 0.066f, 0.051f);                // the stat bar

            Fill(px, edge);

            int bannerBot = Mathf.RoundToInt(H * BannerH);
            int artBot = Mathf.RoundToInt(H * (BannerH + ArtH));
            int rulesBot = Mathf.RoundToInt(H * (BannerH + ArtH + RulesH));
            int inset = Mathf.Max(2, Mathf.RoundToInt(W * ArtInsetX));

            // banner: the ivory plate, ruled off with the element accent
            for (int y = 2; y < bannerBot; y++)
                for (int x = 2; x < W - 2; x++)
                    px[I(x, y)] = Paper(y / (float)bannerBot);
            for (int x = 2; x < W - 2; x++) { px[I(x, bannerBot - 2)] = ec; px[I(x, bannerBot - 1)] = ec; }

            // the cost circle, riding the banner's left edge exactly as the frame's does
            float cr = W * 0.092f;
            Disc(px, 3f + cr, bannerBot * 0.5f, cr, ElementPalette.Mix(ec, Color.white, 0.72f));
            Disc(px, 3f + cr, bannerBot * 0.5f - cr * 0.18f, cr * 0.42f,
                 ElementPalette.Mix(Color.white, ec, 0.55f));

            // art window: a ringed hole. The art quad lays into it; the wash is what shows through
            // for the cards whose illustration is still missing.
            Box(px, inset, bannerBot, W - inset, artBot, wash);
            Outline(px, inset, bannerBot, W - inset, artBot, ring);

            // ability box: ivory, with the ruled lines that make it read as text from a distance
            Box(px, inset, artBot + 1, W - inset, rulesBot, Paper(0.5f));
            Outline(px, inset, artBot + 1, W - inset, rulesBot, new Color(0f, 0f, 0f, 1f));

            var ink = new Color(0.42f, 0.39f, 0.34f);
            const int lines = 3;
            for (int l = 0; l < lines; l++)
            {
                int y = artBot + 4 + Mathf.RoundToInt((rulesBot - artBot - 8) * l / (float)lines);
                int x1 = W - inset - 3 - (l == lines - 1 ? W / 4 : 0);   // a ragged last line
                for (int x = inset + 3; x < x1 && y < H; x++) px[I(x, y)] = ink;
            }

            // stat bar
            Box(px, 2, rulesBot, W - 2, H - 2, bar);
            for (int x = 2; x < W - 2; x++) px[I(x, rulesBot)] = ElementPalette.Mix(ec, bar, 0.5f);

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>linear-gradient(180deg,#f8f3e6,#e4ddc8 62%,#cfc7ae) - the card stock.</summary>
        static Color Paper(float t)
        {
            var top = ElementPalette.Hex("#f8f3e6");
            var mid = ElementPalette.Hex("#e4ddc8");
            var bot = ElementPalette.Hex("#cfc7ae");
            t = Mathf.Clamp01(t);
            return t < 0.62f ? Color.Lerp(top, mid, t / 0.62f) : Color.Lerp(mid, bot, (t - 0.62f) / 0.38f);
        }

        // -- the face-down sleeve ----------------------------------------------------------

        static Texture2D BuildBack(Color sleeve)
        {
            var tex = New("SRD Sleeve");
            var px = new Color[W * H];

            var body0 = ElementPalette.Mix(sleeve, ElementPalette.Hex("#131c2e"), 0.22f);
            var body1 = ElementPalette.Hex("#0a0f1c");

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // linear-gradient(160deg, ..., #0a0f1c 75%)
                    float t = Mathf.Clamp01((0.34f * (x / (float)W) + 0.94f * (y / (float)H)) / 0.75f);
                    var c = Color.Lerp(body0, body1, t);

                    // repeating-linear-gradient(135deg, rgba(255,255,255,.045) 0 2px, transparent 2px 7px)
                    if ((x + y) % 7 < 2) c = Color.Lerp(c, Color.white, 0.045f);

                    px[I(x, y)] = c;
                }
            }

            // the emblem's halo, then the emblem itself as a ringed diamond - the reference draws a
            // glyph there, and a glyph needs a font the world-space layer does not have; the shape
            // is what carries at this size anyway
            float cx = W * 0.5f, cy = H * 0.44f, halo = H * 0.15f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = new Vector2((x - cx) / halo, (y - cy) / halo).magnitude;
                    if (d < 1f) px[I(x, y)] = Color.Lerp(px[I(x, y)], sleeve, 0.30f * (1f - d * d));
                }

            Diamond(px, cx, cy, W * 0.15f, H * 0.11f, sleeve, 0.55f, 1.6f);
            Diamond(px, cx, cy, W * 0.062f, H * 0.045f, sleeve, 0.55f, 99f);

            // double border: tinted line, black gutter, tinted inner
            Outline(px, 0, 0, W, H, ElementPalette.Mix(sleeve, ElementPalette.Hex("#0a090d"), 0.5f));
            Outline(px, 1, 1, W - 1, H - 1, new Color(0f, 0f, 0f, 1f));
            Outline(px, 2, 2, W - 2, H - 2, ElementPalette.Mix(sleeve, ElementPalette.Hex("#141826"), 0.28f));

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // -- raster helpers. Everything is TOP-LEFT origin; I() flips into texture space. --

        static int I(int x, int y) { return (H - 1 - Mathf.Clamp(y, 0, H - 1)) * W + Mathf.Clamp(x, 0, W - 1); }

        static void Fill(Color[] px, Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }

        /// <summary>Solid rectangle, top-left origin, exclusive of x1/y1.</summary>
        static void Box(Color[] px, int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    px[I(x, y)] = c;
        }

        /// <summary>A one-pixel outline, top-left origin, exclusive of x1/y1.</summary>
        static void Outline(Color[] px, int x0, int y0, int x1, int y1, Color c)
        {
            for (int x = x0; x < x1; x++) { px[I(x, y0)] = c; px[I(x, y1 - 1)] = c; }
            for (int y = y0; y < y1; y++) { px[I(x0, y)] = c; px[I(x1 - 1, y)] = c; }
        }

        static void Disc(Color[] px, float cx, float cy, float r, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = new Vector2(x - cx, y - cy).magnitude;
                    if (d <= r) px[I(x, y)] = Color.Lerp(c, px[I(x, y)], Mathf.SmoothStep(0f, 1f, d - r + 1f));
                }
        }

        /// <summary>|dx|/a + |dy|/b = 1, drawn as a band t wide (t >= a fills it).</summary>
        static void Diamond(Color[] px, float cx, float cy, float a, float b, Color c, float alpha, float t)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - a)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + a));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - b)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + b));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Abs(x - cx) / a + Mathf.Abs(y - cy) / b;
                    if (d <= 1f && d >= 1f - t / a) px[I(x, y)] = Color.Lerp(px[I(x, y)], c, alpha);
                }
        }

        // -- a 3x5 bitmap font, and everything printed with it -----------------------------

        /// <summary>
        /// 0-9, then '+' and '-'. Top row first, one bit per column.
        ///
        /// Nothing in this layer can reach the SDF font chain - a plate is a world-space sprite,
        /// not a label - and twelve glyphs of bitmap is cheaper than the machinery that would let
        /// it. The cells are rastered NON-SQUARE on purpose: every strip on a card is limited by
        /// the card's WIDTH and has height going spare, so a digit is drawn about 1 : 1.75 and
        /// gains half its size again over a square one.
        /// </summary>
        static readonly byte[] Font =
        {
            0x7, 0x5, 0x5, 0x5, 0x7,   // 0
            0x2, 0x6, 0x2, 0x2, 0x7,   // 1
            0x7, 0x1, 0x7, 0x4, 0x7,   // 2
            0x7, 0x1, 0x7, 0x1, 0x7,   // 3
            0x5, 0x5, 0x7, 0x1, 0x1,   // 4
            0x7, 0x4, 0x7, 0x1, 0x7,   // 5
            0x7, 0x4, 0x7, 0x5, 0x7,   // 6
            0x7, 0x1, 0x1, 0x1, 0x1,   // 7
            0x7, 0x5, 0x7, 0x5, 0x7,   // 8
            0x7, 0x5, 0x7, 0x1, 0x7,   // 9
            0x0, 0x2, 0x7, 0x2, 0x0,   // +
            0x0, 0x0, 0x7, 0x0, 0x0,   // -

            // ...and the ALPHABET, added so a plate can print its own NAME. Twenty-six more
            // glyphs of five bytes is two hundred bytes; the alternative was a world-space text
            // renderer, or leaving the name to a UI overlay drawn ACROSS the field art, which is
            // what it was doing and what got it removed from there.
            0x2, 0x5, 0x7, 0x5, 0x5,   // A
            0x6, 0x5, 0x6, 0x5, 0x6,   // B
            0x3, 0x4, 0x4, 0x4, 0x3,   // C
            0x6, 0x5, 0x5, 0x5, 0x6,   // D
            0x7, 0x4, 0x6, 0x4, 0x7,   // E
            0x7, 0x4, 0x6, 0x4, 0x4,   // F
            0x3, 0x4, 0x5, 0x5, 0x3,   // G
            0x5, 0x5, 0x7, 0x5, 0x5,   // H
            0x7, 0x2, 0x2, 0x2, 0x7,   // I
            0x1, 0x1, 0x1, 0x5, 0x2,   // J
            0x5, 0x5, 0x6, 0x5, 0x5,   // K
            0x4, 0x4, 0x4, 0x4, 0x7,   // L
            0x5, 0x7, 0x7, 0x5, 0x5,   // M
            0x6, 0x5, 0x5, 0x5, 0x5,   // N
            0x2, 0x5, 0x5, 0x5, 0x2,   // O
            0x6, 0x5, 0x6, 0x4, 0x4,   // P
            0x2, 0x5, 0x5, 0x6, 0x3,   // Q
            0x6, 0x5, 0x6, 0x5, 0x5,   // R
            0x3, 0x4, 0x2, 0x1, 0x6,   // S
            0x7, 0x2, 0x2, 0x2, 0x2,   // T
            0x5, 0x5, 0x5, 0x5, 0x7,   // U
            0x5, 0x5, 0x5, 0x5, 0x2,   // V
            0x5, 0x5, 0x7, 0x7, 0x5,   // W
            0x5, 0x5, 0x2, 0x5, 0x5,   // X
            0x5, 0x5, 0x2, 0x2, 0x2,   // Y
            0x7, 0x1, 0x2, 0x4, 0x7,   // Z

            0x0, 0x0, 0x0, 0x0, 0x0,   // space - advances and draws nothing
            0x0, 0x0, 0x0, 0x0, 0x2,   // .
            0x2, 0x2, 0x0, 0x0, 0x0,   // apostrophe
        };

        const int Cols = 3, Rows = 5, Tracking = 1;

        static int GlyphOf(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c == '+') return 10;
            if (c == '-') return 11;
            if (c >= 'A' && c <= 'Z') return 12 + (c - 'A');
            if (c >= 'a' && c <= 'z') return 12 + (c - 'a');   // the font has one case
            if (c == ' ') return 38;
            if (c == '.') return 39;
            if (c == '\'') return 40;
            return -1;                       // anything else advances and draws nothing
        }

        /// <summary>How wide <paramref name="text"/> rasters at a cell width of sx texels.</summary>
        static int TextW(string text, int sx)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length * (Cols + Tracking) * sx - Tracking * sx;
        }

        /// <summary>Text with its TOP-LEFT at (x, y); one font cell is sx by sy texels.</summary>
        static void Text(Color[] px, int w, int h, string text, int x, int y,
                         int sx, int sy, Color c)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int g = GlyphOf(text[i]);
                if (g < 0) continue;
                int gx = x + i * (Cols + Tracking) * sx;
                for (int row = 0; row < Rows; row++)
                {
                    byte bits = Font[g * Rows + row];
                    for (int col = 0; col < Cols; col++)
                        if ((bits & (1 << (Cols - 1 - col))) != 0)
                            PBox(px, w, h, gx + col * sx, y + row * sy,
                                 gx + (col + 1) * sx, y + (row + 1) * sy, c);
                }
            }
        }

        /// <summary>
        /// The same, ringed. A number printed ACROSS a meter crosses both the fill and the empty
        /// half of the trough, so no single ink colour reads the whole way - the ring is what
        /// makes one work over both.
        /// </summary>
        static void TextRinged(Color[] px, int w, int h, string text, int x, int y,
                               int sx, int sy, Color c, Color ring, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx != 0 || dy != 0)
                        Text(px, w, h, text, x + dx, y + dy, sx, sy, ring);
            Text(px, w, h, text, x, y, sx, sy, c);
        }

        // -- raster helpers for a buffer of any size. Top-left origin, like the ones above --

        /// <summary>Solid rectangle, CLIPPED - not clamped, which is what the plate's own helpers
        /// do and would smear an out-of-range box along the edge of the texture.</summary>
        static void PBox(Color[] px, int w, int h, int x0, int y0, int x1, int y1, Color c)
        {
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > w) x1 = w;
            if (y1 > h) y1 = h;
            for (int y = y0; y < y1; y++)
            {
                int row = (h - 1 - y) * w;
                for (int x = x0; x < x1; x++) px[row + x] = c;
            }
        }

        static void POutline(Color[] px, int w, int h, int x0, int y0, int x1, int y1, Color c)
        {
            PBox(px, w, h, x0, y0, x1, y0 + 1, c);
            PBox(px, w, h, x0, y1 - 1, x1, y1, c);
            PBox(px, w, h, x0, y0, x0 + 1, y1, c);
            PBox(px, w, h, x1 - 1, y0, x1, y1, c);
        }

        /// <summary>The corners bitten off, so a filled band reads as a chip rather than a
        /// sticker. Two texels, which is what the badge has always used.</summary>
        static void PBite(Color[] px, int w, int h)
        {
            var clear = new Color(0f, 0f, 0f, 0f);
            PBox(px, w, h, 0, 0, 2, 2, clear);
            PBox(px, w, h, w - 2, 0, w, 2, clear);
            PBox(px, w, h, 0, h - 2, 2, h, clear);
            PBox(px, w, h, w - 2, h - 2, w, h, clear);
        }

        // -- the three marks a statline is made of ------------------------------------------

        enum Mark : byte { Sword = 0, Hammer = 1, Heart = 2 }

        /// <summary>
        /// The glyph as a SHAPE. The real ones live in the gated font chain and are unreachable
        /// from a sprite; and at sixteen texels the crossed blades of a real "swords" glyph are a
        /// smudge anyway, so the attack mark is one upright sword and reads at half the size.
        /// </summary>
        static void Icon(Color[] px, int w, int h, Mark m, int x, int y, int iw, int ih, Color c)
        {
            switch (m)
            {
                case Mark.Sword:
                    Cross(px, w, h, x, y, iw, ih, c);
                    break;
                case Mark.Hammer:
                    PBox(px, w, h, x + iw / 16, y, x + iw * 15 / 16, y + ih * 7 / 16, c);
                    PBox(px, w, h, x + iw * 6 / 16, y + ih * 7 / 16, x + iw * 10 / 16, y + ih, c);
                    break;
                case Mark.Heart:
                    Heart(px, w, h, x + iw * 0.5f, y + ih * 0.5f, iw * 0.5f, ih * 0.5f, c);
                    break;
            }
        }

        /// <summary>
        /// Crossed blades, as an X.
        ///
        /// An upright sword was tried first and does not survive the size: the mark is about a
        /// dozen pixels wide on the deployed board, and a sword drawn in a dozen pixels is a
        /// blade three of them wide - a scratch. An X is the same word (the cut-in's clash glyph
        /// is two crossed swords) and it is legible down to about five.
        /// </summary>
        static void Cross(Color[] px, int w, int h, int x, int y, int iw, int ih, Color c)
        {
            int t = Mathf.Max(2, Mathf.RoundToInt(iw * 0.26f));
            for (int row = 0; row < ih; row++)
            {
                float f = ih <= 1 ? 0f : row / (float)(ih - 1);
                int a = Mathf.RoundToInt(f * (iw - t));
                PBox(px, w, h, x + a, y + row, x + a + t, y + row + 1, c);
                PBox(px, w, h, x + iw - t - a, y + row, x + iw - a, y + row + 1, c);
            }
        }

        /// <summary>
        /// The implicit heart - (u^2 + v^2 - 1)^3 - u^2 v^3 &lt;= 0 - mapped onto the box. One
        /// expression, where a hand-plotted one at this size is a table of magic numbers.
        /// </summary>
        static void Heart(Color[] px, int w, int h, float cx, float cy, float rx, float ry, Color c)
        {
            int x0 = Mathf.FloorToInt(cx - rx), x1 = Mathf.CeilToInt(cx + rx);
            int y0 = Mathf.FloorToInt(cy - ry), y1 = Mathf.CeilToInt(cy + ry);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    // the curve spans u +-1.13 and v [-1.26, 1.0]; +v is UP and +y is DOWN
                    float u = (x + 0.5f - cx) / rx * 1.16f;
                    float v = (cy + ry * 0.13f - y - 0.5f) / ry * 1.30f;
                    float a = u * u + v * v - 1f;
                    if (a * a * a - u * u * v * v * v <= 0f) PBox(px, w, h, x, y, x + 1, y + 1, c);
                }
        }

        // -- the banked-mana badge ---------------------------------------------------------

        static readonly Dictionary<string, Texture2D> _banks = new Dictionary<string, Texture2D>();

        /// <summary>
        /// The mana riding on a card, drawn ON the card.
        ///
        /// A set card used to say "SET 1" on a label floating over its tile, which is two problems
        /// in four characters: the label belongs to the board rather than to the card, and the
        /// diamond in "SET ◆1" was never drawn at all, because that overlay is IMGUI and IMGUI's
        /// built-in font has no ◆. A badge on the card has neither problem - and a face-down card
        /// with a number on it is exactly what a charge IS.
        /// </summary>
        public static Sprite Bank(int n, Color tint)
        {
            string key = n + "/" + ((Color32)tint).GetHashCode();
            Texture2D tex;
            if (!_banks.TryGetValue(key, out tex) || tex == null)
            {
                tex = BuildBank(n, tint);
                _banks[key] = tex;
            }
            return SpriteOf(tex);
        }

        const int BankPx = 3;          // one bitmap pixel, in texels
        const int BankH = 22;

        static Texture2D BuildBank(int n, Color tint)
        {
            string text = Mathf.Clamp(n, 0, 99).ToString();
            int gemW = 13, pad = 4, gap = 3;
            int w = pad + gemW + gap + TextW(text, BankPx) + pad;

            var tex = New(w, BankH, "SRD Bank " + n);
            var px = new Color[w * BankH];
            var body = new Color(0.043f, 0.047f, 0.066f, 0.92f);
            var edge = ElementPalette.Mix(tint, new Color(0.05f, 0.05f, 0.07f), 0.55f);

            for (int i = 0; i < px.Length; i++) px[i] = body;
            POutline(px, w, BankH, 0, 0, w, BankH, edge);
            PBite(px, w, BankH);

            // the gem: a filled diamond in the owner's element
            float cx = pad + gemW * 0.5f - 0.5f, cy = BankH * 0.5f - 0.5f;
            for (int y = 0; y < BankH; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x - cx) / (gemW * 0.5f) + Mathf.Abs(y - cy) / (BankH * 0.38f);
                    if (d <= 1f)
                        PBox(px, w, BankH, x, y, x + 1, y + 1,
                             Color.Lerp(tint, Color.white, 0.25f * (1f - d)));
                }

            Text(px, w, BankH, text, pad + gemW + gap, (BankH - Rows * BankPx) / 2,
                 BankPx, BankPx, Color.white);

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }


        // -- the card's own NAME, printed in its title band --------------------------------

        static readonly Dictionary<string, Texture2D> _names = new Dictionary<string, Texture2D>();

        /// <summary>How tall a name glyph is rastered, in texels. The strip is scaled to the
        /// banner by the layer, so this only decides how crisp it gets there.</summary>
        const int NameSy = 6, NameSx = 4, NameRing = 1;

        /// <summary>
        /// The card's name as a strip, WHITE on transparent with a dark ring, for the layer to
        /// tint and lay into the banner.
        ///
        /// This exists because the name had nowhere honest to go. It was a UI Toolkit chip hung
        /// off the tile's front, which is a label floating on the grass belonging to nothing; then
        /// it was the same chip moved onto the card's title band, which drew it straight ACROSS
        /// the field art, because an overlay panel cannot sort behind a sprite in the scene. A
        /// strip on the plate can: it is a world-space sprite like everything else on the card, it
        /// sorts under the standee (CardPlateLayer.OrderName), and where the cut-out is opaque the
        /// ART WINS - which is the whole of what was asked for.
        ///
        /// White and ringed so one texture serves both seats: the ring is what lets it read over
        /// a pale banner and a dark one, and the tint is the renderer's, not the raster's.
        /// </summary>
        public static Sprite Name(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            Texture2D tex;
            if (!_names.TryGetValue(text, out tex) || tex == null)
            {
                tex = BuildName(text);
                _names[text] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildName(string text)
        {
            int w = TextW(text, NameSx) + NameRing * 2 + 2;
            int h = Rows * NameSy + NameRing * 2 + 2;

            var px = new Color[w * h];
            TextRinged(px, w, h, text, NameRing + 1, NameRing + 1, NameSx, NameSy,
                       Color.white, new Color(0f, 0f, 0f, 0.85f), NameRing);

            var tex = New(w, h, "SRD Name " + text);
            tex.SetPixels(px);

            // Upload and let the CPU copy go. Nothing ever reads a name strip back, and a cache
            // that grows with the card pool has no business keeping a second copy of each one in
            // a heap that starts at thirty-two megabytes.
            tex.Apply(false, true);
            return tex;
        }

        // -- what a card on the board is worth ----------------------------------------------

        /// <summary>
        /// The two bands that carry numbers, at their own aspects - so the layer lays each
        /// texture straight over its band and does no arithmetic of its own.
        /// </summary>
        public const int RuleBoxW = 384;
        public static readonly int RuleBoxH = Mathf.RoundToInt(RuleBoxW * RulesH * H / (float)W);

        static readonly Dictionary<string, Texture2D> _lines = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, Texture2D> _nums = new Dictionary<int, Texture2D>();
        static Sprite _solid;

        /// <summary>
        /// One white texel. The health meter's trough and its fill are this, scaled: a meter
        /// rastered whole would cost a texture for every (hp, max) pair a match reaches, and a
        /// fill that is a scaled quad moves continuously instead of in texture-sized steps.
        /// </summary>
        public static Sprite Solid()
        {
            if (_solid != null) return _solid;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SRD Solid",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, false);
            _solid = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _solid.name = "SRD Solid";
            _solid.hideFlags = HideFlags.HideAndDontSave;
            return _solid;
        }

        const int NumSx = 8, NumSy = 11, NumRing = 2;

        /// <summary>
        /// A number, ringed, cached by VALUE alone - which is what keeps the health meter from
        /// costing a texture per (hp, max) pair. The raster size decides crispness only: the
        /// layer scales it into whatever band it lands in.
        /// </summary>
        public static Sprite Num(int value)
        {
            Texture2D tex;
            if (!_nums.TryGetValue(value, out tex) || tex == null)
            {
                tex = BuildNum(value);
                _nums[value] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildNum(int value)
        {
            string text = value.ToString();
            int w = TextW(text, NumSx) + NumRing * 2;
            int h = Rows * NumSy + NumRing * 2;

            var tex = New(w, h, "SRD Num " + value);
            var px = new Color[w * h];
            TextRinged(px, w, h, text, NumRing, NumRing, NumSx, NumSy,
                       Color.white, new Color(0f, 0f, 0f, 0.92f), NumRing);
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>How full a meter is, as the colour its fill takes. The same three steps the
        /// vitals chips have always used, so one unit never reads two ways.</summary>
        public static Color HealthTint(float frac)
        {
            return frac > 0.5f ? new Color(0.42f, 0.86f, 0.48f)
                 : frac > 0.25f ? new Color(0.98f, 0.78f, 0.30f)
                                : new Color(0.95f, 0.34f, 0.28f);
        }

        /// <summary>The meter's ground: the stat bar's own colour, a shade darker.</summary>
        public static Color MeterTrough { get { return new Color(0.050f, 0.044f, 0.036f, 0.97f); } }

        /// <summary>
        /// The ABILITY BOX, filled with what the card is worth: attack, the worker draw or upkeep
        /// it carries, and the health it was printed with.
        ///
        /// A plaque rather than three loose marks, because it has to survive being drawn over a
        /// standee: the figure stands at the FRONT of its own tile, so its shins cross this band,
        /// and dark ink over a dark cut-out is nothing at all. The plaque brings its own
        /// parchment - the same parchment the frame under it already draws - so what the change
        /// really does is replace the frame's three ruled lines (a stand-in for text) with the
        /// text they were standing in for.
        ///
        /// The cell size is FITTED, not fixed: a four-digit attack has to fit the box a two-digit
        /// one does, and shrinking the cell is better than clipping the number.
        /// </summary>
        public static Sprite StatLine(int attack, int worker, int baseHp,
                                      bool hasAttack, bool hasWorker)
        {
            string key = (hasAttack ? attack.ToString() : "-") + "|"
                       + (hasWorker ? worker.ToString() : "-") + "|" + baseHp;
            Texture2D tex;
            if (!_lines.TryGetValue(key, out tex) || tex == null)
            {
                tex = BuildStatLine(attack, worker, baseHp, hasAttack, hasWorker);
                _lines[key] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildStatLine(int attack, int worker, int baseHp,
                                       bool hasAttack, bool hasWorker)
        {
            int w = RuleBoxW, h = RuleBoxH;
            var tex = New(w, h, "SRD Stats");
            var px = new Color[w * h];

            for (int y = 0; y < h; y++) PBox(px, w, h, 0, y, w, y + 1, Paper(y / (float)h));
            POutline(px, w, h, 0, 0, w, h, new Color(0.10f, 0.09f, 0.07f, 0.85f));

            var marks = new Mark[3];
            var texts = new string[3];
            var inks = new Color[3];
            int n = 0;

            if (hasAttack)
            {
                marks[n] = Mark.Sword;
                texts[n] = attack.ToString();
                inks[n++] = ElementPalette.Hex("#39415c");
            }
            if (hasWorker)
            {
                marks[n] = Mark.Hammer;
                texts[n] = (worker > 0 ? "+" : "-") + Mathf.Abs(worker);
                inks[n++] = ElementPalette.Hex("#7c5a1f");
            }
            marks[n] = Mark.Heart;
            texts[n] = baseHp.ToString();
            inks[n++] = ElementPalette.Hex("#93262a");

            // The widest cell the content still fits in. The raster is deliberately twice the
            // band it lands in: the cell size is an INTEGER, and at 192 texels the step from one
            // that fits to one that does not threw away a fifth of the width - which comes
            // straight off the size of the digits on screen.
            int pad = 10, sx = 12;
            while (sx > 2 && Layout(texts, n, sx) > w - 2 * pad) sx--;
            int sy = Mathf.Max(1, Mathf.RoundToInt(sx * 1.4f));
            while (sy > 1 && Rows * sy > h - 16) sy--;

            int x = (w - Layout(texts, n, sx)) / 2;
            int y0 = (h - Rows * sy) / 2;
            var ink = ElementPalette.Hex("#1b1610");

            for (int i = 0; i < n; i++)
            {
                Icon(px, w, h, marks[i], x, y0, (Cols + Tracking) * sx, Rows * sy, inks[i]);
                x += (Cols + Tracking) * sx + sx;
                Text(px, w, h, texts[i], x, y0, sx, sy, ink);
                x += TextW(texts[i], sx) + 3 * sx;
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>The rastered width of the whole statline at a cell width of sx: each mark is
        /// one cell-and-tracking wide, sits one cell off its number, and fields are three
        /// apart.</summary>
        static int Layout(string[] texts, int n, int sx)
        {
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                total += (Cols + Tracking) * sx + sx;
                total += TextW(texts[i], sx);
                if (i < n - 1) total += 3 * sx;
            }
            return total;
        }

        static Texture2D New(string name) { return New(W, H, name); }

        static Texture2D New(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }
    }
}
