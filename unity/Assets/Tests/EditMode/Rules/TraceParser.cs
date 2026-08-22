using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// Reads a trace back in - the half of the harness that makes SHRINKING possible.
    ///
    /// A fuzz failure arrives as four hundred commands, and almost none of them matter. Shrinking
    /// means replaying the trace with candidate commands removed and asking whether it still
    /// diverges, which requires turning the wire form back into real commands. That is this file.
    ///
    /// The one thing it refuses to do is trust a unit ID. Ids come from a per-match counter, so
    /// dropping a single earlier command renumbers everything after it; a shrunk replay that
    /// matched on ids would attach commands to the wrong units and "reproduce" a different bug.
    /// Every reference is resolved through the CELL instead, against the state as it stands at
    /// that moment - the same reasoning that made trace ANSWERS name cells (see TraceRecorder).
    ///
    /// A command whose cell is empty, or whose subject is the wrong kind, comes back null: the
    /// shrink loop skips it rather than failing. Legality is still the engine's call afterwards.
    /// </summary>
    public static class TraceParser
    {
        public sealed class Doc
        {
            public ulong Seed;
            public string You, Foe;
            public readonly List<HandCard> YouDeck = new List<HandCard>();
            public readonly List<HandCard> FoeDeck = new List<HandCard>();
            public readonly List<JsonValue> Commands = new List<JsonValue>();
        }

        public static Doc Parse(string json)
        {
            var root = JsonValue.Parse(json);
            var doc = new Doc();
            doc.Seed = (ulong)root.Get("seed").AsLong;
            doc.You = root.StrReq("you", "trace");
            doc.Foe = root.StrReq("foe", "trace");
            ReadDeck(root.ArrReq("youDeck", "trace"), doc.YouDeck);
            ReadDeck(root.ArrReq("foeDeck", "trace"), doc.FoeDeck);

            var plies = root.ArrReq("plies", "trace");
            for (int i = 0; i < plies.Count; i++)
                doc.Commands.Add(plies[i].ObjReq("cmd", "ply"));
            return doc;
        }

        static void ReadDeck(JsonValue arr, List<HandCard> into)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var key = arr[i].AsString;
                int bar = key.IndexOf('|');
                var color = key.Substring(0, bar);
                var id = key.Substring(bar + 1);
                into.Add(new HandCard(new CardId(id), ElementOf(color)));
            }
        }

        static Element ElementOf(string name)
        {
            if (name == "neutral") return Element.None;
            return (Element)Enum.Parse(typeof(Element), name, true);
        }

        /// <summary>The wire command as an ICommand against THIS state, or null if unresolvable.</summary>
        public static ICommand ToCommand(GameState s, JsonValue c)
        {
            var t = c.StrReq("t", "cmd");
            var actor = (Side)c.IntReq("a", "cmd");

            switch (t)
            {
                case "beginTurn": return new BeginTurnCommand(actor);
                case "harvest": return new HarvestCommand(actor);
                case "draw": return new DrawForTurnCommand(actor);
                case "endTurn": return new EndTurnCommand(actor);
                case "resolve": return new ResolveCombatCommand(actor);

                case "upkeepPay":
                {
                    var at = ParseCell(c.StrReq("at", "cmd"));
                    int id = IdAt(s, at);
                    return id == 0 ? null : (ICommand)new UpkeepPayCommand(actor, at, id);
                }
                case "upkeepSacrifice":
                {
                    var at = ParseCell(c.StrReq("at", "cmd"));
                    int id = IdAt(s, at);
                    return id == 0 ? null : (ICommand)new UpkeepSacrificeCommand(actor, at, id);
                }
                case "move":
                {
                    var from = ParseCell(c.StrReq("from", "cmd"));
                    var to = ParseCell(c.StrReq("to", "cmd"));
                    int id = IdAt(s, from);
                    return id == 0 ? null : (ICommand)new MoveUnitCommand(actor, from, to, id);
                }
                case "play":
                {
                    var mode = (PlayMode)Enum.Parse(typeof(PlayMode), c.StrReq("mode", "cmd"));
                    return new PlayCardCommand(actor, c.IntReq("hand", "cmd"), mode,
                                               ParseCell(c.StrReq("to", "cmd")));
                }
                case "build":
                {
                    var color = c.StrReq("color", "cmd");
                    return new BuildStructureCommand(actor, new StructId(c.StrReq("def", "cmd")),
                        color == "None" ? Element.None : ElementOf(color),
                        ParseCell(c.StrReq("to", "cmd")));
                }
                case "upgrade":
                {
                    var at = ParseCell(c.StrReq("at", "cmd"));
                    int id = IdAt(s, at);
                    return id == 0 ? null : (ICommand)new UpgradeStructureCommand(
                        actor, at, id, new StructId(c.StrReq("to", "cmd")));
                }
                case "pour":
                {
                    var at = ParseCell(c.StrReq("at", "cmd"));
                    int id = IdAt(s, at);
                    return id == 0 ? null : (ICommand)new PourIntoChargeCommand(
                        actor, at, id, c.IntReq("amount", "cmd"));
                }
                case "flip":
                {
                    var at = ParseCell(c.StrReq("at", "cmd"));
                    int id = IdAt(s, at);
                    return id == 0 ? null : (ICommand)new FlipChargeCommand(actor, at, id);
                }
                case "sendMana":
                    return new SendBankedManaCommand(actor, ParseCell(c.StrReq("from", "cmd")),
                                                     ParseCell(c.StrReq("to", "cmd")));
                case "declare":
                {
                    var from = ParseCell(c.StrReq("from", "cmd"));
                    int id = IdAt(s, from);
                    if (id == 0) return null;
                    var target = ParseTarget(s, c.StrReq("target", "cmd"));
                    if (target == null) return null;
                    return new DeclareAttackCommand(actor, from, id, target,
                                                    c.StrReq("defer", "cmd") == "1");
                }
                case "respond":
                {
                    var answer = ParseAnswer(s, c.StrReq("answer", "cmd"));
                    return answer == null ? null : (ICommand)new RespondCommand(actor, answer);
                }
            }
            return null;
        }

        public static CellRef ParseCell(string v)
        {
            int colon = v.IndexOf(':');
            var row = (RowKey)Enum.Parse(typeof(RowKey), v.Substring(0, colon));
            return new CellRef(row, int.Parse(v.Substring(colon + 1)));
        }

        static int IdAt(GameState s, CellRef at)
        {
            var o = s.At(at);
            return o == null ? 0 : o.Id;
        }

        static AttackTarget ParseTarget(GameState s, string v)
        {
            if (v.StartsWith("wall:"))
                return new WallTarget((Side)Enum.Parse(typeof(Side), v.Substring(5)));

            if (v.StartsWith("workers:"))
            {
                var parts = v.Split(':');
                return new WorkerStackTarget((Side)Enum.Parse(typeof(Side), parts[1]),
                                             (WorkerZone)Enum.Parse(typeof(WorkerZone), parts[2]));
            }

            int at = v.IndexOf('@');
            if (at < 0) return null;
            var cell = ParseCell(v.Substring(at + 1));
            int id = IdAt(s, cell);
            return id == 0 ? null : new UnitTarget(cell, id);
        }

        static ChoiceResponse ParseAnswer(GameState s, string v)
        {
            if (v.StartsWith("index:")) return new IndexChosen(int.Parse(v.Substring(6)));

            if (v == "trap:pass") return TrapChosen.Passed;
            if (v.StartsWith("trap:"))
            {
                var cell = ParseCell(v.Substring(5));
                int id = IdAt(s, cell);
                return id == 0 ? null : new TrapChosen(UnitRef.Cell(cell, id));
            }

            if (v.StartsWith("blockers:"))
            {
                var body = v.Substring(9);
                var refs = new List<UnitRef>();
                if (body.Length > 0)
                {
                    var parts = body.Split('+');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] == "pool") continue;      // a worker blocker cannot be named
                        var cell = ParseCell(parts[i]);
                        int id = IdAt(s, cell);
                        if (id != 0) refs.Add(UnitRef.Cell(cell, id));
                    }
                }
                return new BlockersChosen(refs.ToArray());
            }

            return null;
        }
    }
}
