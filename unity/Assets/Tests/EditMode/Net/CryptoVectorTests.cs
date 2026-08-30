using System;
using NUnit.Framework;

namespace SpawnRowDuel.Net.Tests
{
    /// <summary>
    /// The netcode's crypto is hand-written (design 04 s2.1), so it is pinned to published test
    /// vectors rather than to itself. Every expectation below was produced independently - the
    /// SHA/HMAC/PBKDF2 lines from Python's hashlib, the AEAD lines from a reference
    /// implementation cross-checked against the tag published in RFC 8439 s2.8.2
    /// (1ae10b594f09e26a7e902ecbd0600691).
    ///
    /// A round-trip test would pass with a completely wrong cipher. These would not.
    /// </summary>
    public class CryptoVectorTests
    {
        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        static byte[] FromHex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        static byte[] Ascii(string s) { return Utf8.Bytes(s); }

        // ---- SHA-256 (RFC 6234) --------------------------------------------------------------

        [Test]
        public void Sha256_MatchesPublishedVectors()
        {
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                            Hex(Sha256.Hash(new byte[0])), "empty");

            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                            Hex(Sha256.Hash(Ascii("abc"))), "abc");

            Assert.AreEqual("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
                            Hex(Sha256.Hash(Ascii(
                                "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"))),
                            "two-block message");
        }

        /// <summary>1000 bytes crosses many blocks and exercises the streaming buffer, which is
        /// where a hand-written padding bug hides.</summary>
        [Test]
        public void Sha256_LongMessage_MatchesVector()
        {
            var a = new byte[1000];
            for (int i = 0; i < a.Length; i++) a[i] = (byte)'a';
            Assert.AreEqual("41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3",
                            Hex(Sha256.Hash(a)));
        }

        /// <summary>Fed in awkward slices, the streaming path must agree with the one-shot path.</summary>
        [Test]
        public void Sha256_Streaming_AgreesWithOneShot()
        {
            var data = new byte[517];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7);

            var oneShot = Sha256.Hash(data);

            var ctx = Sha256.Context.Create();
            int pos = 0;
            int[] chunks = { 1, 63, 64, 65, 2, 100, 0, 222 };
            for (int i = 0; i < chunks.Length && pos < data.Length; i++)
            {
                int take = Math.Min(chunks[i], data.Length - pos);
                Sha256.Update(ref ctx, data, pos, take);
                pos += take;
            }
            Sha256.Update(ref ctx, data, pos, data.Length - pos);

            var streamed = new byte[Sha256.HashSize];
            Sha256.Final(ref ctx, streamed, 0);

            Assert.AreEqual(Hex(oneShot), Hex(streamed));
        }

        // ---- HMAC (RFC 4231) -----------------------------------------------------------------

        [Test]
        public void HmacSha256_MatchesPublishedVectors()
        {
            var key = new byte[20];
            for (int i = 0; i < key.Length; i++) key[i] = 0x0b;
            Assert.AreEqual("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
                            Hex(HmacSha256.Mac(key, Ascii("Hi There"))), "case 1");

            Assert.AreEqual("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
                            Hex(HmacSha256.Mac(Ascii("Jefe"), Ascii("what do ya want for nothing?"))),
                            "case 2");
        }

        // ---- PBKDF2 --------------------------------------------------------------------------

        [Test]
        public void Pbkdf2_MatchesPublishedVectors()
        {
            Assert.AreEqual("120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b",
                            Hex(Kdf.Pbkdf2("password", Ascii("salt"), 1, 32)), "c=1");

            Assert.AreEqual("ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43",
                            Hex(Kdf.Pbkdf2("password", Ascii("salt"), 2, 32)), "c=2");

            Assert.AreEqual("c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a",
                            Hex(Kdf.Pbkdf2("password", Ascii("salt"), 4096, 32)), "c=4096");
        }

        /// <summary>40 bytes forces a second PBKDF2 block, where the counter suffix must advance.</summary>
        [Test]
        public void Pbkdf2_MultiBlockOutput_MatchesVector()
        {
            Assert.AreEqual("348c89dbcbd32b2f32d814b8116e84cf2b17347ebc1800181c4e2a1fb8dd53e1c635518c7dac47e9",
                            Hex(Kdf.Pbkdf2("passwordPASSWORDpassword",
                                           Ascii("saltSALTsaltSALTsaltSALTsaltSALTsalt"), 4096, 40)));
        }

        // ---- HKDF ----------------------------------------------------------------------------

        [Test]
        public void HkdfExpand_MatchesVectors()
        {
            var prk = new byte[32];
            for (int i = 0; i < prk.Length; i++) prk[i] = (byte)i;

            Assert.AreEqual("c6bdfe61eb273401b3de", Hex(Kdf.Expand(prk, "topic", 10)));
            Assert.AreEqual("8c78f9cf91c31b4d28f758b815a84e18ec2e8bde0a17daae62390fe011c51a7d",
                            Hex(Kdf.Expand(prk, "seal", 32)));
            Assert.AreEqual("8c78f9cf91c31b4d28f758b815a84e18ec2e8bde0a17daae62390fe011c51a7d"
                            + "27398fbbb8c0f15bf8f01f1dca3009b9",
                            Hex(Kdf.Expand(prk, "seal", 48)), "second output block");
        }

        /// <summary>Domain separation is the whole point: the public topic name must reveal
        /// nothing about the secret key derived from the same password.</summary>
        [Test]
        public void HkdfExpand_DifferentInfo_DifferentOutput()
        {
            var prk = new byte[32];
            for (int i = 0; i < prk.Length; i++) prk[i] = (byte)i;
            Assert.AreNotEqual(Hex(Kdf.Expand(prk, "topic", 32)), Hex(Kdf.Expand(prk, "seal", 32)));
        }

        // ---- ChaCha20-Poly1305 (RFC 8439) ----------------------------------------------------

        [Test]
        public void Aead_MatchesRfc8439WorkedExample()
        {
            var key = new byte[32];
            for (int i = 0; i < 32; i++) key[i] = (byte)(0x80 + i);
            var nonce = FromHex("070000004041424344454647");
            var aad = FromHex("50515253c0c1c2c3c4c5c6c7");
            var plain = Ascii("Ladies and Gentlemen of the class of '99: If I could offer you "
                            + "only one tip for the future, sunscreen would be it.");

            var sealedBytes = ChaCha20Poly1305.Seal(key, nonce, plain, aad);

            Assert.AreEqual("d31a8d34648e60db7b86afbc53ef7ec2",
                            Hex(sealedBytes).Substring(0, 32), "ciphertext head");

            var tag = new byte[16];
            Buffer.BlockCopy(sealedBytes, sealedBytes.Length - 16, tag, 0, 16);
            Assert.AreEqual("1ae10b594f09e26a7e902ecbd0600691", Hex(tag), "the RFC's published tag");
        }

        [Test]
        public void Aead_ShortCases_MatchReference()
        {
            var key = new byte[32];
            for (int i = 0; i < 32; i++) key[i] = (byte)i;
            var nonce = new byte[12];
            for (int i = 0; i < 12; i++) nonce[i] = (byte)i;

            Assert.AreEqual("295a498b8841a1c5f55d4d606f731159",
                            Hex(ChaCha20Poly1305.Seal(key, nonce, new byte[0], new byte[0])),
                            "empty plaintext, empty aad");

            Assert.AreEqual("e19243e4e0543237bc1287606287851a396a",
                            Hex(ChaCha20Poly1305.Seal(key, nonce, Ascii("hi"), new byte[0])));

            Assert.AreEqual("e19e646c4637d22fc5ef5ba8eef2fc80e50f9148068b423945e492",
                            Hex(ChaCha20Poly1305.Seal(key, nonce, Ascii("hello world"), Ascii("aad"))));

            var block = new byte[64];
            for (int i = 0; i < 64; i++) block[i] = (byte)i;
            var aad5 = new byte[5];
            for (int i = 0; i < 5; i++) aad5[i] = (byte)i;
            Assert.AreEqual(
                "89fa0a032d12a347bf8a35f89410006cd961a0f44561bbaefe8e35de69ddb823"
                + "cca10ed0c23b97bf1f1b5cf349b9a10c4eb59b47c91d8eac2a81e33cac72a0e9"
                + "28e41cfad88ab4a53a8aefff46da1ccd",
                Hex(ChaCha20Poly1305.Seal(key, nonce, block, aad5)), "exactly one block");
        }

        [Test]
        public void Aead_Open_RoundTrips()
        {
            var key = new byte[32];
            var nonce = new byte[12];
            for (int i = 0; i < 32; i++) key[i] = (byte)(i * 3);
            for (int i = 0; i < 12; i++) nonce[i] = (byte)(i * 5);

            var plain = Ascii("a whole command frame, more or less");
            var aad = Ascii("srd|1|host");

            var sealedBytes = ChaCha20Poly1305.Seal(key, nonce, plain, aad);
            var opened = ChaCha20Poly1305.Open(key, nonce, sealedBytes, aad);

            Assert.IsNotNull(opened);
            Assert.AreEqual(Hex(plain), Hex(opened));
        }

        [Test]
        public void Aead_RejectsWrongKey_WrongAad_AndTampering()
        {
            var key = new byte[32];
            var other = new byte[32];
            var nonce = new byte[12];
            for (int i = 0; i < 32; i++) { key[i] = (byte)i; other[i] = (byte)(i + 1); }

            var plain = Ascii("the guest declares an attack");
            var aad = Ascii("srd|1|guest");
            var sealedBytes = ChaCha20Poly1305.Seal(key, nonce, plain, aad);

            Assert.IsNull(ChaCha20Poly1305.Open(other, nonce, sealedBytes, aad), "wrong key");
            Assert.IsNull(ChaCha20Poly1305.Open(key, nonce, sealedBytes, Ascii("srd|1|host")),
                          "aad bound to the other role");

            var tampered = (byte[])sealedBytes.Clone();
            tampered[3] ^= 0x01;
            Assert.IsNull(ChaCha20Poly1305.Open(key, nonce, tampered, aad), "flipped ciphertext bit");

            var shortened = new byte[8];
            Assert.IsNull(ChaCha20Poly1305.Open(key, nonce, shortened, aad), "too short to hold a tag");
        }

        // ---- the derivation the product actually uses -----------------------------------------

        [Test]
        public void PasswordChannel_DerivesTheExpectedTopicAndKey()
        {
            var c = PasswordChannel.Derive("blue dragon");

            Assert.AreEqual("gg3cpkwkozftgkjt", c.TopicId);
            Assert.AreEqual("f1c2a0c843ab1bb305800ae746245dd09d2f16d0ee1a5f4aa30a8fad28eab902",
                            Hex(c.SealKey));
            Assert.AreEqual("srd2-gg3cpkwkozftgkjt-h", c.TopicFor(NetRole.Host));
            Assert.AreEqual("srd2-gg3cpkwkozftgkjt-g", c.TopicFor(NetRole.Guest));
        }

        [Test]
        public void PasswordChannel_NormalisesTheWayPeopleType()
        {
            Assert.AreEqual("blue dragon", PasswordChannel.Normalise("  Blue   DRAGON "));
            Assert.AreEqual("blue dragon", PasswordChannel.Normalise("blue\tdragon"));
            Assert.AreEqual("", PasswordChannel.Normalise("   "));

            // and therefore two people who typed it differently land on the same channel
            Assert.AreEqual(PasswordChannel.Derive(PasswordChannel.Normalise("Blue Dragon")).TopicId,
                            PasswordChannel.Derive(PasswordChannel.Normalise("blue  dragon")).TopicId);
        }

        [Test]
        public void PasswordChannel_DifferentPasswords_DifferentTopics()
        {
            Assert.AreNotEqual(PasswordChannel.Derive("one").TopicId,
                               PasswordChannel.Derive("two").TopicId);
        }

        // ---- base64url -------------------------------------------------------------------------

        [Test]
        public void Base64Url_RoundTripsEveryLengthRemainder()
        {
            for (int n = 0; n < 40; n++)
            {
                var data = new byte[n];
                for (int i = 0; i < n; i++) data[i] = (byte)(i * 37 + n);

                var text = Base64Url.Encode(data);
                Assert.IsFalse(text.Contains("+") || text.Contains("/") || text.Contains("="),
                               "url-safe alphabet at length " + n);

                var back = Base64Url.Decode(text);
                if (n == 0) { Assert.IsTrue(back == null || back.Length == 0); continue; }
                Assert.AreEqual(Hex(data), Hex(back), "round trip at length " + n);
            }
        }

        [Test]
        public void Base64Url_RejectsGarbage()
        {
            Assert.IsNull(Base64Url.Decode("!!!!"));
            Assert.IsNull(Base64Url.Decode("A"), "a single character cannot be a whole byte");
        }

        // ---- the nonce source -------------------------------------------------------------------

        [Test]
        public void NetRandom_DoesNotRepeat()
        {
            var r = new NetRandom(Ascii("fixed seed"));
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 2000; i++)
                Assert.IsTrue(seen.Add(Hex(r.Bytes(12))), "nonce repeated after " + i + " draws");
        }

        [Test]
        public void NetRandom_SameSeed_SameStream()
        {
            var a = new NetRandom(Ascii("seed"));
            var b = new NetRandom(Ascii("seed"));
            Assert.AreEqual(Hex(a.Bytes(64)), Hex(b.Bytes(64)));
        }
    }
}
