using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>A card sitting in hand or deck. Value type - decks are just ordered lists of these.</summary>
    public readonly struct HandCard
    {
        public readonly CardId Id;
        public readonly Element Color;
        public HandCard(CardId id, Element color) { Id = id; Color = color; }
    }

    /// <summary>What a death leaves behind. Kept for revive and for the graveyard viewer.</summary>
    public readonly struct GraveRecord
    {
        public readonly CardId Id;
        public readonly Element Color;
        public readonly UnitKind Kind;
        public readonly int TurnDied;

        public GraveRecord(CardId id, Element color, UnitKind kind, int turnDied)
        {
            Id = id; Color = color; Kind = kind; TurnDied = turnDied;
        }
    }

    /// <summary>
    /// One zone's materialised worker bodies.
    ///
    /// This is the genuinely awkward corner of the model, and it is awkward on purpose. The worker
    /// FIGURE is derived on every read from structures minus monsters. The worker POOL is a
    /// materialised list whose members carry sick/tapped state that must survive a resync. The two
    /// are deliberately allowed to disagree: cleanup() does NOT resync, so a razed structure leaves
    /// harvestable workers standing until the next syncWorkers. That is observable behaviour and
    /// reproducing it is a requirement, not an accident (spec 02 s6.4, spec 05 s5.2).
    ///
    /// There is no Raid pool - no support exists behind enemy lines.
    /// </summary>
    public sealed class WorkerPool
    {
        public readonly List<CreatureUnit> Members = new List<CreatureUnit>();

        public int Count { get { return Members.Count; } }

        /// <summary>
        /// syncWorkers. Shrinks by popping the TAIL and leaves NO grave record - a worker that
        /// evaporates because its structure fell was never really a card. Grows by pushing bodies
        /// that arrive SICK, so a worker created this turn cannot be harvested this turn.
        /// </summary>
        public void Resync(int target, Func<CreatureUnit> makeWorker)
        {
            if (target < 0) target = 0;
            while (Members.Count > target) Members.RemoveAt(Members.Count - 1);
            while (Members.Count < target)
            {
                var w = makeWorker();
                w.Sick = true;
                Members.Add(w);
            }
        }

        /// <summary>readyWorkers - only ever called at turn start, never after an upkeep settle.</summary>
        public void Ready()
        {
            for (int i = 0; i < Members.Count; i++)
            {
                var m = Members[i];
                m.Sick = false; m.Tapped = false; m.Moved = false;
            }
        }

        /// <summary>Workers that could be tapped for harvest right now.</summary>
        public int ReadyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Members.Count; i++)
                    if (!Members[i].Sick && !Members[i].Tapped) n++;
                return n;
            }
        }

        public WorkerPool Clone()
        {
            var p = new WorkerPool();
            for (int i = 0; i < Members.Count; i++)
                p.Members.Add((CreatureUnit)Members[i].Clone());
            return p;
        }
    }

    public sealed class PlayerState
    {
        public Element PrimaryColor;
        public CommanderId Commander;
        public int Life;
        public int Mana;

        public readonly List<HandCard> Hand = new List<HandCard>();
        public readonly List<HandCard> Deck = new List<HandCard>();   // draw from the END
        public readonly List<GraveRecord> Grave = new List<GraveRecord>();

        /// <summary>Indexed by WorkerZone: Back, Front, Center. Raid has no pool.</summary>
        public readonly WorkerPool[] Workers = { new WorkerPool(), new WorkerPool(), new WorkerPool() };

        /// <summary>Indexed by WorkerZone (all four). Reset every BeginTurn.</summary>
        public readonly int[] UpkeepPaid = new int[4];

        public WorkerPool Pool(WorkerZone z)
        {
            if (z == WorkerZone.Raid) return null;   // by design - no support behind enemy lines
            return Workers[(int)z];
        }

        /// <summary>readyWorkers - all three pools. Only ever called at turn start and match start.</summary>
        public void ReadyWorkers()
        {
            for (int i = 0; i < Workers.Length; i++) Workers[i].Ready();
        }

        public PlayerState Clone()
        {
            var p = new PlayerState
            {
                PrimaryColor = PrimaryColor,
                Commander = Commander,
                Life = Life,
                Mana = Mana,
            };
            p.Hand.AddRange(Hand);
            p.Deck.AddRange(Deck);
            p.Grave.AddRange(Grave);
            for (int i = 0; i < Workers.Length; i++) p.Workers[i] = Workers[i].Clone();
            Array.Copy(UpkeepPaid, p.UpkeepPaid, UpkeepPaid.Length);
            return p;
        }
    }
}
