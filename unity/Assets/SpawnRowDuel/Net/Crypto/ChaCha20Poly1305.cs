using System;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// ChaCha20-Poly1305 AEAD, RFC 8439, hand-written for the same reason as
    /// <see cref="Sha256"/>: .NET Standard 2.1 has no AesGcm, and this must produce identical
    /// bytes under Mono, IL2CPP and WebAssembly.
    ///
    /// AEAD rather than plain encryption is the point. A frame that fails its authentication tag
    /// is REJECTED, so a wrong password is a clean "wrong password" and a stranger publishing
    /// noise onto the (public) relay topic is discarded before a single byte of it is parsed -
    /// the protocol decoder only ever sees bytes this key authenticated.
    ///
    /// Pinned by <c>CryptoVectorTests</c> against the RFC 8439 section 2.8.2 worked example.
    /// </summary>
    public static class ChaCha20Poly1305
    {
        public const int KeySize = 32;
        public const int NonceSize = 12;
        public const int TagSize = 16;

        // ---- ChaCha20 block function (RFC 8439 s2.3) ---------------------------------------

        static uint Rol(uint x, int n) { return (x << n) | (x >> (32 - n)); }

        static void Quarter(Span<uint> s, int a, int b, int c, int d)
        {
            unchecked
            {
                s[a] += s[b]; s[d] = Rol(s[d] ^ s[a], 16);
                s[c] += s[d]; s[b] = Rol(s[b] ^ s[c], 12);
                s[a] += s[b]; s[d] = Rol(s[d] ^ s[a], 8);
                s[c] += s[d]; s[b] = Rol(s[b] ^ s[c], 7);
            }
        }

        static uint Le32(byte[] b, int o)
        {
            return b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24);
        }

        static void Block(byte[] key, byte[] nonce, uint counter, byte[] into)
        {
            Span<uint> s = stackalloc uint[16];
            Span<uint> w = stackalloc uint[16];

            s[0] = 0x61707865u; s[1] = 0x3320646eu; s[2] = 0x79622d32u; s[3] = 0x6b206574u;
            for (int i = 0; i < 8; i++) s[4 + i] = Le32(key, i * 4);
            s[12] = counter;
            for (int i = 0; i < 3; i++) s[13 + i] = Le32(nonce, i * 4);

            for (int i = 0; i < 16; i++) w[i] = s[i];

            for (int round = 0; round < 10; round++)
            {
                Quarter(w, 0, 4, 8, 12); Quarter(w, 1, 5, 9, 13);
                Quarter(w, 2, 6, 10, 14); Quarter(w, 3, 7, 11, 15);
                Quarter(w, 0, 5, 10, 15); Quarter(w, 1, 6, 11, 12);
                Quarter(w, 2, 7, 8, 13); Quarter(w, 3, 4, 9, 14);
            }

            for (int i = 0; i < 16; i++)
            {
                uint v = unchecked(w[i] + s[i]);
                into[i * 4 + 0] = (byte)v;
                into[i * 4 + 1] = (byte)(v >> 8);
                into[i * 4 + 2] = (byte)(v >> 16);
                into[i * 4 + 3] = (byte)(v >> 24);
            }
        }

        /// <summary>XOR the buffer with the keystream, starting at the given block counter.</summary>
        static void Xor(byte[] key, byte[] nonce, uint counter, byte[] buf, int offset, int count)
        {
            var stream = new byte[64];
            int done = 0;
            while (done < count)
            {
                Block(key, nonce, unchecked(counter + (uint)(done / 64)), stream);
                int take = Math.Min(64, count - done);
                for (int i = 0; i < take; i++) buf[offset + done + i] ^= stream[i];
                done += take;
            }
        }

        // ---- Poly1305 (RFC 8439 s2.5) ------------------------------------------------------

        /// <summary>
        /// Poly1305 over 26-bit limbs. Limbs rather than a BigInteger because BigInteger is both
        /// slow and, on IL2CPP, another platform surface; five 26-bit limbs multiply without
        /// overflowing a ulong, which is all this needs.
        /// </summary>
        sealed class Poly1305
        {
            readonly uint[] _r = new uint[5];
            readonly uint[] _s = new uint[4];
            readonly ulong[] _h = new ulong[5];
            readonly byte[] _buf = new byte[16];
            int _buffered;

            public Poly1305(byte[] key)
            {
                // clamp r
                uint t0 = Le32(key, 0), t1 = Le32(key, 4), t2 = Le32(key, 8), t3 = Le32(key, 12);
                _r[0] = t0 & 0x3ffffffu;
                _r[1] = ((t0 >> 26) | (t1 << 6)) & 0x3ffff03u;
                _r[2] = ((t1 >> 20) | (t2 << 12)) & 0x3ffc0ffu;
                _r[3] = ((t2 >> 14) | (t3 << 18)) & 0x3f03fffu;
                _r[4] = (t3 >> 8) & 0x00fffffu;

                for (int i = 0; i < 4; i++) _s[i] = Le32(key, 16 + i * 4);
            }

            void Absorb(byte[] b, int o, bool final)
            {
                uint t0 = Le32(b, o), t1 = Le32(b, o + 4), t2 = Le32(b, o + 8), t3 = Le32(b, o + 12);

                _h[0] += t0 & 0x3ffffffu;
                _h[1] += ((t0 >> 26) | (t1 << 6)) & 0x3ffffffu;
                _h[2] += ((t1 >> 20) | (t2 << 12)) & 0x3ffffffu;
                _h[3] += ((t2 >> 14) | (t3 << 18)) & 0x3ffffffu;
                _h[4] += (t3 >> 8) | (final ? 0u : (1u << 24));

                // h *= r  (mod 2^130 - 5)
                ulong d0 = _h[0] * _r[0] + _h[1] * (5UL * _r[4]) + _h[2] * (5UL * _r[3])
                         + _h[3] * (5UL * _r[2]) + _h[4] * (5UL * _r[1]);
                ulong d1 = _h[0] * _r[1] + _h[1] * _r[0] + _h[2] * (5UL * _r[4])
                         + _h[3] * (5UL * _r[3]) + _h[4] * (5UL * _r[2]);
                ulong d2 = _h[0] * _r[2] + _h[1] * _r[1] + _h[2] * _r[0]
                         + _h[3] * (5UL * _r[4]) + _h[4] * (5UL * _r[3]);
                ulong d3 = _h[0] * _r[3] + _h[1] * _r[2] + _h[2] * _r[1]
                         + _h[3] * _r[0] + _h[4] * (5UL * _r[4]);
                ulong d4 = _h[0] * _r[4] + _h[1] * _r[3] + _h[2] * _r[2]
                         + _h[3] * _r[1] + _h[4] * _r[0];

                ulong c = d0 >> 26; _h[0] = d0 & 0x3ffffffu;
                d1 += c; c = d1 >> 26; _h[1] = d1 & 0x3ffffffu;
                d2 += c; c = d2 >> 26; _h[2] = d2 & 0x3ffffffu;
                d3 += c; c = d3 >> 26; _h[3] = d3 & 0x3ffffffu;
                d4 += c; c = d4 >> 26; _h[4] = d4 & 0x3ffffffu;
                _h[0] += c * 5; c = _h[0] >> 26; _h[0] &= 0x3ffffffu;
                _h[1] += c;
            }

            public void Update(byte[] data, int offset, int count)
            {
                int i = offset, end = offset + count;

                if (_buffered > 0)
                {
                    int take = Math.Min(16 - _buffered, end - i);
                    Buffer.BlockCopy(data, i, _buf, _buffered, take);
                    _buffered += take; i += take;
                    if (_buffered == 16) { Absorb(_buf, 0, false); _buffered = 0; }
                }

                while (end - i >= 16) { Absorb(data, i, false); i += 16; }

                if (end > i)
                {
                    Buffer.BlockCopy(data, i, _buf, 0, end - i);
                    _buffered = end - i;
                }
            }

            /// <summary>Zero-pad the message to a 16-byte boundary, as the AEAD construction requires.</summary>
            public void Pad16(long written)
            {
                int rem = (int)(written % 16);
                if (rem == 0) return;
                var zeros = new byte[16 - rem];
                Update(zeros, 0, zeros.Length);
            }

            public byte[] Final()
            {
                if (_buffered > 0)
                {
                    _buf[_buffered++] = 1;
                    while (_buffered < 16) _buf[_buffered++] = 0;
                    Absorb(_buf, 0, true);
                    _buffered = 0;
                }

                // full carry propagation
                ulong c = _h[1] >> 26; _h[1] &= 0x3ffffffu;
                _h[2] += c; c = _h[2] >> 26; _h[2] &= 0x3ffffffu;
                _h[3] += c; c = _h[3] >> 26; _h[3] &= 0x3ffffffu;
                _h[4] += c; c = _h[4] >> 26; _h[4] &= 0x3ffffffu;
                _h[0] += c * 5; c = _h[0] >> 26; _h[0] &= 0x3ffffffu;
                _h[1] += c;

                // g = h + -p, then select g if it did not borrow (i.e. h >= p)
                ulong g0 = _h[0] + 5; c = g0 >> 26; g0 &= 0x3ffffffu;
                ulong g1 = _h[1] + c; c = g1 >> 26; g1 &= 0x3ffffffu;
                ulong g2 = _h[2] + c; c = g2 >> 26; g2 &= 0x3ffffffu;
                ulong g3 = _h[3] + c; c = g3 >> 26; g3 &= 0x3ffffffu;
                ulong g4 = _h[4] + c - (1UL << 26);

                ulong mask = (g4 >> 63) - 1;                 // all ones when h >= p
                ulong nmask = ~mask;
                _h[0] = (_h[0] & nmask) | (g0 & mask);
                _h[1] = (_h[1] & nmask) | (g1 & mask);
                _h[2] = (_h[2] & nmask) | (g2 & mask);
                _h[3] = (_h[3] & nmask) | (g3 & mask);
                _h[4] = (_h[4] & nmask) | (g4 & mask);

                // serialise the low 128 bits, adding s
                ulong f0 = ((_h[0]) | (_h[1] << 26)) & 0xffffffffUL;
                ulong f1 = ((_h[1] >> 6) | (_h[2] << 20)) & 0xffffffffUL;
                ulong f2 = ((_h[2] >> 12) | (_h[3] << 14)) & 0xffffffffUL;
                ulong f3 = ((_h[3] >> 18) | (_h[4] << 8)) & 0xffffffffUL;

                var tag = new byte[16];
                ulong carry = 0;
                carry = f0 + _s[0]; Write32(tag, 0, (uint)carry); carry >>= 32;
                carry += f1 + _s[1]; Write32(tag, 4, (uint)carry); carry >>= 32;
                carry += f2 + _s[2]; Write32(tag, 8, (uint)carry); carry >>= 32;
                carry += f3 + _s[3]; Write32(tag, 12, (uint)carry);
                return tag;
            }

            static void Write32(byte[] b, int o, uint v)
            {
                b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
                b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
            }
        }

        // ---- AEAD --------------------------------------------------------------------------

        static byte[] PolyKey(byte[] key, byte[] nonce)
        {
            var block = new byte[64];
            Block(key, nonce, 0, block);
            var pk = new byte[32];
            Buffer.BlockCopy(block, 0, pk, 0, 32);
            return pk;
        }

        static byte[] Tag(byte[] polyKey, byte[] aad, byte[] cipher)
        {
            var p = new Poly1305(polyKey);
            if (aad != null && aad.Length > 0) { p.Update(aad, 0, aad.Length); p.Pad16(aad.Length); }
            if (cipher.Length > 0) { p.Update(cipher, 0, cipher.Length); p.Pad16(cipher.Length); }

            var lengths = new byte[16];
            long a = aad == null ? 0 : aad.Length, c = cipher.Length;
            for (int i = 0; i < 8; i++) lengths[i] = (byte)(a >> (i * 8));
            for (int i = 0; i < 8; i++) lengths[8 + i] = (byte)(c >> (i * 8));
            p.Update(lengths, 0, 16);
            return p.Final();
        }

        /// <summary>Returns ciphertext ‖ 16-byte tag. The nonce is the caller's to choose and to keep unique.</summary>
        public static byte[] Seal(byte[] key, byte[] nonce, byte[] plaintext, byte[] aad)
        {
            Require(key, nonce);
            var outp = new byte[plaintext.Length + TagSize];
            Buffer.BlockCopy(plaintext, 0, outp, 0, plaintext.Length);
            Xor(key, nonce, 1, outp, 0, plaintext.Length);

            var cipher = new byte[plaintext.Length];
            Buffer.BlockCopy(outp, 0, cipher, 0, cipher.Length);
            var tag = Tag(PolyKey(key, nonce), aad, cipher);
            Buffer.BlockCopy(tag, 0, outp, plaintext.Length, TagSize);
            return outp;
        }

        /// <summary>Null when the tag does not verify - a wrong key, a corrupted frame, or a stranger.</summary>
        public static byte[] Open(byte[] key, byte[] nonce, byte[] sealedBytes, byte[] aad)
        {
            Require(key, nonce);
            if (sealedBytes == null || sealedBytes.Length < TagSize) return null;

            int clen = sealedBytes.Length - TagSize;
            var cipher = new byte[clen];
            Buffer.BlockCopy(sealedBytes, 0, cipher, 0, clen);

            var expect = Tag(PolyKey(key, nonce), aad, cipher);
            if (!ConstantTimeEquals(expect, 0, sealedBytes, clen, TagSize)) return null;

            Xor(key, nonce, 1, cipher, 0, clen);
            return cipher;
        }

        static void Require(byte[] key, byte[] nonce)
        {
            if (key == null || key.Length != KeySize) throw new ArgumentException("key must be 32 bytes");
            if (nonce == null || nonce.Length != NonceSize) throw new ArgumentException("nonce must be 12 bytes");
        }

        /// <summary>Length-independent compare. The tag check must not be a timing oracle.</summary>
        public static bool ConstantTimeEquals(byte[] a, int ao, byte[] b, int bo, int count)
        {
            int diff = 0;
            for (int i = 0; i < count; i++) diff |= a[ao + i] ^ b[bo + i];
            return diff == 0;
        }
    }
}
