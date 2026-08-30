using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.Rules.Tests;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The same stack, against the real public brokers, over the real internet.
    ///
    /// [Explicit] on purpose: the default gate must stay hermetic and fast, and a test that fails
    /// because somebody else's free broker is having a bad afternoon is a test that trains people
    /// to ignore red. Run it deliberately when the transport changes, or when a link misbehaves
    /// and you need to know whether the brokers are the problem:
    ///
    ///     bash tools/run-unity-tests.sh LiveRelayTests
    ///
    /// It is the only test here that can tell you the relays are down.
    /// </summary>
    [Explicit("talks to public MQTT brokers over the internet")]
    public class LiveRelayTests
    {
        static RelayTransport Live(string seed)
        {
            return new RelayTransport(new PlatformWebSocketFactory(),
                                      new NetRandom(Utf8.Bytes(seed)));
        }

        /// <summary>Real seconds. The editor's EditMode runner has no player loop, so the pump
        /// is driven by the wall clock here - the only place in the netcode that does.</summary>
        static void Run(double seconds, params IMessageTransport[] transports)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double last = 0;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                double now = sw.Elapsed.TotalSeconds;
                for (int i = 0; i < transports.Length; i++) transports[i].Pump(now - last);
                last = now;
                System.Threading.Thread.Sleep(20);
            }
        }

        [Test]
        public void AtLeastOnePublicBrokerIsReachable()
        {
            using (var t = Live("probe"))
            {
                t.Subscribe("srd2-livecheck");
                Run(15.0, t);

                UnityEngine.Debug.Log("[net] live relays: " + t.Description
                                      + (t.LastError == null ? "" : " (last error: " + t.LastError + ")"));
                Assert.AreEqual(TransportStatus.Connected, t.Status,
                                "no public broker answered - " + t.LastError);
            }
        }

        [Test]
        public void TwoPeers_RoundTripAMessageOverTheRealInternet()
        {
            string topic = "srd2-live-" + Base32.Encode(new NetRandom().Bytes(8));
            string payload = "srd-live-" + Base32.Encode(new NetRandom().Bytes(8));

            using (var a = Live("a"))
            using (var b = Live("b"))
            {
                b.Subscribe(topic);
                Run(12.0, a, b);
                Assert.AreEqual(TransportStatus.Connected, b.Status, b.LastError);

                a.Publish(topic, payload);

                string got = null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double last = 0;
                while (sw.Elapsed.TotalSeconds < 15 && got == null)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    a.Pump(now - last); b.Pump(now - last);
                    last = now;

                    var inbox = b.Poll();
                    for (int i = 0; i < inbox.Count; i++)
                        if (inbox[i].Text == payload) got = inbox[i].Text;

                    System.Threading.Thread.Sleep(20);
                }

                UnityEngine.Debug.Log("[net] live round trip via " + a.Description);
                Assert.AreEqual(payload, got, "the message never came back");
            }
        }

        /// <summary>
        /// A whole handshake and a few plies between two sessions that have never met, over the
        /// public internet, on a password. This is the thing the feature actually is.
        /// </summary>
        [Test]
        public void AMatch_StartsAndPlaysOverTheRealInternet()
        {
            var cat = NetTestData.Catalog();
            string password = "srd live " + Base32.Encode(new NetRandom().Bytes(8));

            using (var hostT = Live("h"))
            using (var guestT = Live("g"))
            {
                var host = new NetSession(NetRole.Host, PasswordChannel.Derive(password), cat,
                                          hostT, new NetRandom(Utf8.Bytes("h")));
                var guest = new NetSession(NetRole.Guest, PasswordChannel.Derive(password), cat,
                                           guestT, new NetRandom(Utf8.Bytes("g")));

                host.Begin(cat.Commanders[0].Id, null, "Host", 0);
                guest.Begin(cat.Commanders[3].Id, null, "Guest", 0);

                Assert.IsTrue(Wait(host, guest, 40, delegate
                {
                    return host.Phase == SessionPhase.Playing && guest.Phase == SessionPhase.Playing;
                }), "never paired - host: " + host.Status + " / guest: " + guest.Status);

                Assert.AreEqual(host.Engine.Hash(), guest.Engine.Hash(), "same opening board");

                var policy = new FuzzPolicy(1234);
                for (int i = 0; i < 12; i++)
                {
                    var driver = host.Engine;
                    ICommand cmd;
                    if (driver.State.Pending == null && driver.State.Phase == TurnPhase.End)
                        cmd = new BeginTurnCommand(TurnMachine.Other(driver.State.Turn));
                    else
                        cmd = policy.Next(driver);
                    if (cmd == null) break;

                    var side = cmd.Actor == Side.You ? host : guest;
                    Assert.AreEqual(Rejection.None, side.Submit(cmd));

                    Assert.IsTrue(Wait(host, guest, 15, delegate { return host.Ply == guest.Ply; }),
                                  "ply " + i + " never crossed");
                    Assert.AreEqual(host.Engine.Hash(), guest.Engine.Hash(), "ply " + i);
                    host.Engine.DrainEvents();
                    guest.Engine.DrainEvents();
                }

                UnityEngine.Debug.Log("[net] live match: " + host.Ply + " plies via "
                                      + hostT.Description);
                Assert.Greater(host.Ply, 6);
            }
        }

        static bool Wait(NetSession a, NetSession b, double seconds, System.Func<bool> until)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double last = 0;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                double now = sw.Elapsed.TotalSeconds;
                a.Pump(now - last);
                b.Pump(now - last);
                last = now;
                if (until()) return true;
                if (a.Phase == SessionPhase.Failed || b.Phase == SessionPhase.Failed) return false;
                System.Threading.Thread.Sleep(20);
            }
            return false;
        }
    }
}
