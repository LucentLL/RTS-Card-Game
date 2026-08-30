using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.Rules.Tests;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The MQTT wire, byte for byte, and then the whole shipped transport driven against an
    /// in-memory broker that speaks it.
    ///
    /// LoopbackTransport proves the protocol in the abstract. These prove the thing that will
    /// actually carry it on a player's machine.
    /// </summary>
    public class RelayTransportTests
    {
        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        // ---- the wire ------------------------------------------------------------------------

        [Test]
        public void Connect_MatchesTheSpecifiedBytes()
        {
            // 0x10 CONNECT | len 13 | "MQTT" | level 4 | clean session | keepalive 45 | id "c"
            Assert.AreEqual("100d00044d5154540402002d000163",
                            Hex(Mqtt.EncodeConnect("c", 45)));
        }

        [Test]
        public void Subscribe_Publish_Ping_And_Disconnect_MatchTheSpecifiedBytes()
        {
            // 0x82 SUBSCRIBE | len 8 | id 1 | "a/b" | QoS 0
            Assert.AreEqual("820800010003612f6200", Hex(Mqtt.EncodeSubscribe(1, "a/b")));

            Assert.AreEqual("30050001746869", Hex(Mqtt.EncodePublish("t", "hi")));
            Assert.AreEqual("c000", Hex(Mqtt.EncodePingReq()));
            Assert.AreEqual("e000", Hex(Mqtt.EncodeDisconnect()));
        }

        /// <summary>A payload over 127 bytes needs a two-byte remaining length, and that encoding
        /// is the classic place a hand-written MQTT goes wrong.</summary>
        [Test]
        public void LongPayload_UsesMultiByteRemainingLength()
        {
            var payload = new string('x', 300);
            var frame = Mqtt.EncodePublish("t", payload);

            Assert.AreEqual(0x30, frame[0]);
            Assert.AreEqual(0xAF, frame[1], "303 % 128 = 47, with the continuation bit set");
            Assert.AreEqual(0x02, frame[2], "303 / 128");

            var packets = new List<Mqtt.Packet>();
            Assert.AreEqual(frame.Length, Mqtt.Decode(frame, frame.Length, packets));
            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(payload, packets[0].Payload);
        }

        [Test]
        public void Decode_HandlesSeveralPacketsInOneBuffer()
        {
            var buf = new List<byte>();
            buf.AddRange(Mqtt.EncodePublish("a", "one"));
            buf.AddRange(Mqtt.EncodePingReq());
            buf.AddRange(Mqtt.EncodePublish("b", "two"));

            var packets = new List<Mqtt.Packet>();
            var bytes = buf.ToArray();
            Assert.AreEqual(bytes.Length, Mqtt.Decode(bytes, bytes.Length, packets));

            Assert.AreEqual(3, packets.Count);
            Assert.AreEqual("one", packets[0].Payload);
            Assert.AreEqual(Mqtt.PingReq, packets[1].Type);
            Assert.AreEqual("b", packets[2].Topic);
        }

        /// <summary>A WebSocket message boundary is not a packet boundary. A packet split down
        /// the middle must be held, not misread.</summary>
        [Test]
        public void Decode_LeavesAPartialPacketAlone()
        {
            var whole = Mqtt.EncodePublish("topic", "a payload of some length");

            for (int cut = 1; cut < whole.Length; cut++)
            {
                var packets = new List<Mqtt.Packet>();
                int consumed = Mqtt.Decode(whole, cut, packets);
                Assert.AreEqual(0, consumed, "nothing consumed at cut " + cut);
                Assert.AreEqual(0, packets.Count, "nothing decoded at cut " + cut);
            }

            var all = new List<Mqtt.Packet>();
            Assert.AreEqual(whole.Length, Mqtt.Decode(whole, whole.Length, all));
            Assert.AreEqual(1, all.Count);
        }

        // ---- the transport --------------------------------------------------------------------

        const string One = "wss://one/mqtt";
        const string Two = "wss://two/mqtt";

        static RelayTransport Transport(FakeNetwork net, string seed, params string[] urls)
        {
            return new RelayTransport(net, new NetRandom(Utf8.Bytes(seed)),
                                      urls.Length > 0 ? urls : new[] { One });
        }

        static void Run(double seconds, params IMessageTransport[] transports)
        {
            for (double t = 0; t < seconds; t += 0.1)
                for (int i = 0; i < transports.Length; i++) transports[i].Pump(0.1);
        }

        [Test]
        public void TwoTransports_ExchangeMessagesThroughABroker()
        {
            var net = new FakeNetwork();
            net.Add(One);
            var a = Transport(net, "a");
            var b = Transport(net, "b");

            a.Subscribe("room-b");
            b.Subscribe("room-a");
            Run(1.0, a, b);

            Assert.AreEqual(TransportStatus.Connected, a.Status);

            a.Publish("room-a", "hello from a");
            Run(0.5, a, b);

            var got = b.Poll();
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual("room-a", got[0].Topic);
            Assert.AreEqual("hello from a", got[0].Text);
            Assert.AreEqual(0, a.Poll().Count, "and a does not hear its own publish");
        }

        /// <summary>Publishing before the broker has answered CONNECT must not lose the message -
        /// the very first thing a lobby sends is the thing that matters most.</summary>
        [Test]
        public void MessagesPublishedBeforeConnack_AreQueuedNotDropped()
        {
            var net = new FakeNetwork();
            net.Add(One);
            var a = Transport(net, "a");
            var b = Transport(net, "b");

            b.Subscribe("room-a");
            Run(0.5, b);

            a.Publish("room-a", "sent before the handshake finished");   // a has no client yet
            Run(1.0, a, b);

            var got = b.Poll();
            Assert.AreEqual(1, got.Count, "the early publish should still arrive");
            Assert.AreEqual("sent before the handshake finished", got[0].Text);
        }

        [Test]
        public void ADeadBroker_IsSkipped_AndALiveOneStillCarriesTheMatch()
        {
            var net = new FakeNetwork();
            net.Add(One).Down = true;
            net.Add(Two);

            var a = Transport(net, "a", One, Two);
            var b = Transport(net, "b", One, Two);

            a.Subscribe("t");
            b.Subscribe("t");
            Run(2.0, a, b);

            Assert.AreEqual(TransportStatus.Connected, a.Status, "one live broker is enough");

            b.Publish("t", "still works");
            Run(0.5, a, b);
            Assert.AreEqual(1, a.Poll().Count);
        }

        /// <summary>The same publish arriving over two brokers is one event, not two.</summary>
        [Test]
        public void FanOutAcrossBrokers_IsDeduplicated()
        {
            var net = new FakeNetwork();
            var one = net.Add(One);
            var two = net.Add(Two);

            var a = Transport(net, "a", One, Two);
            var b = Transport(net, "b", One, Two);

            a.Subscribe("t");                     // only a listens, so the count is unambiguous
            Run(2.0, a, b);

            b.Publish("t", "one message, two paths");
            Run(0.5, a, b);

            Assert.AreEqual(2, one.Delivered + two.Delivered, "it really did travel twice");
            Assert.AreEqual(1, a.Poll().Count, "and arrived once");
        }

        /// <summary>A broker that accepts the socket and then says nothing must not hold the
        /// transport hostage.</summary>
        [Test]
        public void ABrokerThatNeverAnswers_TimesOutAndIsRetried()
        {
            var net = new FakeNetwork();
            var mute = net.Add(One);
            mute.Mute = true;
            var a = Transport(net, "a");
            a.Subscribe("t");

            Run(RelayTransport.ConnectTimeout + 1.0, a);
            Assert.AreNotEqual(TransportStatus.Connected, a.Status);

            mute.Mute = false;
            Run(RelayTransport.RetryDelay + 2.0, a);
            Assert.AreEqual(TransportStatus.Connected, a.Status, "and it comes back when it can");
        }

        [Test]
        public void ADroppedConnection_Reconnects()
        {
            var net = new FakeNetwork();
            var broker = net.Add(One);
            var a = Transport(net, "a");
            var b = Transport(net, "b");
            a.Subscribe("t");
            b.Subscribe("t");
            Run(1.0, a, b);
            Assert.AreEqual(TransportStatus.Connected, a.Status);

            broker.DropEveryone();
            Run(0.5, a, b);

            Run(RelayTransport.RetryDelay + 2.0, a, b);
            Assert.AreEqual(TransportStatus.Connected, a.Status, "reconnected");

            a.Publish("t", "after the drop");
            Run(0.5, a, b);
            Assert.AreEqual(1, b.Poll().Count);
        }

        // ---- the whole stack --------------------------------------------------------------------

        /// <summary>
        /// The real thing: two NetSessions, over RelayTransport, over MqttClient, over the MQTT
        /// wire, playing a whole match. Every layer that will run on a player's machine except
        /// the socket itself.
        /// </summary>
        [Test]
        public void AWholeMatch_OverTheRealTransportStack()
        {
            var net = new FakeNetwork();
            var broker = net.Add(One);
            var cat = NetTestData.Catalog();
            var channel = PasswordChannel.Derive("over mqtt");

            var host = new NetSession(NetRole.Host, channel, cat, Transport(net, "h"),
                                      new NetRandom(Utf8.Bytes("h")));
            var guest = new NetSession(NetRole.Guest, channel, cat, Transport(net, "g"),
                                       new NetRandom(Utf8.Bytes("g")));

            host.Begin(cat.Commanders[0].Id, null, "Host", 0);
            guest.Begin(cat.Commanders[3].Id, null, "Guest", 0);

            for (double t = 0; t < 30; t += 0.1)
            {
                host.Pump(0.1);
                guest.Pump(0.1);
                if (host.Phase == SessionPhase.Playing && guest.Phase == SessionPhase.Playing) break;
            }

            Assert.AreEqual(SessionPhase.Playing, host.Phase, host.Status);
            Assert.AreEqual(SessionPhase.Playing, guest.Phase, guest.Status);
            Assert.AreEqual(host.Engine.Hash(), guest.Engine.Hash());

            var policy = new FuzzPolicy(4711);
            int plies = 0;
            for (; plies < 120; plies++)
            {
                var driver = host.Engine;
                if (driver.State.IsOver) break;

                ICommand cmd;
                if (driver.State.Pending == null && driver.State.Phase == TurnPhase.End)
                    cmd = new BeginTurnCommand(TurnMachine.Other(driver.State.Turn));
                else
                    cmd = policy.Next(driver);
                if (cmd == null) break;

                var side = cmd.Actor == Side.You ? host : guest;
                Assert.AreEqual(Rejection.None, side.Submit(cmd), "ply " + plies);

                for (double t = 0; t < 3; t += 0.1)
                {
                    host.Pump(0.1);
                    guest.Pump(0.1);
                    if (host.Ply == guest.Ply) break;
                }

                Assert.AreEqual(host.Ply, guest.Ply, "ply " + plies);
                Assert.AreEqual(host.Engine.Hash(), guest.Engine.Hash(), "ply " + plies);
                host.Engine.DrainEvents();
                guest.Engine.DrainEvents();
            }

            Assert.Greater(plies, 40);
            UnityEngine.Debug.Log("[net] over MQTT: " + plies + " plies, "
                                  + broker.Published + " broker publishes");
        }

    }
}
