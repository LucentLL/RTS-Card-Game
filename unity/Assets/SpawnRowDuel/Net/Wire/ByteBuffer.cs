using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Net
{
    /// <summary>A malformed or truncated frame. Always caught at the session boundary and
    /// reported as a protocol error - it must never escape as an unhandled exception, because
    /// the bytes that caused it may have come from a stranger on a public relay topic.</summary>
    public sealed class WireFormatException : Exception
    {
        public WireFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// Little-endian varints, deliberately the same shape as BinaryStateWriter's encoding. Wire
    /// compactness is not vanity here: every frame crosses a free public relay with per-message
    /// and per-day allowances, so a command that fits in ten bytes is a design constraint.
    /// </summary>
    public sealed class ByteWriter
    {
        readonly List<byte> _buf;

        public ByteWriter() { _buf = new List<byte>(64); }
        public ByteWriter(int capacity) { _buf = new List<byte>(capacity); }

        public int Length { get { return _buf.Count; } }

        public byte[] ToArray() { return _buf.ToArray(); }

        public void Byte(byte b) { _buf.Add(b); }

        public void Bool(bool b) { _buf.Add(b ? (byte)1 : (byte)0); }

        public void VarInt(ulong v)
        {
            while (v >= 0x80) { _buf.Add((byte)(v | 0x80)); v >>= 7; }
            _buf.Add((byte)v);
        }

        /// <summary>Zig-zag, so small negatives stay one byte.</summary>
        public void Int(int v) { VarInt((ulong)((v << 1) ^ (v >> 31))); }

        public void U64(ulong v)
        {
            for (int i = 0; i < 8; i++) _buf.Add((byte)(v >> (i * 8)));
        }

        public void Bytes(byte[] b)
        {
            if (b == null) { VarInt(0); return; }
            VarInt((ulong)b.Length);
            _buf.AddRange(b);
        }

        public void Raw(byte[] b) { _buf.AddRange(b); }

        public void String(string s)
        {
            Bytes(s == null ? null : Utf8.Bytes(s));
        }
    }

    public sealed class ByteReader
    {
        readonly byte[] _buf;
        int _pos;

        public ByteReader(byte[] buf) : this(buf, 0) { }

        public ByteReader(byte[] buf, int offset)
        {
            if (buf == null) throw new WireFormatException("empty frame");
            _buf = buf;
            _pos = offset;
        }

        public int Position { get { return _pos; } }
        public int Remaining { get { return _buf.Length - _pos; } }
        public bool AtEnd { get { return _pos >= _buf.Length; } }

        void Need(int n)
        {
            if (_buf.Length - _pos < n) throw new WireFormatException("frame truncated");
        }

        public byte Byte() { Need(1); return _buf[_pos++]; }

        public bool Bool() { return Byte() != 0; }

        public ulong VarInt()
        {
            ulong v = 0;
            int shift = 0;
            while (true)
            {
                byte b = Byte();
                if (shift > 63) throw new WireFormatException("varint overflow");
                v |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return v;
                shift += 7;
            }
        }

        public int Int()
        {
            ulong v = VarInt();
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        public ulong U64()
        {
            Need(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)_buf[_pos++] << (i * 8);
            return v;
        }

        public byte[] Bytes()
        {
            ulong n = VarInt();
            if (n > int.MaxValue) throw new WireFormatException("length overflow");
            int count = (int)n;
            Need(count);
            var outp = new byte[count];
            Buffer.BlockCopy(_buf, _pos, outp, 0, count);
            _pos += count;
            return outp;
        }

        public string String()
        {
            var b = Bytes();
            return Utf8.String(b);
        }

        /// <summary>A byte constrained to a known range - the guard every enum read needs when
        /// the bytes are untrusted.</summary>
        public byte Enum(int exclusiveMax, string what)
        {
            byte b = Byte();
            if (b >= exclusiveMax) throw new WireFormatException("bad " + what + ": " + b);
            return b;
        }
    }
}
