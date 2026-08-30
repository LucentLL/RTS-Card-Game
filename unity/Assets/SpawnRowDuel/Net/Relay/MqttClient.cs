using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// One broker connection: open the socket, say hello, subscribe, then pump messages both ways.
    ///
    /// Everything is driven from Pump(dt) - there is no timer, no thread and no callback above the
    /// socket - so a stalled connection is visible as a state rather than as a hang, and the whole
    /// thing steps deterministically inside a test.
    /// </summary>
    public sealed class MqttClient : IDisposable
    {
        public const int KeepAliveSeconds = 45;

        readonly IWebSocket _socket;
        readonly string _url;
        readonly string _clientId;

        readonly List<string> _topics = new List<string>();
        readonly List<string> _pendingSubscribes = new List<string>();
        readonly List<string> _pendingPublishTopics = new List<string>();
        readonly List<string> _pendingPublishTexts = new List<string>();
        readonly List<Mqtt.Packet> _packets = new List<Mqtt.Packet>();

        byte[] _buffer = new byte[4096];
        int _buffered;

        bool _sentConnect;
        bool _ready;
        int _nextPacketId = 1;
        double _sinceKeepAlive;
        double _sinceConnect;

        public MqttClient(IWebSocket socket, string url, string clientId)
        {
            _socket = socket;
            _url = url;
            _clientId = clientId;
        }

        public string Url { get { return _url; } }
        public bool Ready { get { return _ready; } }
        public string LastError { get; private set; }

        public SocketState State { get { return _socket.State; } }

        /// <summary>How long we have been trying without reaching Ready. The relay pool gives up
        /// on a broker that will not answer and leans on the others.</summary>
        public double ConnectingFor { get { return _ready ? 0.0 : _sinceConnect; } }

        public void Start()
        {
            _socket.Connect(_url, "mqtt");
        }

        public void Subscribe(string topic)
        {
            if (_topics.Contains(topic)) return;
            _topics.Add(topic);
            if (_ready) SendSubscribe(topic);
            else _pendingSubscribes.Add(topic);
        }

        public void Publish(string topic, string text)
        {
            if (_ready) { Write(Mqtt.EncodePublish(topic, text)); return; }

            // Queued rather than dropped: the commonest case for a not-yet-ready broker is the
            // first second of a match, when the handshake is exactly what must not be lost.
            if (_pendingPublishTopics.Count < 256)
            {
                _pendingPublishTopics.Add(topic);
                _pendingPublishTexts.Add(text);
            }
        }

        /// <summary>Anything that arrived, appended to the caller's list.</summary>
        public void Pump(double deltaSeconds, List<InboundMessage> into)
        {
            if (_socket.State == SocketState.Failed)
            {
                LastError = _socket.LastError;
                _ready = false;
                return;
            }

            if (!_ready) _sinceConnect += deltaSeconds;

            if (_socket.State != SocketState.Open) return;

            if (!_sentConnect)
            {
                _sentConnect = true;
                Write(Mqtt.EncodeConnect(_clientId, KeepAliveSeconds));
            }

            byte[] chunk;
            while ((chunk = _socket.Receive()) != null) Append(chunk);

            _packets.Clear();
            int consumed;
            try { consumed = Mqtt.Decode(_buffer, _buffered, _packets); }
            catch (WireFormatException e)
            {
                // A broker speaking something we cannot parse is a broker we stop using; the pool
                // has others. Never let it throw into the player loop.
                LastError = e.Message;
                _ready = false;
                _socket.Close();
                return;
            }

            if (consumed > 0)
            {
                Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _buffered - consumed);
                _buffered -= consumed;
            }

            for (int i = 0; i < _packets.Count; i++) Handle(_packets[i], into);

            if (_ready)
            {
                _sinceKeepAlive += deltaSeconds;
                if (_sinceKeepAlive >= KeepAliveSeconds * 0.6)
                {
                    _sinceKeepAlive = 0.0;
                    Write(Mqtt.EncodePingReq());
                }
            }
        }

        void Handle(Mqtt.Packet p, List<InboundMessage> into)
        {
            switch (p.Type)
            {
                case Mqtt.ConnAck:
                    LastError = Mqtt.ConnAckReason(p.ReturnCode);
                    if (LastError != null) { _ready = false; _socket.Close(); return; }
                    _ready = true;
                    _sinceKeepAlive = 0.0;

                    for (int i = 0; i < _topics.Count; i++) SendSubscribe(_topics[i]);
                    _pendingSubscribes.Clear();

                    for (int i = 0; i < _pendingPublishTopics.Count; i++)
                        Write(Mqtt.EncodePublish(_pendingPublishTopics[i], _pendingPublishTexts[i]));
                    _pendingPublishTopics.Clear();
                    _pendingPublishTexts.Clear();
                    break;

                case Mqtt.Publish:
                    into.Add(new InboundMessage { Topic = p.Topic, Text = p.Payload });
                    break;
            }
        }

        void SendSubscribe(string topic)
        {
            int id = _nextPacketId++;
            if (_nextPacketId > 0xFFFF) _nextPacketId = 1;
            Write(Mqtt.EncodeSubscribe(id, topic));
        }

        void Write(byte[] bytes)
        {
            try { _socket.Send(bytes); }
            catch (Exception e) { LastError = e.Message; _ready = false; }
        }

        void Append(byte[] chunk)
        {
            if (_buffered + chunk.Length > _buffer.Length)
            {
                int size = _buffer.Length;
                while (size < _buffered + chunk.Length) size *= 2;
                var bigger = new byte[size];
                Buffer.BlockCopy(_buffer, 0, bigger, 0, _buffered);
                _buffer = bigger;
            }
            Buffer.BlockCopy(chunk, 0, _buffer, _buffered, chunk.Length);
            _buffered += chunk.Length;
        }

        public void Dispose()
        {
            try { if (_ready) Write(Mqtt.EncodeDisconnect()); }
            catch (Exception) { }
            _socket.Dispose();
        }
    }
}
