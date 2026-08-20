using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool Ok { get { return Errors.Count == 0; } }

        public string Describe()
        {
            var parts = new List<string>();
            foreach (var e in Errors) parts.Add("ERROR " + e);
            foreach (var w in Warnings) parts.Add("warn  " + w);
            return string.Join("\n", parts.ToArray());
        }
    }

    /// <summary>
    /// The import-time validations from design 03 s5.6, V1-V11. Each check maps to a port risk
    /// the extraction flagged; they run in the pure loader so the same battery guards the asset
    /// importer, the EditMode suite and any future headless CI without duplication.
    ///
    /// V12 (art coverage report) needs a filesystem and lives with the Unity importer.
    /// </summary>
    public static class CatalogValidator
    {
        public static ValidationReport Validate(CardsJsonDoc doc, CardCatalog catalog)
        {
            var rep = new ValidationReport();

            CheckCounts(doc, rep);                     // V1
            CheckSlugUniqueness(doc, rep);             // V2
            CheckAssetPathUniqueness(doc, rep);        // V3
            CheckStatScale(doc, rep);                  // V4
            CheckNoZeroCostDeckables(doc, rep);        // V5
            CheckPoolShape(catalog, rep);              // V6
            CheckDualCommanderRounding(catalog, rep);  // V7
            CheckUpgradeGraphSymmetry(doc, rep);       // V8 - warn only
            CheckTechTreeResolves(doc, catalog, rep);  // V9
            CheckBuildListsResolve(catalog, rep);      // V10
            // V11 (unknown enum strings) is enforced structurally: CardCatalogBuilder throws
            // before this validator can even run.

            return rep;
        }

        // V1 - the export's own counts block against what actually parsed.
        private static void CheckCounts(CardsJsonDoc doc, ValidationReport rep)
        {
            Count(rep, "elements", doc.Elements.Count, doc.CountElements);
            Count(rep, "commanders", doc.Commanders.Count, doc.CountCommanders);
            Count(rep, "creatures", doc.Creatures.Count, doc.CountCreatures);
            Count(rep, "divine", doc.Divine.Count, doc.CountDivine);
            Count(rep, "spells", doc.Spells.Count, doc.CountSpells);
            Count(rep, "structures", doc.Structures.Count, doc.CountStructures);
            Count(rep, "forges", doc.Forges.Count, doc.CountForges);
            Count(rep, "deckRegistry", doc.DeckRegistry.Count, doc.CountDeckRegistry);

            int traps = 0;
            foreach (var s in doc.Spells) if (s.Trap) traps++;
            Count(rep, "traps", traps, doc.CountTraps);

            var byEl = new Dictionary<string, int>();
            foreach (var c in doc.Creatures)
            {
                int n;
                byEl.TryGetValue(c.ElementRaw, out n);
                byEl[c.ElementRaw] = n + 1;
            }
            foreach (var kv in doc.CountCreaturesByElement)
            {
                int actual;
                byEl.TryGetValue(kv.Key, out actual);
                if (actual != kv.Value)
                    rep.Errors.Add("V1: creaturesByElement." + kv.Key + " says " + kv.Value +
                                   " but " + actual + " parsed - truncated or partial export");
            }
        }

        private static void Count(ValidationReport rep, string what, int actual, int declared)
        {
            if (actual != declared)
                rep.Errors.Add("V1: counts." + what + " says " + declared + " but " + actual +
                               " rows parsed - truncated or partial export");
        }

        // V2 - a slug collision silently gives two cards the same art (spec 06 s9.1).
        private static void CheckSlugUniqueness(CardsJsonDoc doc, ValidationReport rep)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in doc.Creatures) Slug(rep, seen, c.Slug, "creature " + c.Nm);
            foreach (var c in doc.Divine) Slug(rep, seen, c.Slug, "divine " + c.Nm);
            foreach (var s in doc.Spells) Slug(rep, seen, s.Slug, "spell " + s.Nm);
            foreach (var b in doc.Structures) Slug(rep, seen, b.Slug, "structure " + b.Key);
            foreach (var b in doc.Forges) Slug(rep, seen, b.Slug, "forge " + b.Key);
            if (doc.Worker != null) Slug(rep, seen, doc.Worker.Slug, "worker token");
        }

        private static void Slug(ValidationReport rep, Dictionary<string, string> seen,
                                 string slug, string who)
        {
            if (string.IsNullOrEmpty(slug))
            {
                rep.Errors.Add("V2: " + who + " has no slug");
                return;
            }
            string other;
            if (seen.TryGetValue(slug, out other))
                rep.Errors.Add("V2: slug '" + slug + "' is shared by " + other + " and " + who +
                               " - both would resolve the same art file");
            else
                seen[slug] = who;
        }

        // V3 - two export keys folding onto one generated .asset path.
        private static void CheckAssetPathUniqueness(CardsJsonDoc doc, ValidationReport rep)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in doc.Creatures) Path(rep, seen, c.Key, "creature " + c.Nm);
            foreach (var c in doc.Divine) Path(rep, seen, c.Key, "divine " + c.Nm);
            foreach (var s in doc.Spells) Path(rep, seen, s.Key, "spell " + s.Nm);
            foreach (var b in doc.Structures) Path(rep, seen, b.Key, "structure " + b.Key);
            foreach (var b in doc.Forges) Path(rep, seen, b.Key, "forge " + b.Key);
            foreach (var cc in doc.Commanders) Path(rep, seen, "cc|" + cc.Id, "commander " + cc.Id);
            foreach (var el in doc.Elements) Path(rep, seen, "el|" + el.Id, "element " + el.Id);
        }

        private static void Path(ValidationReport rep, Dictionary<string, string> seen,
                                 string key, string who)
        {
            var safe = CardCatalogBuilder.SafeFileName(key);
            string other;
            if (seen.TryGetValue(safe, out other))
                rep.Errors.Add("V3: asset file name '" + safe + "' is produced by both " + other +
                               " and " + who);
            else
                seen[safe] = who;
        }

        // V4 - the x500 stat-scale audit the extraction explicitly asked for (spec 06 s11.2).
        private static void CheckStatScale(CardsJsonDoc doc, ValidationReport rep)
        {
            foreach (var c in doc.Creatures) ScaleCreature(rep, c);
            foreach (var c in doc.Divine) ScaleCreature(rep, c);
            foreach (var s in doc.Spells)
                if (s.Val.HasValue) Scale(rep, "spell " + s.Nm + ".val", s.Val.Value);
        }

        private static void ScaleCreature(ValidationReport rep, CardsJsonDoc.CreatureRow c)
        {
            Scale(rep, c.Nm + ".a", c.A);
            Scale(rep, c.Nm + ".h", c.H);
            if (c.Det.HasValue) Scale(rep, c.Nm + ".det", c.Det.Value);
            if (c.Reap.HasValue) Scale(rep, c.Nm + ".reap", c.Reap.Value);
            if (c.WardHp.HasValue) Scale(rep, c.Nm + ".wardhp", c.WardHp.Value);
            if (c.IntoNm != null)
            {
                Scale(rep, c.Nm + ".into.a", c.IntoA);
                Scale(rep, c.Nm + ".into.h", c.IntoH);
            }
        }

        private static void Scale(ValidationReport rep, string what, int v)
        {
            if (v % 500 != 0)
                rep.Errors.Add("V4: " + what + " = " + v +
                               " is not on the x500 stat scale - incomplete rescale");
        }

        // V5 - "no deckable card may cost 0" (spec 04 data invariant).
        private static void CheckNoZeroCostDeckables(CardsJsonDoc doc, ValidationReport rep)
        {
            foreach (var c in doc.Creatures)
                if (c.C <= 0) rep.Errors.Add("V5: deckable creature " + c.Nm + " costs " + c.C);
            foreach (var s in doc.Spells)
                if (s.C <= 0) rep.Errors.Add("V5: spell " + s.Nm + " costs " + s.C);
        }

        // V6 - each element pool is 8 creatures at costs 1,1,2,2,3,4,5,6 / upkeep 1,1,1,1,2,2,3,3,
        // and the cost-3 card is the pool's First Strike card (spec 06 s2.1).
        private static readonly int[] PoolCosts = { 1, 1, 2, 2, 3, 4, 5, 6 };
        private static readonly int[] PoolUpkeep = { 1, 1, 1, 1, 2, 2, 3, 3 };

        private static void CheckPoolShape(CardCatalog catalog, ValidationReport rep)
        {
            foreach (var el in ElementNames.Majors)
            {
                var pool = catalog.PoolOf(el);
                if (pool.Count != 8)
                {
                    rep.Errors.Add("V6: " + el + " pool has " + pool.Count + " creatures, not 8");
                    continue;
                }
                for (int i = 0; i < 8; i++)
                {
                    var c = pool[i];
                    if (c.Cost != PoolCosts[i])
                        rep.Errors.Add("V6: " + el + " pool slot " + i + " (" + c.Name + ") costs " +
                                       c.Cost + ", expected " + PoolCosts[i]);
                    if (c.Upkeep != PoolUpkeep[i])
                        rep.Errors.Add("V6: " + el + " pool slot " + i + " (" + c.Name + ") upkeep " +
                                       c.Upkeep + ", expected " + PoolUpkeep[i]);
                    bool expectFs = PoolCosts[i] == 3;
                    if (c.FirstStrike != expectFs)
                        rep.Errors.Add("V6: " + el + " pool slot " + i + " (" + c.Name + ") fs=" +
                                       c.FirstStrike + ", expected " + expectFs);
                }
            }
        }

        // V7 - dual-commander workers must use half-UP rounding. C# banker's rounding would
        // silently cost 16 of the 36 commanders a worker (spec 06 s2.4).
        private static void CheckDualCommanderRounding(CardCatalog catalog, ValidationReport rep)
        {
            foreach (var cc in catalog.Commanders)
            {
                if (cc.Colors.Length != 2) continue;
                int wkA = catalog.ElementOf(cc.Colors[0]).Workers;
                int wkB = catalog.ElementOf(cc.Colors[1]).Workers;
                int hpA = catalog.ElementOf(cc.Colors[0]).Hp;
                int hpB = catalog.ElementOf(cc.Colors[1]).Hp;
                int expectWk = (wkA + wkB + 1) / 2;      // integer half-up; no Math.Round anywhere
                int expectHp = (hpA + hpB + 1) / 2;
                if (cc.Workers != expectWk)
                    rep.Errors.Add("V7: commander " + cc.Id + " wk=" + cc.Workers +
                                   ", expected half-up " + expectWk);
                if (cc.Hp != expectHp)
                    rep.Errors.Add("V7: commander " + cc.Id + " hp=" + cc.Hp +
                                   ", expected half-up " + expectHp);
            }
        }

        // V8 - up2/from symmetry. WARN only: `tower` is a known real defect in the data
        // (outpost lists tower in up2, tower carries no from) - spec 05 open question 3.
        private static void CheckUpgradeGraphSymmetry(CardsJsonDoc doc, ValidationReport rep)
        {
            var byBid = new Dictionary<string, CardsJsonDoc.StructureRow>(StringComparer.Ordinal);
            foreach (var b in doc.Structures) byBid[b.Bid] = b;
            foreach (var b in doc.Forges) byBid[b.Bid] = b;

            foreach (var b in doc.Structures)
            {
                foreach (var target in b.Up2)
                {
                    CardsJsonDoc.StructureRow t;
                    if (!byBid.TryGetValue(target, out t)) continue;   // V9's problem, not V8's
                    if (t.From != b.Bid)
                        rep.Warnings.Add("V8: " + b.Bid + ".up2 lists " + target + " but " + target +
                                         ".from is " + (t.From ?? "null") +
                                         " - asymmetric upgrade edge (known offender: tower)");
                }
            }
        }

        // V9 - every prereq and up2 entry names a real family.
        private static void CheckTechTreeResolves(CardsJsonDoc doc, CardCatalog catalog,
                                                  ValidationReport rep)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in doc.Structures) known.Add(b.Bid);
            foreach (var b in doc.Forges) known.Add(b.Bid);

            foreach (var b in doc.Structures)
            {
                foreach (var p in b.Prereq)
                    if (!known.Contains(p))
                        rep.Errors.Add("V9: " + b.Bid + " prereq '" + p + "' is not a known structure");
                foreach (var u in b.Up2)
                    if (!known.Contains(u))
                        rep.Errors.Add("V9: " + b.Bid + " up2 '" + u + "' is not a known structure");
                if (b.From != null && !known.Contains(b.From))
                    rep.Errors.Add("V9: " + b.Bid + " from '" + b.From + "' is not a known structure");
            }
        }

        // V10 - every commander buildList entry resolves through the forge-aware lookup.
        private static void CheckBuildListsResolve(CardCatalog catalog, ValidationReport rep)
        {
            foreach (var cc in catalog.Commanders)
            {
                foreach (var raw in cc.BuildListRaw)
                {
                    var entry = CardCatalog.ParseBuildEntry(raw);
                    if (catalog.Structure(entry.Key, entry.Value) == null)
                        rep.Errors.Add("V10: commander " + cc.Id + " buildList entry '" + raw +
                                       "' does not resolve");
                }
            }
        }
    }

    /// <summary>The one-call entry point: parse, build, validate, throw on hard failure.</summary>
    public static class CardsJsonCatalog
    {
        public static CardCatalog Load(string json)
        {
            ValidationReport ignored;
            return Load(json, out ignored);
        }

        public static CardCatalog Load(string json, out ValidationReport report)
        {
            var doc = CardsJsonDoc.Parse(json);
            var catalog = CardCatalogBuilder.Build(doc);
            report = CatalogValidator.Validate(doc, catalog);
            if (!report.Ok)
                throw new CardsJsonException("cards.json failed validation:\n" + report.Describe());
            return catalog;
        }
    }
}
