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

        /// <summary>Pin the tide, 0 (fully out) to 1 (fully in). Negative runs it off the clock.
        /// 1 is the water at its highest reach up the beach, which is the way round the word means
        /// it - the shader's own term is a position, and a bigger position is water further away.
        /// For the screenshot probe: a still of a twenty-second cycle taken at the wrong second
        /// shows an empty beach and proves nothing.</summary>
        public static float TideFreeze = -1f;

        [Header("Materials (assigned by SceneBootstrap)")]
        public Material TerrainMaterial;
        public Material GrassMaterial;
        public Material CloudMaterial;
        public Material VeilMaterial;
        public Material FallMaterial;
        public Material SettleMaterial;

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
        public float UnitPressStrength = 0.95f;   // a card presses the grass it is lying on

        [Header("Settling")]
        /// <summary>How deep a card sinks into the ground, in world units.</summary>
        public float PressDepth = 0.085f;
        /// <summary>How high the material shoved aside piles up around it.</summary>
        public float BermHeight = 0.055f;
        /// <summary>How far past a card's own footprint the displaced rim reaches.</summary>
        public float BermReach = 0.42f;
        public float GustSpeed = 5f;             // world units per second the ring travels
        public float GustLife = 1.8f;

        [Header("Weather")]
        /// <summary>How far past the island flakes fall. The ground reaches much further than
        /// this, but distant weather is haze - what has to be full of falling ash is the part of
        /// the field a player is looking at.</summary>
        public float FallMargin = 9f;
        public int FallCap = 3600;

        /// <summary>The accumulation field: how much has settled, per texel, over the same patch
        /// of world the press field covers. Coarser than the press field, because settled ash is
        /// broken up by noise in the shader and never shows a texel edge.</summary>
        public int SettleWidth = 160, SettleHeight = 120;
        /// <summary>Seconds between accumulation ticks. Ash takes half a minute to cover a card;
        /// resolving that at six frames a second is more than the eye can follow.</summary>
        public float SettleTick = 0.16f;
        /// <summary>How far past the board the settled layer reaches before it fades out.</summary>
        public float SettleReach = 5f;

        MeshRenderer _ground, _blades, _bushes, _veil, _fall, _settle;
        Material _groundMat, _bladeMat, _bushMat, _cloudMat, _veilMat, _fallMat, _settleMat;
        Mesh _bladeMesh, _bushMesh, _groundMesh, _fallMesh, _settleMesh;
        BiomeId? _built;
        Camera _cam;
        MatchController _match;

        Texture2D _disp;
        Color32[] _dispPixels;
        Vector2 _dispOrigin, _dispSize;
        int _seenVersion = -1;

        // the accumulation field, and the board occupancy it is wiped by
        Texture2D _settleTex;
        Color32[] _settlePixels;
        float[] _settleLevel;
        int[] _cellOwner, _cellNow;
        float _settleRate, _settleCap = 1f, _settleAt;
        bool _settleDirty;

        /// <summary>A card's own footprint, which is the shape everything the board presses into
        /// the ground now takes. Filled from the board's real cell size.</summary>
        Vector2 _pressHalf = new Vector2(0.44f, 0.64f);
        const float PressRound = 0.16f;

        struct GustPulse { public Vector2 At; public float Born, Strength; }
        static readonly GustPulse[] _gusts = new GustPulse[4];
        static int _nextGust;
        readonly Vector4[] _gustUniform = new Vector4[4];

        void Start()
        {
            _cam = Camera.main;
            _match = FindFirstObjectByType<MatchController>();
            BuildDisplacement();
            BuildSettleField();
            BuildGround();
            BuildVeil();
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
                SyncOccupancy();
                RepaintDisplacement();
            }

            if (_groundMat != null) _groundMat.SetFloat("_TideFreeze", TideFreeze);

            GrowSettle();
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

            SyncBoardShape();
            float boardX = TerrainHeight.PlateauHalf.x;
            float boardZ = TerrainHeight.PlateauHalf.y;

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

                    // A CARD SHAPE, not a disc. What is lying on the ground is a rectangle with
                    // rounded corners, and the hollow it makes and the rim of material it shoves
                    // out both take its outline - the concentric circles around every piece were
                    // the single loudest tell that the ground was a shader and not ground.
                    var at = _match.Board.WorldOf(kv.Key);
                    StampRoundedRect(at, _pressHalf, PressRound, 0.34f, UnitPressStrength);
                    StampRoundedBerm(at, _pressHalf, PressRound, BermReach, 1f);
                }
            }

            _disp.SetPixels32(_dispPixels);
            _disp.Apply(false);
        }

        /// <summary>Signed distance to a rounded rectangle. Negative inside.</summary>
        static float RoundBox(Vector2 p, Vector2 half, float round)
        {
            float dx = Mathf.Abs(p.x) - Mathf.Max(half.x - round, 0f);
            float dy = Mathf.Abs(p.y) - Mathf.Max(half.y - round, 0f);
            float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - round;
        }

        /// <summary>The hollow under a card: full inside its outline, feathering out past it.</summary>
        void StampRoundedRect(Vector3 world, Vector2 half, float round, float feather, float strength)
        {
            var c = new Vector2(world.x, world.z);
            float reach = Mathf.Max(half.x, half.y) + feather;
            int x0 = WorldToTexelX(c.x - reach), x1 = WorldToTexelX(c.x + reach);
            int y0 = WorldToTexelY(c.y - reach), y1 = WorldToTexelY(c.y + reach);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                {
                    float d = RoundBox(TexelToWorld(x, y) - c, half, round);
                    float v = strength * (1f - Mathf.SmoothStep(0f, 1f,
                                          Mathf.Clamp01(d / Mathf.Max(feather, 0.0001f))));
                    Write(x, y, v);
                }
        }

        /// <summary>
        /// The material shoved out of the way, as a rim OUTSIDE the card's outline.
        ///
        /// Written to G while the hollow goes to R, so one texture fetch in the terrain shader
        /// gets both. The rim is what actually sells "set into the ground" - a plain depression
        /// under an opaque card is invisible, because the card is covering it. What you see is
        /// the pile around the edge, and it is card-shaped because the card made it.
        /// </summary>
        void StampRoundedBerm(Vector3 world, Vector2 half, float round, float reach, float strength)
        {
            var c = new Vector2(world.x, world.z);
            float span = Mathf.Max(half.x, half.y) + reach;
            int x0 = WorldToTexelX(c.x - span), x1 = WorldToTexelX(c.x + span);
            int y0 = WorldToTexelY(c.y - span), y1 = WorldToTexelY(c.y + span);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                {
                    float d = RoundBox(TexelToWorld(x, y) - c, half, round);
                    if (d < 0f || d > reach) continue;

                    // Peaks just outside the card and falls away - material does not pile up in
                    // a wall, it slumps.
                    float u = d / Mathf.Max(0.0001f, reach);
                    float ring = Mathf.Sin(u * Mathf.PI);
                    WriteG(x, y, strength * ring * ring);
                }
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

            SyncBoardShape();
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

                // Nothing grows in the sea. On a tidal biome the cover stops short of the water's
                // reach and thins out approaching it - marram is a DUNE plant. This is not a
                // nicety: at this camera angle the far field compresses into a few dozen rows of
                // pixels, so grass planted out there stacks into a WALL, and a wall of grass
                // standing in the surf hid the entire shore behind it.
                if (look.TideAmount > 0.001f)
                {
                    float along = x * look.TideDir.x + z * look.TideDir.y;
                    float dry = look.TideLevel - look.TideRange * 0.55f;
                    if (along > dry) continue;
                    if (rng.NextDouble() < Mathf.Clamp01((along - (dry - 4f)) / 4f)) continue;
                }

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
        /// What comes DOWN, as things IN THE WORLD: one quad per flake, each with a landing point.
        ///
        /// The first version was a screen-covering pass with layers of discs scrolling down it.
        /// The complaint it earned was exact - blocky flakes going diagonally, forever - and both
        /// halves of that are the same mistake. A screen-space disc has no perspective, so a flake
        /// over the far wall is drawn the size of a flake by the camera; and a pass with no ground
        /// in it has nowhere to land, so everything drifts off the bottom of the frame still
        /// falling. Weather that never arrives is a screensaver in front of the board.
        ///
        /// So every flake gets a fixed landing point on the ground here, and the vertex shader
        /// flies it down to that point on its own clock, its own wobble, its own rate. It lands,
        /// lies flat, fades - and the settle layer picks up its coverage from there.
        /// </summary>
        void BuildFallField(BiomeLook look)
        {
            EnsureFall();
            if (_fall == null) return;

            var half = IslandExtent + Vector2.one * FallMargin;
            int count = Mathf.Clamp(Mathf.RoundToInt(half.x * half.y * 4f * look.FallDensity),
                                    0, FallCap);
            _fall.enabled = count > 0 && look.FallAmount > 0.001f;
            if (!_fall.enabled) return;

            // fixed seed, as the blades are: a probe that reshuffles its weather every launch
            // turns every screenshot diff into noise
            var rng = new System.Random(BladeSeed ^ 0x2545f491);

            var verts = new Vector3[count * 4];
            var corners = new Vector2[count * 4];
            var seeds = new Vector2[count * 4];
            var colors = new Color[count * 4];
            var tris = new int[count * 6];

            float boardX = TerrainHeight.PlateauHalf.x, boardZ = TerrainHeight.PlateauHalf.y;

            for (int n = 0; n < count; n++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * half.x;
                float z = (float)(rng.NextDouble() * 2.0 - 1.0) * half.y;

                // ON the ground - the same height field the mesh and the blades use. Over the
                // board it lands on the CARDS instead, a hair above the plates, because that is
                // where the ash is supposed to be piling up.
                float y = TerrainHeight.At(x, z, look.Terrain) + 0.012f;
                if (Mathf.Abs(x) < boardX + 0.4f && Mathf.Abs(z) < boardZ + 0.4f) y = 0.052f;

                int v = n * 4;
                var at = new Vector3(x, y, z);
                verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = at;

                corners[v] = new Vector2(-0.5f, -0.5f);
                corners[v + 1] = new Vector2(-0.5f, 0.5f);
                corners[v + 2] = new Vector2(0.5f, 0.5f);
                corners[v + 3] = new Vector2(0.5f, -0.5f);

                var sd = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                seeds[v] = seeds[v + 1] = seeds[v + 2] = seeds[v + 3] = sd;

                // 0..1 every channel: vertex colour is bytes, and anything outside that range
                // clamps silently and hands every flake the same size
                var c = new Color((float)rng.NextDouble(), (float)rng.NextDouble(),
                                  (float)rng.NextDouble(), (float)rng.NextDouble());
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;

                int t = n * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }

            if (_fallMesh == null)
            {
                _fallMesh = new Mesh { name = "SRD Fall", hideFlags = HideFlags.DontSave };
                _fallMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _fallMesh.Clear();
            _fallMesh.vertices = verts;
            _fallMesh.uv = corners;
            _fallMesh.uv2 = seeds;
            _fallMesh.colors = colors;
            _fallMesh.triangles = tris;

            // The flakes are FLOWN by the vertex shader, so the mesh's own extent is the landing
            // plane and nothing else. Without a bounds tall enough for the whole fall the field
            // pops out of view the moment the camera looks up.
            _fallMesh.bounds = new Bounds(new Vector3(0f, look.FallHeight * 0.5f, 0f),
                new Vector3(half.x * 2f + 6f, look.FallHeight + 4f, half.y * 2f + 6f));

            _fall.GetComponent<MeshFilter>().sharedMesh = _fallMesh;
        }

        void EnsureFall()
        {
            if (_fall != null) return;

            var go = new GameObject("Fall");
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            go.AddComponent<MeshFilter>();

            _fall = go.AddComponent<MeshRenderer>();
            _fall.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fall.receiveShadows = false;
            _fallMat = Instance(FallMaterial);
            _fall.sharedMaterial = _fallMat;
        }

        // ── what has settled ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The accumulation field: how much has landed, per texel, over the same patch of world
        /// the press field covers.
        ///
        /// R is coverage. G is a MASK - the strip of ground each standing figure hides - and it
        /// exists for a sorting reason rather than a weather one. The sheet draws after the cards
        /// so it can lie on them, and the figures are sprites that write no depth, so without the
        /// mask a covered board would have ash painted across every standee's knees. The strip
        /// runs from a piece's own square away from the camera, which is exactly the ground its
        /// billboard is standing in front of, so nothing visible is lost by clearing it.
        /// </summary>
        void BuildSettleField()
        {
            _settleTex = new Texture2D(SettleWidth, SettleHeight, TextureFormat.RGBA32, false)
            {
                name = "SRD Settle",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _settlePixels = new Color32[SettleWidth * SettleHeight];
            _settleLevel = new float[SettleWidth * SettleHeight];
            _settleTex.SetPixels32(_settlePixels);
            _settleTex.Apply(false);
            _settleAt = Time.time;
        }

        /// <summary>
        /// Grow the cover. Off a clock rather than per frame: ash takes half a minute to bury a
        /// card and there is nothing in that to resolve at sixty frames a second.
        /// </summary>
        /// <summary>
        /// Fill the accumulation field, for the screenshot probe.
        ///
        /// Ash takes twenty-five seconds to bury a card, which is the right number for a match and
        /// the wrong one for a test: a probe that has to wait half a minute of game time for the
        /// thing it is photographing is a probe nobody runs. This is the seam that lets it ask for
        /// a covered board directly.
        /// </summary>
        public void PrimeSettle(float level)
        {
            if (_settleLevel == null) return;
            level = Mathf.Min(Mathf.Clamp01(level), _settleCap);
            byte b = (byte)(level * 255f);
            for (int i = 0; i < _settleLevel.Length; i++)
            {
                _settleLevel[i] = level;
                _settlePixels[i].r = b;
            }
            SyncOccupancy();          // the pieces standing on it still wipe their own squares
            _settleDirty = true;
            _settleTex.SetPixels32(_settlePixels);
            _settleTex.Apply(false);
        }

        /// <summary>Clean ground: a new biome does not inherit the last one's weather.</summary>
        void ResetSettle()
        {
            if (_settleLevel == null) return;
            System.Array.Clear(_settleLevel, 0, _settleLevel.Length);
            for (int i = 0; i < _settlePixels.Length; i++) _settlePixels[i].r = 0;
            _settleDirty = true;
            if (_cellOwner != null) System.Array.Clear(_cellOwner, 0, _cellOwner.Length);
        }

        void GrowSettle()
        {
            if (_settleTex == null || _settleLevel == null) return;
            if (Time.time - _settleAt < SettleTick) return;

            float dt = Time.time - _settleAt;
            _settleAt = Time.time;

            if (_settleRate > 0.0001f)
            {
                float step = _settleRate * dt;
                bool any = false;
                for (int i = 0; i < _settleLevel.Length; i++)
                {
                    if (_settleLevel[i] >= _settleCap) continue;
                    _settleLevel[i] = Mathf.Min(_settleCap, _settleLevel[i] + step);
                    _settlePixels[i].r = (byte)(_settleLevel[i] * 255f);
                    any = true;
                }
                _settleDirty |= any;
            }

            if (!_settleDirty) return;
            _settleDirty = false;
            _settleTex.SetPixels32(_settlePixels);
            _settleTex.Apply(false);
        }

        /// <summary>
        /// Wipe the cover under one square, and rebuild the figure mask.
        ///
        /// This is the whole of "until they move": a cell whose occupant changed - arrived, left,
        /// or was replaced - has its patch of the field set back to zero, and the ash starts over.
        /// No animation, because there is nothing to animate: a piece that moves takes its ash
        /// with it and what it uncovers is clean ground.
        /// </summary>
        void SyncOccupancy()
        {
            if (_settleLevel == null || _match == null || _match.Board == null) return;
            SyncBoardShape();

            int cells = Board.Columns * Board.Rows;
            if (_cellOwner == null || _cellOwner.Length != cells)
            {
                _cellOwner = new int[cells];
                _cellNow = new int[cells];
            }

            System.Array.Clear(_cellNow, 0, cells);
            foreach (var kv in _match.Engine.State.Objects())
            {
                int i = kv.Key.Index;
                if (i >= 0 && i < cells && kv.Value != null) _cellNow[i] = kv.Value.Id;
            }

            // G is a pure function of who is standing where, so it is rebuilt rather than patched
            for (int i = 0; i < _settlePixels.Length; i++) _settlePixels[i].g = 0;

            var pad = _pressHalf + new Vector2(0.14f, 0.14f);
            for (int i = 0; i < cells; i++)
            {
                var world = _match.Board.WorldOf(CellRef.FromIndex(i));

                if (_cellNow[i] != _cellOwner[i])
                {
                    _cellOwner[i] = _cellNow[i];
                    WipeSettle(world, pad);
                }

                if (_cellNow[i] != 0) MaskFigure(world);
            }

            _settleDirty = true;
        }

        void WipeSettle(Vector3 world, Vector2 half)
        {
            var c = new Vector2(world.x, world.z);
            float reach = Mathf.Max(half.x, half.y) + 0.2f;
            int x0 = SettleTexelX(c.x - reach), x1 = SettleTexelX(c.x + reach);
            int y0 = SettleTexelY(c.y - reach), y1 = SettleTexelY(c.y + reach);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(SettleHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(SettleWidth - 1, x1); x++)
                {
                    float d = RoundBox(SettleTexelToWorld(x, y) - c, half, PressRound);
                    if (d > 0.2f) continue;

                    int i = y * SettleWidth + x;
                    float keep = Mathf.Clamp01(d / 0.2f);      // a soft rim, not a stencil
                    _settleLevel[i] *= keep;
                    _settlePixels[i].r = (byte)(_settleLevel[i] * 255f);
                }
        }

        /// <summary>The strip of ground a standing billboard covers: its own square, and about a
        /// square and a half of ground behind it.</summary>
        void MaskFigure(Vector3 world)
        {
            var c = new Vector2(world.x, world.z);
            // A figure stands at the FRONT of its tile and rises about a tile's height, so at a
            // 42-degree camera it hides the ground from its own front edge to roughly one unit
            // behind it. Any more than that and the mask starts clearing ash from ground nothing
            // is standing in front of, which reads as a swept rectangle.
            float halfW = _pressHalf.x * 0.72f, reach = 1.05f, feather = 0.3f;

            int x0 = SettleTexelX(c.x - halfW - feather), x1 = SettleTexelX(c.x + halfW + feather);
            int y0 = SettleTexelY(c.y - 0.32f), y1 = SettleTexelY(c.y + reach + feather);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(SettleHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(SettleWidth - 1, x1); x++)
                {
                    var w = SettleTexelToWorld(x, y) - c;
                    float dx = Mathf.Abs(w.x) - halfW;
                    float dz = Mathf.Max(-0.32f - w.y, w.y - reach);
                    float d = Mathf.Max(dx, dz);
                    float v = 1f - Mathf.Clamp01(d / feather);
                    if (v <= 0f) continue;

                    int i = y * SettleWidth + x;
                    byte b = (byte)(Mathf.Clamp01(v) * 255f);
                    if (b > _settlePixels[i].g) _settlePixels[i].g = b;
                }
        }

        Vector2 SettleTexelToWorld(int x, int y)
        {
            return new Vector2(_dispOrigin.x + (x + 0.5f) / SettleWidth * _dispSize.x,
                               _dispOrigin.y + (y + 0.5f) / SettleHeight * _dispSize.y);
        }

        int SettleTexelX(float wx)
        {
            return Mathf.FloorToInt((wx - _dispOrigin.x) / _dispSize.x * SettleWidth);
        }

        int SettleTexelY(float wz)
        {
            return Mathf.FloorToInt((wz - _dispOrigin.y) / _dispSize.y * SettleHeight);
        }

        /// <summary>
        /// The sheet the settled material is drawn on: a grid that FOLLOWS the ground, so the ash
        /// lies in the hollows past the board as flatly as it lies on the tiles.
        ///
        /// A single flat quad was the obvious build and it only works over the plateau; the moment
        /// it reaches the dunes it is either buried or hovering. This costs three thousand
        /// triangles once per biome.
        /// </summary>
        void BuildSettleSheet(BiomeLook look)
        {
            EnsureSettle();
            if (_settle == null) return;

            _settle.enabled = look.SettleRate > 0.0001f;
            if (!_settle.enabled) return;

            var half = TerrainHeight.PlateauHalf + Vector2.one * SettleReach;
            const int N = 56;

            var verts = new Vector3[(N + 1) * (N + 1)];
            var tris = new int[N * N * 6];

            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float x = Mathf.Lerp(-half.x, half.x, i / (float)N);
                    float z = Mathf.Lerp(-half.y, half.y, j / (float)N);
                    verts[j * (N + 1) + i] =
                        new Vector3(x, TerrainHeight.At(x, z, look.Terrain) + 0.075f, z);
                }

            int t2 = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i;
                    tris[t2++] = a; tris[t2++] = a + N + 1; tris[t2++] = a + N + 2;
                    tris[t2++] = a; tris[t2++] = a + N + 2; tris[t2++] = a + 1;
                }

            if (_settleMesh == null)
                _settleMesh = new Mesh { name = "SRD Settled", hideFlags = HideFlags.DontSave };
            _settleMesh.Clear();
            _settleMesh.vertices = verts;
            _settleMesh.triangles = tris;
            _settleMesh.RecalculateBounds();

            _settle.GetComponent<MeshFilter>().sharedMesh = _settleMesh;

            if (_settleMat != null)
            {
                _settleMat.SetVector("_Extent", new Vector4(half.x, half.y, 0f, 0f));
                _settleMat.SetFloat("_Fade", SettleReach * 0.8f);
            }
        }

        void EnsureSettle()
        {
            if (_settle != null) return;

            var go = new GameObject("Settled");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, GroundY, 0f);
            go.AddComponent<MeshFilter>();

            _settle = go.AddComponent<MeshRenderer>();
            _settle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _settle.receiveShadows = false;
            _settleMat = Instance(SettleMaterial);
            _settle.sharedMaterial = _settleMat;
        }

        /// <summary>
        /// The board's real footprint: the flat area the terrain must leave it, and the card
        /// shape everything presses into the ground with.
        ///
        /// The pitch used to be assumed square at 1.08. The rows are STRETCHED by 1.45 - a cell
        /// reads square on screen only because it is deeper than it is wide in the world - so the
        /// plateau was a third too shallow in Z and the back row had the foot of a dune in it.
        /// </summary>
        void SyncBoardShape()
        {
            float col = 1.08f, row = 1.08f * 1.45f, cellW = 1f, cellD = 1.45f;

            var b = _match != null ? _match.Board : null;
            if (b != null)
            {
                col = b.ColPitch; row = b.RowPitch;
                cellW = b.CellSize; cellD = b.CellSize * b.RowStretch;
            }

            TerrainHeight.PlateauHalf = new Vector2(Board.Columns * col * 0.5f,
                                                    (Board.Rows + 2) * row * 0.5f);
            _pressHalf = new Vector2(cellW * 0.44f, cellD * 0.44f);
        }

        /// <summary>The board's cell pitch, for anything that has to line up with the grid.</summary>
        void BoardCell(out float col, out float row)
        {
            var b = _match != null ? _match.Board : null;
            col = b != null ? b.ColPitch : 1.08f;
            row = b != null ? b.RowPitch : 1.08f * 1.45f;
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

            SyncBoardShape();
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

                _groundMat.SetFloat("_TideAmount", look.TideAmount);
                _groundMat.SetVector("_TideDir",
                    new Vector4(look.TideDir.x, 0f, look.TideDir.y, 0f));
                _groundMat.SetFloat("_TideLevel", look.TideLevel);
                _groundMat.SetFloat("_TideRange", look.TideRange);
                _groundMat.SetFloat("_TidePeriod", look.TidePeriod);
                _groundMat.SetFloat("_TideFreeze", TideFreeze);
                _groundMat.SetFloat("_WaveFreq", look.WaveFreq);
                _groundMat.SetFloat("_WaveSpeed", look.WaveSpeed);
                _groundMat.SetColor("_WaterColor", look.WaterColor);
                _groundMat.SetColor("_DeepColor", look.DeepColor);
                _groundMat.SetColor("_FoamColor", look.FoamColor);
                _groundMat.SetVector("_BoardHalf",
                    new Vector4(TerrainHeight.PlateauHalf.x, TerrainHeight.PlateauHalf.y, 0f, 0f));
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
                _veilMat.SetFloat("_Grains", look.GrainAmount);
                _veilMat.SetColor("_GrainColor", look.GrainColor);
                _veilMat.SetVector("_GustHalf",
                    new Vector4(_pressHalf.x, _pressHalf.y, 0f, 0f));
                _veilMat.SetFloat("_GustRound", PressRound);
                _veilMat.SetVector("_WindDir",
                    new Vector4(Mathf.Sin(windRad), 0f, Mathf.Cos(windRad), 0f));
                _veilMat.SetVector("_SunDir", new Vector4(sun.x, sun.y, sun.z, 0f));
                _veilMat.SetColor("_SunColor", look.SunColor);
                _veilMat.SetVector("_BoardHalf",
                    new Vector4(TerrainHeight.PlateauHalf.x, TerrainHeight.PlateauHalf.y, 0f, 0f));
            }

            BuildBlades(look);
            BuildFallField(look);
            BuildSettleSheet(look);

            if (_fallMat != null)
            {
                float windRad = look.Terrain.WindAngle * Mathf.Deg2Rad;
                _fallMat.SetColor("_FallColor", look.FallColor);
                _fallMat.SetFloat("_Amount", look.FallAmount);
                _fallMat.SetFloat("_Speed", look.FallSpeed);
                _fallMat.SetFloat("_Drift", look.FallDrift);
                _fallMat.SetFloat("_Size", look.FallSize);
                _fallMat.SetFloat("_Swirl", look.FallSwirl);
                _fallMat.SetFloat("_Height", look.FallHeight);
                _fallMat.SetVector("_WindDir",
                    new Vector4(Mathf.Sin(windRad), 0f, Mathf.Cos(windRad), 0f));
            }

            // What has already landed. The RATE is kept on this side rather than in the material:
            // it is the CPU that grows the field, and a biome with no accumulation should not be
            // spending a tick a second walking twenty thousand texels that never change.
            _settleRate = look.SettleRate;
            _settleCap = Mathf.Clamp01(look.SettleCap);
            ResetSettle();
            if (_settleMat != null)
            {
                _settleMat.SetTexture("_SettleTex", _settleTex);
                _settleMat.SetVector("_SettleOrigin",
                    new Vector4(_dispOrigin.x, _dispOrigin.y, 0f, 0f));
                _settleMat.SetVector("_SettleSize",
                    new Vector4(_dispSize.x, _dispSize.y, 0f, 0f));
                _settleMat.SetColor("_SettleColor", look.SettleColor);
                _settleMat.SetColor("_ShadeColor", look.SettleShade);
                _settleMat.SetFloat("_Amount", look.SettleMax);
                _settleMat.SetFloat("_Grain", look.SettleGrain);
                _settleMat.SetFloat("_Sparkle", look.SettleSparkle);
                _settleMat.SetVector("_BoardHalf",
                    new Vector4(TerrainHeight.PlateauHalf.x, TerrainHeight.PlateauHalf.y, 0f, 0f));

                float col, row;
                BoardCell(out col, out row);
                _settleMat.SetVector("_CellPitch", new Vector4(col, row, 0f, 0f));
                _settleMat.SetVector("_CellHalf",
                    new Vector4(_pressHalf.x * 1.14f, _pressHalf.y * 1.14f, 0f, 0f));
            }

            if (_bushMat != null)
            {
                _bushMat.SetTexture("_BladeTex", GrassTextures.Tufts);
                _bushMat.SetFloat("_Variants", GrassTextures.Variants);
                _bushMat.SetFloat("_Inset", GrassTextures.Inset);
                _bushMat.SetVector("_GustHalf",
                    new Vector4(_pressHalf.x, _pressHalf.y, 0f, 0f));
                _bushMat.SetFloat("_GustRound", PressRound);
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
                _bladeMat.SetFloat("_Inset", GrassTextures.Inset);
                _bladeMat.SetVector("_GustHalf",
                    new Vector4(_pressHalf.x, _pressHalf.y, 0f, 0f));
                _bladeMat.SetFloat("_GustRound", PressRound);
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
