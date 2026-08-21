using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Ai
{
    /// <summary>
    /// The individual decision procedures of `foeTurn`, each one separable and testable
    /// (spec 07 s11). They read state and return a choice; they never mutate anything except the
    /// match RNG, and only `PickTarget` does that.
    /// </summary>
    public static class AiChoices
    {
        /// <summary>
        /// aiPickDeploySlot (16_movement.js:20-23). A fixed column preference per row - the AI
        /// builds outward from the middle - falling back to the first free legal slot.
        /// </summary>
        public static readonly int[] CenterOrder = { 3, 1, 5 };
        public static readonly int[] FrontOrder = { 3, 4, 2, 5, 1, 6, 0 };
        public static readonly int[] BackOrder = { 2, 4, 3, 1, 5, 0, 6 };

        public static int[] DeployOrder(SlotName which)
        {
            if (which == SlotName.Center) return CenterOrder;
            return which == SlotName.Front ? FrontOrder : BackOrder;
        }

        /// <summary>The preferred free column of a row, or -1 when the row is full.</summary>
        public static int PickDeploySlot(GameState s, Side owner, SlotName which)
        {
            var row = Board.RowFor(owner, which);
            var order = DeployOrder(which);
            for (int i = 0; i < order.Length; i++)
                if (Board.IsRealSlot(row, order[i]) && s.At(new CellRef(row, order[i])) == null)
                    return order[i];

            for (int col = 0; col < Board.Columns; col++)          // freeDeploySlot fallback
                if (Board.IsRealSlot(row, col) && s.At(new CellRef(row, col)) == null)
                    return col;
            return -1;
        }

        /// <summary>
        /// aiAttackers (17_turns_ai.js:247-250): every untapped, unsick, non-worker creature the
        /// AI owns, WHEREVER it stands - the middle rows are all contested, so a raider deep in
        /// enemy ground attacks from there. Global ROWS order, slots ascending.
        /// </summary>
        public static List<KeyValuePair<CellRef, CreatureUnit>> Attackers(GameState s, Side owner)
        {
            var outp = new List<KeyValuePair<CellRef, CreatureUnit>>();
            foreach (var kv in s.ObjectsOf(owner))
            {
                var c = kv.Value as CreatureUnit;
                if (c == null || c.IsWorker || c.Sick || c.Tapped || c.Hp <= 0) continue;
                outp.Add(new KeyValuePair<CellRef, CreatureUnit>(kv.Key, c));
            }
            return outp;
        }

        /// <summary>yourFieldTargets: everything the enemy owns, anywhere. Columns never matter
        /// in combat - reach is the whole board.</summary>
        public static List<KeyValuePair<CellRef, BoardObject>> FieldTargets(GameState s, Side owner)
        {
            var outp = new List<KeyValuePair<CellRef, BoardObject>>();
            foreach (var kv in s.ObjectsOf(TurnMachine.Other(owner)))
                outp.Add(kv);
            return outp;
        }

        /// <summary>
        /// aiPickTarget (17_turns_ai.js:256-266) - THE ONLY RANDOMISED DECISION IN THE WHOLE AI,
        /// and therefore the only place the match RNG advances on an AI turn.
        ///
        /// Two JS quirks are preserved on purpose and both are flagged:
        ///   * the 60% face-down roll is taken BEFORE the guaranteed-kill check, so the AI
        ///     sometimes rolls its way past lethal (RulesOptions.AiTakesGuaranteedKillFirst);
        ///   * the kill test reads RAW attack, not effA, so an Overcharge attacker's banked
        ///     discharge does not count toward "can I kill it" (spec 07 s18 bug 2).
        /// </summary>
        public static AttackTarget PickTarget(GameState s, Side owner, CreatureUnit m,
                                              AiTuning tuning)
        {
            var field = FieldTargets(s, owner);

            KeyValuePair<CellRef, BoardObject> best;

            if (!s.Options.AiTakesGuaranteedKillFirst)
            {
                var charge = BestFundedCharge(field);
                if (charge.Value != null && s.Random.Chance(tuning.FaceDownRollPercent, 100))
                    return new UnitTarget(charge.Key, charge.Value.Id);
            }

            best = CheapestKill(field, m);                      // 100% - lethal is never declined
            if (best.Value != null) return new UnitTarget(best.Key, best.Value.Id);

            if (s.Options.AiTakesGuaranteedKillFirst)
            {
                var charge = BestFundedCharge(field);
                if (charge.Value != null && s.Random.Chance(tuning.FaceDownRollPercent, 100))
                    return new UnitTarget(charge.Key, charge.Value.Id);
            }

            var bld = FrailestStructure(field);
            if (bld.Value != null && s.Random.Chance(tuning.StructureRollPercent, 100))
                return new UnitTarget(bld.Key, bld.Value.Id);

            return new WallTarget(TurnMachine.Other(owner));    // storm the wall for life damage
        }

        /// <summary>Face-downs worth cracking: invested >= 2, richest first, ties by board order.</summary>
        static KeyValuePair<CellRef, BoardObject> BestFundedCharge(
            List<KeyValuePair<CellRef, BoardObject>> field)
        {
            KeyValuePair<CellRef, BoardObject> best = default(KeyValuePair<CellRef, BoardObject>);
            int bestInv = 0;
            for (int i = 0; i < field.Count; i++)
            {
                var ch = field[i].Value as ChargeUnit;
                if (ch == null || ch.Invested < 2) continue;
                if (best.Value == null || ch.Invested > bestInv)
                {
                    best = field[i];
                    bestInv = ch.Invested;
                }
            }
            return best;
        }

        /// <summary>A creature this attacker's RAW attack would finish, frailest first.</summary>
        static KeyValuePair<CellRef, BoardObject> CheapestKill(
            List<KeyValuePair<CellRef, BoardObject>> field, CreatureUnit m)
        {
            KeyValuePair<CellRef, BoardObject> best = default(KeyValuePair<CellRef, BoardObject>);
            for (int i = 0; i < field.Count; i++)
            {
                var c = field[i].Value as CreatureUnit;
                if (c == null || c.IsWorker || m.Attack < c.Hp) continue;
                if (best.Value == null || c.Hp < ((CreatureUnit)best.Value).Hp) best = field[i];
            }
            return best;
        }

        static KeyValuePair<CellRef, BoardObject> FrailestStructure(
            List<KeyValuePair<CellRef, BoardObject>> field)
        {
            KeyValuePair<CellRef, BoardObject> best = default(KeyValuePair<CellRef, BoardObject>);
            for (int i = 0; i < field.Count; i++)
            {
                var b = field[i].Value as StructureUnit;
                if (b == null) continue;
                if (best.Value == null || b.Hp < ((StructureUnit)best.Value).Hp) best = field[i];
            }
            return best;
        }

        /// <summary>
        /// The AI's gang-block absorber (17_turns_ai.js:349-351): dump the blow on a blocker it
        /// actually kills - the frailest such - and otherwise on the TOUGHEST, which is almost
        /// certainly backwards and is why RulesOptions.AbsorberIsWeakestBlocker exists.
        /// </summary>
        public static int PickAbsorber(GameState s, AbsorberRequest req)
        {
            CellRef at;
            bool onBoard;
            var attacker = s.FindById(req.AttackerId, out at, out onBoard) as CreatureUnit;
            int power = attacker != null ? attacker.EffectiveAttack : 0;

            int kill = -1, killHp = 0;
            for (int i = 0; i < req.Blockers.Length; i++)
            {
                var b = s.FindById(req.Blockers[i].UnitId, out at, out onBoard) as CreatureUnit;
                if (b == null || b.Hp > power) continue;
                if (kill < 0 || b.Hp < killHp) { kill = i; killHp = b.Hp; }
            }
            if (kill >= 0) return kill;

            int pick = 0, pickHp = -1;
            for (int i = 0; i < req.Blockers.Length; i++)
            {
                var b = s.FindById(req.Blockers[i].UnitId, out at, out onBoard) as CreatureUnit;
                if (b == null) continue;
                bool better = s.Options.AbsorberIsWeakestBlocker
                    ? (pickHp < 0 || b.Hp < pickHp)
                    : (pickHp < 0 || b.Hp > pickHp);
                if (better) { pick = i; pickHp = b.Hp; }
            }
            return pick;
        }
    }
}
