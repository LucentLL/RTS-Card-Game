using UnityEngine;

namespace SpawnRowDuel.View.World
{
    /// <summary>
    /// The blade sprite atlas: a few tufts of FINE, LONG grass, generated.
    ///
    /// Four versions of this have shipped and the failure each time was the same one at a
    /// different size. First a single tapered quad per instance, which read as a field of
    /// identical spikes. Then a squat 40x64 tuft of six fat blades - and on a quad that was
    /// 0.175 wide by 0.185 tall, a near-square sprite of near-square blades, a meadow came out
    /// looking like a field of cabbages.
    ///
    /// What the reference has instead is HAIR: blades several times longer than they are wide,
    /// many of them per clump, each one leaning its own way, overlapping so the eye reads a
    /// continuous swept surface rather than a scatter of objects. So the cell is much taller than
    /// it is wide (2.5:1) and a blade runs the height of it.
    ///
    /// The fourth version - this one - is the same hair with a BODY. Twenty blades at under a
    /// texel wide and a texel and a half of feather is not twenty blades, it is one grey smear:
    /// every blade was entirely edge, so nothing in the clump had an interior to carry a colour
    /// and the meadow averaged toward the dirt it was standing on. Half as many blades, twice as
    /// wide, a tighter feather and a lit spine down each one, and the same field reads as grass
    /// you can count without ever getting near the fat spikes that read as cabbages.
    ///
    /// Six variants, picked per blade by its seed. A gutter either side of each cell keeps the
    /// mip chain from bleeding one variant into the next - and there IS a mip chain now, because
    /// hair-thin blades with no mips crawl with aliasing the moment the camera moves.
    ///
    /// RGB carries a root-to-tip luminance ramp AND a cross-blade spine for the shader to tint;
    /// A is coverage.
    /// </summary>
    public static class GrassTextures
    {
        public const int Variants = 6;

        const int CellW = 44, CellH = 112;
        const int Gutter = 3;                     // transparent margin, so mips do not bleed

        /// <summary>How much of a cell's width the shader may address. The rest is gutter.</summary>
        public static float Inset { get { return (CellW - Gutter * 2f) / CellW; } }

        static Texture2D _atlas;

        public static Texture2D Tufts
        {
            get
            {
                if (_atlas == null) _atlas = Build();
                return _atlas;
            }
        }

        static Texture2D Build()
        {
            int w = CellW * Variants;
            var px = new Color[w * CellH];

            // deterministic: the field must look the same on every launch, or the screenshot probe
            // compares two different fields and every diff is noise
            var rng = new System.Random(8823);

            for (int v = 0; v < Variants; v++)
            {
                // A CLUMP, not a sprig - but FEWER of them than the last pass had. Twenty-odd
                // hair-thin blades in one cell do not read as twenty blades: they read as a
                // whisker brush, because at the size a tuft draws on screen the gaps between them
                // are under a pixel and the whole clump greys into one soft smear. The reference
                // clump is four or five blades you can count. Eleven to twenty-one is the
                // compromise: still hair rather than the fat spikes that came out looking like
                // cabbages, but each blade now owns enough texels to be a blade.
                int blades = 11 + v * 2;

                for (int b = 0; b < blades; b++)
                {
                    // Feet gather toward the middle of the cell: a clump grows from a crown, so
                    // blades that start at the rim and lean outward read as a splayed star.
                    float spread = (float)(rng.NextDouble() + rng.NextDouble() - 1.0) * 0.5f;
                    float baseX = 0.5f + spread * 0.42f;

                    // The outer blades lean out further, and they SPLAY harder than they did. Blades that all lean the same few
                    // degrees stack into a column; the reference clump is a fan, and the fan is
                    // what gives a tuft a silhouette at any distance.
                    float lean = spread * 2.1f + ((float)rng.NextDouble() - 0.5f) * 0.72f;

                    // LONG. Most blades reach past three quarters of the cell, and the tallest
                    // few go to the top of it.
                    float top = 0.45f + (float)rng.NextDouble() * 0.55f;
                    top *= top < 0.6f ? 1f : 1.02f;

                    // ...and thin, but with a CORE. 0.020 of 44 texels is under a texel wide, and
                    // a blade thinner than its own feather is all feather - it has no interior for
                    // the spine shading below to light, so it cannot read as a rounded thing and
                    // the field comes out pale and flat. Doubled, a blade is three to six texels
                    // across: still hair at this camera, and now hair with an edge and a middle.
                    float wide = 0.032f + (float)rng.NextDouble() * 0.042f;

                    // some blades sit in shade inside the clump, some catch the light on top
                    float shade = 0.62f + (float)rng.NextDouble() * 0.38f;

                    DrawBlade(px, w, v, baseX, lean, top, wide, shade);
                }
            }

            var tex = new Texture2D(w, CellH, TextureFormat.RGBA32, true)
            {
                name = "SRD Grass Tufts",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// One blade: a quadratic lean from a pinned foot, tapering to a point, with a real tip.
        ///
        /// The taper is cubic near the top rather than quadratic. A quadratic blade still has a
        /// third of its width left at 80% height and then stops, which is a blunt end; grass ends
        /// in a hair, and the last fifth of a blade is most of what says so.
        /// </summary>
        static void DrawBlade(Color[] px, int atlasW, int variant,
                              float baseX, float lean, float top, float wide, float shade)
        {
            int x0 = variant * CellW;
            float g = Gutter / (float)CellW;

            for (int y = 0; y < CellH; y++)
            {
                float t = y / (float)(CellH - 1);          // 0 at the foot, 1 at the top of the cell
                if (t > top) break;

                float u = t / Mathf.Max(top, 0.0001f);     // 0..1 along THIS blade
                float centre = baseX + lean * u * u;       // leans more the higher it gets
                centre = Mathf.Clamp(centre, g, 1f - g);   // never into the gutter

                float half = wide * (1f - u * u * u * 0.97f);

                // darker at the root, brightest just short of the tip - the shape of light on grass
                float lum = Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u * 1.25f)));
                lum *= shade;

                for (int x = Gutter; x < CellW - Gutter; x++)
                {
                    float fx = (x + 0.5f) / CellW;
                    float d = Mathf.Abs(fx - centre) - half;

                    // ~a texel of feather. Enough to keep a blade from being a jagged one-pixel
                    // stripe, and no more: at a texel and a half a three-texel blade was ALL edge,
                    // which is why the last meadow came out pale - there was no interior left to
                    // carry a colour, so the whole field averaged toward its own background.
                    float a = Mathf.Clamp01(-d * CellW * 1.6f);
                    if (a <= 0.004f) continue;

                    // A SPINE. The blade is brightest along its middle and falls off to its edges,
                    // which is the difference between a rounded thing and a ribbon of paint - and
                    // at this size it is the only cue that says how many blades are in the clump.
                    float across = Mathf.Clamp01(Mathf.Abs(fx - centre) / Mathf.Max(half, 1e-5f));
                    float lit = lum * Mathf.Lerp(1f, 0.55f, across * across);

                    int i = y * atlasW + (x0 + x);
                    // blades OVERLAP: the nearer one wins on colour, but coverage accumulates, so
                    // a dense clump fills instead of flickering between its own blades
                    float acc = Mathf.Clamp01(px[i].a + a * 0.85f);
                    if (a > px[i].a) px[i] = new Color(lit, lit, lit, acc);
                    else px[i].a = acc;
                }
            }
        }
    }
}
