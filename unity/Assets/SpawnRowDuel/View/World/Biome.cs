using UnityEngine;

namespace SpawnRowDuel.View.World
{
    public enum BiomeId { Grass, Sand, Water, Ash }

    /// <summary>
    /// What a battlefield looks like, as data.
    ///
    /// Biome is deliberately NOT a shader per ground type. The terrain shader has three motion
    /// terms - waves, ripples, embers - and a biome is a set of amounts for them, so water is
    /// waves at 1 and embers at 0 rather than its own material. The next biome anybody wants is a
    /// row in this table and no new shader at all, which is the whole reason it is shaped this way.
    ///
    /// Nothing here reaches the rules. The board still tints its rows by owner and the engine has
    /// never heard of a biome; this is scenery, and scenery that could change a game would be a
    /// rule wearing a costume.
    /// </summary>
    public struct BiomeLook
    {
        public string Name;
        public Color Base, Tint2, Tint3, Highlight;
        public float Waves, Ripples, Embers, MotionSpeed;

        /// <summary>Blades per square world unit. Zero means open ground - water has no grass.</summary>
        public float BladeDensity;
        public Color BladeA, BladeB, BladeRoot;
        public float BladeHeight, BladeWidth, Sway;

        /// <summary>The colour a cloud shadow pulls the ground toward. Shade is bluer, not just darker.</summary>
        public Color ShadowTint;
        public float CloudAmount;
    }

    public static class Biomes
    {
        public static readonly BiomeId[] All =
            { BiomeId.Grass, BiomeId.Sand, BiomeId.Water, BiomeId.Ash };

        public static BiomeLook Of(BiomeId id)
        {
            switch (id)
            {
                case BiomeId.Sand: return Sand();
                case BiomeId.Water: return Water();
                case BiomeId.Ash: return Ash();
                default: return Grass();
            }
        }

        public static string NameOf(BiomeId id) { return Of(id).Name; }

        static BiomeLook Grass()
        {
            return new BiomeLook
            {
                Name = "meadow",
                Base = Hex("#3d6b2e"), Tint2 = Hex("#477a33"), Tint3 = Hex("#325c27"),
                Highlight = Hex("#8fb05a"),
                Waves = 0f, Ripples = 0f, Embers = 0f, MotionSpeed = 0.3f,
                BladeDensity = 20f,
                BladeA = Hex("#4f8a34"), BladeB = Hex("#6ba844"), BladeRoot = Hex("#26491d"),
                BladeHeight = 0.11f, BladeWidth = 0.050f, Sway = 0.045f,
                ShadowTint = Hex("#7f93c8"), CloudAmount = 1f,
            };
        }

        static BiomeLook Sand()
        {
            return new BiomeLook
            {
                Name = "dunes",
                Base = Hex("#c2a866"), Tint2 = Hex("#d4b878"), Tint3 = Hex("#a98c52"),
                Highlight = Hex("#efe0b0"),
                Waves = 0f, Ripples = 1f, Embers = 0f, MotionSpeed = 0.5f,
                BladeDensity = 5f,                       // sparse dry tufts, not a lawn
                BladeA = Hex("#a89253"), BladeB = Hex("#c4ad6a"), BladeRoot = Hex("#7a6636"),
                BladeHeight = 0.09f, BladeWidth = 0.042f, Sway = 0.055f,
                ShadowTint = Hex("#9a93b8"), CloudAmount = 0.85f,
            };
        }

        static BiomeLook Water()
        {
            return new BiomeLook
            {
                Name = "shallows",
                Base = Hex("#1d4f6b"), Tint2 = Hex("#256b8e"), Tint3 = Hex("#173e57"),
                Highlight = Hex("#a8dced"),
                Waves = 1f, Ripples = 0f, Embers = 0f, MotionSpeed = 0.45f,
                BladeDensity = 0f,                       // open water
                BladeA = Color.white, BladeB = Color.white, BladeRoot = Color.white,
                BladeHeight = 0f, BladeWidth = 0f, Sway = 0f,
                ShadowTint = Hex("#6f86b5"), CloudAmount = 1f,
            };
        }

        static BiomeLook Ash()
        {
            return new BiomeLook
            {
                Name = "scorched",
                Base = Hex("#2e2724"), Tint2 = Hex("#3a312c"), Tint3 = Hex("#241e1c"),
                Highlight = Hex("#ff7a2e"),
                Waves = 0f, Ripples = 0f, Embers = 1f, MotionSpeed = 0.4f,
                BladeDensity = 7f,                       // burnt stalks still standing
                BladeA = Hex("#463a33"), BladeB = Hex("#5c4a3f"), BladeRoot = Hex("#1e1917"),
                BladeHeight = 0.10f, BladeWidth = 0.038f, Sway = 0.06f,
                ShadowTint = Hex("#8a7f8f"), CloudAmount = 0.7f,
            };
        }

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
