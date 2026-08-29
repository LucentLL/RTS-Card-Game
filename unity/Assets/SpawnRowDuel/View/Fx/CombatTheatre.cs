using System.Collections.Generic;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Fx
{
    /// <summary>
    /// The battle cut-in and the damage numbers - "I don't see attacks when they happen", answered
    /// the way the reference build answers it (`#battleView`, 22_fx_wrappers.js:29 + spec 09 §18).
    ///
    /// A whole combat resolves inside ONE `Apply`: declarations, blockers, both damage tiers,
    /// retaliation and the death sweep all land in the same frame, and the only trace left on the
    /// board is that something is missing from it. So the fight is replayed here, from the events,
    /// as a DS-Yu-Gi-Oh cut-in: the attacker's card flies in from the left, the defender's from the
    /// right, ⚔ lands between them, and each card shows what it hit for and what it has left.
    ///
    /// It fires on the RESOLUTION, not the declaration. Combat v3 declares first and resolves
    /// later - the attacker taps, the defender may interpose, and nothing is decided until the
    /// rail's Rocket button runs ResolveCombatCommand - so a cut-in at declaration time is a
    /// picture of two cards that have not fought yet, and the fight itself would then happen off
    /// screen. Each declaration is recorded as it is made and TOLD when its damage lands, one
    /// after another, which is also the order the resolver applies them in.
    ///
    /// Two problems make this less obvious than it sounds:
    ///
    /// 1. **The events arrive after the fact.** By the time `AttackDeclared` is drained the loser is
    ///    already off the board, so nothing that reads GameState can draw it. A one-frame-old
    ///    SNAPSHOT of every unit is therefore kept - name, statline, art, cell - and refreshed in
    ///    LateUpdate, after the frame's events have been pumped. What the cut-in draws is the board
    ///    as it was before the blow.
    /// 2. **The AI would talk over it.** `AiDriver` pumps a command every 0.35 s, so the opponent's
    ///    next summon lands while the clash is still on screen. The theatre HOLDS the autopilot for
    ///    exactly as long as a cut-in runs (MatchController.Hold) and never holds your own input.
    ///
    /// It is drawn BIG - a card is about three tenths of the screen wide, or as much as fits its
    /// height - because the cut-in's whole job is to be looked at, and at the old 132 px cap it was
    /// a pair of postage stamps in the middle of a board it was supposed to interrupt.
    ///
    /// A JOINT attack is told as one picture, not as three: every declaration against the same
    /// defender is stacked on the left, fanned so each card's name and blow still shows, against
    /// the one card they are all hitting. Which is attacking which needs no arrows in that shape -
    /// everything on the left is attacking the thing on the right - and three cut-ins in a row for
    /// one decision is exactly the thing MaxToldPerCombat exists to stop.
    ///
    /// Damage numbers are separate from the cut-in on purpose: a Bolt, a Cannon Tower's upkeep
    /// shot and a Backlash are not battles and get no cut-in, but they are all still damage, and
    /// "-150" floating off the thing that took it is the one witness they have.
    /// </summary>
    public sealed class CombatTheatre : MonoBehaviour
    {
        /// <summary>Off leaves the numbers and drops the cut-in - `srd.cutins` in the browser.</summary>
        public static bool CutIns = true;

        const float CutInSeconds = 1.45f;
        const float SlideSeconds = 0.22f;         // the fly-in, front-loaded like the CSS keyframes
        const float FadeSeconds = 0.18f;
        const float FloatSeconds = 0.95f;
        const float FloatRisePx = 46f;

        MatchController _match;
        BoardInput _input;
        HandBar _hand;
        ElementPalette _palette;

        // ── the one-frame-old board ────────────────────────────────────────────────────────

        /// <summary>What a unit was, before the events now being drained happened to it.</summary>
        struct Snap
        {
            public int Id;
            public string Name;
            public Element Color;
            public int Attack, Hp, MaxHp;
            public bool Structure, Wall;
            public CellRef At;
            public Side Owner;
            public Sprite Art;
            public string Lead;                   // "⚔300" / "◆+2" / "⌂"
            public float SeenAt;                  // when it was last on the board
        }

        readonly Dictionary<int, Snap> _snaps = new Dictionary<int, Snap>(64);
        readonly List<int> _snapDead = new List<int>();

        // ── the fight being told ───────────────────────────────────────────────────────────

        sealed class Fight
        {
            public int Index;                     // its DeclarationIndex, so blockers find it again
            public bool Queued;
            public Snap Attacker;
            public Snap Defender;
            public bool HasDefender;
            public int DamageToDefender, DamageToAttacker;
            public bool AttackerDied, DefenderDied;
            public bool DirectHit;                // the wall, not a unit
        }

        /// <summary>This combat's declarations, in declaration order - which is the order the
        /// resolver works through them, and so the order the cut-ins play in.</summary>
        readonly List<Fight> _fights = new List<Fight>();

        /// <summary>Fights whose damage has landed and which have not been told yet.</summary>
        readonly List<Fight> _queue = new List<Fight>();

        Fight _showing;

        /// <summary>The fights being told AS ONE: a joint attack, in declaration order, sharing
        /// the defender <see cref="_showing"/> names.</summary>
        readonly List<Fight> _group = new List<Fight>();

        /// <summary>How many attackers one cut-in will stack. Past three the fan is a smear and
        /// the cards are too narrow to read a name off.</summary>
        const int MaxStack = 3;

        float _shownAt = -99f;
        int _told;
        bool _rebind;

        /// <summary>
        /// How many clashes one combat may stop the game for. An alpha strike of six declarations
        /// is one decision, not six, and nine seconds of cut-ins is a punishment for making it.
        /// Past this the fights still resolve and still throw their damage numbers - they just do
        /// not each get a card flying in.
        /// </summary>
        const int MaxToldPerCombat = 3;

        /// <summary>How long a unit that has left the board can still be drawn.</summary>
        const float GraveMemory = 12f;

        // ── surfaces ───────────────────────────────────────────────────────────────────────

        VisualElement _cutIn, _inner, _left, _right;
        Label _clash;
        BattleCard _rightCard;
        readonly List<BattleCard> _leftCards = new List<BattleCard>();

        VisualElement _floatLayer;
        readonly List<Floater> _floaters = new List<Floater>();

        sealed class Floater
        {
            public Label Label;
            public Vector3 World;
            public float Born;
            public bool Live;
        }

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
            _hand = GetComponent<HandBar>();
        }

        void OnEnable()
        {
            if (_match != null) _match.Observed += Observe;
        }

        void OnDisable()
        {
            if (_match != null) _match.Observed -= Observe;
        }

        // ── reading the fight out of the events ────────────────────────────────────────────

        void Observe(GameEvent ev)
        {
            var declared = ev as AttackDeclared;
            if (declared != null)
            {
                // Declaration 0 is a FRESH combat: the resolver clears its list when it finishes,
                // so the index restarting is the only signal that a new one has begun.
                if (declared.DeclarationIndex == 0) { _fights.Clear(); _told = 0; }

                var f = new Fight { Index = declared.DeclarationIndex };
                if (!TrySnap(declared.AttackerId, out f.Attacker)) return;

                var unit = declared.Target as UnitTarget;
                if (unit != null) f.HasDefender = TrySnap(unit.UnitId, out f.Defender);

                var wall = declared.Target as WallTarget;
                if (wall != null)
                {
                    f.Defender = WallSnap(wall.Defender);
                    f.HasDefender = true;
                    f.DirectHit = true;
                }

                _fights.Add(f);
                return;
            }

            // A blocker interposing is the fight the player actually needs to see: the card that
            // was declared at is no longer the card being hit.
            var blocked = ev as BlockersAssigned;
            if (blocked != null && blocked.BlockerIds.Length > 0)
            {
                var f = ByIndex(blocked.DeclarationIndex);
                Snap blocker;
                if (f != null && TrySnap(blocked.BlockerIds[0], out blocker))
                {
                    f.Defender = blocker;
                    f.HasDefender = true;
                    f.DirectHit = false;
                }
                return;
            }

            var dmg = ev as DamageApplied;
            if (dmg != null)
            {
                Snap s;
                if (TrySnap(dmg.TargetId, out s)) Pop(s.At, "-" + Stat.Show(dmg.Amount), Hurt);
                Record(dmg.TargetId, dmg.Amount);
                return;
            }

            var wallHit = ev as WallStruck;
            if (wallHit != null)
            {
                Pop(WallWorld(wallHit.Defender), "-" + Stat.Show(wallHit.Amount), Hurt);

                // Wall damage is AGGREGATED over the whole combat and applied once, so the total
                // is told by whichever declaration aimed at that wall first - but every attacker
                // that aimed there is part of the picture, so they all resolve and the group
                // stacks them.
                bool first = true;
                for (int i = 0; i < _fights.Count; i++)
                {
                    var f = _fights[i];
                    if (!f.HasDefender || !f.Defender.Wall || f.Defender.Owner != wallHit.Defender)
                        continue;
                    if (first) { f.DamageToDefender += wallHit.Amount; first = false; }
                    Resolved(f);
                }
                return;
            }

            var dead = ev as UnitDestroyed;
            if (dead != null)
            {
                for (int i = 0; i < _fights.Count; i++)
                {
                    var f = _fights[i];
                    if (f.Attacker.Id == dead.UnitId) { f.AttackerDied = true; Resolved(f); }
                    if (f.HasDefender && f.Defender.Id == dead.UnitId)
                    { f.DefenderDied = true; Resolved(f); }
                }
                return;
            }
        }

        Fight ByIndex(int index)
        {
            for (int i = 0; i < _fights.Count; i++) if (_fights[i].Index == index) return _fights[i];
            return null;
        }

        /// <summary>
        /// This fight has actually happened to somebody. Queue it once; if it is already on
        /// screen, re-read its numbers instead, so a second damage tier lands on the cut-in in
        /// flight rather than restarting it.
        /// </summary>
        void Resolved(Fight f)
        {
            if (f == null) return;
            if (_group.Contains(f)) { _rebind = true; return; }   // FIRST: a shown fight stays Queued
            if (f.Queued) return;
            f.Queued = true;
            _queue.Add(f);
        }

        void Record(int unitId, int amount)
        {
            if (amount <= 0) return;
            for (int i = 0; i < _fights.Count; i++)
            {
                var f = _fights[i];
                if (f.Attacker.Id == unitId) { f.DamageToAttacker += amount; Resolved(f); return; }
                if (f.HasDefender && f.Defender.Id == unitId)
                { f.DamageToDefender += amount; Resolved(f); return; }
            }
        }

        // ── frame ──────────────────────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _hand == null) return;
            if (_palette == null) _palette = new ElementPalette(_match.Engine.Catalog);

            EnsureSurfaces();

            if (_showing == null && _queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                if (CutIns && _told < MaxToldPerCombat) { Show(next); _told++; }
                else Regroup(next);              // still claim its partners, so they do not queue
            }
            else if (_rebind && _showing != null)
            {
                BindCards();
            }
            _rebind = false;

            AnimateCutIn();
            AnimateFloaters();
            Resnap();                              // LAST: the snapshot is the board BEFORE the next batch
        }

        /// <summary>
        /// The board as it stands at the end of this frame. Taken after the events have been
        /// pumped, so when the next batch arrives this is the state they are about to change -
        /// which is the only version of a dead creature anyone can still draw.
        /// </summary>
        void Resnap()
        {
            var s = _match.Engine.State;
            _snapDead.Clear();
            foreach (var kv in _snaps) _snapDead.Add(kv.Key);

            foreach (var kv in s.Objects())
            {
                var o = kv.Value;
                var cre = o as CreatureUnit;
                var bld = o as StructureUnit;
                if (cre == null && bld == null) continue;         // a face-down card keeps its secret

                var def = _match.DefOfObject(o);
                var snap = new Snap
                {
                    Id = o.Id,
                    Name = cre != null ? cre.Name : bld.Name,
                    Color = o.Color,
                    Attack = cre != null ? cre.EffectiveAttack : 0,
                    Hp = cre != null ? cre.Hp : bld.Hp,
                    MaxHp = cre != null ? cre.MaxHp : bld.MaxHp,
                    Structure = bld != null,
                    At = kv.Key,
                    Owner = o.Owner,
                    Art = def != null ? def.CardArt : null,
                    Lead = cre != null ? Stat.Atk(cre.EffectiveAttack) : StructureLead(bld),
                    SeenAt = Time.unscaledTime,
                };
                if (string.IsNullOrEmpty(snap.Name) && bld != null) snap.Name = bld.DefId.Value;
                _snaps[o.Id] = snap;
                _snapDead.Remove(o.Id);
            }

            // A unit that has left the board keeps its last snapshot for a while. It is the only
            // version of a dead creature anything can still draw, and the cut-in that needs it
            // may not have been told yet - it can be queued behind another fight, or waiting on
            // the resolve of a combat declared several seconds ago.
            for (int i = 0; i < _snapDead.Count; i++)
            {
                int id = _snapDead[i];
                Snap gone;
                if (InFlight(id)) continue;
                if (_snaps.TryGetValue(id, out gone)
                    && Time.unscaledTime - gone.SeenAt < GraveMemory) continue;
                _snaps.Remove(id);
            }
        }

        bool InFlight(int unitId)
        {
            for (int i = 0; i < _fights.Count; i++)
            {
                var f = _fights[i];
                if (f.Attacker.Id == unitId) return true;
                if (f.HasDefender && f.Defender.Id == unitId) return true;
            }
            return false;
        }

        static string StructureLead(StructureUnit b)
        {
            if (b == null) return "⌂";
            switch (b.Effect)
            {
                case StructEffect.Mana: return "◆+" + b.Value;
                case StructEffect.Vault: return "◆" + b.Value;
                case StructEffect.Damage: return Stat.Atk(b.Value);
                default: return "⌂";
            }
        }

        bool TrySnap(int unitId, out Snap snap)
        {
            if (_snaps.TryGetValue(unitId, out snap)) return true;

            // never seen: read it live, which is right for anything that has not been hit yet
            CellRef at;
            bool onBoard;
            var o = _match.Engine.State.FindById(unitId, out at, out onBoard);
            var cre = o as CreatureUnit;
            var bld = o as StructureUnit;
            if (o == null || (cre == null && bld == null) || (cre != null && cre.IsWorker)) return false;

            var def = _match.DefOfObject(o);
            snap = new Snap
            {
                Id = o.Id,
                Name = cre != null ? cre.Name : bld.Name,
                Color = o.Color,
                Attack = cre != null ? cre.EffectiveAttack : 0,
                Hp = cre != null ? cre.Hp : bld.Hp,
                MaxHp = cre != null ? cre.MaxHp : bld.MaxHp,
                Structure = bld != null,
                At = at,
                Owner = o.Owner,
                Art = def != null ? def.CardArt : null,
                Lead = cre != null ? Stat.Atk(cre.EffectiveAttack) : StructureLead(bld),
            };
            return true;
        }

        Snap WallSnap(Side defender)
        {
            var p = _match.Engine.State.P(defender);
            return new Snap
            {
                Id = -1 - (int)defender,
                Name = defender == Side.You ? "Your Wall" : "Their Wall",
                Color = p.PrimaryColor,
                Attack = 0,
                Hp = p.Life,
                MaxHp = Mathf.Max(p.Life, 1),
                Wall = true,
                Owner = defender,
                At = new CellRef(Board.RowFor(defender, SlotName.Back), Board.Columns / 2),
                Lead = "▣",
            };
        }

        Vector3 WallWorld(Side defender)
        {
            var row = Board.RowFor(defender, SlotName.Back);
            return _match.Board.WorldOf(new CellRef(row, Board.Columns / 2));
        }

        // ── the cut-in ─────────────────────────────────────────────────────────────────────

        void Show(Fight f)
        {
            if (!f.HasDefender) return;             // nothing to hold up against the attacker

            _showing = f;
            _shownAt = Time.unscaledTime;

            Regroup(f);
            BindCards();
            _cutIn.style.display = DisplayStyle.Flex;

            // hold the opponent for as long as this runs - and no longer
            MatchController.Hold(CutInSeconds);
        }

        /// <summary>
        /// Everything declared against the same defender, told together.
        ///
        /// It reads `_fights`, not the queue, and has to: in a joint attack only ONE fight is ever
        /// resolved by name - `Record` attributes every blow the defender takes to the first
        /// declaration that named it, which is what makes that card show the TOTAL - so the other
        /// attackers are sitting in the list having done everything and been told nothing. They
        /// are claimed here, marked Queued so they cannot come back as a second cut-in of the same
        /// fight, and drawn as the stack.
        /// </summary>
        void Regroup(Fight primary)
        {
            _group.Clear();
            _group.Add(primary);
            if (!primary.HasDefender) return;

            for (int i = 0; i < _fights.Count && _group.Count < MaxStack; i++)
            {
                var f = _fights[i];
                if (f == primary || !f.HasDefender) continue;
                if (f.Defender.Id != primary.Defender.Id) continue;
                if (f.Queued && !_queue.Contains(f)) continue;      // already told, on its own
                _queue.Remove(f);
                f.Queued = true;
                _group.Add(f);
            }

            _group.Sort((a, b) => a.Index.CompareTo(b.Index));      // declaration order
        }

        /// <summary>
        /// How wide one card is. Both axes matter: the pair has to fit across the screen AND
        /// inside its height, and a card is 1.39 times taller than it is wide, so on any landscape
        /// screen it is the HEIGHT that decides. The floor is what a phone in portrait gets.
        /// </summary>
        float CardWidth()
        {
            var panel = _hand.PanelSize();
            return Mathf.Max(64f, Mathf.Min(panel.x * 0.31f, panel.y * 0.72f / CardFace.Aspect));
        }

        /// <summary>Two cards fan into the width of about one and a half; three into two.</summary>
        static float StackScale(int n) { return n <= 1 ? 1f : n == 2 ? 0.84f : 0.72f; }

        void BindCards()
        {
            float cardW = CardWidth();
            int n = Mathf.Max(1, _group.Count);
            float stackW = cardW * StackScale(n);

            // Rebuilt in REVERSE so the first declaration is the last child: UI Toolkit draws in
            // child order, and the card the fight is named after has to be the one on top of the
            // fan - which is also the one nearest the clash.
            _left.Clear();
            for (int i = n - 1; i >= 0; i--)
            {
                var card = LeftCard(i);
                card.Bind(Model(_group[i].Attacker, _group[i].DamageToAttacker,
                                _group[i].AttackerDied), _palette, stackW);

                card.style.marginLeft = i == n - 1 ? 0f : -stackW * 0.36f;
                card.style.translate = new Translate(0f, i * stackW * 0.05f);
                card.style.rotate = new Rotate(new Angle(-3.5f * i, AngleUnit.Degree));
                _left.Add(card);
            }

            // the defender takes the sum, which is where every blow was attributed anyway, and
            // dies if any declaration in the group killed it
            int damage = 0;
            bool died = false;
            for (int i = 0; i < _group.Count; i++)
            {
                damage += _group[i].DamageToDefender;
                died |= _group[i].DefenderDied;
            }
            _rightCard.Bind(Model(_showing.Defender, damage, died), _palette, cardW);

            _clash.text = "⚔";
            _clash.style.fontSize = cardW * 0.42f;
            _clash.style.marginLeft = cardW * 0.10f;
            _clash.style.marginRight = cardW * 0.10f;
        }

        BattleCard LeftCard(int i)
        {
            while (_leftCards.Count <= i) _leftCards.Add(new BattleCard());
            return _leftCards[i];
        }

        BattleCard.Model Model(Snap s, int damage, bool died)
        {
            return new BattleCard.Model
            {
                Name = s.Name,
                Lead = s.Lead,
                Hp = s.Hp,
                HpAfter = Mathf.Max(0, s.Hp - damage),
                MaxHp = Mathf.Max(1, s.MaxHp),
                Damage = damage,
                Died = died,
                Element = s.Color,
                Art = s.Art,
                Foe = s.Owner == Side.Foe,
                Wall = s.Wall,
                Structure = s.Structure,
            };
        }

        void AnimateCutIn()
        {
            if (_showing == null) { _cutIn.style.display = DisplayStyle.None; return; }

            float age = Time.unscaledTime - _shownAt;
            if (age > CutInSeconds)
            {
                _showing = null;
                _group.Clear();
                _cutIn.style.display = DisplayStyle.None;
                return;
            }

            // in fast, hold, out fast - the reference's 0 → 14% → 80% → 100% opacity ramp
            float alpha = age < FadeSeconds ? age / FadeSeconds
                        : age > CutInSeconds - FadeSeconds ? (CutInSeconds - age) / FadeSeconds
                        : 1f;
            _cutIn.style.opacity = Mathf.Clamp01(alpha);

            float slide = Mathf.Clamp01(age / SlideSeconds);
            float ease = 1f - (1f - slide) * (1f - slide) * (1f - slide);      // out-cubic
            float off = (1f - ease) * CardWidth() * 0.55f;
            _left.style.translate = new Translate(-off, 0f);
            _right.style.translate = new Translate(off, 0f);

            // the ⚔ lands after the cards, overshoots, settles
            float punch = Mathf.Clamp01((age - SlideSeconds * 0.5f) / 0.28f);
            float scale = punch <= 0f ? 0f
                        : punch < 0.6f ? Mathf.Lerp(0.2f, 1.35f, punch / 0.6f)
                        : Mathf.Lerp(1.35f, 1f, (punch - 0.6f) / 0.4f);
            _clash.style.scale = new Scale(new Vector3(scale, scale, 1f));

            // the numbers land WITH the clash, not with the fly-in: the cards read as they were,
            // then the blow happens
            bool hit = age >= SlideSeconds * 0.5f + 0.12f;
            for (int i = 0; i < _group.Count && i < _leftCards.Count; i++)
                _leftCards[i].ShowResult(hit);
            _rightCard.ShowResult(hit);
        }

        // ── damage numbers ─────────────────────────────────────────────────────────────────

        static readonly Color Hurt = new Color(1f, 0.42f, 0.34f);

        struct Popped
        {
            public Vector3 World;
            public string Text;
            public Color Color;
        }

        /// <summary>
        /// Asked for, not yet built. Events are drained the moment a command lands - which for a
        /// tap on the turn rail is inside OnGUI - and building a VisualElement from there is not
        /// somewhere this layer should be finding out whether it can. The numbers are recorded
        /// here and the elements are made in LateUpdate, where every other surface is made.
        /// </summary>
        readonly List<Popped> _popped = new List<Popped>();

        void Pop(CellRef at, string text, Color color)
        {
            if (_match.Board == null) return;
            Pop(_match.Board.WorldOf(at), text, color);
        }

        void Pop(Vector3 world, string text, Color color)
        {
            if (_popped.Count > 24) return;                 // a runaway resolution is still one frame
            _popped.Add(new Popped
            {
                World = world + new Vector3(0f, 0.6f, 0f),
                Text = text,
                Color = color,
            });
        }

        void SpawnPopped()
        {
            if (_popped.Count == 0 || _floatLayer == null) return;

            for (int n = 0; n < _popped.Count; n++)
            {
                Floater f = null;
                for (int i = 0; i < _floaters.Count; i++)
                    if (!_floaters[i].Live) { f = _floaters[i]; break; }

                if (f == null)
                {
                    f = new Floater { Label = NewLabel(UiFont.DisplayBlack, 22f) };
                    f.Label.style.position = Position.Absolute;
                    _floatLayer.Add(f.Label);
                    _floaters.Add(f);
                }

                f.Live = true;
                f.Born = Time.unscaledTime;
                f.World = _popped[n].World;
                f.Label.text = _popped[n].Text;
                f.Label.style.color = _popped[n].Color;
                f.Label.style.fontSize = 22f * HudLayout.Scale;
                f.Label.style.display = DisplayStyle.Flex;
            }
            _popped.Clear();
        }

        void AnimateFloaters()
        {
            SpawnPopped();

            var cam = _input != null && _input.Cam != null ? _input.Cam : Camera.main;

            for (int i = 0; i < _floaters.Count; i++)
            {
                var f = _floaters[i];
                if (!f.Live) continue;

                float age = Time.unscaledTime - f.Born;
                if (age > FloatSeconds || cam == null)
                {
                    f.Live = false;
                    f.Label.style.display = DisplayStyle.None;
                    continue;
                }

                Vector2 p;
                if (!_hand.TryProject(cam, f.World, out p))
                {
                    f.Live = false;
                    f.Label.style.display = DisplayStyle.None;
                    continue;
                }

                float t = age / FloatSeconds;
                f.Label.style.left = p.x - 30f;
                f.Label.style.top = p.y - t * FloatRisePx * HudLayout.Scale - 12f;
                f.Label.style.opacity = t < 0.7f ? 1f : (1f - t) / 0.3f;
            }
        }

        // ── surfaces ───────────────────────────────────────────────────────────────────────

        void EnsureSurfaces()
        {
            if (_cutIn != null || _hand == null || !_hand.PanelReady) return;

            _floatLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _floatLayer.style.position = Position.Absolute;
            _floatLayer.style.left = 0; _floatLayer.style.right = 0;
            _floatLayer.style.top = 0; _floatLayer.style.bottom = 0;
            _hand.BoardLayer.Add(_floatLayer);

            _cutIn = new VisualElement { pickingMode = PickingMode.Ignore };
            _cutIn.style.position = Position.Absolute;
            _cutIn.style.left = 0; _cutIn.style.right = 0;
            _cutIn.style.top = 0; _cutIn.style.bottom = 0;
            _cutIn.style.alignItems = Align.Center;
            _cutIn.style.justifyContent = Justify.Center;
            _cutIn.style.display = DisplayStyle.None;
            _hand.OverlayLayer.Add(_cutIn);

            _inner = new VisualElement { pickingMode = PickingMode.Ignore };
            _inner.style.flexDirection = FlexDirection.Row;
            _inner.style.alignItems = Align.Center;
            _inner.style.backgroundColor = new Color(0.031f, 0.023f, 0.055f, 0.74f);
            _inner.style.paddingTop = 14; _inner.style.paddingBottom = 14;
            _inner.style.paddingLeft = 22; _inner.style.paddingRight = 22;
            _inner.style.borderTopLeftRadius = 16; _inner.style.borderTopRightRadius = 16;
            _inner.style.borderBottomLeftRadius = 16; _inner.style.borderBottomRightRadius = 16;
            _cutIn.Add(_inner);

            _left = new VisualElement { pickingMode = PickingMode.Ignore };
            _left.style.flexDirection = FlexDirection.Row;
            _left.style.alignItems = Align.Center;
            _inner.Add(_left);

            _clash = NewLabel(UiFont.DisplayBlack, 34f);
            _clash.text = "⚔";
            _clash.style.color = new Color(0.85f, 0.69f, 0.29f);
            _clash.style.marginLeft = 18; _clash.style.marginRight = 18;
            _inner.Add(_clash);

            _right = new VisualElement { pickingMode = PickingMode.Ignore };
            _rightCard = new BattleCard();
            _right.Add(_rightCard);
            _inner.Add(_right);
        }

        internal static Label NewLabel(UiFont face, float size)
        {
            var l = new Label("") { pickingMode = PickingMode.Ignore };
            var font = ViewAssets.Font(face);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            l.style.fontSize = size;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.textShadow = new TextShadow
            {
                offset = new Vector2(0f, 2f),
                blurRadius = 3f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            return l;
        }
    }
}
