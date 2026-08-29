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
    /// side's health meter on its own edge of the board - yours along the near edge of your tiles,
    /// theirs along the far edge of theirs - so the two never sit in the same place and a glance
    /// down the board is never reading someone else's numbers. What does NOT turn over is any
    /// number: the plate is rotated and every readout on it is counter-rotated, because a figure
    /// nobody can read is not information (D34).
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
        /// The same card, turned to face the other seat: a half turn about the board's up axis,
        /// which for a card lying flat is a half turn IN ITS OWN PLANE. A rotation, not a
        /// reflection - the art is the right way round, it is only the wrong way up.
        /// </summary>
        public static readonly Quaternion FoeOnTile =
            Quaternion.Euler(0f, 180f, 0f) * FlatOnTile;

        /// <summary>Undoes that half turn for one child, so a readout on a foe card comes out the
        /// same way up as one on yours. Local +Z is the plate's normal, so this is in-plane.</summary>
        public static readonly Quaternion UprightOnFoeCard = Quaternion.Euler(0f, 0f, 180f);

        public static Quaternion RotationFor(Side owner)
        {
            return owner == Side.You ? FlatOnTile : FoeOnTile;
        }

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
            public SpriteRenderer Stats;      // attack / workers / printed health
            public SpriteRenderer Trough;     // the health meter's ground ...
            public SpriteRenderer Fill;       // ... and what is left of it
            public SpriteRenderer Hp;         // the number printed across the meter
        }

        /// <summary>
        /// Sorting orders. The frame and the art sit UNDER the standee (20); everything carrying a
        /// number sits over it, and has to: the figure is planted at the FRONT of its own tile, so
        /// its shins cross the two bands the numbers are printed in, and a number behind a
        /// cut-out is a number that is not there. The cost is that a tall figure in the row in
        /// front can have a far row's numbers drawn across its head - a band's worth of wrong
        /// occlusion, traded for every number on the board being readable.
        /// </summary>
        const int OrderFrame = 4, OrderArt = 6, OrderTrough = 22, OrderFill = 23,
                  OrderStats = 24, OrderNum = 25, OrderBank = 26;

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
                Frame = NewRenderer(root.transform, "frame", OrderFrame),
                Art = NewRenderer(root.transform, "art", OrderArt),
                Trough = NewRenderer(root.transform, "meter", OrderTrough),
                Fill = NewRenderer(root.transform, "fill", OrderFill),
                Stats = NewRenderer(root.transform, "stats", OrderStats),
                Hp = NewRenderer(root.transform, "hp", OrderNum),
                Bank = NewRenderer(root.transform, "bank", OrderBank),
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

            bool foe = o.Owner != Side.You;
            p.Root.transform.position = _match.Board.WorldOf(cell) + new Vector3(0f, Lift, 0f);
            p.Root.transform.rotation = RotationFor(o.Owner);

            // everything with a number on it turns back the right way up
            var upright = foe ? UprightOnFoeCard : Quaternion.identity;
            p.Stats.transform.localRotation = upright;
            p.Hp.transform.localRotation = upright;
            p.Bank.transform.localRotation = upright;

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
            PlaceNumbers(p, o, plateW, plateH, faceDown, foe);
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
        /// What a card on the board is worth, printed on the card: the health METER in the stat
        /// bar, and the statline - attack, worker draw or upkeep, printed health - in the ability
        /// box directly above it.
        ///
        /// It answers "the black bars under the card should display health, a meter with a number
        /// in it, and above the black bar the Attack, Worker Amount and Base HP" - and it answers
        /// the older complaint underneath that one, which is that a board's numbers belong to the
        /// pieces rather than to labels floating near them. The frame was drawing a black bar and
        /// three ruled lines: a stat bar with no stats in it and a stand-in for text. Both are now
        /// the thing they were standing in for.
        ///
        /// The meter is quads rather than a raster: one texture per (hp, max) pair the match
        /// reaches is a cache that grows with the fight, and a scaled quad drains continuously
        /// where a texture drains in texel steps. Only the NUMBER is rastered, keyed by its value.
        /// </summary>
        void PlaceNumbers(Plate p, BoardObject o, float plateW, float plateH, bool faceDown, bool foe)
        {
            var cre = o as CreatureUnit;
            var bld = o as StructureUnit;

            // a face-down card says its investment and nothing else - the rest is the secret
            if (faceDown || (cre == null && bld == null))
            {
                p.Stats.enabled = false;
                p.Trough.enabled = false;
                p.Fill.enabled = false;
                p.Hp.enabled = false;
                return;
            }

            int hp = cre != null ? cre.Hp : bld.Hp;
            int max = Mathf.Max(1, cre != null ? cre.MaxHp : bld.MaxHp);
            float frac = Mathf.Clamp01(hp / (float)max);
            int worker = cre != null ? -cre.Upkeep : bld.Support;

            // ---- the statline, filling the ability box
            float rulesTop = CardPlateTextures.BannerH + CardPlateTextures.ArtH;
            var line = CardPlateTextures.StatLine(
                cre != null ? Stat.Show(cre.EffectiveAttack) : 0,
                worker, Stat.Show(max), cre != null, worker != 0);

            float boxW = plateW * (1f - 8f / CardPlateTextures.W);      // inside inset + outline
            float boxH = plateH * CardPlateTextures.RulesH * 0.98f;
            p.Stats.sprite = line;
            p.Stats.enabled = true;
            p.Stats.transform.localScale = FillScale(line, boxW, boxH);
            p.Stats.transform.localPosition =
                new Vector3(0f, BandY(rulesTop, CardPlateTextures.RulesH, plateH), -0.003f);

            // ---- the meter, filling the stat bar
            float barW = plateW * (1f - 4f / CardPlateTextures.W);      // inside the frame's border
            float barH = plateH * CardPlateTextures.StatsH;
            float y = BandY(rulesTop + CardPlateTextures.RulesH, CardPlateTextures.StatsH, plateH);
            var solid = CardPlateTextures.Solid();

            float troughH = barH * 0.82f;
            p.Trough.sprite = solid;
            p.Trough.enabled = true;
            p.Trough.color = CardPlateTextures.MeterTrough;
            p.Trough.transform.localScale = new Vector3(barW, troughH, 1f);
            p.Trough.transform.localPosition = new Vector3(0f, y, -0.003f);

            // The fill grows the same way ON SCREEN for both sides. It is the one thing on the
            // card that is not turned over with it: a meter has no up, and two boards draining in
            // opposite directions is a thing to decode rather than to read.
            float m = troughH * 0.13f;
            float runW = barW - 2f * m;
            float fillW = runW * frac;
            float dir = foe ? -1f : 1f;

            p.Fill.sprite = solid;
            p.Fill.enabled = fillW > 0.0001f;
            p.Fill.color = CardPlateTextures.HealthTint(frac);
            p.Fill.transform.localScale = new Vector3(fillW, troughH - 2f * m, 1f);
            p.Fill.transform.localPosition =
                new Vector3(dir * (fillW * 0.5f - runW * 0.5f), y, -0.004f);

            var num = CardPlateTextures.Num(Stat.Show(hp));
            var size = num.bounds.size;
            float k = Mathf.Min(barH * 0.76f / Mathf.Max(0.0001f, size.y),
                                barW * 0.62f / Mathf.Max(0.0001f, size.x));
            p.Hp.sprite = num;
            p.Hp.enabled = true;
            p.Hp.transform.localScale = new Vector3(k, k, k);
            p.Hp.transform.localPosition = new Vector3(0f, y, -0.005f);
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
            p.Bank.enabled = true;

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
