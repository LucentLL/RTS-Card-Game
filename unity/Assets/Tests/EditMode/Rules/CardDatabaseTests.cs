using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using SpawnRowDuel.Data;
using SpawnRowDuel.EditorPipeline;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The ScriptableObject layer: the generated database exists, a re-import is a no-op, and -
    /// the load-bearing one - the catalog built FROM THE ASSETS is field-for-field identical to
    /// the catalog built from cards.json, registry order included. That parity is what lets the
    /// runtime boot from assets while tests and CI trust the pure loader.
    /// </summary>
    public class CardDatabaseTests
    {
        private CardDatabase _db;

        [OneTimeSetUp]
        public void Load()
        {
            _db = AssetDatabase.LoadAssetAtPath<CardDatabase>(CardImporter.DatabasePath);
        }

        [Test]
        public void Database_ExistsAndIsPopulated()
        {
            Assert.IsNotNull(_db,
                "CardDatabase.asset missing - run Tools > Spawn Row Duel > Import Cards (or bash tools/regen-cards.sh)");
            // 68 creatures + 14 spells + 31 structures + 36 commanders + 9 elements + 1 worker
            Assert.AreEqual(159, _db.All.Count);
            Assert.AreEqual(40, _db.DeckSize);
            Assert.AreEqual(3, _db.MaxCopies);
            Assert.IsNotEmpty(_db.SourceHash);
        }

        [Test]
        public void Reimport_IsIdempotent()
        {
            var report = CardImporter.Run(false, true);   // dry run
            Assert.AreEqual(0, report.Drift,
                "generated card assets are stale vs cards.json - run tools/regen-cards.sh and commit");
        }

        [Test]
        public void SourceHash_MatchesTheCurrentCardsJson()
        {
            Assert.IsNotNull(_db);
            Assert.AreEqual(CardImporter.HashOfFile(CardImporter.CardsJsonPath), _db.SourceHash,
                "cards.json changed since the last import");
        }

        [Test]
        public void DeckKeys_ResolveThroughTheDatabase()
        {
            Assert.IsNotNull(_db);
            CardDefinition def;
            Assert.IsTrue(_db.TryByDeckKey("fire|Sparkimp", out def));
            Assert.AreEqual(CardKind.Creature, def.Kind);
            Assert.IsTrue(_db.TryByDeckKey("neutral|Ember Bolt", out def));
            Assert.AreEqual(CardKind.Spell, def.Kind);
            Assert.IsFalse(_db.TryByDeckKey("divine|Cherub", out def),
                "divine creatures are not deckable");
        }

        [Test]
        public void AssetCatalog_IsIdenticalToThePureCatalog()
        {
            Assert.IsNotNull(_db);
            var fromAssets = _db.ToCatalog();
            var fromJson = TestData.Catalog;
            Assert.AreEqual(Dump(fromJson), Dump(fromAssets),
                "the SO-built catalog must match the JSON-built catalog field-for-field, order included");
        }

        /// <summary>Canonical text of everything the catalog contract promises, order included.</summary>
        private static string Dump(ICardCatalog cat)
        {
            var sb = new StringBuilder(64 * 1024);

            sb.Append("deckSize=").Append(cat.DeckSize)
              .Append(" maxCopies=").Append(cat.MaxCopies).Append('\n');

            foreach (var c in cat.Creatures)
            {
                sb.Append("C|").Append(c.Name)
                  .Append('|').Append(c.Element)
                  .Append('|').Append(c.PoolIndex)
                  .Append('|').Append(c.Cost).Append('/').Append(c.Attack).Append('/')
                  .Append(c.Health).Append('/').Append(c.Upkeep)
                  .Append('|').Append(c.FirstStrike ? 1 : 0).Append(c.Entrench ? 1 : 0)
                  .Append('|').Append(c.Keyword)
                  .Append('|').Append(N(c.Detonate)).Append(',').Append(N(c.Reap)).Append(',')
                  .Append(N(c.WardHp)).Append(',').Append(N(c.Grow)).Append(',').Append(N(c.Hatch))
                  .Append('|').Append(c.Into == null ? "-" : c.Into.Name + "/" + c.Into.Attack + "/" + c.Into.Health)
                  .Append('|').Append(c.Tribe).Append('/').Append(c.Subtype)
                  .Append('|').Append(c.Deckable ? 1 : 0)
                  .Append('|').Append(c.Slug)
                  .Append('\n');
            }

            foreach (var s in cat.Spells)
            {
                sb.Append("S|").Append(s.Name)
                  .Append('|').Append(s.Cost)
                  .Append('|').Append(s.IsTrap ? 1 : 0)
                  .Append('|').Append(s.Effect)
                  .Append('|').Append(N(s.Value))
                  .Append('|').Append(s.Target)
                  .Append('|').Append(s.Trigger)
                  .Append('|').Append(s.Glyph)
                  .Append('|').Append(s.Slug)
                  .Append('\n');
            }

            foreach (var cc in cat.Commanders)
            {
                sb.Append("CC|").Append(cc.Id.Value)
                  .Append('|').Append(cc.Name)
                  .Append('|').Append(cc.Hp).Append('/').Append(cc.Workers)
                  .Append('|');
                foreach (var col in cc.Colors) sb.Append(col).Append(',');
                sb.Append('|').Append(cc.Dual ? 1 : 0).Append('|');
                foreach (var b in cc.BuildListRaw) sb.Append(b).Append(',');
                sb.Append('\n');
            }

            foreach (var b in cat.Structures)
            {
                sb.Append("B|").Append(b.ExportKey)
                  .Append('|').Append(b.Bid.Value)
                  .Append('|').Append(b.Name)
                  .Append('|').Append(b.Cost).Append('/').Append(b.MaxHp)
                  .Append('|').Append(b.Effect).Append('/').Append(b.Value).Append('/').Append(b.Support)
                  .Append('|').Append(b.Element)
                  .Append('|');
                foreach (var p in b.Prereqs) sb.Append(p).Append(',');
                sb.Append('|').Append(b.UpgradedFrom.IsNone ? "-" : b.UpgradedFrom.Value).Append('|');
                foreach (var u in b.UpgradeTargets) sb.Append(u).Append(',');
                sb.Append('|').Append(b.RowGate)
                  .Append('|').Append(b.Buildable ? 1 : 0)
                  .Append('|').Append(b.Slug)
                  .Append('\n');
            }

            foreach (var e in cat.Elements)
            {
                sb.Append("E|").Append(e.Key)
                  .Append('|').Append(e.Name)
                  .Append('|').Append(e.Glyph)
                  .Append('|').Append(e.Hp).Append('/').Append(e.Workers)
                  .Append('|').Append(e.Deckable ? 1 : 0)
                  .Append('\n');
            }

            var w = cat.WorkerTemplate;
            sb.Append("W|").Append(w == null ? "-" :
                w.Name + "|" + w.Attack + "/" + w.Health + "/" + w.Cost + "/" + w.Upkeep).Append('\n');

            return sb.ToString();
        }

        private static string N(int? v)
        {
            return v.HasValue ? v.Value.ToString() : "null";
        }
    }
}
