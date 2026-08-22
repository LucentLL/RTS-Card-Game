using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// placeBuild (06_mana_workers.js:221-227) as a host-grade command: the def must come off
    /// the commander's own build list, the cell must be a legal structure stand, placeRowOK
    /// gates negative support, and canBuild (mana + lineage prereqs) is checked whole.
    /// </summary>
    public sealed class BuildStructureHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (BuildStructureCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;

            var def = cat.Structure(m.Def, m.Color);
            if (def == null) return Rejection.NotAStructure;
            if (!Placement.IsInBuildList(cat, s.P(m.Actor).Commander, def))
                return Rejection.MissingPrereq;

            // structures stand in your own rows, or on center FLANKS - never lanes, never
            // enemy ground
            bool ownRow = Placement.IsOwnDeployRow(m.Actor, m.To.Row);
            bool centerFlank = m.To.Row == RowKey.Center && !Board.IsLane(m.To.Col);
            if (!ownRow && !centerFlank)
                return m.To.Row == RowKey.Center
                    ? Rejection.CenterLaneForStructure
                    : Rejection.DestinationNotDeployable;
            if (m.To.Col >= Board.Columns) return Rejection.CellNotReal;
            if (s.At(m.To) != null) return Rejection.CellOccupied;

            var zone = Board.ZoneForRow(m.Actor, m.To.Row);
            if (!Placement.PlaceRowOk(s, m.Actor, zone, def, cat)) return Rejection.RowLacksWorkers;

            if (!Placement.PrereqMet(s, m.Actor, def, cat)) return Rejection.MissingPrereq;
            if (s.P(m.Actor).Mana < def.Cost) return Rejection.NotEnoughMana;

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (BuildStructureCommand)cmd;
            var def = cat.Structure(m.Def, m.Color);

            Mana.TrySpend(s, m.Actor, def.Cost);
            var b = UnitFactory.MakeStructure(s, m.Actor, def);
            s.Put(m.To, b);
            ev.Add(new StructureRaised(b.Id, m.To, def.Bid));

            WorkerMath.Resync(s, m.Actor, cat);             // afterDeploy
        }
    }

    /// <summary>
    /// In-place upgrade (07_structures.js:4-31): the unit keeps its tile, id, owner, colour and
    /// bank; damage carries through the rebuild - an upgrade repairs nothing, it only adds the
    /// new tier's extra max HP. Prerequisites are deliberately NOT re-checked (razing your
    /// Foundry does not stop a Keep becoming a Citadel).
    /// </summary>
    public sealed class UpgradeStructureHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (UpgradeStructureCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;

            var b = s.At(m.At) as StructureUnit;
            if (b == null) return Rejection.NotAStructure;
            if (b.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (b.Owner != m.Actor) return Rejection.NotYourUnit;
            if (b.IsCommandCenter || b.DefId.IsNone) return Rejection.NotUpgradeable;

            var src = cat.Structure(b.DefId, b.Color);
            if (src == null) return Rejection.NotUpgradeable;

            bool listed = false;
            for (int i = 0; i < src.UpgradeTargets.Length; i++)
                if (src.UpgradeTargets[i] == m.Target.Value) listed = true;
            if (!listed) return Rejection.NotAnUpgradeTarget;

            // the instance's own colour rides down the chain - an Emberforge can only ever
            // become a Grand Emberforge
            var def = cat.Structure(m.Target, b.Color);
            if (def == null) return Rejection.NotAnUpgradeTarget;

            var zone = Board.ZoneForRow(m.Actor, m.At.Row);
            if (def.RowGate == RowGate.BackOnly && zone != WorkerZone.Back) return Rejection.WrongRowForTier;
            if (def.RowGate == RowGate.FrontOnly && zone != WorkerZone.Front) return Rejection.WrongRowForTier;

            if (s.P(m.Actor).Mana < def.Cost) return Rejection.NotEnoughMana;

            // negative-support headroom: remove the current support, add the target's
            if (def.Support < 0 &&
                WorkerMath.RowWorkers(s, m.Actor, zone, cat) - b.Support + def.Support < 0)
                return Rejection.RowLacksWorkers;

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (UpgradeStructureCommand)cmd;
            var b = (StructureUnit)s.At(m.At);
            var def = cat.Structure(m.Target, b.Color);
            var from = b.DefId;

            Mana.TrySpend(s, m.Actor, def.Cost);

            int damage = Math.Max(0, b.MaxHp - b.Hp);
            b.DefId = def.Bid;
            b.Name = def.Name;
            b.Cost = def.Cost;
            b.Value = def.Value;
            b.Support = def.Support;
            b.Effect = def.Effect;
            b.MaxHp = def.MaxHp;
            b.Hp = Math.Max(1, def.MaxHp - damage);

            ev.Add(new StructureUpgraded(b.Id, from, def.Bid));
            WorkerMath.Resync(s, m.Actor, cat);
        }
    }
}
