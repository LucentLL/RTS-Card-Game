using System.Collections.Generic;
using System.Text;
using SpawnRowDuel.Ai;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// A match, written down: both decks, every command in order, and the state hash after each
    /// one. This is the input contract of the M12 differential harness and, on its own, a golden
    /// fixture that pins the C# engine's behaviour ply by ply.
    ///
    /// The decks are recorded EXPLICITLY rather than re-derived from the seed, because the two
    /// engines cannot be made to shuffle alike (D16) - and they do not need to be. The AI's random
    /// choices are already frozen into the trace as concrete DeclareAttack commands, so a replay
    /// consults no RNG at all and the harness ends up testing the rules rather than the generator.
    ///
    /// One line per ply on purpose: when a diff fails, the line number IS the ply.
    /// </summary>
    public static class TraceRecorder
    {
        public sealed class Trace
        {
            public string Json;
            /// <summary>One full projection per ply, for pinpointing a divergence the hashes
            /// only flag. Written beside the golden on demand; never committed.</summary>
            public string Projections;
            public int Plies;
            public bool Over;
            public MatchOutcome Outcome;
            public Rejection Rejection;
        }

        /// <summary>Where the next command comes from: two scripted AIs, or the fuzzer.</summary>
        public delegate ICommand CommandSource(DuelEngine engine);

        /// <summary>
        /// A deliberate rules mutation applied after a chosen ply. It exists for ONE purpose: to
        /// give the shrinker a divergence it can be tested against, because a shrinker that has
        /// never converged on a known-minimal answer is not a tool, it is a hope.
        ///
        /// It is a parameter rather than a static hook (DECISIONS D9): an assignable static is a
        /// live hazard the moment two matches share a process, and this one would silently poison
        /// the golden traces.
        /// </summary>
        public delegate void Poison(GameState s, ICommand cmd, int ply);

        public static Trace RecordSelfPlay(ICardCatalog cat, string you, string foe,
                                           ulong seed, int maxTurns)
        {
            var policies = new[] { new ScriptedAiPolicy(Side.You), new ScriptedAiPolicy(Side.Foe) };
            return Record(cat, you, foe, seed, maxTurns * 40, engine =>
            {
                for (int i = 0; i < policies.Length; i++)
                {
                    var cmd = policies[i].Next(engine);
                    if (cmd != null) return cmd;
                }
                return null;
            });
        }

        /// <summary>
        /// A match played by <see cref="FuzzPolicy"/> - the same trace format, so replay.mjs
        /// consumes it without knowing which side of the harness produced it. The fuzz seed is
        /// separate from the match seed on purpose: the match RNG belongs to the RULES, and a
        /// fuzzer that drew from it would be changing the game it is supposed to be exploring.
        /// </summary>
        public static Trace RecordFuzz(ICardCatalog cat, string you, string foe,
                                       ulong seed, ulong fuzzSeed, int maxPlies, int turnBudget,
                                       Poison poison = null)
        {
            var policy = new FuzzPolicy(fuzzSeed, turnBudget);
            return Record(cat, you, foe, seed, maxPlies, policy.Next, null, null, true, poison);
        }

        /// <summary>
        /// Re-record a trace with some plies REMOVED - the shrink step.
        ///
        /// Dropping a command changes everything downstream, so this is a genuine replay rather
        /// than an edit: each surviving command is re-resolved against the state it now meets
        /// (TraceParser resolves by cell, never by id) and simply skipped if it has become
        /// illegal. What comes out is a shorter LEGAL trace, ready to put back through the JS
        /// oracle. Whether it still diverges is the shrink loop's question, not this one's.
        /// </summary>
        public static Trace RecordFromCommands(ICardCatalog cat, TraceParser.Doc doc,
                                               HashSet<int> drop,
                                               Poison poison = null)
        {
            int cursor = 0;
            CommandSource source = engine =>
            {
                while (cursor < doc.Commands.Count)
                {
                    int at = cursor++;
                    if (drop != null && drop.Contains(at)) continue;
                    var cmd = TraceParser.ToCommand(engine.State, doc.Commands[at]);
                    if (cmd == null) continue;
                    if (engine.CanApply(cmd) != Rejection.None) continue;
                    return cmd;
                }
                return null;
            };

            return Record(cat, doc.You, doc.Foe, doc.Seed, doc.Commands.Count, source,
                          doc.YouDeck, doc.FoeDeck, false, poison);
        }

        static Trace Record(ICardCatalog cat, string you, string foe,
                            ulong seed, int maxPlies, CommandSource source,
                            List<HandCard> youDeckIn = null, List<HandCard> foeDeckIn = null,
                            bool handOffWhenIdle = true, Poison poison = null)
        {
            // Build the decks OUTSIDE the match and inject them, so the trace can record the full
            // 40 as dealt rather than the 36 that survive the opening hand. Injecting is also
            // exactly what the JS replay does (startGame takes both decks), which keeps the two
            // sides symmetric - and it is what lets the harness sidestep the unreconcilable
            // shuffles entirely (DECISIONS D16).
            //
            // A shrink pass hands the decks back IN, because re-deriving them from the seed is
            // only correct while the deck factory is untouched, and a shrunk trace must stand on
            // its own.
            var youCc = cat.Commander(new CommanderId(you));
            var foeCc = cat.Commander(new CommanderId(foe));
            var deckRng = new Pcg32(seed);
            var youDeck = youDeckIn ?? DeckFactory.DeckOf(cat, youCc.Colors, deckRng);
            var foeDeck = foeDeckIn ?? DeckFactory.DeckOf(cat, foeCc.Colors, deckRng);

            var s = MatchSetup.NewMatch(cat, new CommanderId(you), new CommanderId(foe),
                                        new List<HandCard>(youDeck), new List<HandCard>(foeDeck),
                                        seed, RulesOptions.JsParity);
            var engine = new DuelEngine(s, cat);

            var sb = new StringBuilder(1 << 16);
            var proj = new StringBuilder(1 << 16);
            sb.Append("{\n");
            sb.Append("\"seed\":").Append(seed).Append(",\n");
            sb.Append("\"you\":\"").Append(you).Append("\",\"foe\":\"").Append(foe).Append("\",\n");
            sb.Append("\"flags\":").Append(s.Options.FlagBits).Append(",\n");
            WriteDeck(sb, "youDeck", youDeck);      // the FULL 40, before the opening hand
            WriteDeck(sb, "foeDeck", foeDeck);
            sb.Append("\"open\":\"").Append(Hash(s)).Append("\",\n");
            // the opening board as the differential harness compares it - the JS replay rebuilds
            // these same decks, calls startGame, and must land on this exact projection
            sb.Append("\"openProjection\":").Append(StateProjection.Of(s, cat)).Append(",\n");
            sb.Append("\"plies\":[\n");

            var trace = new Trace();
            int plies = 0;
            bool first = true;

            while (!s.IsOver && plies < maxPlies)
            {
                ICommand cmd = source(engine);

                if (cmd == null)
                {
                    if (handOffWhenIdle && s.Pending == null && s.Phase == TurnPhase.End)
                        cmd = new BeginTurnCommand(TurnMachine.Other(s.Turn));
                    else break;
                }

                var r = engine.Apply(cmd);
                if (r.Status == CommandStatus.Rejected)
                {
                    trace.Rejection = r.Rejection;
                    break;
                }

                if (poison != null) poison(s, cmd, plies + 1);   // before the hash: it must show

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  {\"i\":").Append(++plies)
                  .Append(",\"cmd\":").Append(Describe(cmd))
                  .Append(",\"h\":\"").Append(Hash(s))
                  .Append("\",\"p\":\"").Append(StateProjection.Hash(s, cat)).Append("\"}");
                proj.Append(StateProjection.Of(s, cat)).Append('\n');
            }

            sb.Append("\n],\n");
            sb.Append("\"plies_total\":").Append(plies).Append(",\n");
            sb.Append("\"over\":").Append(s.IsOver ? "true" : "false").Append(",\n");
            sb.Append("\"outcome\":\"").Append(s.Outcome).Append("\",\n");
            sb.Append("\"final\":\"").Append(Hash(s)).Append("\"\n}\n");

            trace.Json = sb.ToString();
            trace.Projections = proj.ToString();
            trace.Plies = plies;
            trace.Over = s.IsOver;
            trace.Outcome = s.Outcome;
            return trace;
        }

        static string Hash(GameState s)
        {
            return StateCodec.Hash(s).ToString("x16");
        }

        /// <summary>
        /// Deck entries use the JS registry's own key shape - "fire|Ashfang", "neutral|Riptide" -
        /// so the replay can rebuild an identical deck through CARD_BY_KEY without guessing. The
        /// name alone is ambiguous: colour is what separates two pools' cards.
        /// </summary>
        static void WriteDeck(StringBuilder sb, string name, List<HandCard> deck)
        {
            sb.Append('"').Append(name).Append("\":[");
            for (int i = 0; i < deck.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Esc(DeckKeyOf(deck[i]))).Append('"');
            }
            sb.Append("],\n");
        }

        public static string DeckKeyOf(HandCard c)
        {
            string color = c.Color == Element.None
                ? "neutral"
                : c.Color.ToString().ToLowerInvariant();
            return color + "|" + c.Id.Value;
        }

        /// <summary>
        /// The wire form of a command. Deliberately terse and total - a command the harness
        /// cannot spell is a command the JS side cannot replay, so an unknown type is loud.
        /// </summary>
        public static string Describe(ICommand cmd)
        {
            var a = (int)cmd.Actor;

            var begin = cmd as BeginTurnCommand;
            if (begin != null) return Obj("beginTurn", a);
            if (cmd is HarvestCommand) return Obj("harvest", a);
            if (cmd is DrawForTurnCommand) return Obj("draw", a);
            if (cmd is EndTurnCommand) return Obj("endTurn", a);
            if (cmd is ResolveCombatCommand) return Obj("resolve", a);

            var pay = cmd as UpkeepPayCommand;
            if (pay != null) return Obj("upkeepPay", a, Cell("at", pay.Target), Num("id", pay.UnitId));

            var sac = cmd as UpkeepSacrificeCommand;
            if (sac != null) return Obj("upkeepSacrifice", a, Cell("at", sac.Target), Num("id", sac.UnitId));

            var move = cmd as MoveUnitCommand;
            if (move != null)
                return Obj("move", a, Cell("from", move.From), Cell("to", move.To), Num("id", move.UnitId));

            var play = cmd as PlayCardCommand;
            if (play != null)
                return Obj("play", a, Num("hand", play.HandIndex),
                           Str("mode", play.Mode.ToString()), Cell("to", play.To));

            var build = cmd as BuildStructureCommand;
            if (build != null)
                return Obj("build", a, Str("def", build.Def.Value),
                           Str("color", build.Color.ToString()), Cell("to", build.To));

            var up = cmd as UpgradeStructureCommand;
            if (up != null)
                return Obj("upgrade", a, Cell("at", up.At), Num("id", up.UnitId),
                           Str("to", up.Target.Value));

            var pour = cmd as PourIntoChargeCommand;
            if (pour != null)
                return Obj("pour", a, Cell("at", pour.At), Num("id", pour.UnitId),
                           Num("amount", pour.Amount));

            var flip = cmd as FlipChargeCommand;
            if (flip != null) return Obj("flip", a, Cell("at", flip.At), Num("id", flip.UnitId));

            var send = cmd as SendBankedManaCommand;
            if (send != null) return Obj("sendMana", a, Cell("from", send.From), Cell("to", send.To));

            var declare = cmd as DeclareAttackCommand;
            if (declare != null)
                return Obj("declare", a, Cell("from", declare.Attacker), Num("id", declare.UnitId),
                           Str("target", TargetOf(declare.Target)),
                           Str("defer", declare.DeferBlockers ? "1" : "0"));

            var respond = cmd as RespondCommand;
            if (respond != null) return Obj("respond", a, Str("answer", AnswerOf(respond.Response)));

            return Obj("UNKNOWN:" + cmd.GetType().Name, a);
        }

        static string TargetOf(AttackTarget t)
        {
            var u = t as UnitTarget;
            if (u != null) return "unit#" + u.UnitId + "@" + u.Cell.Row + ":" + u.Cell.Col;
            var w = t as WallTarget;
            if (w != null) return "wall:" + w.Defender;
            var s = t as WorkerStackTarget;
            if (s != null) return "workers:" + s.Owner + ":" + s.Zone;
            return "none";
        }

        /// <summary>
        /// Answers name units by POSITION, not by unit id. The two engines run independent id
        /// counters, so an id is meaningless across the harness boundary - but the boards are in
        /// lockstep, so a cell identifies the same unit on both sides. (An id-based answer let the
        /// replay index-match and silently attach the wrong blocker.)
        /// </summary>
        static string AnswerOf(ChoiceResponse r)
        {
            var b = r as BlockersChosen;
            if (b != null)
            {
                var sb = new StringBuilder("blockers:");
                for (int i = 0; i < b.Blockers.Length; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append(RefCell(b.Blockers[i]));
                }
                return sb.ToString();
            }
            var idx = r as IndexChosen;
            if (idx != null) return "index:" + idx.Index;
            var trap = r as TrapChosen;
            if (trap != null) return trap.Pass ? "trap:pass" : "trap:" + RefCell(trap.Trap);
            return "none";
        }

        static string RefCell(UnitRef r)
        {
            if (r.Kind != UnitRefKind.Cell) return "pool";
            var c = r.AsCell;
            return c.Row + ":" + c.Col;
        }

        static string Obj(string type, int actor, params string[] fields)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"").Append(type).Append("\",\"a\":").Append(actor);
            for (int i = 0; i < fields.Length; i++) sb.Append(',').Append(fields[i]);
            sb.Append('}');
            return sb.ToString();
        }

        static string Cell(string name, CellRef c)
        {
            return "\"" + name + "\":\"" + c.Row + ":" + c.Col + "\"";
        }

        static string Num(string name, int v) { return "\"" + name + "\":" + v; }
        static string Str(string name, string v) { return "\"" + name + "\":\"" + Esc(v) + "\""; }

        static string Esc(string v)
        {
            if (v == null) return "";
            return v.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
