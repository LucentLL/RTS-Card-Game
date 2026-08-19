namespace SpawnRowDuel.Rules
{
    public enum Side : byte { You = 0, Foe = 1 }

    /// <summary>Global row addressing, top to bottom. Distance is |difference|.</summary>
    public enum RowKey : byte { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }

    /// <summary>Owner-relative half-board addressing ("which") used by deployment and build code.</summary>
    public enum SlotName : byte { Back = 0, Front = 1, Center = 2 }

    /// <summary>
    /// Economy addressing. Enumeration order IS the settle order (spec 02 s7.1).
    /// Raid has no pool - no support behind enemy lines.
    /// </summary>
    public enum WorkerZone : byte { Back = 0, Front = 1, Center = 2, Raid = 3 }

    public enum TurnPhase : byte { Upkeep = 0, Draw = 1, Action = 2, End = 3 }

    public enum UnitKind : byte { Creature = 0, Building = 1, Charge = 2, Trap = 3 }
}
