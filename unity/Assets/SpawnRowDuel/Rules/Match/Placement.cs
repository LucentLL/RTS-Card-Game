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
        /// hasPlacement, with the lane over-count fixed for planning use: a free CENTER cell
        /// only counts when it is a flank (the JS scanned lanes too and relied on the placement
        /// step to reject - spec 05 s6.1 note).
        /// </summary>
        public static bool HasPlacement(GameState s, Side owner, StructureDef def, ICardCatalog cat)
        {
            for (int z = 0; z < 3; z++)
            {
                var zone = (WorkerZone)z;
                if (!PlaceRowOk(s, owner, zone, def, cat)) continue;
                var rows = Board.RowsOfZone(owner, zone);
                for (int r = 0; r < rows.Length; r++)
                    for (int col = 0; col < Board.Columns; col++)
                    {
                        if (rows[r] == RowKey.Center && Board.IsLane(col)) continue;
                        if (s.At(new CellRef(rows[r], col)) == null) return true;
                    }
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
