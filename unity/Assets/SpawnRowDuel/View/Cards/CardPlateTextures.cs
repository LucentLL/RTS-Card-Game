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
        public const float ArtInsetX = 0.035f;     // fraction of the WIDTH

        static readonly Dictionary<string, Texture2D> _fronts = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, Texture2D> _backs = new Dictionary<int, Texture2D>();
        static readonly Dictionary<Texture2D, Sprite> _sprites = new Dictionary<Texture2D, Sprite>();

        public static Sprite Front(ElementPalette.Swatch sw)
        {
            Texture2D tex;
            if (!_fronts.TryGetValue(sw.Name, out tex))
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
            if (!_backs.TryGetValue(key, out tex))
            {
                tex = BuildBack(sleeve);
                _backs[key] = tex;
            }
            return SpriteOf(tex);
        }

        static Sprite SpriteOf(Texture2D tex)
        {
            Sprite s;
            if (_sprites.TryGetValue(tex, out s)) return s;
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
        ///
        /// The digits are a 3x5 bitmap font, scaled. Nothing here can reach the SDF font chain -
        /// this is a world-space sprite, not a label - and eleven glyphs' worth of bitmap is
        /// cheaper than the machinery that would let it.
        /// </summary>
        public static Sprite Bank(int n, Color tint)
        {
            string key = n + "/" + ((Color32)tint).GetHashCode();
            Texture2D tex;
            if (!_banks.TryGetValue(key, out tex))
            {
                tex = BuildBank(n, tint);
                _banks[key] = tex;
            }
            return SpriteOf(tex);
        }

        // 3x5, top row first, one bit per column
        static readonly byte[] Digits =
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
        };

        const int BankPx = 3;          // one bitmap pixel, in texels
        const int BankH = 22;

        static Texture2D BuildBank(int n, Color tint)
        {
            string text = Mathf.Clamp(n, 0, 99).ToString();
            int gemW = 13, pad = 4, gap = 3, glyphW = 3 * BankPx + 2;
            int w = pad + gemW + gap + text.Length * glyphW + pad;

            var tex = new Texture2D(w, BankH, TextureFormat.RGBA32, false)
            {
                name = "SRD Bank " + n,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color[w * BankH];
            var body = new Color(0.043f, 0.047f, 0.066f, 0.92f);
            var edge = ElementPalette.Mix(tint, new Color(0.05f, 0.05f, 0.07f), 0.55f);

            for (int i = 0; i < px.Length; i++) px[i] = body;

            // border, and the corners bitten off so it reads as a chip rather than a sticker
            for (int y = 0; y < BankH; y++)
                for (int x = 0; x < w; x++)
                {
                    bool corner = (x < 2 && y < 2) || (x < 2 && y >= BankH - 2)
                               || (x >= w - 2 && y < 2) || (x >= w - 2 && y >= BankH - 2);
                    if (corner) px[y * w + x] = new Color(0f, 0f, 0f, 0f);
                    else if (x == 0 || y == 0 || x == w - 1 || y == BankH - 1)
                        px[y * w + x] = edge;
                }

            // the gem: a filled diamond in the owner's element
            float cx = pad + gemW * 0.5f - 0.5f, cy = BankH * 0.5f - 0.5f;
            for (int y = 0; y < BankH; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x - cx) / (gemW * 0.5f) + Mathf.Abs(y - cy) / (BankH * 0.38f);
                    if (d <= 1f) px[y * w + x] = Color.Lerp(tint, Color.white, 0.25f * (1f - d));
                }

            // the number
            int gx = pad + gemW + gap;
            int gy = (BankH - 5 * BankPx) / 2;
            for (int c = 0; c < text.Length; c++)
            {
                int digit = text[c] - '0';
                for (int row = 0; row < 5; row++)
                {
                    byte bits = Digits[digit * 5 + row];
                    for (int col = 0; col < 3; col++)
                    {
                        if ((bits & (1 << (2 - col))) == 0) continue;
                        for (int yy = 0; yy < BankPx; yy++)
                            for (int xx = 0; xx < BankPx; xx++)
                            {
                                int x = gx + c * glyphW + col * BankPx + xx;
                                // rows count DOWN from the glyph's top; texture space is bottom-up
                                int y = BankH - 1 - (gy + row * BankPx + yy);
                                if (x >= 0 && x < w && y >= 0 && y < BankH) px[y * w + x] = Color.white;
                            }
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        static Texture2D New(string name)
        {
            return new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }
    }
}
