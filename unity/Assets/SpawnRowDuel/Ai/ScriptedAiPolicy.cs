using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Ai
{
    /// <summary>
    /// `foeTurn` (17_turns_ai.js:268-389) as a COMMAND SOURCE, not a coroutine.
    ///
    /// The JS AI turn was one long async function that mutated the board directly and awaited
    /// modals in the middle of combat. That shape is unportable: it cannot be snapshotted, it
    /// cannot be replayed, and it bypasses every validator. Here the policy answers exactly one
    /// question - "what would you do next?" - and the engine does the doing. Every AI action is
    /// an ordinary command through the ordinary validators, so an illegal AI move is a rejection
    /// rather than a corrupt board.
    ///
    /// The turn's ORDER is the JS's, step for step (spec 07 s9):
    ///   upkeep: settle the shortfall (move, then sacrifice, then pay) -> harvest
    ///   draw
    ///   action: fuel face-downs -> build x2 -> upgrade -> raze -> burn -> set a trap
    ///           -> summon -> declare every attack -> resolve
    ///   end
    ///
    /// The only per-turn memory is a set of budget counters - "have I built twice yet" is not
    /// derivable from the board. They reset when the turn number changes, so a policy handed a
    /// restored snapshot picks up correctly rather than carrying a stale turn's budget.
    /// </summary>
    public sealed class ScriptedAiPolicy
    {
        readonly Side _side;
        readonly AiTuning _tuning;

        int _turnSeen = -1;
        int _builds, _upgrades, _traps, _summons;
        bool _fueled, _rebalanceStuck, _declared, _razed, _burned;
        readonly List<PlannedAttack> _attacks = new List<PlannedAttack>();

        struct PlannedAttack
        {
            public CellRef From;
            public int UnitId;
            public AttackTarget Target;
        }

        public ScriptedAiPolicy(Side side) : this(side, AiTuning.JsDefault) { }

        public ScriptedAiPolicy(Side side, AiTuning tuning)
        {
            _side = side;
            _tuning = tuning;
        }

        public Side Side { get { return _side; } }

        /// <summary>
        /// The next command this AI wants applied, or null when it has nothing left to do.
        ///
        /// Call it once per applied command. It is almost pure - the single exception is the
        /// attack-planning step, which draws from the match RNG (aiPickTarget's two rolls) and
        /// caches its whole declaration list, exactly as the JS builds `declared[]` in one pass.
        /// </summary>
        public ICommand Next(DuelEngine engine)
        {
            var s = engine.State;
            if (s.IsOver) return null;

            // A parked choice outranks everything, including whose turn it is: the AI answers as
            // defender on the player's turn too.
            if (s.Pending != null)
                return s.Pending.Responder == _side ? Respond(s, s.Pending) : null;

            if (s.Turn != _side) return null;
            ResetIfNewTurn(s);

            switch (s.Phase)
            {
                case TurnPhase.Upkeep: return UpkeepPhase(engine);
                case TurnPhase.Draw: return new DrawForTurnCommand(_side);
                case TurnPhase.Action: return Action(engine);
                default: return null;               // End: the driver hands off to the other side
            }
        }

        void ResetIfNewTurn(GameState s)
        {
            if (s.TurnNumber == _turnSeen) return;
            _turnSeen = s.TurnNumber;
            _builds = _upgrades = _traps = _summons = 0;
            _fueled = false;
            _rebalanceStuck = false;
            _declared = false;
            _razed = _burned = false;
            _attacks.Clear();
        }

        // ── parked choices ───────────────────────────────────────────────────────────────────

        ICommand Respond(GameState s, PendingRequest pending)
        {
            var blocker = pending as BlockerRequest;
            if (blocker != null)
                return new RespondCommand(_side,
                    new BlockersChosen(AiPolicy.ChooseInterceptors(s, blocker)));

            var absorber = pending as AbsorberRequest;
            if (absorber != null)
                return new RespondCommand(_side, new IndexChosen(AiChoices.PickAbsorber(s, absorber)));

            // The JS defender never chose where to strike back - targetFight was always called
            // with retaliation index 0, the first declared attacker.
            if (pending is RetaliationRequest)
                return new RespondCommand(_side, new IndexChosen(0));

            // Springing the first armed trap is exactly what the JS auto-spring did, and it is
            // what makes this policy's behaviour identical to the pre-window build (D6).
            var window = pending as ResponseWindowRequest;
            if (window != null)
                return new RespondCommand(_side, window.ArmedTraps.Length > 0
                    ? new TrapChosen(window.ArmedTraps[0])
                    : TrapChosen.Passed);

            return null;
        }

        // ── upkeep: aiFixDeficit, then harvest ───────────────────────────────────────────────

        /// <summary>
        /// aiFixDeficit (17_turns_ai.js:188-219) in three ordered passes, one command at a time:
        /// rebalance by MOVING creatures into rows that can carry them, SACRIFICE only while the
        /// bill is still unaffordable, then PAY what is left. Sacrificing before checking
        /// affordability would throw away bodies the vault could have covered.
        /// </summary>
        ICommand UpkeepPhase(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;

            if (!_rebalanceStuck)
            {
                var move = FindRebalanceMove(engine);
                if (move != null) return move;
                _rebalanceStuck = true;             // the JS's `if(!moved) break`
            }

            int owe = Upkeep.TotalDeficit(s, _side, cat);
            if (owe > 0 && owe > s.P(_side).Mana)
            {
                var victim = FindSacrifice(engine);
                if (victim != null) return victim;
            }

            if (owe > 0 && s.P(_side).Mana >= owe)
            {
                var pay = FindPayment(engine);
                if (pay != null) return pay;
            }

            var harvest = new HarvestCommand(_side);
            if (engine.CanApply(harvest) == Rejection.None) return harvest;

            // Harvest is gated on there being no settleable offender left. If one survives all
            // three passes the turn would dead-lock, so settle it the bluntest way available.
            var forced = FindSacrifice(engine);
            return forced;
        }

        /// <summary>
        /// Pass 1: the highest-upkeep creature in the first short zone steps into an ADJACENT
        /// zone that can absorb it. MOVE_ADJ is the JS's zone graph, and 'raid' is excluded -
        /// the AI never rebalances INTO enemy ground.
        /// </summary>
        ICommand FindRebalanceMove(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;

            for (int z = 0; z < Upkeep.SettleOrder.Length; z++)
            {
                var zone = Upkeep.SettleOrder[z];
                if (zone == WorkerZone.Raid) continue;
                if (Upkeep.ZoneDeficit(s, _side, zone, cat) <= 0) continue;

                var worst = HeaviestCreatureIn(s, _side, zone);
                if (worst.Value == null) return null;

                var to = MoveAdjacency(zone);
                for (int i = 0; i < to.Length; i++)
                {
                    // only into a row that stays solvent once this creature's keep lands on it
                    if (WorkerMath.RowWorkers(s, _side, to[i], cat) - worst.Value.Upkeep < 0) continue;

                    var rows = Board.RowsOfZone(_side, to[i]);
                    for (int r = 0; r < rows.Length; r++)
                        for (int col = 0; col < Board.Columns; col++)
                        {
                            var dest = new CellRef(rows[r], col);
                            var cmd = new MoveUnitCommand(_side, worst.Key, dest, worst.Value.Id);
                            if (engine.CanApply(cmd) == Rejection.None) return cmd;
                        }
                }
                return null;                         // this zone's worst offender cannot move
            }
            return null;
        }

        /// <summary>MOVE_ADJ (17_turns_ai.js:177): the zone graph the AI shuffles along.</summary>
        static WorkerZone[] MoveAdjacency(WorkerZone from)
        {
            switch (from)
            {
                case WorkerZone.Back: return new[] { WorkerZone.Front };
                case WorkerZone.Front: return new[] { WorkerZone.Back, WorkerZone.Center };
                case WorkerZone.Center: return new[] { WorkerZone.Front };
                default: return new WorkerZone[0];
            }
        }

        ICommand FindSacrifice(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            for (int z = 0; z < Upkeep.SettleOrder.Length; z++)
            {
                var zone = Upkeep.SettleOrder[z];
                if (Upkeep.ZoneDeficit(s, _side, zone, cat) <= 0) continue;

                var worst = HeaviestCreatureIn(s, _side, zone);
                if (worst.Value == null) continue;
                var cmd = new UpkeepSacrificeCommand(_side, worst.Key, worst.Value.Id);
                if (engine.CanApply(cmd) == Rejection.None) return cmd;
            }
            return null;
        }

        ICommand FindPayment(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            for (int z = 0; z < Upkeep.SettleOrder.Length; z++)
            {
                var zone = Upkeep.SettleOrder[z];
                if (Upkeep.ZoneDeficit(s, _side, zone, cat) <= 0) continue;

                var worst = HeaviestCreatureIn(s, _side, zone);
                if (worst.Value == null) continue;
                var cmd = new UpkeepPayCommand(_side, worst.Key, worst.Value.Id);
                if (engine.CanApply(cmd) == Rejection.None) return cmd;
            }
            return null;
        }

        /// <summary>The zone's costliest unsettled creature - the JS sorts by upkeep DESC and
        /// takes the first, so ties fall to board order.</summary>
        static KeyValuePair<CellRef, CreatureUnit> HeaviestCreatureIn(GameState s, Side owner,
                                                                    WorkerZone zone)
        {
            var best = default(KeyValuePair<CellRef, CreatureUnit>);
            var rows = Board.RowsOfZone(owner, zone);
            for (int r = 0; r < rows.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var at = new CellRef(rows[r], col);
                    var c = s.At(at) as CreatureUnit;
                    if (c == null || c.Owner != owner || c.IsWorker || c.PaidUpkeep) continue;
                    if (best.Value == null || c.Upkeep > best.Value.Upkeep)
                        best = new KeyValuePair<CellRef, CreatureUnit>(at, c);
                }
            return best;
        }

        // ── action ───────────────────────────────────────────────────────────────────────────

        ICommand Action(DuelEngine engine)
        {
            var s = engine.State;

            if (!_fueled)
            {
                var pour = FuelCharges(engine);
                if (pour != null) return pour;
                _fueled = true;
            }

            if (_builds < _tuning.MaxBuildsPerTurn)
            {
                var build = FindBuild(engine);
                if (build != null) { _builds++; return build; }
                _builds = _tuning.MaxBuildsPerTurn;            // nothing to build; stop trying
            }

            if (_upgrades < _tuning.MaxUpgradesPerTurn)
            {
                var up = FindUpgrade(engine);
                _upgrades++;                                   // one attempt per turn either way
                if (up != null) return up;
            }

            if (!_razed)                                       // 5a: bring down a structure
            {
                _razed = true;
                var raze = FindSpell(engine, SpellEffect.Raze);
                if (raze != null) return raze;
            }

            if (!_burned)                                      // 5b: burn the strongest soldier
            {
                _burned = true;
                var burn = FindSpell(engine, SpellEffect.Burn);
                if (burn != null) return burn;
            }

            if (_traps < _tuning.MaxTrapsPerTurn)
            {
                var trap = FindTrap(engine);
                _traps++;
                if (trap != null) return trap;
            }

            if (_summons < _tuning.MaxSummonsPerTurn)
            {
                var summon = FindSummon(engine);
                if (summon != null) { _summons++; return summon; }
                _summons = _tuning.MaxSummonsPerTurn;
            }

            if (!_declared)
            {
                PlanAttacks(engine);                           // draws the RNG rolls, once
                _declared = true;
            }
            if (_attacks.Count > 0)
            {
                var a = _attacks[0];
                _attacks.RemoveAt(0);
                // deferred blocks: the s12 mirrored cadence - the defender answers after seeing
                // the COMPLETE assault, which is what the JS AI turn does with its one window
                var cmd = new DeclareAttackCommand(_side, a.From, a.UnitId, a.Target, true);
                if (engine.CanApply(cmd) == Rejection.None) return cmd;
                return Action(engine);                         // stale plan entry: skip it
            }

            if (s.Combat.HasDeclarations) return new ResolveCombatCommand(_side);

            return new EndTurnCommand(_side);
        }

        /// <summary>
        /// Step 0 (17_turns_ai.js:271-272): pour everything into the AI's own face-downs on the
        /// front line and in the centre, and flip whatever that funds.
        ///
        /// Unreachable in solo - the JS AI never sets a card face-down, so it never owns a charge
        /// (RulesOptions.AiUsesFullSpellSet documents the same gap). Ported because the step is
        /// real and a future AI, or a remote player driving this side, will produce charges.
        /// Note the JS fuels BEFORE harvesting, on leftover vault mana only; the phase machine
        /// puts it after, which is strictly better play and, being unreachable, unobservable.
        /// </summary>
        ICommand FuelCharges(DuelEngine engine)
        {
            var s = engine.State;
            var rows = new[] { Board.RowFor(_side, SlotName.Front), RowKey.Center };
            for (int r = 0; r < rows.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var at = new CellRef(rows[r], col);
                    var ch = s.At(at) as ChargeUnit;
                    if (ch == null || ch.Owner != _side) continue;

                    if (ch.Invested < ch.Card.Cost && s.P(_side).Mana > 0)
                    {
                        int want = ch.Card.Cost - ch.Invested;
                        int pour = want < s.P(_side).Mana ? want : s.P(_side).Mana;
                        var cmd = new PourIntoChargeCommand(_side, at, ch.Id, pour);
                        if (engine.CanApply(cmd) == Rejection.None) return cmd;
                    }
                    if (ch.Invested >= ch.Card.Cost)
                    {
                        var flip = new FlipChargeCommand(_side, at, ch.Id);
                        if (engine.CanApply(flip) == Rejection.None) return flip;
                    }
                }
            return null;
        }

        /// <summary>
        /// aiBuild (07_structures.js:50-66). buildList ORDER IS THE PRIORITY - Foundry first,
        /// then the forges, and so on down the commander's menu. The caps stop it stacking six
        /// Longhouses, and lineage-aware counting means an upgraded Keep still counts against the
        /// Foundry cap.
        /// </summary>
        ICommand FindBuild(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            var list = cat.BuildList(s.P(_side).Commander);

            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                int cap = CapFor(def.Bid.Value);
                if (cap > 0 && CountByLineage(s, cat, def.Bid.Value) >= cap) continue;

                // one forge - or its Grand upgrade - per COLOUR
                if (def.Bid.Value == "forge" && HasForgeOfColor(s, cat, def.Element)) continue;
                if (def.Bid.Value == "grandforge" && HasGrandForgeOfColor(s, def.Element)) continue;

                if (!Placement.CanBuild(s, _side, def, cat)) continue;

                var which = new[] { SlotName.Back, SlotName.Front };
                for (int w = 0; w < which.Length; w++)
                {
                    var zone = which[w] == SlotName.Back ? WorkerZone.Back : WorkerZone.Front;
                    if (!Placement.PlaceRowOk(s, _side, zone, def, cat)) continue;

                    int slot = AiChoices.PickDeploySlot(s, _side, which[w]);
                    if (slot < 0) continue;

                    var cell = new CellRef(Board.RowFor(_side, which[w]), slot);
                    var cmd = new BuildStructureCommand(_side, def.Bid, def.Element, cell);
                    if (engine.CanApply(cmd) == Rejection.None) return cmd;
                }
            }
            return null;
        }

        /// <summary>The JS's CAP table. 0 means uncapped.</summary>
        static int CapFor(string bid)
        {
            switch (bid)
            {
                case "foundry": return 1;
                case "encampment": return 1;
                case "longhouse": return 1;
                case "vault": return 1;
                case "outpost": return 1;
                case "bulwark": return 1;
                case "reliquary": return 1;
                case "tower": return 2;
                default: return 0;
            }
        }

        int CountByLineage(GameState s, ICardCatalog cat, string familyBid)
        {
            int n = 0;
            foreach (var kv in s.ObjectsOf(_side))
            {
                var b = kv.Value as StructureUnit;
                if (b == null || b.IsCommandCenter || b.DefId.IsNone) continue;
                var lineage = cat.Lineage(b.DefId);
                for (int i = 0; i < lineage.Count; i++)
                    if (lineage[i].Value == familyBid) { n++; break; }
            }
            return n;
        }

        bool HasForgeOfColor(GameState s, ICardCatalog cat, Element color)
        {
            foreach (var kv in s.ObjectsOf(_side))
            {
                var b = kv.Value as StructureUnit;
                if (b == null || b.DefId.IsNone || b.Color != color) continue;
                var lineage = cat.Lineage(b.DefId);
                for (int i = 0; i < lineage.Count; i++)
                    if (lineage[i].Value == "forge") return true;
            }
            return false;
        }

        bool HasGrandForgeOfColor(GameState s, Element color)
        {
            foreach (var kv in s.ObjectsOf(_side))
            {
                var b = kv.Value as StructureUnit;
                if (b != null && b.DefId.Value == "grandforge" && b.Color == color) return true;
            }
            return false;
        }

        /// <summary>
        /// aiUpgrade (07_structures.js:38-48): the first owned structure, in board order, whose
        /// first legal upgrade target it can afford. At most one per turn. The engine's own
        /// validator is the oracle for "can I upgrade to this" - the row gate, the mana and the
        /// support arithmetic all live there, and duplicating them here is how they drift.
        /// </summary>
        ICommand FindUpgrade(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            foreach (var kv in s.ObjectsOf(_side))
            {
                var b = kv.Value as StructureUnit;
                if (b == null || b.IsCommandCenter || b.DefId.IsNone) continue;

                var def = cat.Structure(b.DefId, b.Color);
                if (def == null) continue;
                for (int i = 0; i < def.UpgradeTargets.Length; i++)
                {
                    var target = new StructId(def.UpgradeTargets[i]);
                    var cmd = new UpgradeStructureCommand(_side, kv.Key, b.Id, target);
                    if (engine.CanApply(cmd) == Rejection.None) return cmd;
                }
            }
            return null;
        }

        /// <summary>
        /// Steps 5a and 5b: one raze at a structure, then one burn at the strongest soldier.
        /// The JS never casts chain or bounce and never sets a creature face-down
        /// (RulesOptions.AiUsesFullSpellSet is the flag for widening that).
        ///
        /// The raze target is whatever structure the ROWS scan saw LAST - not the best one. That
        /// is the JS's `tk=key;ti=j` with no break, and AiRazeUsesHeuristic flags it.
        /// </summary>
        ICommand FindSpell(DuelEngine engine, SpellEffect effect)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            var hand = s.P(_side).Hand;

            for (int i = 0; i < hand.Count; i++)
            {
                SpellCard sp;
                if (!cat.TrySpell(hand[i].Id, out sp) || sp.IsTrap) continue;
                if (sp.Effect != effect) continue;
                if (s.P(_side).Mana < sp.Cost) continue;

                var target = effect == SpellEffect.Raze ? RazeTarget(s) : StrongestEnemySoldier(s);
                if (target == null) return null;

                var cmd = new PlayCardCommand(_side, i, PlayMode.Cast, target.Value);
                return engine.CanApply(cmd) == Rejection.None ? cmd : null;
            }
            return null;
        }

        /// <summary>The LAST structure the board scan sees, not the best one - `tk=key;ti=j`
        /// with no break. AiRazeUsesHeuristic swaps in "the frailest" instead.</summary>
        CellRef? RazeTarget(GameState s)
        {
            CellRef? found = null;
            int frailest = 0;
            foreach (var kv in s.ObjectsOf(TurnMachine.Other(_side)))
            {
                var b = kv.Value as StructureUnit;
                if (b == null) continue;
                if (!s.Options.AiRazeUsesHeuristic) { found = kv.Key; continue; }
                if (found == null || b.Hp < frailest) { found = kv.Key; frailest = b.Hp; }
            }
            return found;
        }

        /// <summary>Highest RAW attack; strictly greater, so the FIRST maximum in board order wins.</summary>
        CellRef? StrongestEnemySoldier(GameState s)
        {
            CellRef? best = null;
            int bestAtk = -1;
            foreach (var kv in s.ObjectsOf(TurnMachine.Other(_side)))
            {
                var c = kv.Value as CreatureUnit;
                if (c == null || c.IsWorker) continue;
                if (c.Attack > bestAtk) { bestAtk = c.Attack; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Step 5c: arm the FIRST trap in hand, back row before front.</summary>
        ICommand FindTrap(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            var hand = s.P(_side).Hand;

            for (int i = 0; i < hand.Count; i++)
            {
                SpellCard sp;
                if (!cat.TrySpell(hand[i].Id, out sp) || !sp.IsTrap) continue;

                var which = new[] { SlotName.Back, SlotName.Front };
                for (int w = 0; w < which.Length; w++)
                {
                    var row = Board.RowFor(_side, which[w]);
                    for (int col = 0; col < Board.Columns; col++)
                    {
                        var cell = new CellRef(row, col);
                        if (s.At(cell) != null) continue;
                        var cmd = new PlayCardCommand(_side, i, PlayMode.SetTrap, cell);
                        if (engine.CanApply(cmd) == Rejection.None) return cmd;
                    }
                }
                return null;                         // it holds a trap but cannot place it
            }
            return null;
        }

        /// <summary>
        /// Step 6: summon the costliest affordable creature it can, front row before back. The
        /// JS sorts its candidates by cost DESCENDING and walks them, re-checking affordability
        /// as its mana drains.
        /// </summary>
        ICommand FindSummon(DuelEngine engine)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            var hand = s.P(_side).Hand;

            int bestIdx = -1, bestCost = -1;
            for (int i = 0; i < hand.Count; i++)
            {
                CreatureCard c;
                int cost;
                if (hand[i].Snapshot.HasValue) cost = hand[i].Snapshot.Cost;
                else if (cat.TryCreature(hand[i].Id, out c)) cost = c.Cost;
                else continue;

                if (cost > s.P(_side).Mana) continue;
                if (cost > bestCost) { bestCost = cost; bestIdx = i; }
            }
            if (bestIdx < 0) return null;

            var which = new[] { SlotName.Front, SlotName.Back };     // the AI pushes forward
            for (int w = 0; w < which.Length; w++)
            {
                int slot = AiChoices.PickDeploySlot(s, _side, which[w]);
                if (slot < 0) continue;
                var cell = new CellRef(Board.RowFor(_side, which[w]), slot);
                var cmd = new PlayCardCommand(_side, bestIdx, PlayMode.Summon, cell);
                if (engine.CanApply(cmd) == Rejection.None) return cmd;
            }
            return null;
        }

        /// <summary>
        /// Step 7: every eligible attacker declares, in board order, at a target chosen by
        /// aiPickTarget. Planned in ONE pass - and therefore drawing all of its RNG rolls in one
        /// pass, exactly where the JS draws them - then issued one command at a time.
        /// </summary>
        void PlanAttacks(DuelEngine engine)
        {
            var s = engine.State;
            _attacks.Clear();

            var attackers = AiChoices.Attackers(s, _side);
            for (int i = 0; i < attackers.Count; i++)
            {
                var target = AiChoices.PickTarget(s, _side, attackers[i].Value, _tuning);
                if (target == null) continue;
                _attacks.Add(new PlannedAttack
                {
                    From = attackers[i].Key,
                    UnitId = attackers[i].Value.Id,
                    Target = target,
                });
            }
        }
    }
}
