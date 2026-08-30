using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// MQTT 3.1.1, the parts of it a duel needs: connect, subscribe, publish at QoS 0, keepalive.
    ///
    /// Hand-written and tiny for the usual reason - it has to run identically under Mono, IL2CPP
    /// and WebAssembly, and an MQTT package would drag in sockets, threads and a TLS stack that
    /// WebGL does not have. The whole protocol here is under two hundred lines because everything
    /// above it already tolerates loss, duplication and reordering, so QoS 0 is not a compromise:
    /// QoS 1 would add packet ids, retransmission and an acknowledgement state machine to
    /// re-solve a problem the ply counter already solves.
    ///
    /// Pinned by <c>MqttCodecTests</c> against byte sequences read off the specification.
    /// </summary>
    public static class Mqtt
    {
        public const byte Connect = 1;
        public const byte ConnAck = 2;
        public const byte Publish = 3;
        public const byte Subscribe = 8;
        public const byte SubAck = 9;
        public const byte PingReq = 12;
        public const byte PingResp = 13;
        public const byte Disconnect = 14;

        /// <summary>A decoded packet. Only the fields the client reads.</summary>
        public struct Packet
        {
            public byte Type;
            public byte Flags;
            public string Topic;        // PUBLISH only
            public string Payload;      // PUBLISH only
            public byte ReturnCode;     // CONNACK only

            /// <summary>The variable header and payload, undecoded. The client ignores it; the
            /// in-memory broker the tests run against parses SUBSCRIBE out of it.</summary>
            public byte[] Body;
        }

        /// <summary>Read a length-prefixed MQTT string out of a packet body.</summary>
        public static string ReadString(byte[] body, ref int pos)
        {
            if (pos + 2 > body.Length) throw new WireFormatException("mqtt string truncated");
            int len = (body[pos] << 8) | body[pos + 1];
            pos += 2;
            if (pos + len > body.Length) throw new WireFormatException("mqtt string overruns");
            var s = Utf8.String(body, pos, len);
            pos += len;
            return s;
        }

        // ---- writing ---------------------------------------------------------------------------

        static void RemainingLength(List<byte> into, int length)
        {
            do
            {
                byte b = (byte)(length % 128);
                length /= 128;
                if (length > 0) b |= 0x80;
                into.Add(b);
            } while (length > 0);
        }

        static void Str(List<byte> into, string s)
        {
            var bytes = Utf8.Bytes(s);
            if (bytes.Length > 0xFFFF) throw new WireFormatException("mqtt string too long");
            into.Add((byte)(bytes.Length >> 8));
            into.Add((byte)bytes.Length);
            into.AddRange(bytes);
        }

        static byte[] Frame(byte type, byte flags, List<byte> body)
        {
            var outp = new List<byte>(body.Count + 5);
            outp.Add((byte)((type << 4) | (flags & 0x0F)));
            RemainingLength(outp, body.Count);
            outp.AddRange(body);
            return outp.ToArray();
        }

        /// <summary>
        /// CONNECT with a clean session and no credentials. Clean session matters: these are
        /// public brokers and we must never leave a subscription behind that a later connection
        /// with the same client id would inherit.
        /// </summary>
        public static byte[] EncodeConnect(string clientId, int keepAliveSeconds)
        {
            var body = new List<byte>(32);
            Str(body, "MQTT");
            body.Add(4);                                   // protocol level 3.1.1
            body.Add(0x02);                                // clean session, no will, no auth
            body.Add((byte)(keepAliveSeconds >> 8));
            body.Add((byte)keepAliveSeconds);
            Str(body, clientId);
            return Frame(Connect, 0, body);
        }

        public static byte[] EncodeSubscribe(int packetId, string topic)
        {
            var body = new List<byte>(topic.Length + 8);
            body.Add((byte)(packetId >> 8));
            body.Add((byte)packetId);
            Str(body, topic);
            body.Add(0);                                   // QoS 0
            return Frame(Subscribe, 0x02, body);           // reserved flags for SUBSCRIBE are 0010
        }

        public static byte[] EncodePublish(string topic, string payload)
        {
            var body = new List<byte>(topic.Length + payload.Length + 4);
            Str(body, topic);                              // no packet id at QoS 0
            body.AddRange(Utf8.Bytes(payload));
            return Frame(Publish, 0, body);
        }

        public static byte[] EncodePingReq() { return Frame(PingReq, 0, new List<byte>()); }

        public static byte[] EncodeDisconnect() { return Frame(Disconnect, 0, new List<byte>()); }

        // ---- reading ---------------------------------------------------------------------------

        /// <summary>
        /// Decode as many whole packets as the buffer holds, consuming them. Returns how many
        /// bytes were consumed; a partial packet at the end is left for the next read, because a
        /// WebSocket message boundary is not a packet boundary.
        /// </summary>
        public static int Decode(byte[] buffer, int length, List<Packet> into)
        {
            int pos = 0;
            while (true)
            {
                if (length - pos < 2) break;

                int start = pos;
                byte header = buffer[pos++];

                int remaining = 0, multiplier = 1, digits = 0;
                bool complete = false;
                while (pos < length)
                {
                    byte b = buffer[pos++];
                    remaining += (b & 0x7F) * multiplier;
                    multiplier *= 128;
                    if (++digits > 4) throw new WireFormatException("mqtt length overflow");
                    if ((b & 0x80) == 0) { complete = true; break; }
                }
                if (!complete) { pos = start; break; }
                if (remaining < 0 || pos + remaining > length) { pos = start; break; }

                var p = new Packet();
                p.Type = (byte)(header >> 4);
                p.Flags = (byte)(header & 0x0F);

                int end = pos + remaining;
                if (p.Type == Publish)
                {
                    int qos = (p.Flags >> 1) & 3;
                    int tlen = (buffer[pos] << 8) | buffer[pos + 1];
                    pos += 2;
                    if (pos + tlen > end) throw new WireFormatException("mqtt topic overruns packet");
                    p.Topic = Utf8.String(buffer, pos, tlen);
                    pos += tlen;
                    if (qos > 0) pos += 2;                 // packet id we never asked for
                    if (pos > end) throw new WireFormatException("mqtt publish overruns packet");
                    p.Payload = Utf8.String(buffer, pos, end - pos);
                }
                else
                {
                    if (p.Type == ConnAck && remaining >= 2) p.ReturnCode = buffer[pos + 1];
                    p.Body = new byte[remaining];
                    Buffer.BlockCopy(buffer, pos, p.Body, 0, remaining);
                }

                into.Add(p);
                pos = end;
            }
            return pos;
        }

        /// <summary>What a CONNACK return code means, in words a player could read.</summary>
        public static string ConnAckReason(byte code)
        {
            switch (code)
            {
                case 0: return null;
                case 1: return "the relay refused our protocol version";
                case 2: return "the relay rejected our client id";
                case 3: return "the relay is unavailable";
                case 4: return "the relay wanted credentials";
                case 5: return "the relay refused us";
                default: return "the relay refused us (code " + code + ")";
            }
        }
    }
}
