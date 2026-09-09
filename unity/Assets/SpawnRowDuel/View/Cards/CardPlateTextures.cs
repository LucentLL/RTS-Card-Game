using System.Collections.Generic;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The board plate's frame, rastered - the same DM card anatomy as <see cref="CardFace"/>, at
    /// the size a card actually occupies lying on a tile.
    ///
    /// Why a raster and not the real CardFace: a plate is a world-space sprite lying on a tile,
    /// and UI Toolkit does not go there. So the anatomy is rebuilt out of texels - ivory banner
    /// with its cost disc, element ring, art window, ability box, dark stat strip - from the same
    /// numbers CardFace uses (spec 09 6.1): banner .155 of the height, art .479, rules .211,
    /// stats .155, which is what its 3.3 : 1.45 flex split resolves to.
    ///
    /// EVERY BAND IS FILLED, and that is the 2026-09-08 change. The frame used to carry the card's
    /// SHAPE only - a blank cost disc, three ruled lines standing in for ability text, a bare
    /// strip - on the argument that nothing at ~80 screen pixels is legible anyway, and the real
    /// numbers lived on an overlay hovering above the unit. Both halves of that turned out to be
    /// wrong: a plate covers its whole tile now, so it is the biggest thing on the board rather
    /// than a stamp under a figure, and the 3x5 bitmap font below reads perfectly well at the size
    /// a tile actually gives it. The cost, the name, one short ability line and the live statline
    /// are all printed here, in the bands a card in the hand prints them in.
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
            var bar = StatBar;                                          // the stat bar

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

            // ability box: ivory, and EMPTY.
            //
            // It used to be ruled with three ink lines - a stand-in for text, drawn because no
            // text was coming. Text is coming now: the layer lays a Brief plaque into this band
            // with the card's own ability line on it, the same short labels the hand card prints.
            // Leaving the lines under it would print a rule over a fake of one.
            Box(px, inset, artBot + 1, W - inset, rulesBot, Paper(0.5f));
            Outline(px, inset, artBot + 1, W - inset, rulesBot, new Color(0f, 0f, 0f, 1f));

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
        /// The card's name as a strip, DARK on a pale ring, for the layer to lay into the banner.
        ///
        /// This exists because the name had nowhere honest to go. It was a UI Toolkit chip hung
        /// off the tile's front, which is a label floating on the grass belonging to nothing; then
        /// it was the same chip moved onto the card's title band, which drew it straight ACROSS
        /// the field art, because an overlay panel cannot sort behind a sprite in the scene. A
        /// strip on the plate can: it is a world-space sprite like everything else on the card, it
        /// sorts under the standee (CardPlateLayer.OrderName), and where the cut-out is opaque the
        /// ART WINS - which is the whole of what was asked for.
        ///
        /// It was white-on-black-ring, on the argument that one texture then serves a pale banner
        /// and a dark one. There is no dark one: the banner is Paper on every card of every
        /// element, and outlined white letters on ivory are the lowest-contrast thing the frame
        /// can print. So it is the hand card's ink (#1a140a) with a pale ring, which is what dark
        /// type on light stock looks like at eight pixels.
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
                       new Color(0.10f, 0.078f, 0.04f), new Color(1f, 0.98f, 0.92f, 0.85f), NameRing);

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
        /// The two bands that carry printing, at their own aspects - so the layer lays each
        /// texture straight over its band and does no arithmetic of its own.
        ///
        /// They swapped contents on 2026-09-08. The statline used to be rastered onto parchment
        /// and laid into the ABILITY BOX while a health meter filled the stat bar, which is a card
        /// wearing its own anatomy inside out: the box a real card spends on what the card DOES
        /// held its numbers, and the black strip a real card spends on numbers held a progress
        /// bar. A board card is the hand card it came from now - a short ability line in the box,
        /// the statline in the strip - and the strip's health figure is the LIVE one, which is
        /// the whole of what the meter was for.
        /// </summary>
        /// <summary>
        /// The ability plaque, at the shape of the box it lands in - which is the ART WINDOW's
        /// width, not the card's. The frame insets that box by ArtInsetX on both sides (BuildFront
        /// draws them with the same number), so a plaque rastered at the card's full width comes
        /// out a third too wide and overhangs the frame it is supposed to sit inside.
        /// </summary>
        public const int RuleBoxW = 384;
        public static readonly int RuleBoxH =
            Mathf.RoundToInt(RuleBoxW * RulesH * H / (W * (1f - 2f * ArtInsetX)));

        public const int StatBoxW = 384;
        public static readonly int StatBoxH = Mathf.RoundToInt(StatBoxW * StatsH * H / (float)W);

        static readonly Dictionary<string, Texture2D> _lines = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> _briefs = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, Texture2D> _pips = new Dictionary<int, Texture2D>();

        /// <summary>The stat strip's black. Shared with the frame so a plaque laid into the band
        /// and the band under it are one continuous bar rather than two nearly-equal darks.
        /// </summary>
        public static readonly Color StatBar = new Color(0.078f, 0.066f, 0.051f);

        // -- the mana cost, in the circle the frame already draws for it --------------------

        const int PipSx = 7, PipSy = 10, PipRing = 1;

        /// <summary>
        /// The card's printed COST, for the disc on the banner's left edge.
        ///
        /// The frame has drawn that disc since the plate existed and has never put anything in
        /// it, so every card on the board wore a blank token where the hand card wears a number.
        /// Dark ink with a pale ring, because the disc under it is a light tint of the element
        /// and the board's other numbers - white, ringed black - would sink into it.
        ///
        /// Cached by value and not by element: the ring carries the contrast, so one raster
        /// serves all nine.
        /// </summary>
        public static Sprite Pip(int value)
        {
            Texture2D tex;
            if (!_pips.TryGetValue(value, out tex) || tex == null)
            {
                tex = BuildPip(value);
                _pips[value] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildPip(int value)
        {
            string text = Mathf.Clamp(value, 0, 99).ToString();
            int w = TextW(text, PipSx) + PipRing * 2;
            int h = Rows * PipSy + PipRing * 2;

            var tex = New(w, h, "SRD Pip " + value);
            var px = new Color[w * h];
            TextRinged(px, w, h, text, PipRing, PipRing, PipSx, PipSy,
                       new Color(0.07f, 0.055f, 0.03f), new Color(1f, 0.98f, 0.92f, 0.90f), PipRing);
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        // -- the ability box, and the one short line it holds -------------------------------

        /// <summary>
        /// The card's ability line, on the parchment the ability box is made of - at most two
        /// short rows of it, newline separated.
        ///
        /// MINIMAL, deliberately, and the same LABELS the hand card prints in the same box:
        /// "UPKEEP -3", "DETONATE 150", "FORGE 2". The sentences behind those labels live on the
        /// inspect card, which is where a player goes when they want to know what a rule does; a
        /// plate is about eighty screen pixels tall and a paragraph rastered into one is grey
        /// noise. What the box held before was three ruled ink lines - a stand-in for text that
        /// never arrived - with the statline plaque laid over the top of them.
        ///
        /// Empty text is NO SPRITE rather than a blank plaque, so a vanilla creature shows the
        /// frame's own parchment and nothing else.
        /// </summary>
        public static Sprite Brief(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            Texture2D tex;
            if (!_briefs.TryGetValue(text, out tex) || tex == null)
            {
                tex = BuildBrief(text);
                _briefs[text] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildBrief(string text)
        {
            int w = RuleBoxW, h = RuleBoxH;
            var tex = New(w, h, "SRD Brief");
            var px = new Color[w * h];

            for (int y = 0; y < h; y++) PBox(px, w, h, 0, y, w, y + 1, Paper(y / (float)h));
            POutline(px, w, h, 0, 0, w, h, new Color(0.10f, 0.09f, 0.07f, 0.85f));

            var rows = text.Split(NewLine);
            int pad = 12, gap = 6;

            // The cell the box can afford in BOTH directions, at the font's own 2 : 3 proportion.
            //
            // A cap and not just a fit. Fitting to the WIDTH alone is what a one-word line does to
            // this box if you let it: "WARD" has room for a cell three times the size "OVERCHARGE"
            // gets, so the two cards end up with wildly different type and the short one shouts.
            // Thirteen is what a seven-character line resolves to, which is about three fifths of
            // the box's height - a caption, which is what an ability line on a tile is.
            int sx = 13;
            while (sx > 2 && Widest(rows, sx) > w - 2 * pad) sx--;

            int room = Mathf.FloorToInt((h - 10 - (rows.Length - 1) * gap)
                                        / (float)(rows.Length * Rows) / 1.5f);
            if (sx > room) sx = Mathf.Max(1, room);

            int sy = Mathf.Max(1, Mathf.RoundToInt(sx * 1.5f));
            while (sy > 1 && rows.Length * Rows * sy + (rows.Length - 1) * gap > h - 10) sy--;

            var ink = ElementPalette.Hex("#1b1610");
            int block = rows.Length * Rows * sy + (rows.Length - 1) * gap;
            int y0 = (h - block) / 2;

            for (int i = 0; i < rows.Length; i++)
                Text(px, w, h, rows[i], (w - TextW(rows[i], sx)) / 2,
                     y0 + i * (Rows * sy + gap), sx, sy, ink);

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        static readonly char[] NewLine = { '\n' };

        static int Widest(string[] rows, int sx)
        {
            int max = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                int n = TextW(rows[i], sx);
                if (n > max) max = n;
            }
            return max;
        }

        // -- the stat strip -----------------------------------------------------------------

        /// <summary>
        /// How hurt a unit is, as the colour its health figure takes: the hand card's salmon
        /// while it is healthy, then amber, then red.
        ///
        /// This is the health METER's whole job done in a glyph that was going to be printed
        /// anyway. A bar spends a fifth of the card saying what a colour says, and it says it
        /// about a card that is already printing the number - "the same health in two places a
        /// finger's width apart is not redundancy, it is two things to check", which is the
        /// argument that took the numbers off the overlay and put them on the card to begin with.
        ///
        /// Salmon rather than green at the top, so a card at full health reads the same on the
        /// board as it does in the hand. The two lower steps are the departure, and departing is
        /// what they are for.
        /// </summary>
        public static Color HpInk(int hp, int max)
        {
            float frac = max <= 0 ? 1f : Mathf.Clamp01(hp / (float)max);
            return frac > 0.5f ? new Color(1f, 0.60f, 0.54f)        // #ff9a8a, the hand card's
                 : frac > 0.25f ? new Color(0.98f, 0.78f, 0.30f)
                                : new Color(0.95f, 0.34f, 0.28f);
        }

        /// <summary>
        /// The STAT STRIP, filled the way the hand card fills its footer: attack, the worker
        /// chip, and the health the unit has LEFT.
        ///
        /// Rastered on the strip's own black rather than on parchment. The parchment was bought
        /// by an argument that has since expired - the plaque used to land in the ability box and
        /// had to survive a standee's shins crossing it, and the shins win outright now
        /// (StandeeLayer sorts above every part of the plate). Here it is the card's black
        /// footer, so it brings the footer's colour and the two read as one band.
        ///
        /// The health figure is the LIVE one, tinted by how much of the printed total is left.
        /// Cached on that three-step tint rather than on the fraction, so a long fight costs
        /// three rasters per (attack, worker, health) and not one per point of damage.
        ///
        /// The cell size is FITTED, not fixed: a four-digit attack has to fit the strip a
        /// two-digit one does, and shrinking the cell beats clipping the number.
        /// </summary>
        public static Sprite StatLine(int attack, int worker, int hp,
                                      bool hasAttack, bool hasWorker, Color hpInk)
        {
            string key = (hasAttack ? attack.ToString() : "-") + "|"
                       + (hasWorker ? worker.ToString() : "-") + "|" + hp
                       + "|" + ((Color32)hpInk).GetHashCode();
            Texture2D tex;
            if (!_lines.TryGetValue(key, out tex) || tex == null)
            {
                tex = BuildStatLine(attack, worker, hp, hasAttack, hasWorker, hpInk);
                _lines[key] = tex;
            }
            return SpriteOf(tex);
        }

        static Texture2D BuildStatLine(int attack, int worker, int hp,
                                       bool hasAttack, bool hasWorker, Color hpInk)
        {
            int w = StatBoxW, h = StatBoxH;
            var tex = New(w, h, "SRD Stats");
            var px = new Color[w * h];

            for (int i = 0; i < px.Length; i++) px[i] = StatBar;

            var marks = new Mark[3];
            var texts = new string[3];
            var inks = new Color[3];        // the number
            var glyphs = new Color[3];      // the mark in front of it
            var pill = new bool[3];
            int n = 0;

            if (hasAttack)
            {
                marks[n] = Mark.Sword;
                texts[n] = attack.ToString();
                glyphs[n] = new Color(0.72f, 0.78f, 0.92f);
                inks[n++] = Color.white;                      // the DM power number
            }
            if (hasWorker)
            {
                marks[n] = Mark.Hammer;
                texts[n] = (worker > 0 ? "+" : "-") + Mathf.Abs(worker);
                glyphs[n] = new Color(0.88f, 0.72f, 0.40f);
                inks[n] = new Color(0.96f, 0.90f, 0.76f);     // the hand card's chip
                pill[n++] = true;
            }
            marks[n] = Mark.Heart;
            texts[n] = hp.ToString();
            glyphs[n] = hpInk;
            inks[n++] = hpInk;

            // The raster is deliberately wider than the band it lands in: the cell size is an
            // INTEGER, and at 192 texels the step from one that fits to one that does not threw
            // away a fifth of the width - which comes straight off the size on screen.
            int pad = 10, sx = 12;
            while (sx > 2 && Layout(texts, n, sx) > w - 2 * pad) sx--;
            int sy = Mathf.Max(1, Mathf.RoundToInt(sx * 1.4f));
            while (sy > 1 && Rows * sy > h - 10) sy--;

            int x = (w - Layout(texts, n, sx)) / 2;
            int y0 = (h - Rows * sy) / 2;

            for (int i = 0; i < n; i++)
            {
                int iw = (Cols + Tracking) * sx, ih = Rows * sy;

                // The worker chip's PILL - the one piece of the hand card's footer that is a
                // shape rather than a glyph, and the thing that lets a worker draw read as a
                // badge at the size a plate is actually drawn at. Green when the row gains
                // bodies, brown when it holds them off the harvest, exactly as CardFace tints it.
                if (pill[i])
                {
                    int end = x + iw + sx + TextW(texts[i], sx);
                    PBox(px, w, h, x - sx, Mathf.Max(1, y0 - sy / 2),
                         end + sx, Mathf.Min(h - 1, y0 + ih + sy / 2),
                         worker > 0 ? new Color(0.08f, 0.22f, 0.10f, 1f)
                                    : new Color(0.24f, 0.15f, 0.06f, 1f));
                }

                Icon(px, w, h, marks[i], x, y0, iw, ih, glyphs[i]);
                x += iw + sx;
                Text(px, w, h, texts[i], x, y0, sx, sy, inks[i]);
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
