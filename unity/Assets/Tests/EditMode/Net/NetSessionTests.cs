using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.Rules.Tests;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The M17 gate.
    ///
    /// Netcode that is only tested by two people opening two windows is netcode whose failures
    /// are anecdotes. Everything below runs headless on a virtual clock against
    /// <see cref="LoopbackHub"/>, so a whole match - handshake, four hundred plies, a dropped
    /// peer, a reconnect, a deliberate desync - takes milliseconds and fails the same way twice.
    ///
    /// The command source is <see cref="FuzzPolicy"/>, not the scripted AI, and that is not a
    /// convenience: AiChoices rolls its face-down and structure decisions off the MATCH RNG
    /// (AiChoices.cs:89,99,104), so running the AI on one peer advances that peer's stream and
    /// not the other's. FuzzPolicy draws from its own Pcg32 and touches nothing. Human duels have
    /// no AI in them, so this is a testing constraint rather than a product one - but it is
    /// exactly why "put the AI on the other end of a network" is not free.
    /// </summary>
    public class NetSessionTests
    {
        // ---- harness ----------------------------------------------------------------------------

        sealed class Pair
        {
            public LoopbackHub Hub;
            public NetSession Host;
            public NetSession Guest;
            public LoopbackTransport HostTransport;
            public LoopbackTransport GuestTransport;

            public void Step(double dt)
            {
                Hub.Advance(dt);
                Host.Pump(dt);
                Guest.Pump(dt);
            }

            /// <summary>Run the clock until both ends are playing, or fail loudly.</summary>
            public void Connect(double limit = 30.0)
            {
                for (double t = 0; t < limit; t += 0.1)
                {
                    Step(0.1);
                    if (Host.Phase == SessionPhase.Playing && Guest.Phase == SessionPhase.Playing)
                        return;
                    if (Host.Phase == SessionPhase.Failed || Guest.Phase == SessionPhase.Failed)
                        break;
                }
                Assert.Fail("never paired - host " + Host.Phase + " (" + Host.Status + "), guest "
                            + Guest.Phase + " (" + Guest.Status + ")");
            }

            public NetSession Of(Side side)
            {
                return side == Side.You ? Host : Guest;
            }
        }

        static Pair Build(string password, ulong hostSeed = 1, ulong guestSeed = 2,
                          CommanderId? hostCmdr = null, CommanderId? guestCmdr = null,
                          int hostFlags = 0, int guestFlags = 0)
        {
            var cat = NetTestData.Catalog();
            var hub = new LoopbackHub();

            var hostChannel = PasswordChannel.Derive(password);
            var guestChannel = PasswordChannel.Derive(password);

            var ht = hub.Connect();
            var gt = hub.Connect();

            var host = new NetSession(NetRole.Host, hostChannel, cat, ht,
                                      new NetRandom(Utf8.Bytes("host" + hostSeed)));
            var guest = new NetSession(NetRole.Guest, guestChannel, cat, gt,
                                       new NetRandom(Utf8.Bytes("guest" + guestSeed)));

            host.Begin(hostCmdr ?? cat.Commanders[0].Id, null, "Host", hostFlags);
            guest.Begin(guestCmdr ?? cat.Commanders[1].Id, null, "Guest", guestFlags);

            return new Pair
            {
                Hub = hub, Host = host, Guest = guest,
                HostTransport = ht, GuestTransport = gt,
            };
        }

        /// <summary>
        /// Play a whole match through the protocol. Every ply: ask the fuzzer what is legal, hand
        /// it to the session of the side that owns it, run the clock until the other end has it,
        /// and assert the two engines still agree.
        /// </summary>
        static int PlayThrough(Pair pair, ulong fuzzSeed, int maxPlies, double stepSeconds = 0.1,
                               double settleSeconds = 4.0)
        {
            var policy = new FuzzPolicy(fuzzSeed);
            int plies = 0;

            for (; plies < maxPlies; plies++)
            {
                var driver = pair.Host.Engine;
                if (driver.State.IsOver) break;

                // The turn hand-off is not a decision, it is a rule: at End the only legal thing
                // is the INCOMING side beginning its turn, and its own peer is what issues it
                // (design 04 s4.3). Doing it here rather than letting the fuzzer offer it is also
                // what keeps the End phase a one-actor state - see LocalGate.
                ICommand cmd;
                if (driver.State.Pending == null && driver.State.Phase == TurnPhase.End)
                    cmd = new BeginTurnCommand(TurnMachine.Other(driver.State.Turn));
                else
                    cmd = policy.Next(driver);
                if (cmd == null) break;

                var session = pair.Of(cmd.Actor);
                var why = session.Submit(cmd);
                Assert.AreEqual(Rejection.None, why,
                                "ply " + plies + ": " + cmd.GetType().Name + " refused locally");

                // let it cross
                for (double t = 0; t < settleSeconds; t += stepSeconds)
                {
                    pair.Step(stepSeconds);
                    if (pair.Host.Ply == pair.Guest.Ply) break;
                }

                Assert.AreNotEqual(SessionPhase.Failed, pair.Host.Phase,
                                   "host failed at ply " + plies + ": " + pair.Host.Status);
                Assert.AreNotEqual(SessionPhase.Failed, pair.Guest.Phase,
                                   "guest failed at ply " + plies + ": " + pair.Guest.Status);

                Assert.AreEqual(pair.Host.Ply, pair.Guest.Ply, "ply counters at " + plies);
                Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash(),
                                "state diverged at ply " + plies + " after " + cmd.GetType().Name);

                pair.Host.Engine.DrainEvents();
                pair.Guest.Engine.DrainEvents();
            }
            return plies;
        }

        // ---- the handshake ----------------------------------------------------------------------

        [Test]
        public void TwoPeers_FindEachOther_AndBuildTheSameBoard()
        {
            var pair = Build("find each other");
            pair.Connect();

            Assert.AreEqual(SessionPhase.Playing, pair.Host.Phase);
            Assert.AreEqual(SessionPhase.Playing, pair.Guest.Phase);
            Assert.AreEqual(Side.You, pair.Host.LocalSide);
            Assert.AreEqual(Side.Foe, pair.Guest.LocalSide);

            Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash(),
                            "the opening boards must be identical, bit for bit");
            Assert.AreEqual(0, pair.Host.Ply);
            Assert.AreEqual(0, pair.Guest.Ply);
        }

        /// <summary>The guest may arrive long after the host started waiting - the host keeps
        /// advertising rather than sending one offer into an empty room.</summary>
        [Test]
        public void GuestArrivingLate_StillPairs()
        {
            var cat = NetTestData.Catalog();
            var hub = new LoopbackHub();
            var channel = PasswordChannel.Derive("late guest");

            var host = new NetSession(NetRole.Host, channel, cat, hub.Connect(),
                                      new NetRandom(Utf8.Bytes("h")));
            host.Begin(cat.Commanders[0].Id, null, "Host", 0);

            for (double t = 0; t < 20; t += 0.1) { hub.Advance(0.1); host.Pump(0.1); }
            Assert.AreEqual(SessionPhase.Advertising, host.Phase);

            var guest = new NetSession(NetRole.Guest, channel, cat, hub.Connect(),
                                       new NetRandom(Utf8.Bytes("g")));
            guest.Begin(cat.Commanders[2].Id, null, "Guest", 0);

            for (double t = 0; t < 30; t += 0.1)
            {
                hub.Advance(0.1); host.Pump(0.1); guest.Pump(0.1);
                if (host.Phase == SessionPhase.Playing && guest.Phase == SessionPhase.Playing) break;
            }

            Assert.AreEqual(SessionPhase.Playing, host.Phase, host.Status);
            Assert.AreEqual(SessionPhase.Playing, guest.Phase, guest.Status);
            Assert.AreEqual(host.Engine.Hash(), guest.Engine.Hash());
        }

        /// <summary>Neither peer alone decides the shuffle.</summary>
        [Test]
        public void Seed_DependsOnBothNonces()
        {
            var a = new byte[16];
            var b = new byte[16];
            for (int i = 0; i < 16; i++) { a[i] = (byte)i; b[i] = (byte)(i + 1); }

            Assert.AreNotEqual(NetSession.SeedFrom(a, b), NetSession.SeedFrom(b, a),
                               "order matters, so neither side can predict from its own half");
            Assert.AreEqual(NetSession.SeedFrom(a, b), NetSession.SeedFrom(a, b));
        }

        [Test]
        public void WrongPassword_NeverPairs()
        {
            var cat = NetTestData.Catalog();
            var hub = new LoopbackHub();

            var host = new NetSession(NetRole.Host, PasswordChannel.Derive("correct horse"), cat,
                                      hub.Connect(), new NetRandom(Utf8.Bytes("h")));
            var guest = new NetSession(NetRole.Guest, PasswordChannel.Derive("wrong horse"), cat,
                                       hub.Connect(), new NetRandom(Utf8.Bytes("g")));

            host.Begin(cat.Commanders[0].Id, null, "Host", 0);
            guest.Begin(cat.Commanders[1].Id, null, "Guest", 0);

            for (double t = 0; t < 30; t += 0.1) { hub.Advance(0.1); host.Pump(0.1); guest.Pump(0.1); }

            Assert.AreNotEqual(SessionPhase.Playing, host.Phase);
            Assert.AreNotEqual(SessionPhase.Playing, guest.Phase);
            Assert.IsNull(host.Engine, "no board is ever built on a failed pairing");
        }

        /// <summary>Different rules settings are a lobby refusal with a sentence, not a desync
        /// twenty turns later.</summary>
        [Test]
        public void MismatchedRulesFlags_AreRefusedInTheLobby()
        {
            var pair = Build("flag mismatch", hostFlags: 0, guestFlags: 1);

            for (double t = 0; t < 30; t += 0.1)
            {
                pair.Step(0.1);
                if (pair.Host.Phase == SessionPhase.Failed || pair.Guest.Phase == SessionPhase.Failed)
                    break;
            }

            Assert.IsTrue(pair.Host.Phase == SessionPhase.Failed
                          || pair.Guest.Phase == SessionPhase.Failed,
                          "one end must refuse");
            var failed = pair.Host.Phase == SessionPhase.Failed ? pair.Host : pair.Guest;
            Assert.AreEqual(RefuseCode.CatalogMismatch, failed.RefusedBecause);
            Assert.IsNotEmpty(failed.Status);
        }

        // ---- the whole point ---------------------------------------------------------------------

        [Test]
        public void AWholeMatch_StaysBitIdenticalOnBothPeers()
        {
            var pair = Build("a whole match");
            pair.Connect();

            int plies = PlayThrough(pair, 0xC0FFEE, 400);

            Assert.Greater(plies, 60, "the fuzzer should have played a real game, not two moves");
            Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash());
            Assert.AreEqual(pair.Host.Ply, pair.Guest.Ply);
        }

        /// <summary>Several seeds, because one match visits one corner of the rules.</summary>
        [Test]
        public void ManyMatches_StayBitIdentical()
        {
            ulong[] seeds = { 1, 7, 99, 4242, 31337 };
            for (int i = 0; i < seeds.Length; i++)
            {
                var pair = Build("match " + seeds[i]);
                pair.Connect();
                int plies = PlayThrough(pair, seeds[i], 220);
                Assert.Greater(plies, 20, "seed " + seeds[i] + " barely played");
                Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash(),
                                "seed " + seeds[i]);
            }
        }

        /// <summary>
        /// The relay is not a socket. It loses things, repeats things, and delivers them out of
        /// order - so the protocol is played across one that does all three at once.
        /// </summary>
        [Test]
        public void AWholeMatch_SurvivesLatencyDuplicationAndReordering()
        {
            var pair = Build("hostile relay");
            pair.Hub.Latency = 0.20;
            pair.Hub.Jitter = 0.60;             // jitter > step ⇒ genuine reordering
            pair.Hub.DuplicateChance = 0.25;
            pair.Connect(60.0);

            int plies = PlayThrough(pair, 0xBADBEEF, 200, 0.05, 12.0);

            Assert.Greater(plies, 40);
            Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash());
        }

        /// <summary>A duplicated frame must be applied once. If it were applied twice the ply
        /// counters would run away and the hashes would part.</summary>
        [Test]
        public void DuplicateFrames_AreAppliedOnce()
        {
            var pair = Build("duplicates");
            pair.Hub.DuplicateChance = 1.0;      // every single message arrives twice
            pair.Connect();

            int plies = PlayThrough(pair, 5150, 60);

            Assert.Greater(plies, 10);
            Assert.AreEqual(plies, pair.Host.Ply);
            Assert.AreEqual(plies, pair.Guest.Ply);
        }

        // ---- when it goes wrong --------------------------------------------------------------------

        /// <summary>
        /// Poison one engine behind the protocol's back and the very next frame must be caught.
        /// This is the property that makes lockstep safe to ship: a divergence cannot be silent,
        /// and it names the ply.
        /// </summary>
        [Test]
        public void ADivergedEngine_IsCaughtAtTheNextFrame_NotLater()
        {
            var pair = Build("desync");
            pair.Connect();

            // A few honest plies first, so the failure is not an artefact of the opening.
            var policy = new FuzzPolicy(77);
            for (int i = 0; i < 6; i++)
            {
                var cmd = policy.Next(pair.Host.Engine);
                if (cmd == null) break;
                pair.Of(cmd.Actor).Submit(cmd);
                for (double t = 0; t < 4; t += 0.1)
                {
                    pair.Step(0.1);
                    if (pair.Host.Ply == pair.Guest.Ply) break;
                }
            }
            Assert.AreEqual(SessionPhase.Playing, pair.Guest.Phase);

            // Whoever is about to RECEIVE the next frame is the one to poison, so the test does
            // not depend on which side the fuzz seed happens to move next.
            var next = policy.Next(pair.Host.Engine);
            Assert.IsNotNull(next);
            var sender = pair.Of(next.Actor);
            var receiver = next.Actor == Side.You ? pair.Guest : pair.Host;

            receiver.Engine.State.P(Side.You).Life -= 1;
            Assert.AreNotEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash());

            sender.Submit(next);

            for (double t = 0; t < 6; t += 0.1)
            {
                pair.Step(0.1);
                if (receiver.Phase == SessionPhase.Failed) break;
            }

            Assert.AreEqual(SessionPhase.Failed, receiver.Phase, "a desync must not be silent");
            Assert.AreEqual(RefuseCode.Desync, receiver.RefusedBecause);
            Assert.IsNotNull(receiver.Desync);
            Assert.IsNotEmpty(receiver.Desync.ToString());
            Assert.AreEqual(next.GetType().Name, receiver.Desync.Command, "and it names the ply");
        }

        /// <summary>
        /// A peer may only ever move its own side. This is the one check that survives a peer
        /// lying about everything else, so it is checked directly rather than trusted to the
        /// rules - which would let a foe-turn command through on a foe turn.
        /// </summary>
        [Test]
        public void APeerCannotSubmitForTheOtherSide()
        {
            var pair = Build("wrong side");
            pair.Connect();

            var why = pair.Guest.Submit(new HarvestCommand(Side.You));
            Assert.AreEqual(Rejection.NotYourTurn, why);
            Assert.AreEqual(0, pair.Guest.Ply, "and nothing was published");
            Assert.AreEqual(SessionPhase.Playing, pair.Host.Phase);
        }

        [Test]
        public void ByeEndsTheMatchCleanly()
        {
            var pair = Build("goodbye");
            pair.Connect();

            pair.Host.Leave("Host left the duel.");
            for (double t = 0; t < 5; t += 0.1) pair.Step(0.1);

            Assert.AreEqual(SessionPhase.Ended, pair.Guest.Phase);
            Assert.AreEqual("Host left the duel.", pair.Guest.Status);
        }

        /// <summary>Silence is reported, not acted on: a friend who put their phone down has not
        /// forfeited, and the JS build's timer budgets are exactly the complexity this avoids.</summary>
        [Test]
        public void ASilentPeer_IsReported_ButTheMatchSurvives()
        {
            var pair = Build("silence");
            pair.Connect();

            pair.GuestTransport.Offline = true;
            for (double t = 0; t < 45; t += 0.5) pair.Step(0.5);

            Assert.IsTrue(pair.Host.PeerSilent, "silence should be noticed");
            Assert.AreEqual(SessionPhase.Playing, pair.Host.Phase, "but not acted on");

            pair.GuestTransport.Offline = false;
            for (double t = 0; t < 20; t += 0.5) pair.Step(0.5);
            Assert.IsFalse(pair.Host.PeerSilent, "and it clears when they come back");
        }

        // ---- reconnect ------------------------------------------------------------------------------

        /// <summary>
        /// A browser reload mid-match. The replacement peer knows only the password; the peer
        /// that never stopped playing hands it the agreed setup and every command since, and it
        /// replays into the same ply and the same hash.
        ///
        /// Nothing here depends on the relay remembering anything, which is what lets the same
        /// code work over a live broker that retains nothing at all.
        /// </summary>
        [Test]
        public void AGuestThatReloads_GetsTheMatchBackFromTheHost()
        {
            var pair = Build("reconnect");
            pair.Connect();

            var policy = new FuzzPolicy(2468);
            for (int i = 0; i < 25; i++)
            {
                var cmd = policy.Next(pair.Host.Engine);
                if (cmd == null) break;
                pair.Of(cmd.Actor).Submit(cmd);
                for (double t = 0; t < 4; t += 0.1)
                {
                    pair.Step(0.1);
                    if (pair.Host.Ply == pair.Guest.Ply) break;
                }
            }

            int plyBefore = pair.Guest.Ply;
            ulong hashBefore = pair.Guest.Engine.Hash();
            Assert.Greater(plyBefore, 5, "there should be a real game to rejoin");

            // The guest's process goes away. A brand-new session, same password, same role.
            pair.GuestTransport.Offline = true;
            var replacement = new NetSession(NetRole.Guest, PasswordChannel.Derive("reconnect"),
                                             NetTestData.Catalog(), pair.Hub.Connect(),
                                             new NetRandom(Utf8.Bytes("g2")));
            replacement.Begin(NetTestData.Catalog().Commanders[1].Id, null, "Guest", 0);

            for (double t = 0; t < 20; t += 0.1)
            {
                pair.Hub.Advance(0.1);
                pair.Host.Pump(0.1);
                replacement.Pump(0.1);
                if (replacement.Phase == SessionPhase.Playing && !replacement.CatchingUp) break;
            }

            Assert.AreEqual(SessionPhase.Playing, replacement.Phase, replacement.Status);
            Assert.IsFalse(replacement.CatchingUp, "the whole log should have been replayed");
            Assert.AreEqual(plyBefore, replacement.Ply, "rejoined at the ply it left");
            Assert.AreEqual(hashBefore, replacement.Engine.Hash(), "and at the same board");
            Assert.AreEqual(pair.Host.Engine.Hash(), replacement.Engine.Hash());

            // and play continues
            pair.Guest = replacement;
            int more = PlayThrough(pair, 1357, 30);
            Assert.Greater(more, 3, "the rejoined peer can still play");
            Assert.AreEqual(pair.Host.Engine.Hash(), replacement.Engine.Hash());
        }

        /// <summary>
        /// The other direction: the HOST reloads. It comes back knowing only the password and
        /// with no board at all, and the guest - who never stopped playing - hands the whole
        /// match back. Hello and Join are the same message in opposite directions precisely so
        /// that this needs no extra protocol.
        /// </summary>
        [Test]
        public void AHostThatReloads_GetsTheMatchBackFromTheGuest()
        {
            var pair = Build("host reload");
            pair.Connect();

            var policy = new FuzzPolicy(8080);
            for (int i = 0; i < 20; i++)
            {
                var cmd = policy.Next(pair.Host.Engine);
                if (cmd == null) break;
                pair.Of(cmd.Actor).Submit(cmd);
                for (double t = 0; t < 4; t += 0.1)
                {
                    pair.Step(0.1);
                    if (pair.Host.Ply == pair.Guest.Ply) break;
                }
            }

            int plyBefore = pair.Guest.Ply;
            ulong hashBefore = pair.Guest.Engine.Hash();
            Assert.Greater(plyBefore, 4);

            pair.HostTransport.Offline = true;
            var cat = NetTestData.Catalog();
            var replacement = new NetSession(NetRole.Host, PasswordChannel.Derive("host reload"),
                                             cat, pair.Hub.Connect(),
                                             new NetRandom(Utf8.Bytes("h2")));
            replacement.Begin(cat.Commanders[0].Id, null, "Host", 0);

            for (double t = 0; t < 25; t += 0.1)
            {
                pair.Hub.Advance(0.1);
                pair.Guest.Pump(0.1);
                replacement.Pump(0.1);
                if (replacement.Phase == SessionPhase.Playing && !replacement.CatchingUp) break;
            }

            Assert.AreEqual(SessionPhase.Playing, replacement.Phase, replacement.Status);
            Assert.AreEqual(plyBefore, replacement.Ply, "rejoined at the ply it left");
            Assert.AreEqual(hashBefore, replacement.Engine.Hash());
            Assert.AreEqual(Side.You, replacement.LocalSide, "and still in the host's seat");
        }

        /// <summary>
        /// A peer that missed frames while its link was down catches up from the other peer's
        /// log, not from any relay's memory - the Ping carries how far along each end is, and the
        /// one that is ahead re-sends the difference.
        /// </summary>
        [Test]
        public void APeerThatMissedFrames_IsCaughtUpByThePing()
        {
            var pair = Build("catch up");
            pair.Connect();

            var policy = new FuzzPolicy(1212);

            // The guest's link drops. The host keeps playing into the void.
            pair.GuestTransport.Offline = true;
            int sent = 0;
            for (int i = 0; i < 40 && sent < 6; i++)
            {
                var cmd = policy.Next(pair.Host.Engine);
                if (cmd == null) break;
                if (cmd.Actor != Side.You) break;            // only the host can act while alone
                Assert.AreEqual(Rejection.None, pair.Host.Submit(cmd));
                sent++;
                for (double t = 0; t < 1; t += 0.1) pair.Step(0.1);
            }

            Assert.Greater(sent, 2);
            Assert.AreEqual(0, pair.Guest.Ply, "the guest heard none of it");

            pair.GuestTransport.Offline = false;
            for (double t = 0; t < 40; t += 0.5)
            {
                pair.Step(0.5);
                if (pair.Guest.Ply == pair.Host.Ply) break;
            }

            Assert.AreEqual(pair.Host.Ply, pair.Guest.Ply, "caught up");
            Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash());
        }

        /// <summary>
        /// The one place the rules do NOT serialise authority.
        ///
        /// SendBankedMana carries no phase gate, so at Phase == End the outgoing side can still
        /// legally move a bank at the same moment the incoming side is told to begin its turn.
        /// Two legal commands, from two peers, on one state, that do not commute - the exact
        /// shape that breaks optimistic lockstep. NetSession.LocalGate closes it without touching
        /// the rules; this is the test that says so.
        /// </summary>
        [Test]
        public void AtTheTurnHandover_OnlyTheIncomingSideMayAct()
        {
            var pair = Build("end phase");
            pair.Connect();

            // Walk the host's opening turn to its End phase through the real commands.
            Assert.AreEqual(Rejection.None, pair.Host.Submit(new HarvestCommand(Side.You)));
            Assert.AreEqual(Rejection.None, pair.Host.Submit(new DrawForTurnCommand(Side.You)));
            Assert.AreEqual(Rejection.None, pair.Host.Submit(new EndTurnCommand(Side.You)));
            Assert.AreEqual(TurnPhase.End, pair.Host.Engine.State.Phase);

            // The engine itself would still allow this - that is the hole.
            var send = new SendBankedManaCommand(Side.You, new CellRef(RowKey.YouBack, 0),
                                                 new CellRef(RowKey.YouBack, 1));
            Assert.AreEqual(Rejection.WrongPhase, pair.Host.LocalGate(send),
                            "the netcode gate refuses everything but the hand-off at End");
            Assert.AreEqual(Rejection.WrongPhase, pair.Host.Submit(send));

            // ...and the hand-off itself is still allowed, from the incoming side only.
            for (double t = 0; t < 4; t += 0.1) pair.Step(0.1);
            Assert.AreEqual(Rejection.NotYourTurn,
                            pair.Host.LocalGate(new BeginTurnCommand(Side.Foe)),
                            "the outgoing peer does not begin its opponent's turn");
            Assert.AreEqual(Rejection.None, pair.Guest.Submit(new BeginTurnCommand(Side.Foe)));

            for (double t = 0; t < 4; t += 0.1) pair.Step(0.1);
            Assert.AreEqual(pair.Host.Engine.Hash(), pair.Guest.Engine.Hash());
            Assert.AreEqual(Side.Foe, pair.Host.Engine.State.Turn);
        }

        // ---- what it costs -----------------------------------------------------------------------

        /// <summary>
        /// Design 04 s1 claims a frame is about ten bytes and that this is what makes the whole
        /// approach affordable on somebody else's free relay. Numbers in a design document rot;
        /// this one is measured.
        /// </summary>
        [Test]
        public void AWholeMatchCostsVeryLittleWire()
        {
            var pair = Build("wire budget");
            pair.Connect();

            int handshakeBytes = pair.Hub.TotalBytes;
            int handshakeMessages = pair.Hub.TotalMessages;

            int plies = PlayThrough(pair, 909, 200);

            int matchBytes = pair.Hub.TotalBytes - handshakeBytes;
            int matchMessages = pair.Hub.TotalMessages - handshakeMessages;

            UnityEngine.Debug.Log("[net] " + plies + " plies: " + matchMessages + " messages, "
                                  + matchBytes + " bytes (handshake " + handshakeBytes + ")");

            Assert.Less(matchBytes / System.Math.Max(1, plies), 400,
                        "average wire bytes per ply, base64 and AEAD overhead included");
            Assert.Less(pair.Hub.TotalBytes, 300000, "a whole match should be well under a megabyte");
        }
    }
}
