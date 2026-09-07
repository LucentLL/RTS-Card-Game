namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Deployment legality (spec 04 s9): new cards enter only your own back and front rows;
    /// structures may additionally take center FLANKS through the build path. Creatures can
    /// never be summoned into the center - they must march.
    /// </summary>
    public static class Placement
    {
        /// <summary>deployKey - the two rows hand cards may drop into.</summary>
        public static bool IsOwnDeployRow(Side owner, RowKey row)
        {
            return row == Board.RowFor(owner, SlotName.Back)
                || row == Board.RowFor(owner, SlotName.Front);
        }

        /// <summary>
        /// placeRowOK - only negative-support structures are gated: the target row's worker
        /// figure must stay non-negative after the build.
        /// </summary>
        public static bool PlaceRowOk(GameState s, Side owner, WorkerZone zone,
                                      StructureDef def, ICardCatalog cat)
        {
            if (def.Support >= 0) return true;
            return WorkerMath.RowWorkers(s, owner, zone, cat) + def.Support >= 0;
        }

        /// <summary>
        /// The tier's row gate, applied to a BUILD as well as an upgrade.
        ///
        /// The JS only ever gated upgrades (spec 05 s7.3), so a Longhouse marked front-only could
        /// be raised straight into the back row and only its Barracks was ever refused. The gate
        /// is a property of the tier, not of how the tier was reached - and the Outpost is now
        /// centre-only precisely so that a forward post has to stand forward.
        /// </summary>
        public static bool RowGateOk(StructureDef def, WorkerZone zone)
        {
            switch (def.RowGate)
            {
                case RowGate.BackOnly: return zone == WorkerZone.Back;
                case RowGate.FrontOnly: return zone == WorkerZone.Front;
                case RowGate.CenterOnly: return zone == WorkerZone.Center;
                default: return true;
            }
        }

        /// <summary>hasBuild - lineage-aware: an upgraded Keep still counts as a Foundry.</summary>
        public static bool HasBuild(GameState s, Side owner, string familyBid, ICardCatalog cat)
        {
            foreach (var kv in s.ObjectsOf(owner))
            {
                var b = kv.Value as StructureUnit;
                if (b == null || b.IsCommandCenter || b.DefId.IsNone) continue;
                var lineage = cat.Lineage(b.DefId);
                for (int i = 0; i < lineage.Count; i++)
                    if (lineage[i].Value == familyBid) return true;
            }
            return false;
        }

        public static bool PrereqMet(GameState s, Side owner, StructureDef def, ICardCatalog cat)
        {
            for (int i = 0; i < def.Prereqs.Length; i++)
                if (!HasBuild(s, owner, def.Prereqs[i], cat)) return false;
            return true;
        }

        /// <summary>
        /// hasPlacement: is there anywhere this structure could legally stand?
        ///
        /// It used to subtract the three centre LANES, which a structure could not take. The
        /// middle row is seven open cells now, so every empty cell of every zone counts.
        /// </summary>
        public static bool HasPlacement(GameState s, Side owner, StructureDef def, ICardCatalog cat)
        {
            for (int z = 0; z < 3; z++)
            {
                var zone = (WorkerZone)z;
                if (!RowGateOk(def, zone)) continue;
                if (!PlaceRowOk(s, owner, zone, def, cat)) continue;
                var rows = Board.RowsOfZone(owner, zone);
                for (int r = 0; r < rows.Length; r++)
                    for (int col = 0; col < Board.Columns; col++)
                        if (s.At(new CellRef(rows[r], col)) == null) return true;
            }
            return false;
        }

        /// <summary>canBuild = mana + prereqs + somewhere legal to stand.</summary>
        public static bool CanBuild(GameState s, Side owner, StructureDef def, ICardCatalog cat)
        {
            return s.P(owner).Mana >= def.Cost
                && PrereqMet(s, owner, def, cat)
                && HasPlacement(s, owner, def, cat);
        }

        /// <summary>The commander's menu carries exactly these defs; building anything else is
        /// rejected wholesale - host semantics, no trusting the UI.</summary>
        public static bool IsInBuildList(ICardCatalog cat, CommanderId cc, StructureDef def)
        {
            var list = cat.BuildList(cc);
            for (int i = 0; i < list.Count; i++)
                if (list[i].ExportKey == def.ExportKey) return true;
            return false;
        }
    }
}
