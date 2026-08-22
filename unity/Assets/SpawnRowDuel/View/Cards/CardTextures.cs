using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The card frame's gradients and paper grain, generated once at load.
    ///
    /// The reference frame is built entirely out of CSS gradients and a turbulence filter - an
    /// ivory plate, a radial cost circle, a lozenge sweep, an art vignette. USS has no gradient
    /// functions, so the choice is between shipping PNGs an artist has to maintain and generating
    /// the same ramps in code. Generated wins: the recipes are IN the spec as numbers (§6.1), they
    /// tint per element at runtime, and there is no art file to fall out of step with the design.
    ///
    /// Textures are tiny (the largest is 64×64), created once, and tinted per card through
    /// `unity-background-image-tint-color`, which costs nothing per instance.
    /// </summary>
    public static class CardTextures
    {
        static Texture2D _paper, _radial, _sweep, _vignette, _ring;

        /// <summary>Ivory card stock: a vertical ramp with a deterministic grain over it.</summary>
        public static Texture2D Paper
        {
            get
            {
                if (_paper != null) return _paper;
                const int N = 64;
                _paper = New(N, N, "SRD Card Paper");
                // linear-gradient(180deg,#f8f3e6,#e4ddc8 62%,#cfc7ae) - spec 09 §6.1
                var top = ElementPalette.Hex("#f8f3e6");
                var mid = ElementPalette.Hex("#e4ddc8");
                var bot = ElementPalette.Hex("#cfc7ae");
                for (int y = 0; y < N; y++)
                {
                    float t = 1f - (y / (float)(N - 1));               // texture y is bottom-up
                    var c = t < 0.62f ? Color.Lerp(top, mid, t / 0.62f)
                                      : Color.Lerp(mid, bot, (t - 0.62f) / 0.38f);
                    for (int x = 0; x < N; x++)
                    {
                        float grain = (Hash(x, y) - 0.5f) * 0.045f;    // the turbulence, quietly
                        _paper.SetPixel(x, y, new Color(c.r + grain, c.g + grain, c.b + grain, 1f));
                    }
                }
                _paper.Apply();
                return _paper;
            }
        }

        /// <summary>
        /// The cost circle: white-hot at 35%/28%, the element at 58%, element+black at the rim.
        /// Drawn white here and tinted per element by the caller.
        /// </summary>
        public static Texture2D Radial
        {
            get
            {
                if (_radial != null) return _radial;
                const int N = 64;
                _radial = New(N, N, "SRD Card Radial");
                var c0 = new Vector2(0.35f, 0.72f);                    // the highlight is off-centre
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var p = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
                        float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
                        float d = Vector2.Distance(p, c0) * 1.6f;
                        float lum = Mathf.Lerp(1.35f, 0.42f, Mathf.Clamp01(d));
                        float alpha = r > 1f ? 0f : 1f;                // circular cut
                        _radial.SetPixel(x, y, new Color(lum, lum, lum, alpha));
                    }
                _radial.Apply();
                return _radial;
            }
        }

        /// <summary>Type-lozenge sweep: light element on the left, dark on the right.</summary>
        public static Texture2D Sweep
        {
            get
            {
                if (_sweep != null) return _sweep;
                const int W = 32;
                _sweep = New(W, 4, "SRD Card Sweep");
                for (int x = 0; x < W; x++)
                {
                    float t = x / (float)(W - 1);
                    float lum = Mathf.Lerp(1.28f, 0.62f, t);           // mix(ec 72% white) → mix(ec 70% black)
                    for (int y = 0; y < 4; y++) _sweep.SetPixel(x, y, new Color(lum, lum, lum, 1f));
                }
                _sweep.Apply();
                return _sweep;
            }
        }

        /// <summary>A 20 px inner vignette for the art window, as an overlay.</summary>
        public static Texture2D Vignette
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int N = 64;
                _vignette = New(N, N, "SRD Card Vignette");
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float ex = Mathf.Min(x, N - 1 - x) / (float)(N * 0.5f);
                        float ey = Mathf.Min(y, N - 1 - y) / (float)(N * 0.5f);
                        float edge = Mathf.Clamp01(Mathf.Min(ex, ey) * 3.2f);
                        _vignette.SetPixel(x, y, new Color(0f, 0f, 0f, (1f - edge) * 0.55f));
                    }
                _vignette.Apply();
                return _vignette;
            }
        }

        /// <summary>A soft round halo, for the element gem behind its kanji.</summary>
        public static Texture2D Gem
        {
            get
            {
                if (_ring != null) return _ring;
                const int N = 64;
                _ring = New(N, N, "SRD Card Gem");
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var p = new Vector2(x / (float)(N - 1), y / (float)(N - 1));
                        float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
                        float d = Vector2.Distance(p, new Vector2(0.38f, 0.68f)) * 1.7f;
                        float lum = Mathf.Lerp(1.5f, 0.5f, Mathf.Clamp01(d));
                        float a = r > 1f ? 0f : Mathf.SmoothStep(1f, 0.85f, Mathf.Clamp01((r - 0.86f) / 0.14f));
                        _ring.SetPixel(x, y, new Color(lum, lum, lum, a));
                    }
                _ring.Apply();
                return _ring;
            }
        }

        static Texture2D New(int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return t;
        }

        /// <summary>Deterministic value noise - the same grain every run, on every platform.</summary>
        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }
}
