using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M7: the build menu and in-place upgrades - lineage prerequisites, row gates, the
    /// negative-support worker gate, and the damage-carry rebuild (spec 05 s6-s7).
    /// </summary>
    public class StructureBuildTests
    {
        private static DuelEngine Engine(out GameState s, string you = "fire")
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId(you), new CommanderId("water"), 41, RulesOptions.JsParity);
            var e = new DuelEngine(s, TestData.Catalog);
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
            return e;
        }

        [Test]
        public void Build_PlacesFromTheMenu_AndPaysTheDef()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 5;

            var r = e.Apply(new BuildStructureCommand(Side.You, new StructId("foundry"),
                Element.None, new CellRef(RowKey.YouBack, 0)));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            var b = s.At(new CellRef(RowKey.YouBack, 0)) as StructureUnit;
            Assert.IsNotNull(b);
            Assert.AreEqual("foundry", b.DefId.Value);
            Assert.AreEqual(3000, b.Hp);
            Assert.AreEqual(3, s.P(Side.You).Mana, "the Foundry costs ◆2");
            Assert.AreEqual(4, s.P(Side.You).Workers[(int)WorkerZone.Back].Count,
                "afterDeploy: wk 2 + sup 2 materialise (the new two arrive sick)");
        }

        [Test]
        public void Build_Prerequisites_WalkTheLineage()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 30;

            Assert.AreEqual(Rejection.MissingPrereq,
                e.CanApply(new BuildStructureCommand(Side.You, new StructId("forge"),
                    Element.Fire, new CellRef(RowKey.YouBack, 1))),
                "a forge needs a Foundry");

            Assert.IsTrue(e.Apply(new BuildStructureCommand(Side.You, new StructId("foundry"),
                Element.None, new CellRef(RowKey.YouBack, 0))).Applied);

            // upgrade the foundry away - its lineage must STILL satisfy the prereq
            var foundry = (StructureUnit)s.At(new CellRef(RowKey.YouBack, 0));
            Assert.IsTrue(e.Apply(new UpgradeStructureCommand(Side.You,
                new CellRef(RowKey.YouBack, 0), foundry.Id, new StructId("keep"))).Applied);
            Assert.AreEqual("keep", foundry.DefId.Value);

            var r = e.Apply(new BuildStructureCommand(Side.You, new StructId("forge"),
                Element.Fire, new CellRef(RowKey.YouBack, 1)));
            Assert.IsTrue(r.Applied, "a Keep still counts as a Foundry: " + r.Rejection);
        }

        [Test]
        public void Build_OnlyOffYourOwnMenu()
        {
            GameState s;
            var e = Engine(out s);            // fire commander
            s.P(Side.You).Mana = 30;
            s.Put(new CellRef(RowKey.YouBack, 0), UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None)));

            Assert.AreEqual(Rejection.MissingPrereq,
                e.CanApply(new BuildStructureCommand(Side.You, new StructId("forge"),
                    Element.Water, new CellRef(RowKey.YouBack, 1))),
                "a fire commander's menu holds no Tidewell");
        }

        [Test]
        public void Build_GeometryAndWorkerGates()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 30;

            Assert.IsTrue(e.Apply(new BuildStructureCommand(Side.You, new StructId("foundry"),
                Element.None, new CellRef(RowKey.Center, 2))).Applied,
                "center FLANKS take structures");

            Assert.AreEqual(Rejection.CenterLaneForStructure,
                e.CanApply(new BuildStructureCommand(Side.You, new StructId("encampment"),
                    Element.None, new CellRef(RowKey.Center, 3))),
                "center LANES are creature ground");

            Assert.AreEqual(Rejection.DestinationNotDeployable,
                e.CanApply(new BuildStructureCommand(Side.You, new StructId("encampment"),
                    Element.None, new CellRef(RowKey.FoeBack, 0))));

            // the tower's -2 support: legal where the figure can bear it, refused where not.
            // (prereq: forge -> foundry chain first)
            s.Put(new CellRef(RowKey.YouBack, 6), UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("forge"), Element.Fire)));
            Assert.AreEqual(Rejection.RowLacksWorkers,
                e.CanApply(new BuildStructureCommand(Side.You, new StructId("tower"),
                    Element.None, new CellRef(RowKey.YouFront, 0))),
                "an empty front row has no ⚒ to spare for a -2 tower");
            Assert.IsTrue(e.Apply(new BuildStructureCommand(Side.You, new StructId("tower"),
                Element.None, new CellRef(RowKey.YouBack, 1))).Applied,
                "the back row's free workforce absorbs the -2");
        }

        [Test]
        public void Upgrade_RowGates_DamageCarry_AndIdentity()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 30;

            // a foundry in the FRONT row cannot become a Keep (back-gated tier)
            var frontFoundry = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            s.Put(new CellRef(RowKey.YouFront, 0), frontFoundry);
            Assert.AreEqual(Rejection.WrongRowForTier,
                e.CanApply(new UpgradeStructureCommand(Side.You, new CellRef(RowKey.YouFront, 0),
                    frontFoundry.Id, new StructId("keep"))));

            // damage carries: a foundry at 1000/3000 (2000 damage) becomes a keep at 3000/5000
            var backFoundry = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            backFoundry.Hp = 1000;
            backFoundry.Bank = 3;
            s.Put(new CellRef(RowKey.YouBack, 2), backFoundry);
            int id = backFoundry.Id;

            var r = e.Apply(new UpgradeStructureCommand(Side.You, new CellRef(RowKey.YouBack, 2),
                id, new StructId("keep")));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            var keep = (StructureUnit)s.At(new CellRef(RowKey.YouBack, 2));
            Assert.AreEqual(id, keep.Id, "the upgrade mutates IN PLACE - same unit id");
            Assert.AreEqual("keep", keep.DefId.Value);
            Assert.AreEqual(5000, keep.MaxHp);
            Assert.AreEqual(3000, keep.Hp, "an upgrade repairs nothing - the 2000 damage carries");
            Assert.AreEqual(3, keep.Bank, "banked ◆ survives the rebuild");

            Assert.AreEqual(Rejection.NotAnUpgradeTarget,
                e.CanApply(new UpgradeStructureCommand(Side.You, new CellRef(RowKey.YouBack, 2),
                    id, new StructId("barracks"))),
                "a Keep's up2 lists citadel, nothing else");
        }

        [Test]
        public void Upgrade_NegativeSupportHeadroom_UsesTheSwapFormula()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 30;

            // Outpost (sup +1) alone in the FRONT row: figure 1; 1 - 1 + (-2) = -2 -> refused
            var frontOutpost = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("outpost"), Element.None));
            s.Put(new CellRef(RowKey.YouFront, 3), frontOutpost);
            Assert.AreEqual(Rejection.RowLacksWorkers,
                e.CanApply(new UpgradeStructureCommand(Side.You, new CellRef(RowKey.YouFront, 3),
                    frontOutpost.Id, new StructId("tower"))));

            // in the BACK row: figure wk2 + 1 = 3; 3 - 1 - 2 = 0 -> allowed
            var backOutpost = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("outpost"), Element.None));
            s.Put(new CellRef(RowKey.YouBack, 5), backOutpost);
            var r = e.Apply(new UpgradeStructureCommand(Side.You, new CellRef(RowKey.YouBack, 5),
                backOutpost.Id, new StructId("tower")));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());
            Assert.AreEqual(-2, backOutpost.Support);
        }

        [Test]
        public void Forge_UpgradesWithinItsOwnElement()
        {
            GameState s;
            var e = Engine(out s);
            s.P(Side.You).Mana = 30;

            var forge = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("forge"), Element.Fire));
            s.Put(new CellRef(RowKey.YouBack, 4), forge);
            Assert.AreEqual(Element.Fire, forge.Color);

            Assert.IsTrue(e.Apply(new UpgradeStructureCommand(Side.You,
                new CellRef(RowKey.YouBack, 4), forge.Id, new StructId("grandforge"))).Applied);
            Assert.AreEqual("grandforge", forge.DefId.Value);
            Assert.AreEqual(Element.Fire, forge.Color,
                "an Emberforge becomes a GRAND Emberforge - the colour rides the chain");
            Assert.AreEqual(3, forge.Value, "grand forge yields ◆3");
        }
    }
}
