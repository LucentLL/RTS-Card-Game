using System;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// SHA-256, HMAC-SHA256, PBKDF2 and HKDF, hand-written.
    ///
    /// Hand-written for the same reason StateCodec is: this has to produce IDENTICAL bytes on
    /// Mono in the editor, on IL2CPP in a Windows player, and on IL2CPP-to-WebAssembly in the
    /// browser build - and .NET Standard 2.1 is the API profile this project targets
    /// (apiCompatibilityLevel 6), which has no AesGcm at all and whose managed crypto has a
    /// history of platform-specific surprises under WebGL. Something with no platform surface
    /// cannot surprise us, and RFC test vectors in the EditMode gate prove it every run.
    ///
    /// Pinned by <c>CryptoVectorTests</c> against RFC 6234 (SHA-256), RFC 4231 (HMAC) and
    /// RFC 6070 (PBKDF2).
    /// </summary>
    public static class Sha256
    {
        public const int HashSize = 32;
        public const int BlockSize = 64;

        static readonly uint[] K =
        {
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u,
            0x923f82a4u, 0xab1c5ed5u, 0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
            0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u, 0xe49b69c1u, 0xefbe4786u,
            0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u,
            0x06ca6351u, 0x14292967u, 0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
            0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u, 0xa2bfe8a1u, 0xa81a664bu,
            0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au,
            0x5b9cca4fu, 0x682e6ff3u, 0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
            0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
        };

        static uint Ror(uint x, int n) { return (x >> n) | (x << (32 - n)); }

        /// <summary>A streaming context. Reused across PBKDF2's iterations so the inner loop
        /// does not allocate 60,000 times - which on WebGL is the difference between a
        /// noticeable pause and an invisible one.</summary>
        public struct Context
        {
            public uint H0, H1, H2, H3, H4, H5, H6, H7;
            public long Length;                 // total message bytes fed so far
            public int Buffered;                // bytes sitting in Block
            public byte[] Block;                // 64-byte staging buffer

            public static Context Create()
            {
                var c = new Context();
                c.Block = new byte[BlockSize];
                c.Reset();
                return c;
            }

            public void Reset()
            {
                H0 = 0x6a09e667u; H1 = 0xbb67ae85u; H2 = 0x3c6ef372u; H3 = 0xa54ff53au;
                H4 = 0x510e527fu; H5 = 0x9b05688cu; H6 = 0x1f83d9abu; H7 = 0x5be0cd19u;
                Length = 0;
                Buffered = 0;
            }
        }

        static void Compress(ref Context c, byte[] block, int offset)
        {
            // The message schedule. A stack array keeps the whole compression allocation-free.
            Span<uint> w = stackalloc uint[64];

            for (int i = 0; i < 16; i++)
            {
                int o = offset + i * 4;
                w[i] = ((uint)block[o] << 24) | ((uint)block[o + 1] << 16)
                     | ((uint)block[o + 2] << 8) | block[o + 3];
            }
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Ror(w[i - 15], 7) ^ Ror(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Ror(w[i - 2], 17) ^ Ror(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = unchecked(w[i - 16] + s0 + w[i - 7] + s1);
            }

            uint a = c.H0, b = c.H1, cc = c.H2, d = c.H3, e = c.H4, f = c.H5, g = c.H6, h = c.H7;

            for (int i = 0; i < 64; i++)
            {
                uint S1 = Ror(e, 6) ^ Ror(e, 11) ^ Ror(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = unchecked(h + S1 + ch + K[i] + w[i]);
                uint S0 = Ror(a, 2) ^ Ror(a, 13) ^ Ror(a, 22);
                uint maj = (a & b) ^ (a & cc) ^ (b & cc);
                uint t2 = unchecked(S0 + maj);

                h = g; g = f; f = e;
                e = unchecked(d + t1);
                d = cc; cc = b; b = a;
                a = unchecked(t1 + t2);
            }

            c.H0 = unchecked(c.H0 + a); c.H1 = unchecked(c.H1 + b);
            c.H2 = unchecked(c.H2 + cc); c.H3 = unchecked(c.H3 + d);
            c.H4 = unchecked(c.H4 + e); c.H5 = unchecked(c.H5 + f);
            c.H6 = unchecked(c.H6 + g); c.H7 = unchecked(c.H7 + h);
        }

        public static void Update(ref Context c, byte[] data, int offset, int count)
        {
            c.Length += count;
            int i = offset, end = offset + count;

            if (c.Buffered > 0)
            {
                int take = Math.Min(BlockSize - c.Buffered, end - i);
                Buffer.BlockCopy(data, i, c.Block, c.Buffered, take);
                c.Buffered += take;
                i += take;
                if (c.Buffered == BlockSize) { Compress(ref c, c.Block, 0); c.Buffered = 0; }
            }

            while (end - i >= BlockSize) { Compress(ref c, data, i); i += BlockSize; }

            if (end > i)
            {
                Buffer.BlockCopy(data, i, c.Block, 0, end - i);
                c.Buffered = end - i;
            }
        }

        public static void Update(ref Context c, byte[] data) { Update(ref c, data, 0, data.Length); }

        /// <summary>Finalise into <paramref name="into"/>. The context is left spent; Reset to reuse.</summary>
        public static void Final(ref Context c, byte[] into, int offset)
        {
            long bits = c.Length * 8L;

            // pad: 0x80, then zeros, then the 64-bit big-endian bit length
            c.Block[c.Buffered++] = 0x80;
            if (c.Buffered > BlockSize - 8)
            {
                while (c.Buffered < BlockSize) c.Block[c.Buffered++] = 0;
                Compress(ref c, c.Block, 0);
                c.Buffered = 0;
            }
            while (c.Buffered < BlockSize - 8) c.Block[c.Buffered++] = 0;
            for (int i = 7; i >= 0; i--) c.Block[c.Buffered++] = (byte)(bits >> (i * 8));
            Compress(ref c, c.Block, 0);

            WriteBe(into, offset + 0, c.H0); WriteBe(into, offset + 4, c.H1);
            WriteBe(into, offset + 8, c.H2); WriteBe(into, offset + 12, c.H3);
            WriteBe(into, offset + 16, c.H4); WriteBe(into, offset + 20, c.H5);
            WriteBe(into, offset + 24, c.H6); WriteBe(into, offset + 28, c.H7);
        }

        static void WriteBe(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        public static byte[] Hash(byte[] data)
        {
            var c = Context.Create();
            Update(ref c, data, 0, data.Length);
            var outp = new byte[HashSize];
            Final(ref c, outp, 0);
            return outp;
        }

        public static byte[] Hash(string utf8) { return Hash(Utf8.Bytes(utf8)); }

        /// <summary>Two buffers hashed as one message, without joining them first.</summary>
        public static byte[] Hash(byte[] a, byte[] b)
        {
            var c = Context.Create();
            Update(ref c, a, 0, a.Length);
            Update(ref c, b, 0, b.Length);
            var outp = new byte[HashSize];
            Final(ref c, outp, 0);
            return outp;
        }
    }

    /// <summary>HMAC-SHA256 with a pre-expanded key, so PBKDF2's inner loop re-uses one instance.</summary>
    public sealed class HmacSha256
    {
        readonly byte[] _ipad = new byte[Sha256.BlockSize];
        readonly byte[] _opad = new byte[Sha256.BlockSize];
        readonly byte[] _inner = new byte[Sha256.HashSize];

        public HmacSha256(byte[] key)
        {
            if (key == null) key = new byte[0];
            var k = key.Length > Sha256.BlockSize ? Sha256.Hash(key) : key;

            for (int i = 0; i < Sha256.BlockSize; i++)
            {
                byte kb = i < k.Length ? k[i] : (byte)0;
                _ipad[i] = (byte)(kb ^ 0x36);
                _opad[i] = (byte)(kb ^ 0x5c);
            }
        }

        public void Compute(byte[] message, int offset, int count, byte[] into, int intoOffset)
        {
            var c = Sha256.Context.Create();
            Sha256.Update(ref c, _ipad, 0, Sha256.BlockSize);
            Sha256.Update(ref c, message, offset, count);
            Sha256.Final(ref c, _inner, 0);

            c.Reset();
            Sha256.Update(ref c, _opad, 0, Sha256.BlockSize);
            Sha256.Update(ref c, _inner, 0, Sha256.HashSize);
            Sha256.Final(ref c, into, intoOffset);
        }

        public byte[] Compute(byte[] message)
        {
            var outp = new byte[Sha256.HashSize];
            Compute(message, 0, message.Length, outp, 0);
            return outp;
        }

        public static byte[] Mac(byte[] key, byte[] message)
        {
            return new HmacSha256(key).Compute(message);
        }
    }

    public static class Kdf
    {
        /// <summary>
        /// PBKDF2-HMAC-SHA256. The password is stretched ONCE per connection, so the cost is
        /// paid in the lobby where a pause is invisible, never in a match.
        /// </summary>
        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, int length)
        {
            if (iterations < 1) throw new ArgumentOutOfRangeException("iterations");
            if (length < 1) throw new ArgumentOutOfRangeException("length");

            var hmac = new HmacSha256(Utf8.Bytes(password));
            var outp = new byte[length];

            var block = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, block, 0, salt.Length);

            var u = new byte[Sha256.HashSize];
            var acc = new byte[Sha256.HashSize];

            int done = 0;
            for (int i = 1; done < length; i++)
            {
                block[salt.Length + 0] = (byte)(i >> 24);
                block[salt.Length + 1] = (byte)(i >> 16);
                block[salt.Length + 2] = (byte)(i >> 8);
                block[salt.Length + 3] = (byte)i;

                hmac.Compute(block, 0, block.Length, u, 0);
                Buffer.BlockCopy(u, 0, acc, 0, Sha256.HashSize);

                for (int it = 1; it < iterations; it++)
                {
                    hmac.Compute(u, 0, Sha256.HashSize, u, 0);
                    for (int b = 0; b < Sha256.HashSize; b++) acc[b] ^= u[b];
                }

                int take = Math.Min(Sha256.HashSize, length - done);
                Buffer.BlockCopy(acc, 0, outp, done, take);
                done += take;
            }
            return outp;
        }

        /// <summary>
        /// HKDF-Expand (RFC 5869) over an already-uniform key. Extract is skipped deliberately:
        /// the input here is a PBKDF2 output, which is already a uniformly random 32 bytes, and
        /// expanding it under distinct info strings is exactly what domain separation needs.
        /// </summary>
        public static byte[] Expand(byte[] prk, string info, int length)
        {
            var hmac = new HmacSha256(prk);
            var infoBytes = Utf8.Bytes(info);
            var outp = new byte[length];

            var t = new byte[0];
            var buf = new byte[Sha256.HashSize + infoBytes.Length + 1];
            int done = 0;

            for (byte counter = 1; done < length; counter++)
            {
                int n = 0;
                Buffer.BlockCopy(t, 0, buf, n, t.Length); n += t.Length;
                Buffer.BlockCopy(infoBytes, 0, buf, n, infoBytes.Length); n += infoBytes.Length;
                buf[n++] = counter;

                var block = new byte[Sha256.HashSize];
                hmac.Compute(buf, 0, n, block, 0);
                t = block;

                int take = Math.Min(Sha256.HashSize, length - done);
                Buffer.BlockCopy(block, 0, outp, done, take);
                done += take;
            }
            return outp;
        }
    }

    /// <summary>UTF-8 without a BOM and without a dependency on which encoder a platform ships.</summary>
    public static class Utf8
    {
        static readonly System.Text.UTF8Encoding Enc = new System.Text.UTF8Encoding(false, false);

        public static byte[] Bytes(string s) { return Enc.GetBytes(s ?? ""); }
        public static string String(byte[] b) { return Enc.GetString(b); }
        public static string String(byte[] b, int offset, int count) { return Enc.GetString(b, offset, count); }
    }
}
