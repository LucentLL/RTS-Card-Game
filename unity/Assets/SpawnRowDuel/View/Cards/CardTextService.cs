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
                case Keyword.Detonate: return "Detonate " + (c != null && c.Detonate.HasValue ? Stat.Num(c.Detonate.Value) : "");
                case Keyword.Undertow: return "Undertow";
                case Keyword.Entrench: return "Entrench";
                case Keyword.Ward: return "Ward";
                case Keyword.Reap: return "Reap " + (c != null && c.Reap.HasValue ? Stat.Num(c.Reap.Value) : "");
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
                    return "<b>Bolt.</b> Deal <b>" + Stat.Num(val) + "</b> damage to an enemy creature, structure, or face-down card.";
                case SpellEffect.Raze:
                    return "<b>Sunder.</b> Destroy a target enemy <b>structure</b>.";
                case SpellEffect.Pitfall:
                    return "<b>Snare.</b> When your opponent <b>summons</b> a creature, destroy it.";
                case SpellEffect.Chain:
                    return "<b>Arc.</b> Deal <b>" + Stat.Num(val) + "</b> to the two highest-attack enemy creatures.";
                case SpellEffect.Bounce:
                    return "<b>Riptide.</b> Return target enemy creature to its owner's hand (Entrench resists).";
                case SpellEffect.Thornmail:
                    return "<b>Overgrowth.</b> When your line is struck, the defending creature gains <b>+50/+100</b> permanently.";
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
                case StructEffect.Damage: parts.Add("<b>Tower.</b> ⚔" + Stat.Num(d.Value) + " each turn"); break;
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
                    return "<b>Longhouse.</b> Trains a free <b>Minion</b> (0/100 ⚒) into its owner's base pool at the start of its owner's turn." + supTxt;
                case StructEffect.Wall:
                    return "<b>Bulwark.</b> A heavy body that screens the line — it can intercept and be raided, but never moves or attacks." + supTxt;
                case StructEffect.Damage:
                    return "<b>Cannon Tower.</b> Strikes the nearest enemy creature for <b>" + Stat.Num(val) + "</b> at the start of its owner's turn." + supTxt;
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
            CreatureCard creature;
            if (_catalog.TryCreature(id, out creature))
                return CreatureFull(creature, KeywordEngine.TextOf(creature, _catalog));

            SpellCard spell;
            if (_catalog.TrySpell(id, out spell)) return SpellText(spell);

            return "";
        }

        /// <summary>
        /// The inspector's paragraph for a creature: the ability line it prints on the card, and
        /// then a SENTENCE for every named thing on that line.
        ///
        /// This is "there was no way to see what the effect did". A card that says <i>First Strike
        /// Chrysalis</i> and nothing else has named two rules the player has never been shown, and
        /// the hand card's three-word brief is the only text most cards ever get. The sentences
        /// were not missing - each keyword handler has carried its own since M10 (kwText) - they
        /// simply had no reader; <see cref="KeywordEngine.TextOf(CreatureCard, ICardCatalog)"/> is
        /// the door to them, so the wording stays with the rule that implements it and cannot
        /// drift from it.
        ///
        /// First Strike, Entrench and upkeep are NOT keywords - they are flags with no handler -
        /// so their sentences are written here, next to the labels that name them.
        ///
        /// <paramref name="keywordText"/> is passed in rather than looked up so the board can hand
        /// over the LIVE unit's sentence, where a cocoon's swell count is a real number instead of
        /// the printed zero.
        /// </summary>
        public string CreatureFull(CreatureCard c, string keywordText)
        {
            if (c == null) return "";

            // NOT the brief line as well. The brief is a list of LABELS - "Upkeep ⚒-3 · First
            // Strike · Chrysalis" - and every one of those labels is the opening of a sentence
            // below it, so printing both says each name twice and spends half a small paper box
            // saying nothing new.
            var lines = new List<string>();

            if (c.Upkeep > 0)
                lines.Add("<b>Upkeep ⚒-" + c.Upkeep + ".</b> Holds " + c.Upkeep
                    + " worker" + (c.Upkeep == 1 ? "" : "s") + " off the harvest in its row — "
                    + "move it, pay, or sacrifice it when the row cannot support it.");

            if (c.FirstStrike)
                lines.Add("<b>First Strike.</b> Strikes in its own tier, before ordinary blows "
                    + "land — anything it kills there never hits back.");

            // The bool and the keyword are two different things wearing one name; a creature with
            // the KEYWORD gets the handler's sentence below instead of this one.
            if (c.Entrench && c.Keyword != Keyword.Entrench)
                lines.Add("<b>Entrench.</b> Immovable — cannot be bounced or pushed.");

            if (!string.IsNullOrEmpty(keywordText)) lines.Add(Emphasise(keywordText));

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Bold the leading clause of a rules sentence - "Detonate 1500." - so a paragraph of them
        /// scans as a list of named rules rather than as prose.
        ///
        /// The rules core writes plain sentences on purpose: it has no business knowing about rich
        /// text. The markup is the view's, and it goes on here.
        /// </summary>
        static string Emphasise(string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return "";
            int stop = sentence.IndexOf('.');
            if (stop < 0 || stop + 1 >= sentence.Length) return "<b>" + sentence + "</b>";
            return "<b>" + sentence.Substring(0, stop + 1) + "</b>" + sentence.Substring(stop + 1);
        }
    }
}
