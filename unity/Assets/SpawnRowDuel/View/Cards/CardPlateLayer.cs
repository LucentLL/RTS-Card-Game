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

        const float PlateH = 0.98f;      // cells, along the card's LONG axis - it nearly fills its tile
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

        void Place(Plate p, BoardObject o, CellRef cell, GameState s)
        {
            float plateH = PlateH * _match.Board.CellSize;
            float plateW = plateH / CardFace.Aspect;

            p.Root.transform.position = _match.Board.WorldOf(cell) + new Vector3(0f, Lift, 0f);

            bool faceDown = o is ChargeUnit || o is TrapUnit;
            var frame = faceDown ? CardPlateTextures.Back(Sleeve(s, o.Owner))
                                 : CardPlateTextures.Front(_palette.Of(o.Color));

            p.Frame.sprite = frame;
            p.Frame.transform.localScale = FitScale(frame, plateW, plateH);

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

        static Vector3 FitScale(Sprite sprite, float w, float h)
        {
            var size = sprite.bounds.size;
            float k = Mathf.Min(w / Mathf.Max(0.0001f, size.x), h / Mathf.Max(0.0001f, size.y));
            return new Vector3(k, k, k);
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
