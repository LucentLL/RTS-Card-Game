using UnityEngine;

namespace SpawnRowDuel.View.World
{
    /// <summary>
    /// The shape of the ground, as a function rather than a mesh.
    ///
    /// It lives on its own because THREE things have to agree about where the ground is and a
    /// disagreement between any two of them is visible instantly: the terrain mesh is built from
    /// it, every blade of grass is planted on it, and the drifting veils are clipped against it.
    /// A height baked only into the mesh would leave the grass hovering over the troughs.
    ///
    /// Pure, deterministic, no Unity randomness, no per-frame cost - it is evaluated a few hundred
    /// thousand times when a biome is built and never again.
    ///
    /// ## The plateau
    ///
    /// The board is a grid of markings lying at y = 0 with cards flat on it and figures standing
    /// on it, so the ground UNDER the board cannot roll: a dune crest through the centre lane
    /// would put a card inside a hill. Every profile is therefore multiplied by a mask that is
    /// zero over the board's footprint and eases to one outside it. That is not a compromise
    /// forced on the look - it is the composition the reference photograph has anyway, a flat
    /// foreground you stand on with the dunes rolling away from it.
    /// </summary>
    public struct TerrainProfile
    {
        /// <summary>Peak displacement in world units, outside the plateau.</summary>
        public float Amplitude;

        /// <summary>World units per noise cycle for the largest feature. Bigger = broader dunes.</summary>
        public float Wavelength;

        /// <summary>How far the dunes are dragged along the wind - what makes a dune a dune and
        /// not a lump. 0 is blobby hills, 1 is long transverse ridges.</summary>
        public float WindStretch;

        /// <summary>Wind bearing in degrees. Ridges run ACROSS it; streaks run along it.</summary>
        public float WindAngle;

        /// <summary>0 = rounded swells, 1 = sharp wind-carved crests with a slip face.</summary>
        public float Ridge;

        /// <summary>Second-octave detail, as a fraction of Amplitude.</summary>
        public float Detail;

        /// <summary>How far past the board the ground stays flat, in world units.</summary>
        public float PlateauPad;

        /// <summary>How long the ramp from flat to full relief is.</summary>
        public float PlateauFalloff;

        public static TerrainProfile Flat()
        {
            return new TerrainProfile
            {
                Amplitude = 0f, Wavelength = 10f, WindStretch = 0f, WindAngle = 0f,
                Ridge = 0f, Detail = 0f, PlateauPad = 1f, PlateauFalloff = 1f,
            };
        }
    }

    public static class TerrainHeight
    {
        // ── the plateau the board sits on ────────────────────────────────────────────────

        /// <summary>Half-extent of the flat area, set from the board's real footprint.</summary>
        public static Vector2 PlateauHalf = new Vector2(4f, 4f);

        /// <summary>
        /// 0 on the board, 1 out in the dunes. A smoothstep either side of the pad, so the ramp
        /// has no crease at its foot - a linear ramp meeting flat ground leaves a visible seam
        /// exactly where the eye is already looking.
        /// </summary>
        public static float Relief(float x, float z, TerrainProfile p)
        {
            float dx = Mathf.Abs(x) - (PlateauHalf.x + p.PlateauPad);
            float dz = Mathf.Abs(z) - (PlateauHalf.y + p.PlateauPad);
            float d = Mathf.Max(dx, dz);
            if (d <= 0f) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / Mathf.Max(0.001f, p.PlateauFalloff)));
        }

        // ── the height field ─────────────────────────────────────────────────────────────

        public static float At(float x, float z, TerrainProfile p)
        {
            float relief = Relief(x, z, p);
            if (relief <= 0f || p.Amplitude <= 0f) return 0f;

            // Into wind space: u runs along the wind, v across it. Squashing u is what turns
            // round noise into ridges lying broadside to the wind, which is the whole silhouette
            // of a dune field.
            float rad = p.WindAngle * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float u = (x * c + z * s) / Mathf.Max(0.001f, p.Wavelength);
            float v = (-x * s + z * c) / Mathf.Max(0.001f, p.Wavelength);

            float squash = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(p.WindStretch));
            float2 warp = new float2(u * squash, v);

            // A gentle domain warp before the octaves. Without it every crest lies parallel and
            // the field reads as corrugated iron; with it they braid and fork the way real dunes do.
            float wx = Fbm(warp.x * 0.35f + 11.3f, warp.y * 0.35f - 4.1f, 2);
            float wy = Fbm(warp.x * 0.35f - 7.7f, warp.y * 0.35f + 2.9f, 2);
            float warpAmount = 0.55f * Mathf.Clamp01(p.WindStretch);
            float au = warp.x + (wx - 0.5f) * warpAmount;
            float av = warp.y + (wy - 0.5f) * warpAmount;

            float baseN = Fbm(au, av, 4);

            // Ridged: fold the field about its midpoint so the peaks come to an edge. Dunes have
            // a rounded windward back and a sharp brink, and this is the cheap way to get one.
            float ridged = 1f - Mathf.Abs(baseN * 2f - 1f);
            float h = Mathf.Lerp(baseN, ridged * ridged, Mathf.Clamp01(p.Ridge));

            if (p.Detail > 0f)
                h += (Fbm(au * 3.1f + 19.0f, av * 3.1f - 8.0f, 3) - 0.5f) * p.Detail;

            return (h - 0.5f) * 2f * p.Amplitude * relief;
        }

        /// <summary>Central-difference normal. Used for the mesh and for planting things upright.</summary>
        public static Vector3 NormalAt(float x, float z, TerrainProfile p, float eps = 0.18f)
        {
            float hl = At(x - eps, z, p), hr = At(x + eps, z, p);
            float hd = At(x, z - eps, p), hu = At(x, z + eps, p);
            return new Vector3(hl - hr, 2f * eps, hd - hu).normalized;
        }

        // ── noise ────────────────────────────────────────────────────────────────────────
        //
        // Value noise with a smootherstep fade, matching SRD_Noise.hlsl closely enough that the
        // shader's detail layers sit believably on the mesh's shape. They do not have to agree
        // exactly - the mesh carries the silhouette and the shader only adds grain on top of it -
        // but a wildly different character between the two reads as two surfaces.

        struct float2
        {
            public float x, y;
            public float2(float x, float y) { this.x = x; this.y = y; }
        }

        static float Hash(float x, float y)
        {
            float h = x * 127.1f + y * 311.7f;
            h = Mathf.Sin(h) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static float ValueNoise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
            float fx = x - ix, fy = y - iy;

            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

            float a = Hash(ix, iy);
            float b = Hash(ix + 1f, iy);
            float c = Hash(ix, iy + 1f);
            float d = Hash(ix + 1f, iy + 1f);

            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x, y) * amp;
                norm += amp;
                x = x * 2.03f + 3.1f;
                y = y * 2.03f - 1.7f;
                amp *= 0.5f;
            }
            return sum / Mathf.Max(0.0001f, norm);
        }
    }
}
