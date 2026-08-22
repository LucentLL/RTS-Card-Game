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
    /// Blades are laid OUTSIDE the board footprint on purpose. Grass growing between the tiles was
    /// the first thing tried and it is charming for about ten seconds, until you cannot tell which
    /// slot a unit is standing in - the board is where the game is read, and scenery does not get
    /// to compete with it.
    ///
    /// Nothing here has a collider. The board owns picking (BoardInput raycasts the cell cubes) and
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
        public float GroundY = -0.062f;                       // the underside of a 0.12 cell cube
        public int BladeSeed = 20260822;

        MeshRenderer _ground, _blades;
        Material _groundMat, _bladeMat, _cloudMat;
        Mesh _bladeMesh;
        BiomeId? _built;
        Camera _cam;

        void Start()
        {
            _cam = Camera.main;
            BuildGround();
            BuildCloudQuad();
        }

        void LateUpdate()
        {
            if (_built.HasValue && _built.Value == Requested) return;
            Apply(Requested);
        }

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
            int count = Mathf.Clamp(Mathf.RoundToInt(area * look.BladeDensity), 0, 16000);
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

            float keepOutX, keepOutZ;
            BoardKeepOut(out keepOutX, out keepOutZ);

            int n = 0;
            for (int guard = 0; guard < count * 8 && n < count; guard++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * IslandExtent.x;
                float z = (float)(rng.NextDouble() * 2.0 - 1.0) * IslandExtent.y;

                // off the board, and thinning out toward the rim so the island has a soft edge
                if (Mathf.Abs(x) < keepOutX && Mathf.Abs(z) < keepOutZ) continue;
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

        /// <summary>The rectangle the board occupies, plus a tile of margin. Read from the RULES
        /// geometry, so a board that grows a column does not have to be remembered about here.</summary>
        void BoardKeepOut(out float x, out float z)
        {
            const float cell = 1f, gap = 0.08f;             // BoardView's defaults
            float pitch = cell + gap;
            x = Board.Columns * pitch * 0.5f + 0.55f;
            z = (Board.Rows + 2) * pitch * 0.5f + 0.35f;    // +2 for the two wall rows
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
