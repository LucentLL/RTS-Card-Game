using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The normative 12-step BeginTurn pipeline (design 01 s4.2, startTurn 17_turns_ai.js:49-71)
    /// and the harvest algorithm (doHarvest, :147-174). Run by BOTH sides - the AI gets no
    /// private phase anomaly here.
    /// </summary>
    public static class TurnPipeline
    {
        public static void BeginTurn(GameState s, Side owner, ICardCatalog cat, EventSink ev)
        {
            s.TurnNumber++;                                    // 1. the PLY counter
            s.Turn = owner;                                    // 2.

            s.Combat.Clear();                                  // 3. declarations do not survive
            s.Pending = null;                                  //    a turn boundary

            Array.Clear(s.P(owner).UpkeepPaid, 0,              // 4. last turn's keep payments EXPIRE
                        s.P(owner).UpkeepPaid.Length);

            foreach (var kv in s.ObjectsOf(owner))             // 5. THIS side's board units only
            {
                var c = kv.Value as CreatureUnit;
                if (c == null) continue;
                c.Sick = false; c.Tapped = false; c.Moved = false; c.MovedTwice = false;
                c.PaidUpkeep = false; c.HasBlocked = false; c.DischargeBonus = 0;
            }

            UpkeepKeywords.ChrysalisTick(s, owner, cat, ev);   // 6. may hatch; always re-sicks
            UpkeepKeywords.OverchargeTick(s, owner, ev);       // 7. oc = min(3, oc+1)
            StructureUpkeep.Tick(s, owner, cat, ev);           // 8. mana -> tower fire -> revive
            DeathSweep.Cleanup(s, cat, ev);                    // 9. sweep what the tower killed
            WorkerMath.Resync(s, owner, cat);                  // 10. pools from the board as it now is
            s.P(owner).ReadyWorkers();                         // 11. the ONLY un-sick/un-tap of workers

            TurnMachine.SetPhase(s, TurnPhase.Upkeep, ev);     // 12. both sides - no phase anomaly
            ev.Add(new TurnStarted(owner, s.TurnNumber));
        }

        /// <summary>
        /// doHarvest, exactly - including the deliberately STALE owe (captured before
        /// harvesting, paid after) and the "credit the full remaining deficit even when only
        /// partially paid" anti-deadlock rule. The offender gate lives in the handler's
        /// Validate; by the time this runs, any shortfall left is orphaned.
        /// </summary>
        public static void Harvest(GameState s, Side owner, ICardCatalog cat, EventSink ev)
        {
            int owe = Upkeep.TotalDeficit(s, owner, cat);      // captured BEFORE harvesting

            for (int z = 0; z < 3; z++)                        // back, front, center - fixed order
            {
                var pool = s.P(owner).Workers[z];
                int ready = pool.ReadyCount;
                if (ready <= 0) continue;

                int total = ready * 1;                         // minYield: every row harvests 1
                for (int i = 0; i < pool.Members.Count; i++)
                    if (!pool.Members[i].Sick) pool.Members[i].Tapped = true;

                Mana.Add(s, owner, total, ev);
                ev.Add(new HarvestCollected(owner, (WorkerZone)z, total));
            }

            if (owe > 0)
            {
                // A purely structural shortfall: pay what the till can bear...
                int pay = Math.Min(owe, s.P(owner).Mana);
                if (pay > 0) Mana.TrySpend(s, owner, pay);

                // ...but credit EVERY deficit zone in full - the turn must never dead-lock.
                for (int z = 0; z < Upkeep.SettleOrder.Length; z++)
                {
                    var zone = Upkeep.SettleOrder[z];
                    int deficit = Upkeep.ZoneDeficit(s, owner, zone, cat);
                    if (deficit > 0)
                    {
                        s.P(owner).UpkeepPaid[(int)zone] += deficit;
                        ev.Add(new WorkerShortfallSettled(owner, zone, SettleKind.Pay, 0));
                    }
                }
            }

            TurnMachine.SetPhase(s, TurnPhase.Draw, ev);
        }
    }
}
