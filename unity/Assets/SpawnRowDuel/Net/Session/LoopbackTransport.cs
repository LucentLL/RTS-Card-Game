using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// An in-memory relay that behaves like a bad one on purpose.
    ///
    /// The real transport's failure modes are latency, duplicate delivery, reordering, loss and a
    /// link that drops - and every one of those is a protocol bug waiting to happen. Rather than
    /// hope, the gate plays whole matches across this thing with the knobs turned up, on a
    /// virtual clock, in milliseconds.
    ///
    /// It models a live broker rather than a mailbox: a message reaches whoever is subscribed at
    /// that instant, and nothing is retained. That is exactly what a public MQTT broker does, and
    /// modelling it faithfully is what forced the protocol to keep its own log instead of leaning
    /// on somebody else's cache.
    /// </summary>
    public sealed class LoopbackHub
    {
        /// <summary>Virtual seconds. Nothing here reads a real clock.</summary>
        public double Now;

        /// <summary>One-way delay applied to every message.</summary>
        public double Latency;

        /// <summary>0..1 chance a delivery is dropped entirely.</summary>
        public double LossChance;

        /// <summary>0..1 chance a message is delivered twice.</summary>
        public double DuplicateChance;

        /// <summary>Extra delay added per message, which is what produces reordering.</summary>
        public double Jitter;

        /// <summary>Deterministic - a test that fails must fail again.</summary>
        public readonly Rng Random = new Rng(0x5eed);

        public sealed class Rng
        {
            ulong _s;
            public Rng(ulong seed) { _s = seed * 6364136223846793005UL + 1442695040888963407UL; }
            public double NextDouble()
            {
                _s = _s * 6364136223846793005UL + 1442695040888963407UL;
                return ((_s >> 11) & ((1UL << 53) - 1)) / (double)(1UL << 53);
            }
        }

        /// <summary>Wire accounting, for the cost assertions.</summary>
        public int TotalMessages;
        public int TotalBytes;

        sealed class Pending
        {
            public string Topic;
            public string Text;
            public double DueAt;
            public LoopbackTransport To;
        }

        readonly List<Pending> _inFlight = new List<Pending>();
        readonly List<LoopbackTransport> _peers = new List<LoopbackTransport>();

        public LoopbackTransport Connect()
        {
            var t = new LoopbackTransport(this);
            _peers.Add(t);
            return t;
        }

        internal void Post(string topic, string text)
        {
            TotalMessages++;
            TotalBytes += text.Length;

            for (int i = 0; i < _peers.Count; i++)
            {
                var peer = _peers[i];
                if (!peer.Listens(topic)) continue;

                int copies = 1 + (Random.NextDouble() < DuplicateChance ? 1 : 0);
                for (int c = 0; c < copies; c++)
                {
                    if (Random.NextDouble() < LossChance) continue;
                    _inFlight.Add(new Pending
                    {
                        Topic = topic,
                        Text = text,
                        To = peer,
                        DueAt = Now + Latency + Random.NextDouble() * Jitter,
                    });
                }
            }
        }

        /// <summary>Advance the virtual clock and deliver anything now due.</summary>
        public void Advance(double seconds)
        {
            Now += seconds;
            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                if (_inFlight[i].DueAt > Now) continue;
                _inFlight[i].To.Deliver(_inFlight[i].Topic, _inFlight[i].Text);
                _inFlight.RemoveAt(i);
            }
        }
    }

    public sealed class LoopbackTransport : IMessageTransport
    {
        readonly LoopbackHub _hub;
        readonly HashSet<string> _topics = new HashSet<string>();
        readonly List<InboundMessage> _inbox = new List<InboundMessage>();

        internal LoopbackTransport(LoopbackHub hub) { _hub = hub; }

        public TransportStatus Status { get { return Offline ? TransportStatus.Retrying : TransportStatus.Connected; } }
        public string LastError { get { return null; } }
        public string Description { get { return "loopback"; } }

        /// <summary>Simulates a peer whose network has gone away entirely.</summary>
        public bool Offline;

        internal bool Listens(string topic) { return !Offline && _topics.Contains(topic); }

        public void Publish(string topic, string text)
        {
            if (Offline) return;
            _hub.Post(topic, text);
        }

        public void Subscribe(string topic) { _topics.Add(topic); }

        public void Unsubscribe(string topic) { _topics.Remove(topic); }

        internal void Deliver(string topic, string text)
        {
            if (Offline) return;
            _inbox.Add(new InboundMessage { Topic = topic, Text = text });
        }

        public IList<InboundMessage> Poll()
        {
            if (_inbox.Count == 0) return Empty;
            var outp = new List<InboundMessage>(_inbox);
            _inbox.Clear();
            return outp;
        }

        static readonly InboundMessage[] Empty = new InboundMessage[0];

        public void Pump(double deltaSeconds) { }

        public void Dispose() { _topics.Clear(); _inbox.Clear(); }
    }
}
