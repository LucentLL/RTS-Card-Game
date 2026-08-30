using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net
{
    public static class NetProtocol
    {
        /// <summary>
        /// Bump on ANY wire change. A mismatch is refused in the lobby naming both versions,
        /// never negotiated: two builds that disagree about the wire almost certainly disagree
        /// about the rules, and lockstep between them would desync on some later ply instead of
        /// failing where a human can read it.
        /// </summary>
        /// It leads every framed message OUTSIDE the sealed blob, so a peer from a future build
        /// can be named in an error rather than looking like noise - but it is also folded into
        /// the AEAD's associated data, so it cannot be forged.
        public const byte Version = 1;
    }

    public enum NetMessageKind : byte
    {
        Hello = 1,      // host -> guest : I am hosting, here is who I am
        Join = 2,       // guest -> host : I am here, here is who I am
        Start = 3,      // host -> guest : the agreed match, build it
        Frame = 4,      // either        : one command at one ply
        Ping = 5,       // either        : still here
        Bye = 6,        // either        : leaving on purpose
        Refuse = 7,     // either        : I cannot play with you, and why
        Max = 8,
    }

    /// <summary>Why a peer refused. Each one is a DIFFERENT sentence in the lobby - "wrong
    /// password" and "your cards differ from mine" and "someone is already in this game" are
    /// three completely different things for the person trying to play.</summary>
    public enum RefuseCode : byte
    {
        None = 0,
        ProtocolMismatch = 1,
        CatalogMismatch = 2,
        AlreadyPaired = 3,
        OpeningHashMismatch = 4,
        Desync = 5,
        UnknownSession = 6,
        Max = 7,
    }

    /// <summary>
    /// Everything both peers need to build byte-identical opening states. Deliberately the exact
    /// argument list of MatchSetup.NewMatch: if this record round-trips, two engines cannot start
    /// from different boards.
    ///
    /// The host is Side.You and the guest is Side.Foe IN BOTH ENGINES. There is no perspective
    /// swap on the wire; the view takes a seat instead (design 04 s6).
    /// </summary>
    public sealed class MatchConfig
    {
        public CommanderId HostCommander;
        public CommanderId GuestCommander;
        public List<HandCard> HostDeck;      // null == roll from the commander's pools
        public List<HandCard> GuestDeck;
        public ulong Seed;
        public int FlagBits;
        public ulong CatalogFingerprint;

        public GameState Build(ICardCatalog cat)
        {
            RulesOptions options;
            if (!RulesOptions.TryFromFlagBits(FlagBits, out options))
                throw new WireFormatException("unknown rules flags: " + FlagBits);

            return MatchSetup.NewMatch(cat, HostCommander, GuestCommander,
                                       HostDeck, GuestDeck, Seed, options);
        }

        /// <summary>Which Side a role plays. Fixed, and the reason no command needs remapping.</summary>
        public static Side SideOf(NetRole role)
        {
            return role == NetRole.Host ? Side.You : Side.Foe;
        }

        public static NetRole RoleOf(Side side)
        {
            return side == Side.You ? NetRole.Host : NetRole.Guest;
        }
    }

    /// <summary>
    /// One message. A plain mutable bag rather than a class hierarchy: it is read from untrusted
    /// bytes exactly once, at the session boundary, and every consumer switches on Kind anyway.
    /// </summary>
    public sealed class NetMessage
    {
        public NetMessageKind Kind;

        /// <summary>
        /// Minted by the host when it starts hosting, echoed by everyone else. It is what tells
        /// this match's frames from those of any other game on the same password - a stranger
        /// replaying captured frames, or the tail of a previous duel still in flight - and a
        /// frame carrying the wrong one is dropped before it is interpreted.
        /// </summary>
        public ulong SessionId;

        // ---- Hello / Join -------------------------------------------------------------------
        public byte[] Nonce;                 // 16 bytes; both halves seed the match RNG
        public CommanderId Commander;
        public List<HandCard> Deck;          // null == let the commander roll one
        public int FlagBits;
        public ulong CatalogFingerprint;
        public string DisplayName;

        // ---- Start --------------------------------------------------------------------------
        public MatchConfig Config;
        public ulong OpeningHash;

        // ---- Frame --------------------------------------------------------------------------
        /// <summary>
        /// The GLOBAL command counter before this command applies - not a per-sender sequence.
        /// The rules let exactly one side act at a time, so one counter totally orders the whole
        /// match across both peers. That is what makes a reordering relay safe (hold anything
        /// ahead of the cursor) and what makes handing a reconnected peer the whole log a
        /// replay rather than a merge. On a Hello, Join or Ping it means something adjacent:
        /// how far along the sender already is.
        /// </summary>
        public int Ply;
        public ulong HashBefore;
        public ICommand Command;

        // ---- Refuse / Bye -------------------------------------------------------------------
        public RefuseCode Code;
        public string Text;

        // ---- codec --------------------------------------------------------------------------

        public void Write(ByteWriter w, CardRegistry registry)
        {
            w.Byte((byte)Kind);
            w.U64(SessionId);

            switch (Kind)
            {
                case NetMessageKind.Hello:
                case NetMessageKind.Join:
                    w.Bytes(Nonce);
                    w.String(Commander.Value);
                    if (Deck == null) w.VarInt(0); else registry.WriteDeck(w, Deck);
                    w.Int(FlagBits);
                    w.U64(CatalogFingerprint);
                    w.String(DisplayName);
                    w.Int(Ply);          // how far along we already are, if we are reconnecting
                    break;

                case NetMessageKind.Start:
                    w.String(DisplayName);
                    w.Int(Ply);          // 0 for a new match; the log length when restoring one
                    w.String(Config.HostCommander.Value);
                    w.String(Config.GuestCommander.Value);
                    if (Config.HostDeck == null) w.VarInt(0); else registry.WriteDeck(w, Config.HostDeck);
                    if (Config.GuestDeck == null) w.VarInt(0); else registry.WriteDeck(w, Config.GuestDeck);
                    w.U64(Config.Seed);
                    w.Int(Config.FlagBits);
                    w.U64(Config.CatalogFingerprint);
                    w.U64(OpeningHash);
                    break;

                case NetMessageKind.Frame:
                    w.Int(Ply);
                    w.U64(HashBefore);
                    CommandCodec.Write(w, Command);
                    break;

                case NetMessageKind.Ping:
                    w.Int(Ply);                 // the sender's view of the log's length
                    break;

                case NetMessageKind.Bye:
                case NetMessageKind.Refuse:
                    w.Byte((byte)Code);
                    w.String(Text);
                    break;

                default:
                    throw new WireFormatException("unwritable message kind " + Kind);
            }
        }

        public static NetMessage Read(ByteReader r, CardRegistry registry)
        {
            var m = new NetMessage();
            m.Kind = (NetMessageKind)r.Enum((int)NetMessageKind.Max, "message kind");
            m.SessionId = r.U64();

            switch (m.Kind)
            {
                case NetMessageKind.Hello:
                case NetMessageKind.Join:
                    m.Nonce = r.Bytes();
                    m.Commander = new CommanderId(r.String());
                    m.Deck = registry.ReadDeck(r);
                    m.FlagBits = r.Int();
                    m.CatalogFingerprint = r.U64();
                    m.DisplayName = r.String();
                    m.Ply = r.Int();
                    if (m.Nonce.Length != HandshakeNonceBytes)
                        throw new WireFormatException("bad handshake nonce length");
                    if (m.Ply < 0) throw new WireFormatException("negative ply");
                    break;

                case NetMessageKind.Start:
                    m.DisplayName = r.String();
                    m.Ply = r.Int();
                    if (m.Ply < 0) throw new WireFormatException("negative ply");
                    m.Config = new MatchConfig();
                    m.Config.HostCommander = new CommanderId(r.String());
                    m.Config.GuestCommander = new CommanderId(r.String());
                    m.Config.HostDeck = registry.ReadDeck(r);
                    m.Config.GuestDeck = registry.ReadDeck(r);
                    m.Config.Seed = r.U64();
                    m.Config.FlagBits = r.Int();
                    m.Config.CatalogFingerprint = r.U64();
                    m.OpeningHash = r.U64();
                    break;

                case NetMessageKind.Frame:
                    m.Ply = r.Int();
                    m.HashBefore = r.U64();
                    m.Command = CommandCodec.Read(r);
                    if (m.Ply < 0) throw new WireFormatException("negative ply");
                    break;

                case NetMessageKind.Ping:
                    m.Ply = r.Int();
                    break;

                case NetMessageKind.Bye:
                case NetMessageKind.Refuse:
                    m.Code = (RefuseCode)r.Enum((int)RefuseCode.Max, "refuse code");
                    m.Text = r.String();
                    break;

                default:
                    throw new WireFormatException("unreadable message kind " + m.Kind);
            }
            return m;
        }

        public const int HandshakeNonceBytes = 16;
    }

    /// <summary>
    /// The sealed frame: version byte, nonce, ciphertext+tag, all base64url. Nothing outside the
    /// tag is trusted, and nothing inside it can be read without the password.
    /// </summary>
    public static class NetEnvelope
    {
        public static string Seal(PasswordChannel channel, NetRole senderRole, NetMessage message,
                                  CardRegistry registry, NetRandom random)
        {
            var body = new ByteWriter(96);
            message.Write(body, registry);

            var nonce = random.Bytes(ChaCha20Poly1305.NonceSize);
            var cipher = ChaCha20Poly1305.Seal(channel.SealKey, nonce, body.ToArray(),
                                               Aad(senderRole));

            var frame = new ByteWriter(cipher.Length + 16);
            frame.Byte(NetProtocol.Version);
            frame.Raw(nonce);
            frame.Raw(cipher);
            return Base64Url.Encode(frame.ToArray());
        }

        /// <summary>
        /// Null for anything that is not a message from a peer holding this password: noise on a
        /// public topic, a wrong password, a truncated frame, a replay onto the wrong channel.
        /// The caller reports "wrong password" only after several failures, because one failure
        /// is far more likely to be a stranger than a typo.
        /// </summary>
        public static NetMessage Open(PasswordChannel channel, NetRole senderRole, string text,
                                      CardRegistry registry, out byte peerVersion)
        {
            peerVersion = 0;

            var frame = Base64Url.Decode(text);
            if (frame == null || frame.Length < 1 + ChaCha20Poly1305.NonceSize + ChaCha20Poly1305.TagSize)
                return null;

            peerVersion = frame[0];

            var nonce = new byte[ChaCha20Poly1305.NonceSize];
            System.Buffer.BlockCopy(frame, 1, nonce, 0, ChaCha20Poly1305.NonceSize);

            int cipherLen = frame.Length - 1 - ChaCha20Poly1305.NonceSize;
            var cipher = new byte[cipherLen];
            System.Buffer.BlockCopy(frame, 1 + ChaCha20Poly1305.NonceSize, cipher, 0, cipherLen);

            // The version byte is authenticated through the AAD, so a peer cannot lie about it
            // to make us mis-parse; a mismatch fails the tag and reads as "not for us".
            var plain = ChaCha20Poly1305.Open(channel.SealKey, nonce, cipher, Aad(senderRole, peerVersion));
            if (plain == null) return null;

            try
            {
                return NetMessage.Read(new ByteReader(plain), registry);
            }
            catch (WireFormatException)
            {
                return null;      // authenticated but unparseable: a build skew, not an attack
            }
        }

        /// <summary>
        /// Associated data binds a frame to the ROLE that published it and to the protocol
        /// version. Without the role, a frame lifted off the host topic could be replayed onto
        /// the guest topic and would authenticate perfectly.
        /// </summary>
        static byte[] Aad(NetRole role) { return Aad(role, NetProtocol.Version); }

        static byte[] Aad(NetRole role, byte version)
        {
            return new byte[] { (byte)'s', (byte)'r', (byte)'d', version, (byte)role };
        }
    }
}
