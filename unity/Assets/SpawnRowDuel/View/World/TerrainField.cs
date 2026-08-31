using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.World
{
    /// <summary>
    /// The ground the duel happens on: an island of terrain under and around the board, a field of
    /// wind-blown blades over it, and cloud shadows drifting across the lot.
    ///
    /// Three meshes, all generated, none of them touched after the biome changes:
    ///
    ///   GROUND  one quad. Colour, patches and the biome's motion are all fragment work.
    ///   BLADES  one mesh of camera-facing quads, positioned once. The wind lives in the vertex
    ///           shader, so a field of two thousand blades costs one draw call and no CPU per frame.
    ///   CLOUDS  a screen-covering quad on the camera, multiplying the finished frame.
    ///
    /// Blades grow EVERYWHERE, the board included. They used to be kept off it, on the reasoning
    /// that scenery must not compete with the game state - but that also meant a card landing on a
    /// centre cell could only bend grass at the rim, which is most of what "the cards should press
    /// down on the grass" was missing. The board is a translucent marking rather than a slab now,
    /// so the cards lie ON the field and the press field is what keeps the surface readable.
    ///
    /// Nothing here has a collider. The board owns picking (BoardInput raycasts the cell boxes) and
    /// a ground plane with a collider under it would swallow every tap that missed a tile.
    /// </summary>
    public sealed class TerrainField : MonoBehaviour
    {
        /// <summary>Which biome the next build uses. Set from the commander screen.</summary>
        public static BiomeId Requested = BiomeId.Grass;

        [Header("Materials (assigned by SceneBootstrap)")]
        public Material TerrainMaterial;
        public Material GrassMaterial;
        public Material CloudMaterial;
        public Material VeilMaterial;
        public Material FallMaterial;

        [Header("Layout")]
        public Vector2 IslandExtent = new Vector2(15f, 11f);  // half-size, world units

        /// <summary>
        /// How far the ground actually reaches. Much further than the island, because at the
        /// tilted angle there is no horizon and no sky in frame - the top of the screen is
        /// DISTANT GROUND, and a field that stops at the island edge stops inside the picture.
        /// The grid bunches toward the middle (TerrainMesh.Bunching), so reaching this far costs
        /// vertices only where they are too small to see anyway.
        /// </summary>
        public float FarExtent = 54f;

        public float EdgeFade = 3.0f;
        public float GroundY = -0.020f;                       // just under the 0.02-thick tile markings
        public int BladeSeed = 20260822;

        [Header("Displacement")]
        // 0.11 world units a texel over the island - fine enough that the rim of displaced
        // material around a card is a curve rather than a staircase.
        public int DispWidth = 320, DispHeight = 240;
        public float UnitPressRadius = 1.15f;   // a card presses the grass it is lying on
        public float UnitPressStrength = 0.95f;

        [Header("Settling")]
        /// <summary>How deep a card sinks into the ground, in world units.</summary>
        public float PressDepth = 0.085f;
        /// <summary>How high the material shoved aside piles up around it.</summary>
        public float BermHeight = 0.055f;
        /// <summary>How far past a card's own footprint the displaced rim reaches.</summary>
        public float BermReach = 0.42f;
        public float GustSpeed = 5f;             // world units per second the ring travels
        public float GustLife = 1.8f;

        MeshRenderer _ground, _blades, _bushes, _veil;
        Material _groundMat, _bladeMat, _bushMat, _cloudMat, _veilMat, _fallMat;
        Mesh _bladeMesh, _bushMesh, _groundMesh;
        BiomeId? _built;
        Camera _cam;
        MatchController _match;

        Texture2D _disp;
        Color32[] _dispPixels;
        Vector2 _dispOrigin, _dispSize;
        int _seenVersion = -1;

        struct GustPulse { public Vector2 At; public float Born, Strength; }
        static readonly GustPulse[] _gusts = new GustPulse[4];
        static int _nextGust;
        readonly Vector4[] _gustUniform = new Vector4[4];

        void Start()
        {
            _cam = Camera.main;
            _match = FindFirstObjectByType<MatchController>();
            BuildDisplacement();
            BuildGround();
            BuildVeil();
            BuildFallQuad();
            BuildCloudQuad();
        }

        void LateUpdate()
        {
            if (!_built.HasValue || _built.Value != Requested) Apply(Requested);

            // The press field only changes when the BOARD does - a few times a turn - so it is
            // repainted off the controller's version stamp rather than every frame.
            if (_match != null && _match.Engine != null && _match.Version != _seenVersion)
            {
                _seenVersion = _match.Version;
                RepaintDisplacement();
            }

            PushGusts();
        }

        // ── gusts ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ring of wind rolling out from a point - what a card landing, a spell going off or a
        /// unit dying does to the grass around it.
        ///
        /// Static because the callers are event handlers that have no reason to know the scenery
        /// exists, and a gust with no terrain in the scene should be a no-op rather than a null.
        /// </summary>
        public static void Gust(Vector3 world, float strength)
        {
            _gusts[_nextGust] = new GustPulse
            {
                At = new Vector2(world.x, world.z),
                Born = Time.time,
                Strength = Mathf.Clamp01(strength),
            };
            _nextGust = (_nextGust + 1) % _gusts.Length;
        }

        void PushGusts()
        {
            if (_bladeMat == null) return;

            for (int i = 0; i < _gusts.Length; i++)
            {
                float age = Time.time - _gusts[i].Born;
                float life = Mathf.Clamp01(1f - age / GustLife);
                // LINEAR decay, not squared. Squared felt right and was wrong: the nearest grass
                // is ~3.8 units from a centre cell, the ring reaches it around 0.76 s, and a
                // squared falloff had already spent three quarters of the gust by then - the ring
                // arrived at 5% and nothing visibly moved.
                _gustUniform[i] = new Vector4(_gusts[i].At.x, _gusts[i].At.y,
                                              age * GustSpeed,
                                              _gusts[i].Strength * life);
            }
            _bladeMat.SetVectorArray("_Gusts", _gustUniform);
            if (_bushMat != null) _bushMat.SetVectorArray("_Gusts", _gustUniform);
            if (_veilMat != null) _veilMat.SetVectorArray("_Gusts", _gustUniform);
        }

        // ── the press field ───────────────────────────────────────────────────────────────

        void BuildDisplacement()
        {
            _dispSize = (IslandExtent + Vector2.one * EdgeFade) * 2f;
            _dispOrigin = -_dispSize * 0.5f;

            _disp = new Texture2D(DispWidth, DispHeight, TextureFormat.RGBA32, false)
            {
                name = "SRD Displacement",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _dispPixels = new Color32[DispWidth * DispHeight];
            RepaintDisplacement();
        }

        /// <summary>
        /// Redraw the press field: a light flattening over the playing surface, and a hard halo
        /// under everything standing on it.
        ///
        /// The board's own press is deliberately WEAK. It is not a slab any more, so it should not
        /// mow the field - it just settles the grass enough that the markings and the cards read
        /// over it. The halo under a unit is what says something heavy is there.
        /// </summary>
        void RepaintDisplacement()
        {
            if (_disp == null) return;
            System.Array.Clear(_dispPixels, 0, _dispPixels.Length);

            const float cell = 1f, gap = 0.08f;
            float pitch = cell + gap;
            float boardX = Board.Columns * pitch * 0.5f;
            float boardZ = (Board.Rows + 2) * pitch * 0.5f;      // +2 for the wall rows

            // The board is MOWN. 0.30 was right when the meadow was 24 thin tufts a square
            // unit and you could see between them; at a real density it buried the tiles and the
            // grid with them. Grass still grows across the board - the cards lie in it - but it
            // lies down hard enough to read the ground through.
            StampRect(boardX, boardZ, 1.4f, 0.80f);

            if (_match != null && _match.Engine != null && _match.Board != null)
            {
                foreach (var kv in _match.Engine.State.Objects())
                {
                    var cre = kv.Value as CreatureUnit;
                    if (cre != null && cre.IsWorker) continue;

                    var at = _match.Board.WorldOf(kv.Key);
                    StampDisc(at, UnitPressRadius, UnitPressStrength);
                    StampBerm(at, UnitPressRadius * 0.62f, UnitPressRadius * 0.62f + BermReach, 1f);
                }
            }

            _disp.SetPixels32(_dispPixels);
            _disp.Apply(false);
        }

        void StampRect(float halfX, float halfZ, float falloff, float strength)
        {
            for (int y = 0; y < DispHeight; y++)
                for (int x = 0; x < DispWidth; x++)
                {
                    Vector2 w = TexelToWorld(x, y);
                    float d = Mathf.Max(Mathf.Abs(w.x) - halfX, Mathf.Abs(w.y) - halfZ);
                    Write(x, y, strength * (1f - Mathf.Clamp01(d / falloff)));
                }
        }

        /// <summary>
        /// The material a card shoved out of the way, as a ring around it.
        ///
        /// Written to G while the hollow goes to R, so one texture fetch in the terrain shader
        /// gets both. The ring is what actually sells "set into the sand" - a plain depression
        /// under an opaque card is invisible, because the card is covering it. What you see is
        /// the pile around the edge.
        /// </summary>
        void StampBerm(Vector3 world, float inner, float outer, float strength)
        {
            var c = new Vector2(world.x, world.z);
            int x0 = WorldToTexelX(c.x - outer), x1 = WorldToTexelX(c.x + outer);
            int y0 = WorldToTexelY(c.y - outer), y1 = WorldToTexelY(c.y + outer);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                {
                    float d = Vector2.Distance(TexelToWorld(x, y), c);
                    if (d < inner || d > outer) continue;

                    // Peaks just outside the card and falls away - material does not pile up in
                    // a wall, it slumps.
                    float u = (d - inner) / Mathf.Max(0.0001f, outer - inner);
                    float ring = Mathf.Sin(u * Mathf.PI);
                    WriteG(x, y, strength * ring * ring);
                }
        }

        void StampDisc(Vector3 world, float radius, float strength)
        {
            var c = new Vector2(world.x, world.z);
            int x0 = WorldToTexelX(c.x - radius), x1 = WorldToTexelX(c.x + radius);
            int y0 = WorldToTexelY(c.y - radius), y1 = WorldToTexelY(c.y + radius);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                {
                    float d = Vector2.Distance(TexelToWorld(x, y), c);
                    float v = strength * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / radius)));
                    Write(x, y, v);
                }
        }

        void Write(int x, int y, float v)
        {
            int i = y * DispWidth + x;
            byte b = (byte)(Mathf.Clamp01(v) * 255f);
            if (b > _dispPixels[i].r) _dispPixels[i].r = b;     // max, so stamps never cancel
        }

        void WriteG(int x, int y, float v)
        {
            int i = y * DispWidth + x;
            byte b = (byte)(Mathf.Clamp01(v) * 255f);
            if (b > _dispPixels[i].g) _dispPixels[i].g = b;
        }

        Vector2 TexelToWorld(int x, int y)
        {
            return new Vector2(_dispOrigin.x + (x + 0.5f) / DispWidth * _dispSize.x,
                               _dispOrigin.y + (y + 0.5f) / DispHeight * _dispSize.y);
        }

        int WorldToTexelX(float wx) { return Mathf.FloorToInt((wx - _dispOrigin.x) / _dispSize.x * DispWidth); }
        int WorldToTexelY(float wz) { return Mathf.FloorToInt((wz - _dispOrigin.y) / _dispSize.y * DispHeight); }

        // ── ground ────────────────────────────────────────────────────────────────────────

        void BuildGround()
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, GroundY, 0f);
            go.AddComponent<MeshFilter>();

            _ground = go.AddComponent<MeshRenderer>();
            _ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ground.receiveShadows = false;      // its sun is baked into the vertices, not sampled
            _groundMat = Instance(TerrainMaterial);
            _ground.sharedMaterial = _groundMat;

            // The flat area the board needs, from the board's own footprint rather than a guess.
            const float cell = 1f, gap = 0.08f;
            float pitch = cell + gap;
            TerrainHeight.PlateauHalf = new Vector2(Rules.Board.Columns * pitch * 0.5f,
                                                    (Rules.Board.Rows + 2) * pitch * 0.5f);
        }

        /// <summary>
        /// Re-cut the ground for a biome. About a tenth of a second, once, when a match starts -
        /// alongside the twenty thousand blades that were always built here.
        /// </summary>
        void RebuildGround(BiomeLook look)
        {
            if (_ground == null) return;

            var built = TerrainMesh.Build(look, IslandExtent, FarExtent, _groundMesh);
            _groundMesh = built.Mesh;
            _ground.GetComponent<MeshFilter>().sharedMesh = _groundMesh;
        }

        // ── blades ────────────────────────────────────────────────────────────────────────

        void BuildBlades(BiomeLook look)
        {
            EnsureLayer(ref _blades, ref _bladeMat, "Blades");
            EnsureLayer(ref _bushes, ref _bushMat, "Bushes");

            _bladeMesh = BuildCover(_blades, _bladeMesh, look, look.BladeDensity,
                                    BladeSeed, 48000);
            _bushMesh = BuildCover(_bushes, _bushMesh, look, look.BushDensity,
                                   BladeSeed ^ 0x5f3759df, 6000);
        }

        void EnsureLayer(ref MeshRenderer mr, ref Material mat, string name)
        {
            if (mr != null) return;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, GroundY, 0f);
            go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mat = Instance(GrassMaterial);
            mr.sharedMaterial = mat;
        }

        /// <summary>
        /// One layer of ground cover: blades, or the sparser and much larger bushes over them.
        ///
        /// Both go through here because they are the same thing at two sizes - the same billboard
        /// quads, the same wind, and crucially the same press field, so a bush flattens under a
        /// card exactly as a blade does. Splitting them into two systems would have meant two
        /// answers to "what happens when something lands on this".
        /// </summary>
        Mesh BuildCover(MeshRenderer target, Mesh reuse, BiomeLook look, float density,
                        int seed, int cap)
        {
            float area = IslandExtent.x * IslandExtent.y * 4f;
            int count = Mathf.Clamp(Mathf.RoundToInt(area * density), 0, cap);
            target.enabled = count > 0;
            if (count == 0) return reuse;

            return FillCover(target, reuse, look, count, seed);
        }

        Mesh FillCover(MeshRenderer target, Mesh reuse, BiomeLook look, int count, int seed)
        {

            // A FIXED seed, not Random: the screenshot probe compares frames across runs, and a
            // field that reshuffles every launch turns every diff into noise.
            var rng = new System.Random(seed);

            var verts = new Vector3[count * 4];
            var corners = new Vector2[count * 4];
            var seeds = new Vector2[count * 4];
            var colors = new Color[count * 4];
            var tris = new int[count * 6];


            int n = 0;
            for (int guard = 0; guard < count * 8 && n < count; guard++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * IslandExtent.x;
                float z = (float)(rng.NextDouble() * 2.0 - 1.0) * IslandExtent.y;

                // Grass grows EVERYWHERE, the board included. It used to be kept off the board so
                // it could not compete with the game state; the board is a translucent marking
                // rather than a slab now, so the cards sit ON the field and the press field is
                // what keeps the playing surface readable. Thin out toward the rim regardless, so
                // the island has a soft edge rather than a mown line.
                float rim = Mathf.Max(Mathf.Abs(x) / IslandExtent.x, Mathf.Abs(z) / IslandExtent.y);
                if (rng.NextDouble() < rim * rim * 0.9) continue;

                float s1 = (float)rng.NextDouble();
                float s2 = (float)rng.NextDouble();

                // 0..1 on purpose: vertex colour is stored as bytes, so anything outside that
                // range clamps and every blade comes out identical. The shader remaps.
                float hScale = (float)rng.NextDouble();
                float wScale = (float)rng.NextDouble();
                float curve = (float)rng.NextDouble();

                int v = n * 4;
                // ON the ground, not on the plane the ground used to be. The height field is the
                // single source both the mesh and this read from, which is the whole reason it
                // is a function and not a mesh - blades hovering over the troughs would be the
                // first thing anyone noticed.
                var foot = new Vector3(x, TerrainHeight.At(x, z, look.Terrain), z);
                verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = foot;

                corners[v] = new Vector2(-0.5f, 0f);
                corners[v + 1] = new Vector2(-0.5f, 1f);
                corners[v + 2] = new Vector2(0.5f, 1f);
                corners[v + 3] = new Vector2(0.5f, 0f);

                var sd = new Vector2(s1, s2);
                seeds[v] = seeds[v + 1] = seeds[v + 2] = seeds[v + 3] = sd;

                var c = new Color(s2, hScale, wScale, curve);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;

                int t = n * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                n++;
            }

            var mesh = reuse;
            if (mesh == null)
            {
                mesh = new Mesh { name = "SRD Cover", hideFlags = HideFlags.DontSave };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = corners;
            mesh.uv2 = seeds;
            mesh.colors = colors;
            mesh.triangles = tris;

            // The blades are billboarded in the VERTEX shader, so their real screen extent is not
            // the mesh's. Without a padded bounds the whole field pops out of view at a glance
            // angle, because Unity culls against geometry that never gets drawn where it says.
            mesh.bounds = new Bounds(Vector3.zero,
                new Vector3(IslandExtent.x * 2f + 4f, 6f + look.Terrain.Amplitude * 4f,
                            IslandExtent.y * 2f + 4f));

            target.GetComponent<MeshFilter>().sharedMesh = mesh;
            return mesh;
        }

        // ── the air ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A stack of large horizontal sheets low over the ground, for whatever is blowing across
        /// it. Horizontal rather than billboarded: the camera looks DOWN at the field, so sheets
        /// lying flat read as material streaming over the surface, where an upright card would
        /// show its edges the moment anything moved.
        ///
        /// One mesh, one draw, no CPU per frame - the drift, the break-up and the specks are all
        /// in the fragment, and a card landing shoves material outward through the same gust ring
        /// the grass already uses.
        /// </summary>
        void BuildVeil()
        {
            const int Layers = 5;

            var go = new GameObject("Veil");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, GroundY, 0f);

            var mesh = new Mesh { name = "SRD Veil", hideFlags = HideFlags.DontSave };
            var verts = new Vector3[Layers * 4];
            var cols = new Color[Layers * 4];
            var tris = new int[Layers * 6];

            float half = FarExtent * 0.62f;
            for (int l = 0; l < Layers; l++)
            {
                float f = l / (float)(Layers - 1);
                float y = 0.10f + f * 1.9f;
                int v = l * 4;

                verts[v] = new Vector3(-half, y, -half);
                verts[v + 1] = new Vector3(-half, y, half);
                verts[v + 2] = new Vector3(half, y, half);
                verts[v + 3] = new Vector3(half, y, -half);

                var c = new Color(f, l * 0.31f % 1f, 0f, 1f);
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;

                int t = l * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }

            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 8f, half * 2f));

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _veil = go.AddComponent<MeshRenderer>();
            _veil.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _veil.receiveShadows = false;
            _veilMat = Instance(VeilMaterial);
            _veil.sharedMaterial = _veilMat;
        }

        /// <summary>
        /// A screen-covering pass for what comes DOWN: snow, and ash falling slower than snow.
        ///
        /// On the camera rather than in the world, because a horizontal sheet cannot show vertical
        /// motion however you scroll it - the veil handles what the wind drags across the ground,
        /// and this handles what the sky drops on it.
        /// </summary>
        void BuildFallQuad()
        {
            if (_cam == null) return;

            var go = new GameObject("Fall");
            go.transform.SetParent(_cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, _cam.nearClipPlane + 0.02f);
            go.transform.localRotation = Quaternion.identity;

            var m = new Mesh { name = "SRD Fall Quad", hideFlags = HideFlags.DontSave };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            go.AddComponent<MeshFilter>().sharedMesh = m;

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _fallMat = Instance(FallMaterial);
            mr.sharedMaterial = _fallMat;
        }

        // ── clouds ────────────────────────────────────────────────────────────────────────

        void BuildCloudQuad()
        {
            if (_cam == null) return;

            var go = new GameObject("CloudShadows");
            go.transform.SetParent(_cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, _cam.nearClipPlane + 0.01f);
            go.transform.localRotation = Quaternion.identity;

            var m = new Mesh { name = "SRD Cloud Quad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            // The vertex shader writes clip space directly and ignores the transform, so the only
            // thing bounds do here is decide whether Unity bothers to draw it. Make them enormous.
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            go.AddComponent<MeshFilter>().sharedMesh = m;

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cloudMat = Instance(CloudMaterial);
            mr.sharedMaterial = _cloudMat;
        }

        // ── biome ─────────────────────────────────────────────────────────────────────────

        void Apply(BiomeId id)
        {
            _built = id;
            var look = Biomes.Of(id);

            RebuildGround(look);

            if (_groundMat != null)
            {
                var sun = TerrainMesh.SunDirection(look);
                _groundMat.SetVector("_SunDir", new Vector4(sun.x, sun.y, sun.z, 0f));
                _groundMat.SetColor("_SunColor", look.SunColor);
                _groundMat.SetColor("_SkyColor", look.SkyColor);
                _groundMat.SetColor("_BounceColor", look.BounceColor);
                _groundMat.SetFloat("_Sheen", look.Sheen);
                _groundMat.SetFloat("_SheenPower", look.SheenPower);
                _groundMat.SetFloat("_ShadowDepth", look.ShadowDepth);

                float windRad = look.Terrain.WindAngle * Mathf.Deg2Rad;
                _groundMat.SetVector("_WindDir",
                    new Vector4(Mathf.Sin(windRad), 0f, Mathf.Cos(windRad), 0f));
                _groundMat.SetFloat("_StreakAmount", look.StreakAmount);
                _groundMat.SetFloat("_StreakScale", look.StreakScale);
                _groundMat.SetFloat("_DetailBump", look.DetailBump);
                _groundMat.SetFloat("_CrestLight", look.CrestLight);
                _groundMat.SetFloat("_TroughShade", look.TroughShade);
                _groundMat.SetFloat("_Sparkle", look.Sparkle);

                _groundMat.SetTexture("_DispTex", _disp);
                _groundMat.SetVector("_DispOrigin", new Vector4(_dispOrigin.x, _dispOrigin.y, 0f, 0f));
                _groundMat.SetVector("_DispSize", new Vector4(_dispSize.x, _dispSize.y, 0f, 0f));
                _groundMat.SetFloat("_PressDepth", PressDepth);
                _groundMat.SetFloat("_BermHeight", BermHeight);

                _groundMat.SetColor("_HazeColor", look.HazeColor);
                _groundMat.SetFloat("_HazeStart", look.HazeStart);
                _groundMat.SetFloat("_HazeDensity", look.HazeDensity);

                _groundMat.SetColor("_BaseColor", look.Base);
                _groundMat.SetColor("_Tint2", look.Tint2);
                _groundMat.SetColor("_Tint3", look.Tint3);
                _groundMat.SetColor("_Highlight", look.Highlight);
                _groundMat.SetFloat("_WaveAmount", look.Waves);
                _groundMat.SetFloat("_RippleAmount", look.Ripples);
                _groundMat.SetFloat("_EmberAmount", look.Embers);
                _groundMat.SetFloat("_MotionSpeed", look.MotionSpeed);
                _groundMat.SetVector("_IslandExtent", new Vector4(IslandExtent.x, IslandExtent.y, 0f, 0f));
                _groundMat.SetFloat("_FadeWidth", EdgeFade);
                _groundMat.SetFloat("_CloudAmount", 0f);   // SRD_CloudShadow casts them, once, for everything
            }

            if (_veilMat != null)
            {
                var sun = TerrainMesh.SunDirection(look);
                float windRad = look.Terrain.WindAngle * Mathf.Deg2Rad;

                _veil.enabled = look.VeilAmount > 0.001f;
                _veilMat.SetColor("_VeilColor", look.VeilColor);
                _veilMat.SetFloat("_Amount", look.VeilAmount);
                _veilMat.SetFloat("_Speed", look.VeilSpeed);
                _veilMat.SetFloat("_Scale", look.VeilScale);
                _veilMat.SetFloat("_Specks", look.Sparkle > 0.01f ? 1.1f : 0.75f);
                _veilMat.SetVector("_WindDir",
                    new Vector4(Mathf.Sin(windRad), 0f, Mathf.Cos(windRad), 0f));
                _veilMat.SetVector("_SunDir", new Vector4(sun.x, sun.y, sun.z, 0f));
                _veilMat.SetColor("_SunColor", look.SunColor);
                _veilMat.SetVector("_BoardHalf",
                    new Vector4(TerrainHeight.PlateauHalf.x, TerrainHeight.PlateauHalf.y, 0f, 0f));
            }

            BuildBlades(look);

            if (_fallMat != null)
            {
                _fallMat.SetColor("_FallColor", look.FallColor);
                _fallMat.SetFloat("_Amount", look.FallAmount);
                _fallMat.SetFloat("_Speed", look.FallSpeed);
                _fallMat.SetFloat("_Drift", look.FallDrift);
                _fallMat.SetFloat("_Size", look.FallSize);
                _fallMat.SetFloat("_Swirl", look.FallSwirl);
            }

            if (_bushMat != null)
            {
                _bushMat.SetTexture("_BladeTex", GrassTextures.Tufts);
                _bushMat.SetFloat("_Variants", GrassTextures.Variants);
                _bushMat.SetTexture("_DispTex", _disp);
                _bushMat.SetVector("_DispOrigin", new Vector4(_dispOrigin.x, _dispOrigin.y, 0f, 0f));
                _bushMat.SetVector("_DispSize", new Vector4(_dispSize.x, _dispSize.y, 0f, 0f));
                _bushMat.SetColor("_ColorA", look.BushA);
                _bushMat.SetColor("_ColorB", look.BushB);
                _bushMat.SetColor("_RootColor", look.BushRoot);
                _bushMat.SetFloat("_Height", look.BushHeight);
                _bushMat.SetFloat("_Width", look.BushWidth);
                _bushMat.SetFloat("_Sway", look.BushSway);
                _bushMat.SetFloat("_CloudAmount", 0f);
            }

            if (_bladeMat != null)
            {
                _bladeMat.SetTexture("_BladeTex", GrassTextures.Tufts);
                _bladeMat.SetFloat("_Variants", GrassTextures.Variants);
                _bladeMat.SetTexture("_DispTex", _disp);
                _bladeMat.SetVector("_DispOrigin", new Vector4(_dispOrigin.x, _dispOrigin.y, 0f, 0f));
                _bladeMat.SetVector("_DispSize", new Vector4(_dispSize.x, _dispSize.y, 0f, 0f));
                _bladeMat.SetColor("_ColorA", look.BladeA);
                _bladeMat.SetColor("_ColorB", look.BladeB);
                _bladeMat.SetColor("_RootColor", look.BladeRoot);
                _bladeMat.SetFloat("_Height", look.BladeHeight);
                _bladeMat.SetFloat("_Width", look.BladeWidth);
                _bladeMat.SetFloat("_Sway", look.Sway);
                _bladeMat.SetFloat("_CloudAmount", 0f);
            }

            if (_cloudMat != null)
            {
                _cloudMat.SetColor("_ShadowTint", look.ShadowTint);
                _cloudMat.SetFloat("_CloudAmount", look.CloudAmount);
                _cloudMat.SetFloat("_GroundY", GroundY);

                // Size is in CLOUD CELLS now (SrdCloudCover), so it reads directly: a cell is
                // 6.5 world units and a lump is about half of one, which puts a cloud at roughly
                // half the board. Speed is cells per second - a cloud crosses the field in about
                // seven, where the first pass took seventeen and read as a shifting gradient.
                _cloudMat.SetFloat("_CloudScale", 6.5f);
                _cloudMat.SetFloat("_CloudSpeed", 0.55f);
            }
        }

        /// <summary>
        /// A per-instance copy of the shared material.
        ///
        /// Not `renderer.material`, which does the same thing but only once the renderer exists and
        /// leaks a copy per access. The ASSET is what the scene serializes - which is what keeps
        /// the shader out of the WebGL stripper's reach - and the copy is what we are allowed to
        /// write biome colours into without dirtying it.
        /// </summary>
        static Material Instance(Material asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("TerrainField is missing a material - re-run SceneBootstrap.Build");
                return null;
            }
            return new Material(asset) { name = asset.name + " (biome)", hideFlags = HideFlags.DontSave };
        }
    }
}
