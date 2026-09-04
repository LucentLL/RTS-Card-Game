using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The wire is the netcode's only contract with itself. Every command must survive a round
    /// trip byte-for-byte, and every malformed frame must fail as a WireFormatException rather
    /// than as an index-out-of-range in a handler - the bytes arrive from a PUBLIC relay topic
    /// that anyone may publish to.
    /// </summary>
    public class WireCodecTests
    {
        static ICardCatalog Catalog() { return NetTestData.Catalog(); }

        static void AssertSame(ICommand a, ICommand b)
        {
            Assert.AreEqual(a.GetType(), b.GetType(), "command type");
            Assert.AreEqual(Describe(a), Describe(b));
        }

        /// <summary>Structural equality by way of a canonical string - cheaper and more legible
        /// in a failure message than field-by-field asserts across sixteen command types.</summary>
        static string Describe(ICommand c)
        {
            var w = new ByteWriter();
            CommandCodec.Write(w, c);
            var b = w.ToArray();
            var sb = new System.Text.StringBuilder(c.GetType().Name).Append(':');
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        static IEnumerable<ICommand> EveryCommand()
        {
            var cell = new CellRef(RowKey.YouFront, 2);
            var other = new CellRef(RowKey.Center, 3);

            yield return new BeginTurnCommand(Side.Foe);
            yield return new HarvestCommand(Side.You);
            yield return new DrawForTurnCommand(Side.You);
            yield return new EndTurnCommand(Side.Foe);
            yield return new UpkeepPayCommand(Side.You, cell, 41);
            yield return new UpkeepSacrificeCommand(Side.Foe, cell, 7);
            yield return new MoveUnitCommand(Side.You, cell, other, 12);
            yield return new PlayCardCommand(Side.You, 3, PlayMode.Summon, cell);
            yield return new PlayCardCommand(Side.Foe, 0, PlayMode.SetTrap, other);
            yield return new PlayCardCommand(Side.You, 2, PlayMode.Cast, other);
            yield return new BuildStructureCommand(Side.You, new StructId("forge"), Element.Electric, cell);
            yield return new UpgradeStructureCommand(Side.Foe, cell, 9, new StructId("grandforge"));
            yield return new PourIntoChargeCommand(Side.You, cell, 5, 3);
            yield return new FlipChargeCommand(Side.You, cell, 5);
            yield return new SendBankedManaCommand(Side.Foe, cell, other);
            yield return new ResolveCombatCommand(Side.You);

            yield return new DeclareAttackCommand(Side.You, cell, 3,
                new UnitTarget(new CellRef(RowKey.FoeFront, 6), 88));
            yield return new DeclareAttackCommand(Side.You, cell, 3, new WallTarget(Side.Foe), true);
            yield return new DeclareAttackCommand(Side.Foe, other, 4,
                new WorkerStackTarget(Side.You, WorkerZone.Center));

            yield return new RespondCommand(Side.Foe, new BlockersChosen(new[]
            {
                UnitRef.Cell(cell, 11),
                UnitRef.Pool(new PoolRef(Side.You, WorkerZone.Back, 2), 12),
                UnitRef.None,
            }));
            yield return new RespondCommand(Side.You, new BlockersChosen(new UnitRef[0]));
            yield return new RespondCommand(Side.You, new IndexChosen(2));
            yield return new RespondCommand(Side.You, new IndexChosen(-1));
            yield return new RespondCommand(Side.Foe, TrapChosen.Passed);
            yield return new RespondCommand(Side.Foe, new TrapChosen(UnitRef.Cell(other, 21)));
        }

        [Test]
        public void EveryCommandKind_RoundTrips()
        {
            int n = 0;
            foreach (var cmd in EveryCommand())
            {
                var bytes = CommandCodec.Encode(cmd);
                var back = CommandCodec.Decode(bytes);
                AssertSame(cmd, back);
                Assert.AreEqual(cmd.Actor, back.Actor, "actor survives");
                n++;
            }
            Assert.GreaterOrEqual(n, 24, "the table should cover every command type");
        }

        /// <summary>The affordability claim in design 04 s1 is a number, so it is a test. If a
        /// command ever grows past this, the relay's per-message allowance is the thing that
        /// notices, in production, on someone's phone.</summary>
        [Test]
        public void CommandsStaySmall()
        {
            foreach (var cmd in EveryCommand())
            {
                int size = CommandCodec.Encode(cmd).Length;
                Assert.LessOrEqual(size, 40, cmd.GetType().Name + " encodes to " + size + " bytes");
            }

            Assert.LessOrEqual(CommandCodec.Encode(
                new PlayCardCommand(Side.You, 3, PlayMode.Summon, new CellRef(RowKey.YouFront, 2))).Length,
                8, "the commonest command of all");
        }

        [Test]
        public void TruncatedFrame_ThrowsWireFormat()
        {
            var bytes = CommandCodec.Encode(
                new MoveUnitCommand(Side.You, new CellRef(RowKey.YouBack, 1),
                                    new CellRef(RowKey.YouFront, 1), 4));

            for (int cut = 0; cut < bytes.Length; cut++)
            {
                var shorter = new byte[cut];
                System.Buffer.BlockCopy(bytes, 0, shorter, 0, cut);
                Assert.Throws<WireFormatException>(delegate { CommandCodec.Decode(shorter); },
                                                   "truncation at " + cut + " must be caught");
            }
        }

        [Test]
        public void OutOfRangeEnums_ThrowWireFormat()
        {
            Assert.Throws<WireFormatException>(
                delegate { CommandCodec.Decode(new byte[] { 200, 0 }); }, "unknown command tag");

            Assert.Throws<WireFormatException>(
                delegate { CommandCodec.Decode(new byte[] { 1, 9 }); }, "actor out of range");

            // Harvest(actor=You) then a bad row on a cell-bearing command
            Assert.Throws<WireFormatException>(
                delegate { CommandCodec.Decode(new byte[] { 5, 0, 9, 0, 0 }); }, "row out of range");

            Assert.Throws<WireFormatException>(
                delegate { CommandCodec.Decode(new byte[] { 5, 0, 0, 99, 0 }); }, "column out of range");
        }

        /// <summary>A hostile length prefix must not make us allocate an enormous array.</summary>
        [Test]
        public void AbsurdBlockerCount_ThrowsWireFormat()
        {
            var w = new ByteWriter();
            w.Byte(16);          // Respond
            w.Byte(0);           // actor
            w.Byte(1);           // BlockersChosen
            w.VarInt(4000000);
            Assert.Throws<WireFormatException>(delegate { CommandCodec.Decode(w.ToArray()); });
        }

        // ---- envelopes -------------------------------------------------------------------------

        static NetMessage Frame(int ply, ICommand cmd)
        {
            var m = new NetMessage();
            m.Kind = NetMessageKind.Frame;
            m.SessionId = 0xABCDEF0123456789UL;
            m.Ply = ply;
            m.HashBefore = 0x0123456789ABCDEFUL;
            m.Command = cmd;
            return m;
        }

        [Test]
        public void SealedFrame_OpensWithTheSamePassword()
        {
            var cat = Catalog();
            var registry = new CardRegistry(cat);
            var channel = PasswordChannel.Derive("sealed frame");
            var random = new NetRandom(Utf8.Bytes("t"));

            var sent = Frame(7, new HarvestCommand(Side.You));
            var text = NetEnvelope.Seal(channel, NetRole.Host, sent, registry, random);

            byte version;
            var got = NetEnvelope.Open(channel, NetRole.Host, text, registry, out version);

            Assert.IsNotNull(got);
            Assert.AreEqual(NetProtocol.Version, version);
            Assert.AreEqual(sent.SessionId, got.SessionId);
            Assert.AreEqual(sent.Ply, got.Ply);
            Assert.AreEqual(sent.HashBefore, got.HashBefore);
            AssertSame(sent.Command, got.Command);
        }

        [Test]
        public void SealedFrame_DoesNotOpenWithAnotherPassword()
        {
            var registry = new CardRegistry(Catalog());
            var mine = PasswordChannel.Derive("one password");
            var theirs = PasswordChannel.Derive("a different one");

            var text = NetEnvelope.Seal(mine, NetRole.Host, Frame(1, new EndTurnCommand(Side.You)),
                                        registry, new NetRandom(Utf8.Bytes("t")));

            byte version;
            Assert.IsNull(NetEnvelope.Open(theirs, NetRole.Host, text, registry, out version));
        }

        /// <summary>
        /// The role is associated data, so a frame lifted off the host's topic and re-published
        /// on the guest's cannot authenticate. Without this, an eavesdropper could bounce a
        /// player's own commands back at them attributed to their opponent.
        /// </summary>
        [Test]
        public void SealedFrame_DoesNotOpenAsTheOtherRole()
        {
            var registry = new CardRegistry(Catalog());
            var channel = PasswordChannel.Derive("role binding");

            var text = NetEnvelope.Seal(channel, NetRole.Host, Frame(1, new EndTurnCommand(Side.You)),
                                        registry, new NetRandom(Utf8.Bytes("t")));

            byte version;
            Assert.IsNull(NetEnvelope.Open(channel, NetRole.Guest, text, registry, out version));
            Assert.IsNotNull(NetEnvelope.Open(channel, NetRole.Host, text, registry, out version));
        }

        [Test]
        public void GarbageOnThePublicTopic_IsNull_NotAnException()
        {
            var registry = new CardRegistry(Catalog());
            var channel = PasswordChannel.Derive("noise");
            byte version;

            Assert.IsNull(NetEnvelope.Open(channel, NetRole.Host, "", registry, out version));
            Assert.IsNull(NetEnvelope.Open(channel, NetRole.Host, "hello, ntfy", registry, out version));
            Assert.IsNull(NetEnvelope.Open(channel, NetRole.Host, "!!!!!!!!", registry, out version));
            Assert.IsNull(NetEnvelope.Open(channel, NetRole.Host,
                                           Base64Url.Encode(new byte[64]), registry, out version));
        }

        [Test]
        public void HandshakeMessages_RoundTrip()
        {
            var cat = Catalog();
            var registry = new CardRegistry(cat);
            var channel = PasswordChannel.Derive("handshake");
            var random = new NetRandom(Utf8.Bytes("t"));

            var deck = DeckFactory.DeckOf(cat, new[] { Element.Fire }, new Pcg32(9UL));

            var hello = new NetMessage();
            hello.Kind = NetMessageKind.Hello;
            hello.SessionId = 12345UL;
            hello.Nonce = random.Bytes(NetMessage.HandshakeNonceBytes);
            hello.Commander = cat.Commanders[3].Id;
            hello.Deck = deck;
            hello.FlagBits = 0;
            hello.CatalogFingerprint = registry.Fingerprint;
            hello.DisplayName = "Mara";

            byte version;
            var text = NetEnvelope.Seal(channel, NetRole.Host, hello, registry, random);
            var got = NetEnvelope.Open(channel, NetRole.Host, text, registry, out version);

            Assert.IsNotNull(got);
            Assert.AreEqual(NetMessageKind.Hello, got.Kind);
            Assert.AreEqual(hello.SessionId, got.SessionId);
            Assert.AreEqual(hello.Commander, got.Commander);
            Assert.AreEqual("Mara", got.DisplayName);
            Assert.AreEqual(registry.Fingerprint, got.CatalogFingerprint);
            Assert.AreEqual(deck.Count, got.Deck.Count);
            for (int i = 0; i < deck.Count; i++)
            {
                Assert.AreEqual(deck[i].Id, got.Deck[i].Id, "deck card " + i);
                Assert.AreEqual(deck[i].Color, got.Deck[i].Color, "deck colour " + i);
            }
        }

        /// <summary>A whole handshake carrying two 40-card decks has to fit inside the relay's
        /// per-message allowance, with room to spare. This is the number that decided decks are
        /// <summary>
        /// The host does not always open.
        ///
        /// Side.You moved first and the host IS Side.You, so whoever pressed Host got the first
        /// turn of every game they ever played. The flip comes off the shared seed - SHA-256 over
        /// both peers' nonces, so neither side chose it - and both engines derive it rather than
        /// sending it, which is why it cannot disagree.
        /// </summary>
        [Test]
        public void FirstMove_IsACoinFlip_NeitherPeerTosses()
        {
            int you = 0, foe = 0;
            for (ulong seed = 1; seed <= 400; seed++)
            {
                if (MatchConfig.FirstMoveFrom(seed) == Side.You) you++; else foe++;
                Assert.AreEqual(MatchConfig.FirstMoveFrom(seed), MatchConfig.FirstMoveFrom(seed),
                    "and both peers derive the same answer from the same seed");
            }

            Assert.Greater(you, 120, "the flip is not stuck on the host");
            Assert.Greater(foe, 120, "nor on the guest");
        }

        /// <summary>And the built match actually starts on that side - the flip is not advice.</summary>
        [Test]
        public void FirstMove_ReachesTheOpeningState()
        {
            var cat = Catalog();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var config = new MatchConfig();
                config.HostCommander = cat.Commanders[0].Id;
                config.GuestCommander = cat.Commanders[1].Id;
                config.Seed = seed;
                config.CatalogFingerprint = new CardRegistry(cat).Fingerprint;

                Assert.AreEqual(MatchConfig.FirstMoveFrom(seed), config.Build(cat).Turn,
                    "seed " + seed + " opened on the wrong side");
            }
        }

        /// sent as registry indices rather than as "colour|name" strings.</summary>
        [Test]
        public void StartMessage_FitsComfortablyInOneRelayMessage()
        {
            var cat = Catalog();
            var registry = new CardRegistry(cat);
            var channel = PasswordChannel.Derive("size");
            var random = new NetRandom(Utf8.Bytes("t"));

            var config = new MatchConfig();
            config.HostCommander = cat.Commanders[0].Id;
            config.GuestCommander = cat.Commanders[1].Id;
            config.HostDeck = DeckFactory.DeckOf(cat, new[] { Element.Fire }, new Pcg32(1UL));
            config.GuestDeck = DeckFactory.DeckOf(cat, new[] { Element.Water }, new Pcg32(2UL));
            config.Seed = 99UL;
            config.CatalogFingerprint = registry.Fingerprint;

            var start = new NetMessage();
            start.Kind = NetMessageKind.Start;
            start.SessionId = 7UL;
            start.Config = config;
            start.OpeningHash = 1234UL;

            var text = NetEnvelope.Seal(channel, NetRole.Host, start, registry, random);
            Assert.Less(text.Length, 2000,
                        "a Start with two full decks is " + text.Length + " characters");

            byte version;
            var got = NetEnvelope.Open(channel, NetRole.Host, text, registry, out version);
            Assert.IsNotNull(got);
            Assert.AreEqual(config.Seed, got.Config.Seed);
            Assert.AreEqual(config.HostCommander, got.Config.HostCommander);
            Assert.AreEqual(40, got.Config.GuestDeck.Count);
        }

        /// <summary>Two peers with different card data must be told so, in the lobby, rather than
        /// desyncing later. The fingerprint is what tells them.</summary>
        [Test]
        public void CatalogFingerprint_IsStableAndSensitive()
        {
            var a = new CardRegistry(Catalog());
            var b = new CardRegistry(Catalog());
            Assert.AreEqual(a.Fingerprint, b.Fingerprint, "same catalog, same fingerprint");
            Assert.AreNotEqual(0UL, a.Fingerprint);
        }

        [Test]
        public void UnknownRulesFlags_AreRefused_NotIgnored()
        {
            RulesOptions options;
            Assert.IsTrue(RulesOptions.TryFromFlagBits(0, out options));
            Assert.AreEqual(0, options.FlagBits);

            Assert.IsTrue(RulesOptions.TryFromFlagBits(RulesOptions.KnownFlagMask, out options));
            Assert.AreEqual(RulesOptions.KnownFlagMask, options.FlagBits, "every flag round-trips");

            Assert.IsFalse(RulesOptions.TryFromFlagBits(1 << 20, out options),
                           "a flag this build has never heard of is a refusal");
        }
    }
}
