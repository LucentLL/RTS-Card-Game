namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Anything that occupies a board cell.
    ///
    /// Owner is THE ownership authority. It is never inferred from which array an object sits in -
    /// the front rows are contested and enemy raiders legally stand in them, so reading ownership
    /// from position is the bug that silently converted a foe raider in your front row into your
    /// own unit (spec 01 s13.2).
    ///
    /// Deliberately absent, per spec 01 s15.1 and spec 06 s11.5: art, ic, desc, laid, cc, ward,
    /// target. Those are presentation or dead. `laid` in particular is DERIVED - the core answers
    /// "can this act now" as a query and never stores a pose.
    /// </summary>
    public abstract class BoardObject
    {
        public int Id;          // from GameState.NextUid - unique, serialized, never reused
        public Side Owner;
        public UnitKind Kind;
        public Element Color;   // resolved at construction; never None for a real unit
        public int Bank;        // banked mana riding on the card

        public abstract BoardObject Clone();

        protected void CopyBaseTo(BoardObject o)
        {
            o.Id = Id; o.Owner = Owner; o.Kind = Kind; o.Color = Color; o.Bank = Bank;
        }
    }

    public sealed class CreatureUnit : BoardObject
    {
        public CardId Card;
        public string Name = "";     // denormalised for logs; the catalog stays authoritative

        public int Attack, Hp, MaxHp, Cost, Upkeep;
        public bool FirstStrike, Entrench, IsWorker, IsToken;
        public Keyword Keyword;
        public int Detonate, Reap, WardHp, Grow, Hatch;
        public CardId Into;          // catalog key, never an object reference

        public int ChrysalisCount;   // cnt  - persists across turns
        public int OverchargeBank;   // oc   - persists across turns
        public int DischargeBonus;   // _dis - transient, cleared every resolution

        public Tribe Tribe;
        public Subtype Subtype;

        // Per-turn flags. All cleared at the OWNER's own BeginTurn (spec 03 s2.1) - not at the
        // start of every turn, which is why a raider stays tapped through the enemy's turn.
        public bool Sick, Tapped, Moved, MovedTwice, PaidUpkeep, HasBlocked;

        public CreatureUnit() { Kind = UnitKind.Creature; }

        /// <summary>effA - what this creature actually hits for right now.</summary>
        public int EffectiveAttack { get { return Attack + DischargeBonus; } }

        public override BoardObject Clone()
        {
            var c = new CreatureUnit();
            CopyBaseTo(c);
            c.Card = Card; c.Name = Name;
            c.Attack = Attack; c.Hp = Hp; c.MaxHp = MaxHp; c.Cost = Cost; c.Upkeep = Upkeep;
            c.FirstStrike = FirstStrike; c.Entrench = Entrench; c.IsWorker = IsWorker; c.IsToken = IsToken;
            c.Keyword = Keyword;
            c.Detonate = Detonate; c.Reap = Reap; c.WardHp = WardHp; c.Grow = Grow; c.Hatch = Hatch;
            c.Into = Into;
            c.ChrysalisCount = ChrysalisCount; c.OverchargeBank = OverchargeBank; c.DischargeBonus = DischargeBonus;
            c.Tribe = Tribe; c.Subtype = Subtype;
            c.Sick = Sick; c.Tapped = Tapped; c.Moved = Moved; c.MovedTwice = MovedTwice;
            c.PaidUpkeep = PaidUpkeep; c.HasBlocked = HasBlocked;
            return c;
        }
    }

    public sealed class StructureUnit : BoardObject
    {
        /// <summary>None means a legacy hand-built structure, which is NEVER upgradeable.</summary>
        public StructId DefId;

        public int Hp, MaxHp, Cost, Value;

        /// <summary>May be NEGATIVE - Cannon Tower is -2. Worker capacity math depends on that.</summary>
        public int Support;

        public StructEffect Effect;

        /// <summary>Always false today. Kept so the guard sites stay alive and typed.</summary>
        public bool IsCommandCenter;

        public StructureUnit() { Kind = UnitKind.Building; }

        public override BoardObject Clone()
        {
            var b = new StructureUnit();
            CopyBaseTo(b);
            b.DefId = DefId;
            b.Hp = Hp; b.MaxHp = MaxHp; b.Cost = Cost; b.Value = Value; b.Support = Support;
            b.Effect = Effect; b.IsCommandCenter = IsCommandCenter;
            return b;
        }
    }

    /// <summary>A face-down creature or structure. Flipping it spends the banked investment.</summary>
    public sealed class ChargeUnit : BoardObject
    {
        public SlotName SetIn;
        public bool IsStructure;      // ctype
        public CardSnapshot Card;     // frozen value type
        public int Invested;          // starts at 1 - the mana the set itself cost
        public int SetTurn;

        public ChargeUnit() { Kind = UnitKind.Charge; }

        public override BoardObject Clone()
        {
            var c = new ChargeUnit();
            CopyBaseTo(c);
            c.SetIn = SetIn; c.IsStructure = IsStructure; c.Card = Card;
            c.Invested = Invested; c.SetTurn = SetTurn;
            return c;
        }
    }

    public sealed class TrapUnit : BoardObject
    {
        public SlotName SetIn;
        public CardId Card;
        public SpellEffect Effect;
        public int Value;
        public TrapTrigger Trigger;
        public int SetTurn;

        public TrapUnit() { Kind = UnitKind.Trap; }

        /// <summary>A trap is never armed on the turn it was set.</summary>
        public bool IsArmed(int turnNo) { return turnNo > SetTurn; }

        public override BoardObject Clone()
        {
            var t = new TrapUnit();
            CopyBaseTo(t);
            t.SetIn = SetIn; t.Card = Card; t.Effect = Effect; t.Value = Value;
            t.Trigger = Trigger; t.SetTurn = SetTurn;
            return t;
        }
    }

    /// <summary>
    /// The frozen card data a face-down charge carries. Copies exactly what the JS snapshot copies,
    /// PLUS Color - the JS drops it, so a flipped off-colour creature inherits the player's element
    /// instead of its own (spec 04 s13.2, flagged as a bug). Whether we reproduce that is governed
    /// by RulesOptions.FaceDownKeepsColor, which defaults to JS-faithful so parity tests pass.
    /// </summary>
    public readonly struct CardSnapshot
    {
        public readonly CardId Id;
        public readonly string Name;
        public readonly Element Color;
        public readonly int Cost, Attack, Health, Upkeep;
        public readonly Keyword Keyword;
        public readonly bool FirstStrike, Entrench;
        public readonly StructId StructDef;

        public CardSnapshot(CardId id, string name, Element color, int cost, int attack, int health,
                            int upkeep, Keyword keyword, bool firstStrike, bool entrench, StructId structDef)
        {
            Id = id; Name = name ?? ""; Color = color;
            Cost = cost; Attack = attack; Health = health; Upkeep = upkeep;
            Keyword = keyword; FirstStrike = firstStrike; Entrench = entrench; StructDef = structDef;
        }
    }
}
