using UnityEngine;

namespace SpawnRowDuel.View.World
{
    /// <summary>
    /// Builds the ground as real geometry, and bakes its lighting into the vertices.
    ///
    /// ## Why geometry at all
    ///
    /// The ground used to be one flat quad with every bit of its look painted in the fragment
    /// shader. That is the right call for a surface seen from directly above, and the wrong one
    /// for a surface seen at 42°: at a raking angle the thing that says "landscape" is a crest
    /// standing in front of the hollow behind it. Painting cannot do occlusion. So the dunes are
    /// vertices now.
    ///
    /// ## Why the lighting is baked
    ///
    /// The sun does not move and the terrain does not deform, so the expensive part of the look -
    /// long shadows thrown down-sun off every crest - is a constant. It is marched once on the CPU
    /// when the biome is built and stored in the vertex colours, which costs the GPU nothing at
    /// all and gives shadows far longer and softer than a shadow map at this scale would.
    /// Real-time shadows would also have to be paid for on a WebGL target that is already drawing
    /// twenty thousand blades of grass.
    ///
    /// Vertex colour is: R sun exposure, G sky openness, B height above the local floor.
    ///
    /// ## Why the grid is not uniform
    ///
    /// The frame is full of ground - at the tilted angle there is no horizon and no sky, so the
    /// far half of the screen is distant terrain. It therefore has to extend a long way, and a
    /// uniform grid that reaches that far is mostly wasted on cells the size of a pixel. Vertices
    /// are placed on a power curve instead: dense where the board is, spreading out toward the
    /// distance, one mesh and no LOD seam.
    /// </summary>
    public static class TerrainMesh
    {
        /// <summary>Vertices across each axis. 221x221 is ~49k verts / 97k tris - one static draw
        /// call, and fine enough that a dune brink is a curve rather than a staircase.</summary>
        public const int Resolution = 221;

        /// <summary>
        /// Spacing between vertices at the very centre. Everything near the board is at roughly
        /// this size, which is what decides whether a card can press a believable hollow into the
        /// ground: a cell is one world unit, so a card rim needs several vertices across it.
        /// </summary>
        public const float NearSpacing = 0.115f;

        /// <summary>Steps taken along the sun ray when baking shadows.</summary>
        const int ShadowSteps = 26;

        public sealed class Built
        {
            public Mesh Mesh;
            public float[] Heights;      // Resolution^2, row-major (z outer, x inner)
            public float[] Xs, Zs;       // the non-uniform axis positions
        }

        /// <summary>
        /// Generate the ground for one biome. Costs about a tenth of a second and happens when a
        /// match starts, alongside the twenty thousand blades that were always built there.
        /// </summary>
        public static Built Build(BiomeLook look, Vector2 nearExtent, float farExtent, Mesh reuse)
        {
            var p = look.Terrain;
            int n = Resolution;
            int count = n * n;

            var xs = Axis(n, farExtent);
            var zs = xs;

            // ── heights, once ────────────────────────────────────────────────────────────
            // Everything below reads this array rather than the noise, which is what makes the
            // shadow march affordable: the noise is evaluated once per vertex, not once per step.
            var h = new float[count];
            for (int j = 0; j < n; j++)
            {
                float z = zs[j];
                int row = j * n;
                for (int i = 0; i < n; i++) h[row + i] = TerrainHeight.At(xs[i], z, p);
            }

            // ── vertices, normals, colours ───────────────────────────────────────────────
            var verts = new Vector3[count];
            var norms = new Vector3[count];
            var cols = new Color32[count];
            var uvs = new Vector2[count];

            var sun = SunDirection(look);            // points FROM the ground TOWARD the sun
            float sunStep = farExtent * 2f / (n - 1) * 3.0f;

            // The local floor a crest is measured against, for the height term in B.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < count; k++) { if (h[k] < lo) lo = h[k]; if (h[k] > hi) hi = h[k]; }
            float span = Mathf.Max(0.001f, hi - lo);

            for (int j = 0; j < n; j++)
            {
                int row = j * n;
                for (int i = 0; i < n; i++)
                {
                    int k = row + i;
                    float x = xs[i], z = zs[j], y = h[k];

                    verts[k] = new Vector3(x, y, z);
                    uvs[k] = new Vector2(x, z);

                    norms[k] = NormalFrom(h, xs, zs, n, i, j);

                    float sunExposure = SunExposure(h, xs, zs, n, x, z, y, sun, sunStep, farExtent);
                    float sky = SkyOpenness(h, xs, zs, n, i, j, y);
                    float rise = Mathf.Clamp01((y - lo) / span);

                    cols[k] = new Color32((byte)(sunExposure * 255f), (byte)(sky * 255f),
                                          (byte)(rise * 255f), 255);
                }
            }

            // ── triangles ────────────────────────────────────────────────────────────────
            var tris = new int[(n - 1) * (n - 1) * 6];
            int ti = 0;
            for (int j = 0; j < n - 1; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = reuse ?? new Mesh { name = "SRD Ground" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.colors32 = cols;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            return new Built { Mesh = mesh, Heights = h, Xs = xs, Zs = zs };
        }


        /// <summary>
        /// Vertex positions along one axis: fine at the board, growing smoothly outward.
        ///
        /// A power curve was the obvious answer and was wrong in both directions at once - it
        /// piled vertices nine millimetres apart at dead centre, where nothing needs them, and
        /// left three tenths of a unit at the board's edge, where a card has to press a hollow.
        /// Geometric growth fixes both: every step is a fixed RATIO larger than the last, so the
        /// spacing is even where it matters and there is no density jump anywhere to catch the
        /// light differently.
        /// </summary>
        public static float[] Axis(int n, float farExtent)
        {
            int half = (n - 1) / 2;
            float target = farExtent;

            // Solve for the growth ratio that reaches farExtent in `half` steps starting at
            // NearSpacing. Bisection: the sum is monotonic in g, so twenty rounds is exact enough.
            float lo = 1.0f, hi = 1.3f;
            for (int iter = 0; iter < 40; iter++)
            {
                float g = (lo + hi) * 0.5f;
                float sum = Mathf.Abs(g - 1f) < 1e-6f
                    ? NearSpacing * half
                    : NearSpacing * (Mathf.Pow(g, half) - 1f) / (g - 1f);
                if (sum < target) lo = g; else hi = g;
            }
            float ratio = (lo + hi) * 0.5f;

            var axis = new float[n];
            float pos = 0f, step = NearSpacing;
            axis[half] = 0f;
            for (int k = 1; k <= half; k++)
            {
                pos += step;
                step *= ratio;
                axis[half + k] = pos;
                axis[half - k] = -pos;
            }
            return axis;
        }

        /// <summary>Unit vector from the ground toward the sun.</summary>
        public static Vector3 SunDirection(BiomeLook look)
        {
            float a = look.SunAngle * Mathf.Deg2Rad;
            float e = Mathf.Max(2f, look.SunElevation) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e))
                   .normalized;
        }

        // ── the baked terms ──────────────────────────────────────────────────────────────

        /// <summary>
        /// March toward the sun and see whether the ground gets in the way.
        ///
        /// Soft rather than binary: the closest approach of the terrain to the ray decides how
        /// much light is left, so a crest throws a shadow with a penumbra that grows along its
        /// length. A hard in-or-out test gives a shadow with a paper edge, which at this scale
        /// reads as a decal lying on the sand.
        /// </summary>
        static float SunExposure(float[] h, float[] xs, float[] zs, int n,
                                 float x, float z, float y, Vector3 sun, float step, float extent)
        {
            float shade = 0f;
            float rx = x, rz = z, ry = y;

            for (int s = 1; s <= ShadowSteps; s++)
            {
                rx += sun.x * step;
                rz += sun.z * step;
                ry += sun.y * step;

                if (Mathf.Abs(rx) > extent || Mathf.Abs(rz) > extent) break;

                float ground = Sample(h, xs, zs, n, rx, rz);
                float over = ground - ry;
                if (over <= 0f) continue;

                // Nearer blockers cast harder. Dividing by the distance travelled is what turns
                // the march into a soft shadow instead of a stack of hard ones.
                float dist = s * step;
                shade = Mathf.Max(shade, Mathf.Clamp01(over / (0.35f + dist * 0.16f)));
                if (shade >= 1f) break;
            }
            return Mathf.Clamp01(1f - shade);
        }

        /// <summary>
        /// How much sky a point can see, from how much of its neighbourhood stands above it.
        /// Cheap, and enough: it darkens hollows and the feet of slip faces, which is where the
        /// eye expects contact shade.
        /// </summary>
        static float SkyOpenness(float[] h, float[] xs, float[] zs, int n, int i, int j, float y)
        {
            float occl = 0f;
            int taps = 0;
            for (int r = 2; r <= 8; r += 3)
            {
                for (int d = 0; d < 4; d++)
                {
                    int si = i + (d == 0 ? r : d == 1 ? -r : 0);
                    int sj = j + (d == 2 ? r : d == 3 ? -r : 0);
                    if (si < 0 || sj < 0 || si >= n || sj >= n) continue;

                    float dx = xs[si] - xs[i], dz = zs[sj] - zs[j];
                    float horiz = Mathf.Sqrt(dx * dx + dz * dz);
                    if (horiz < 0.0001f) continue;

                    float rise = h[sj * n + si] - y;
                    occl += Mathf.Clamp01(rise / horiz);
                    taps++;
                }
            }
            if (taps == 0) return 1f;
            return Mathf.Clamp01(1f - occl / taps * 1.35f);
        }

        static Vector3 NormalFrom(float[] h, float[] xs, float[] zs, int n, int i, int j)
        {
            int il = Mathf.Max(0, i - 1), ir = Mathf.Min(n - 1, i + 1);
            int jd = Mathf.Max(0, j - 1), ju = Mathf.Min(n - 1, j + 1);

            float dx = Mathf.Max(0.0001f, xs[ir] - xs[il]);
            float dz = Mathf.Max(0.0001f, zs[ju] - zs[jd]);

            float hl = h[j * n + il], hr = h[j * n + ir];
            float hd = h[jd * n + i], hu = h[ju * n + i];

            return new Vector3(-(hr - hl) / dx, 2f, -(hu - hd) / dz).normalized;
        }

        /// <summary>Bilinear sample of the height array at a world position, honouring the
        /// non-uniform axes.</summary>
        static float Sample(float[] h, float[] xs, float[] zs, int n, float x, float z)
        {
            int i = IndexOf(xs, n, x), j = IndexOf(zs, n, z);
            int i2 = Mathf.Min(n - 1, i + 1), j2 = Mathf.Min(n - 1, j + 1);

            float tx = xs[i2] > xs[i] ? (x - xs[i]) / (xs[i2] - xs[i]) : 0f;
            float tz = zs[j2] > zs[j] ? (z - zs[j]) / (zs[j2] - zs[j]) : 0f;
            tx = Mathf.Clamp01(tx); tz = Mathf.Clamp01(tz);

            float a = h[j * n + i], b = h[j * n + i2];
            float c = h[j2 * n + i], d = h[j2 * n + i2];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        /// <summary>Binary search the non-uniform axis. It is monotonic, so this is exact.</summary>
        static int IndexOf(float[] axis, int n, float v)
        {
            if (v <= axis[0]) return 0;
            if (v >= axis[n - 1]) return n - 1;

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (axis[mid] <= v) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
