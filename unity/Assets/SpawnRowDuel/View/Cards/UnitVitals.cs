using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// Who each unit IS, drawn under its own tile - and whether your selected attacker may hit it.
    ///
    /// It used to carry the numbers, and then the name, and it carries neither now. The numbers
    /// went to the CARD (<see cref="CardPlateLayer"/> prints the meter in the stat bar and the
    /// statline in the ability box), because the same health in two places a finger's width apart
    /// is not redundancy, it is two things to check. The NAME went to the card as well, printed
    /// into its title band at a sorting order beneath the standee - the only placement that is
    /// both ON the card and not drawn across the field art, which an overlay panel can never
    /// manage, since it cannot sort behind a sprite in the scene.
    ///
    /// What is left is what an overlay is actually for - things that are true right now and
    /// appear on no card at all:
    ///
    /// * the two keyword states that CHANGE - a cocoon's progress and a banked discharge;
    /// * the TARGET ring, straight off BoardInput's engine-probed list, so aiming at something and
    ///   reading what it has left is one glance.
    ///
    /// With nothing to say it does not draw at all. A board of quiet units is a board of bare
    /// cards, which is the point.
    ///
    /// The placement is still the one the complaint bought: hung off the tile's NEAR edge, in UI
    /// Toolkit, on the gated font chain. The old IMGUI overlay floated 1.45 cells ABOVE the cell -
    /// most of a row up the screen under the tilt - so the foe's back row put its label behind the
    /// castle wall and the layer's answer was to drop it. The row you attack into was the row with
    /// nothing on it.
    /// </summary>
    public sealed class UnitVitals : MonoBehaviour
    {
        public static bool Enabled = true;

        MatchController _match;
        BoardInput _input;
        MatchHud _hud;
        HandBar _hand;

        VisualElement _layer;
        int _builtFor = -1;           // the HandBar panel generation these chips belong to
        readonly Dictionary<int, Chip> _live = new Dictionary<int, Chip>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _dead = new List<int>();

        static readonly Color You = new Color(1f, 0.90f, 0.55f);
        static readonly Color Foe = new Color(0.65f, 0.80f, 1f);
        static readonly Color TargetRed = new Color(1f, 0.36f, 0.30f);
        static readonly Color JoinGold = new Color(1f, 0.80f, 0.36f);

        /// <summary>Committed to the block. The same green the card frame and the standee use, so
        /// the ring, the card and the figure are one light rather than three.</summary>
        static readonly Color BlockGreen = new Color(0.38f, 0.95f, 0.55f);

        sealed class Chip
        {
            public VisualElement Root;
            public Label Name;
            public Label Line;
        }

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
            _hud = GetComponent<MatchHud>();
            _hand = GetComponent<HandBar>();
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _match.Board == null) return;
            if (_hand == null || !_hand.PanelReady) return;

            // REBUILT WITH THE PANEL. These chips hang off HandBar's board layer, and that layer is
            // thrown away and remade every time the board object is switched off and on again -
            // which the shell does whenever the screen is not a duel. The old elements survive as
            // references in these dictionaries, orphaned, drawing nothing: a match returned to
            // from the menu had no health bars, no target rings and no keyword chips on it.
            if (_layer == null || _builtFor != _hand.PanelGeneration)
            {
                _builtFor = _hand.PanelGeneration;
                _live.Clear();
                System.Array.Clear(_workers, 0, _workers.Length);

                _layer = new VisualElement { pickingMode = PickingMode.Ignore };
                _layer.style.position = Position.Absolute;
                _layer.style.left = 0; _layer.style.right = 0;
                _layer.style.top = 0; _layer.style.bottom = 0;
                _hand.BoardLayer.Add(_layer);
            }

            var cam = _input != null && _input.Cam != null ? _input.Cam : Camera.main;
            var s = _match.Engine.State;
            _seen.Clear();

            if (Enabled && cam != null && !s.IsOver)
            {
                var panel = _hand.PanelSize();
                float scale = panel.y / Mathf.Max(1f, Screen.height);   // panel px per screen px

                foreach (var kv in s.Objects())
                {
                    var o = kv.Value;
                    var cre = o as CreatureUnit;
                    var bld = o as StructureUnit;
                    if (cre == null && bld == null) continue;      // a face-down card says nothing
                    if (cre != null && cre.IsWorker) continue;

                    _seen.Add(o.Id);
                    Place(o, cre, bld, kv.Key, cam, panel, scale);
                }

                PlaceWorkerFigures(cam, panel, scale);
            }
            else HideWorkerFigures();

            Prune();
        }

        // ── the row workforce ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Each row's workforce, as a SIGNED NUMBER hung off the end of its own row.
        ///
        /// It used to be a file of little capsules standing beside the board, one per body in the
        /// pool. Two things a file of pills cannot do: be read without counting, and go negative -
        /// and the second one is the whole upkeep mechanic. A row in deficit is what locks the
        /// harvest, and the pills simply vanished below zero, which reads as "no workers here"
        /// rather than "this row owes two".
        ///
        /// Six of them: both sides, all three zones. Raid is deliberately not drawn - it is not a
        /// row, it is the bill for standing on somebody else's.
        ///
        /// Positioned in SCREEN space off the row's own ends, not at a fixed world offset outside
        /// the board. The board is framed to FILL the width, so there is no world space outside it
        /// at the near rows - which is where the capsules went when they disappeared.
        /// </summary>
        readonly Chip[,] _workers = new Chip[2, 3];

        static readonly Color Short = new Color(1f, 0.45f, 0.38f);

        void PlaceWorkerFigures(Camera cam, Vector2 panel, float scale)
        {
            for (int side = 0; side < 2; side++)
                for (int z = 0; z < 3; z++)
                {
                    var who = (Side)side;
                    var zone = (WorkerZone)z;
                    var chip = _workers[side, z] ?? (_workers[side, z] = Ensure(-1 - (side * 3 + z)));
                    _seen.Add(-1 - (side * 3 + z));

                    var row = _match.WorkerRow(who, zone);
                    Vector2 a, b;
                    if (!_hand.TryProject(cam, _match.Board.WorldOf(new CellRef(row, 0)), out a)
                        || !_hand.TryProject(cam,
                               _match.Board.WorldOf(new CellRef(row, Board.Columns - 1)), out b))
                    {
                        chip.Root.style.display = DisplayStyle.None;
                        continue;
                    }

                    float y = (a.y + b.y) * 0.5f;
                    if (y < HudLayout.TopBlockPx * scale || y > panel.y - HudLayout.BottomBlockPx * scale)
                    {
                        chip.Root.style.display = DisplayStyle.None;
                        continue;
                    }

                    bool mine = who == Seat.Local;
                    float pad = Mathf.Max(24f, Mathf.Abs(b.x - a.x) * 0.06f);
                    float x = mine ? Mathf.Min(a.x, b.x) - pad : Mathf.Max(a.x, b.x) + pad;
                    float half = 20f * HudLayout.Scale;
                    x = Mathf.Clamp(x, half, panel.x - half);

                    int n = _match.WorkerFigure(who, zone);

                    chip.Name.style.display = DisplayStyle.None;
                    chip.Line.text = n > 0 ? "⚒+" + n : n < 0 ? "⚒" + n : "⚒0";
                    chip.Line.style.display = DisplayStyle.Flex;
                    chip.Line.style.fontSize = Mathf.Clamp(11f * HudLayout.Scale, 9f, 20f);
                    chip.Line.style.color = n < 0 ? Short : (mine ? You : Foe);

                    chip.Root.style.borderTopWidth = 0; chip.Root.style.borderBottomWidth = 0;
                    chip.Root.style.borderLeftWidth = 0; chip.Root.style.borderRightWidth = 0;
                    chip.Root.style.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.66f);
                    chip.Root.style.width = half * 2f;
                    chip.Root.style.left = x - half;
                    chip.Root.style.top = y - 9f * HudLayout.Scale;
                    chip.Root.style.display = DisplayStyle.Flex;
                }
        }

        void HideWorkerFigures()
        {
            for (int side = 0; side < 2; side++)
                for (int z = 0; z < 3; z++)
                {
                    var chip = _workers[side, z];
                    if (chip != null) chip.Root.style.display = DisplayStyle.None;
                }
        }

        /// <summary>
        /// A chip that cannot be placed is HIDDEN, never destroyed: a unit behind an open wall or
        /// off the near plane would otherwise have its element torn down and rebuilt every frame
        /// it stayed there.
        /// </summary>
        void Place(BoardObject o, CreatureUnit cre, StructureUnit bld, CellRef cell,
                   Camera cam, Vector2 panel, float scale)
        {
            var chip = Ensure(o.Id);

            // BACK to the tile's near edge, and carrying no NAME any more.
            //
            // The name went through both wrong answers in turn. Hung off the front of the tile it
            // was a label floating on the grass, belonging to nothing; moved onto the card's title
            // band it drew straight across the field art, because this is an overlay panel and an
            // overlay cannot sort behind a sprite in the scene. CardPlateLayer prints the name on
            // the plate itself now, at a sorting order under the standee, which is the only place
            // that satisfies both.
            //
            // What is left here is what an overlay is FOR: the target ring, and the two keyword
            // states that change and appear on no card. Those are decisions rather than identity,
            // they are tiny, and they sit on the ground in front of the card where they cross
            // nothing.
            // The tile's near edge is the one nearest THIS player - the guest's camera is yawed a
            // half turn, so a bare -Z anchor hangs their chips off the far edge and lays them
            // across the card instead of on the ground in front of it.
            float depth = _match.Board.CellSize * _match.Board.RowStretch;
            var anchor = _match.Board.WorldOf(cell)
                       + new Vector3(0f, 0f, depth * 0.5f * Seat.TowardCamera);

            Vector2 p, side;
            if (!_hand.TryProject(cam, anchor, out p)
                || !_hand.TryProject(cam, anchor + new Vector3(_match.Board.ColPitch, 0f, 0f), out side))
            {
                chip.Root.style.display = DisplayStyle.None;
                return;
            }

            float cellW = Mathf.Max(26f, Mathf.Abs(side.x - p.x));

            // behind a wall is behind a wall - the board is framed clear of the rails, so this
            // only ever fires while one is open
            if (p.y < HudLayout.TopBlockPx * scale || p.y > panel.y - HudLayout.BottomBlockPx * scale)
            {
                chip.Root.style.display = DisplayStyle.None;
                return;
            }

            bool mine = o.Owner == Seat.Local;
            // what your selected attacker may hit - or, with an attack already aimed and nothing
            // selected, what the whole group is aimed AT
            bool target = Has(_input != null ? _input.LegalAttacks : null, cell)
                       || (_match.AssaultCell.HasValue && _match.AssaultCell.Value == cell);

            // ... and who may still pile into it. The board lights those cells too, and on a board
            // where the card covers the whole tile that light is UNDER the card - so the ring here
            // is the only one anybody sees.
            bool joining = !target && Has(_input != null ? _input.Joiners : null, cell);

            // ...and, on the other side of the exchange, who YOU have put in front of the blow.
            // It outranks both: while a blocker choice is parked nothing else on the board is
            // being asked about, and the ring is the only mark a card standing behind its own
            // figure can carry.
            bool defending = _hud != null && _hud.IsDefending(cell);

            // the floor and ceiling scale, or they become the dominant term on a big display
            float px = HudLayout.Scale;
            float font = Mathf.Clamp(cellW * 0.19f, 8f * px, 15f * px);

            // The chip is a STATUS marker now, not a nameplate. It draws at all only when it has
            // something to say that no card carries: a ring for what your attacker may hit or
            // join or what you are blocking with, and a keyword state that is mid-change.
            string extra = cre != null ? Extra(cre) : "";
            if (defending) extra = "BLOCK";                 // plain ASCII: no glyph gate to clear
            if (!target && !joining && !defending && extra.Length == 0)
            {
                chip.Root.style.display = DisplayStyle.None;
                return;
            }

            chip.Name.style.display = DisplayStyle.None;

            chip.Line.text = extra;
            chip.Line.style.display = extra.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            chip.Line.style.fontSize = font;
            chip.Line.style.color = defending ? BlockGreen
                                  : target ? TargetRed
                                  : joining ? JoinGold : (mine ? You : Foe);

            // a target wears the red ring, a creature that may join it a gold one and a committed
            // blocker a green one; nothing else draws a border, so none can be mistaken for
            // anything
            float border = target || joining || defending ? 1.5f : 0f;
            var ring = defending ? BlockGreen : target ? TargetRed : JoinGold;
            chip.Root.style.borderTopWidth = border; chip.Root.style.borderBottomWidth = border;
            chip.Root.style.borderLeftWidth = border; chip.Root.style.borderRightWidth = border;
            chip.Root.style.borderTopColor = ring; chip.Root.style.borderBottomColor = ring;
            chip.Root.style.borderLeftColor = ring; chip.Root.style.borderRightColor = ring;
            chip.Root.style.backgroundColor = defending ? new Color(0.03f, 0.20f, 0.09f, 0.86f)
                                            : target ? new Color(0.28f, 0.04f, 0.04f, 0.82f)
                                            : joining ? new Color(0.24f, 0.16f, 0.02f, 0.82f)
                                                      : new Color(0.02f, 0.02f, 0.04f, 0.62f);

            chip.Root.style.width = cellW;
            chip.Root.style.left = p.x - cellW * 0.5f;
            chip.Root.style.top = p.y + 2f;
            chip.Root.style.display = DisplayStyle.Flex;
        }

        /// <summary>IReadOnlyList has no Contains, and LINQ on a per-unit per-frame path is a
        /// per-unit per-frame allocation.</summary>
        static bool Has(IReadOnlyList<CellRef> cells, CellRef cell)
        {
            if (cells == null) return false;
            for (int i = 0; i < cells.Count; i++) if (cells[i] == cell) return true;
            return false;
        }

        static string Name(CreatureUnit cre, StructureUnit bld)
        {
            if (cre != null) return cre.Name;
            if (bld == null) return "";
            return string.IsNullOrEmpty(bld.Name) ? bld.DefId.Value : bld.Name;
        }

        /// <summary>
        /// The two keyword states that CHANGE. A cocoon's progress and a banked discharge are
        /// decisions a player makes; every other keyword is printed on the card, and the mana
        /// banked on a card is a badge ON that card, so neither is repeated here.
        /// </summary>
        static string Extra(CreatureUnit c)
        {
            if (c.Keyword == Keyword.Chrysalis)
                return c.ChrysalisCount + "/" + (c.Hatch > 0 ? c.Hatch : 3);
            if (c.Keyword == Keyword.Overcharge && c.OverchargeBank > 0)
                return "◆" + c.OverchargeBank;
            return "";
        }

        Chip Ensure(int id)
        {
            Chip c;
            if (_live.TryGetValue(id, out c)) return c;

            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.alignItems = Align.Center;
            root.style.paddingTop = 1; root.style.paddingBottom = 2;
            root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
            root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;
            root.style.overflow = Overflow.Hidden;
            _layer.Add(root);

            var name = Fx.CombatTheatre.NewLabel(UiFont.DisplayBold, 10f);
            name.style.whiteSpace = WhiteSpace.NoWrap;
            root.Add(name);

            var line = Fx.CombatTheatre.NewLabel(UiFont.DisplayBlack, 12f);
            line.style.whiteSpace = WhiteSpace.NoWrap;
            root.Add(line);

            c = new Chip { Root = root, Name = name, Line = line };
            _live[id] = c;
            return c;
        }

        void Prune()
        {
            _dead.Clear();
            foreach (var kv in _live)
                if (!_seen.Contains(kv.Key)) _dead.Add(kv.Key);

            for (int i = 0; i < _dead.Count; i++)
            {
                var c = _live[_dead[i]];
                if (c.Root != null && c.Root.parent != null) c.Root.RemoveFromHierarchy();
                _live.Remove(_dead[i]);
            }
        }
    }
}
