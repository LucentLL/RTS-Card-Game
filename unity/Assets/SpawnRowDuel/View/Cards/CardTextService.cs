using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// Every word of rules text on a card, GENERATED from card data (spec 09 §6.6 [REQ]).
    ///
    /// The reference build had four functions doing this - `spellText`, `abilityBrief`, `kwName`
    /// and `bldEffectText` - and the requirement they encode is that no card ever carries authored
    /// prose. Change Bolt's damage in the registry and the sentence changes with it; add a keyword
    /// and every card that has it gains a label. This is that, one class, with the format strings
    /// in one place ready to become a localisation table.
    ///
    /// Markup is TextCore rich text (`&lt;b&gt;`), which UI Toolkit renders directly - the same tags
    /// the HTML used, which is why the sentences read identically.
    /// </summary>
    public sealed class CardTextService
    {
        readonly ICardCatalog _catalog;

        public CardTextService(ICardCatalog catalog) { _catalog = catalog; }

        /// <summary>"Human Wizard" - tribe and subtype, when the card has them.</summary>
        public string TypeLine(CreatureCard c)
        {
            if (c == null) return "";
            var parts = new List<string>();
            if (c.Tribe != Tribe.None) parts.Add(c.Tribe.ToString());
            if (c.Subtype != Subtype.None) parts.Add(c.Subtype.ToString());
            return string.Join(" ", parts);
        }

        /// <summary>The short keyword label: "Detonate 1500", "Reap 500", "Ward".</summary>
        public string KeywordLabel(Keyword kw, CreatureCard c)
        {
            switch (kw)
            {
                case Keyword.Detonate: return "Detonate " + (c != null && c.Detonate.HasValue ? c.Detonate.Value.ToString() : "");
                case Keyword.Undertow: return "Undertow";
                case Keyword.Entrench: return "Entrench";
                case Keyword.Ward: return "Ward";
                case Keyword.Reap: return "Reap " + (c != null && c.Reap.HasValue ? c.Reap.Value.ToString() : "");
                case Keyword.Chrysalis: return "Chrysalis";
                case Keyword.Scour: return "Scour";
                case Keyword.Overcharge: return "Overcharge";
                default: return "";
            }
        }

        /// <summary>spellText: one sentence per EFFECT, never per card name.</summary>
        public string SpellText(SpellCard s)
        {
            if (s == null) return "A spell.";
            int val = s.Value.HasValue ? s.Value.Value : 0;
            switch (s.Effect)
            {
                case SpellEffect.Burn:
                    return "<b>Bolt.</b> Deal <b>" + val + "</b> damage to an enemy creature, structure, or face-down card.";
                case SpellEffect.Raze:
                    return "<b>Sunder.</b> Destroy a target enemy <b>structure</b>.";
                case SpellEffect.Pitfall:
                    return "<b>Snare.</b> When your opponent <b>summons</b> a creature, destroy it.";
                case SpellEffect.Chain:
                    return "<b>Arc.</b> Deal <b>" + val + "</b> to the two highest-attack enemy creatures.";
                case SpellEffect.Bounce:
                    return "<b>Riptide.</b> Return target enemy creature to its owner's hand (Entrench resists).";
                case SpellEffect.Thornmail:
                    return "<b>Overgrowth.</b> When your line is struck, the defending creature gains <b>+500/+1000</b> permanently.";
                default:
                    return "A spell.";
            }
        }

        /// <summary>abilityBrief for a creature: upkeep · first strike · keyword.</summary>
        public string CreatureBrief(CreatureCard c)
        {
            if (c == null) return "";
            var parts = new List<string>();
            if (c.Upkeep > 0) parts.Add("<b>Upkeep ⚒-" + c.Upkeep + "</b>");
            if (c.FirstStrike) parts.Add("<b>First Strike</b>");
            var kw = KeywordLabel(c.Keyword, c);
            if (kw.Length > 0) parts.Add("<b>" + kw.Trim() + "</b>");
            return string.Join(" · ", parts);
        }

        /// <summary>abilityBrief for a structure: its effect, then its worker clause.</summary>
        public string StructureBrief(StructureDef d)
        {
            if (d == null) return "";
            var parts = new List<string>();
            switch (d.Effect)
            {
                case StructEffect.Mana: parts.Add("<b>Forge.</b> ◆" + d.Value + " each turn"); break;
                case StructEffect.Villager: parts.Add("<b>Longhouse.</b> Trains a worker each turn"); break;
                case StructEffect.Damage: parts.Add("<b>Tower.</b> ⚔" + d.Value + " each turn"); break;
                case StructEffect.Wall: parts.Add("<b>Bulwark.</b> Screens the line"); break;
                case StructEffect.Revive: parts.Add("<b>Reliquary.</b> Recalls the fallen"); break;
                case StructEffect.Vault: parts.Add("<b>Vault.</b> Banks ◆" + d.Value + " past your turn"); break;
            }
            if (d.Support != 0) parts.Add("⚒" + (d.Support > 0 ? "+" : "") + d.Support + " workers");
            return string.Join(" · ", parts);
        }

        /// <summary>bldEffectText: the inspector's full paragraph, plus the support clause.</summary>
        public string StructureFull(StructEffect eff, int val, int sup)
        {
            string supTxt = sup > 0
                ? " Raises <b>⚒+" + sup + "</b> in its row."
                : sup < 0
                    ? " Costs <b>⚒" + sup + "</b> in its row — build it where workers are to spare."
                    : "";

            switch (eff)
            {
                case StructEffect.Mana:
                    return "<b>Forge.</b> Yields <b>◆" + val + " mana</b> at the start of its owner's turn." + supTxt;
                case StructEffect.Villager:
                    return "<b>Longhouse.</b> Trains a free <b>Minion</b> (0/2 ⚒) into its owner's base pool at the start of its owner's turn." + supTxt;
                case StructEffect.Wall:
                    return "<b>Bulwark.</b> A heavy body that screens the line — it can intercept and be raided, but never moves or attacks." + supTxt;
                case StructEffect.Damage:
                    return "<b>Cannon Tower.</b> Strikes the nearest enemy creature for <b>" + val + "</b> at the start of its owner's turn." + supTxt;
                case StructEffect.Vault:
                    return "<b>Mana Vault.</b> Unspent mana <b>drains at the end of your turn</b> — your vaults keep up to <b>◆" + val + "</b> of it banked. Upgrade it to hold more." + supTxt;
                case StructEffect.Revive:
                    return "<b>Reliquary.</b> Once per turn at upkeep, returns your most recently fallen creature to your hand." + supTxt;
                default:
                    return "Structure with no upkeep effect." + supTxt;
            }
        }

        /// <summary>The full inspector text for any card the catalog knows.</summary>
        public string Full(CardId id)
        {
            var creature = _catalog.Creature(id);
            if (creature != null)
            {
                var brief = CreatureBrief(creature);
                if (creature.Into != null)
                    brief += (brief.Length > 0 ? " · " : "")
                        + "<b>Chrysalis</b> — hatches into " + creature.Into.Name
                        + " (" + creature.Into.Attack + "/" + creature.Into.Health + ")";
                return brief;
            }

            var spell = _catalog.Spell(id);
            if (spell != null) return SpellText(spell);

            return "";
        }
    }
}
