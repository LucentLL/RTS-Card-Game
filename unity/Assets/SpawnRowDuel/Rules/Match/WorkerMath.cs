using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The per-row worker model, and nothing else. Workers are a DERIVED figure - structures
    /// minus monsters, plus the commander's free back-row workforce - materialised into pools
    /// only so bodies can be tapped, intercept and be raided (spec 02 s6).
    ///
    /// The previous global-cap model (workerCap, canTrain, enforceCap, trainVillager) is verified
    /// dead code and is deliberately not here (spec 02 s6.5).
    /// </summary>
    public static class WorkerMath
    {
        /// <summary>
        /// rowWorkers. MAY BE NEGATIVE - that is the upkeep shortfall. Only the owner's own
        /// objects count; a raider standing in the row contributes to ITS owner's raid zone,
        /// not to this row's figure.
        /// </summary>
        public static int RowWorkers(GameState s, Side owner, WorkerZone zone, ICardCatalog cat)
        {
            int sum = 0;
            var rows = Board.RowsOfZone(owner, zone);
            for (int r = 0; r < rows.Length; r++)
            {
                for (int col = 0; col < Board.Columns; col++)
                {
                    var o = s.At(new CellRef(rows[r], col));
                    if (o == null || o.Owner != owner) continue;

                    var b = o as StructureUnit;
                    if (b != null)
                    {
                        sum += b.Support + (b.Effect == StructEffect.Villager ? b.Value : 0);
                        continue;
                    }

                    var c = o as CreatureUnit;
                    if (c != null && !c.IsWorker) sum -= c.Upkeep;

                    // Face-down charges and traps contribute nothing (spec 02 s6.3).
                }
            }

            if (zone == WorkerZone.Back)
                sum += cat.Commander(s.P(owner).Commander).Workers;   // the homeland staffs the back row

            return sum;
        }

        /// <summary>The HUD number: negatives clamp to 0, raid excluded.</summary>
        public static int TotalWorkers(GameState s, Side owner, ICardCatalog cat)
        {
            int total = 0;
            total += Math.Max(0, RowWorkers(s, owner, WorkerZone.Back, cat));
            total += Math.Max(0, RowWorkers(s, owner, WorkerZone.Front, cat));
            total += Math.Max(0, RowWorkers(s, owner, WorkerZone.Center, cat));
            return total;
        }

        /// <summary>mkVil: Worker 0/1000, cost 0, upkeep 0, the player's colour.</summary>
        public static CreatureUnit MakeWorker(GameState s, Side owner, ICardCatalog cat)
        {
            var t = cat.WorkerTemplate;
            var w = new CreatureUnit();
            w.Id = s.NewUid();
            w.Owner = owner;
            w.Color = s.P(owner).PrimaryColor;
            w.Card = t.Id;
            w.Name = t.Name;
            w.Attack = t.Attack;
            w.Hp = t.Health;
            w.MaxHp = t.Health;
            w.Cost = t.Cost;
            w.Upkeep = t.Upkeep;
            w.IsWorker = true;
            return w;
        }

        /// <summary>
        /// syncWorkers: each pool is trimmed from the tail (no grave record - a worker that
        /// evaporates was never really a card) or grown with bodies that arrive SICK, so a
        /// structure raised mid-turn cannot harvest that same turn.
        ///
        /// The complete call-site list lives in spec 02 s6.4; a missing call leaves a stale pool
        /// and an extra call is a behaviour change. Cleanup deliberately does NOT call this.
        /// </summary>
        public static void Resync(GameState s, Side owner, ICardCatalog cat)
        {
            var p = s.P(owner);
            for (int z = 0; z < p.Workers.Length; z++)
            {
                int target = Math.Max(0, RowWorkers(s, owner, (WorkerZone)z, cat));
                p.Workers[z].Resync(target, delegate { return MakeWorker(s, owner, cat); });
            }
        }
    }
}
