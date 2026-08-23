using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The two castle walls, rastered.
    ///
    /// The walls are the reference build's signature HUD (spec 09 §4.1): crenellated stone
    /// battlements that slide in from the bottom (yours) and the top (theirs). What this draws is
    /// their RETRACTED state - the rail that shows when the wall is down - because that is the
    /// state the board is played in, and it is the state that makes the screen edge read as the
    /// edge of a keep instead of a black bar with numbers on it.
    ///
    /// The silhouette follows the stylesheet's 60-point clip path: tall square towers at 0-21%
    /// and 79-100%, a low crenellated wall across the middle span with eight merlons, and an
    /// element-tinted stripe along the inner rail. The towers are where the vitals go and the
    /// middle span is where the hand goes, which is why the towers are the tall part.
    ///
    /// Generated at the band's real pixel size and cached on it: the stone's block courses are
    /// sized in screen pixels, and a texture stretched to fit would have turned them into slabs.
    ///
    /// </summary>
    public static class WallTextures
    {
        // silhouette geometry, as fractions of the band's width (spec 09 §4.1)
        const float TowerSpan = 0.21f;
        const int Merlons = 8;
        const float MerlonDuty = 0.62f;     // how much of a merlon's pitch is stone

        // stone courses, in logical units before the HUD scale
        const float CourseH = 13f;
        const float BlockW = 26f;

        static Texture2D _you, _foe;
        static string _youKey = "", _foeKey = "";

        /// <summary>
        /// One wall band. <paramref name="overhang"/> is how far the battlements rise past the
        /// band's inner edge and INTO the field - the part that is drawn with alpha, so the board
        /// shows through the gaps between the merlons.
        /// </summary>
        public static Texture2D Band(bool foe, Color element, int w, int h, int overhang, float scale)
        {
            string key = w + "x" + h + "/" + overhang + "/" + ((Color32)element).GetHashCode();
            if (foe && _foe != null && _foeKey == key) return _foe;
            if (!foe && _you != null && _youKey == key) return _you;

            var tex = Build(foe, element, w, h, overhang, scale);
            if (foe) { if (_foe != null) Object.DestroyImmediate(_foe); _foe = tex; _foeKey = key; }
            else { if (_you != null) Object.DestroyImmediate(_you); _you = tex; _youKey = key; }
            return tex;
        }

        static Texture2D Build(bool foe, Color element, int w, int h, int overhang, float scale)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = foe ? "SRD Wall Foe" : "SRD Wall You",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color[w * h];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var deep = new Color(0.086f, 0.090f, 0.115f);
            var mid = new Color(0.168f, 0.176f, 0.211f);
            var lit = new Color(0.246f, 0.254f, 0.293f);
            var mortar = new Color(0.055f, 0.058f, 0.076f);

            float courseH = Mathf.Max(6f, CourseH * scale);
            float blockW = Mathf.Max(12f, BlockW * scale);

            for (int x = 0; x < w; x++)
            {
                // rows are counted DOWN FROM THE WALL'S OWN TOP, so both walls are the same
                // drawing and only the flip at the end tells them apart
                int crest = CrestOf(x, w, overhang);

                for (int d = crest; d < h; d++)
                {
                    // Texture space is BOTTOM-UP, and each wall's crest faces the field: the top
                    // band's is its lower edge, yours is its upper one. Getting this backwards
                    // buries the battlements off the screen edge and leaves a straight cut facing
                    // the board - which is a letterbox bar again, with stone on it.
                    int y = foe ? d : h - 1 - d;
                    float depth = Mathf.Clamp01((d - crest) / Mathf.Max(1f, h * 0.85f));

                    // block courses: every other one offset by half a block, mortar between
                    int course = Mathf.FloorToInt((d - crest) / courseH);
                    float bx = (x + (course % 2 == 0 ? 0f : blockW * 0.5f)) / blockW;
                    float inBlock = bx - Mathf.Floor(bx);
                    float inCourse = ((d - crest) / courseH) - course;

                    var c = Color.Lerp(mid, deep, depth * 0.85f);

                    // per-block variation, so the courses do not read as a printed grid
                    float jitter = Frac(Mathf.Floor(bx) * 12.9898f + course * 78.233f);
                    c = Color.Lerp(c, jitter > 0.5f ? lit : deep, 0.16f * Mathf.Abs(jitter - 0.5f) * 2f);

                    if (inCourse < 0.10f || inBlock < 0.045f) c = Color.Lerp(c, mortar, 0.75f);
                    else if (inCourse < 0.22f) c = Color.Lerp(c, lit, 0.16f);   // the block's lit lip

                    // the crest catches the sky
                    int fromCrest = d - crest;
                    if (fromCrest < 2) c = Color.Lerp(c, lit, 0.85f);
                    else if (fromCrest < 4) c = Color.Lerp(c, lit, 0.35f);

                    px[y * w + x] = c;
                }

                // the element-tinted rail: a 5-on/19-off stripe just inside the crest
                int railTop = crest + Mathf.RoundToInt(6f * scale);
                int railH = Mathf.Max(2, Mathf.RoundToInt(3f * scale));
                float stripe = (x / Mathf.Max(1f, 6f * scale)) % 4f;
                for (int d = railTop; d < railTop + railH && d < h; d++)
                {
                    int y = foe ? d : h - 1 - d;
                    px[y * w + x] = Color.Lerp(px[y * w + x], element, stripe < 1f ? 0.85f : 0.22f);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>How far down from the texture's top edge this column's stone starts.</summary>
        static int CrestOf(int x, int w, int overhang)
        {
            float t = x / (float)Mathf.Max(1, w - 1);
            if (t < TowerSpan || t > 1f - TowerSpan) return 0;               // towers: full height

            float span = 1f - TowerSpan * 2f;
            float u = (t - TowerSpan) / span * Merlons;
            bool merlon = (u - Mathf.Floor(u)) < MerlonDuty;
            return merlon ? Mathf.RoundToInt(overhang * 0.45f) : overhang;   // merlon, or the gap
        }

        static float Frac(float v) { return v - Mathf.Floor(v); }
    }
}
