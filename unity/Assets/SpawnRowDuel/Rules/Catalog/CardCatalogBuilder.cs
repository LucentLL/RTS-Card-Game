using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// CardsJsonDoc rows -> pure catalog records. Every enum string resolves through a fail-loud
    /// parser that names the offending card: a typo'd keyword must break the build, never ship as
    /// a silently-vanilla card (design 03 s5.4 rule 6, validation V11).
    /// </summary>
    public static class CardCatalogBuilder
    {
        public static CardCatalog Build(CardsJsonDoc doc)
        {
            var elements = new List<ElementDef>(doc.Elements.Count);
            foreach (var r in doc.Elements)
            {
                var el = ParseElementReq(r.Id, "element '" + r.Id + "'");
                elements.Add(new ElementDef(el, r.Id, r.Name, r.Glyph, r.Hp, r.Wk, r.Deckable,
                                            r.ColorHex, r.AccentHex, r.DeepHex, r.Bg, r.Lore));
            }

            var creatures = new List<CreatureCard>(doc.Creatures.Count + doc.Divine.Count);
            foreach (var r in doc.Creatures) creatures.Add(BuildCreature(r, true));
            foreach (var r in doc.Divine) creatures.Add(BuildCreature(r, false));

            var spells = new List<SpellCard>(doc.Spells.Count);
            foreach (var r in doc.Spells)
            {
                string ctx = "spell '" + r.Nm + "'";
                spells.Add(new SpellCard(
                    new CardId(r.Nm), r.Nm, r.C, r.Trap,
                    ParseSpellEffect(r.EffectRaw, ctx),
                    r.Val,
                    ParseSpellTarget(r.TargetRaw, ctx),
                    ParseTrapTrigger(r.TriggerRaw, ctx),
                    r.Ic, r.Slug));
            }

            var structures = new List<StructureDef>(doc.Structures.Count + doc.Forges.Count);
            foreach (var r in doc.Structures) structures.Add(BuildStructure(r));
            foreach (var r in doc.Forges) structures.Add(BuildStructure(r));

            var commanders = new List<CommanderDef>(doc.Commanders.Count);
            foreach (var r in doc.Commanders)
            {
                var colors = new Element[r.Colors.Length];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = ParseElementReq(r.Colors[i], "commander '" + r.Id + "'");
                commanders.Add(new CommanderDef(new CommanderId(r.Id), r.Name, r.Hp, r.Wk,
                                                colors, r.Dual, r.BuildList, r.Desc));
            }

            CreatureCard worker = null;
            if (doc.Worker != null)
                worker = new CreatureCard(new CardId(doc.Worker.Nm), doc.Worker.Nm, Element.None, -1,
                                          doc.Worker.C, doc.Worker.A, doc.Worker.H, doc.Worker.Up,
                                          false, false, Keyword.None,
                                          null, null, null, null, null,
                                          null, Tribe.None, Subtype.None, false, doc.Worker.Slug);

            return new CardCatalog(creatures, spells, commanders, structures, elements, worker,
                                   doc.DeckSize, doc.MaxCopies);
        }

        private static CreatureCard BuildCreature(CardsJsonDoc.CreatureRow r, bool deckable)
        {
            string ctx = "creature '" + r.Nm + "'";
            HatchForm into = r.IntoNm == null ? null : new HatchForm(r.IntoNm, r.IntoA, r.IntoH);
            return new CreatureCard(
                new CardId(r.Nm), r.Nm,
                ParseElementReq(r.ElementRaw, ctx),
                r.PoolIndex, r.C, r.A, r.H, r.Up,
                r.Fs, r.Entrench,
                ParseKeyword(r.KwRaw, ctx),
                r.Det, r.Reap, r.WardHp, r.Grow, r.Hatch,
                into,
                ParseTribe(r.TribeRaw, ctx),
                ParseSubtype(r.SubtypeRaw, ctx),
                deckable, r.Slug);
        }

        private static StructureDef BuildStructure(CardsJsonDoc.StructureRow r)
        {
            string ctx = "structure '" + r.Key + "'";
            return new StructureDef(
                r.Key, new StructId(r.Bid), r.Nm, r.C, r.H,
                ParseStructEffect(r.EffRaw, ctx),
                r.Val, r.Sup,
                r.ColorRaw == null ? Element.None : ParseElementReq(r.ColorRaw, ctx),
                r.Prereq,
                r.From == null ? StructId.None : new StructId(r.From),
                r.Up2,
                ParseRowGate(r.RowRaw, ctx),
                r.Buildable, r.Ic, r.Slug, r.Desc);
        }

        // ---- fail-loud enum parsers -----------------------------------------------------------

        public static Element ParseElementReq(string raw, string ctx)
        {
            if (raw == null)
                throw new CardsJsonException(ctx + ": missing element");
            var e = ElementNames.Parse(raw);
            if (e == Element.None)
                throw new CardsJsonException(ctx + ": unknown element '" + raw + "'");
            return e;
        }

        public static Keyword ParseKeyword(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return Keyword.None;
                case "detonate": return Keyword.Detonate;
                case "undertow": return Keyword.Undertow;
                case "entrench": return Keyword.Entrench;
                case "ward": return Keyword.Ward;
                case "reap": return Keyword.Reap;
                case "chrysalis": return Keyword.Chrysalis;
                case "scour": return Keyword.Scour;
                case "overcharge": return Keyword.Overcharge;
                default:
                    throw new CardsJsonException(ctx + ": unknown keyword '" + raw +
                        "' - add it to the Keyword enum or fix the JS registry; the importer will not guess");
            }
        }

        public static Tribe ParseTribe(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return Tribe.None;
                case "Dragon": return Tribe.Dragon;
                case "Human": return Tribe.Human;
                default:
                    throw new CardsJsonException(ctx + ": unknown tribe '" + raw + "'");
            }
        }

        public static Subtype ParseSubtype(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return Subtype.None;
                case "Warrior": return Subtype.Warrior;
                case "Wizard": return Subtype.Wizard;
                default:
                    throw new CardsJsonException(ctx + ": unknown subtype '" + raw + "'");
            }
        }

        public static SpellEffect ParseSpellEffect(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return SpellEffect.None;
                case "burn": return SpellEffect.Burn;
                case "raze": return SpellEffect.Raze;
                case "pitfall": return SpellEffect.Pitfall;
                case "chain": return SpellEffect.Chain;
                case "bounce": return SpellEffect.Bounce;
                case "thornmail": return SpellEffect.Thornmail;
                default:
                    throw new CardsJsonException(ctx + ": unknown spell effect '" + raw + "'");
            }
        }

        public static SpellTarget ParseSpellTarget(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return SpellTarget.None;
                case "enemy": return SpellTarget.Enemy;
                case "building": return SpellTarget.Building;
                default:
                    throw new CardsJsonException(ctx + ": unknown spell target '" + raw + "'");
            }
        }

        public static TrapTrigger ParseTrapTrigger(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return TrapTrigger.None;
                case "summon": return TrapTrigger.Summon;
                case "attack": return TrapTrigger.Attack;
                default:
                    throw new CardsJsonException(ctx + ": unknown trap trigger '" + raw + "'");
            }
        }

        public static StructEffect ParseStructEffect(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return StructEffect.None;
                case "none": return StructEffect.None;    // encampment/outpost carry a literal "none"
                case "mana": return StructEffect.Mana;
                case "villager": return StructEffect.Villager;
                case "vault": return StructEffect.Vault;
                case "wall": return StructEffect.Wall;
                case "damage": return StructEffect.Damage;
                case "revive": return StructEffect.Revive;
                default:
                    throw new CardsJsonException(ctx + ": unknown structure effect '" + raw + "'");
            }
        }

        public static RowGate ParseRowGate(string raw, string ctx)
        {
            switch (raw)
            {
                case null: return RowGate.Any;
                case "front": return RowGate.FrontOnly;
                case "back": return RowGate.BackOnly;
                case "center": return RowGate.CenterOnly;
                default:
                    throw new CardsJsonException(ctx + ": unknown row gate '" + raw + "'");
            }
        }

        /// <summary>
        /// The deterministic export-key to file-name fold shared by the asset importer and the V3
        /// collision check: "fire|Magmaw" becomes "fire_magmaw", "forge:fire" becomes "forge_fire".
        /// Lossy on purpose, therefore collision-checked rather than trusted.
        /// </summary>
        public static string SafeFileName(string key)
        {
            if (key == null) return "";
            var chars = key.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '|' || c == ':' || c == ' ' || c == '/' || c == '\\') chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
