using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// ICommand to bytes and back.
    ///
    /// This is the whole wire payload of a match. The command set is closed and small
    /// (Commands.cs), a summon is nine bytes, and that is what makes the lockstep model
    /// affordable over a relay we do not own - the snapshot model the JS shipped moved 25-40 KB
    /// for the same event.
    ///
    /// Every read is bounds-checked, because these bytes arrive from a PUBLIC relay topic.
    /// Authentication happens first (a frame that fails its Poly1305 tag never reaches here), so
    /// a malformed frame means a version skew or a bug, not an attack - but it still must fail as
    /// a clean WireFormatException rather than an index-out-of-range somewhere downstream.
    ///
    /// Tags are permanent. Adding a command means adding a tag; changing one means bumping
    /// NetProtocol.Version.
    /// </summary>
    public static class CommandCodec
    {
        enum Tag : byte
        {
            BeginTurn = 1, Harvest = 2, DrawForTurn = 3, EndTurn = 4,
            UpkeepPay = 5, UpkeepSacrifice = 6, MoveUnit = 7,
            PlayCard = 8, BuildStructure = 9, UpgradeStructure = 10,
            PourIntoCharge = 11, FlipCharge = 12, SendBankedMana = 13,
            DeclareAttack = 14, ResolveCombat = 15, Respond = 16,
            WithdrawAttack = 17,
            Max = 18,
        }

        enum TargetTag : byte { Unit = 1, Wall = 2, WorkerStack = 3, Max = 4 }

        enum ResponseTag : byte { Blockers = 1, Index = 2, Trap = 3, Max = 4 }

        // ---- write --------------------------------------------------------------------------

        public static byte[] Encode(ICommand cmd)
        {
            var w = new ByteWriter(16);
            Write(w, cmd);
            return w.ToArray();
        }

        public static void Write(ByteWriter w, ICommand cmd)
        {
            if (cmd == null) throw new WireFormatException("null command");

            var begin = cmd as BeginTurnCommand;
            if (begin != null) { Head(w, Tag.BeginTurn, cmd); return; }

            var harvest = cmd as HarvestCommand;
            if (harvest != null) { Head(w, Tag.Harvest, cmd); return; }

            var draw = cmd as DrawForTurnCommand;
            if (draw != null) { Head(w, Tag.DrawForTurn, cmd); return; }

            var end = cmd as EndTurnCommand;
            if (end != null) { Head(w, Tag.EndTurn, cmd); return; }

            var pay = cmd as UpkeepPayCommand;
            if (pay != null)
            {
                Head(w, Tag.UpkeepPay, cmd); Cell(w, pay.Target); w.Int(pay.UnitId); return;
            }

            var sac = cmd as UpkeepSacrificeCommand;
            if (sac != null)
            {
                Head(w, Tag.UpkeepSacrifice, cmd); Cell(w, sac.Target); w.Int(sac.UnitId); return;
            }

            var move = cmd as MoveUnitCommand;
            if (move != null)
            {
                Head(w, Tag.MoveUnit, cmd);
                Cell(w, move.From); Cell(w, move.To); w.Int(move.UnitId);
                return;
            }

            var play = cmd as PlayCardCommand;
            if (play != null)
            {
                Head(w, Tag.PlayCard, cmd);
                w.Int(play.HandIndex); w.Byte((byte)play.Mode); Cell(w, play.To);
                return;
            }

            var build = cmd as BuildStructureCommand;
            if (build != null)
            {
                Head(w, Tag.BuildStructure, cmd);
                w.String(build.Def.Value); w.Byte((byte)build.Color); Cell(w, build.To);
                return;
            }

            var upgrade = cmd as UpgradeStructureCommand;
            if (upgrade != null)
            {
                Head(w, Tag.UpgradeStructure, cmd);
                Cell(w, upgrade.At); w.Int(upgrade.UnitId); w.String(upgrade.Target.Value);
                return;
            }

            var pour = cmd as PourIntoChargeCommand;
            if (pour != null)
            {
                Head(w, Tag.PourIntoCharge, cmd);
                Cell(w, pour.At); w.Int(pour.UnitId); w.Int(pour.Amount);
                return;
            }

            var flip = cmd as FlipChargeCommand;
            if (flip != null)
            {
                Head(w, Tag.FlipCharge, cmd); Cell(w, flip.At); w.Int(flip.UnitId); return;
            }

            var send = cmd as SendBankedManaCommand;
            if (send != null)
            {
                Head(w, Tag.SendBankedMana, cmd); Cell(w, send.From); Cell(w, send.To); return;
            }

            var declare = cmd as DeclareAttackCommand;
            if (declare != null)
            {
                Head(w, Tag.DeclareAttack, cmd);
                Cell(w, declare.Attacker); w.Int(declare.UnitId);
                WriteTarget(w, declare.Target);
                w.Bool(declare.DeferBlockers);
                return;
            }

            var resolve = cmd as ResolveCombatCommand;
            if (resolve != null) { Head(w, Tag.ResolveCombat, cmd); return; }

            var withdraw = cmd as WithdrawAttackCommand;
            if (withdraw != null) { Head(w, Tag.WithdrawAttack, cmd); return; }

            var respond = cmd as RespondCommand;
            if (respond != null)
            {
                Head(w, Tag.Respond, cmd);
                WriteResponse(w, respond.Response);
                return;
            }

            throw new WireFormatException("unencodable command " + cmd.GetType().Name);
        }

        static void Head(ByteWriter w, Tag tag, ICommand cmd)
        {
            w.Byte((byte)tag);
            w.Byte((byte)cmd.Actor);
        }

        static void Cell(ByteWriter w, CellRef c)
        {
            w.Byte((byte)c.Row);
            w.Byte(c.Col);
        }

        static void WriteUnitRef(ByteWriter w, UnitRef r)
        {
            w.Byte((byte)r.Kind);
            w.Int(r.UnitId);
            if (r.IsCell) { Cell(w, r.AsCell); }
            else if (r.IsPool)
            {
                var p = r.AsPool;
                w.Byte((byte)p.Owner); w.Byte((byte)p.Zone); w.Int(p.Index);
            }
        }

        static void WriteTarget(ByteWriter w, AttackTarget t)
        {
            var unit = t as UnitTarget;
            if (unit != null)
            {
                w.Byte((byte)TargetTag.Unit); Cell(w, unit.Cell); w.Int(unit.UnitId); return;
            }

            var wall = t as WallTarget;
            if (wall != null)
            {
                w.Byte((byte)TargetTag.Wall); w.Byte((byte)wall.Defender); return;
            }

            var stack = t as WorkerStackTarget;
            if (stack != null)
            {
                w.Byte((byte)TargetTag.WorkerStack);
                w.Byte((byte)stack.Owner); w.Byte((byte)stack.Zone);
                return;
            }

            throw new WireFormatException("unencodable attack target");
        }

        static void WriteResponse(ByteWriter w, ChoiceResponse r)
        {
            var blockers = r as BlockersChosen;
            if (blockers != null)
            {
                w.Byte((byte)ResponseTag.Blockers);
                w.VarInt((ulong)blockers.Blockers.Length);
                for (int i = 0; i < blockers.Blockers.Length; i++) WriteUnitRef(w, blockers.Blockers[i]);
                return;
            }

            var index = r as IndexChosen;
            if (index != null)
            {
                w.Byte((byte)ResponseTag.Index); w.Int(index.Index); return;
            }

            var trap = r as TrapChosen;
            if (trap != null)
            {
                w.Byte((byte)ResponseTag.Trap);
                w.Bool(trap.Pass);
                if (!trap.Pass) WriteUnitRef(w, trap.Trap);
                return;
            }

            throw new WireFormatException("unencodable choice response");
        }

        // ---- read ---------------------------------------------------------------------------

        public static ICommand Decode(byte[] bytes)
        {
            return Read(new ByteReader(bytes));
        }

        public static ICommand Read(ByteReader r)
        {
            var tag = (Tag)r.Enum((int)Tag.Max, "command tag");
            var actor = (Side)r.Enum(2, "actor");

            switch (tag)
            {
                case Tag.BeginTurn: return new BeginTurnCommand(actor);
                case Tag.Harvest: return new HarvestCommand(actor);
                case Tag.DrawForTurn: return new DrawForTurnCommand(actor);
                case Tag.EndTurn: return new EndTurnCommand(actor);
                case Tag.ResolveCombat: return new ResolveCombatCommand(actor);
                case Tag.WithdrawAttack: return new WithdrawAttackCommand(actor);

                case Tag.UpkeepPay:
                {
                    var cell = ReadCell(r);
                    return new UpkeepPayCommand(actor, cell, r.Int());
                }
                case Tag.UpkeepSacrifice:
                {
                    var cell = ReadCell(r);
                    return new UpkeepSacrificeCommand(actor, cell, r.Int());
                }
                case Tag.MoveUnit:
                {
                    var from = ReadCell(r);
                    var to = ReadCell(r);
                    return new MoveUnitCommand(actor, from, to, r.Int());
                }
                case Tag.PlayCard:
                {
                    int hand = r.Int();
                    var mode = (PlayMode)r.Enum(5, "play mode");
                    return new PlayCardCommand(actor, hand, mode, ReadCell(r));
                }
                case Tag.BuildStructure:
                {
                    var def = new StructId(r.String());
                    var color = (Element)r.Enum(ElementCount, "element");
                    return new BuildStructureCommand(actor, def, color, ReadCell(r));
                }
                case Tag.UpgradeStructure:
                {
                    var at = ReadCell(r);
                    int id = r.Int();
                    return new UpgradeStructureCommand(actor, at, id, new StructId(r.String()));
                }
                case Tag.PourIntoCharge:
                {
                    var at = ReadCell(r);
                    int id = r.Int();
                    return new PourIntoChargeCommand(actor, at, id, r.Int());
                }
                case Tag.FlipCharge:
                {
                    var at = ReadCell(r);
                    return new FlipChargeCommand(actor, at, r.Int());
                }
                case Tag.SendBankedMana:
                {
                    var from = ReadCell(r);
                    return new SendBankedManaCommand(actor, from, ReadCell(r));
                }
                case Tag.DeclareAttack:
                {
                    var attacker = ReadCell(r);
                    int id = r.Int();
                    var target = ReadTarget(r);
                    bool defer = r.Bool();
                    return new DeclareAttackCommand(actor, attacker, id, target, defer);
                }
                case Tag.Respond:
                    return new RespondCommand(actor, ReadResponse(r));
            }

            throw new WireFormatException("unhandled command tag " + tag);
        }

        /// <summary>Element has no Count member; this is the guard for reading one off the wire.</summary>
        const int ElementCount = 10;   // None, Fire, Water, Earth, Wind, Forest, Electric, Light, Dark, Divine

        static CellRef ReadCell(ByteReader r)
        {
            var row = (RowKey)r.Enum(Rules.Board.Rows, "row");
            byte col = r.Byte();
            if (col >= Rules.Board.Columns) throw new WireFormatException("bad column " + col);
            return new CellRef(row, col);
        }

        static UnitRef ReadUnitRef(ByteReader r)
        {
            var kind = (UnitRefKind)r.Enum(3, "unit ref kind");
            int id = r.Int();
            switch (kind)
            {
                case UnitRefKind.Cell: return UnitRef.Cell(ReadCell(r), id);
                case UnitRefKind.Pool:
                {
                    var owner = (Side)r.Enum(2, "pool owner");
                    var zone = (WorkerZone)r.Enum(4, "pool zone");
                    int index = r.Int();
                    if (index < 0 || index > byte.MaxValue)
                        throw new WireFormatException("pool index out of range: " + index);
                    return UnitRef.Pool(new PoolRef(owner, zone, (byte)index), id);
                }
                default: return UnitRef.None;
            }
        }

        static AttackTarget ReadTarget(ByteReader r)
        {
            var tag = (TargetTag)r.Enum((int)TargetTag.Max, "target tag");
            switch (tag)
            {
                case TargetTag.Unit:
                {
                    var cell = ReadCell(r);
                    return new UnitTarget(cell, r.Int());
                }
                case TargetTag.Wall:
                    return new WallTarget((Side)r.Enum(2, "wall defender"));
                case TargetTag.WorkerStack:
                {
                    var owner = (Side)r.Enum(2, "stack owner");
                    var zone = (WorkerZone)r.Enum(4, "stack zone");
                    return new WorkerStackTarget(owner, zone);
                }
            }
            throw new WireFormatException("unhandled target tag " + tag);
        }

        /// <summary>A hard cap on a length-prefixed array read from untrusted bytes. Nothing in
        /// the rules can produce more blockers than there are cells on the board.</summary>
        const int MaxRefs = Rules.Board.Cells;

        static ChoiceResponse ReadResponse(ByteReader r)
        {
            var tag = (ResponseTag)r.Enum((int)ResponseTag.Max, "response tag");
            switch (tag)
            {
                case ResponseTag.Blockers:
                {
                    ulong n = r.VarInt();
                    if (n > MaxRefs) throw new WireFormatException("absurd blocker count " + n);
                    var refs = new UnitRef[(int)n];
                    for (int i = 0; i < refs.Length; i++) refs[i] = ReadUnitRef(r);
                    return new BlockersChosen(refs);
                }
                case ResponseTag.Index:
                    return new IndexChosen(r.Int());
                case ResponseTag.Trap:
                {
                    bool pass = r.Bool();
                    if (pass) return TrapChosen.Passed;
                    return new TrapChosen(ReadUnitRef(r));
                }
            }
            throw new WireFormatException("unhandled response tag " + tag);
        }
    }
}
