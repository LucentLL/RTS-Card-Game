using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The diorama's core (spec 09 §3.8 [REQ]): every creature and structure on the board is a
    /// standing cut-out figure hovering over its slot, casting a ground shadow, idling with a
    /// gentle bob, and LYING FLAT when it cannot act.
    ///
    /// The pose is not decoration - it is the game's readability channel. "Can this unit do
    /// something right now" is otherwise buried in three flags (sick, tapped, moved) that a player
    /// has to remember the interaction of; the reference build answers it at a glance by laying the
    /// figure down, and the rule for it is `canActNow`, ported here against the same three flags.
    ///
    /// Workers never get a figure and face-down cards never get one - a set card is a card back,
    /// and that secret is a rule, not a style choice.
    ///
    /// The figure stands ON the card: <see cref="CardPlateLayer"/> lays the unit's own card flat on
    /// the tile first. A unit whose `_fieldart` cut-out has not been drawn yet gets no figure at
    /// all rather than the card illustration standing up as a second copy of the picture already
    /// lying under it.
    ///
    /// TILTED ONLY (spec 09 §3.8 rule 1, read the other way round). A standing cut-out is a
    /// diorama piece and a diorama needs an angle to be seen from: viewed from directly above, an
    /// upright billboard projects off the top of its own tile and onto the row behind, so the foe's
    /// structures stop looking like they are on the board at all. The figures therefore FADE OUT
    /// with the swing to top-down and the cards on the tiles carry that view by themselves - which
    /// is the Master Duel read, and the reason the top-down angle exists.
    /// </summary>
    public sealed class StandeeLayer : MonoBehaviour
    {
        /// <summary>The 🧍 Figures toggle. Tilted view forces it back on (spec 09 §3.8 rule 1).</summary>
        public static bool Enabled = true;

        const float BobPeriod = 3.4f;          // seconds, ease-in-out, translateY 0 → -7% → 0
        const float BobAmount = 0.07f;

        // The reference stylesheet's sizes, in the cell's own container units (01_board.css):
        //   creature  height min(150cqh, 120cqw), max-width 165cqw
        //   structure height min(122cqh, 102cqw), max-width 132cqw
        //
        // cqh and cqw are the cell's RENDERED height and width - what the tile measures on screen,
        // not what it measures in the world. That distinction is the whole of "buildings in the
        // opponent's back row extend far outside of the tile": a billboard's screen height falls
        // off with distance as 1/z, while its tile's screen height falls off FASTER, because the
        // tile is lying down and its near edge is closer to the camera than its far one. Sized to
        // a fixed number of world units, a figure therefore grows relative to its own tile the
        // further away it stands - by the foe's back row a structure was half again taller than
        // the ground it was standing on. Measured against the tile as it actually projects, the
        // proportion holds across all five rows.
        const float FigureH = 1.50f, FigureHCapW = 1.20f, FigureMaxW = 1.65f;
        const float StructH = 1.22f, StructHCapW = 1.02f, StructMaxW = 1.32f;

        /// <summary>
        /// Where the figure's FEET are, as a fraction of the tile's depth measured from the tile's
        /// near edge - `.spritebob { bottom: 11% }` in the reference (12% for a structure).
        ///
        /// This is the whole of "the buildings float far too high above their tiles". The feet
        /// were at the tile's CENTRE, which is geometrically where the cell is and visually where
        /// nothing is: the card now covers the whole tile, so its near half sits BELOW the point
        /// the figure stands on, and a building with half a card showing under it is a building
        /// hovering over one. Standing it at the front of its own tile puts the card behind it,
        /// where the ground a standee stands on belongs.
        ///
        /// It went to 0.37 for a while - where the printed readout ends - and standing the figure
        /// there did keep every number clear, at the cost of the thing the figure is for. A
        /// billboard one and a half tile-heights tall planted at the FAR end of its card leans
        /// back off the card entirely and stands on the grass behind it, and the card then reads
        /// as a separate object lying in front of its feet rather than as the ground it is
        /// standing on.
        ///
        /// The readout does not need protecting any more: the plate draws every number UNDER the
        /// standee, so where a wide cut-out crosses the statline the ART WINS, and the inspect
        /// card carries the live attack and health of whatever is being pointed at.
        /// </summary>
        const float FeetFromFront = 0.11f;
        const float StructFeetFromFront = 0.12f;

        /// <summary>
        /// How high the figure stands: just clear of the card plate lying on the tile (0.03) and
        /// no higher.
        ///
        /// It "hovers over the card" by the BOB and nothing else. A static lift was tried at 0.30
        /// and was wrong by a factor of six: an upright billboard's height turns into vertical
        /// screen distance under the tilt, so lifting it walks the figure off its own tile and up
        /// over the row behind it, and the player loses track of which slot the unit is in.
        /// </summary>
        const float Lift = 0.05f;

        /// <summary>
        /// How far TOWARD THE CAMERA of its cell centre a standing figure's feet are, in world
        /// units. Always inside the figure's own tile - the front of it, never past the edge.
        ///
        /// A magnitude, not a direction: which way "toward the camera" points is a question about
        /// the SEAT (<see cref="FeetShift"/>), and the two are kept apart so that the size rule -
        /// never past your own tile's near edge - stays one number both seats can be tested on.
        /// </summary>
        public static float FeetOffset(BoardView board, bool structure)
        {
            float tileD = board.CellSize * board.RowStretch;
            return (0.5f - (structure ? StructFeetFromFront : FeetFromFront)) * tileD;
        }

        /// <summary>
        /// The same offset as a world vector, pointing at whichever end of the board this player
        /// is sitting at (<see cref="Seat.TowardCamera"/>).
        ///
        /// The guest's camera is yawed a half turn, so world -Z runs AWAY from them: planting a
        /// figure there stands it at the far edge of its own tile, leaning back off its card and
        /// over the row behind. On a 42-degree tilt that reads as the figure floating high above
        /// the card it belongs to - and as the guest's whole board doing it at once.
        /// </summary>
        public static Vector3 FeetShift(BoardView board, bool structure)
        {
            return new Vector3(0f, 0f, FeetOffset(board, structure) * Seat.TowardCamera);
        }

        /// <summary>
        /// How big this tile is ON SCREEN, and how much screen a world unit is worth standing on
        /// it. Everything a standee is sized by is one of these four numbers.
        ///
        /// Six projections per figure per frame, which is nothing next to twenty units, and the
        /// alternative is a constant that is only right in the middle of the board.
        /// </summary>
        static bool Measure(Camera cam, Vector3 ground, float tileW, float tileD,
                            out float screenH, out float screenW,
                            out float upPerWorld, out float rightPerWorld)
        {
            screenH = screenW = upPerWorld = rightPerWorld = 1f;

            var here = cam.WorldToScreenPoint(ground);
            if (here.z <= 0.01f) return false;

            // the TILE, which lies in the ground plane
            var near = cam.WorldToScreenPoint(ground + new Vector3(0f, 0f, -tileD * 0.5f));
            var far = cam.WorldToScreenPoint(ground + new Vector3(0f, 0f, tileD * 0.5f));
            var left = cam.WorldToScreenPoint(ground + new Vector3(-tileW * 0.5f, 0f, 0f));
            var right = cam.WorldToScreenPoint(ground + new Vector3(tileW * 0.5f, 0f, 0f));

            // the FIGURE, which does not: it is a billboard, so it grows along the CAMERA's own
            // up and right, not the world's. Measuring its height against world +Y understates it
            // by cos(pitch) - a third of it at 42° - and every figure came out half again too big
            // to make up the difference.
            var up = cam.WorldToScreenPoint(ground + cam.transform.up);
            var across = cam.WorldToScreenPoint(ground + cam.transform.right);
            if (near.z <= 0.01f || far.z <= 0.01f || up.z <= 0.01f || across.z <= 0.01f) return false;

            screenH = Mathf.Abs(near.y - far.y);
            screenW = Mathf.Abs(right.x - left.x);
            upPerWorld = Vector2.Distance(new Vector2(up.x, up.y), new Vector2(here.x, here.y));
            rightPerWorld = Vector2.Distance(new Vector2(across.x, across.y),
                                             new Vector2(here.x, here.y));

            return screenH > 0.01f && screenW > 0.01f && upPerWorld > 0.01f && rightPerWorld > 0.01f;
        }

        MatchController _match;
        BoardInput _input;

        readonly Dictionary<int, Standee> _live = new Dictionary<int, Standee>();
        readonly List<int> _seen = new List<int>();
        Sprite _shadow;

        sealed class Standee
        {
            public GameObject Root;
            public Transform Pivot;            // bobs
            public SpriteRenderer Figure;
            public SpriteRenderer Shadow;
            public float Phase;                // so a row of the same creature does not bob in lockstep
            public bool Laid;
        }

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _match.Board == null) return;

            var cam = _input != null && _input.Cam != null ? _input.Cam : Camera.main;
            if (cam == null) return;

            var s = _match.Engine.State;
            _seen.Clear();

            // 1 tilted, 0 top-down, and every value between while the camera swings
            float show = _input != null ? _input.TiltBlend : 1f;

            if (Enabled && show > 0.01f)
            {
                foreach (var kv in s.Objects())
                {
                    var o = kv.Value;
                    if (!(o is CreatureUnit) && !(o is StructureUnit)) continue;   // no figure for a secret
                    var cre = o as CreatureUnit;
                    if (cre != null && cre.IsWorker) continue;

                    _seen.Add(o.Id);
                    var st = Ensure(o);
                    Place(st, o, kv.Key, s, cam, show);
                }
            }

            Prune();
        }

        Standee Ensure(BoardObject o)
        {
            Standee st;
            if (_live.TryGetValue(o.Id, out st)) return st;

            var root = new GameObject("standee:" + o.Id);
            root.transform.SetParent(transform, false);

            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root.transform, false);

            var figGo = new GameObject("figure");
            figGo.transform.SetParent(pivot, false);
            var fig = figGo.AddComponent<SpriteRenderer>();
            fig.sortingOrder = 20;
            fig.sharedMaterial = SpriteMat.Unlit;

            var shadowGo = new GameObject("shadow");
            shadowGo.transform.SetParent(root.transform, false);
            shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var shadow = shadowGo.AddComponent<SpriteRenderer>();
            shadow.sprite = ShadowSprite();
            shadow.color = new Color(0f, 0f, 0f, 0.5f);
            shadow.sortingOrder = 10;
            shadow.sharedMaterial = SpriteMat.Unlit;

            st = new Standee
            {
                Root = root,
                Pivot = pivot,
                Figure = fig,
                Shadow = shadow,
                Phase = (o.Id * 0.37f) % 1f,
            };
            _live[o.Id] = st;
            return st;
        }

        void Place(Standee st, BoardObject o, CellRef cell, GameState s, Camera cam, float show)
        {
            var def = _match.DefOfObject(o);
            var sprite = def != null ? def.FieldArt : null;
            st.Figure.sprite = sprite;
            st.Root.SetActive(sprite != null);
            if (sprite == null) return;                    // no cut-out yet (G1) - the plate carries it

            bool structure = o is StructureUnit;

            // the tile's own footprint - the figure is sized and placed against the ground it
            // stands on, not against an abstract "cell" that stopped being square two slices ago
            float tileW = _match.Board.CellSize;
            float tileD = _match.Board.CellSize * _match.Board.RowStretch;

            bool laid = !structure && !CanActNow(o as CreatureUnit, cell, s);
            st.Laid = laid;

            // A LAID figure lies on the middle of its own card; a STANDING one is planted at the
            // front of the tile - the end nearest whoever is looking - so the card reads as the
            // ground behind its feet.
            var feet = laid ? Vector3.zero : FeetShift(_match.Board, structure);
            var ground = _match.Board.WorldOf(cell) + feet;
            st.Root.transform.position = ground + new Vector3(0f, Lift, 0f);

            // The tile AS IT PROJECTS, converted back into world units at the figure's own depth.
            // Sizing against the flat numbers instead is what let the far rows outgrow their
            // ground: one world unit of upright billboard is worth more screen at the back of the
            // board than one world unit of tile is.
            float upPerWorld, rightPerWorld;
            float tileScreenH, tileScreenW;
            if (!Measure(cam, ground, tileW, tileD,
                         out tileScreenH, out tileScreenW, out upPerWorld, out rightPerWorld))
            {
                tileScreenH = tileD; tileScreenW = tileW; upPerWorld = 1f; rightPerWorld = 1f;
            }

            float targetH = (structure
                ? Mathf.Min(StructH * tileScreenH, StructHCapW * tileScreenW)
                : Mathf.Min(FigureH * tileScreenH, FigureHCapW * tileScreenW)) / upPerWorld;
            float maxW = (structure ? StructMaxW : FigureMaxW) * tileScreenW / rightPerWorld;

            // fit the sprite into the height budget, then clamp its WIDTH - a wide cut-out would
            // otherwise spill across its neighbours in the tilted view
            var size = sprite.bounds.size;
            float scale = size.y > 0.0001f ? targetH / size.y : 1f;
            if (size.x * scale > maxW) scale = maxW / Mathf.Max(0.0001f, size.x);

            float bob = 0f;
            if (!laid && !structure)
            {
                float t = (Time.time / BobPeriod + st.Phase) % 1f;
                bob = -Mathf.Sin(t * Mathf.PI * 2f) * BobAmount * targetH;
            }

            st.Pivot.localScale = Vector3.one;
            st.Figure.transform.localScale = new Vector3(scale, scale, scale);

            // The pivot sits on the cell floor and carries the bob; the figure hangs half its own
            // height above it, so the FEET stay on the slot and the sprite grows upward.
            if (laid)
            {
                // lying flat on its own slot, facing the camera's yaw - the "cannot act" pose.
                // It lies ON its card rather than hovering: down is the whole point of the pose.
                st.Pivot.localPosition = new Vector3(0f, 0.02f, 0f);
                st.Pivot.rotation = Quaternion.Euler(90f, cam.transform.eulerAngles.y, 0f);
                st.Figure.transform.localPosition = Vector3.zero;
            }
            else
            {
                st.Pivot.localPosition = new Vector3(0f, bob, 0f);
                st.Pivot.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
                st.Figure.transform.localPosition = new Vector3(0f, size.y * scale * 0.5f, 0f);
            }

            // AT THE FEET, and as wide as the figure is. It used to sit at the cell centre at a
            // fixed size, which is a shadow belonging to no particular thing: a figure standing at
            // the front of its tile with a shadow half a tile behind it reads as flying.
            float shadowW = Mathf.Max(0.35f, size.x * scale * 0.72f);
            st.Shadow.transform.position = ground + new Vector3(0f, 0.042f, 0f);
            st.Shadow.transform.localScale = new Vector3(shadowW, shadowW * 0.46f, 1f);
            st.Shadow.color = new Color(0f, 0f, 0f, (laid ? 0.30f : 0.50f) * show);

            // the owner reads at a glance even before the stat overlay: a cold rim for the foe
            var tint = o.Owner == Seat.Local ? Color.white : new Color(0.86f, 0.88f, 1f);
            tint.a = show;                                 // fades away as the view goes top-down
            st.Figure.color = tint;
        }


        /// <summary>
        /// canActNow (16_movement.js:30-38), the pose rule verbatim: on its controller's turn a
        /// tapped unit is down, a ready one is up, and a summoning-sick one is up only while it
        /// still has a move and somewhere to move to. On the opponent's turn the question is
        /// instead "could it still block", which a sick creature can and a spent blocker cannot.
        /// </summary>
        /// <remarks>Public and static because the SCENERY needs the same answer: a figure that
        /// is lying down hides no ground behind it, so settled ash must not be masked off its
        /// card. It touches no instance state, so there is nothing to share but the rule.</remarks>
        public static bool CanActNow(CreatureUnit c, CellRef at, GameState s)
        {
            if (c == null) return true;
            if (s.Turn == c.Owner)
            {
                if (c.Tapped) return false;
                if (!c.Sick) return true;
                return !c.Moved && HasEmptyNeighbour(at, s);   // sick, but can still reposition
            }
            return !c.HasBlocked;                              // summoning-sick may still block
        }

        static bool HasEmptyNeighbour(CellRef at, GameState s)
        {
            System.Span<CellRef> around = stackalloc CellRef[8];
            int n = Board.Neighbours(at, around);
            for (int i = 0; i < n; i++) if (s.At(around[i]) == null) return true;
            return false;
        }

        void Prune()
        {
            // NOT gated on a count comparison: a unit dying and another being summoned in the same
            // frame leaves the counts equal and the sets different, and the dead figure would stay
            var dead = new List<int>();
            foreach (var kv in _live)
                if (!_seen.Contains(kv.Key)) dead.Add(kv.Key);

            for (int i = 0; i < dead.Count; i++)
            {
                var st = _live[dead[i]];
                if (st.Root != null) Destroy(st.Root);
                _live.Remove(dead[i]);
            }
        }

        /// <summary>An elliptical blob, generated - the reference's radial-gradient shadow.</summary>
        Sprite ShadowSprite()
        {
            if (_shadow != null) return _shadow;

            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "SRD Standee Shadow",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x / (float)(N - 1) - 0.5f, y / (float)(N - 1) - 0.5f);
                    float r = p.magnitude * 2f;
                    float a = Mathf.Clamp01(1f - r);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a * a));
                }
            tex.Apply();

            _shadow = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            _shadow.name = "SRD Standee Shadow";
            _shadow.hideFlags = HideFlags.HideAndDontSave;
            return _shadow;
        }
    }
}
