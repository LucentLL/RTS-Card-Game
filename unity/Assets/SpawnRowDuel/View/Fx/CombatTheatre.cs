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
    /// later - the attacker taps, the defender may interpose, and nothing is decided until
    /// ⚔ ATTACK under the board runs ResolveCombatCommand (and until then ✕ CANCEL can take the
    /// whole assault back again) - so a cut-in at declaration time is a
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

        /// <summary>
        /// Is a cut-in on screen right now? Asked by <see cref="TurnHerald"/>, which draws across
        /// the same middle of the board and waits its turn rather than stacking on top of a fight.
        ///
        /// In solo the two cannot collide - Autopilot holds for the cut-in before it ever reaches
        /// the turn hand-off - but a REMOTE peer's BeginTurn arrives over the wire and knows
        /// nothing about our HoldUntil, so in multiplayer it can land on a running clash.
        /// </summary>
        public bool Busy { get { return _showing != null || _cast != null; } }

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

        /// <summary>
        /// What is UNDER each face-down card, by cell - kept but never drawn until the card turns
        /// over.
        ///
        /// A flip mints a new unit and the fight that provoked it usually kills that unit inside
        /// the same command, so by the time the events are drained there is nothing on the board
        /// to read a face off and nothing in <see cref="_snaps"/> either: the card was a secret
        /// last frame, and secrets are exactly what Resnap refuses to record. This is the one
        /// thing that can still name it.
        ///
        /// Holding it costs no secrecy. Both peers run the same engine, so each already knows
        /// what its own opponent set - the secret is enforced by never PAINTING it, and the only
        /// reader here is OnFlipped, which runs after the card is public.
        /// </summary>
        readonly Dictionary<CellRef, Snap> _hidden = new Dictionary<CellRef, Snap>(16);
        readonly List<CellRef> _hiddenGone = new List<CellRef>();

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

            /// <summary>The cell a UNIT declaration was aimed at, and whether the thing standing
            /// there was still a secret when it was declared at. A face-down has no card to hold
            /// up, so the fight waits for the flip to give it one - see OnFlipped.</summary>
            public CellRef TargetCell;
            public bool AwaitingFlip;
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

        // ── the card being played ──────────────────────────────────────────────────────────

        /// <summary>
        /// A spell or trap resolving, told with the same picture a fight gets: the card that was
        /// played on the left, what it happened TO on the right.
        ///
        /// This exists because a raze is invisible. `Spells.Raze` puts the cell to null and emits
        /// UnitDestroyed and nothing else - no DamageApplied - so the floating number that is
        /// every other effect's witness never fires, and a Cave-In reads on the board as a
        /// structure that stopped existing for no stated reason. The class doc above argues that
        /// spells "are not battles and get no cut-in"; that holds for a Bolt, which at least throws
        /// a number, and does not hold for the three razes.
        /// </summary>
        sealed class Cast
        {
            public Snap Card;                     // the spell itself - synthesised, never on the board
            public Snap Victim;
            public bool HasVictim;
            public int Damage;
            public bool VictimDied;
            public bool Trap;
            public CellRef At;
            public bool Targeted;                 // an untargeted card names itself and nothing else
        }

        /// <summary>
        /// Cards played this batch, waiting for the frame to end so their effect can be read off.
        ///
        /// The two events arrive on OPPOSITE sides of their own effect - SpellResolved is raised
        /// after `SpellEngine.Resolve` (PlayCardHandler), TrapSprung before it (Traps) - so neither
        /// can be bound where it arrives. Both are held here instead and resolved in LateUpdate,
        /// which runs after the whole batch has been pumped and before Resnap replaces the board:
        /// the one moment where the victim is still snapshotted AS IT WAS and everything that
        /// happened to it has already been seen.
        /// </summary>
        readonly List<Cast> _pending = new List<Cast>();
        readonly List<Cast> _castQueue = new List<Cast>();

        /// <summary>What this batch's events did to each unit, so a Cast can claim it afterwards.</summary>
        readonly Dictionary<int, int> _batchHurt = new Dictionary<int, int>(16);
        readonly HashSet<int> _batchDead = new HashSet<int>();

        Cast _cast;

        /// <summary>
        /// How many played cards one batch may stop the game for. A trap that springs on a summon
        /// during a resolution can chain, and three cards flying in for one tap is the same
        /// punishment MaxToldPerCombat exists to prevent.
        /// </summary>
        const int MaxCastsPerBatch = 2;

        float _shownAt = -99f;
        int _told;
        int _leftShown;
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
        int _builtFor = -1;           // the HandBar panel generation these surfaces belong to
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
            // AN ASSAULT TAKEN BACK takes its recordings with it.
            //
            // The list is otherwise cleared by declaration 0 arriving, on the argument that the
            // resolver empties the combat when it finishes so a restarting index is the only
            // signal a new one has begun. ✕ CANCEL breaks that argument: it clears the combat
            // WITHOUT the resolver ever running, so the withdrawn fights would sit here waiting,
            // and Record matches a later DamageApplied by attacker-or-defender id alone - bolt the
            // creature you nearly attacked and you would get a full cut-in of an attack you took
            // back. It would also quietly eat the next combat's cut-in budget.
            if (ev is AttackWithdrawn) { _fights.Clear(); _told = 0; return; }

            var declared = ev as AttackDeclared;
            if (declared != null)
            {
                // Declaration 0 is a FRESH combat: the resolver clears its list when it finishes,
                // so the index restarting is the only signal that a new one has begun.
                if (declared.DeclarationIndex == 0) { _fights.Clear(); _told = 0; }

                var f = new Fight { Index = declared.DeclarationIndex };
                if (!TrySnap(declared.AttackerId, out f.Attacker)) return;

                var unit = declared.Target as UnitTarget;
                if (unit != null)
                {
                    f.TargetCell = unit.Cell;
                    f.HasDefender = TrySnap(unit.UnitId, out f.Defender);

                    // A FACE-DOWN cannot be snapped - it has no name, no statline and no art to
                    // draw, and that is a rule rather than a gap. Striking one either flips it up
                    // and fights what was under it, or destroys it unfinished; the first of those
                    // is a real battle and had no cut-in at all, because the fight was recorded
                    // with no defender and Show() drops a fight with nothing to hold up.
                    f.AwaitingFlip = !f.HasDefender;
                }

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

            // The face-down turning over: it is a card now, so the fight it is in gets one.
            var flipped = ev as CardFlipped;
            if (flipped != null) { OnFlipped(flipped); return; }

            var dmg = ev as DamageApplied;
            if (dmg != null)
            {
                // TrySnap reads the live board or the last frame's, and a creature that was flipped
                // face-up and killed inside one command is in neither - the fight it belongs to is
                // the only thing holding its face. The floating number is the witness for damage
                // that gets no cut-in, so it must not be the one hit that quietly has none.
                Snap s;
                if (TrySnap(dmg.TargetId, out s) || TryFightSnap(dmg.TargetId, out s))
                    Pop(s.At, "-" + Stat.Show(dmg.Amount), Hurt);
                Record(dmg.TargetId, dmg.Amount);
                int had;
                _batchHurt[dmg.TargetId] = (_batchHurt.TryGetValue(dmg.TargetId, out had) ? had : 0)
                                         + dmg.Amount;
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
                _batchDead.Add(dead.UnitId);
                for (int i = 0; i < _fights.Count; i++)
                {
                    var f = _fights[i];
                    if (f.Attacker.Id == dead.UnitId) { f.AttackerDied = true; Resolved(f); }
                    if (f.HasDefender && f.Defender.Id == dead.UnitId)
                    { f.DefenderDied = true; Resolved(f); }
                }
                return;
            }

            // A CARD PLAYED. Both are recorded with their target cell and bound later - see _pending.
            var cast = ev as SpellResolved;
            if (cast != null) { Played(cast.Card, false, cast.HasTarget, cast.Target); return; }

            var sprung = ev as TrapSprung;
            if (sprung != null) { Played(sprung.Card, true, true, sprung.At); return; }
        }

        /// <summary>
        /// Record a spell or trap for telling. The card's face is built here, from the catalog,
        /// because it is the one thing that cannot be recovered later: a spell is never a
        /// BoardObject, so Resnap has never seen it and never will - by the time the frame ends the
        /// card is already in the graveyard.
        /// </summary>
        void Played(CardId card, bool trap, bool hasTarget, CellRef at)
        {
            if (_match == null || _match.Engine == null) return;
            if (_pending.Count >= MaxCastsPerBatch) return;

            var spell = _match.Engine.Catalog.Spell(card);
            string name = spell != null && !string.IsNullOrEmpty(spell.Name) ? spell.Name : card.Value;
            var def = _match.DefOf(name);

            var face = new Snap
            {
                Id = -1,                                   // never a board id: nothing may match it
                Name = name,
                Color = Element.None,                      // spells are neutral
                Art = def != null ? def.CardArt : null,
                Lead = spell != null ? "◆" + spell.Cost : (trap ? "⚠" : "✦"),
                SeenAt = Time.unscaledTime,
            };

            _pending.Add(new Cast { Card = face, Trap = trap, At = at, Targeted = hasTarget });
        }

        /// <summary>
        /// Turn this batch's played cards into cut-ins, now that the batch is over.
        ///
        /// The victim is found by CELL, not by id: a razed structure is off the board before
        /// SpellResolved is even raised, so there is nothing to look up - but `_snaps` still holds
        /// the board as it was one frame ago, keyed by id and carrying the cell each unit stood in,
        /// and that is the version worth drawing. Damage and death come from what the batch was
        /// seen doing to that same unit.
        /// </summary>
        void ResolveCasts()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var c = _pending[i];

                Snap victim;
                if (c.Targeted && SnapAt(c.At, out victim))
                {
                    c.Victim = victim;
                    c.HasVictim = true;
                    int hurt;
                    c.Damage = _batchHurt.TryGetValue(victim.Id, out hurt) ? hurt : 0;
                    c.VictimDied = _batchDead.Contains(victim.Id);
                }

                // A spell with nothing to hold up against it - a trap that fizzled, a bolt whose
                // target had already gone - is still worth naming, so it is told on its own.
                _castQueue.Add(c);
            }

            _pending.Clear();
            _batchHurt.Clear();
            _batchDead.Clear();
        }

        /// <summary>
        /// The unit that was standing in this cell as of last frame, dead or alive.
        ///
        /// Takes the most recently SEEN of the candidates, and has to: a snapshot outlives the unit
        /// by GraveMemory, so a cell that has been fought over twice in twelve seconds holds
        /// several, and the older ones are corpses that have nothing to do with this spell. Every
        /// unit still on the board was re-stamped by the last Resnap, so the freshest match is
        /// either the thing standing there now or the thing that was standing there one frame ago -
        /// which is exactly the pair a spell can have hit.
        /// </summary>
        bool SnapAt(CellRef at, out Snap found)
        {
            bool any = false;
            found = default(Snap);
            foreach (var kv in _snaps)
            {
                var s = kv.Value;
                if (!s.At.Equals(at)) continue;
                if (any && s.SeenAt <= found.SeenAt) continue;
                found = s;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// A set card provoked into the open. Every attack aimed at that cell that has been
        /// waiting for a face to draw gets one now.
        ///
        /// The flip mints a NEW unit id - the ChargeUnit is replaced, not converted - so the
        /// declaration's stored target id can never find it, and the cell is the only thing the
        /// two have in common. This runs BEFORE the damage in the same batch (ProvokeFaceDown
        /// flips and then fights), so by the time Record is called the fight has a defender and
        /// the numbers land on it.
        /// </summary>
        void OnFlipped(CardFlipped flipped)
        {
            for (int i = 0; i < _fights.Count; i++)
            {
                var f = _fights[i];
                if (!f.AwaitingFlip || f.TargetCell != flipped.At) continue;

                // Live first - a flip that survives its fight is on the board and can be read
                // straight off it. A flip that does NOT survive is the ordinary case, and the
                // only version of it left is the face remembered while it was still a secret.
                Snap revealed;
                if (!TrySnap(flipped.UnitId, out revealed)
                    && !_hidden.TryGetValue(flipped.At, out revealed)) continue;
                revealed.Id = flipped.UnitId;      // so Record can find the fight by the new id

                f.Defender = revealed;
                f.HasDefender = true;
                f.DirectHit = false;
                f.AwaitingFlip = false;
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

            // The batch is over: a played card can now see what it did. Before the dequeue, so a
            // card played this frame can be told this frame.
            ResolveCasts();

            if (_showing == null && _cast == null && _queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                if (CutIns && _told < MaxToldPerCombat) { Show(next); _told++; }
                else Regroup(next);              // still claim its partners, so they do not queue
            }
            else if (_showing == null && _cast == null && _castQueue.Count > 0)
            {
                // A fight outranks a spell: the fight is the thing the player is waiting on, and
                // the spell that provoked it has already had its say in the log.
                var next = _castQueue[0];
                _castQueue.RemoveAt(0);
                if (CutIns) ShowCast(next);
            }
            else if (_rebind && _showing != null)
            {
                BindCards();
            }
            _rebind = false;

            // A cut-in that never got shown must not pile up behind one that is running: the queue
            // is a frame's worth of news, not a backlog.
            if (_castQueue.Count > MaxCastsPerBatch)
                _castQueue.RemoveRange(0, _castQueue.Count - MaxCastsPerBatch);

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

            _hiddenGone.Clear();
            foreach (var kv in _hidden) _hiddenGone.Add(kv.Key);

            foreach (var kv in s.Objects())
            {
                var o = kv.Value;
                var cre = o as CreatureUnit;
                var bld = o as StructureUnit;
                if (cre == null && bld == null)
                {
                    // a face-down card keeps its secret - it is remembered, never drawn
                    var ch = o as ChargeUnit;
                    if (ch != null) { _hidden[kv.Key] = HiddenSnap(ch, kv.Key); _hiddenGone.Remove(kv.Key); }
                    continue;
                }

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

            // A cell that no longer holds a face-down keeps its remembered face for a moment, for
            // the same reason a dead unit does: the cut-in for the flip may not have played yet.
            for (int i = 0; i < _hiddenGone.Count; i++)
            {
                Snap gone;
                if (_hidden.TryGetValue(_hiddenGone[i], out gone)
                    && Time.unscaledTime - gone.SeenAt < GraveMemory) continue;
                _hidden.Remove(_hiddenGone[i]);
            }
        }

        /// <summary>The card lying face-down in this cell, as it would look face-up. A bounced
        /// creature that was set again carries its LIVE statline (ChargeUnit.Snap); anything else
        /// is the printed card.</summary>
        Snap HiddenSnap(ChargeUnit ch, CellRef at)
        {
            int atk = ch.Snap.HasValue ? ch.Snap.Attack : ch.Card.Attack;
            int hp = ch.Snap.HasValue ? ch.Snap.Health : ch.Card.Health;
            string name = ch.Snap.HasValue ? ch.Snap.Name : ch.Card.Name;
            var def = _match.DefOf(name) ?? _match.DefOf(ch.Card.Id.Value);

            return new Snap
            {
                Id = ch.Id,
                Name = name,
                Color = ch.Color,
                Attack = ch.IsStructure ? 0 : atk,
                Hp = hp,
                MaxHp = Mathf.Max(hp, 1),
                Structure = ch.IsStructure,
                At = at,
                Owner = ch.Owner,
                Art = def != null ? def.CardArt : null,
                Lead = ch.IsStructure ? "⌂" : Stat.Atk(atk),
                SeenAt = Time.unscaledTime,
            };
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

        /// <summary>A card already bound into a fight, for anything that can no longer be read off
        /// the board at all.</summary>
        bool TryFightSnap(int unitId, out Snap snap)
        {
            for (int i = 0; i < _fights.Count; i++)
            {
                var f = _fights[i];
                if (f.Attacker.Id == unitId) { snap = f.Attacker; return true; }
                if (f.HasDefender && f.Defender.Id == unitId) { snap = f.Defender; return true; }
            }
            snap = default(Snap);
            return false;
        }

        Snap WallSnap(Side defender)
        {
            var p = _match.Engine.State.P(defender);
            return new Snap
            {
                Id = -1 - (int)defender,
                Name = defender == Seat.Local ? "Your Wall" : "Their Wall",
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
        /// The played card, held up against what it happened to. Same picture as a fight, one card
        /// on the left instead of a fan, and a different glyph between them: a spell is not a
        /// clash, so ⚔ would be a lie about what just happened.
        /// </summary>
        void ShowCast(Cast c)
        {
            _cast = c;
            _shownAt = Time.unscaledTime;

            _group.Clear();
            BindCast();
            _cutIn.style.display = DisplayStyle.Flex;

            MatchController.Hold(CutInSeconds);
        }

        void BindCast()
        {
            float cardW = CardWidth();

            _left.Clear();
            var card = LeftCard(0);
            card.Bind(new BattleCard.Model
            {
                Name = _cast.Card.Name,
                Lead = _cast.Card.Lead,
                Element = _cast.Card.Color,
                Art = _cast.Card.Art,
                Played = true,
                TypeName = _cast.Trap ? "TRAP" : "SPELL",
            }, _palette, cardW);
            card.style.marginLeft = 0f;
            card.style.translate = new Translate(0f, 0f);
            card.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            _left.Add(card);
            _leftShown = 1;

            // An untargeted card holds up nothing: the right half stays empty rather than showing
            // a card that had nothing to do with it.
            _right.style.display = _cast.HasVictim ? DisplayStyle.Flex : DisplayStyle.None;
            if (_cast.HasVictim)
                _rightCard.Bind(Model(_cast.Victim, _cast.Damage, _cast.VictimDied), _palette, cardW);

            _clash.text = _cast.Trap ? "⚠" : "✦";
            _clash.style.fontSize = cardW * 0.42f;
            _clash.style.marginLeft = cardW * 0.10f;
            _clash.style.marginRight = cardW * 0.10f;
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
                Foe = s.Owner == Seat.Remote,
                Wall = s.Wall,
                Structure = s.Structure,
            };
        }

        void AnimateCutIn()
        {
            if (_showing == null && _cast == null)
            {
                _cutIn.style.display = DisplayStyle.None;
                return;
            }

            float age = Time.unscaledTime - _shownAt;
            if (age > CutInSeconds)
            {
                _showing = null;
                _cast = null;
                _group.Clear();
                _right.style.display = DisplayStyle.Flex;    // a cast may have hidden it
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
            int left = _cast != null ? _leftShown : _group.Count;
            for (int i = 0; i < left && i < _leftCards.Count; i++)
                _leftCards[i].ShowResult(hit);
            if (_cast == null || _cast.HasVictim) _rightCard.ShowResult(hit);
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
            if (_hand == null || !_hand.PanelReady) return;

            // REBUILT WITH THE PANEL, for the same reason UnitVitals is: the cut-in and the
            // floating damage numbers are parented into HandBar's layers, and those are remade
            // every time the board object is switched off and on. A non-null `_cutIn` pointing at
            // an orphan is a battle theatre that runs its whole animation into nothing.
            if (_cutIn != null && _builtFor == _hand.PanelGeneration) return;
            _builtFor = _hand.PanelGeneration;
            _cutIn = null; _inner = null; _left = null; _right = null; _floatLayer = null;
            _clash = null; _rightCard = null;

            // A cast in flight is holding the OLD _left/_right; let it go with them rather than
            // animate a card that is parented to a detached layer.
            _cast = null;

            // THE POOLS GO TOO, and they are the half that would have been missed. Both are
            // RECYCLED rather than rebuilt - SpawnPopped hands back the first floater that is not
            // live, and Card(i) grows _leftCards only when it runs out - so a pool that survives
            // the teardown goes on writing damage numbers and battle cards into elements parented
            // to a layer that is no longer attached to anything. The hand would come back and the
            // numbers would not.
            _floaters.Clear();
            _leftCards.Clear();

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
