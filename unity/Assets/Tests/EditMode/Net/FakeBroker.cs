using System.Collections.Generic;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// A set of MQTT brokers in memory, behind their urls, speaking the real wire format.
    ///
    /// This is what makes the SHIPPED transport testable. LoopbackTransport proves the protocol;
    /// this proves the thing that will actually carry it - MqttClient's packet handling, its
    /// connect/subscribe ordering, RelayTransport's fan-out across several brokers, its
    /// deduplication, and its failover when one of them dies - without a network, on a virtual
    /// clock, in the same EditMode gate as everything else.
    ///
    /// Sockets resolve their broker from the url at dial time, exactly as a real one does, so a
    /// transport configured with three brokers really is talking to three different things.
    /// </summary>
    public sealed class FakeNetwork : IWebSocketFactory
    {
        readonly Dictionary<string, FakeBroker> _brokers =
            new Dictionary<string, FakeBroker>(System.StringComparer.Ordinal);

        public FakeBroker Add(string url)
        {
            var b = new FakeBroker();
            _brokers[url] = b;
            return b;
        }

        internal FakeBroker Resolve(string url)
        {
            FakeBroker b;
            return _brokers.TryGetValue(url, out b) ? b : null;
        }

        public IWebSocket Create() { return new FakeSocket(this); }
    }

    /// <summary>One broker. Answers CONNECT, SUBSCRIBE, PUBLISH and PINGREQ, and nothing else,
    /// because that is the entire dialect the client speaks.</summary>
    public sealed class FakeBroker
    {
        readonly List<FakeSocket> _clients = new List<FakeSocket>();

        /// <summary>Refuse every connection, as an unreachable broker does.</summary>
        public bool Down;

        /// <summary>Accept the socket but never answer CONNECT - the hang the connect timeout
        /// exists for.</summary>
        public bool Mute;

        /// <summary>Refuse at the protocol level, with this CONNACK code.</summary>
        public byte RefuseWith;

        public int Published;
        public int Delivered;

        internal void Attach(FakeSocket s) { _clients.Add(s); }

        internal void Detach(FakeSocket s) { _clients.Remove(s); }

        internal void Route(string topic, string payload)
        {
            Published++;
            for (int i = 0; i < _clients.Count; i++)
            {
                if (!_clients[i].Subscribed(topic)) continue;
                _clients[i].Deliver(Mqtt.EncodePublish(topic, payload));
                Delivered++;
            }
        }

        /// <summary>Kill every live connection, as a broker restart does.</summary>
        public void DropEveryone()
        {
            var copy = _clients.ToArray();
            for (int i = 0; i < copy.Length; i++) copy[i].Break("the relay went away");
        }

    }

    internal sealed class FakeSocket : IWebSocket
    {
        readonly FakeNetwork _network;
        FakeBroker _broker;
        readonly Queue<byte[]> _inbound = new Queue<byte[]>();
        readonly HashSet<string> _topics = new HashSet<string>();
        readonly List<Mqtt.Packet> _scratch = new List<Mqtt.Packet>();

        byte[] _buffer = new byte[2048];
        int _buffered;
        bool _connected;

        public FakeSocket(FakeNetwork network) { _network = network; }

        public SocketState State { get; private set; }
        public string LastError { get; private set; }

        public void Connect(string url, string subProtocol)
        {
            // Resolved at dial time from the url, exactly as a real socket does - so a transport
            // configured with several brokers really does reach the one it addressed.
            _broker = _network.Resolve(url);
            if (_broker == null || _broker.Down)
            {
                State = SocketState.Failed;
                LastError = "unreachable";
                return;
            }
            State = SocketState.Open;
            _broker.Attach(this);
        }

        internal bool Subscribed(string topic) { return _topics.Contains(topic); }

        internal void Deliver(byte[] frame) { _inbound.Enqueue(frame); }

        internal void Break(string why)
        {
            LastError = why;
            State = SocketState.Failed;
            if (_broker != null) _broker.Detach(this);
        }

        public void Send(byte[] bytes)
        {
            if (State != SocketState.Open) return;

            if (_buffered + bytes.Length > _buffer.Length)
            {
                int size = _buffer.Length;
                while (size < _buffered + bytes.Length) size *= 2;
                var bigger = new byte[size];
                System.Buffer.BlockCopy(_buffer, 0, bigger, 0, _buffered);
                _buffer = bigger;
            }
            System.Buffer.BlockCopy(bytes, 0, _buffer, _buffered, bytes.Length);
            _buffered += bytes.Length;

            _scratch.Clear();
            int consumed = Mqtt.Decode(_buffer, _buffered, _scratch);
            if (consumed > 0)
            {
                System.Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _buffered - consumed);
                _buffered -= consumed;
            }

            for (int i = 0; i < _scratch.Count; i++) Handle(_scratch[i]);
        }

        void Handle(Mqtt.Packet p)
        {
            switch (p.Type)
            {
                case Mqtt.Connect:
                    if (_broker.Mute) return;                       // accept, then say nothing
                    _connected = _broker.RefuseWith == 0;
                    _inbound.Enqueue(ConnAck(_broker.RefuseWith));
                    break;

                case Mqtt.Subscribe:
                {
                    if (!_connected) return;
                    int pos = 2;                                    // packet id
                    var topic = Mqtt.ReadString(p.Body, ref pos);
                    _topics.Add(topic);
                    _inbound.Enqueue(SubAck(p.Body[0], p.Body[1]));
                    break;
                }

                case Mqtt.Publish:
                    if (!_connected) return;
                    _broker.Route(p.Topic, p.Payload);
                    break;

                case Mqtt.PingReq:
                    _inbound.Enqueue(new byte[] { Mqtt.PingResp << 4, 0 });
                    break;

                case Mqtt.Disconnect:
                    State = SocketState.Closed;
                    if (_broker != null) _broker.Detach(this);
                    break;
            }
        }

        static byte[] ConnAck(byte code) { return new byte[] { Mqtt.ConnAck << 4, 2, 0, code }; }

        static byte[] SubAck(byte idHi, byte idLo)
        {
            return new byte[] { Mqtt.SubAck << 4, 3, idHi, idLo, 0 };
        }

        public byte[] Receive()
        {
            return _inbound.Count > 0 ? _inbound.Dequeue() : null;
        }

        public void Close()
        {
            if (State == SocketState.Open && _broker != null) _broker.Detach(this);
            State = SocketState.Closed;
        }

        public void Dispose() { Close(); }
    }
}
