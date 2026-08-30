using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// The shipped transport: several public MQTT brokers at once, spoken to over WebSocket.
    ///
    /// **Why a broker and not HTTP polling.** The first draft of this used ntfy.sh: publish with
    /// POST, receive by polling a cursor. It is one code path on every platform and it was very
    /// nearly right. It is also unaffordable. ntfy's free tier allows a 60-request burst
    /// replenished at one request per five seconds, and 250 published messages a day; a 350 ms
    /// poll is 2.9 requests a second, so the burst is gone in about twenty seconds and the daily
    /// publish quota inside half an hour. Measured, not theorised: while this was being built,
    /// ntfy.sh stopped answering this machine entirely after a few dozen probe requests, and had
    /// not come back an hour later. A relay we can be cut off from by playing the game is not a
    /// transport. A broker connection costs one socket and no per-message budget at all.
    ///
    /// **Why several.** The same afternoon proved the other half: a single free relay is a single
    /// point of failure that nobody is obliged to keep up for us. So every message goes to every
    /// broker we have a live connection to, and we read all of them. Two peers meet as long as
    /// ONE broker is reachable by both, which needs no negotiation and no fallback logic, and the
    /// duplicate copies cost nothing - the protocol above already had to tolerate duplicates, and
    /// identical sealed text is deduplicated here anyway.
    ///
    /// Public brokers are unauthenticated and anyone may publish to any topic, which is exactly
    /// why every byte is sealed: an unauthenticated frame never reaches the decoder.
    /// </summary>
    public sealed class RelayTransport : IMessageTransport
    {
        /// <summary>
        /// Public MQTT-over-WebSocket brokers, all verified reachable on 2026-08-30. They are
        /// test/demo endpoints run as a courtesy - which is precisely why there are three.
        /// </summary>
        public static readonly string[] DefaultBrokers =
        {
            "wss://broker.emqx.io:8084/mqtt",
            "wss://broker.hivemq.com:8884/mqtt",
            "wss://test.mosquitto.org:8081/mqtt",
        };

        /// <summary>A broker that has not reached CONNACK by now is not going to.</summary>
        public const double ConnectTimeout = 12.0;
        public const double RetryDelay = 6.0;

        readonly IWebSocketFactory _sockets;
        readonly string[] _urls;
        readonly string _clientId;

        readonly MqttClient[] _clients;
        readonly double[] _retryAt;
        readonly List<string> _topics = new List<string>();
        readonly List<InboundMessage> _inbox = new List<InboundMessage>();

        // Multi-broker fan-out means the same sealed text arrives up to three times. Every frame
        // carries a random nonce, so identical text can only be the same publish echoed back.
        readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        readonly Queue<string> _seenOrder = new Queue<string>();

        double _clock;

        public RelayTransport(IWebSocketFactory sockets, NetRandom random)
            : this(sockets, random, DefaultBrokers)
        {
        }

        public RelayTransport(IWebSocketFactory sockets, NetRandom random, string[] brokers)
        {
            if (sockets == null) throw new ArgumentNullException("sockets");
            _sockets = sockets;
            _urls = brokers;
            _clientId = "srd-" + Base32.Encode((random ?? new NetRandom()).Bytes(10));
            _clients = new MqttClient[_urls.Length];
            _retryAt = new double[_urls.Length];
        }

        public string Description
        {
            get
            {
                var live = new List<string>();
                for (int i = 0; i < _clients.Length; i++)
                    if (_clients[i] != null && _clients[i].Ready) live.Add(Host(_urls[i]));
                return live.Count == 0 ? "no relay" : string.Join(", ", live.ToArray());
            }
        }

        static string Host(string url)
        {
            int a = url.IndexOf("://", StringComparison.Ordinal);
            if (a < 0) return url;
            int b = url.IndexOf(':', a + 3);
            int c = url.IndexOf('/', a + 3);
            int end = b < 0 ? c : (c < 0 ? b : Math.Min(b, c));
            return end < 0 ? url.Substring(a + 3) : url.Substring(a + 3, end - a - 3);
        }

        public TransportStatus Status
        {
            get
            {
                bool anyReady = false, anyTrying = false;
                for (int i = 0; i < _clients.Length; i++)
                {
                    if (_clients[i] == null) { anyTrying = true; continue; }
                    if (_clients[i].Ready) anyReady = true;
                    else if (_clients[i].State != SocketState.Failed) anyTrying = true;
                }
                if (anyReady) return TransportStatus.Connected;
                return anyTrying ? TransportStatus.Connecting : TransportStatus.Retrying;
            }
        }

        public string LastError { get; private set; }

        public void Subscribe(string topic)
        {
            if (_topics.Contains(topic)) return;
            _topics.Add(topic);
            for (int i = 0; i < _clients.Length; i++)
                if (_clients[i] != null) _clients[i].Subscribe(topic);
        }

        public void Unsubscribe(string topic)
        {
            _topics.Remove(topic);       // MQTT UNSUBSCRIBE omitted: connections are short-lived
        }

        public void Publish(string topic, string text)
        {
            // Every broker we have, so the pair meets if ANY one of them works for both.
            bool anywhere = false;
            for (int i = 0; i < _clients.Length; i++)
                if (_clients[i] != null) { _clients[i].Publish(topic, text); anywhere = true; }

            // No client exists yet - the first Pump has not run. Hold it rather than drop it:
            // the first thing a lobby says is the thing that must not be lost.
            if (!anywhere && _outboxTopics.Count < 64)
            {
                _outboxTopics.Add(topic);
                _outboxTexts.Add(text);
            }
        }

        readonly List<string> _outboxTopics = new List<string>();
        readonly List<string> _outboxTexts = new List<string>();

        public void Pump(double deltaSeconds)
        {
            _clock += deltaSeconds;

            for (int i = 0; i < _clients.Length; i++)
            {
                var c = _clients[i];

                if (c == null)
                {
                    if (_clock < _retryAt[i]) continue;
                    c = new MqttClient(_sockets.Create(), _urls[i], _clientId);
                    _clients[i] = c;
                    for (int t = 0; t < _topics.Count; t++) c.Subscribe(_topics[t]);
                    for (int o = 0; o < _outboxTopics.Count; o++)
                        c.Publish(_outboxTopics[o], _outboxTexts[o]);
                    c.Start();
                    continue;
                }

                c.Pump(deltaSeconds, _inbox);

                bool dead = c.State == SocketState.Failed
                            || (!c.Ready && c.ConnectingFor > ConnectTimeout);
                if (dead)
                {
                    if (c.LastError != null) LastError = Host(_urls[i]) + ": " + c.LastError;
                    c.Dispose();
                    _clients[i] = null;
                    _retryAt[i] = _clock + RetryDelay;
                }
            }

            // Handed over as soon as anything exists to hand it to. Not kept: a broker that
            // reconnects an hour later must not re-publish the lobby's opening line.
            if (_outboxTopics.Count > 0)
            {
                for (int i = 0; i < _clients.Length; i++)
                {
                    if (_clients[i] == null) continue;
                    _outboxTopics.Clear();
                    _outboxTexts.Clear();
                    break;
                }
            }
        }

        public IList<InboundMessage> Poll()
        {
            if (_inbox.Count == 0) return Empty;

            var outp = new List<InboundMessage>(_inbox.Count);
            for (int i = 0; i < _inbox.Count; i++)
            {
                var m = _inbox[i];
                if (!Remember(m.Text)) continue;      // the same publish, via another broker
                outp.Add(m);
            }
            _inbox.Clear();
            return outp;
        }

        static readonly InboundMessage[] Empty = new InboundMessage[0];

        bool Remember(string text)
        {
            if (!_seen.Add(text)) return false;
            _seenOrder.Enqueue(text);
            while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
            return true;
        }

        public void Dispose()
        {
            for (int i = 0; i < _clients.Length; i++)
                if (_clients[i] != null) { _clients[i].Dispose(); _clients[i] = null; }
        }
    }
}
