using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The real registry, loaded through the pure JSON loader, held against the invariants the
    /// specs declare (design 03 s5.6). These mirror the importer's own validation battery so the
    /// invariants hold in CI even when nobody re-imports.
    /// </summary>
    public class CatalogTests
    {
        [Test]
        public void RealRegistry_LoadsAndValidates()
        {
            Assert.IsNotNull(TestData.Catalog);
            Assert.IsTrue(TestData.Report.Ok, TestData.Report.Describe());
        }

        [Test]
        public void Counts_MatchTheExport()
        {
            var cat = TestData.Catalog;
            Assert.AreEqual(68, cat.Creatures.Count, "64 deckable + 4 divine");
            Assert.AreEqual(14, cat.Spells.Count);
            Assert.AreEqual(36, cat.Commanders.Count);
            Assert.AreEqual(31, cat.Structures.Count, "13 static + 18 generated forges");
            Assert.AreEqual(9, cat.Elements.Count);
            Assert.AreEqual(40, cat.DeckSize);
            Assert.AreEqual(3, cat.MaxCopies);
        }

        [Test]
        public void Commanders_EightSoloThenTwentyEightDual_InRegistryOrder()
        {
            var cat = TestData.Catalog;
            int solo = 0, dual = 0;
            for (int i = 0; i < cat.Commanders.Count; i++)
            {
                if (cat.Commanders[i].Dual) dual++;
                else
                {
                    solo++;
                    Assert.AreEqual(0, dual, "solo commanders must precede duals - order feeds the random pick");
                }
            }
            Assert.AreEqual(8, solo);
            Assert.AreEqual(28, dual);
        }

        // V6 - the pool-shape design invariant (spec 06 s2.1).
        private static readonly int[] PoolCosts = { 1, 1, 2, 2, 3, 4, 5, 6 };
        private static readonly int[] PoolUpkeep = { 1, 1, 1, 1, 2, 2, 3, 3 };

        [Test]
        public void EveryElementPool_Has8Creatures_WithTheCanonicalCostCurve()
        {
            var cat = TestData.Catalog;
            foreach (var el in ElementNames.Majors)
            {
                var pool = cat.PoolOf(el);
                Assert.AreEqual(8, pool.Count, el.ToString());
                for (int i = 0; i < 8; i++)
                {
                    Assert.AreEqual(PoolCosts[i], pool[i].Cost, el + " slot " + i);
                    Assert.AreEqual(PoolUpkeep[i], pool[i].Upkeep, el + " slot " + i);
                    Assert.AreEqual(PoolCosts[i] == 3, pool[i].FirstStrike,
                        el + " slot " + i + " - the cost-3 card is the pool's First Strike card");
                    Assert.AreEqual(i, pool[i].PoolIndex, el + " slot " + i);
                }
            }
        }

        // V4 - the x500 rescale audit.
        [Test]
        public void AllCombatValues_AreOnTheX500Scale()
        {
            foreach (var c in TestData.Catalog.Creatures)
            {
                Assert.AreEqual(0, c.Attack % 500, c.Name + ".Attack");
                Assert.AreEqual(0, c.Health % 500, c.Name + ".Health");
                Assert.Greater(c.Cost, 0, c.Name + ": no deckable card may cost 0");
            }
        }

        // V7 - half-up rounding; banker's rounding silently costs 16 of 36 commanders a worker.
        [Test]
        public void DualCommanders_WorkersUseHalfUpRounding()
        {
            var cat = TestData.Catalog;
            int affected = 0;
            foreach (var cc in cat.Commanders)
            {
                if (cc.Colors.Length != 2) continue;
                int wkA = cat.ElementOf(cc.Colors[0]).Workers;
                int wkB = cat.ElementOf(cc.Colors[1]).Workers;
                int halfUp = (wkA + wkB + 1) / 2;
                Assert.AreEqual(halfUp, cc.Workers, cc.Id.ToString());
                if ((wkA + wkB) % 2 != 0) affected++;
            }
            Assert.AreEqual(16, affected,
                "16 of the 28 duals sit on a .5 boundary - if this changes, the data moved");
        }

        [Test]
        public void UpgradeGraph_ReportsTheKnownTowerAsymmetry_AsAWarning()
        {
            // V8 is a warning, not an error: outpost lists tower in up2, tower carries no from.
            // The port must DECIDE this (spec 05 open question 3), not have the importer refuse.
            bool found = false;
            foreach (var w in TestData.Report.Warnings)
                if (w.Contains("tower")) found = true;
            Assert.IsTrue(found, "the tower up2/from asymmetry should be surfaced as a warning");
        }

        [Test]
        public void ForgeFamilies_ResolvePerElement()
        {
            var cat = TestData.Catalog;
            var forge = cat.Structure(new StructId("forge"), Element.Fire);
            Assert.IsNotNull(forge);
            Assert.AreEqual("Emberforge", forge.Name);
            Assert.AreEqual(Element.Fire, forge.Element);
            Assert.AreEqual("forge", forge.Bid.Value, "board objects carry the FAMILY id");

            var grand = cat.Structure(new StructId("grandforge"), Element.Dark);
            Assert.IsNotNull(grand);
            Assert.AreEqual(Element.Dark, grand.Element);

            var foundry = cat.Structure(new StructId("foundry"), Element.None);
            Assert.IsNotNull(foundry);
            Assert.AreEqual(2, foundry.Cost);
            Assert.AreEqual(3000, foundry.MaxHp);

            Assert.IsNull(cat.Structure(new StructId("nonsense"), Element.None));
        }

        [Test]
        public void BuildLists_ResolveInMenuOrder()
        {
            var cat = TestData.Catalog;
            var list = cat.BuildList(new CommanderId("fire"));
            Assert.AreEqual(10, list.Count);
            Assert.AreEqual("foundry", list[0].Bid.Value, "the Foundry heads every build menu");
            Assert.AreEqual("Emberforge", list[1].Name, "a fire commander's forge is the Emberforge");

            var dual = cat.BuildList(new CommanderId("fire_water"));
            Assert.AreEqual(12, dual.Count, "duals get both forges and both grand forges");
        }

        [Test]
        public void Lineage_WalksFromLinks_WithTheHopGuard()
        {
            var cat = TestData.Catalog;

            var citadel = cat.Lineage(new StructId("citadel"));
            Assert.AreEqual(3, citadel.Count);
            Assert.AreEqual("citadel", citadel[0].Value);
            Assert.AreEqual("keep", citadel[1].Value);
            Assert.AreEqual("foundry", citadel[2].Value);

            var grand = cat.Lineage(new StructId("grandforge"));
            Assert.AreEqual(2, grand.Count);
            Assert.AreEqual("forge", grand[1].Value);

            // tower is the known data defect: no from link, so its lineage is just itself.
            var tower = cat.Lineage(new StructId("tower"));
            Assert.AreEqual(1, tower.Count);

            // a bid the catalog has never heard of still terminates
            var unknown = cat.Lineage(new StructId("mystery"));
            Assert.AreEqual(1, unknown.Count);
        }

        [Test]
        public void DeckRegistry_ResolvesDeckKeys_AndExcludesDivine()
        {
            var cat = TestData.Catalog;

            CardId id;
            Assert.IsTrue(cat.TryByDeckKey(DeckKey.Parse("fire|Sparkimp"), out id));
            Assert.AreEqual("Sparkimp", id.Value);

            Assert.IsTrue(cat.TryByDeckKey(DeckKey.Parse("neutral|Ember Bolt"), out id));
            Assert.AreEqual("Ember Bolt", id.Value);

            Assert.IsFalse(cat.TryByDeckKey(DeckKey.Parse("divine|Cherub"), out id),
                "divine creatures are not deckable and must not be in the registry");
        }

        [Test]
        public void WorkerTemplate_IsTheMkVilCard()
        {
            var w = TestData.Catalog.WorkerTemplate;
            Assert.AreEqual("Worker", w.Name);
            Assert.AreEqual(0, w.Attack);
            Assert.AreEqual(1000, w.Health);
            Assert.AreEqual(0, w.Cost);
            Assert.AreEqual(0, w.Upkeep);
        }

        [Test]
        public void Creatures_LookUpByName_IncludingDivine()
        {
            var cat = TestData.Catalog;

            var magmaw = cat.Creature(new CardId("Magmaw"));
            Assert.AreEqual(Element.Fire, magmaw.Element);
            Assert.IsTrue(magmaw.Deckable);

            var cherub = cat.Creature(new CardId("Cherub"));
            Assert.AreEqual(Element.Divine, cherub.Element);
            Assert.IsFalse(cherub.Deckable);

            CreatureCard missing;
            Assert.IsFalse(cat.TryCreature(new CardId("Not A Card"), out missing));
        }

        [Test]
        public void ChrysalisCreatures_CarryTheirHatchForm()
        {
            var sapPod = TestData.Catalog.Creature(new CardId("Sap Pod"));
            Assert.AreEqual(Keyword.Chrysalis, sapPod.Keyword);
            Assert.IsNotNull(sapPod.Into);
            Assert.AreEqual("Canopy Beast", sapPod.Into.Name);
            Assert.AreEqual(2500, sapPod.Into.Attack);
            Assert.AreEqual(2000, sapPod.Into.Health);
            Assert.IsTrue(sapPod.Grow.HasValue);
            Assert.IsTrue(sapPod.Hatch.HasValue);
        }

        [Test]
        public void NullVsZero_SurvivesTheLoad()
        {
            // Sparkimp has NO keyword numbers - they must be null, not 0. The distinction is
            // load-bearing for wardhp, where the instance default is 2, not 0 (spec 06 s6.3).
            var sparkimp = TestData.Catalog.Creature(new CardId("Sparkimp"));
            Assert.IsFalse(sparkimp.Detonate.HasValue);
            Assert.IsFalse(sparkimp.Reap.HasValue);
            Assert.IsFalse(sparkimp.WardHp.HasValue);
            Assert.IsFalse(sparkimp.Grow.HasValue);
            Assert.IsFalse(sparkimp.Hatch.HasValue);
        }

        [Test]
        public void Spells_TrapsAndTargets_ParseCorrectly()
        {
            var cat = TestData.Catalog;

            var bolt = cat.Spell(new CardId("Ember Bolt"));
            Assert.IsFalse(bolt.IsTrap);
            Assert.AreEqual(SpellEffect.Burn, bolt.Effect);
            Assert.AreEqual(1500, bolt.Value.Value);
            Assert.AreEqual(SpellTarget.Enemy, bolt.Target);

            var snare = cat.Spell(new CardId("Snare Pit"));
            Assert.IsTrue(snare.IsTrap);
            Assert.AreEqual(SpellEffect.Pitfall, snare.Effect);
            Assert.AreEqual(TrapTrigger.Summon, snare.Trigger);
            Assert.IsFalse(snare.Value.HasValue);

            int traps = 0;
            foreach (var s in cat.Spells) if (s.IsTrap) traps++;
            Assert.AreEqual(5, traps);
        }
    }
}
