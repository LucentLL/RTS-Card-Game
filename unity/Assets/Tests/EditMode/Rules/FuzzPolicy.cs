using System.Collections.Generic;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M12 tier 3's command source: a player that does something LEGAL at random.
    ///
    /// The scripted AI is a good trace generator and a bad fuzzer - it plays one opening, never
    /// sets a creature face-down, never pours into a charge, never sends banked mana, never moves
    /// a raider sideways, and never attacks a worker stack. Those paths exist in both engines and
    /// nothing has ever compared them. This policy reaches them by enumerating every command it
    /// can spell, asking the engine's own <see cref="DuelEngine.CanApply"/> which are legal right
    /// now, and picking one.
    ///
    /// Two properties make the traces it produces useful:
    ///
    ///   * It draws from its OWN Pcg32, never the match RNG. Consuming the match stream would
    ///     change the game being fuzzed, so a fuzz seed and a match seed stay independent and a
    ///     failure reproduces from the pair.
    ///   * It picks a command KIND first and an instance second. Uniform choice over instances
    ///     would drown "end the turn" under four hundred legal summon placements and no turn
    ///     would ever end; kind-first keeps turns to roughly a dozen plies and still reaches the
    ///     rare kinds, because a kind with one legal instance is as likely as a kind with four
    ///     hundred.
    ///
    /// The turn budget is a backstop, not a rule: past it only the advancing kinds are offered,
    /// so a fuzzer that has found a mana-neutral shuffle it can repeat forever still hands over.
    /// </summary>
    public sealed class FuzzPolicy
    {
        // Command kinds, in a fixed order - the pick is over this array, so its ORDER is part of
        // what a fuzz seed reproduces. Never reorder without expecting different traces.
        const int KAdvance = 0;    // harvest / draw / begin the next turn
        const int KEnd = 1;
        const int KPay = 2;
        const int KSacrifice = 3;
        const int KMove = 4;
        const int KSummon = 5;
        const int KSet = 6;
        const int KSetTrap = 7;
        const int KCast = 8;
        const int KBuild = 9;
        const int KUpgrade = 10;
        const int KPour = 11;
        const int KFlip = 12;
        const int KSendMana = 13;
        const int KDeclare = 14;
        const int KResolve = 15;
        const int Kinds = 16;

        readonly Pcg32 _rng;
        readonly int _turnBudget;
        readonly List<ICommand>[] _kinds = new List<ICommand>[Kinds];

        int _turnKey = -1;
        int _pliesThisTurn;

        /// <summary>How many CanApply probes the last Next() spent - a cost meter for the tests.</summary>
        public int LastProbeCount;

        public FuzzPolicy(ulong fuzzSeed, int turnBudget = 24)
        {
            _rng = new Pcg32(fuzzSeed, 7UL);       // its own stream, deliberately not the match's
            _turnBudget = turnBudget;
            for (int i = 0; i < Kinds; i++) _kinds[i] = new List<ICommand>();
        }

        /// <summary>One legal command, or null when the fuzzer has nothing legal left to do.</summary>
        public ICommand Next(DuelEngine engine)
        {
            var s = engine.State;
            if (s.IsOver) return null;
            if (s.Pending != null) return Respond(engine, s.Pending);

            int key = s.TurnNumber * 2 + (int)s.Turn;
            if (key != _turnKey) { _turnKey = key; _pliesThisTurn = 0; }

            Collect(engine);
            _pliesThisTurn++;

            if (_pliesThisTurn > _turnBudget)
            {
                var forced = PickIn(KResolve) ?? PickIn(KEnd) ?? PickIn(KAdvance);
                if (forced != null) return forced;
            }

            int available = 0;
            for (int i = 0; i < Kinds; i++) if (_kinds[i].Count > 0) available++;
            if (available == 0) return null;

            int pick = _rng.NextInt(available);
            for (int i = 0; i < Kinds; i++)
            {
                if (_kinds[i].Count == 0) continue;
                if (pick-- == 0) return PickIn(i);
            }
            return null;
        }

        ICommand PickIn(int kind)
        {
            var list = _kinds[kind];
            if (list.Count == 0) return null;
            return list[_rng.NextInt(list.Count)];
        }

        // ---- enumeration ---------------------------------------------------------------------

        void Collect(DuelEngine engine)
        {
            for (int i = 0; i < Kinds; i++) _kinds[i].Clear();
            LastProbeCount = 0;

            var s = engine.State;
            var me = s.Turn;
            var foe = TurnMachine.Other(me);

            Offer(engine, KAdvance, new HarvestCommand(me));
            Offer(engine, KAdvance, new DrawForTurnCommand(me));
            Offer(engine, KAdvance, new BeginTurnCommand(me));
            Offer(engine, KAdvance, new BeginTurnCommand(foe));
            Offer(engine, KEnd, new EndTurnCommand(me));
            Offer(engine, KResolve, new ResolveCombatCommand(me));

            CollectBoard(engine, me, foe);
            CollectHand(engine, me);
            CollectBuilds(engine, me);
        }

        void CollectBoard(DuelEngine engine, Side me, Side foe)
        {
            var s = engine.State;
            var cat = engine.Catalog;
            System.Span<CellRef> around = stackalloc CellRef[8];   // outside the loop, once

            foreach (var kv in s.ObjectsOf(me))
            {
                var cell = kv.Key;
                var cre = kv.Value as CreatureUnit;
                if (cre != null)
                {
                    Offer(engine, KPay, new UpkeepPayCommand(me, cell, cre.Id));
                    Offer(engine, KSacrifice, new UpkeepSacrificeCommand(me, cell, cre.Id));

                    int n = Board.Neighbours(cell, around);
                    for (int i = 0; i < n; i++)
                        Offer(engine, KMove, new MoveUnitCommand(me, cell, around[i], cre.Id));

                    CollectAttacks(engine, me, foe, cell, cre);
                }

                var bld = kv.Value as StructureUnit;
                if (bld != null && !bld.DefId.IsNone)
                {
                    var def = cat.Structure(bld.DefId, bld.Color);
                    if (def != null)
                    {
                        for (int i = 0; i < def.UpgradeTargets.Length; i++)
                            Offer(engine, KUpgrade, new UpgradeStructureCommand(
                                me, cell, bld.Id, new StructId(def.UpgradeTargets[i])));
                    }
                }

                var ch = kv.Value as ChargeUnit;
                if (ch != null)
                {
                    Offer(engine, KFlip, new FlipChargeCommand(me, cell, ch.Id));
                    int most = s.P(me).Mana; if (most > 3) most = 3;
                    for (int amount = 1; amount <= most; amount++)
                        Offer(engine, KPour, new PourIntoChargeCommand(me, cell, ch.Id, amount));
                }

                // banked mana is rare enough that pairing every source with every destination
                // costs nothing, and it is the only way this command is ever exercised
                if (kv.Value.Bank > 0)
                {
                    foreach (var other in s.ObjectsOf(me))
                    {
                        if (other.Key == cell) continue;
                        Offer(engine, KSendMana, new SendBankedManaCommand(me, cell, other.Key));
                    }
                }
            }
        }

        void CollectAttacks(DuelEngine engine, Side me, Side foe, CellRef from, CreatureUnit cre)
        {
            var s = engine.State;
            for (int d = 0; d < 2; d++)
            {
                bool defer = d == 1;
                Offer(engine, KDeclare, new DeclareAttackCommand(
                    me, from, cre.Id, new WallTarget(foe), defer));

                for (int z = 0; z < 3; z++)
                    Offer(engine, KDeclare, new DeclareAttackCommand(
                        me, from, cre.Id, new WorkerStackTarget(foe, (WorkerZone)z), defer));

                foreach (var kv in s.ObjectsOf(foe))
                    Offer(engine, KDeclare, new DeclareAttackCommand(
                        me, from, cre.Id, new UnitTarget(kv.Key, kv.Value.Id), defer));
            }
        }

        void CollectHand(DuelEngine engine, Side me)
        {
            var hand = engine.State.P(me).Hand;
            var modes = new[] { PlayMode.Summon, PlayMode.Set, PlayMode.SetTrap, PlayMode.Cast };
            var kindOf = new[] { KSummon, KSet, KSetTrap, KCast };

            for (int h = 0; h < hand.Count; h++)
            {
                for (int m = 0; m < modes.Length; m++)
                {
                    for (int i = 0; i < Board.Cells; i++)
                    {
                        Offer(engine, kindOf[m], new PlayCardCommand(
                            me, h, modes[m], CellRef.FromIndex(i)));
                    }
                }
            }
        }

        void CollectBuilds(DuelEngine engine, Side me)
        {
            var list = engine.Catalog.BuildList(engine.State.P(me).Commander);
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                for (int c = 0; c < Board.Cells; c++)
                {
                    Offer(engine, KBuild, new BuildStructureCommand(
                        me, def.Bid, def.Element, CellRef.FromIndex(c)));
                }
            }
        }

        /// <summary>The engine is the only oracle for legality - nothing here re-implements a rule.</summary>
        void Offer(DuelEngine engine, int kind, ICommand cmd)
        {
            LastProbeCount++;
            if (engine.CanApply(cmd) == Rejection.None) _kinds[kind].Add(cmd);
        }

        // ---- answers -------------------------------------------------------------------------

        /// <summary>
        /// A parked choice, answered at random - with one deliberate exception.
        ///
        /// An ATTACK-trigger response window is answered with the first armed trap, which is what
        /// the JS auto-spring does. The JS has no expressible alternative there: `_resolveNow`
        /// springs the defender's first armed trap itself, mid-resolution, with no seam for an
        /// answer. Randomising it would produce divergences that are the harness's fault rather
        /// than the port's. Summon windows have a real seam on both sides, so those are free.
        /// </summary>
        ICommand Respond(DuelEngine engine, PendingRequest req)
        {
            var side = req.Responder;

            var blk = req as BlockerRequest;
            if (blk != null)
            {
                var chosen = new List<UnitRef>();
                for (int i = 0; i < blk.Eligible.Length; i++)
                    if (_rng.NextInt(2) == 0) chosen.Add(blk.Eligible[i]);

                var cmd = new RespondCommand(side, new BlockersChosen(chosen.ToArray()));
                if (engine.CanApply(cmd) == Rejection.None) return cmd;
                return new RespondCommand(side, new BlockersChosen(new UnitRef[0]));
            }

            var abs = req as AbsorberRequest;
            if (abs != null)
                return new RespondCommand(side, new IndexChosen(
                    abs.Blockers.Length > 0 ? _rng.NextInt(abs.Blockers.Length) : 0));

            var ret = req as RetaliationRequest;
            if (ret != null)
                return new RespondCommand(side, new IndexChosen(
                    ret.Attackers.Length > 0 ? _rng.NextInt(ret.Attackers.Length) : 0));

            var win = req as ResponseWindowRequest;
            if (win != null)
            {
                if (win.ArmedTraps.Length == 0)
                    return new RespondCommand(side, TrapChosen.Passed);

                if (win.Trigger == TrapTrigger.Attack)
                    return new RespondCommand(side, new TrapChosen(win.ArmedTraps[0]));

                int k = _rng.NextInt(win.ArmedTraps.Length + 1);
                return new RespondCommand(side, k == win.ArmedTraps.Length
                    ? TrapChosen.Passed
                    : new TrapChosen(win.ArmedTraps[k]));
            }

            return null;
        }
    }
}
