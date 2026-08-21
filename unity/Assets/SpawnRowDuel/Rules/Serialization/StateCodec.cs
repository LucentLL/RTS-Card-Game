using System.Collections.Generic;
using System.Text;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// One traversal, two encodings. The binary writer feeds the state hash and (later) save files;
    /// the canonical-JSON writer produces human-diffable dumps for differential testing against the
    /// JS build. Both walk the SAME Write() below, so they cannot drift apart.
    ///
    /// Hand-written on purpose. Reflection-based serializers do not contractually pin field order
    /// or default-value elision across versions, so a package bump could silently change the byte
    /// stream and therefore every recorded replay hash.
    /// </summary>
    public interface IStateWriter
    {
        void BeginObject(string name);
        void EndObject();
        void BeginArray(string name, int count);
        void EndArray();
        void Write(string name, int v);
        void Write(string name, bool v);
        void Write(string name, string v);
        void WriteNull(string name);
    }

    /// <summary>Compact, name-free, little-endian varints. Names are ignored entirely.</summary>
    public sealed class BinaryStateWriter : IStateWriter
    {
        private readonly List<byte> _buf = new List<byte>(1024);

        public byte[] ToArray() { return _buf.ToArray(); }

        private void Tag(byte t) { _buf.Add(t); }

        private void VarInt(ulong v)
        {
            while (v >= 0x80) { _buf.Add((byte)(v | 0x80)); v >>= 7; }
            _buf.Add((byte)v);
        }

        // zig-zag so small negatives stay small (Support can be negative)
        private void ZigZag(int v) { VarInt((ulong)((v << 1) ^ (v >> 31))); }

        public void BeginObject(string name) { Tag(0x01); }
        public void EndObject() { Tag(0x02); }
        public void BeginArray(string name, int count) { Tag(0x03); VarInt((ulong)count); }
        public void EndArray() { Tag(0x04); }
        public void Write(string name, int v) { Tag(0x05); ZigZag(v); }
        public void Write(string name, bool v) { Tag(0x06); _buf.Add(v ? (byte)1 : (byte)0); }
        public void WriteNull(string name) { Tag(0x07); }

        public void Write(string name, string v)
        {
            if (v == null) { WriteNull(name); return; }
            Tag(0x08);
            var bytes = Encoding.UTF8.GetBytes(v);
            VarInt((ulong)bytes.Length);
            _buf.AddRange(bytes);
        }
    }

    /// <summary>Stable key order (traversal order), no whitespace. For eyeballing divergences.</summary>
    public sealed class CanonicalJsonStateWriter : IStateWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private bool _first = true;

        public override string ToString() { return _sb.ToString(); }

        private void Sep()
        {
            if (!_first) _sb.Append(',');
            _first = false;
        }

        private void Key(string name)
        {
            Sep();
            if (name != null) { _sb.Append('"').Append(name).Append("\":"); }
        }

        public void BeginObject(string name) { Key(name); _sb.Append('{'); _first = true; }
        public void EndObject() { _sb.Append('}'); _first = false; }
        public void BeginArray(string name, int count) { Key(name); _sb.Append('['); _first = true; }
        public void EndArray() { _sb.Append(']'); _first = false; }
        public void Write(string name, int v) { Key(name); _sb.Append(v); }
        public void Write(string name, bool v) { Key(name); _sb.Append(v ? "true" : "false"); }
        public void WriteNull(string name) { Key(name); _sb.Append("null"); }

        public void Write(string name, string v)
        {
            if (v == null) { WriteNull(name); return; }
            Key(name);
            _sb.Append('"');
            foreach (char ch in v)
            {
                if (ch == '"' || ch == '\\') _sb.Append('\\').Append(ch);
                else if (ch < 0x20) _sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else _sb.Append(ch);
            }
            _sb.Append('"');
        }
    }

    public static class StateCodec
    {
        public const int Version = GameState.SchemaVersion;

        public static void Write(GameState s, IStateWriter w)
        {
            w.BeginObject(null);
            w.Write("schema", Version);
            w.Write("flags", s.Options.FlagBits);
            w.Write("nextUid", s.NextUid);
            w.Write("rngState", unchecked((int)s.Random.State));
            w.Write("rngStateHi", unchecked((int)(s.Random.State >> 32)));
            w.Write("rngInc", unchecked((int)s.Random.Increment));
            w.Write("rngIncHi", unchecked((int)(s.Random.Increment >> 32)));
            w.Write("turn", (int)s.Turn);
            w.Write("turnNo", s.TurnNumber);
            w.Write("phase", (int)s.Phase);
            w.Write("isOver", s.IsOver);
            w.Write("outcome", (int)s.Outcome);

            WritePending(s.Pending, w);
            WriteCombat(s.Combat, w);

            // Board: fixed length, index-ordered. Empty cells are written explicitly so a shifted
            // board can never hash equal to an unshifted one.
            w.BeginArray("cells", Board.Cells);
            for (int i = 0; i < Board.Cells; i++)
            {
                var o = s.At(CellRef.FromIndex(i));
                if (o == null) w.WriteNull(null);
                else WriteObject(o, w);
            }
            w.EndArray();

            w.BeginArray("players", s.Players.Length);
            for (int i = 0; i < s.Players.Length; i++) WritePlayer(s.Players[i], w);
            w.EndArray();

            w.EndObject();
        }

        /// <summary>
        /// The suspended choice. The kind tag is written even when None so a state that parks a
        /// request can never hash equal to one that does not.
        /// </summary>
        static void WritePending(PendingRequest p, IStateWriter w)
        {
            w.Write("pending", (int)(p == null ? PendingKind.None : p.Kind));
            if (p == null) return;

            w.BeginObject("pendingReq");
            w.Write("responder", (int)p.Responder);

            var blocker = p as BlockerRequest;
            if (blocker != null)
            {
                w.Write("attacker", blocker.AttackerId);
                w.Write("declIndex", blocker.DeclarationIndex);
                w.Write("declCount", blocker.DeclarationCount);
                WriteUnitRefs("eligible", blocker.Eligible, w);
                w.EndObject();
                return;
            }

            var absorber = p as AbsorberRequest;
            if (absorber != null)
            {
                w.Write("attacker", absorber.AttackerId);
                WriteUnitRefs("blockers", absorber.Blockers, w);
                w.EndObject();
                return;
            }

            var retaliation = p as RetaliationRequest;
            if (retaliation != null)
            {
                w.Write("defender", retaliation.DefenderId);
                WriteUnitRefs("attackers", retaliation.Attackers, w);
                w.EndObject();
                return;
            }

            var window = p as ResponseWindowRequest;
            if (window != null)
            {
                w.Write("trigger", (int)window.Trigger);
                WriteUnitRefs("traps", window.ArmedTraps, w);
                w.EndObject();
                return;
            }

            w.EndObject();
        }

        /// <summary>Declarations and the resolver's cursor - authoritative, resumable state.</summary>
        static void WriteCombat(CombatState c, IStateWriter w)
        {
            w.BeginObject("combat");
            w.Write("stage", (int)c.Stage);
            w.Write("cursor", c.Cursor);
            w.Write("subCursor", c.SubCursor);
            w.Write("wallDmg", c.AccumulatedWallDamage);
            w.Write("hasAnswer", c.HasAnswer);
            w.Write("answered", c.AnsweredIndex);

            w.BeginArray("decls", c.Declarations.Count);
            for (int i = 0; i < c.Declarations.Count; i++)
            {
                var d = c.Declarations[i];
                w.BeginObject(null);
                w.Write("aRow", (int)d.Attacker.Row);
                w.Write("aCol", d.Attacker.Col);
                w.Write("aId", d.AttackerUnitId);
                w.Write("kind", (int)d.Kind);
                w.Write("tRow", (int)d.TargetCell.Row);
                w.Write("tCol", d.TargetCell.Col);
                w.Write("tId", d.TargetUnitId);
                w.Write("tSide", (int)d.TargetSide);
                w.Write("tZone", (int)d.TargetZone);
                WriteUnitRefs("blockers", d.Blockers.ToArray(), w);
                w.EndObject();
            }
            w.EndArray();

            WriteIntList("blocked", c.BlockedDeclIndices, w);
            WriteIntList("open", c.OpenDeclIndices, w);
            WriteIntList("groupTargets", c.GroupTargetIds, w);
            WriteIntList("groupOffsets", c.GroupOffsets, w);
            WriteIntList("groupDecls", c.GroupDeclIndices, w);
            WriteIntList("resAttackers", c.ResolutionAttackerIds, w);
            WriteIntList("scour", c.ScourHitUnitIds, w);

            w.EndObject();
        }

        static void WriteIntList(string name, System.Collections.Generic.List<int> list,
                                 IStateWriter w)
        {
            w.BeginArray(name, list.Count);
            for (int i = 0; i < list.Count; i++) w.Write(null, list[i]);
            w.EndArray();
        }

        static void WriteUnitRefs(string name, UnitRef[] refs, IStateWriter w)
        {
            w.BeginArray(name, refs.Length);
            for (int i = 0; i < refs.Length; i++)
            {
                var r = refs[i];
                w.BeginObject(null);
                w.Write("kind", (int)r.Kind);
                w.Write("unit", r.UnitId);
                if (r.IsCell)
                {
                    var c = r.AsCell;
                    w.Write("row", (int)c.Row);
                    w.Write("col", c.Col);
                }
                else if (r.IsPool)
                {
                    var pr = r.AsPool;
                    w.Write("owner", (int)pr.Owner);
                    w.Write("zone", (int)pr.Zone);
                    w.Write("idx", pr.Index);
                }
                w.EndObject();
            }
            w.EndArray();
        }

        static void WriteObject(BoardObject o, IStateWriter w)
        {
            w.BeginObject(null);
            w.Write("id", o.Id);
            w.Write("owner", (int)o.Owner);
            w.Write("kind", (int)o.Kind);
            w.Write("color", (int)o.Color);
            w.Write("bank", o.Bank);

            var c = o as CreatureUnit;
            if (c != null)
            {
                w.Write("card", c.Card.Value);
                w.Write("atk", c.Attack); w.Write("hp", c.Hp); w.Write("maxHp", c.MaxHp);
                w.Write("cost", c.Cost); w.Write("up", c.Upkeep);
                w.Write("fs", c.FirstStrike); w.Write("entrench", c.Entrench);
                w.Write("worker", c.IsWorker); w.Write("token", c.IsToken);
                w.Write("kw", (int)c.Keyword);
                w.Write("det", c.Detonate); w.Write("reap", c.Reap); w.Write("wardHp", c.WardHp);
                w.Write("grow", c.Grow); w.Write("hatch", c.Hatch); w.Write("into", c.Into.Value);
                w.Write("cnt", c.ChrysalisCount); w.Write("oc", c.OverchargeBank); w.Write("dis", c.DischargeBonus);
                w.Write("tribe", (int)c.Tribe); w.Write("sub", (int)c.Subtype);
                w.Write("sick", c.Sick); w.Write("tapped", c.Tapped);
                w.Write("moved", c.Moved); w.Write("moved2", c.MovedTwice);
                w.Write("paid", c.PaidUpkeep); w.Write("blocked", c.HasBlocked);
                w.EndObject();
                return;
            }

            var b = o as StructureUnit;
            if (b != null)
            {
                w.Write("def", b.DefId.Value);
                w.Write("hp", b.Hp); w.Write("maxHp", b.MaxHp); w.Write("cost", b.Cost);
                w.Write("val", b.Value); w.Write("sup", b.Support);
                w.Write("eff", (int)b.Effect); w.Write("cc", b.IsCommandCenter);
                w.EndObject();
                return;
            }

            var ch = o as ChargeUnit;
            if (ch != null)
            {
                w.Write("setIn", (int)ch.SetIn);
                w.Write("isStruct", ch.IsStructure);
                w.Write("inv", ch.Invested);
                w.Write("setTurn", ch.SetTurn);
                w.Write("snapId", ch.Card.Id.Value);
                w.Write("snapColor", (int)ch.Card.Color);
                w.Write("snapCost", ch.Card.Cost);
                w.Write("snapAtk", ch.Card.Attack);
                w.Write("snapHp", ch.Card.Health);
                w.Write("snapUp", ch.Card.Upkeep);
                w.Write("snapKw", (int)ch.Card.Keyword);
                w.Write("snapFs", ch.Card.FirstStrike);
                w.Write("snapEnt", ch.Card.Entrench);
                w.Write("snapDef", ch.Card.StructDef.Value);
                w.EndObject();
                return;
            }

            var t = o as TrapUnit;
            if (t != null)
            {
                w.Write("setIn", (int)t.SetIn);
                w.Write("card", t.Card.Value);
                w.Write("eff", (int)t.Effect);
                w.Write("val", t.Value);
                w.Write("trig", (int)t.Trigger);
                w.Write("setTurn", t.SetTurn);
                w.EndObject();
                return;
            }

            w.EndObject();
        }

        static void WritePlayer(PlayerState p, IStateWriter w)
        {
            w.BeginObject(null);
            w.Write("color", (int)p.PrimaryColor);
            w.Write("cc", p.Commander.Value);
            w.Write("life", p.Life);
            w.Write("mana", p.Mana);

            w.BeginArray("hand", p.Hand.Count);
            for (int i = 0; i < p.Hand.Count; i++)
            {
                w.BeginObject(null);
                w.Write("id", p.Hand[i].Id.Value);
                w.Write("color", (int)p.Hand[i].Color);
                w.EndObject();
            }
            w.EndArray();

            w.BeginArray("deck", p.Deck.Count);
            for (int i = 0; i < p.Deck.Count; i++)
            {
                w.BeginObject(null);
                w.Write("id", p.Deck[i].Id.Value);
                w.Write("color", (int)p.Deck[i].Color);
                w.EndObject();
            }
            w.EndArray();

            w.BeginArray("grave", p.Grave.Count);
            for (int i = 0; i < p.Grave.Count; i++)
            {
                w.BeginObject(null);
                w.Write("id", p.Grave[i].Id.Value);
                w.Write("name", p.Grave[i].Name);
                w.Write("color", (int)p.Grave[i].Color);
                w.Write("kind", (int)p.Grave[i].Kind);
                w.Write("token", p.Grave[i].IsToken);
                w.Write("worker", p.Grave[i].IsWorker);
                w.Write("turn", p.Grave[i].TurnDied);
                w.EndObject();
            }
            w.EndArray();

            w.BeginArray("workers", p.Workers.Length);
            for (int z = 0; z < p.Workers.Length; z++)
            {
                var pool = p.Workers[z];
                w.BeginArray(null, pool.Members.Count);
                for (int i = 0; i < pool.Members.Count; i++) WriteObject(pool.Members[i], w);
                w.EndArray();
            }
            w.EndArray();

            w.BeginArray("upkeepPaid", p.UpkeepPaid.Length);
            for (int i = 0; i < p.UpkeepPaid.Length; i++) w.Write(null, p.UpkeepPaid[i]);
            w.EndArray();

            w.EndObject();
        }

        public static byte[] ToBytes(GameState s)
        {
            var w = new BinaryStateWriter();
            Write(s, w);
            return w.ToArray();
        }

        public static string ToCanonicalJson(GameState s)
        {
            var w = new CanonicalJsonStateWriter();
            Write(s, w);
            return w.ToString();
        }

        /// <summary>
        /// FNV-1a 64 over the canonical bytes. One number that answers "are these two states the
        /// same", which is what the golden-scenario and differential suites are built on.
        /// </summary>
        public static ulong Hash(GameState s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong h = offset;
            var bytes = ToBytes(s);
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h = unchecked(h * prime);
            }
            return h;
        }
    }
}
