using UnityEngine;

namespace SpawnRowDuel.View.World
{
    /// <summary>
    /// The blade sprite atlas: a few tufts, generated.
    ///
    /// The first grass drew ONE tapered quad per instance in the fragment shader, and it looked
    /// exactly like what it was - a field of identical spikes. Real grass reads as tufts: several
    /// blades of different heights leaning different ways, soft at the edge, dark at the root. The
    /// Godot reference gets that from a hand-drawn sprite atlas (CC BY 4.0, not shipped here), and
    /// the same shape can be drawn in code, which is how this project makes every other texture.
    ///
    /// Four variants, picked per blade by its seed, is enough that the eye stops finding the
    /// repeat. RGB carries a root-to-tip luminance ramp for the shader to tint; A is coverage.
    /// </summary>
    public static class GrassTextures
    {
        public const int Variants = 4;
        const int CellW = 40, CellH = 64;

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
                int blades = 6 + (v % 3);          // a tuft, not a sprig - coverage per quad is what
                                                   // decides how many quads a full field needs
                for (int b = 0; b < blades; b++)
                {
                    float baseX = 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.62f;
                    float lean = ((float)rng.NextDouble() - 0.5f) * 0.9f;
                    float top = 0.62f + (float)rng.NextDouble() * 0.38f;
                    float wide = 0.085f + (float)rng.NextDouble() * 0.075f;
                    DrawBlade(px, w, v, baseX, lean, top, wide);
                }
            }

            var tex = new Texture2D(w, CellH, TextureFormat.RGBA32, false)
            {
                name = "SRD Grass Tufts",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>One blade: a quadratic lean from a pinned foot, tapering to a point.</summary>
        static void DrawBlade(Color[] px, int atlasW, int variant,
                              float baseX, float lean, float top, float wide)
        {
            int x0 = variant * CellW;

            for (int y = 0; y < CellH; y++)
            {
                float t = y / (float)(CellH - 1);          // 0 at the foot, 1 at the top of the cell
                if (t > top) break;

                float u = t / Mathf.Max(top, 0.0001f);     // 0..1 along THIS blade
                float centre = baseX + lean * u * u;       // leans more the higher it gets
                float half = wide * (1f - 0.92f * u * u);  // and narrows to a point

                // darker at the root, brightest just short of the tip - the shape of light on grass
                float lum = Mathf.Lerp(0.52f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u * 1.35f)));

                for (int x = 0; x < CellW; x++)
                {
                    float fx = (x + 0.5f) / CellW;
                    float d = Mathf.Abs(fx - centre) - half;
                    // a texel of feather, so the silhouette is soft instead of stair-stepped
                    float a = Mathf.Clamp01(-d * CellW * 1.6f);
                    if (a <= 0.002f) continue;

                    int i = y * atlasW + (x0 + x);
                    if (a > px[i].a) px[i] = new Color(lum, lum, lum, a);
                }
            }
        }
    }
}
