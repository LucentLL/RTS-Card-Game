using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
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
        // 0.11 world units a texel over the island. That used to have to describe the rim of
        // displaced material around a card and it never could: a card covers its whole tile face,
        // so the ground it leaves showing is the tile's own 0.040 gap - a third of a texel. The
        // rim is drawn from the board's PITCH now (SRD_Terrain), so all this field carries is how
        // flat the grass lies (R) and which squares have a piece on them (G).
        public int DispWidth = 320, DispHeight = 240;
        public float UnitPressStrength = 0.95f;   // a card presses the grass it is lying on

        /// <summary>
        /// How long a crushed square takes to stand back up, in seconds.
        ///
        /// There is no MOW any more. The board used to stamp 0.80 across its whole plateau, which
        /// cut every blade over the playing surface to a third of its height and left a visible
        /// rectangle of lawn in the middle of a meadow. The field is one field now: the grass on
        /// the board is the same grass as the grass beside it, and the only thing that flattens it
        /// is a CARD lying on it.
        ///
        /// Which means it has to come back, and slowly - grass that springs up the instant a unit
        /// steps off is grass on a hinge. Nearly two minutes, so a square a creature left three
        /// turns ago is still visibly trodden and a square it left ten turns ago is not.
        /// </summary>
        public float GrassRegrow = 110f;

        [Header("Settling")]
        /// <summary>How proud of the ground the rim around a card stands. Shading, not geometry -
        /// see the impression block in SRD_Terrain for why the EDGE cannot be geometry.</summary>
        public float RimRelief = 0.11f;
        /// <summary>How far out of the card's own outline that rim reaches, in X units.
        ///
        /// The shader CAPS this at the tile's own half-gap whatever it is set to, because only the
        /// nearest cell is evaluated and a rim wider than the gap is cut off mid-slope at the
        /// halfway line - a hard seam down every tile boundary. So width is not the knob that
        /// answers "is the ground displaced at all?": the DISH under it, the crushed grass on it
        /// and the contact shade in the crack are.</summary>
        public float RimReach = 0.04f;

        /// <summary>
        /// How deep the DISH under a card sinks the ground, in world units. This one IS geometry.
        ///
        /// The card's crisp outline cannot be - vertices near the board are 0.19 units apart and a
        /// 1.00-wide card is five of them across, so its edge snapped 0.02 to 0.06 off the card, a
        /// different amount in every column. A BROAD SOFT DISH is a different question: half a unit
        /// across is three or four vertices, which the grid carries without a stagger, and at a
        /// 42-degree camera real relief is what says the ground gave way rather than got painted.
        /// So the two halves are split - the dish moves vertices, the rim is shaded on top of it.
        /// </summary>
        public float PressDepth = 0.075f;
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
        /// <summary>Seconds between accumulation ticks. Coverage arrives a ply at a time and eases
        /// on over about a second; resolving that at six frames a second is all the eye can follow.</summary>
        public float SettleTick = 0.16f;
        /// <summary>How fast a ply's worth of coverage eases on, in cover per second. Fast enough
        /// that a ply has landed before the next one is billed - the AI beats a command every
        /// 0.35s - and slow enough that it arrives as weather rather than as a lighting change.</summary>
        public float SettleEase = 0.12f;
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
        bool[] _cellStands, _cellIsStruct;

        // how crushed each square is, 1 under a card and easing back to 0 once it leaves
        float[] _pressLevel;
        float _pressAt;
        bool _pressDirty = true;
        float _settleRate, _settleCap = 1f, _settleAt;
        float _settleOwed;            // coverage billed by a ply and not yet eased on
        int _settleTurn = -1;
        bool _settleDirty;

        /// <summary>A card's own footprint, which is the shape everything the board presses into
        /// the ground now takes. Filled from the board's real cell size.</summary>
        Vector2 _pressHalf = new Vector2(0.5f, 0.725f);

        /// <summary>The corner radius the impression is drawn with. The card FRAME starts with a
        /// filled rectangle and has square corners, so 0.16 was rounding a shape that is not
        /// round - and offset outward by the rim it came out at an effective 0.37.</summary>
        public const float CardRound = 0.06f;

        /// <summary>What the press stamp rounds ITS corners by. Unchanged: R is read by the grass,
        /// and a blade does not care about a tenth of a corner.</summary>
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
                _pressDirty = true;
            }

            // The press field changes on its own now - grass rises back out of a square a card has
            // left - so it is repainted off a clock as well as off the board's version stamp. Five
            // times a second: a hundred-second regrowth resolved any finer is work nobody can see.
            if (StepPress()) _pressDirty = true;
            if (_pressDirty && Time.time - _pressAt >= 0.2f)
            {
                _pressAt = Time.time;
                _pressDirty = false;
                RepaintDisplacement();
            }

            if (_groundMat != null) _groundMat.SetFloat("_TideFreeze", TideFreeze);

            BillSettle();
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

            // NO BOARD-WIDE MOW. It used to stamp 0.80 across the whole plateau, and that is a
            // lawn: every blade over the playing surface cut to a third of its height, in a
            // rectangle, in the middle of a meadow. The one field runs straight under the board
            // now and the only thing that flattens it is a card lying on it.
            //
            // THE UPLOAD IS UNCONDITIONAL. It used to return here when there was no match, having
            // cleared the CPU array and left the GPU texture alone - and a freshly created
            // Texture2D holds UNINITIALISED memory, so with no match the shader was reading
            // garbage out of G. G is the flag that says "a card is lying on this cell", read at
            // each cell centre and drawn from _CellPitch, so garbage in it stamps a card-shaped
            // impression on cell after cell, to the horizon, over hills no board has ever been
            // near. That is the lattice of tiles on the title screen: not a pattern anybody drew,
            // just whatever was in that memory.
            bool playing = _match != null && _match.Board != null && _pressLevel != null;
            for (int i = 0; playing && i < _pressLevel.Length; i++)
            {
                float level = _pressLevel[i];
                if (level <= 0.004f) continue;
                var at = _match.Board.WorldOf(CellRef.FromIndex(i));

                // A CARD SHAPE, not a disc. R is what a blade reads to know how flat to lie, and
                // 0.10 past the outline is the tile's own gap: beyond that a card has no business
                // flattening anything. The rim of shoved-out material is not stamped here at all
                // any more, because a texel is 0.11 units and the whole rim is 0.036 wide.
                StampRoundedRect(at, _pressHalf, PressRound, 0.10f, UnitPressStrength * level);

                // ...and a BROAD SOFT DISH into B, which is the half that moves vertices. Feathered
                // over 0.55 of a unit so it spans four or five of the ground's 0.19-unit vertices
                // and cannot show their staircase - the crisp edge is the rim's job, in shading.
                StampRoundedDish(at, _pressHalf * 0.55f, 0.55f, level);

                // ...and G says only THAT SOMETHING IS HERE. The fragment reads it at the cell
                // centre, where a whole card's worth of texels agree, and draws the impression
                // itself from the pitch - so the shape is the card's, exactly, at pixel
                // resolution, which no 0.11-unit texel and no 0.19-unit vertex could give it.
                // Scaled by the same level, so the dent fades out as the grass stands back up.
                StampCellFlag(at, level);
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
        /// "There is a piece on this square", as a solid patch of G around the cell centre.
        ///
        /// Deliberately CRUDE - it is a flag, not a shape. The shader rounds a world position to
        /// the nearest cell centre and samples G there, so the only thing that has to be true is
        /// that the texels around a centre all read 1; the impression's outline is arithmetic off
        /// the pitch and owes this texture nothing. Half a card is stamped rather than a single
        /// texel so that bilinear filtering cannot soften the flag at a cell centre.
        ///
        /// What used to be here was the rim of shoved-out material itself, and that is the change:
        /// a texel is 0.11 units and the whole rim is 0.036 wide, so this texture never had the
        /// resolution to draw it, and the ground mesh - 0.19 between vertices - had less.
        /// </summary>
        void StampCellFlag(Vector3 world, float level)
        {
            var c = new Vector2(world.x, world.z);
            var half = _pressHalf * 0.5f;
            int x0 = WorldToTexelX(c.x - half.x), x1 = WorldToTexelX(c.x + half.x);
            int y0 = WorldToTexelY(c.y - half.y), y1 = WorldToTexelY(c.y + half.y);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                    WriteG(x, y, level);
        }

        /// <summary>
        /// Crush what is stood on and let the rest come back up. Returns whether anything moved.
        ///
        /// The crush is INSTANT and the recovery is not, which is the asymmetry the effect lives
        /// on: a card lands with a thump, and the square it lands on is flat before the animation
        /// has finished. What takes time is the grass standing up again after it leaves - and it
        /// has to, because grass that springs back the frame a unit steps off is grass on a hinge.
        /// </summary>
        bool StepPress()
        {
            if (_match == null || _match.Board == null || _cellNow == null) return false;

            if (_pressLevel == null || _pressLevel.Length != _cellNow.Length)
            {
                _pressLevel = new float[_cellNow.Length];
                return true;
            }

            float back = Time.deltaTime / Mathf.Max(1f, GrassRegrow);
            bool moved = false;
            for (int i = 0; i < _pressLevel.Length; i++)
            {
                if (_cellNow[i] != 0)
                {
                    if (_pressLevel[i] < 1f) { _pressLevel[i] = 1f; moved = true; }
                }
                else if (_pressLevel[i] > 0f)
                {
                    _pressLevel[i] = Mathf.Max(0f, _pressLevel[i] - back);
                    moved = true;
                }
            }
            return moved;
        }

        /// <summary>The dish, into B: broad, soft and centred on the card, for the vertex shader.</summary>
        void StampRoundedDish(Vector3 world, Vector2 half, float feather, float strength)
        {
            var c = new Vector2(world.x, world.z);
            float reach = Mathf.Max(half.x, half.y) + feather;
            int x0 = WorldToTexelX(c.x - reach), x1 = WorldToTexelX(c.x + reach);
            int y0 = WorldToTexelY(c.y - reach), y1 = WorldToTexelY(c.y + reach);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(DispHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(DispWidth - 1, x1); x++)
                {
                    float d = RoundBox(TexelToWorld(x, y) - c, half, PressRound);
                    float v = strength * (1f - Mathf.SmoothStep(0f, 1f,
                                          Mathf.Clamp01(d / Mathf.Max(feather, 0.0001f))));
                    WriteB(x, y, v);
                }
        }

        void WriteB(int x, int y, float v)
        {
            int i = y * DispWidth + x;
            byte b = (byte)(Mathf.Clamp01(v) * 255f);
            if (b > _dispPixels[i].b) _dispPixels[i].b = b;
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
            _veil.sortingOrder = 45;      // the air is in front of everything it blows across
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
            _fall.sortingOrder = 40;      // in front of the settled sheet, as falling is of fallen
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
        /// Bill a ply's worth of coverage, once, when the turn number moves.
        ///
        /// The rate used to be per WALL-CLOCK SECOND, and the doc comments claiming half a minute
        /// to bury a card were out by two: 0.055 a second against a cap of 0.72 is thirteen
        /// seconds to full and under nine to half cover, so the board went from clean to buried
        /// inside about one round - and, worse, it went on thickening while a player sat looking
        /// at their hand. Everything else in this system is turn-shaped ("until they move", the
        /// wipe on an occupancy change); the clock was the odd one out, and it was the complaint.
        ///
        /// A ply at a time puts it back where it belongs. Cover starts showing around the fourth
        /// ply and caps around the tenth, and nothing changes while nobody is playing.
        /// </summary>
        void BillSettle()
        {
            if (_settleRate <= 0.0001f || _match == null || _match.Engine == null) return;

            int turn = _match.Engine.State.TurnNumber;
            if (turn == _settleTurn) return;

            // One ply's worth however far the number jumped - a rematch resets it to 1 and a
            // backwards step must not bank a negative debt.
            _settleTurn = turn;
            _settleOwed = Mathf.Min(_settleOwed + _settleRate, _settleRate * 2f);
        }

        /// <summary>
        /// Ease the billed coverage on. Off a clock rather than per frame: what a ply buys is
        /// about a fiftieth of the visible band, and there is nothing in that to resolve at sixty
        /// frames a second.
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
            // OCCUPANCY FIRST, then the fill. The other way round - which is how this read - has
            // SyncOccupancy see every piece as newly arrived (the ownership array is empty until
            // it runs once) and wipe the square each one is standing on, so the probe photographed
            // a fully settled field with a clean rectangle under every card. Priming means "this
            // has been falling for a while", and a card that has been sitting there the whole time
            // is a card it has been falling ON.
            SyncOccupancy();

            level = Mathf.Min(Mathf.Clamp01(level), _settleCap);
            byte b = (byte)(level * 255f);
            for (int i = 0; i < _settleLevel.Length; i++)
            {
                _settleLevel[i] = level;
                _settlePixels[i].r = b;
            }
            _settleDirty = true;
            _settleTex.SetPixels32(_settlePixels);
            _settleTex.Apply(false);
        }

        /// <summary>Clean ground: a new biome does not inherit the last one's weather.</summary>
        void ResetSettle()
        {
            if (_settleLevel == null) return;
            System.Array.Clear(_settleLevel, 0, _settleLevel.Length);
            _settleOwed = 0f;
            _settleTurn = -1;
            for (int i = 0; i < _settlePixels.Length; i++) _settlePixels[i].r = 0;
            _settleDirty = true;
            if (_cellOwner != null) System.Array.Clear(_cellOwner, 0, _cellOwner.Length);
        }

        void GrowSettle()
        {
            if (_settleTex == null || _settleLevel == null) return;
            if (Time.time - _settleAt < SettleTick) return;

            // dt is CLAMPED. A biome change rebuilds the ground, forty-eight thousand blades and
            // three thousand flake quads in one frame, and a WebGL hitch is longer still; without
            // this the first tick after one of those dumps a whole ply in a single frame, which is
            // the exact thing the easing exists to prevent.
            float dt = Mathf.Min(Time.time - _settleAt, 0.25f);
            _settleAt = Time.time;

            if (_settleRate > 0.0001f && _settleOwed > 0.0001f)
            {
                float step = Mathf.Min(_settleOwed, SettleEase * dt);
                _settleOwed -= step;
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
                _cellStands = new bool[cells];
                _cellIsStruct = new bool[cells];
            }

            var state = _match.Engine.State;
            System.Array.Clear(_cellNow, 0, cells);
            System.Array.Clear(_cellStands, 0, cells);
            foreach (var kv in state.Objects())
            {
                int i = kv.Key.Index;
                if (i < 0 || i >= cells || kv.Value == null) continue;
                _cellNow[i] = kv.Value.Id;
                _cellIsStruct[i] = kv.Value is StructureUnit;
                _cellStands[i] = StandsUp(kv.Value, kv.Key, state);
            }

            // G is a pure function of who is standing where, so it is rebuilt rather than patched
            for (int i = 0; i < _settlePixels.Length; i++) _settlePixels[i].g = 0;

            // The card, plus the tile's own gap. It used to be 0.14 on a _pressHalf that was 88%
            // of a card, which wiped 1.28 x 1.73 of clean ground around a 1.00 x 1.45 card - the
            // ground's own complaint, in ash. 0.06 and no less, though: the sheet draws a hair
            // above the card and at this angle that height throws about 0.03 of ash forward.
            var pad = _pressHalf + new Vector2(0.06f, 0.06f);
            for (int i = 0; i < cells; i++)
            {
                var world = _match.Board.WorldOf(CellRef.FromIndex(i));

                if (_cellNow[i] != _cellOwner[i])
                {
                    _cellOwner[i] = _cellNow[i];
                    WipeSettle(world, pad);
                }

                if (_cellStands[i]) MaskFigure(world, _cellIsStruct[i]);
            }

            _settleDirty = true;
        }

        void WipeSettle(Vector3 world, Vector2 half)
        {
            // 0.08 of rim, not 0.2. What actually sets the size of the bare patch a new card
            // leaves is this ramp rather than the pad above it, and at 0.2 the clean rectangle
            // came out 1.52 x 1.97 around a 1.00 x 1.45 card - the ground's own complaint again,
            // in ash. Still a ramp and not a stencil: an edge this sharp on a field broken up by
            // noise would be the one straight line on the board.
            const float Rim = 0.08f;

            var c = new Vector2(world.x, world.z);
            float reach = Mathf.Max(half.x, half.y) + Rim;
            int x0 = SettleTexelX(c.x - reach), x1 = SettleTexelX(c.x + reach);
            int y0 = SettleTexelY(c.y - reach), y1 = SettleTexelY(c.y + reach);

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(SettleHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(SettleWidth - 1, x1); x++)
                {
                    float d = RoundBox(SettleTexelToWorld(x, y) - c, half, PressRound);
                    if (d > Rim) continue;

                    int i = y * SettleWidth + x;
                    float keep = Mathf.Clamp01(d / Rim);
                    _settleLevel[i] *= keep;
                    _settlePixels[i].r = (byte)(_settleLevel[i] * 255f);
                }
        }

        /// <summary>
        /// Whether a FIGURE is actually drawn standing up on this square.
        ///
        /// The mask below exists for one reason - a standee is a sprite that writes no depth, so
        /// ash drawn after it would be painted across its knees - and it was being stamped for
        /// every occupied cell instead, whatever was on it. That is the whole of "the snow does
        /// not accumulate on the cards": a face-down charge has no figure at all (StandeeLayer
        /// skips anything that is not a creature or a structure, and bails again when the card has
        /// no cut-out), and a LAID creature - which is every creature on the turn it arrives and
        /// every attacker after it swings - lies flat on its own card and hides nothing behind it.
        /// All three were masking a strip of ground nothing was standing in front of.
        /// </summary>
        bool StandsUp(BoardObject o, CellRef at, GameState s)
        {
            if (!StandeeLayer.Enabled) return false;
            if (!(o is CreatureUnit) && !(o is StructureUnit)) return false;
            var cre = o as CreatureUnit;
            if (cre != null && cre.IsWorker) return false;

            var def = _match.DefOfObject(o);
            if (def == null || !def.HasFieldArt) return false;

            return cre == null || StandeeLayer.CanActNow(cre, at, s);
        }

        /// <summary>The strip of ground a standing billboard covers: from its own FEET back, which
        /// is not the same as from its own square back.</summary>
        void MaskFigure(Vector3 world, bool structure)
        {
            var c = new Vector2(world.x, world.z);

            // A figure is planted at the FRONT of its tile (StandeeLayer.FeetShift) and stands
            // about a tile-and-a-half tall, so at 42 degrees it hides 1.6 units of ground BEHIND
            // its feet - and none at all in front of them. The mask used to run from 0.32 in front
            // of the CELL CENTRE, which is 0.25 forward of the feet and covers the one strip of an
            // occupied card the figure does not already hide: the stats band at its near edge.
            // That strip is where ash on a card is actually seen, so the front edge is hard now.
            //
            // "Front" and "behind" are the SEAT's, not the world's. Away is the direction the
            // figure leans in, which is away from whoever is looking at it, so both the near edge
            // and the reach turn round with the camera for the guest - otherwise the guest's ash
            // is masked off the grass in front of every figure and drawn straight over the card
            // the figure is standing on.
            float away = -Seat.TowardCamera;
            float front = -StandeeLayer.FeetOffset(_match.Board, structure) * away;
            const float Reach = 1.62f, Feather = 0.22f, Toe = 0.06f;
            float halfW = _pressHalf.x * 0.84f;

            float near = c.y + front, far = c.y + front + (Reach + Feather) * away;
            int x0 = SettleTexelX(c.x - halfW - Feather), x1 = SettleTexelX(c.x + halfW + Feather);
            int y0 = SettleTexelY(Mathf.Min(near, far)), y1 = SettleTexelY(Mathf.Max(near, far));

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(SettleHeight - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(SettleWidth - 1, x1); x++)
                {
                    var w = SettleTexelToWorld(x, y) - c;
                    float depth = (w.y - front) * away;          // how far BEHIND the feet, always positive
                    float side = 1f - Mathf.Clamp01((Mathf.Abs(w.x) - halfW) / Feather);
                    float back = 1f - Mathf.Clamp01((depth - Reach) / Feather);
                    float ahead = Mathf.Clamp01(depth / Toe);
                    float v = side * back * ahead;
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
                    // 0.055 over the ground, so on the plateau the sheet lies at y 0.035 - five
                    // thousandths over the card plate at 0.030. It was 0.075, and at 42 degrees
                    // that extra height threw the ash a full 0.03 forward of whatever it was
                    // supposed to be lying on.
                    verts[j * (N + 1) + i] =
                        new Vector3(x, TerrainHeight.At(x, z, look.Terrain) + 0.055f, z);
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

            // SORTING ORDER, not the render queue, and this is the whole reason ash never lay on a
            // card. The material is Transparent+10 and the card plates are Sprites/Default at
            // 3000, so on queue alone the sheet draws second and wins - but a renderer's sorting
            // layer and order are compared BEFORE its queue, and every plate sprite carries an
            // explicit order (4 to 16) while a bare MeshRenderer carries 0. The sheet was sorting
            // underneath every card on the board and the queue never got a say.
            //
            // 30 puts it over the plates and over the standees (20) as well, which is intended:
            // the figures are handled by the G mask, which exists precisely because they write no
            // depth for the sheet to test against.
            _settle.sortingOrder = 30;
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
            // THE CARD, exactly. CardPlateLayer.Fill is 1, so a card covers its whole tile face -
            // 0.44 was drawing every impression, gust ring and ash wipe at 88% of the thing that
            // made it, and then the settle sheet multiplied it back up by 1.14 to compensate.
            _pressHalf = new Vector2(cellW * 0.5f, cellD * 0.5f);
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
                _groundMat.SetFloat("_GustSwing", look.GustSwing);
                _groundMat.SetFloat("_GustPeriod", look.GustPeriod);

                _groundMat.SetTexture("_DispTex", _disp);
                _groundMat.SetVector("_DispOrigin", new Vector4(_dispOrigin.x, _dispOrigin.y, 0f, 0f));
                _groundMat.SetVector("_DispSize", new Vector4(_dispSize.x, _dispSize.y, 0f, 0f));

                // The impression is drawn from the board's own geometry, so the shader is handed
                // the pitch and the card's real half-size rather than anything measured off the
                // press texture.
                float impCol, impRow;
                BoardCell(out impCol, out impRow);
                _groundMat.SetVector("_CellPitch", new Vector4(impCol, impRow, 0f, 0f));
                _groundMat.SetVector("_CellHalf", new Vector4(_pressHalf.x, _pressHalf.y, 0f, 0f));
                _groundMat.SetFloat("_CardRound", CardRound);
                _groundMat.SetFloat("_PressDepth", PressDepth);
                _groundMat.SetFloat("_RimReach", RimReach);
                _groundMat.SetFloat("_RimRelief", RimRelief);

                _groundMat.SetColor("_HazeColor", look.HazeColor);
                _groundMat.SetFloat("_HazeStart", look.HazeStart);
                _groundMat.SetFloat("_HazeDensity", look.HazeDensity);

                _groundMat.SetColor("_BaseColor", look.Base);
                _groundMat.SetColor("_Tint2", look.Tint2);
                _groundMat.SetColor("_Tint3", look.Tint3);
                _groundMat.SetColor("_Highlight", look.Highlight);
                _groundMat.SetFloat("_WaveAmount", look.Waves);
                _groundMat.SetFloat("_RippleAmount", look.Ripples);
                _groundMat.SetVector("_SwellDir",
                    new Vector4(look.SwellDir.x, 0f, look.SwellDir.y, 0f));
                _groundMat.SetFloat("_SwellHeight", look.SwellHeight);
                _groundMat.SetFloat("_SwellFoam", look.SwellFoam);

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
                _veilMat.SetFloat("_GustSwing", look.GustSwing);
                _veilMat.SetFloat("_GustPeriod", look.GustPeriod);
                _veilMat.SetFloat("_GustWander", look.GustWander);
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
                    new Vector4(_pressHalf.x, _pressHalf.y, 0f, 0f));
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
