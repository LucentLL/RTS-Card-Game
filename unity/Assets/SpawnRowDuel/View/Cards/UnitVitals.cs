using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// What every unit on the board has left, drawn under its own tile.
    ///
    /// This replaces the IMGUI overlay, and it replaces it for three reasons that all came out of
    /// the same complaint - "I can't see the health of buildings I am attacking":
    ///
    /// 1. **It was in the wrong place.** The label floated 1.45 cells ABOVE the unit's cell, which
    ///    under the tilt is most of a row further up the screen - so the foe's back row put its
    ///    numbers behind the castle wall, and the overlay's own answer to that was to DROP them
    ///    (`if (y < TopH + 2) continue`). The row you attack into is the row whose numbers went
    ///    missing. They hang off the tile's NEAR edge now, which is inside the board at every row.
    /// 2. **IMGUI has no ♥.** OnGUI draws with the built-in font, which carries no ♥, ◆ or ⚔, and
    ///    silently drops them - the reason the old overlay reads "6 hp" rather than "♥6". This is
    ///    a UI Toolkit surface on the gated font chain, so the glyphs are the same ones the cards
    ///    and the wall rails use.
    /// 3. **A number is not a quantity.** A structure at 250 of 300 and one at 30 of 300 both read
    ///    as three digits. The bar under the line is the thing that answers "can I kill it".
    ///
    /// It also has a state the old overlay could not have: a unit your selected attacker may
    /// legally hit is marked as a TARGET, straight off BoardInput's engine-probed list. Aiming at
    /// something and reading what it has left is one glance now instead of two.
    /// </summary>
    public sealed class UnitVitals : MonoBehaviour
    {
        public static bool Enabled = true;

        MatchController _match;
        BoardInput _input;
        HandBar _hand;

        VisualElement _layer;
        readonly Dictionary<int, Chip> _live = new Dictionary<int, Chip>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _dead = new List<int>();

        static readonly Color You = new Color(1f, 0.90f, 0.55f);
        static readonly Color Foe = new Color(0.65f, 0.80f, 1f);
        static readonly Color TargetRed = new Color(1f, 0.36f, 0.30f);

        sealed class Chip
        {
            public VisualElement Root;
            public Label Name;
            public Label Line;
            public VisualElement Bar;
            public VisualElement Fill;
        }

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
            _hand = GetComponent<HandBar>();
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _match.Board == null) return;
            if (_hand == null || !_hand.PanelReady) return;

            if (_layer == null)
            {
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

                    if (Place(o, cre, bld, kv.Key, cam, panel, scale)) _seen.Add(o.Id);
                }
            }

            Prune();
        }

        bool Place(BoardObject o, CreatureUnit cre, StructureUnit bld, CellRef cell,
                   Camera cam, Vector2 panel, float scale)
        {
            // the tile's NEAR edge: the chip hangs off the front of the card, where there is
            // always board left to hang it on - even in the foe's back row
            float depth = _match.Board.CellSize * _match.Board.RowStretch;
            var anchor = _match.Board.WorldOf(cell) - new Vector3(0f, 0f, depth * 0.5f);

            Vector2 p, side;
            if (!_hand.TryProject(cam, anchor, out p)) return false;
            if (!_hand.TryProject(cam, anchor + new Vector3(_match.Board.ColPitch, 0f, 0f), out side))
                return false;

            float cellW = Mathf.Max(26f, Mathf.Abs(side.x - p.x));

            // behind a wall is behind a wall - the board is framed clear of the rails, so this
            // only ever fires while one is open
            if (p.y < HudLayout.TopBlockPx * scale || p.y > panel.y - HudLayout.BottomBlockPx * scale)
                return false;

            var chip = Ensure(o.Id);
            bool mine = o.Owner == Side.You;
            bool target = Has(_input != null ? _input.LegalAttacks : null, cell);

            int hp = cre != null ? cre.Hp : bld.Hp;
            int maxHp = Mathf.Max(1, cre != null ? cre.MaxHp : bld.MaxHp);

            float font = Mathf.Clamp(cellW * 0.19f, 8f, 15f);
            bool roomy = cellW >= 52f;

            chip.Name.text = roomy ? Name(cre, bld) : "";
            chip.Name.style.display = roomy ? DisplayStyle.Flex : DisplayStyle.None;
            chip.Name.style.fontSize = font * 0.86f;
            chip.Name.style.color = target ? TargetRed : (mine ? You : Foe);

            chip.Line.text = cre != null
                ? Stat.Atk(cre.EffectiveAttack) + " " + Stat.Hp(hp) + Extra(cre)
                : Stat.Hp(hp);
            chip.Line.style.fontSize = font;
            chip.Line.style.color = hp * 4 <= maxHp ? new Color(1f, 0.55f, 0.45f)
                                                    : (mine ? You : Foe);

            float frac = Mathf.Clamp01(hp / (float)maxHp);
            chip.Bar.style.height = Mathf.Max(2.5f, font * 0.24f);
            chip.Fill.style.width = Length.Percent(frac * 100f);
            chip.Fill.style.backgroundColor = frac > 0.5f ? new Color(0.45f, 0.92f, 0.55f)
                                            : frac > 0.25f ? new Color(1f, 0.82f, 0.35f)
                                                           : new Color(1f, 0.38f, 0.32f);

            // a target wears the red ring; nothing else draws a border, so it cannot be mistaken
            float border = target ? 1.5f : 0f;
            chip.Root.style.borderTopWidth = border; chip.Root.style.borderBottomWidth = border;
            chip.Root.style.borderLeftWidth = border; chip.Root.style.borderRightWidth = border;
            chip.Root.style.borderTopColor = TargetRed; chip.Root.style.borderBottomColor = TargetRed;
            chip.Root.style.borderLeftColor = TargetRed; chip.Root.style.borderRightColor = TargetRed;
            chip.Root.style.backgroundColor = target
                ? new Color(0.28f, 0.04f, 0.04f, 0.82f)
                : new Color(0.02f, 0.02f, 0.04f, 0.62f);

            chip.Root.style.width = cellW;
            chip.Root.style.left = p.x - cellW * 0.5f;
            chip.Root.style.top = p.y + 2f;
            chip.Root.style.display = DisplayStyle.Flex;
            return true;
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
        /// The two keyword states that CHANGE, appended to the statline. A cocoon's progress and a
        /// banked discharge are decisions a player makes; every other keyword is printed on the
        /// card and does not belong on the board.
        /// </summary>
        static string Extra(CreatureUnit c)
        {
            if (c.Keyword == Keyword.Chrysalis)
                return "  " + c.ChrysalisCount + "/" + (c.Hatch > 0 ? c.Hatch : 3);
            if (c.Keyword == Keyword.Overcharge && c.OverchargeBank > 0)
                return "  ◆" + c.OverchargeBank;
            if (c.Bank > 0) return "  ◆" + c.Bank;
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

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.width = Length.Percent(84f);
            bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            bar.style.marginTop = 1;
            root.Add(bar);

            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100f);
            bar.Add(fill);

            c = new Chip { Root = root, Name = name, Line = line, Bar = bar, Fill = fill };
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
