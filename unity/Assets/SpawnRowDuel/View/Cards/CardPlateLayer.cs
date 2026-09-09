using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The card itself, lying flat on the tile it occupies - Master Duel's read of a board.
    ///
    /// Every occupied cell gets a plate: the DM frame for a face-up creature or structure with its
    /// illustration laid into the art window, and the procedural sleeve for anything face-down. A
    /// set card is a card back and that secret is a rule, so a charge or a trap is tinted by its
    /// OWNER's element and never by the card underneath it.
    ///
    /// The plate is the unit's identity; <see cref="StandeeLayer"/>'s cut-out then HOVERS over it,
    /// carrying pose and presence. That division is why the standee no longer falls back to the
    /// card illustration when a `_fieldart` cut-out is missing - the fallback drew the same
    /// picture twice, once flat and once standing, and the plate is the one that belongs to the
    /// tile.
    ///
    /// The foe's cards are UPSIDE DOWN, the way they are across a table. That is what puts each
    /// side's stat strip on its own edge of the board - yours along the near edge of your tiles,
    /// theirs along the far edge of theirs - so the two never sit in the same place and a glance
    /// down the board is never reading someone else's numbers. What does NOT turn over is any
    /// figure or word: the plate is rotated and every readout on it is counter-rotated, because
    /// text nobody can read is not information (D34).
    ///
    /// WHAT IS PRINTED ON IT is the hand card's own list, in the hand card's own bands (asked for
    /// 2026-09-08, "they should look just like cards in hand"): the cost in its disc, the name in
    /// the banner, one short ability line in the box, and attack / workers / the health it has
    /// LEFT in the black strip. There is no health bar - see PlaceStats for why the meter went and
    /// what took over its job.
    /// </summary>
    public sealed class CardPlateLayer : MonoBehaviour
    {
        /// <summary>Off hides the plates and leaves the tiles bare under the figures.</summary>
        public static bool Enabled = true;

        /// <summary>
        /// How much of its tile the card covers. ONE, deliberately: the card IS the tile.
        ///
        /// It was 0.98 of a cell along the card's long axis, which sounds like a full tile and is
        /// not one - the card was then sized DOWN from that by its own aspect, so a 0.72 x 0.98
        /// card sat on a 1.00 x 1.45 tile and covered under half of it. Every slot on the board
        /// had a margin of bare ground around its card, and a figure standing on the middle of
        /// that margin is a figure standing on nothing.
        ///
        /// A tile is 1.45x deeper than it is wide (BoardView.RowStretch) and a card is 1.39x
        /// taller than it is wide, so filling the tile costs the card about 4% of stretch along
        /// its length - invisible next to the sin(42 deg) foreshortening the whole plate is
        /// already under, and cheap for a board with no gaps in it.
        /// </summary>
        const float Fill = 1f;
        const float Lift = 0.030f;       // just over the 0.02-thick tile marking (top face 0.01)

        /// <summary>
        /// The basis a plate lies in: local +X to world +X, local +Y to world +Z. That is the only
        /// pair that reads the right way up AND unmirrored from the player's seat, and it puts the
        /// quad's normal DOWN - which is fine, not a mistake: Sprites/Default is Cull Off, the
        /// standee's ground shadow has always been drawn that way, and it is the BASIS, not the
        /// facing, that decides which way round the art comes out.
        /// </summary>
        public static readonly Quaternion FlatOnTile =
            Quaternion.LookRotation(Vector3.down, Vector3.forward);

        /// <summary>
        /// The same card turned to face the other SEAT: a half turn about the board's up axis,
        /// which for a card lying flat is a half turn in its own plane. A rotation, not a
        /// reflection - the art is the right way round, it is only the other way up.
        /// </summary>
        public static readonly Quaternion HalfTurnOnTile =
            Quaternion.Euler(0f, 180f, 0f) * FlatOnTile;

        /// <summary>
        /// Which way up EVERY plate on the board lies - and it is a question about the seat, not
        /// about who owns the card.
        ///
        /// It used to be about the owner: your cards one way up, the foe's turned round the way
        /// they would be across a table, with every readout on them counter-rotated so no number
        /// came out upside down (D34). That arrangement needs the numbers to be separate objects
        /// from the card, and they are not any more - the plate is one texture with the whole face
        /// composited into it. So the choice became a foe card that reads or one that does not,
        /// and D34's own rule settles it: text nobody can read is not information.
        ///
        /// What is left still has to answer to the SEAT. The guest's camera is yawed a half turn
        /// (Seat.CameraYaw), so a plate pinned to the board's absolute geometry lies upright for
        /// the host and upside down for the guest - the whole board of them, which is what the
        /// first version of this shipped. The card faces whoever is looking at it.
        /// </summary>
        public static Quaternion FacingTheSeat
        {
            get { return Seat.Flipped ? HalfTurnOnTile : FlatOnTile; }
        }

        // the CSS sleeve fallbacks, for a player whose element never resolved
        static readonly Color YouSleeve = ElementPalette.Hex("#d9b04a");
        static readonly Color FoeSleeve = ElementPalette.Hex("#9a5cc6");

        MatchController _match;
        BoardInput _input;
        MatchHud _hud;
        ElementPalette _palette;

        /// <summary>The card you have selected, and the cards already committed to the attack.
        /// Bright enough to pick out at a glance in a row of identical creatures.</summary>
        static readonly Color Picked = new Color(0.45f, 0.95f, 1f);
        static readonly Color Swinging = new Color(1f, 0.66f, 0.28f);

        /// <summary>
        /// The DEFENDER's half of the same light: green for a card you have committed to the
        /// block, and a dim green for one that is merely offered it.
        ///
        /// Attacking had a colour and defending did not, which made the two halves of a fight
        /// unequal to read: the attacker could see their group and the defender was ticking names
        /// in a list. Green because the other three are spoken for - cyan is the selection, amber
        /// is the attack, red is the target - and because holding a line is the one thing on this
        /// board that is not aggression.
        /// </summary>
        static readonly Color Defending = new Color(0.38f, 0.95f, 0.55f);
        static readonly Color Offered = new Color(0.44f, 0.66f, 0.50f);

        readonly Dictionary<Sprite, Sprite> _cropped = new Dictionary<Sprite, Sprite>();
        readonly Dictionary<int, Plate> _live = new Dictionary<int, Plate>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _dead = new List<int>();

        sealed class Plate
        {
            public GameObject Root;

            /// <summary>The REAL card face, as a quad sampling its cell of the sheet. This is the
            /// plate for every face-up card the atlas can hold.</summary>
            public MeshFilter Face;
            public MeshRenderer FaceRend;
            public Mesh Quad;
            public Rect FaceUv;

            /// <summary>What you are acting with, as a rim behind the card.</summary>
            public SpriteRenderer Glow;

            // ── the rastered frame: the face-DOWN sleeve, and the fallback for a face-up card
            //    when the sheet is full or could not be built at all ──────────────────────────
            public SpriteRenderer Frame;
            public SpriteRenderer Art;
            public SpriteRenderer Bank;
            public SpriteRenderer Cost;       // the figure in the banner's disc
            public SpriteRenderer Name;       // the card's own title, in its banner
            public SpriteRenderer Rules;      // the one short ability line
            public SpriteRenderer Stats;      // attack / workers / health LEFT
        }

        /// <summary>
        /// Sorting orders. EVERYTHING on the plate sits UNDER the standee (20) now; it used to sit
        /// over it, at 22 to 26.
        ///
        /// The old order was not arbitrary - it was bought with a real argument: a figure planted
        /// 11% up its own tile has its shins crossing the two bands the numbers are printed in,
        /// and a number behind a cut-out is a number that is not there. But paying for that with
        /// the FIGURE was the wrong way round. A health meter ruled across a creature's legs is
        /// the game covering its own art, and the art is the thing a player is looking at.
        ///
        /// So the collision is removed rather than arbitrated: StandeeLayer.FeetFromFront stands
        /// the figure at the top of the ability box instead of inside the stat bar, which puts the
        /// whole readout in FRONT of its feet and the illustration behind them. Nothing overlaps;
        /// where an unusually wide cut-out still does, the art wins - which is also the right
        /// answer to a far row's numbers being drawn across a near figure's head.
        /// </summary>
        const int OrderGlow = 3, OrderFrame = 4, OrderName = 5, OrderArt = 6,
                  OrderRules = 12, OrderStats = 14, OrderCost = 15, OrderBank = 16;

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
            _hud = GetComponent<MatchHud>();       // who you have put in front of the blow

            _atlas = GetComponent<PlateFaceAtlas>();
            if (_atlas == null) _atlas = gameObject.AddComponent<PlateFaceAtlas>();
        }

        PlateFaceAtlas _atlas;
        CardTextService _text;
        CardArtIndex _art;

        void LateUpdate()
        {
            if (_match == null || _match.Board == null) return;

            // NO MATCH, NO CARDS. Returning outright left every plate from the duel just ended
            // standing on the board - Prune is the only thing that destroys one, and it never ran.
            // The shell hides them by switching the board object off, but the COMMANDER SELECT is
            // a battle screen too, so pressing Duel switched them straight back on and the new
            // duel's setup screen was laid over the last duel's cards. An empty seen-set is
            // exactly the sweep this needs.
            if (_match.Engine == null)
            {
                if (_live.Count > 0) { _seen.Clear(); Prune(); }
                return;
            }

            if (_palette == null)
            {
                _palette = new ElementPalette(_match.Engine.Catalog);
                _text = new CardTextService(_match.Engine.Catalog);
                _art = new CardArtIndex(_match.Database);
            }

            var s = _match.Engine.State;
            _seen.Clear();

            if (Enabled)
            {
                foreach (var kv in s.Objects())
                {
                    var o = kv.Value;
                    var cre = o as CreatureUnit;
                    if (cre != null && cre.IsWorker) continue;      // workers file along the edge

                    _seen.Add(o.Id);
                    Place(Ensure(o), o, kv.Key, s);
                }
            }

            if (_atlas != null) _atlas.Sweep(_seen);
            Prune();
        }

        Plate Ensure(BoardObject o)
        {
            Plate p;
            if (_live.TryGetValue(o.Id, out p)) return p;

            var root = new GameObject("plate:" + o.Id);
            root.transform.SetParent(transform, false);
            root.transform.rotation = FlatOnTile;

            p = new Plate
            {
                Root = root,
                Glow = NewRenderer(root.transform, "glow", OrderGlow),
                Frame = NewRenderer(root.transform, "frame", OrderFrame),
                Art = NewRenderer(root.transform, "art", OrderArt),
                Rules = NewRenderer(root.transform, "rules", OrderRules),
                Stats = NewRenderer(root.transform, "stats", OrderStats),
                Cost = NewRenderer(root.transform, "cost", OrderCost),
                Bank = NewRenderer(root.transform, "bank", OrderBank),
                Name = NewRenderer(root.transform, "name", OrderName),
            };

            NewFace(p, root.transform);
            _live[o.Id] = p;
            return p;
        }

        /// <summary>
        /// The quad that samples the card sheet. A MESH rather than a sprite, because a Sprite can
        /// only be cut out of a Texture2D and the sheet is a RenderTexture - so the cell is chosen
        /// by the four UVs instead, and every plate on the board shares one material and one
        /// texture. Renderer.sortingOrder works the same here as on a SpriteRenderer, which is what
        /// keeps the standee above it.
        /// </summary>
        void NewFace(Plate p, Transform parent)
        {
            var go = new GameObject("face");
            go.transform.SetParent(parent, false);

            p.Quad = new Mesh { name = "SRD Plate Face", hideFlags = HideFlags.HideAndDontSave };
            p.Quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            p.Quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            // Sprites/Default multiplies by the vertex colour, and a mesh with no colour stream
            // is undefined on some backends rather than white.
            p.Quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            SetUv(p, new Rect(0f, 0f, 1f, 1f));

            p.Face = go.AddComponent<MeshFilter>();
            p.Face.sharedMesh = p.Quad;
            p.FaceRend = go.AddComponent<MeshRenderer>();
            p.FaceRend.sortingOrder = OrderFrame;
            p.FaceRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.FaceRend.receiveShadows = false;
            p.FaceRend.enabled = false;
        }

        static void SetUv(Plate p, Rect uv)
        {
            p.FaceUv = uv;
            p.Quad.uv = new[]
            {
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin),
                new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax),
            };
        }

        static SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.sharedMaterial = SpriteMat.Unlit;
            return sr;
        }

        /// <summary>
        /// The plate's footprint on a board: the tile's own face, exactly. BoardView scales a cell
        /// CellSize wide and CellSize*RowStretch deep, and the card is that rectangle - x is the
        /// card's width, y its length along the row.
        /// </summary>
        public static Vector2 Footprint(BoardView board)
        {
            return new Vector2(board.CellSize * Fill, board.CellSize * board.RowStretch * Fill);
        }

        void Place(Plate p, BoardObject o, CellRef cell, GameState s)
        {
            var foot = Footprint(_match.Board);
            float plateW = foot.x;
            float plateH = foot.y;

            // EVERY CARD THE RIGHT WAY UP, both halves of the board - and "up" is the seat's, not
            // the world's. See FacingTheSeat.
            p.Root.transform.position = _match.Board.WorldOf(cell) + new Vector3(0f, Lift, 0f);
            p.Root.transform.rotation = FacingTheSeat;

            bool faceDown = o is ChargeUnit || o is TrapUnit;

            // ── the real card face, off the sheet ─────────────────────────────────────────
            bool onSheet = false;
            if (!faceDown && _atlas != null && _text != null)
            {
                CardFaceModel model;
                string key;
                if (TryFaceModel(o, out model, out key))
                {
                    Rect uv;
                    if (_atlas.TryBind(o.Id, model, _palette, key, out uv))
                    {
                        if (p.FaceUv != uv) SetUv(p, uv);
                        p.FaceRend.sharedMaterial = _atlas.Sheet;
                        p.FaceRend.enabled = true;
                        p.Face.transform.localScale = new Vector3(plateW, plateH, 1f);
                        p.Face.transform.localPosition = Vector3.zero;
                        onSheet = true;
                    }
                }
            }

            // The rastered frame is the face-DOWN sleeve, and the understudy for a face-up card
            // the sheet had no room for (or could not be built for at all). Everything it draws
            // goes dark the moment the real face is up, or the two print over each other.
            if (onSheet)
            {
                p.FaceRend.enabled = true;
                p.Frame.enabled = false;
                p.Art.enabled = false;
                p.Name.enabled = false;
                p.Rules.enabled = false;
                p.Stats.enabled = false;
                p.Cost.enabled = false;
                p.Bank.enabled = false;         // CardFace prints its own banked-mana chip
            }
            else
            {
                p.FaceRend.enabled = false;
                PlaceRaster(p, o, cell, s, plateW, plateH, faceDown);
            }

            PlaceGlow(p, o, cell, plateW, plateH);
        }

        /// <summary>
        /// What a card cannot say for itself: whether it is a card you are acting WITH.
        ///
        /// A RIM behind the plate rather than a tint on it. The tint was fine when the frame and
        /// the art were separate renderers and only the frame took the colour; a composited face
        /// is one texture, so multiplying it washes the illustration along with everything else.
        /// The card is opaque, so a quad a few percent larger sitting under it shows as an edge -
        /// which is what a highlight on a card in a real game is.
        ///
        /// Cyan for the one under the cursor, amber for every creature already declared into the
        /// attack, green for every one committed to a block and a dim green for one merely
        /// offered it. Defending outranks the selection: while a blocker choice is parked, the
        /// ticks ARE what the board is being asked about, and a stale cyan from before the attack
        /// landed would be the loudest thing on the screen saying nothing.
        /// </summary>
        void PlaceGlow(Plate p, BoardObject o, CellRef cell, float plateW, float plateH)
        {
            bool picked = _input != null && _input.IsPicked(cell);
            bool swinging = o.Owner == Seat.Local && _match.IsAttacking(cell);
            bool defending = _hud != null && _hud.IsDefending(cell);
            bool offered = !defending && _hud != null && _hud.IsOfferedBlocker(cell);

            if (!picked && !swinging && !defending && !offered)
            {
                p.Glow.enabled = false;
                return;
            }

            var solid = CardPlateTextures.Solid();
            p.Glow.sprite = solid;
            p.Glow.enabled = solid != null;
            p.Glow.color = defending ? Defending
                         : offered ? Offered
                         : picked ? Picked : Swinging;

            // wider than tall in proportion, so the rim is the same thickness on all four sides
            float rim = plateW * 0.055f;
            p.Glow.transform.localScale = new Vector3(plateW + rim, plateH + rim, 1f);
            p.Glow.transform.localPosition = new Vector3(0f, 0f, 0.001f);   // local -Z is up
        }

        /// <summary>
        /// The face-up card as CardFace would draw it, from the LIVE unit - and the key that says
        /// whether anything about it has moved since the last paint.
        ///
        /// It borrows the hand's own model builders, so the ability line is the ABBREVIATED one a
        /// card in hand carries ("Upkeep ⚒-2"), not the inspect card's paragraph. Then the printed
        /// numbers are replaced by what is actually standing there. Raw engine units throughout:
        /// CardFaceModel carries raw and CardFace scales when it prints, and dividing here as well
        /// is how a 500-attack creature once read as 5 on one surface and 50 on another.
        ///
        /// A creature the catalog has never heard of - a token, a hatched form - falls through to
        /// the rastered frame, which reads its stats off the unit and needs no card at all.
        /// </summary>
        bool TryFaceModel(BoardObject o, out CardFaceModel m, out string key)
        {
            m = default(CardFaceModel);
            key = null;
            var catalog = _match.Engine.Catalog;

            var cre = o as CreatureUnit;
            if (cre != null)
            {
                if (!CardFaceModel.TryOfCard(cre.Card, catalog, _text, _art, out m)) return false;
                m.Attack = cre.EffectiveAttack;
                m.Hp = cre.Hp;
                m.MaxHp = cre.MaxHp;
                m.Sick = cre.Sick;
                m.Tapped = cre.Tapped;
                m.Moved = cre.Moved;
            }
            else
            {
                var bld = o as StructureUnit;
                if (bld == null) return false;

                var def = catalog.Structure(bld.DefId, bld.Color);
                if (def == null) return false;
                m = CardFaceModel.OfStructure(def, _text, _art);
                m.Hp = bld.Hp;
                m.MaxHp = bld.MaxHp;
            }

            m.Foe = o.Owner != Seat.Local;
            m.Bank = o.Bank;

            key = m.Name + "|" + m.Hp + "/" + m.MaxHp + "|" + m.Attack + "|" + m.WorkerChip
                + "|" + m.Bank + "|" + (m.Sick ? "s" : "") + (m.Tapped ? "t" : "")
                + (m.Moved ? "m" : "") + (m.Foe ? "f" : "") + (CardFace.Crystal ? "x" : "");
            return true;
        }

        /// <summary>
        /// The rastered frame, unchanged: the procedural sleeve for a face-down card, and the
        /// stand-in frame for a face-up one the sheet could not take.
        ///
        /// A set card is a card back and that secret is a rule, so a charge or a trap is tinted by
        /// its OWNER's element and never by the card underneath it.
        /// </summary>
        void PlaceRaster(Plate p, BoardObject o, CellRef cell, GameState s,
                         float plateW, float plateH, bool faceDown)
        {
            var frame = faceDown ? CardPlateTextures.Back(Sleeve(s, o.Owner))
                                 : CardPlateTextures.Front(_palette.Of(o.Color));

            // A renderer is never left ENABLED holding nothing. A null sprite here means a
            // texture build that failed, and an enabled SpriteRenderer with no sprite is one
            // more thing for the render-node prep to walk (see CardPlateTextures.SpriteOf).
            p.Frame.sprite = frame;
            p.Frame.enabled = frame != null;
            if (frame != null) p.Frame.transform.localScale = FillScale(frame, plateW, plateH);

            // the illustration, filling the art window
            float winW = plateW * (1f - 2f * CardPlateTextures.ArtInsetX) - 0.01f;
            float winH = plateH * CardPlateTextures.ArtH - 0.01f;

            var def = faceDown ? null : _match.DefOfObject(o);
            var art = def != null ? Cropped(def.CardArt, winW / winH) : null;
            p.Art.sprite = art;
            p.Art.enabled = art != null;
            if (art != null)
            {
                // the crop already matches the window's aspect, so one scale fills it exactly
                float k = winW / Mathf.Max(0.0001f, art.bounds.size.x);
                p.Art.transform.localScale = new Vector3(k, k, k);

                float centreFromTop = CardPlateTextures.BannerH + CardPlateTextures.ArtH * 0.5f;
                p.Art.transform.localPosition =
                    new Vector3(0f, (0.5f - centreFromTop) * plateH, -0.001f);   // local -Z is up
            }

            // the foe's half already reads cold from its row tint; the plate keeps the same rule
            var tint = o.Owner == Seat.Local ? Color.white : new Color(0.86f, 0.88f, 1f);
            p.Art.color = tint;
            p.Frame.color = tint;

            PlaceCost(p, o, faceDown, plateW, plateH);
            PlaceName(p, def, faceDown, plateW, plateH);
            PlaceBank(p, o, s, plateW, plateH, faceDown);
            PlaceRules(p, o, plateW, plateH, faceDown);
            PlaceStats(p, o, plateW, plateH, faceDown);
        }

        /// <summary>
        /// The card's NAME, in its own title band.
        ///
        /// A plate prints one because nothing else honestly can. The name lived as a UI Toolkit
        /// chip hung off the tile's front - a label floating on the grass, belonging to nothing -
        /// and then as the same chip moved onto the title band, where it drew straight across the
        /// field art, because an overlay panel cannot sort behind a sprite in the scene. Here it
        /// is a sprite like the rest of the card, at OrderName, so the standee covers it wherever
        /// the cut-out is opaque and it shows through wherever it is not. In TOP-DOWN, where the
        /// figures lie down, the whole board reads its own names.
        ///
        /// A face-down card has no name to print - that is the secret.
        /// </summary>
        void PlaceName(Plate p, SpawnRowDuel.Data.CardDefinition def, bool faceDown,
                       float plateW, float plateH)
        {
            var strip = faceDown || def == null ? null
                      : CardPlateTextures.Name(def.DisplayName);
            p.Name.sprite = strip;
            p.Name.enabled = strip != null;
            if (strip == null) return;

            // Inside the banner and CLEAR OF THE DISC. The cost circle is drawn against the
            // banner's left edge and now has a figure in it (PlaceCost), so a name box centred on
            // the whole width would print the first letter or two straight over the number. The
            // hand card solves it the same way - the badge is absolute and the name column is
            // padded past it (CardFace's `names`).
            float boxW = (0.95f - DiscRight) * plateW;         // ...to a margin off the right edge

            // 0.80 of the banner, up from 0.62. The name is the card's headline and it was
            // printing smaller than the ability caption two bands below it, which is the wrong way
            // round on any card ever made - and at eight screen pixels the difference between
            // legible and nearly is exactly this much.
            float boxH = plateH * CardPlateTextures.BannerH * 0.80f;
            float boxX = ((DiscRight + 0.95f) * 0.5f - 0.5f) * plateW;

            // FIT, not fill. Every other readout on this card is stretched to its band because
            // it was rastered at the band's own aspect; a name is rastered at whatever aspect its
            // letters came to, so stretching it to a fixed box would squash a short name and
            // starve a long one.
            var size = strip.bounds.size;
            float k = Mathf.Min(boxW / Mathf.Max(0.0001f, size.x),
                                boxH / Mathf.Max(0.0001f, size.y));
            p.Name.transform.localScale = new Vector3(k, k, 1f);
            p.Name.transform.localPosition =
                new Vector3(boxX, BandY(0f, CardPlateTextures.BannerH, plateH), -0.0005f);
        }

        /// <summary>
        /// Where the banner's cost disc ends, as a fraction of the card's width from its left
        /// edge. The frame draws it centred at 3 + r with r = 0.092 * W (BuildFront), so it runs
        /// out to 3/W + 2 * 0.092 - and both the disc's own figure and the name that has to dodge
        /// it are placed off this one number rather than off two copies of the same arithmetic.
        /// </summary>
        const float DiscRight = 3f / CardPlateTextures.W + 0.184f;

        /// <summary>
        /// The card's printed COST, in the disc the frame has always drawn for it and never
        /// filled.
        ///
        /// The live unit's cost rather than the definition's, because an upgraded structure IS
        /// its tier - a Bombard that grew out of a Cannon Tower is worth what the Bombard costs,
        /// and the card underneath it went into the ground two upgrades ago.
        ///
        /// A face-down card prints nothing here: what it cost to set is a fixed one, and what was
        /// poured into it afterwards is the bank badge's business (PlaceBank). The card's own cost
        /// is the secret.
        /// </summary>
        void PlaceCost(Plate p, BoardObject o, bool faceDown, float plateW, float plateH)
        {
            var cre = o as CreatureUnit;
            var bld = o as StructureUnit;
            int cost = cre != null ? cre.Cost : bld != null ? bld.Cost : -1;

            if (faceDown || cost < 0)
            {
                p.Cost.enabled = false;
                return;
            }

            var pip = CardPlateTextures.Pip(cost);
            p.Cost.sprite = pip;
            p.Cost.enabled = pip != null;
            if (pip == null) return;

            float h = plateH * CardPlateTextures.BannerH * 0.53f;
            float k = h / Mathf.Max(0.0001f, pip.bounds.size.y);
            p.Cost.transform.localScale = new Vector3(k, k, k);
            p.Cost.transform.localPosition =
                new Vector3((DiscRight * 0.5f - 0.5f) * plateW,
                            BandY(0f, CardPlateTextures.BannerH, plateH), -0.0055f);
        }

        /// <summary>
        /// Local Y of a band's centre. The frame states its bands as fractions of the card's
        /// height measured from the TOP, and the plate's local +Y points at that top, so one
        /// conversion in one place beats four sign errors in four.
        /// </summary>
        static float BandY(float fromTop, float bandH, float plateH)
        {
            return (0.5f - (fromTop + bandH * 0.5f)) * plateH;
        }

        /// <summary>
        /// THE ABILITY BOX, holding one short line of what the card does.
        ///
        /// Asked for 2026-09-08: the cards on the board "should look just like cards in hand -
        /// minimal description". This is the minimal description. It is the same LABELS the hand
        /// card prints in the same band - "UPKEEP -3", "DETONATE 150", "FORGE 2" - because a
        /// label is what fits, and because a board card that says a different thing from the card
        /// that was in your hand a turn ago is two cards.
        ///
        /// Read off the LIVE unit rather than the definition. An upgraded structure is its own
        /// tier, a hatched cocoon is its hatch form, and a token has no card at all - all three
        /// are on the board and none of them can be looked up by the name in the banner.
        /// </summary>
        void PlaceRules(Plate p, BoardObject o, float plateW, float plateH, bool faceDown)
        {
            var strip = faceDown ? null : CardPlateTextures.Brief(Brief(o));
            p.Rules.sprite = strip;
            p.Rules.enabled = strip != null;
            if (strip == null) return;

            float rulesTop = CardPlateTextures.BannerH + CardPlateTextures.ArtH;

            // THE FRAME'S OWN BOX, exactly - the same inset the art window uses one band up,
            // because BuildFront draws both off ArtInsetX. The plaque used to be laid at the
            // card's near-full width, so a white slab overhung the ivory box on both sides and
            // ran out to the card's border.
            float boxW = plateW * (1f - 2f * CardPlateTextures.ArtInsetX);
            float boxH = plateH * CardPlateTextures.RulesH * 0.98f;

            p.Rules.transform.localScale = FillScale(strip, boxW, boxH);
            p.Rules.transform.localPosition =
                new Vector3(0f, BandY(rulesTop, CardPlateTextures.RulesH, plateH), -0.003f);
        }

        /// <summary>
        /// THE STAT STRIP: attack, the worker chip, and the health this unit has LEFT - laid into
        /// the black footer band, which is where a card carries its numbers.
        ///
        /// This is the other half of the same request, and the half with a subtraction in it. The
        /// strip used to hold a health METER - a trough, a draining fill and the number printed
        /// across it - while the statline sat one band up in the ability box. So the card wore its
        /// own anatomy inside out, and it spent a fifth of its face on a bar that said, less
        /// precisely, exactly what the number beside it already said. "Remove health bar. Just
        /// keep track of current HP of cards on the card": the meter is gone, the statline came
        /// down to the band it belongs in, and its heart carries the CURRENT health rather than
        /// the printed total.
        ///
        /// What the bar did better than a figure was read at a glance, without arithmetic, and
        /// that survives as the figure's COLOUR (CardPlateTextures.HpInk) - salmon while healthy,
        /// then amber, then red. Same three steps, one glyph, no band.
        /// </summary>
        void PlaceStats(Plate p, BoardObject o, float plateW, float plateH, bool faceDown)
        {
            var cre = o as CreatureUnit;
            var bld = o as StructureUnit;

            // a face-down card says its investment and nothing else - the rest is the secret
            if (faceDown || (cre == null && bld == null))
            {
                p.Stats.enabled = false;
                return;
            }

            int hp = cre != null ? cre.Hp : bld.Hp;
            int max = Mathf.Max(1, cre != null ? cre.MaxHp : bld.MaxHp);
            int worker = cre != null ? -cre.Upkeep : bld.Support;

            var line = CardPlateTextures.StatLine(
                cre != null ? Stat.Show(cre.EffectiveAttack) : 0,
                worker, Stat.Show(hp), cre != null, worker != 0,
                CardPlateTextures.HpInk(hp, max));

            float y = BandY(1f - CardPlateTextures.StatsH, CardPlateTextures.StatsH, plateH);
            float barW = plateW * (1f - 4f / CardPlateTextures.W);      // inside the frame's border
            float barH = plateH * CardPlateTextures.StatsH;

            p.Stats.sprite = line;
            p.Stats.enabled = line != null;
            p.Stats.transform.localScale = FillScale(line, barW, barH);
            p.Stats.transform.localPosition = new Vector3(0f, y, -0.003f);
        }

        /// <summary>
        /// The card's ability line, in at most two short rows - and in the order a player needs
        /// them, because only two fit.
        ///
        /// Upkeep leads: it is the one clause that costs something every single turn, and a row
        /// that cannot pay it locks its own harvest. The keyword comes next because it is what the
        /// card is FOR, and the two bare flags bring up the rear. A creature carrying all four is
        /// rare and its inspect card still has the lot.
        /// </summary>
        static string Brief(BoardObject o)
        {
            string one = null, two = null;
            var cre = o as CreatureUnit;
            var bld = o as StructureUnit;

            if (cre != null)
            {
                Row(ref one, ref two, cre.Upkeep > 0 ? "UPKEEP -" + cre.Upkeep : null);
                Row(ref one, ref two, KeywordLine(cre));
                Row(ref one, ref two, cre.FirstStrike ? "FIRST STRIKE" : null);
                // the flag and the keyword are two things wearing one name; the keyword already
                // printed its own row above
                Row(ref one, ref two,
                    cre.Entrench && cre.Keyword != Keyword.Entrench ? "ENTRENCH" : null);
            }
            else if (bld != null)
            {
                Row(ref one, ref two, EffectLine(bld));

                // A structure whose whole job IS its workforce - an Encampment - has no upkeep
                // effect at all, and printed an empty ivory box. The hand card prints the worker
                // clause on its own for exactly those (CardTextService.StructureBrief), so this
                // does too. It repeats the strip's chip only on the cards that have nothing else
                // to say, which is better than a blank plaque that reads as missing content.
                if (one == null && bld.Support != 0)
                    one = "WORKERS " + (bld.Support > 0 ? "+" : "") + bld.Support;
            }

            if (one == null) return "";
            return two == null ? one : one + "\n" + two;
        }

        static void Row(ref string one, ref string two, string s)
        {
            if (string.IsNullOrEmpty(s) || two != null) return;
            if (one == null) one = s; else two = s;
        }

        /// <summary>
        /// The keyword's short label, off the LIVE unit - which is why this is not
        /// CardTextService.KeywordLabel: that one takes a CreatureCard, and half the creatures on
        /// this board (tokens, hatched forms) do not have one. Same words, no lookup.
        /// </summary>
        static string KeywordLine(CreatureUnit c)
        {
            switch (c.Keyword)
            {
                case Keyword.Detonate: return "DETONATE " + Stat.Num(c.Detonate);
                case Keyword.Reap: return "REAP " + Stat.Num(c.Reap);
                case Keyword.Ward: return "WARD";
                case Keyword.Undertow: return "UNDERTOW";
                case Keyword.Entrench: return "ENTRENCH";
                case Keyword.Chrysalis: return "CHRYSALIS";
                case Keyword.Scour: return "SCOUR";
                case Keyword.Overcharge: return "OVERCHARGE";
                default: return "";
            }
        }

        /// <summary>
        /// What a structure does, in the fewest words that still name it. The worker clause the
        /// hand card appends is dropped: the ⚒ chip in the strip two bands down is that same
        /// number, and it is the only thing on the card that would be said twice.
        /// </summary>
        static string EffectLine(StructureUnit b)
        {
            switch (b.Effect)
            {
                case StructEffect.Mana: return "FORGE " + b.Value;
                case StructEffect.Villager: return "TRAINS WORKERS";
                case StructEffect.Damage: return "TOWER " + Stat.Num(b.Value);
                case StructEffect.Wall: return "BULWARK";
                case StructEffect.Revive: return "RECALLS FALLEN";
                case StructEffect.Vault: return "BANKS " + b.Value;
                case StructEffect.Heal: return "MENDS " + Stat.Num(b.Value);
                default: return "";
            }
        }
        /// <summary>
        /// The mana riding on this card, ON the card.
        ///
        /// A FACE-DOWN card always shows what was paid to put it there, and shows it to BOTH
        /// players. That number is the whole of the bluff: setting costs ◆1, you may pour in more
        /// than the card needs, and a card that will not have its cost when it is turned over
        /// simply fails (Traps.ProvokeFaceDown destroys an underfunded charge outright). A bluff
        /// nobody can read is not a bluff, so hiding the figure from the opponent - which is what
        /// this did - removed the only reason to over-pay.
        ///
        /// A set TRAP consumed its ◆1 rather than banking it, so it has no investment to report;
        /// it reports the ◆1 it cost. That is not a rounding of the truth, it is the point: a
        /// face-down showing ◆1 is either a trap or a creature nobody has funded yet, and telling
        /// those apart is the guess the mechanic is made of.
        ///
        /// Face-down it sits under the sleeve's emblem, where the eye already is. Face-up it rides
        /// the BANNER's right corner - the stat bar it used to take is the health meter now, and a
        /// badge parked over a meter hides the half of it that matters.
        /// </summary>
        void PlaceBank(Plate p, BoardObject o, GameState s, float plateW, float plateH, bool faceDown)
        {
            var charge = o as ChargeUnit;
            int bank = faceDown
                ? (charge != null ? charge.Invested : 1 + o.Bank)   // a trap spent its ◆1 outright
                : o.Bank;

            if (bank <= 0)
            {
                p.Bank.enabled = false;
                return;
            }

            var sleeve = faceDown ? Sleeve(s, o.Owner) : _palette.Of(o.Color).Color;
            var badge = CardPlateTextures.Bank(bank, sleeve);
            p.Bank.sprite = badge;
            p.Bank.enabled = badge != null;

            // A flat card is foreshortened by sin(42°) at the tilted angle, so a badge sized to
            // look right on the texture reads at two thirds of that on screen. Face-down it is
            // the ONLY thing the card says, so it is a stamp rather than a corner mark.
            float h = plateH * (faceDown ? 0.34f : CardPlateTextures.BannerH * 0.84f);
            float k = h / Mathf.Max(0.0001f, badge.bounds.size.y);
            p.Bank.transform.localScale = new Vector3(k, k, k);

            float bw = badge.bounds.size.x * k;
            float x = faceDown ? 0f : (plateW * 0.5f - bw * 0.5f - plateW * 0.035f);
            float y = faceDown ? -plateH * 0.12f
                               : BandY(0f, CardPlateTextures.BannerH, plateH);
            p.Bank.transform.localPosition = new Vector3(x, y, -0.006f);   // local -Z is up
        }

        /// <summary>
        /// The illustration re-cut to the art window's aspect, centred - `background-size: cover`,
        /// which is what the card frame does with the same picture (CardFace, spec 09 §6.1).
        ///
        /// Fitting INSIDE the window instead was the obvious thing and the wrong one: card art is
        /// portrait, the window is landscape, and fit-inside left a narrow strip of picture adrift
        /// in a field of wash. A sprite cannot be masked without a mask, so the crop happens where
        /// sprites are actually defined - a second Sprite over the same texture rect. No pixels are
        /// copied and the texture never needs to be readable.
        /// </summary>
        Sprite Cropped(Sprite src, float aspect)
        {
            if (src == null) return null;

            Sprite got;
            if (_cropped.TryGetValue(src, out got) && got != null) return got;   // never a corpse

            var r = src.textureRect;
            float w = r.width, h = r.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;

            // FullRect, not the default Tight: a tight mesh trims transparent margins away, and
            // the whole point here is that the sprite's bounds ARE the window - scaling a trimmed
            // mesh to the window width blows an art file's padding up into the frame.
            got = Sprite.Create(src.texture,
                                new Rect(r.x + (r.width - w) * 0.5f, r.y + (r.height - h) * 0.5f, w, h),
                                new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            got.name = src.name + " (plate)";
            got.hideFlags = HideFlags.HideAndDontSave;
            _cropped[src] = got;
            return got;
        }

        /// <summary>
        /// Scale the frame so it COVERS the footprint, per axis. Fitting it uniformly is what left
        /// the margin: the frame texture is 96x133 and the tile is 1.00 x 1.45, so the uniform fit
        /// is decided by the narrower axis and the other one keeps the slack. The two aspects are
        /// within 4.5% of each other, which is the whole distortion this costs.
        /// </summary>
        static Vector3 FillScale(Sprite sprite, float w, float h)
        {
            var size = sprite.bounds.size;
            return new Vector3(w / Mathf.Max(0.0001f, size.x), h / Mathf.Max(0.0001f, size.y), 1f);
        }

        Color Sleeve(GameState s, Side owner)
        {
            var el = s.P(owner).PrimaryColor;
            if (el != Element.None) return _palette.Of(el).Color;
            return owner == Seat.Local ? YouSleeve : FoeSleeve;
        }

        void Prune()
        {
            // set-difference, not a count check: one unit dying as another is summoned in the same
            // frame leaves the counts equal and the sets different, and the dead plate would stay
            _dead.Clear();
            foreach (var kv in _live)
                if (!_seen.Contains(kv.Key)) _dead.Add(kv.Key);

            for (int i = 0; i < _dead.Count; i++)
            {
                var p = _live[_dead[i]];
                // the quad's mesh is built per plate, and destroying a GameObject does not
                // destroy a mesh that was assigned to it
                if (p.Quad != null) Destroy(p.Quad);
                if (p.Root != null) Destroy(p.Root);
                _live.Remove(_dead[i]);
            }
        }
    }
}
