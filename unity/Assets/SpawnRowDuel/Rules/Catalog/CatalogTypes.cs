using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>Where a spell may point. From the registry's `target` field.</summary>
    public enum SpellTarget : byte { None = 0, Enemy = 1, Building = 2 }

    /// <summary>
    /// The Chrysalis hatch form. Registry data carries exactly name/attack/health (spec 06 s6.6);
    /// everything else the hatched creature keeps from its own instance.
    /// </summary>
    public sealed class HatchForm
    {
        public readonly string Name;
        public readonly int Attack;
        public readonly int Health;

        public HatchForm(string name, int attack, int health)
        {
            Name = name ?? ""; Attack = attack; Health = health;
        }
    }

    /// <summary>
    /// A creature template. Immutable, shared, never part of GameState - instances copy what
    /// mutates (spec 01 s15.1, design 01 s2.7).
    ///
    /// Detonate/Reap/WardHp/Grow/Hatch keep the registry's null-vs-0 distinction. Instantiation
    /// collapses them the way mkCre does (null becomes 0, except WardHp where null becomes 2 -
    /// the JS `t.wardhp||2` default, un-rescaled and therefore a latent quirk, spec 06 s6.3).
    /// </summary>
    public sealed class CreatureCard
    {
        public readonly CardId Id;          // == nm; names are globally unique (validated V2)
        public readonly string Name;
        public readonly Element Element;
        public readonly int PoolIndex;      // 0..7 position inside the element pool
        public readonly int Cost;
        public readonly int Attack;
        public readonly int Health;
        public readonly int Upkeep;
        public readonly bool FirstStrike;
        public readonly bool Entrench;
        public readonly Keyword Keyword;
        public readonly int? Detonate;
        public readonly int? Reap;
        public readonly int? WardHp;
        public readonly int? Grow;
        public readonly int? Hatch;
        public readonly HatchForm Into;     // null when the creature never hatches
        public readonly Tribe Tribe;
        public readonly Subtype Subtype;
        public readonly bool Deckable;      // false for the 4 divine creatures
        public readonly string Slug;        // art lookup key

        public CreatureCard(CardId id, string name, Element element, int poolIndex,
                            int cost, int attack, int health, int upkeep,
                            bool firstStrike, bool entrench, Keyword keyword,
                            int? detonate, int? reap, int? wardHp, int? grow, int? hatch,
                            HatchForm into, Tribe tribe, Subtype subtype, bool deckable, string slug)
        {
            Id = id; Name = name ?? ""; Element = element; PoolIndex = poolIndex;
            Cost = cost; Attack = attack; Health = health; Upkeep = upkeep;
            FirstStrike = firstStrike; Entrench = entrench; Keyword = keyword;
            Detonate = detonate; Reap = reap; WardHp = wardHp; Grow = grow; Hatch = hatch;
            Into = into; Tribe = tribe; Subtype = subtype; Deckable = deckable; Slug = slug ?? "";
        }

        public DeckKey DeckKey
        {
            get { return new DeckKey(Element, Name); }
        }
    }

    public sealed class SpellCard
    {
        public readonly CardId Id;          // == nm
        public readonly string Name;
        public readonly int Cost;
        public readonly bool IsTrap;
        public readonly SpellEffect Effect;
        public readonly int? Value;         // null for effects that carry no number (raze, pitfall...)
        public readonly SpellTarget Target;
        public readonly TrapTrigger Trigger;
        public readonly string Glyph;       // ic - presentation, carried for the view
        public readonly string Slug;

        public SpellCard(CardId id, string name, int cost, bool isTrap, SpellEffect effect,
                         int? value, SpellTarget target, TrapTrigger trigger, string glyph, string slug)
        {
            Id = id; Name = name ?? ""; Cost = cost; IsTrap = isTrap; Effect = effect;
            Value = value; Target = target; Trigger = trigger; Glyph = glyph ?? ""; Slug = slug ?? "";
        }

        public DeckKey DeckKey
        {
            get { return new DeckKey(Element.None, Name); }   // spells are always neutral
        }
    }

    /// <summary>
    /// A structure definition. Forge and Grand Forge are FAMILIES: their Bid stays 'forge' /
    /// 'grandforge' and the per-element instance is selected by Element, because prereq and
    /// upgrade matching work on the family id (spec 05 s2.3).
    /// </summary>
    public sealed class StructureDef
    {
        public readonly string ExportKey;   // "foundry", "forge:fire" - unique row key
        public readonly StructId Bid;       // family id - what board objects and prereqs carry
        public readonly string Name;
        public readonly int Cost;
        public readonly int MaxHp;
        public readonly StructEffect Effect;
        public readonly int Value;
        public readonly int Support;        // MAY be negative (tower = -2)
        public readonly Element Element;    // None for neutral structures
        public readonly string[] Prereqs;         // family bids; empty when none
        public readonly StructId UpgradedFrom;    // None when built from the menu
        public readonly string[] UpgradeTargets;  // family bids (up2)
        public readonly RowGate RowGate;
        public readonly bool Buildable;
        public readonly string Glyph;
        public readonly string Slug;
        public readonly string Description;

        public StructureDef(string exportKey, StructId bid, string name, int cost, int maxHp,
                            StructEffect effect, int value, int support, Element element,
                            string[] prereqs, StructId upgradedFrom, string[] upgradeTargets,
                            RowGate rowGate, bool buildable, string glyph, string slug, string description)
        {
            ExportKey = exportKey ?? ""; Bid = bid; Name = name ?? ""; Cost = cost; MaxHp = maxHp;
            Effect = effect; Value = value; Support = support; Element = element;
            Prereqs = prereqs ?? new string[0];
            UpgradedFrom = upgradedFrom;
            UpgradeTargets = upgradeTargets ?? new string[0];
            RowGate = rowGate; Buildable = buildable;
            Glyph = glyph ?? ""; Slug = slug ?? ""; Description = description ?? "";
        }
    }

    public sealed class CommanderDef
    {
        public readonly CommanderId Id;
        public readonly string Name;
        public readonly int Hp;
        public readonly int Workers;         // wk - dual commanders use half-up rounding (V7)
        public readonly Element[] Colors;    // 1 or 2 entries
        public readonly bool Dual;
        public readonly string[] BuildListRaw;  // "foundry", "forge:fire", ... ORDER IS THE AI PRIORITY
        public readonly string Description;

        public CommanderDef(CommanderId id, string name, int hp, int workers, Element[] colors,
                            bool dual, string[] buildListRaw, string description)
        {
            Id = id; Name = name ?? ""; Hp = hp; Workers = workers;
            Colors = colors ?? new Element[0]; Dual = dual;
            BuildListRaw = buildListRaw ?? new string[0];
            Description = description ?? "";
        }
    }

    /// <summary>One of the 9 elements, with its economy numbers and display identity.</summary>
    public sealed class ElementDef
    {
        public readonly Element El;
        public readonly string Key;          // lowercase wire name
        public readonly string Name;
        public readonly string Glyph;        // the CJK ideograph
        public readonly int Hp;              // commander life for a solo commander of this element
        public readonly int Workers;         // wk - the free back-row workforce
        public readonly bool Deckable;
        public readonly string ColorHex;
        public readonly string AccentHex;
        public readonly string DeepHex;
        public readonly string[] BgHex;      // 3 gradient stops
        public readonly string Lore;

        public ElementDef(Element el, string key, string name, string glyph, int hp, int workers,
                          bool deckable, string colorHex, string accentHex, string deepHex,
                          string[] bgHex, string lore)
        {
            El = el; Key = key ?? ""; Name = name ?? ""; Glyph = glyph ?? "";
            Hp = hp; Workers = workers; Deckable = deckable;
            ColorHex = colorHex ?? ""; AccentHex = accentHex ?? ""; DeepHex = deepHex ?? "";
            BgHex = bgHex ?? new string[0]; Lore = lore ?? "";
        }
    }
}
