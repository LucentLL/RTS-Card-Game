using UnityEngine;

namespace SpawnRowDuel.View.World
{
    public enum BiomeId { Grass, Sand, Water, Ash, Snow, Earth, Shore }

    /// <summary>
    /// What a battlefield looks like, as data.
    ///
    /// Biome is deliberately NOT a shader per ground type. There is one terrain shader with a set
    /// of amounts on it, so water is swell at 1 and embers at 0 rather than its own material, and
    /// the next biome anybody wants is a row in this table and no new shader at all.
    ///
    /// It now carries three things it did not: a <see cref="TerrainProfile"/> (the ground has real
    /// relief, and how that relief is shaped is the biggest single difference between a desert and
    /// a snowfield), a lighting block (the ground is lit rather than flat-shaded, so each biome
    /// gets its own sun and sky), and a veil (what drifts through the air over it).
    ///
    /// **Why the lighting is per-biome rather than one scene light.** The scene's sun is fixed at
    /// 48° for the pieces. The ground is shaded against its OWN sun vector, low and raking, because
    /// a dune field at 48° is a beige carpet - the entire read of the terrain is long shadows
    /// thrown by a sun near the horizon, and that is a different light from the one that has to
    /// keep a card face legible.
    ///
    /// Nothing here reaches the rules. The board still tints its rows by owner and the engine has
    /// never heard of a biome; this is scenery, and scenery that could change a game would be a
    /// rule wearing a costume.
    /// </summary>
    public struct BiomeLook
    {
        public string Name;

        // ---- surface colour ----------------------------------------------------------------
        public Color Base, Tint2, Tint3, Highlight;

        // ---- animated surface terms ---------------------------------------------------------
        public float Waves, Ripples, Embers, MotionSpeed;

        // ---- shape ---------------------------------------------------------------------------
        public TerrainProfile Terrain;

        // ---- light ----------------------------------------------------------------------------
        /// <summary>Sun bearing and elevation in degrees. Elevation LOW is the whole point.</summary>
        public float SunAngle, SunElevation;
        public Color SunColor;
        /// <summary>Hemisphere ambient: what the sky throws down and what the ground bounces up.</summary>
        public Color SkyColor, BounceColor;
        /// <summary>Grazing sheen - sand and snow both have it, ash has almost none.</summary>
        public float Sheen, SheenPower;
        /// <summary>How hard the baked sun-occlusion bites. 1 is a black shadow, which nothing has.</summary>
        public float ShadowDepth;

        // ---- surface detail ---------------------------------------------------------------
        /// <summary>Wind striations combed along the surface - the reference photograph's
        /// signature, and most of what says "wind has been over this".</summary>
        public float StreakAmount, StreakScale;
        /// <summary>Fine bump, in the same wind direction as the streaks.</summary>
        public float DetailBump;
        /// <summary>Crests catch light, hollows collect shade; separate from lighting so a biome
        /// can have flat light and still read its own relief.</summary>
        public float CrestLight, TroughShade;
        /// <summary>Point glints on the surface - snow crystals, water sun-glitter.</summary>
        public float Sparkle;

        // ---- distance -------------------------------------------------------------------------
        /// <summary>Everything fades toward this with distance. It is what stops the far dunes
        /// being the same colour as the near ones, which is what flattens a big landscape.</summary>
        public Color HazeColor;
        public float HazeStart, HazeDensity;

        // ---- what drifts through the air ------------------------------------------------------
        public float VeilAmount;          // 0 = still air
        public Color VeilColor;
        public float VeilSpeed, VeilScale, VeilHeight;

        // ---- what falls out of the sky --------------------------------------------------------
        /// <summary>Snow, ash. Separate from the veil, which is what the wind drags ACROSS the
        /// ground - a horizontal sheet cannot show vertical motion.</summary>
        public float FallAmount;
        public Color FallColor;
        public float FallSpeed, FallDrift, FallSize, FallSwirl;

        // ---- ground cover ---------------------------------------------------------------------
        /// <summary>Blades per square world unit. Zero means open ground - water has no grass.</summary>
        public float BladeDensity;
        public Color BladeA, BladeB, BladeRoot;
        public float BladeHeight, BladeWidth, Sway;

        /// <summary>A second, sparser, much larger layer: shrubs and clumps rather than blades.
        /// The same shader and the same press field - a bush flattens under a card exactly as a
        /// blade does, it is just bigger.</summary>
        public float BushDensity;
        public Color BushA, BushB, BushRoot;
        public float BushHeight, BushWidth, BushSway;

        // ---- clouds ------------------------------------------------------------------------
        /// <summary>The colour a cloud shadow pulls the ground toward. Shade is bluer, not just darker.</summary>
        public Color ShadowTint;
        public float CloudAmount;
    }

    public static class Biomes
    {
        public static readonly BiomeId[] All =
        {
            BiomeId.Grass, BiomeId.Earth, BiomeId.Sand, BiomeId.Snow,
            BiomeId.Ash, BiomeId.Shore, BiomeId.Water,
        };

        public static BiomeLook Of(BiomeId id)
        {
            switch (id)
            {
                case BiomeId.Sand: return Sand();
                case BiomeId.Water: return Water();
                case BiomeId.Ash: return Ash();
                case BiomeId.Snow: return Snow();
                case BiomeId.Earth: return Earth();
                case BiomeId.Shore: return Shore();
                default: return Grass();
            }
        }

        public static string NameOf(BiomeId id) { return Of(id).Name; }

        // ── meadow ───────────────────────────────────────────────────────────────────────

        static BiomeLook Grass()
        {
            var b = Common();
            b.Name = "meadow";
            b.Base = Hex("#3d6b2e"); b.Tint2 = Hex("#477a33"); b.Tint3 = Hex("#325c27");
            b.Highlight = Hex("#8fb05a");
            b.MotionSpeed = 0.3f;

            // Rolling downland: broad, soft, no wind carving. Grass holds a hill's shape.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.60f, Wavelength = 8f, WindStretch = 0.25f, WindAngle = 20f,
                Ridge = 0.10f, Detail = 0.18f, PlateauPad = 0.9f, PlateauFalloff = 3.0f,
            };

            b.SunAngle = -34f; b.SunElevation = 26f;
            b.SunColor = Hex("#fff3d4"); b.SkyColor = Hex("#8fb6e8"); b.BounceColor = Hex("#40542a");
            b.Sheen = 0.10f; b.SheenPower = 22f; b.ShadowDepth = 0.42f;
            b.StreakAmount = 0.10f; b.StreakScale = 2.4f; b.DetailBump = 0.35f;
            b.CrestLight = 0.16f; b.TroughShade = 0.20f; b.Sparkle = 0f;
            b.HazeColor = Hex("#9fc0d8"); b.HazeStart = 13f; b.HazeDensity = 0.30f;

            b.VeilAmount = 0.14f; b.VeilColor = Hex("#cfe0b8");
            b.VeilSpeed = 0.5f; b.VeilScale = 5f; b.VeilHeight = 1.1f;
            // A few seeds and insects in the light, nothing more.
            b.FallAmount = 0.10f; b.FallColor = Hex("#e8f0c8");
            b.FallSpeed = 0.06f; b.FallDrift = 0.09f; b.FallSize = 0.45f; b.FallSwirl = 1.4f;

            // LUSH. The old meadow was 24 tufts a square unit at 14 cm tall, which is a lawn
            // somebody has just cut - you could see the ground between every blade, and grass you
            // can see through does not read as something a card presses INTO.
            b.BladeDensity = 30f;
            b.BladeA = Hex("#3a6d26"); b.BladeB = Hex("#6ea845"); b.BladeRoot = Hex("#162e11");
            b.BladeHeight = 0.185f; b.BladeWidth = 0.175f; b.Sway = 0.055f;

            // ...and shrubs standing above it, so the field has a canopy with a height to it
            // rather than one uniform pile. They flatten under a card like everything else.
            b.BushDensity = 1.9f;
            b.BushA = Hex("#2f5d20"); b.BushB = Hex("#4c8330"); b.BushRoot = Hex("#14290f");
            b.BushHeight = 0.70f; b.BushWidth = 0.92f; b.BushSway = 0.030f;
            b.ShadowTint = Hex("#7f93c8"); b.CloudAmount = 1f;
            return b;
        }

        // ── dunes ────────────────────────────────────────────────────────────────────────

        static BiomeLook Sand()
        {
            var b = Common();
            b.Name = "dunes";
            b.Base = Hex("#a98d4c"); b.Tint2 = Hex("#bda062"); b.Tint3 = Hex("#8d7340");
            b.Highlight = Hex("#f6ead0");
            b.Ripples = 1f; b.MotionSpeed = 0.5f;

            // The big one. Long transverse ridges lying across a steady wind, sharply brinked,
            // and tall enough that a crest occludes the trough behind it at the tilted angle -
            // occlusion is what makes a dune field read as a landscape rather than a texture.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 1.75f, Wavelength = 7.0f, WindStretch = 0.84f, WindAngle = 12f,
                Ridge = 0.55f, Detail = 0.16f, PlateauPad = 0.6f, PlateauFalloff = 2.1f,
            };

            // A sun almost on the deck. Everything about the reference photograph is this number.
            b.SunAngle = 6f; b.SunElevation = 11f;
            b.SunColor = Hex("#ffdca8"); b.SkyColor = Hex("#b9cfe6"); b.BounceColor = Hex("#8a6a37");
            b.Sheen = 0.30f; b.SheenPower = 12f; b.ShadowDepth = 0.55f;
            b.StreakAmount = 0.26f; b.StreakScale = 5.5f; b.DetailBump = 0.9f;
            b.CrestLight = 0.20f; b.TroughShade = 0.30f; b.Sparkle = 0.10f;
            b.HazeColor = Hex("#e8d3ab"); b.HazeStart = 9f; b.HazeDensity = 0.62f;

            b.VeilAmount = 0.85f; b.VeilColor = Hex("#e8d2a4");
            b.VeilSpeed = 1.5f; b.VeilScale = 7f; b.VeilHeight = 1.5f;

            // None. A desert with grass in it is a lawn that needs watering, and the reference
            // has not a blade in sight - the surface itself is the whole texture.
            b.BladeDensity = 0f;
            b.BladeA = Hex("#a89253"); b.BladeB = Hex("#c4ad6a"); b.BladeRoot = Hex("#7a6636");
            b.BladeHeight = 0.115f; b.BladeWidth = 0.140f; b.Sway = 0.058f;
            b.ShadowTint = Hex("#9a93b8"); b.CloudAmount = 0.55f;
            return b;
        }

        // ── snowfield ────────────────────────────────────────────────────────────────────

        static BiomeLook Snow()
        {
            var b = Common();
            b.Name = "drifts";
            // Snow is never white. Lit snow is warm and shadowed snow is emphatically blue, and
            // painting the base white leaves nowhere for the light to go - the whole surface
            // clips and the relief disappears. The base is a pale blue-grey and the SUN puts the
            // white back into it.
            b.Base = Hex("#9fb0c4"); b.Tint2 = Hex("#b3c2d2"); b.Tint3 = Hex("#8b9db4");
            b.Highlight = Hex("#ffffff");
            b.MotionSpeed = 0.35f;

            // Sastrugi: wind-carved drifts, shallower than dunes and much sharper, because snow
            // holds an edge that sand cannot.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 1.25f, Wavelength = 6.0f, WindStretch = 0.78f, WindAngle = -24f,
                Ridge = 0.68f, Detail = 0.26f, PlateauPad = 0.6f, PlateauFalloff = 2.0f,
            };

            b.SunAngle = -18f; b.SunElevation = 9f;
            b.SunColor = Hex("#ffe6c4"); b.SkyColor = Hex("#7fa4d8"); b.BounceColor = Hex("#6d8bb4");
            b.Sheen = 0.42f; b.SheenPower = 30f; b.ShadowDepth = 0.46f;
            b.StreakAmount = 0.22f; b.StreakScale = 6.5f; b.DetailBump = 0.70f;
            b.CrestLight = 0.36f; b.TroughShade = 0.30f; b.Sparkle = 1f;
            b.HazeColor = Hex("#dfe9f5"); b.HazeStart = 8f; b.HazeDensity = 0.70f;

            b.VeilAmount = 0.55f; b.VeilColor = Hex("#eef4ff");
            b.VeilSpeed = 2.1f; b.VeilScale = 6f; b.VeilHeight = 1.3f;
            b.FallAmount = 0.85f; b.FallColor = Hex("#ffffff");
            b.FallSpeed = 0.30f; b.FallDrift = 0.22f; b.FallSize = 1.0f; b.FallSwirl = 0.7f;

            b.BladeDensity = 0f;                       // a smooth crust; nothing grows through
            b.BladeA = Hex("#8d8f92"); b.BladeB = Hex("#a9aaad"); b.BladeRoot = Hex("#5f6165");
            b.BladeHeight = 0.100f; b.BladeWidth = 0.120f; b.Sway = 0.070f;
            b.ShadowTint = Hex("#7d9ad0"); b.CloudAmount = 0.75f;
            return b;
        }

        // ── scorched ─────────────────────────────────────────────────────────────────────

        static BiomeLook Ash()
        {
            var b = Common();
            b.Name = "scorched";
            b.Base = Hex("#2b2622"); b.Tint2 = Hex("#39332e"); b.Tint3 = Hex("#1e1a18");
            b.Highlight = Hex("#ff7a2e");
            b.Embers = 1f; b.MotionSpeed = 0.4f;

            // Ash drifts like snow but slumps like sand: soft mounds, no brink, and a lot of fine
            // detail because it takes every footprint and never smooths back out.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 1.10f, Wavelength = 6.5f, WindStretch = 0.55f, WindAngle = 40f,
                Ridge = 0.18f, Detail = 0.34f, PlateauPad = 0.6f, PlateauFalloff = 2.1f,
            };

            // Almost no sun - the light is a dull red sky, so the relief is carried by AO and
            // by the embers rather than by shadows. A raking sun on black ash reads as slate.
            b.SunAngle = 62f; b.SunElevation = 10f;
            b.SunColor = Hex("#d98a52"); b.SkyColor = Hex("#4a3f44"); b.BounceColor = Hex("#2a1f1e");
            b.Sheen = 0.06f; b.SheenPower = 40f; b.ShadowDepth = 0.52f;
            b.StreakAmount = 0.20f; b.StreakScale = 5.8f; b.DetailBump = 0.80f;
            b.CrestLight = 0.26f; b.TroughShade = 0.44f; b.Sparkle = 0f;
            b.HazeColor = Hex("#3f3330"); b.HazeStart = 7f; b.HazeDensity = 0.66f;

            b.VeilAmount = 0.45f; b.VeilColor = Hex("#6e5a52");
            b.VeilSpeed = 0.9f; b.VeilScale = 5.5f; b.VeilHeight = 1.7f;
            // Snow, but slower. Ash is lighter than snow and has further to come, so it hangs -
            // a third of the fall speed, more sideways wander, and it never falls straight.
            b.FallAmount = 0.80f; b.FallColor = Hex("#cfc4bb");
            b.FallSpeed = 0.10f; b.FallDrift = 0.30f; b.FallSize = 0.85f; b.FallSwirl = 1.6f;

            b.BladeDensity = 1.1f;                     // a few burnt stalks, not a crop
            b.BladeA = Hex("#2f2723"); b.BladeB = Hex("#3d332c"); b.BladeRoot = Hex("#171312");
            b.BladeHeight = 0.125f; b.BladeWidth = 0.130f; b.Sway = 0.065f;
            b.ShadowTint = Hex("#8a7f8f"); b.CloudAmount = 0.6f;
            return b;
        }

        // ── shallows ─────────────────────────────────────────────────────────────────────

        static BiomeLook Water()
        {
            var b = Common();
            b.Name = "deep water";
            b.Base = Hex("#123449"); b.Tint2 = Hex("#1a4a66"); b.Tint3 = Hex("#0b2333");
            b.Highlight = Hex("#cdeef8");
            b.Waves = 1f; b.MotionSpeed = 0.45f;

            // Water is the one biome whose surface MOVES, so its relief lives in the shader's
            // normals rather than in the mesh. The mesh carries only a long, low swell - enough
            // that the far water is not a flat plane, shallow enough that nothing on the board
            // looks like it is sitting on a hill of sea.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.30f, Wavelength = 9f, WindStretch = 0.70f, WindAngle = -8f,
                Ridge = 0f, Detail = 0.05f, PlateauPad = 1.0f, PlateauFalloff = 3.5f,
            };

            b.SunAngle = -8f; b.SunElevation = 13f;
            b.SunColor = Hex("#ffe3b6"); b.SkyColor = Hex("#9dc6e8"); b.BounceColor = Hex("#123a4e");
            b.Sheen = 1f; b.SheenPower = 80f; b.ShadowDepth = 0.18f;
            b.StreakAmount = 0.16f; b.StreakScale = 5f; b.DetailBump = 0.55f;
            b.CrestLight = 0.24f; b.TroughShade = 0.22f; b.Sparkle = 0.8f;
            b.HazeColor = Hex("#b9d6e6"); b.HazeStart = 10f; b.HazeDensity = 0.55f;

            b.VeilAmount = 0.35f; b.VeilColor = Hex("#dceef5");
            b.VeilSpeed = 1.1f; b.VeilScale = 6.5f; b.VeilHeight = 0.9f;

            b.BladeDensity = 0f;                       // open water
            b.BladeA = Color.white; b.BladeB = Color.white; b.BladeRoot = Color.white;
            b.BladeHeight = 0f; b.BladeWidth = 0f; b.Sway = 0f;
            b.ShadowTint = Hex("#6f86b5"); b.CloudAmount = 0.9f;
            return b;
        }


        // ── mud ──────────────────────────────────────────────────────────────────────────

        static BiomeLook Earth()
        {
            var b = Common();
            b.Name = "mud";
            // Churned wet ground. The colour range is narrow on purpose - mud is one colour with
            // a lot of WETNESS variation, and it is the sheen that does the describing, not hue.
            b.Base = Hex("#4a3a2c"); b.Tint2 = Hex("#5a4835"); b.Tint3 = Hex("#382b21");
            b.Highlight = Hex("#9c8a6a");
            b.MotionSpeed = 0.25f;

            // Rutted and churned rather than dune-shaped: no wind carving, lots of small detail,
            // because what shapes mud is things walking through it.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.55f, Wavelength = 5.0f, WindStretch = 0.20f, WindAngle = 55f,
                Ridge = 0.12f, Detail = 0.55f, PlateauPad = 0.6f, PlateauFalloff = 2.2f,
            };

            b.SunAngle = -46f; b.SunElevation = 20f;
            b.SunColor = Hex("#f0e2c4"); b.SkyColor = Hex("#8ea2bc"); b.BounceColor = Hex("#3a2e23");
            // Wet ground is the shiniest thing in this table - a broad, weak sheen over all of it
            // is the whole difference between mud and dirt.
            b.Sheen = 0.55f; b.SheenPower = 14f; b.ShadowDepth = 0.44f;
            b.StreakAmount = 0.12f; b.StreakScale = 3.0f; b.DetailBump = 1.15f;
            b.CrestLight = 0.14f; b.TroughShade = 0.42f; b.Sparkle = 0.18f;
            b.HazeColor = Hex("#8b8375"); b.HazeStart = 11f; b.HazeDensity = 0.42f;

            b.VeilAmount = 0.10f; b.VeilColor = Hex("#9a8c78");
            b.VeilSpeed = 0.4f; b.VeilScale = 4.5f; b.VeilHeight = 0.8f;

            b.BladeDensity = 9f;                       // trampled weeds at the edges
            b.BladeA = Hex("#4e5a30"); b.BladeB = Hex("#66743e"); b.BladeRoot = Hex("#2b3119");
            b.BladeHeight = 0.150f; b.BladeWidth = 0.150f; b.Sway = 0.040f;
            b.BushDensity = 0.5f;
            b.BushA = Hex("#3f4a28"); b.BushB = Hex("#556133"); b.BushRoot = Hex("#232a14");
            b.BushHeight = 0.52f; b.BushWidth = 0.62f; b.BushSway = 0.022f;

            b.ShadowTint = Hex("#7b7f96"); b.CloudAmount = 1f;
            return b;
        }

        // ── the shore: sand with water over it ───────────────────────────────────────────

        static BiomeLook Shore()
        {
            var b = Common();
            b.Name = "shore";
            // Sand seen THROUGH water. The base is wet sand and the swell rides on top of it,
            // which is why this is not the deep-water row with a lighter colour: the surface
            // motion is the same term, but what it is moving over is ground you can see.
            b.Base = Hex("#8a7c58"); b.Tint2 = Hex("#a2916a"); b.Tint3 = Hex("#5d6a63");
            b.Highlight = Hex("#eaf6f8");
            b.Waves = 0.62f; b.Ripples = 0.45f; b.MotionSpeed = 0.42f;

            // Almost flat - a beach is flat, and the shallow swell reads entirely in the shading.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.34f, Wavelength = 8f, WindStretch = 0.55f, WindAngle = -14f,
                Ridge = 0.10f, Detail = 0.22f, PlateauPad = 0.8f, PlateauFalloff = 3.0f,
            };

            b.SunAngle = -12f; b.SunElevation = 12f;
            b.SunColor = Hex("#ffe8c0"); b.SkyColor = Hex("#a8cbe8"); b.BounceColor = Hex("#4a5a52");
            b.Sheen = 0.85f; b.SheenPower = 46f; b.ShadowDepth = 0.24f;
            b.StreakAmount = 0.20f; b.StreakScale = 4.5f; b.DetailBump = 0.70f;
            b.CrestLight = 0.20f; b.TroughShade = 0.26f; b.Sparkle = 0.55f;
            b.HazeColor = Hex("#cfe0e2"); b.HazeStart = 10f; b.HazeDensity = 0.50f;

            b.VeilAmount = 0.30f; b.VeilColor = Hex("#e6f0ee");
            b.VeilSpeed = 1.0f; b.VeilScale = 6f; b.VeilHeight = 0.9f;

            b.BladeDensity = 2.5f;                     // marram grass above the tideline
            b.BladeA = Hex("#8a9464"); b.BladeB = Hex("#a5ad7c"); b.BladeRoot = Hex("#5c6141");
            b.BladeHeight = 0.185f; b.BladeWidth = 0.130f; b.Sway = 0.070f;

            b.ShadowTint = Hex("#7f9ab5"); b.CloudAmount = 0.95f;
            return b;
        }

        /// <summary>Defaults, so a new biome only states what makes it different.</summary>
        static BiomeLook Common()
        {
            return new BiomeLook
            {
                Waves = 0f, Ripples = 0f, Embers = 0f, MotionSpeed = 0.35f,
                Terrain = TerrainProfile.Flat(),
                SunAngle = 0f, SunElevation = 14f,
                SunColor = Color.white, SkyColor = Hex("#9ab6d8"), BounceColor = Hex("#4a4034"),
                Sheen = 0.2f, SheenPower = 20f, ShadowDepth = 0.45f,
                StreakAmount = 0.3f, StreakScale = 3f, DetailBump = 0.6f,
                CrestLight = 0.2f, TroughShade = 0.25f, Sparkle = 0f,
                HazeColor = Hex("#c8d6e0"), HazeStart = 10f, HazeDensity = 0.5f,
                VeilAmount = 0f, VeilColor = Color.white,
                VeilSpeed = 1f, VeilScale = 6f, VeilHeight = 1.2f,
                FallAmount = 0f, FallColor = Color.white,
                FallSpeed = 0.35f, FallDrift = 0.15f, FallSize = 1f, FallSwirl = 0.5f,
                BushDensity = 0f, BushA = Color.white, BushB = Color.white, BushRoot = Color.white,
                BushHeight = 0.5f, BushWidth = 0.6f, BushSway = 0.03f,
                CloudAmount = 1f,
            };
        }

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
