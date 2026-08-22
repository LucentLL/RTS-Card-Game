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
    /// The figure HOVERS over the card: <see cref="CardPlateLayer"/> lays the unit's own card flat
    /// on the tile first, and the standee stands on it. A unit whose `_fieldart` cut-out has not
    /// been drawn yet gets no figure at all rather than the card illustration standing up as a
    /// second copy of the picture already lying under it.
    /// </summary>
    public sealed class StandeeLayer : MonoBehaviour
    {
        /// <summary>The 🧍 Figures toggle. Tilted view forces it back on (spec 09 §3.8 rule 1).</summary>
        public static bool Enabled = true;

        const float BobPeriod = 3.4f;          // seconds, ease-in-out, translateY 0 → -7% → 0
        const float BobAmount = 0.07f;
        const float FigureHeight = 1.30f;      // cells - min(150cqh, 120cqw) at a 1×1 cell
        const float StructureHeight = 1.05f;
        const float MaxWidth = 1.60f;          // the 165cqw cap: standees must not inflate with depth
        const float Lift = 0.10f;              // clear of the card plate lying on the tile (0.075)

        /// <summary>
        /// How far above its card the figure floats.
        ///
        /// Not decoration either: the card now lies on the tile, and a figure standing ON it hides
        /// the half of it the camera can see. Floating the cut-out lets the card read while the
        /// blob shadow - which stays down on the card - keeps the figure tied to its slot.
        /// </summary>
        const float Hover = 0.30f;

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

            if (Enabled)
            {
                foreach (var kv in s.Objects())
                {
                    var o = kv.Value;
                    if (!(o is CreatureUnit) && !(o is StructureUnit)) continue;   // no figure for a secret
                    var cre = o as CreatureUnit;
                    if (cre != null && cre.IsWorker) continue;

                    _seen.Add(o.Id);
                    var st = Ensure(o);
                    Place(st, o, kv.Key, s, cam);
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

        void Place(Standee st, BoardObject o, CellRef cell, GameState s, Camera cam)
        {
            var def = _match.DefOfObject(o);
            var sprite = def != null ? def.FieldArt : null;
            st.Figure.sprite = sprite;
            st.Root.SetActive(sprite != null);
            if (sprite == null) return;                    // no cut-out yet (G1) - the plate carries it

            var world = _match.Board.WorldOf(cell);
            st.Root.transform.position = world + new Vector3(0f, Lift, 0f);

            bool structure = o is StructureUnit;
            float targetH = structure ? StructureHeight : FigureHeight;

            // fit the sprite into the height budget, then clamp its WIDTH - a wide cut-out would
            // otherwise spill across its neighbours in the tilted view
            var size = sprite.bounds.size;
            float scale = size.y > 0.0001f ? targetH / size.y : 1f;
            if (size.x * scale > MaxWidth) scale = MaxWidth / Mathf.Max(0.0001f, size.x);

            bool laid = !structure && !CanActNow(o as CreatureUnit, cell, s);
            st.Laid = laid;

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
                st.Pivot.localPosition = new Vector3(0f, Hover + bob, 0f);
                st.Pivot.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
                st.Figure.transform.localPosition = new Vector3(0f, targetH * 0.5f, 0f);
            }

            // ABOVE the cell surface, not inside it: the cell is a 0.12-thick cube whose top face
            // sits at y=0.06, and a shadow quad under that z-fought with it into a bright ellipse.
            // It now lands on the CARD instead of the tile, which is where a hovering figure's
            // shadow belongs anyway.
            st.Shadow.transform.position = world + new Vector3(0f, 0.095f, 0f);
            st.Shadow.transform.localScale = new Vector3(0.62f, 0.30f, 1f);
            st.Shadow.color = new Color(0f, 0f, 0f, laid ? 0.30f : 0.50f);

            // the owner reads at a glance even before the stat overlay: a cold rim for the foe
            st.Figure.color = o.Owner == Side.You ? Color.white : new Color(0.86f, 0.88f, 1f);
        }


        /// <summary>
        /// canActNow (16_movement.js:30-38), the pose rule verbatim: on its controller's turn a
        /// tapped unit is down, a ready one is up, and a summoning-sick one is up only while it
        /// still has a move and somewhere to move to. On the opponent's turn the question is
        /// instead "could it still block", which a sick creature can and a spent blocker cannot.
        /// </summary>
        bool CanActNow(CreatureUnit c, CellRef at, GameState s)
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
