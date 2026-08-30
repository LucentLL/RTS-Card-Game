using System;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// Everything the shared password becomes.
    ///
    /// One secret does three jobs and they must not leak into each other: it names the public
    /// channel two strangers meet on, it seals every byte that crosses it, and it is the only
    /// thing proving the peer on the other end is the person you told the password to. So the
    /// password is stretched once, and the stretched key is then EXPANDED under distinct info
    /// strings - the topic id and the seal key are computationally unrelated, and publishing the
    /// topic (which is unavoidable; it is an HTTP path) reveals nothing about the key.
    ///
    ///   root    = PBKDF2-HMAC-SHA256(password, "srd.mp.v2", 60_000)  -> 32 bytes
    ///   topicId = HKDF-Expand(root, "topic") -> 10 bytes -> base32   (public)
    ///   sealKey = HKDF-Expand(root, "seal")  -> 32 bytes             (secret, never sent)
    ///
    /// The 60,000 iterations are not protecting a stored credential; they are making it
    /// expensive to sweep a dictionary of common passwords against harvested topic names. The
    /// cost is paid once, in the lobby, where a pause of ~100 ms is invisible.
    /// </summary>
    public sealed class PasswordChannel
    {
        /// <summary>Bumping this retires every topic and key derived from every password - which
        /// is exactly what a wire-format break needs to do, so that two incompatible builds
        /// cannot meet on the same channel at all.</summary>
        public const string Salt = "srd.mp.v2";

        public const int Iterations = 60000;

        /// <summary>10 bytes = 80 bits of topic space. Collisions between two different
        /// passwords are not a concern at 2^80; guessing a live game's topic is not either.</summary>
        public const int TopicIdBytes = 10;

        readonly byte[] _sealKey;
        readonly string _topicId;

        PasswordChannel(byte[] sealKey, string topicId)
        {
            _sealKey = sealKey;
            _topicId = topicId;
        }

        /// <summary>The 32-byte AEAD key. Never leaves the process.</summary>
        public byte[] SealKey { get { return _sealKey; } }

        /// <summary>The public channel name. Safe to log; it is a one-way function of the password.</summary>
        public string TopicId { get { return _topicId; } }

        /// <summary>The relay topic a role PUBLISHES to. The two roles never share a topic, so
        /// neither peer has to filter out its own echo - and a peer's own frames cannot be
        /// mistaken for its opponent's.</summary>
        public string TopicFor(NetRole role)
        {
            return "srd2-" + _topicId + (role == NetRole.Host ? "-h" : "-g");
        }

        /// <summary>Derive. Costs ~60,000 HMACs - call it once, off the back of a button press.</summary>
        public static PasswordChannel Derive(string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("password is empty");

            var root = Kdf.Pbkdf2(password, Utf8.Bytes(Salt), Iterations, 32);
            var topic = Kdf.Expand(root, "topic", TopicIdBytes);
            var seal = Kdf.Expand(root, "seal", ChaCha20Poly1305.KeySize);

            Array.Clear(root, 0, root.Length);
            return new PasswordChannel(seal, Base32.Encode(topic));
        }

        /// <summary>
        /// Normalise a typed password the way a human means it: trim the edges, collapse runs of
        /// whitespace, and case-fold. Two people reading a password to each other over voice must
        /// land on the same key, and "Blue Dragon" vs "blue  dragon" failing as "wrong password"
        /// is a support burden with no security benefit at all.
        /// </summary>
        public static string Normalise(string typed)
        {
            if (typed == null) return "";
            var sb = new System.Text.StringBuilder(typed.Length);
            bool space = false;
            for (int i = 0; i < typed.Length; i++)
            {
                char ch = typed[i];
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
    }

    /// <summary>Which end of the link this peer is. It decides the topic to publish on, the
    /// topic to read, and which Side the peer's engine lets it command.</summary>
    public enum NetRole : byte { Host = 0, Guest = 1 }

    /// <summary>
    /// Crockford-ish base32 (no padding, no vowels-confusable set trimming - just the standard
    /// RFC 4648 alphabet lowercased). Used for the topic name because it has to survive being an
    /// HTTP path segment, and base64's '+' and '/' do not.
    /// </summary>
    public static class Base32
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static string Encode(byte[] data)
        {
            var sb = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            for (int i = 0; i < data.Length; i++)
            {
                buffer = (buffer << 8) | data[i];
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }
            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Nonces and handshake nonces.
    ///
    /// Deliberately not System.Security.Cryptography.RandomNumberGenerator: that is exactly the
    /// platform surface this assembly avoids, and on WebGL it is the least-tested corner of the
    /// managed crypto stack. Instead: seed a SHA-256 counter stream from whatever entropy the
    /// platform will admit to having, and take output from that. Guid.NewGuid is version 4 on
    /// every runtime Unity ships, which is 122 bits of platform randomness per call.
    ///
    /// Uniqueness is what the AEAD needs from a nonce, and a counter stream cannot repeat within
    /// a session. Unpredictability comes from the seed.
    /// </summary>
    public sealed class NetRandom
    {
        readonly byte[] _seed;
        ulong _counter;

        public NetRandom() : this(PlatformSeed()) { }

        /// <summary>Deterministic construction, for tests that need a reproducible stream.</summary>
        public NetRandom(byte[] seed)
        {
            _seed = Sha256.Hash(seed);
        }

        static byte[] PlatformSeed()
        {
            var w = new ByteWriter(64);
            w.Raw(Guid.NewGuid().ToByteArray());
            w.Raw(Guid.NewGuid().ToByteArray());
            w.U64((ulong)DateTime.UtcNow.Ticks);
            w.U64((ulong)Environment.TickCount);
            return w.ToArray();
        }

        public void Fill(byte[] into)
        {
            int done = 0;
            while (done < into.Length)
            {
                var counter = new byte[8];
                ulong c = _counter++;
                for (int i = 0; i < 8; i++) counter[i] = (byte)(c >> (i * 8));

                var block = Sha256.Hash(_seed, counter);
                int take = Math.Min(block.Length, into.Length - done);
                Buffer.BlockCopy(block, 0, into, done, take);
                done += take;
            }
        }

        public byte[] Bytes(int count)
        {
            var b = new byte[count];
            Fill(b);
            return b;
        }
    }
}
