using System.Collections.Generic;
using System.Text;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The C# half of the differential comparison surface. Its twin is tools/diffjs/project.mjs,
    /// and the two must emit the same shape, key for key.
    ///
    /// This is deliberately NOT the state hash. For the JS to reproduce StateCodec's byte stream
    /// it would have to mirror every field and its order, and every later codec tweak would break
    /// the harness for reasons unrelated to the rules. A projection compares what the RULES
    /// decide - who stands where, with what stats, holding what - which is the thing under test.
    /// Byte-identical hashing stays available as a tightening once this is green.
    ///
    /// Keys are emitted SORTED and cells in ascending cell index, so a textual diff is positional
    /// and a mismatch points at a square rather than at a set.
    /// </summary>
    public static class StateProjection
    {
        /// <summary>
        /// FNV-1a over the canonical projection. Small enough to record for every ply, exact
        /// enough that a differing ply is a real difference - and the JS twin computes the same
        /// number from the same string, so a per-ply comparison costs 16 characters instead of a
        /// megabyte of embedded state.
        /// </summary>
        public static string Hash(GameState s, ICardCatalog cat)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(Of(s, cat));
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }

        public static string Of(GameState s, ICardCatalog cat)
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');
            Field(sb, "cells", Cells(s, cat), true);
            Field(sb, "foe", Player(s, Side.Foe), false);
            Field(sb, "over", s.IsOver ? "true" : "false", false);
            Field(sb, "phase", Quote(PhaseName(s.Phase)), false);
            Field(sb, "turn", Quote(SideName(s.Turn)), false);
            Field(sb, "turnNo", s.TurnNumber.ToString(), false);
            Field(sb, "you", Player(s, Side.You), false);
            sb.Append('}');
            return sb.ToString();
        }

        static void Field(StringBuilder sb, string name, string value, bool first)
        {
            if (!first) sb.Append(',');
            sb.Append(Quote(name)).Append(':').Append(value);
        }

        static string Cells(GameState s, ICardCatalog cat)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < Board.Cells; i++)
            {
                var o = s.At(CellRef.FromIndex(i));
                if (o == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Unit(o, i, cat));
            }
            sb.Append(']');
            return sb.ToString();
        }

        static string Unit(BoardObject o, int index, ICardCatalog cat)
        {
            var sb = new StringBuilder();
            var cre = o as CreatureUnit;
            if (cre != null)
            {
                sb.Append("{\"a\":").Append(cre.Attack)
                  .Append(",\"bank\":").Append(cre.Bank)
                  .Append(",\"cnt\":").Append(cre.ChrysalisCount)
                  .Append(",\"hp\":").Append(cre.Hp)
                  .Append(",\"i\":").Append(index)
                  .Append(",\"k\":").Append(Quote(cre.IsWorker ? "worker" : "creature"))
                  .Append(",\"kw\":").Append(cre.Keyword == Keyword.None
                        ? "null" : Quote(cre.Keyword.ToString().ToLowerInvariant()))
                  .Append(",\"maxhp\":").Append(cre.MaxHp)
                  .Append(",\"nm\":").Append(Quote(cre.Name))
                  .Append(",\"oc\":").Append(cre.OverchargeBank)
                  .Append(",\"own\":").Append(Quote(SideName(cre.Owner)))
                  .Append(",\"sick\":").Append(cre.Sick ? "true" : "false")
                  .Append(",\"tap\":").Append(cre.Tapped ? "true" : "false")
                  .Append(",\"tok\":").Append(cre.IsToken ? "true" : "false")
                  .Append('}');
                return sb.ToString();
            }

            var b = o as StructureUnit;
            if (b != null)
            {
                var def = b.DefId.IsNone ? null : cat.Structure(b.DefId, b.Color);
                sb.Append("{\"bank\":").Append(b.Bank)
                  .Append(",\"bid\":").Append(b.DefId.IsNone ? "null" : Quote(b.DefId.Value))
                  .Append(",\"hp\":").Append(b.Hp)
                  .Append(",\"i\":").Append(index)
                  .Append(",\"k\":\"building\"")
                  .Append(",\"maxhp\":").Append(b.MaxHp)
                  .Append(",\"nm\":").Append(Quote(def != null ? def.Name : b.DefId.Value))
                  .Append(",\"own\":").Append(Quote(SideName(b.Owner)))
                  .Append(",\"sup\":").Append(b.Support)
                  .Append('}');
                return sb.ToString();
            }

            var ch = o as ChargeUnit;
            if (ch != null)
            {
                sb.Append("{\"ctype\":").Append(Quote(ch.IsStructure ? "building" : "creature"))
                  .Append(",\"i\":").Append(index)
                  .Append(",\"inv\":").Append(ch.Invested)
                  .Append(",\"k\":\"charge\"")
                  .Append(",\"nm\":").Append(Quote(ch.Card.Name))
                  .Append(",\"own\":").Append(Quote(SideName(ch.Owner)))
                  .Append(",\"setTurn\":").Append(ch.SetTurn)
                  .Append('}');
                return sb.ToString();
            }

            var t = o as TrapUnit;
            if (t != null)
            {
                sb.Append("{\"i\":").Append(index)
                  .Append(",\"k\":\"trap\"")
                  .Append(",\"nm\":").Append(Quote(t.Card.Value))
                  .Append(",\"own\":").Append(Quote(SideName(t.Owner)))
                  .Append(",\"setTurn\":").Append(t.SetTurn)
                  .Append(",\"trigger\":").Append(Quote(t.Trigger.ToString().ToLowerInvariant()))
                  .Append('}');
                return sb.ToString();
            }

            return "{\"i\":" + index + ",\"k\":\"?\",\"own\":" + Quote(SideName(o.Owner)) + "}";
        }

        static string Player(GameState s, Side side)
        {
            var p = s.P(side);

            var names = new List<string>();
            for (int i = 0; i < p.Hand.Count; i++)
                names.Add(p.Hand[i].Snapshot.HasValue ? p.Hand[i].Snapshot.Name : p.Hand[i].Id.Value);
            names.Sort(System.StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("{\"deckN\":").Append(p.Deck.Count)
              .Append(",\"graveN\":").Append(p.Grave.Count)
              .Append(",\"hand\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(names[i]));
            }
            sb.Append("],\"handN\":").Append(p.Hand.Count)
              .Append(",\"life\":").Append(p.Life)
              .Append(",\"mana\":").Append(p.Mana)
              .Append(",\"workers\":{\"back\":").Append(p.Workers[0].Count)
              .Append(",\"center\":").Append(p.Workers[2].Count)
              .Append(",\"front\":").Append(p.Workers[1].Count)
              .Append("},\"workersReady\":{\"back\":").Append(p.Workers[0].ReadyCount)
              .Append(",\"center\":").Append(p.Workers[2].ReadyCount)
              .Append(",\"front\":").Append(p.Workers[1].ReadyCount)
              .Append("}}");
            return sb.ToString();
        }

        static string PhaseName(TurnPhase p)
        {
            switch (p)
            {
                case TurnPhase.Upkeep: return "upkeep";
                case TurnPhase.Draw: return "draw";
                case TurnPhase.Action: return "action";
                default: return "end";
            }
        }

        static string SideName(Side s) { return s == Side.You ? "you" : "foe"; }

        static string Quote(string v)
        {
            if (v == null) return "null";
            return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
