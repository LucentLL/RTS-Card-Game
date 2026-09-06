using UnityEngine;

namespace SpawnRowDuel.View.World
{
    public enum BiomeId { Grass, Sand, Water, Ash, Snow, Earth, Shore, Seabed }

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

        /// <summary>How much loose material the wind is carrying, as dashes rather than dots.
        /// This is the dunes' signature and it wants to be near zero anywhere still.</summary>
        public float GrainAmount;
        public Color GrainColor;

        /// <summary>
        /// How the wind VARIES, which is most of what separates weather from a scrolling texture.
        /// Swing is how far the strength breathes either side of its mean (0 is a constant blow,
        /// 1 swings from a lull to about double), Period is how many seconds one breath takes, and
        /// Wander is how many degrees the bearing drifts either way while it does it.
        /// </summary>
        public float GustSwing, GustPeriod, GustWander;

        // ---- what falls out of the sky --------------------------------------------------------
        /// <summary>Snow, ash. Separate from the veil, which is what the wind drags ACROSS the
        /// ground - a horizontal sheet cannot show vertical motion.</summary>
        public float FallAmount;
        public Color FallColor;
        /// <summary>Flakes per square world unit, and how high they start.</summary>
        public float FallDensity, FallHeight;
        /// <summary>Speed is CYCLES PER SECOND now - a flake's whole fall, spawn to landing - and
        /// size is in world units, because the flakes are in the world rather than on the screen.</summary>
        public float FallSpeed, FallDrift, FallSize, FallSwirl;

        // ---- what has already landed ----------------------------------------------------------
        /// <summary>How much cover one PLY buys, as a fraction of full. Zero means nothing settles
        /// - rain does not lie and neither does spray.
        ///
        /// Per ply, not per second. On a clock the board went from clean to buried inside about
        /// one round and kept thickening while a player sat looking at their hand, which is
        /// weather happening TO the screen rather than to the match. Everything else about this
        /// system is turn-shaped already.</summary>
        public float SettleRate;
        /// <summary>Thick cover and thin cover are different colours, not one colour at two
        /// alphas: a dusting of ash is dirty grey and a covering of it is pale.</summary>
        public Color SettleColor, SettleShade;
        /// <summary>The most of a card it may ever cover. Never 1 - a card you cannot read is a
        /// rule broken by scenery, and a board buried in grey is a board nobody can play.</summary>
        public float SettleMax, SettleGrain, SettleSparkle;
        /// <summary>Where the coverage stops GROWING. Deliberately short of full: coverage is a
        /// threshold against noise, so a field at 1 is uniform and a field at 0.6 is patchy, and
        /// patchy is what settled ash looks like.</summary>
        public float SettleCap;

        /// <summary>
        /// How much of a card's hollow ONE PLY fills back in, as a fraction of full.
        /// <see cref="SettleRate"/>'s twin: what buries a card is what fills the print it left.
        ///
        /// Per ply, not per second, and for the same reason - ground that heals itself while a
        /// player sits thinking is weather happening to the screen rather than to the match. This
        /// is the number that decides how long a battlefield remembers where the fighting was, so
        /// it is keyed to how much loose material the field actually has moving over it: blowing
        /// sand fills a footprint in a few rounds, packed earth keeps one for the whole duel.
        ///
        /// 0 is legal and means a mark that never fills. Do not set it on ground that has nothing
        /// in the air to fill it WITH unless you want the board permanently scarred - which is a
        /// look, but it is a decision.
        /// </summary>
        public float RefillRate;

        // ---- the tide -------------------------------------------------------------------------
        /// <summary>0 for everything that is not a beach. The waterline is a position along
        /// <see cref="TideDir"/> that swings by TideRange every TidePeriod seconds.</summary>
        public float TideAmount, TideLevel, TideRange, TidePeriod;
        /// <summary>Which way the sea is. +Z is past the far wall, which is where it belongs:
        /// the water comes in at the top of the frame and the players are up the beach.</summary>
        public Vector2 TideDir;
        public float WaveFreq, WaveSpeed;
        public Color WaterColor, DeepColor, FoamColor;

        // ---- the swell -------------------------------------------------------------------------
        /// <summary>Open water: which way the wave train marches (xz), how hard its slope bends
        /// the light, and how much white it breaks off at the brink.
        ///
        /// Separate from the tide on purpose. A tide is a LINE that runs up a beach and drains
        /// back; a swell has no line and nothing recedes - it just keeps coming, which is the
        /// whole difference between a shore and a sea.</summary>
        public Vector2 SwellDir;
        public float SwellHeight, SwellFoam;

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
        /// <summary>How big one cloud cell is in world units, and how many of them the pattern
        /// crosses a second. Per biome because the same projected pattern is not always cloud: on
        /// a seabed it is the shadow between the bands of light coming down through the surface,
        /// and caustics are small and quick where weather is large and slow.</summary>
        public float CloudScale, CloudSpeed;
    }

    public static class Biomes
    {
        public static readonly BiomeId[] All =
        {
            BiomeId.Grass, BiomeId.Earth, BiomeId.Sand, BiomeId.Snow,
            BiomeId.Ash, BiomeId.Shore, BiomeId.Water, BiomeId.Seabed,
        };

        /// <summary>
        /// NOTE the default. A biome that reaches this switch without a case of its own comes back
        /// as a MEADOW, silently, under its own name - no compiler warning, no failing test, and a
        /// probe shot that looks perfectly reasonable. Adding a BiomeId without adding a case here
        /// is the one mistake in this file that costs an afternoon.
        /// </summary>
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
                case BiomeId.Seabed: return Seabed();
                default: return Grass();
            }
        }

        public static string NameOf(BiomeId id) { return Of(id).Name; }

        // ── meadow ───────────────────────────────────────────────────────────────────────

        static BiomeLook Grass()
        {
            var b = Common();
            b.Name = "meadow";
            // The ground goes DARKER and the blades go brighter, and that pair is most of what
            // was wrong. Measured through the shader, a blade came out (0.244, 0.412, 0.155) and
            // the ground under it (0.239, 0.420, 0.180) - the same colour to two decimals. A field
            // at eighty per cent coverage whose grass has no value contrast against its own dirt
            // is not a field, it is a flat green mat, and no amount of blade shape fixes that.
            b.Base = Hex("#2f5726"); b.Tint2 = Hex("#38652c"); b.Tint3 = Hex("#274a20");
            b.Highlight = Hex("#a8c96a");
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
            // A few seeds and insects in the light, nothing more - and nothing lies.
            b.FallAmount = 0.5f; b.FallColor = Hex("#eef6d2");
            b.FallDensity = 0.09f; b.FallHeight = 5f;
            b.FallSpeed = 0.09f; b.FallDrift = 1.5f; b.FallSize = 0.035f; b.FallSwirl = 2.4f;
            b.GrainAmount = 0.25f;

            // LONG. The quad is two and a half times as tall as it is wide, matching the atlas
            // cell, and the atlas draws twenty hair-thin blades in it instead of six fat ones.
            // The previous pass was 0.175 wide by 0.185 tall - a SQUARE sprite of square-ish
            // blades - and a meadow of them came out looking like a field of cabbages. Density is
            // slightly down and coverage is well up, because each quad is now worth two and a half
            // of the old ones.
            // THICK, and the same thickness over the board as beside it. Thinning it was tried
            // first, on the reasoning that the tiles have to read through the grass - and that is
            // the wrong end of the problem, because what makes a square read is a CARD lying on it
            // crushing the grass flat, not the meadow being kept short in case one ever does.
            b.BladeDensity = 30f;
            b.BladeA = Hex("#5c9c33"); b.BladeB = Hex("#b3dd5e"); b.BladeRoot = Hex("#1b3a12");
            b.BladeHeight = 0.46f; b.BladeWidth = 0.20f; b.Sway = 0.105f;

            // ...and TUSSOCKS standing above it: the same clump, taller and much narrower. They
            // used to be 0.92 wide against 0.70 tall, which is a shrub, and a meadow full of
            // shrubs is a hedge maze. A tussock is a rank of long grass that has got away.
            b.BushDensity = 3.2f;
            b.BushA = Hex("#3f7526"); b.BushB = Hex("#8fc248"); b.BushRoot = Hex("#17330f");
            b.BushHeight = 0.95f; b.BushWidth = 0.34f; b.BushSway = 0.055f;
            // Grass is the one field that fills a print by GROWING rather than by drifting, and
            // it is slow at it. Ten rounds, which is what the old regrowth claimed and - on a
            // wall clock that ran while nobody was playing - never actually delivered.
            b.RefillRate = 0.05f;
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
            b.VeilSpeed = 1.1f; b.VeilScale = 7f; b.VeilHeight = 1.5f;
            // WIND THAT GUSTS. One speed, one direction and one density, held forever, is a
            // scrolling texture however good the noise inside it is - and a desert is the biome
            // where that reads worst, because the air is the subject. The veil breathes now: it
            // swells and lulls on a slow clock and its bearing wanders either side of the dune
            // trend, so the sand comes in squalls rather than at a constant rate.
            b.GustSwing = 1f; b.GustPeriod = 13f; b.GustWander = 26f;
            // The one biome where the AIR is the subject. Grains stream in the gusts as dashes.
            b.GrainAmount = 0.95f; b.GrainColor = Hex("#fff0cf");

            // None. A desert with grass in it is a lawn that needs watering, and the reference
            // has not a blade in sight - the surface itself is the whole texture.
            b.BladeDensity = 0f;
            b.BladeA = Hex("#a89253"); b.BladeB = Hex("#c4ad6a"); b.BladeRoot = Hex("#7a6636");
            b.BladeHeight = 0.115f; b.BladeWidth = 0.140f; b.Sway = 0.058f;
            // The fastest in the table, and the whole reason this is a per-biome number: this is
            // the field with the most material in the air (VeilAmount 0.85, GrainAmount 0.95), and
            // blown sand fills a footprint in five rounds. A desert that kept a card's outline for
            // the length of a duel would be arguing with its own weather.
            b.RefillRate = 0.10f;
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
            b.VeilSpeed = 1.5f; b.VeilScale = 6f; b.VeilHeight = 1.3f;
            // Colder wind gusts harder and swings less - a blizzard comes in slabs.
            b.GustSwing = 1.15f; b.GustPeriod = 9f; b.GustWander = 16f;
            b.GrainAmount = 0.7f; b.GrainColor = Hex("#ffffff");

            b.FallAmount = 1.0f; b.FallColor = Hex("#ffffff");
            b.FallDensity = 0.80f; b.FallHeight = 7f;
            b.FallSpeed = 0.30f; b.FallDrift = 0.85f; b.FallSize = 0.065f; b.FallSwirl = 1.0f;

            // ...and it LIES. Snow on the tiles, snow on the cards, until something moves. 0.08 a
            // ply: the first patches show around the fourth and it caps around the ninth, where
            // 0.055 a SECOND had half the board white in under nine seconds flat.
            b.SettleRate = 0.08f;
            b.SettleColor = Hex("#f4f8ff"); b.SettleShade = Hex("#b6c5da");
            // 0.58, not 0.70. At full cover the readout on a card was still legible and only just
            // - and "only just" is the wrong side of the line for a number a player has to read
            // every turn. Scenery does not get to win that one.
            b.SettleMax = 0.58f; b.SettleCap = 0.72f;
            b.SettleGrain = 1.3f; b.SettleSparkle = 0.9f;

            b.BladeDensity = 0f;                       // a smooth crust; nothing grows through
            b.BladeA = Hex("#8d8f92"); b.BladeB = Hex("#a9aaad"); b.BladeRoot = Hex("#5f6165");
            b.BladeHeight = 0.100f; b.BladeWidth = 0.120f; b.Sway = 0.070f;
            // The same number as its SettleRate, on purpose: the snow that greys over a card is
            // the snow that fills the hollow it leaves, and the two effects should finish together.
            b.RefillRate = 0.08f;
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
            b.GrainAmount = 0.55f; b.GrainColor = Hex("#d8b49a");

            // Snow, but slower. Ash is lighter than snow and has further to come, so it hangs -
            // a third of the fall rate, twice the wander, and it never falls straight. Small,
            // too: an ash flake is a flake, and the old pass drew it the size of a snowball.
            b.FallAmount = 0.95f; b.FallColor = Hex("#d6cbc1");
            b.FallDensity = 1.05f; b.FallHeight = 7.5f;
            b.FallSpeed = 0.11f; b.FallDrift = 1.5f; b.FallSize = 0.062f; b.FallSwirl = 2.0f;

            // ...and then it LIES THERE. This is the point of the whole biome: ash gathers in the
            // seams of the board and greys over the cards, and stays until something moves.
            // Ash is slower than snow and starts later - it caps around the tenth ply, and the
            // seams of the board fill well before the faces of the tiles do.
            b.SettleRate = 0.06f;
            b.SettleColor = Hex("#b8b0a8"); b.SettleShade = Hex("#5f574f");
            b.SettleMax = 0.52f; b.SettleCap = 0.58f;
            b.SettleGrain = 2.4f; b.SettleSparkle = 0f;

            // A few burnt stalks, not a crop - but stalks the colour of the ash they stand in are
            // stalks nobody sees. Burnt straw against black ground, and the light does the rest.
            b.BladeDensity = 2.2f;
            b.BladeA = Hex("#3a2f26"); b.BladeB = Hex("#6b5a44"); b.BladeRoot = Hex("#171310");
            b.BladeHeight = 0.30f; b.BladeWidth = 0.145f; b.Sway = 0.085f;
            b.RefillRate = 0.06f;                      // its SettleRate, for the same reason as snow
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
            b.Waves = 1f; b.MotionSpeed = 0.26f;

            // THE SWELL. A long train rolling toward the player and a little to the right, so the
            // crest lines run across the frame where they can be counted - a train marching along
            // the camera axis is a train you can only see by its speed. 4.5 units between crests
            // at 1.4 a second is one crest passing about every three, which is a swell; the old
            // pair of fixed sines had nothing between crests at all because it had no crests.
            b.SwellDir = new Vector2(0.34f, -0.94f);
            b.SwellHeight = 0.26f; b.SwellFoam = 0.75f;
            // SLOW. A crest every three seconds is a wave machine; ocean swell has a period of
            // eight to twelve, and the difference between the two is the difference between water
            // and a screensaver of water.
            b.WaveFreq = 0.20f; b.WaveSpeed = 2.4f;

            // Water is the one biome whose surface MOVES, so its relief lives in the shader's
            // normals rather than in the mesh. The mesh carries only a long, low swell - enough
            // that the far water is not a flat plane, shallow enough that nothing on the board
            // looks like it is sitting on a hill of sea.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.55f, Wavelength = 9f, WindStretch = 0.70f, WindAngle = -8f,
                Ridge = 0f, Detail = 0.05f, PlateauPad = 1.0f, PlateauFalloff = 3.5f,
            };

            b.SunAngle = -8f; b.SunElevation = 13f;
            b.SunColor = Hex("#ffe3b6"); b.SkyColor = Hex("#9dc6e8"); b.BounceColor = Hex("#123a4e");
            // A BROADER sheen than the 80 it had. At 80 the highlight is a hairline, and a crest
            // lit by a hairline is a glossy streak rather than a wave; widening it lets the whole
            // windward face of a swell take light, which is what says the surface has shape.
            b.Sheen = 1f; b.SheenPower = 42f; b.ShadowDepth = 0.18f;
            b.StreakAmount = 0.16f; b.StreakScale = 5f; b.DetailBump = 0.55f;
            b.CrestLight = 0.24f; b.TroughShade = 0.22f; b.Sparkle = 0.8f;
            b.HazeColor = Hex("#b9d6e6"); b.HazeStart = 10f; b.HazeDensity = 0.55f;

            // NO GRAINS. The veil's grain pass draws saltating SAND - a grain hopping downwind in
            // a long flat arc, smeared into a dash by its own speed - and there is no sand on open
            // water. Over this biome they came out as a field of small pale specks travelling
            // nearly straight away from the camera, which is neither current nor wind and read as
            // neither. What is left is the sheet: low spray and haze over the surface, slower,
            // which is the only thing that belongs in the air over a sea.
            b.VeilAmount = 0.22f; b.VeilColor = Hex("#dceef5");
            b.VeilSpeed = 0.6f; b.VeilScale = 6.5f; b.VeilHeight = 0.9f;
            b.GrainAmount = 0f; b.GrainColor = Hex("#eaf8ff");

            b.BladeDensity = 0f;                       // open water
            b.BladeA = Color.white; b.BladeB = Color.white; b.BladeRoot = Color.white;
            b.BladeHeight = 0f; b.BladeWidth = 0f; b.Sway = 0f;
            b.RefillRate = 0.20f;                      // nothing holds a shape in open water
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

            b.BladeDensity = 13f;                      // trampled weeds at the edges
            b.BladeA = Hex("#5d7038"); b.BladeB = Hex("#9fb463"); b.BladeRoot = Hex("#2b3119");
            b.BladeHeight = 0.34f; b.BladeWidth = 0.165f; b.Sway = 0.075f;
            b.BushDensity = 0.6f;
            b.BushA = Hex("#3f4a28"); b.BushB = Hex("#5f6d39"); b.BushRoot = Hex("#232a14");
            b.BushHeight = 0.72f; b.BushWidth = 0.28f; b.BushSway = 0.042f;
            b.GrainAmount = 0.2f;

            // The slowest in the table, and the point of the biome. Churned wet ground takes a
            // print and KEEPS it - there is almost nothing in this air to fill one back in
            // (VeilAmount 0.10) - so a mud field ends a long duel visibly rutted by where the
            // fighting was. 33 plies to erase, which most matches never reach.
            b.RefillRate = 0.03f;
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
            b.Waves = 0.62f; b.Ripples = 0.45f; b.MotionSpeed = 0.26f;

            // THE TIDE. The sea is off the far edge, past the foe's wall, and the waterline runs
            // up the beach and drains back on a 24-second breath with a faster swash riding on it.
            // A shore whose water only ever flowed one way was a river with sand beside it; what
            // says "sea" is the line moving, the wave train marching in behind it, and the dark
            // band of sand between where the water is and where it just was.
            b.TideAmount = 1f; b.TideDir = new Vector2(0f, 1f);
            // The water runs ACROSS THE BOARD, and that is not a liberty - it is what the
            // framing leaves. The camera frames the board to fill the viewport, so everything
            // past the far row compresses into sixty rows of pixels: a sea out there is a bright
            // sliver under the wall band that no one would ever call a shore. A beach flat enough
            // to fight on is a beach the wash runs over, so the waterline sweeps from well past
            // the far wall down to the middle of the board and drains back, and the tiles it
            // crosses go wet and foamed rather than blue.
            // FORTY seconds, not twenty-two. A tide that comes in and goes out twice a minute is
            // a pump; the whole read of a shore is that the water takes its time, and at 22 the
            // waterline was visibly sliding while a player was reading their hand.
            b.TideLevel = 3.5f; b.TideRange = 5.0f; b.TidePeriod = 40f;
            b.WaveFreq = 0.40f; b.WaveSpeed = 1.1f;
            // A swell under the tide, shoreward and slight. The tide's own train is what breaks on
            // the waterline; this is the water behind it having a surface at all.
            b.SwellDir = new Vector2(0.10f, -0.99f);
            b.SwellHeight = 0.07f; b.SwellFoam = 0.25f;
            b.WaterColor = Hex("#2f6f74"); b.DeepColor = Hex("#134350");
            b.FoamColor = Hex("#eef8f8");

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
            b.GrainAmount = 0.5f; b.GrainColor = Hex("#fff4dd");

            // NO MARRAM. There was a band of it above the tideline - dark against the pale sand so
            // it would not disappear - and the shore reads better without: sand and water and the
            // line where they meet is the whole of the picture, and a fringe of weed across the
            // near half is one thing too many in it.
            b.BladeDensity = 0f;
            b.BladeA = Hex("#6f8a3e"); b.BladeB = Hex("#a8bd6a"); b.BladeRoot = Hex("#4a5530");
            b.BladeHeight = 0.42f; b.BladeWidth = 0.15f; b.Sway = 0.115f;

            // The tide erases a footprint every time it comes over it, and it comes over the board
            // every forty seconds. Seven plies.
            b.RefillRate = 0.14f;
            b.ShadowTint = Hex("#7f9ab5"); b.CloudAmount = 0.95f;
            return b;
        }

        // ── the seabed: sand and mud at the bottom of it ─────────────────────────────────

        /// <summary>
        /// The floor of a lake or a shallow sea, fought on from underneath.
        ///
        /// Not "the water biome, lower down". Water and Shore are both surfaces seen from above -
        /// the whole subject is where the air meets the sea. Here the surface is somewhere overhead
        /// and off-screen, and the only evidence of it is the LIGHT it lets through. So the terms
        /// that carry this field are the ones that describe a medium: the haze, which starts closer
        /// and bites harder than anywhere else in the table; a sun with the red drowned out of it;
        /// an ambient that comes mostly from the lit water column rather than from the beam; and
        /// the swell, read as bands of light crossing the sand rather than as a shape on a surface.
        ///
        /// The four questions this field had to answer, in the order they get asked of any effect
        /// here. Does it ARRIVE - yes: the silt falls, lands and lies on the board until something
        /// moves. Does it ALTERNATE - yes: the swell bands run one way and the caustic net wanders
        /// both, and neither is a scroll. Is it the BOARD'S SHAPE - the impression, the gust ring
        /// and the silt wipe are all the same card-shaped rounded rectangles they are everywhere
        /// else. Is it UNIFORM - no: every drifting mote has its own rate, phase and wobble.
        /// </summary>
        static BiomeLook Seabed()
        {
            var b = Common();
            b.Name = "seabed";

            // Sand seen through METRES of water, not through a film of it. Water eats red first,
            // so the grains that are #a98d4c in the dunes and #8a7c58 wet on the shore land here a
            // third darker with the red pulled down into the green. Tint3 is the mud between the
            // ripples - Earth's #4a3a2c with the warmth taken out of it.
            b.Base = Hex("#9c9268"); b.Tint2 = Hex("#b0a67c"); b.Tint3 = Hex("#6f7160");
            // NOT foam-white. Highlight is what the ripple drift lerps toward and what the card's
            // lip picks up, so it has to be lit SEDIMENT.
            b.Highlight = Hex("#cfd8b8");

            // THE LIGHT COMING DOWN, as the swell term. At Water's 1.0 this is a sea; at 0.45,
            // with no tide under it, what is left is the part that reads on a floor - troughs
            // pulled toward DeepColor, crest bands brightened, the sparkle pinned to those bands.
            // Bands of light crossing sand, which is the thing everybody has actually seen.
            b.Waves = 0.45f;
            // The seabed's own signature, and the one term this shares honestly with the dunes:
            // orbital motion combs sand into ripples exactly as wind does.
            b.Ripples = 0.85f;
            b.MotionSpeed = 0.18f;                     // the slowest clock in the table. Water is not air.

            // Low mounds and swales. A drowned dune field at Sand's 1.75 would bury a card and read
            // as a desert with a filter on it; a beach's 0.34 has no landscape left once the haze
            // has eaten the distance. WindStretch stays high - the shaping current is as
            // directional as wind - but Ridge is low, because water rounds a brink that air
            // sharpens, and Detail is second only to mud: a seabed is pocked with burrow and shell.
            b.Terrain = new TerrainProfile
            {
                Amplitude = 0.72f, Wavelength = 6.0f, WindStretch = 0.74f, WindAngle = -6f,
                Ridge = 0.20f, Detail = 0.42f, PlateauPad = 0.8f, PlateauFalloff = 3.0f,
            };

            // A LOW BLUE SUN. Every other row's sun is warm; this one has been through ten metres
            // of water and has no red left in it. Elevation 16 rather than the deserts' 9-11
            // because refraction bends the beam toward the vertical - still low enough to throw the
            // long soft shadows off the mounds that make the relief read at all.
            b.SunAngle = -14f; b.SunElevation = 24f;
            b.SunColor = Hex("#c8ecdf");
            // ...and the most saturated sky in the table by a distance, because down here "sky" is
            // the lit water column and scatter does more of the work than the beam does.
            b.SkyColor = Hex("#79a5a2");
            b.BounceColor = Hex("#6e7358");            // pale floor throwing green light back up
            // Weaker than mud's 0.55: the sand is wet, but the air-water interface that gives water
            // its specular is far overhead rather than here.
            b.Sheen = 0.20f; b.SheenPower = 16f;
            // The lowest in the table, under open water's 0.18. Nothing casts a black shadow in a
            // medium that scatters from every direction at once.
            b.ShadowDepth = 0.16f;

            b.StreakAmount = 0.16f; b.StreakScale = 4.0f;
            b.DetailBump = 0.95f;                      // rippled sand is very nearly all bump
            b.CrestLight = 0.16f; b.TroughShade = 0.36f;
            b.Sparkle = 0.5f;                          // glitter that travels with the light bands

            // THE HEADLINE, and the number that makes this a battlefield underwater rather than a
            // battlefield with a blue filter: the distance does not fade, it DISSOLVES, and it
            // dissolves dark - the deserts go to a bright #e8d3ab, this goes into the water.
            //
            // Tuned back from 3/0.95 after looking at it. Haze is measured on the camera's XZ
            // distance and the camera stands seven units off the near row, so a start of 3 was
            // putting the whole BOARD behind the murk: the ground the player was asked for -
            // "the sand or mud at the bottom" - never showed at all, and the field read as a
            // green fog with cards floating in it. Starting past the board and biting hard after
            // it keeps both halves: sand you can see, water you cannot see through.
            b.HazeColor = Hex("#2f5f66"); b.HazeStart = 7.5f; b.HazeDensity = 0.78f;

            // SUSPENDED MATTER, hanging rather than blowing. Open water zeroes its grains because
            // "there is no sand on open water" - the opposite is true down here, and the grain pass
            // draws near-isotropic specks that suit marine snow exactly.
            b.VeilAmount = 0.55f; b.VeilColor = Hex("#9fc0bf");
            // The slowest veil in the table by a factor of two, and this one number is most of what
            // says water rather than air.
            b.VeilSpeed = 0.22f; b.VeilScale = 4.5f; b.VeilHeight = 1.6f;
            b.GrainAmount = 0.75f; b.GrainColor = Hex("#dff0ec");
            // A current BREATHES; it does not gust. The calmest air in the table, and eight degrees
            // of wander so the drift holds one bearing.
            b.GustSwing = 0.30f; b.GustPeriod = 30f; b.GustWander = 8f;

            // SILT, FALLING. Slower than ash - which was already the slowest thing here - and small
            // with a lot of wobble, because a fleck of marine snow has no weight to speak of and
            // takes its time. This is the layer that answers "does it arrive?".
            b.FallAmount = 0.8f; b.FallColor = Hex("#dfe9dd");
            b.FallDensity = 0.55f; b.FallHeight = 6.5f;
            b.FallSpeed = 0.055f; b.FallDrift = 1.7f; b.FallSize = 0.045f; b.FallSwirl = 2.6f;

            // ...and it LIES on the board. Slower than ash's 0.06 and capped much lower: silt
            // greys the seams and dusts the cards, and a seabed buried pale inside ten plies would
            // be the snow bug wearing a wetsuit. Half of what snow lays down, arriving half as fast.
            b.SettleRate = 0.03f;
            b.SettleColor = Hex("#c3c9b4"); b.SettleShade = Hex("#5b6a63");
            b.SettleMax = 0.34f; b.SettleCap = 0.44f;
            b.SettleGrain = 2.8f; b.SettleSparkle = 0.15f;

            // Silt fills a hollow, so the ground here does forget - but at a seabed's pace, which
            // is between the snowfield and the meadow. Fourteen plies.
            b.RefillRate = 0.07f;

            // A LONG SWELL, read on the floor. Half open water's frequency, so a light band is
            // about nine units wide and one crosses the board in eight seconds - the period of real
            // ocean swell. SwellHeight is a third of open water's because down here the term is the
            // refractive wobble of the floor, not the shape of a surface.
            b.SwellDir = new Vector2(0.22f, -0.97f);
            b.SwellHeight = 0.10f;
            // ZERO, and not a taste call: the swell breaks whitecaps off its crests, and a whitecap
            // on a seabed is a bug. The only row that runs Waves with no foam at all.
            b.SwellFoam = 0f;
            b.WaveFreq = 0.11f; b.WaveSpeed = 1.6f;
            b.DeepColor = Hex("#173f4a"); b.FoamColor = Hex("#dff2f0");

            // EELGRASS, patchy - a quarter of the meadow's density. The tallest blade in the table,
            // because eelgrass is long, and the widest sway, because water moves a blade further
            // than air does. It crushes under a card exactly as grass does.
            b.BladeDensity = 4.5f;
            b.BladeA = Hex("#3d6b53"); b.BladeB = Hex("#6f9f6a"); b.BladeRoot = Hex("#1d3529");
            b.BladeHeight = 0.52f; b.BladeWidth = 0.17f; b.Sway = 0.16f;
            b.BushDensity = 0.45f;
            b.BushA = Hex("#2f5a4a"); b.BushB = Hex("#517f5c"); b.BushRoot = Hex("#16302a");
            b.BushHeight = 0.85f; b.BushWidth = 0.40f; b.BushSway = 0.10f;

            // CAUSTICS. The same projected pattern every other field uses for cloud, at a quarter
            // the size and three times the speed: over a seabed what crosses the ground is not
            // weather, it is the shadow between the bands of light coming down through a surface
            // that will not hold still. Deep blue-green rather than the violet-grey every other row
            // shades with, because this is water taking the light out, not a cloud.
            b.ShadowTint = Hex("#1e4d59"); b.CloudAmount = 0.85f;
            b.CloudScale = 1.7f; b.CloudSpeed = 1.6f;
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
                GrainAmount = 0.4f, GrainColor = Color.white,
                GustSwing = 0.45f, GustPeriod = 15f, GustWander = 12f,
                FallAmount = 0f, FallColor = Color.white,
                FallDensity = 0f, FallHeight = 7f,
                FallSpeed = 0.2f, FallDrift = 0.9f, FallSize = 0.06f, FallSwirl = 1.2f,
                SettleRate = 0f, SettleColor = Color.white, SettleShade = Color.grey,
                SettleMax = 0.6f, SettleCap = 0.6f, SettleGrain = 1.6f, SettleSparkle = 0f,
                // 20 plies - ten full rounds - to erase a print, and still 70% deep three rounds
                // later. Deliberately NOT derived from SettleRate: five fields settle nothing and
                // would keep every mark for ever, with no weather on screen to explain why.
                RefillRate = 0.05f,
                TideAmount = 0f, TideLevel = 9f, TideRange = 3.4f, TidePeriod = 26f,
                TideDir = new Vector2(0f, 1f), WaveFreq = 0.55f, WaveSpeed = 2.1f,
                WaterColor = Hex("#20505c"), DeepColor = Hex("#0e2c3a"),
                FoamColor = Hex("#f0fbfb"),
                SwellDir = new Vector2(0.35f, -0.94f), SwellHeight = 0f, SwellFoam = 0f,
                BushDensity = 0f, BushA = Color.white, BushB = Color.white, BushRoot = Color.white,
                BushHeight = 0.5f, BushWidth = 0.6f, BushSway = 0.03f,
                CloudAmount = 1f, CloudScale = 6.5f, CloudSpeed = 0.55f,
            };
        }

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
