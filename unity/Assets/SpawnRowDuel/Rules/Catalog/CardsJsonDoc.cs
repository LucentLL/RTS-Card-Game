using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// cards.json, parsed into typed rows with the enum fields still RAW STRINGS. Enum resolution
    /// happens in CardCatalogBuilder so it can fail loudly with the offending card named; the
    /// importer reads these same rows for presentation fields the catalog does not carry.
    ///
    /// The `art` field (~250 KB of dead placeholder SVG) is never read (design 03 s5.2).
    /// </summary>
    public sealed class CardsJsonDoc
    {
        public string GeneratedAt;

        // rules block
        public int DeckSize, MaxCopies, Slots, BaseCol;
        public int[] CenterLanes;

        // counts block - what the export believes it wrote; V1 checks reality against it
        public int CountElements, CountCommanders, CountCreatures, CountDivine;
        public int CountSpells, CountTraps, CountStructures, CountForges, CountDeckRegistry;
        public Dictionary<string, int> CountCreaturesByElement;

        public List<ElementRow> Elements = new List<ElementRow>();
        public List<CommanderRow> Commanders = new List<CommanderRow>();
        public List<CreatureRow> Creatures = new List<CreatureRow>();
        public List<CreatureRow> Divine = new List<CreatureRow>();
        public List<SpellRow> Spells = new List<SpellRow>();
        public List<StructureRow> Structures = new List<StructureRow>();
        public List<StructureRow> Forges = new List<StructureRow>();
        public CreatureRow Worker;
        public List<DeckRegRow> DeckRegistry = new List<DeckRegRow>();

        public sealed class ElementRow
        {
            public string Id, Name, Glyph, ColorHex, AccentHex, DeepHex, Lore;
            public string[] Bg;
            public int Hp, Wk;
            public bool Deckable;
        }

        public sealed class CommanderRow
        {
            public string Id, Name, Desc;
            public int Hp, Wk;
            public string[] Colors, BuildList;
            public bool Dual;
        }

        public sealed class CreatureRow
        {
            public string Key, Nm, Slug, ElementRaw, KwRaw, TribeRaw, SubtypeRaw;
            public int PoolIndex;
            public int C, A, H, Up;
            public bool Fs, Entrench, Token;
            public int? Det, Reap, WardHp, Grow, Hatch;
            public string IntoNm;             // null when the creature never hatches
            public int IntoA, IntoH;
        }

        public sealed class SpellRow
        {
            public string Key, Nm, Slug, EffectRaw, TargetRaw, TriggerRaw, Ic;
            public int C;
            public bool Trap;
            public int? Val;
        }

        public sealed class StructureRow
        {
            public string Key, Bid, Nm, Slug, EffRaw, RowRaw, ColorRaw, From, Ic, Desc;
            public int C, H, Val, Sup;
            public string[] Prereq, Up2;
            public bool Buildable;
        }

        public sealed class DeckRegRow
        {
            public string Key, Type, Color, Nm;
        }

        public static CardsJsonDoc Parse(string json)
        {
            var root = JsonValue.Parse(json);
            var doc = new CardsJsonDoc();

            doc.GeneratedAt = root.StrOrNull("generatedAt");

            var rules = root.ObjReq("rules", "cards.json");
            doc.DeckSize = rules.IntReq("DECK_SIZE", "rules");
            doc.MaxCopies = rules.IntReq("MAX_COPIES", "rules");
            doc.Slots = rules.IntReq("SLOTS", "rules");
            doc.BaseCol = rules.IntReq("BASE_COL", "rules");
            var lanes = rules.ArrReq("CENTER_LANES", "rules");
            doc.CenterLanes = new int[lanes.Count];
            for (int i = 0; i < lanes.Count; i++) doc.CenterLanes[i] = lanes[i].AsInt;

            var counts = root.ObjReq("counts", "cards.json");
            doc.CountElements = counts.IntReq("elements", "counts");
            doc.CountCommanders = counts.IntReq("commanders", "counts");
            doc.CountCreatures = counts.IntReq("creatures", "counts");
            doc.CountDivine = counts.IntReq("divineCreatures", "counts");
            doc.CountSpells = counts.IntReq("spellsTotal", "counts");
            doc.CountTraps = counts.IntReq("traps", "counts");
            doc.CountStructures = counts.IntReq("structuresStaticDefs", "counts");
            doc.CountForges = counts.IntReq("structuresGeneratedForges", "counts");
            doc.CountDeckRegistry = counts.IntReq("deckRegistryEntries", "counts");
            doc.CountCreaturesByElement = new Dictionary<string, int>();
            var byEl = counts.ObjReq("creaturesByElement", "counts");
            foreach (var k in byEl.Keys) doc.CountCreaturesByElement[k] = byEl.IntReq(k, "creaturesByElement");

            var els = root.ArrReq("elements", "cards.json");
            for (int i = 0; i < els.Count; i++) doc.Elements.Add(ParseElement(els[i]));

            var ccs = root.ArrReq("commanders", "cards.json");
            for (int i = 0; i < ccs.Count; i++) doc.Commanders.Add(ParseCommander(ccs[i]));

            var cre = root.ArrReq("creatures", "cards.json");
            for (int i = 0; i < cre.Count; i++) doc.Creatures.Add(ParseCreature(cre[i]));

            var div = root.ArrReq("divine", "cards.json");
            for (int i = 0; i < div.Count; i++) doc.Divine.Add(ParseCreature(div[i]));

            var sp = root.ArrReq("spells", "cards.json");
            for (int i = 0; i < sp.Count; i++) doc.Spells.Add(ParseSpell(sp[i]));

            var st = root.ArrReq("structures", "cards.json");
            for (int i = 0; i < st.Count; i++) doc.Structures.Add(ParseStructure(st[i]));

            var fg = root.ArrReq("forges", "cards.json");
            for (int i = 0; i < fg.Count; i++) doc.Forges.Add(ParseStructure(fg[i]));

            doc.Worker = ParseCreature(root.ObjReq("worker", "cards.json"));

            var reg = root.ArrReq("deckRegistry", "cards.json");
            for (int i = 0; i < reg.Count; i++)
            {
                var r = reg[i];
                var row = new DeckRegRow();
                row.Key = r.StrReq("key", "deckRegistry");
                row.Type = r.StrReq("type", "deckRegistry");
                row.Color = r.StrOrNull("color");
                row.Nm = r.StrReq("nm", "deckRegistry");
                doc.DeckRegistry.Add(row);
            }

            return doc;
        }

        private static ElementRow ParseElement(JsonValue v)
        {
            var r = new ElementRow();
            r.Id = v.StrReq("id", "element");
            string ctx = "element '" + r.Id + "'";
            r.Name = v.StrReq("name", ctx);
            r.Glyph = v.StrReq("glyph", ctx);
            r.ColorHex = v.StrOrNull("color");
            r.AccentHex = v.StrOrNull("accent");
            r.DeepHex = v.StrOrNull("deep");
            r.Bg = v.StringArray("bg", ctx);
            r.Hp = v.IntReq("hp", ctx);
            r.Wk = v.IntReq("wk", ctx);
            r.Lore = v.StrOrNull("lore");
            r.Deckable = v.BoolOr("deckable", false);
            return r;
        }

        private static CommanderRow ParseCommander(JsonValue v)
        {
            var r = new CommanderRow();
            r.Id = v.StrReq("id", "commander");
            string ctx = "commander '" + r.Id + "'";
            r.Name = v.StrReq("name", ctx);
            r.Hp = v.IntReq("hp", ctx);
            r.Wk = v.IntReq("wk", ctx);
            r.Colors = v.StringArray("colors", ctx);
            r.Desc = v.StrOrNull("desc");
            r.Dual = v.BoolOr("dual", false);
            r.BuildList = v.StringArray("buildList", ctx);
            return r;
        }

        private static CreatureRow ParseCreature(JsonValue v)
        {
            var r = new CreatureRow();
            r.Key = v.StrOrNull("key");
            r.Nm = v.StrReq("nm", "creature");
            string ctx = "creature '" + r.Nm + "'";
            r.Slug = v.StrOrNull("slug");
            r.ElementRaw = v.StrOrNull("element");
            r.PoolIndex = v.IntOr("poolIndex", -1);
            r.C = v.IntOr("c", 0);
            r.A = v.IntOr("a", 0);
            r.H = v.IntOr("h", 0);
            r.Up = v.IntOr("up", 0);
            r.Fs = v.BoolOr("fs", false);
            r.KwRaw = v.StrOrNull("kw");
            r.Det = v.IntOrNull("det");
            r.Reap = v.IntOrNull("reap");
            r.WardHp = v.IntOrNull("wardhp");
            r.Grow = v.IntOrNull("grow");
            r.Hatch = v.IntOrNull("hatch");
            var into = v.Get("into");
            if (!into.IsNull)
            {
                r.IntoNm = into.StrReq("nm", ctx + ".into");
                r.IntoA = into.IntReq("a", ctx + ".into");
                r.IntoH = into.IntReq("h", ctx + ".into");
            }
            r.Entrench = v.BoolOr("entrench", false);
            r.TribeRaw = v.StrOrNull("tribe");
            r.SubtypeRaw = v.StrOrNull("subtype");
            r.Token = v.BoolOr("token", false);
            return r;
        }

        private static SpellRow ParseSpell(JsonValue v)
        {
            var r = new SpellRow();
            r.Key = v.StrOrNull("key");
            r.Nm = v.StrReq("nm", "spell");
            r.Slug = v.StrOrNull("slug");
            r.C = v.IntReq("c", "spell '" + r.Nm + "'");
            r.Trap = v.BoolOr("trap", false);
            r.EffectRaw = v.StrOrNull("effect");
            r.Val = v.IntOrNull("val");
            r.TargetRaw = v.StrOrNull("target");
            r.TriggerRaw = v.StrOrNull("trigger");
            r.Ic = v.StrOrNull("ic");
            return r;
        }

        private static StructureRow ParseStructure(JsonValue v)
        {
            var r = new StructureRow();
            r.Key = v.StrReq("key", "structure");
            string ctx = "structure '" + r.Key + "'";
            r.Bid = v.StrReq("bid", ctx);
            r.Nm = v.StrReq("nm", ctx);
            r.Slug = v.StrOrNull("slug");
            r.C = v.IntReq("c", ctx);
            r.H = v.IntReq("h", ctx);
            r.EffRaw = v.StrOrNull("eff");
            r.Val = v.IntOr("val", 0);
            r.Sup = v.IntOr("sup", 0);
            r.Ic = v.StrOrNull("ic");
            r.Prereq = v.StringArray("prereq", ctx);
            r.From = v.StrOrNull("from");
            r.Up2 = v.StringArray("up2", ctx);
            r.RowRaw = v.StrOrNull("row");
            r.ColorRaw = v.StrOrNull("color");
            r.Desc = v.StrOrNull("desc");
            r.Buildable = v.BoolOr("buildable", false);
            return r;
        }
    }
}
