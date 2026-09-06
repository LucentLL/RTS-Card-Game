using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View
{
    /// <summary>Why a node is or is not actionable right now. Drives both colour and the note.</summary>
    public enum BuildNodeState
    {
        /// <summary>In the menu, unlocked, affordable, and there is somewhere to put it.</summary>
        Ready,
        /// <summary>Unlocked and placeable, but you cannot pay for it yet.</summary>
        NoMana,
        /// <summary>Unlocked and affordable, but no legal cell (or no row with the workers to crew it).</summary>
        NoRoom,
        /// <summary>Its prerequisite is not standing.</summary>
        Locked,
        /// <summary>Not in the menu; you own a structure that can become it, and can pay.</summary>
        UpgradeReady,
        /// <summary>You own the source, but the price or the row gate is in the way.</summary>
        UpgradeLater,
        /// <summary>You do not own anything that upgrades into it.</summary>
        UpgradeLocked,
    }

    public struct BuildNode
    {
        public StructureDef Def;
        public BuildNodeState State;
        /// <summary>Short blocker note for the node's second line; "" when Ready.</summary>
        public string Note;
        public bool IsUpgrade;

        public bool Actionable { get { return State == BuildNodeState.Ready; } }
    }

    /// <summary>One horizontal chain: a base structure and the tiers it grows into.</summary>
    public sealed class BuildRow
    {
        public readonly List<BuildNode> Nodes = new List<BuildNode>();
        /// <summary>How many node columns to skip before this row starts - a branch continuation
        /// sits under its parent rather than at the left margin.</summary>
        public int Indent;
        /// <summary>Group heading, set only on the first row of a prerequisite group.</summary>
        public string Header;
        /// <summary>How many rows this heading covers, so it can be set against them in the
        /// gutter instead of costing a row of its own.</summary>
        public int GroupSpan;
    }

    /// <summary>
    /// The build menu as a TECH TREE rather than a list.
    ///
    /// Two relations exist in the data and the flat list showed neither: `prereq` (what must be
    /// standing before a thing can be built) and `upgradesTo` (what a standing thing can become).
    /// They are drawn differently on purpose - prerequisites group the rows, upgrades run along
    /// them - because they are answered by different actions. You satisfy a prerequisite by
    /// building something else; you take an upgrade by selecting the building on the board.
    ///
    /// Rows are seeded from the commander's build list IN MENU ORDER, which is also the AI's
    /// priority order, so the tree reads top-to-bottom the way the game itself values it. An
    /// upgrade target that is ALSO in the build list is skipped as a continuation and gets its own
    /// row instead - which is why Cannon Tower and Grand Forge appear as roots. That is not a
    /// layout choice, it is the data telling the truth about itself: both are reachable from the
    /// menu without ever raising the tier below them.
    /// </summary>
    public static class BuildTree
    {
        /// <summary>Deepest chain the layout will follow; the real data tops out at three.</summary>
        public const int MaxColumns = 4;

        public static List<BuildRow> Build(GameState s, Side side, ICardCatalog cat)
        {
            var rows = new List<BuildRow>();
            var menu = cat.BuildList(s.P(side).Commander);
            var color = s.P(side).PrimaryColor;

            // ExportKeys that own a row of their own, so a chain never re-draws one.
            var seeded = new HashSet<string>();
            for (int i = 0; i < menu.Count; i++) seeded.Add(menu[i].ExportKey);

            // Bucketed by prerequisite, groups in first-seen order, menu order preserved inside
            // each. Menu order alone would print "REQUIRES A FORGE" twice, because the Grand Forge
            // sits after the Reliquary in every commander's list - a heading that comes back is a
            // heading the reader stops trusting.
            var order = new List<string>();
            var byGroup = new Dictionary<string, List<BuildRow>>();
            var branches = new List<BuildRow>();

            for (int i = 0; i < menu.Count; i++)
            {
                var def = menu[i];
                var root = new BuildRow();

                // Branches are collected separately and appended after their own root, so a
                // continuation never lands above the row it hangs off.
                branches.Clear();
                Chain(s, side, cat, def, root, branches, seeded, 0, false);

                string g = GroupOf(def, cat, color);
                List<BuildRow> bucket;
                if (!byGroup.TryGetValue(g, out bucket))
                {
                    bucket = new List<BuildRow>();
                    byGroup[g] = bucket;
                    order.Add(g);
                }
                bucket.Add(root);
                bucket.AddRange(branches);
            }

            for (int i = 0; i < order.Count; i++)
            {
                var bucket = byGroup[order[i]];
                bucket[0].Header = order[i];
                bucket[0].GroupSpan = bucket.Count;
                rows.AddRange(bucket);
            }
            return rows;
        }

        static void Chain(GameState s, Side side, ICardCatalog cat, StructureDef def,
                          BuildRow row, List<BuildRow> branches, HashSet<string> seeded,
                          int col, bool viaUpgrade)
        {
            row.Nodes.Add(NodeFor(s, side, cat, def, viaUpgrade));
            if (col + 1 >= MaxColumns) return;

            bool first = true;
            for (int i = 0; i < def.UpgradeTargets.Length; i++)
            {
                var next = cat.Structure(new StructId(def.UpgradeTargets[i]), def.Element);
                if (next == null) continue;
                if (seeded.Contains(next.ExportKey)) continue;   // it has a row of its own
                seeded.Add(next.ExportKey);

                if (first)
                {
                    first = false;
                    Chain(s, side, cat, next, row, branches, seeded, col + 1, true);
                }
                else
                {
                    var branch = new BuildRow { Indent = col + 1 };
                    Chain(s, side, cat, next, branch, branches, seeded, col + 1, true);
                    branches.Add(branch);
                }
            }
        }

        // ── node state ───────────────────────────────────────────────────────────────────

        static BuildNode NodeFor(GameState s, Side side, ICardCatalog cat,
                                 StructureDef def, bool viaUpgrade)
        {
            var n = new BuildNode { Def = def, IsUpgrade = viaUpgrade, Note = "" };
            n.State = viaUpgrade
                ? UpgradeState(s, side, cat, def, out n.Note)
                : BuildState(s, side, cat, def, out n.Note);
            return n;
        }

        static BuildNodeState BuildState(GameState s, Side side, ICardCatalog cat,
                                         StructureDef def, out string note)
        {
            if (!Placement.PrereqMet(s, side, def, cat))
            {
                note = def.Prereqs.Length > 0
                    ? "needs " + Label(def.Prereqs[0], cat, s.P(side).PrimaryColor)
                    : "locked";
                return BuildNodeState.Locked;
            }

            if (!Placement.HasPlacement(s, side, def, cat))
            {
                // A negative-support structure is refused by the ROW's worker figure, not by the
                // board being full - and "no room" on an empty board is a lie the player cannot act
                // on. The Cannon Tower is the only card that reaches this.
                note = def.Support < 0 ? "needs " + (-def.Support) + " spare workers" : "no room";
                return BuildNodeState.NoRoom;
            }

            if (s.P(side).Mana < def.Cost) { note = "costs " + def.Cost + " mana"; return BuildNodeState.NoMana; }

            note = "";
            return BuildNodeState.Ready;
        }

        /// <summary>
        /// An upgrade tier is never built from this menu - it is taken by selecting the structure
        /// standing on the board. The node is here to say the tier EXISTS and whether the line to
        /// it is open, which is the half the flat list could not say at all.
        /// </summary>
        static BuildNodeState UpgradeState(GameState s, Side side, ICardCatalog cat,
                                           StructureDef def, out string note)
        {
            bool ownSource = false, rowOk = false;

            foreach (var kv in s.ObjectsOf(side))
            {
                var b = kv.Value as StructureUnit;
                if (b == null || b.IsCommandCenter || b.DefId.IsNone) continue;

                var src = cat.Structure(b.DefId, b.Color);
                if (src == null) continue;

                bool listed = false;
                for (int i = 0; i < src.UpgradeTargets.Length; i++)
                    if (src.UpgradeTargets[i] == def.Bid.Value) listed = true;
                if (!listed) continue;

                ownSource = true;

                var zone = Board.ZoneForRow(side, kv.Key.Row);
                if (def.RowGate == RowGate.BackOnly && zone != WorkerZone.Back) continue;
                if (def.RowGate == RowGate.FrontOnly && zone != WorkerZone.Front) continue;
                rowOk = true;
            }

            if (!ownSource)
            {
                note = def.UpgradedFrom.IsNone
                    ? "upgrade"
                    : "from " + Label(def.UpgradedFrom.Value, cat, s.P(side).PrimaryColor);
                return BuildNodeState.UpgradeLocked;
            }

            if (!rowOk)
            {
                note = def.RowGate == RowGate.BackOnly ? "back row only" : "front row only";
                return BuildNodeState.UpgradeLater;
            }

            if (s.P(side).Mana < def.Cost)
            {
                note = "costs " + def.Cost + " mana";
                return BuildNodeState.UpgradeLater;
            }

            note = "upgrade on the board";
            return BuildNodeState.UpgradeReady;
        }

        // ── labels ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A prerequisite is a FAMILY, not a card: any Forge satisfies "forge", which matters to a
        /// dual commander holding two of them. So the parameterised families read generically and
        /// everything else reads by its own name.
        /// </summary>
        public static string Label(string bid, ICardCatalog cat, Element color)
        {
            if (bid == "forge") return "a Forge";
            if (bid == "grandforge") return "a Grand Forge";

            var d = cat.Structure(new StructId(bid), color);
            if (d == null) return bid;
            if (d.Name.StartsWith("The ")) return d.Name;

            // "a Outpost" is the kind of seam that makes generated text read as generated.
            char c0 = char.ToUpperInvariant(d.Name[0]);
            bool vowel = c0 == 'A' || c0 == 'E' || c0 == 'I' || c0 == 'O' || c0 == 'U';
            return (vowel ? "an " : "a ") + d.Name;
        }

        static string GroupOf(StructureDef def, ICardCatalog cat, Element color)
        {
            if (def.Prereqs.Length == 0) return "NO PREREQUISITE";
            return "REQUIRES " + Label(def.Prereqs[0], cat, color).ToUpperInvariant();
        }
    }
}
