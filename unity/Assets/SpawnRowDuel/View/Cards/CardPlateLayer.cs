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
    /// All plates face the viewer, the foe's included, exactly as Master Duel rotates its whole
    /// field to one reader rather than making half the board upside down.
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

        // the CSS sleeve fallbacks, for a player whose element never resolved
        static readonly Color YouSleeve = ElementPalette.Hex("#d9b04a");
        static readonly Color FoeSleeve = ElementPalette.Hex("#9a5cc6");

        MatchController _match;
        ElementPalette _palette;

        readonly Dictionary<Sprite, Sprite> _cropped = new Dictionary<Sprite, Sprite>();
        readonly Dictionary<int, Plate> _live = new Dictionary<int, Plate>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _dead = new List<int>();

        sealed class Plate
        {
            public GameObject Root;
            public SpriteRenderer Frame;
            public SpriteRenderer Art;
            public SpriteRenderer Bank;
        }

        void Awake()
        {
            _match = GetComponent<MatchController>();
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _match.Board == null) return;
            if (_palette == null) _palette = new ElementPalette(_match.Engine.Catalog);

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
                Frame = NewRenderer(root.transform, "frame", 4),
                Art = NewRenderer(root.transform, "art", 6),
                Bank = NewRenderer(root.transform, "bank", 8),
            };
            _live[o.Id] = p;
            return p;
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

            p.Root.transform.position = _match.Board.WorldOf(cell) + new Vector3(0f, Lift, 0f);

            bool faceDown = o is ChargeUnit || o is TrapUnit;
            var frame = faceDown ? CardPlateTextures.Back(Sleeve(s, o.Owner))
                                 : CardPlateTextures.Front(_palette.Of(o.Color));

            p.Frame.sprite = frame;
            p.Frame.transform.localScale = FillScale(frame, plateW, plateH);

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
            var tint = o.Owner == Side.You ? Color.white : new Color(0.86f, 0.88f, 1f);
            p.Frame.color = tint;
            p.Art.color = tint;

            PlaceBank(p, o, s, plateW, plateH, faceDown);
        }

        /// <summary>
        /// The mana riding on this card, ON the card.
        ///
        /// A set card carried a floating "SET 1" over its tile instead, which put the card's own
        /// number on the board rather than on the card - and dropped the ◆ while it was at it,
        /// because that overlay is IMGUI and IMGUI's font has no diamond. A face-down card with a
        /// number on it needs no caption: that is what a charge is.
        ///
        /// Face-down it sits under the sleeve's emblem, where the eye already is. Face-up it takes
        /// the stat bar's right corner, out of the illustration's way.
        /// </summary>
        void PlaceBank(Plate p, BoardObject o, GameState s, float plateW, float plateH, bool faceDown)
        {
            var charge = o as ChargeUnit;
            int bank = charge != null ? charge.Invested : o.Bank;
            if (bank <= 0 || (o.Owner != Side.You && faceDown))
            {
                // their face-down cards keep their secret; a bank they can see is a bank you can
                p.Bank.enabled = false;
                return;
            }

            var sleeve = faceDown ? Sleeve(s, o.Owner) : _palette.Of(o.Color).Color;
            var badge = CardPlateTextures.Bank(bank, sleeve);
            p.Bank.sprite = badge;
            p.Bank.enabled = true;

            // A flat card is foreshortened by sin(42°) at the tilted angle, so a badge sized to
            // look right on the texture reads at two thirds of that on screen. Face-down it is
            // the ONLY thing the card says, so it is a stamp rather than a corner mark.
            float h = plateH * (faceDown ? 0.34f : 0.15f);
            float k = h / Mathf.Max(0.0001f, badge.bounds.size.y);
            p.Bank.transform.localScale = new Vector3(k, k, k);

            float bw = badge.bounds.size.x * k;
            float x = faceDown ? 0f : (plateW * 0.5f - bw * 0.5f - plateW * 0.04f);
            float y = faceDown ? -plateH * 0.12f : -plateH * 0.42f;
            p.Bank.transform.localPosition = new Vector3(x, y, -0.002f);   // local -Z is up
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
            if (_cropped.TryGetValue(src, out got)) return got;

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
            return owner == Side.You ? YouSleeve : FoeSleeve;
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
                if (p.Root != null) Destroy(p.Root);
                _live.Remove(_dead[i]);
            }
        }
    }
}
