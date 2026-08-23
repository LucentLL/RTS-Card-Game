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

        [Header("Layout")]
        public Vector2 IslandExtent = new Vector2(15f, 11f);  // half-size, world units
        public float EdgeFade = 3.0f;
        public float GroundY = -0.020f;                       // just under the 0.02-thick tile markings
        public int BladeSeed = 20260822;

        [Header("Displacement")]
        public int DispWidth = 192, DispHeight = 144;
        public float UnitPressRadius = 1.15f;   // a card presses the grass it is lying on
        public float UnitPressStrength = 0.95f;
        public float GustSpeed = 5f;             // world units per second the ring travels
        public float GustLife = 1.8f;

        MeshRenderer _ground, _blades;
        Material _groundMat, _bladeMat, _cloudMat;
        Mesh _bladeMesh;
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

            StampRect(boardX, boardZ, 1.1f, 0.30f);

            if (_match != null && _match.Engine != null && _match.Board != null)
            {
                foreach (var kv in _match.Engine.State.Objects())
                {
                    var cre = kv.Value as CreatureUnit;
                    if (cre != null && cre.IsWorker) continue;
                    StampDisc(_match.Board.WorldOf(kv.Key), UnitPressRadius, UnitPressStrength);
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

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad(IslandExtent.x + EdgeFade, IslandExtent.y + EdgeFade);

            _ground = go.AddComponent<MeshRenderer>();
            _ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ground.receiveShadows = false;
            _groundMat = Instance(TerrainMaterial);
            _ground.sharedMaterial = _groundMat;
        }

        /// <summary>A flat quad in the XZ plane, facing up.</summary>
        static Mesh Quad(float halfX, float halfZ)
        {
            var m = new Mesh { name = "SRD Ground" };
            m.vertices = new[]
            {
                new Vector3(-halfX, 0f, -halfZ), new Vector3(-halfX, 0f, halfZ),
                new Vector3( halfX, 0f,  halfZ), new Vector3( halfX, 0f, -halfZ),
            };
            m.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        // ── blades ────────────────────────────────────────────────────────────────────────

        void BuildBlades(BiomeLook look)
        {
            if (_blades == null)
            {
                var go = new GameObject("Blades");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(0f, GroundY, 0f);
                go.AddComponent<MeshFilter>();
                _blades = go.AddComponent<MeshRenderer>();
                _blades.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _blades.receiveShadows = false;
                _bladeMat = Instance(GrassMaterial);
                _blades.sharedMaterial = _bladeMat;
            }

            float area = IslandExtent.x * IslandExtent.y * 4f;
            int count = Mathf.Clamp(Mathf.RoundToInt(area * look.BladeDensity), 0, 22000);
            _blades.enabled = count > 0;
            if (count == 0) return;

            // A FIXED seed, not Random: the screenshot probe compares frames across runs, and a
            // field that reshuffles every launch turns every diff into noise.
            var rng = new System.Random(BladeSeed);

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
                var foot = new Vector3(x, 0f, z);
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

            if (_bladeMesh == null)
            {
                _bladeMesh = new Mesh { name = "SRD Blades" };
                _bladeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _bladeMesh.Clear();
            _bladeMesh.vertices = verts;
            _bladeMesh.uv = corners;
            _bladeMesh.uv2 = seeds;
            _bladeMesh.colors = colors;
            _bladeMesh.triangles = tris;

            // The blades are billboarded in the VERTEX shader, so their real screen extent is not
            // the mesh's. Without a padded bounds the whole field pops out of view at a glance
            // angle, because Unity culls against geometry that never gets drawn where it says.
            _bladeMesh.bounds = new Bounds(Vector3.zero,
                new Vector3(IslandExtent.x * 2f + 4f, 6f, IslandExtent.y * 2f + 4f));

            _blades.GetComponent<MeshFilter>().sharedMesh = _bladeMesh;
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

            if (_groundMat != null)
            {
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

            BuildBlades(look);

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
