using System;
using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net
{
    public enum SessionPhase : byte
    {
        Idle = 0,
        Advertising = 1,    // host, offering a game
        Waiting = 2,        // guest, looking for one
        Playing = 3,
        Ended = 4,          // the match finished, or the peer left on purpose
        Failed = 5,         // refused, desynced, or the relay is unreachable
    }

    public sealed class DesyncReport
    {
        public int Ply;
        public ulong Expected;      // what the sender said the state was
        public ulong Actual;        // what ours actually is
        public string Command;
        public Rejection Rejection; // set when the divergence showed up as a refused command

        public override string ToString()
        {
            return Rejection != Rejection.None
                ? "ply " + Ply + ": the opponent's " + Command + " was rejected here (" + Rejection + ")"
                : "ply " + Ply + ": state diverged before " + Command
                  + " (theirs " + Expected.ToString("x16") + ", ours " + Actual.ToString("x16") + ")";
        }
    }

    /// <summary>
    /// The whole protocol: find each other on a password, agree a match, then keep two engines
    /// bit-identical by exchanging commands.
    ///
    /// It owns the DuelEngine, because owning it is what lets the entire thing - handshake, a
    /// 400-ply match, a dropped peer, a reconnect, a deliberate desync - run headless in the
    /// EditMode gate against LoopbackTransport. The view adopts <see cref="Engine"/> and submits
    /// through <see cref="Submit"/>; it never applies a command directly.
    ///
    /// Deliberately: no threads, no async, no wall clock. Pump(dt) is the only thing that moves
    /// time, so a test can play a whole match in a millisecond and a frame-rate hitch cannot
    /// change protocol behaviour.
    /// </summary>
    public sealed class NetSession : IDisposable
    {
        // ---- knobs (seconds) ----------------------------------------------------------------
        public const double AdvertiseInterval = 1.5;
        public const double PingInterval = 10.0;
        public const double SilenceTimeout = 30.0;

        readonly NetRole _role;
        readonly PasswordChannel _channel;
        readonly CardRegistry _registry;
        readonly IMessageTransport _transport;
        readonly ICardCatalog _catalog;
        readonly NetRandom _random;

        readonly string _myTopic;
        readonly string _peerTopic;

        // handshake material
        ulong _sessionId;
        byte[] _myNonce;
        byte[] _peerNonce;
        CommanderId _myCommander;
        List<HandCard> _myDeck;
        string _myName, _peerName;
        int _flagBits;

        MatchConfig _config;
        ulong _openingHash;

        // clocks
        double _clock;
        double _nextAdvertise;
        double _nextPing;
        double _lastHeard;

        /// <summary>
        /// Every command applied, in order, so this peer can hand the whole match to one that
        /// reconnected. The relay does not remember anything - a public MQTT broker delivers to
        /// whoever is connected right now and forgets - so the peers remember for it. At about
        /// fifteen bytes a frame a four-hundred-ply match is six kilobytes; keeping it is free
        /// and it removes the last dependency on any particular relay's cache behaviour.
        /// </summary>
        readonly List<NetMessage> _log = new List<NetMessage>();

        /// <summary>Frames that arrived ahead of their turn - the relay may reorder.</summary>
        readonly Dictionary<int, NetMessage> _future = new Dictionary<int, NetMessage>();

        int _badFrames;

        public NetSession(NetRole role, PasswordChannel channel, ICardCatalog catalog,
                          IMessageTransport transport, NetRandom random)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (transport == null) throw new ArgumentNullException("transport");

            _role = role;
            _channel = channel;
            _catalog = catalog;
            _transport = transport;
            _random = random ?? new NetRandom();
            _registry = new CardRegistry(catalog);

            _myTopic = channel.TopicFor(role);
            _peerTopic = channel.TopicFor(role == NetRole.Host ? NetRole.Guest : NetRole.Host);
        }

        // ---- what the view reads --------------------------------------------------------------

        public NetRole Role { get { return _role; } }
        public SessionPhase Phase { get; private set; }
        public DuelEngine Engine { get; private set; }
        public DesyncReport Desync { get; private set; }
        public RefuseCode RefusedBecause { get; private set; }
        public IMessageTransport Transport { get { return _transport; } }

        /// <summary>The Side this peer is allowed to command. Host is You, guest is Foe, in both
        /// engines - there is no mirroring anywhere in this layer.</summary>
        public Side LocalSide { get { return MatchConfig.SideOf(_role); } }

        public Side RemoteSide
        {
            get { return MatchConfig.SideOf(_role == NetRole.Host ? NetRole.Guest : NetRole.Host); }
        }

        /// <summary>Commands applied so far, by either peer. Also the next frame's ply.</summary>
        public int Ply { get { return _log.Count; } }

        public string PeerName { get { return _peerName; } }

        /// <summary>Seconds since anything arrived from the peer. The HUD says "waiting" versus
        /// "they may have dropped" off this.</summary>
        public double SilentFor { get { return Phase == SessionPhase.Playing ? _clock - _lastHeard : 0.0; } }

        public bool PeerSilent { get { return SilentFor > SilenceTimeout; } }

        /// <summary>One line, ready to show. The lobby has nothing else to say.</summary>
        public string Status { get; private set; }

        /// <summary>Raised after every command that lands, whoever sent it. The view repaints.</summary>
        public event Action<ICommand, Side> Applied;

        /// <summary>Raised when a match is built or rebuilt - the view must re-adopt Engine.</summary>
        public event Action MatchBegun;

        // ---- starting ---------------------------------------------------------------------------

        public void Begin(CommanderId commander, List<HandCard> deck, string displayName, int flagBits)
        {
            _myCommander = commander;
            _myDeck = deck;
            _myName = string.IsNullOrEmpty(displayName)
                ? (_role == NetRole.Host ? "Host" : "Guest") : displayName;
            _flagBits = flagBits;
            _myNonce = _random.Bytes(NetMessage.HandshakeNonceBytes);
            _sessionId = _role == NetRole.Host ? NewSessionId() : 0UL;

            _transport.Subscribe(_peerTopic);

            Phase = _role == NetRole.Host ? SessionPhase.Advertising : SessionPhase.Waiting;
            Status = _role == NetRole.Host
                ? "Waiting for your opponent to join..."
                : "Looking for the host...";
            _nextAdvertise = 0.0;
        }

        // ---- the pump ---------------------------------------------------------------------------

        public void Pump(double deltaSeconds)
        {
            if (Phase == SessionPhase.Idle || Phase == SessionPhase.Failed) return;

            _clock += deltaSeconds;
            _transport.Pump(deltaSeconds);

            var inbox = _transport.Poll();
            for (int i = 0; i < inbox.Count; i++) Receive(inbox[i]);

            switch (Phase)
            {
                case SessionPhase.Advertising: TickAdvertise(); break;
                case SessionPhase.Waiting: TickWaiting(); break;
                case SessionPhase.Playing: TickPlaying(); break;
            }
        }

        void TickAdvertise()
        {
            if (_clock < _nextAdvertise) return;
            _nextAdvertise = _clock + AdvertiseInterval;

            // Re-published rather than sent once. The relay is a live broker, not a mailbox: it
            // delivers to whoever is connected at that instant and remembers nothing, so an offer
            // sent before the guest arrived was never heard by anyone.
            Send(Introduction(NetMessageKind.Hello));
        }

        void TickWaiting()
        {
            if (_clock < _nextAdvertise) return;
            _nextAdvertise = _clock + AdvertiseInterval;

            // The guest announces itself too, rather than waiting mutely for a Hello. That is
            // what lets a host who reloaded find a guest who is still mid-match: the guest's Join
            // reaches it, and the guest sends the whole game back (see OnJoin / RestoreTo).
            Send(Introduction(NetMessageKind.Join));
        }

        NetMessage Introduction(NetMessageKind kind)
        {
            var m = Msg(kind);
            m.Nonce = _myNonce;
            m.Commander = _myCommander;
            m.Deck = _myDeck;
            m.FlagBits = _flagBits;
            m.CatalogFingerprint = _registry.Fingerprint;
            m.DisplayName = _myName;
            m.Ply = Ply;
            return m;
        }

        void TickPlaying()
        {
            if (_clock >= _nextPing)
            {
                _nextPing = _clock + PingInterval;
                var ping = Msg(NetMessageKind.Ping);
                ping.Ply = Ply;
                Send(ping);
            }

            if (Engine.State.IsOver && Phase == SessionPhase.Playing)
            {
                Phase = SessionPhase.Ended;
                Status = "The match is over.";
            }
        }

        // ---- receiving ---------------------------------------------------------------------------

        void Receive(InboundMessage inbound)
        {
            // A message on our own publish topic is our own echo - some brokers loop them back.
            if (inbound.Topic == _myTopic) return;

            byte peerVersion;
            var senderRole = _role == NetRole.Host ? NetRole.Guest : NetRole.Host;

            var m = NetEnvelope.Open(_channel, senderRole, inbound.Text, _registry, out peerVersion);
            if (m == null)
            {
                if (peerVersion != 0 && peerVersion != NetProtocol.Version)
                {
                    Fail(RefuseCode.ProtocolMismatch,
                         "Your opponent is running a different version of the game (wire "
                         + peerVersion + ", yours " + NetProtocol.Version + ").");
                    return;
                }
                // Could be a stranger on a public topic, could be the wrong password. Only say so
                // after a run of them - one failure is far likelier to be noise than a typo.
                if (Phase == SessionPhase.Waiting || Phase == SessionPhase.Advertising)
                {
                    if (++_badFrames == 8)
                        Status = "Someone is on this password but the messages will not open - "
                               + "check you both typed exactly the same thing.";
                }
                return;
            }

            _badFrames = 0;
            _lastHeard = _clock;

            switch (m.Kind)
            {
                case NetMessageKind.Hello: OnIntroduction(m); break;
                case NetMessageKind.Join: OnIntroduction(m); break;
                case NetMessageKind.Start: OnStart(m); break;
                case NetMessageKind.Frame: OnFrame(m); break;
                case NetMessageKind.Ping: OnPing(m); break;
                case NetMessageKind.Bye: OnBye(m); break;
                case NetMessageKind.Refuse: OnRefuse(m); break;
            }
        }

        /// <summary>
        /// Hello and Join are the same message in opposite directions, and are handled the same
        /// way, which is what makes reconnection symmetric: whoever still holds a live match
        /// answers an introduction by handing the whole match over. It does not matter which end
        /// reloaded.
        /// </summary>
        void OnIntroduction(NetMessage m)
        {
            // The host's Hello must reach a waiting guest and the guest's Join must reach a
            // waiting host; a peer never answers its own kind.
            bool fromTheOtherEnd = (_role == NetRole.Host && m.Kind == NetMessageKind.Join)
                                || (_role == NetRole.Guest && m.Kind == NetMessageKind.Hello);
            if (!fromTheOtherEnd) return;

            if (Phase == SessionPhase.Playing || Phase == SessionPhase.Ended)
            {
                // They have forgotten a game we are still holding: give it back, whole.
                RestoreTo(m);
                return;
            }

            if (Phase != SessionPhase.Advertising && Phase != SessionPhase.Waiting) return;
            if (!Compatible(m)) return;

            _peerNonce = m.Nonce;
            _peerName = m.DisplayName;

            if (_role != NetRole.Host)
            {
                // The guest waits to be told; the host is the one that mints the match. Adopting
                // the session id here means the guest's next Join carries it, which is how the
                // host tells its own opponent's frames from a stranger's.
                _sessionId = m.SessionId;
                Status = "Found " + _peerName + " - joining...";
                _nextAdvertise = 0.0;
                return;
            }

            var config = new MatchConfig();
            config.HostCommander = _myCommander;
            config.GuestCommander = m.Commander;
            config.HostDeck = _myDeck;
            config.GuestDeck = m.Deck;
            config.FlagBits = _flagBits;
            config.CatalogFingerprint = _registry.Fingerprint;
            config.Seed = SeedFrom(_myNonce, _peerNonce);

            GameState state;
            try { state = config.Build(_catalog); }
            catch (WireFormatException e) { Refuse(RefuseCode.CatalogMismatch, e.Message); return; }

            _config = config;
            Adopt(new DuelEngine(state, _catalog), 0);
            _openingHash = Engine.Hash();
            Status = "Playing " + _peerName + ".";

            Send(StartMessage());
        }

        NetMessage StartMessage()
        {
            var start = Msg(NetMessageKind.Start);
            start.Config = _config;
            start.OpeningHash = _openingHash;
            start.DisplayName = _myName;
            start.Ply = Ply;                 // 0 for a new match; the log length when restoring
            return start;
        }

        /// <summary>
        /// Hand a peer the entire match: the agreed setup, then every command that has been
        /// played, in order. Their frame buffer sorts it out - a replayed log and a reordered
        /// relay are the same problem, and it was already solved.
        /// </summary>
        void RestoreTo(NetMessage introduction)
        {
            if (_config == null || Engine == null) return;

            Send(StartMessage());
            for (int i = 0; i < _log.Count; i++) Send(_log[i]);

            _peerName = string.IsNullOrEmpty(introduction.DisplayName) ? _peerName
                                                                      : introduction.DisplayName;
            Status = "Your opponent reconnected.";
        }

        void OnStart(NetMessage m)
        {
            // Accepted in EITHER role: usually the host telling the guest the match has begun,
            // but also the surviving peer handing a reloaded one its game back.
            if (Phase == SessionPhase.Playing && m.SessionId == _sessionId) return;   // a re-send
            if (Phase != SessionPhase.Advertising && Phase != SessionPhase.Waiting) return;

            GameState state;
            try { state = m.Config.Build(_catalog); }
            catch (WireFormatException e) { Refuse(RefuseCode.CatalogMismatch, e.Message); return; }

            var engine = new DuelEngine(state, _catalog);
            ulong mine = engine.Hash();
            if (mine != m.OpeningHash)
            {
                // Same cards, same seed, different board. That is a build difference, and playing
                // on would desync at some later ply where it is far harder to read.
                Refuse(RefuseCode.OpeningHashMismatch,
                       "You and your opponent built different starting boards - the two installs "
                       + "are not the same build.");
                return;
            }

            _sessionId = m.SessionId;
            _config = m.Config;
            _openingHash = m.OpeningHash;
            _peerName = string.IsNullOrEmpty(m.DisplayName) ? _peerName : m.DisplayName;
            Adopt(engine, m.Ply);
            Status = m.Ply > 0
                ? "Rejoining " + (_peerName ?? "your opponent") + "'s game..."
                : "Playing " + (_peerName ?? "your opponent") + ".";
        }

        void Adopt(DuelEngine engine, int catchUpTo)
        {
            Engine = engine;
            _log.Clear();
            _future.Clear();
            Desync = null;
            _catchUpTo = catchUpTo;
            _lastHeard = _clock;
            _nextPing = 0.0;
            Phase = SessionPhase.Playing;

            var begun = MatchBegun;
            if (begun != null) begun();
        }

        /// <summary>
        /// How far the frames still arriving from a restore reach. Zero except in the moments
        /// after adopting a match a peer handed back: while catching up we are replaying a log
        /// that contains OUR OWN past commands as well as theirs, so the "a peer may only move
        /// its own side" guard has to stand down for exactly that long - and no longer.
        /// </summary>
        int _catchUpTo;

        /// <summary>True while replaying a restored log. The view shows it; input waits for it.</summary>
        public bool CatchingUp { get { return Ply < _catchUpTo; } }

        void OnPing(NetMessage m)
        {
            if (m.SessionId != _sessionId) return;

            // They are behind us and not catching up on their own: they lost frames while the
            // relay was down. Re-send what they are missing. (Ahead of us is not our problem -
            // their frames are already on the way.)
            if (Phase == SessionPhase.Playing && m.Ply < Ply)
                for (int i = m.Ply; i < _log.Count; i++) Send(_log[i]);
        }

        void OnBye(NetMessage m)
        {
            if (m.SessionId != _sessionId) return;
            Phase = SessionPhase.Ended;
            Status = string.IsNullOrEmpty(m.Text) ? "Your opponent left." : m.Text;
        }

        void OnRefuse(NetMessage m)
        {
            Fail(m.Code, string.IsNullOrEmpty(m.Text) ? m.Code.ToString() : m.Text);
        }

        // ---- the lockstep -----------------------------------------------------------------------

        void OnFrame(NetMessage m)
        {
            if (Engine == null) return;
            if (Phase != SessionPhase.Playing && Phase != SessionPhase.Ended) return;
            if (m.SessionId != _sessionId) return;
            if (m.Ply < Ply) return;                             // already applied; relays repeat

            // A peer may only ever move its OWN side - except while it is handing us back a
            // match we lost, when the log it replays legitimately contains our own past moves.
            if (m.Command.Actor != RemoteSide && !CatchingUp)
            {
                Fail(RefuseCode.Desync, "Your opponent sent a command for your side.");
                return;
            }

            if (m.Ply > Ply) { _future[m.Ply] = m; return; }     // out of order: hold it

            if (ApplyRemote(m)) DrainFuture();
        }

        void DrainFuture()
        {
            NetMessage next;
            while (_future.TryGetValue(Ply, out next))
            {
                _future.Remove(next.Ply);
                if (!ApplyRemote(next)) return;
            }
        }

        bool ApplyRemote(NetMessage m)
        {
            ulong mine = Engine.Hash();
            if (m.HashBefore != mine)
            {
                ReportDesync(m, mine, Rejection.None);
                return false;
            }

            var result = Engine.Apply(m.Command);
            if (!result.Applied)
            {
                ReportDesync(m, mine, result.Rejection);
                return false;
            }

            _log.Add(m);
            Raise(m.Command, RemoteSide);
            return true;
        }

        void ReportDesync(NetMessage m, ulong mine, Rejection why)
        {
            Desync = new DesyncReport
            {
                Ply = m.Ply,
                Expected = m.HashBefore,
                Actual = mine,
                Command = m.Command.GetType().Name,
                Rejection = why,
            };
            Refuse(RefuseCode.Desync, "The two games have drifted apart - " + Desync);
        }

        /// <summary>
        /// The local player's move. Applied here and now - your own taps must never wait on a
        /// round trip - and published in the same breath.
        /// </summary>
        public Rejection Submit(ICommand cmd)
        {
            var gate = LocalGate(cmd);
            if (gate != Rejection.None) return gate;

            ulong before = Engine.Hash();
            var result = Engine.Apply(cmd);
            if (!result.Applied) return result.Rejection;

            var frame = Msg(NetMessageKind.Frame);
            frame.Ply = Ply;
            frame.HashBefore = before;
            frame.Command = cmd;

            _log.Add(frame);
            Send(frame);
            _nextPing = _clock + PingInterval;      // a frame is proof of life

            Raise(cmd, LocalSide);
            DrainFuture();                          // their next move may already be waiting
            return Rejection.None;
        }

        /// <summary>Pure what-if, for the view's legal-cell probing. Never touches the wire.</summary>
        public Rejection CanSubmit(ICommand cmd)
        {
            var gate = LocalGate(cmd);
            if (gate != Rejection.None) return gate;
            return Engine.CanApply(cmd);
        }

        /// <summary>
        /// What this peer is allowed to send, over and above what the rules allow.
        ///
        /// Two extra conditions, and the second one is load-bearing. The whole no-host-authority
        /// argument rests on exactly one side having a legal command at any instant, and that is
        /// very nearly true: every handler gates on the phase and on whose turn it is. The
        /// exception is SendBankedMana, which deliberately carries no phase gate
        /// (ChargeHandlers.cs) - so during Phase == End the OUTGOING side can still legally move
        /// a bank at the same moment the INCOMING side is told to begin its turn. Both validate,
        /// they do not commute, and under optimistic local apply the two engines would end the
        /// exchange holding different banks and halt as a desync in a completely legal game.
        ///
        /// (This is real, not theoretical: the M12 fuzz corpus contains 218 commands landing
        /// between an EndTurn and the next BeginTurn, and every one of them is a SendBankedMana.)
        ///
        /// The narrow fix lives here rather than in the rules because changing a validator would
        /// change the game and force the M12 golden corpus to be re-cut - a rules decision that
        /// belongs to M16, not to a netcode milestone. Solo play is untouched. See DECISIONS D20.
        /// </summary>
        public Rejection LocalGate(ICommand cmd)
        {
            if (Engine == null || cmd == null) return Rejection.UnknownCommand;
            if (Phase != SessionPhase.Playing) return Rejection.GameOver;
            if (CatchingUp) return Rejection.ChoicePending;      // still replaying; not our board yet
            if (cmd.Actor != LocalSide) return Rejection.NotYourTurn;

            var s = Engine.State;
            if (s.Pending == null && s.Phase == TurnPhase.End && !(cmd is BeginTurnCommand))
                return Rejection.WrongPhase;

            return Rejection.None;
        }

        public void Leave(string why)
        {
            if (Phase == SessionPhase.Playing || Phase == SessionPhase.Advertising
                || Phase == SessionPhase.Waiting)
            {
                var bye = Msg(NetMessageKind.Bye);
                bye.Text = why;
                Send(bye);
            }
            Phase = SessionPhase.Ended;
        }

        // ---- plumbing ---------------------------------------------------------------------------

        bool Compatible(NetMessage m)
        {
            if (m.CatalogFingerprint != _registry.Fingerprint)
            {
                Refuse(RefuseCode.CatalogMismatch,
                       "You and your opponent have different card data - one of you is on an "
                       + "older build.");
                return false;
            }
            if (m.FlagBits != _flagBits)
            {
                Refuse(RefuseCode.CatalogMismatch,
                       "You and your opponent have different rules settings.");
                return false;
            }
            return true;
        }

        NetMessage Msg(NetMessageKind kind)
        {
            var m = new NetMessage();
            m.Kind = kind;
            m.SessionId = _sessionId;
            return m;
        }

        void Send(NetMessage m)
        {
            _transport.Publish(_myTopic, NetEnvelope.Seal(_channel, _role, m, _registry, _random));
        }

        /// <summary>Tell them why, then fail ourselves. Both ends need the sentence.</summary>
        void Refuse(RefuseCode code, string text)
        {
            var m = Msg(NetMessageKind.Refuse);
            m.Code = code;
            m.Text = text;
            Send(m);
            Fail(code, text);
        }

        void Fail(RefuseCode code, string text)
        {
            RefusedBecause = code;
            Phase = SessionPhase.Failed;
            Status = text;
        }

        void Raise(ICommand cmd, Side by)
        {
            var handler = Applied;
            if (handler != null) handler(cmd, by);
        }

        ulong NewSessionId()
        {
            var b = _random.Bytes(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)b[i] << (i * 8);
            return v == 0 ? 1UL : v;                 // 0 is reserved for "no session"
        }

        /// <summary>
        /// Both halves decide the shuffle. Neither peer can grind a favourable opening by
        /// re-rolling its own nonce, because it cannot see the other's before committing.
        /// </summary>
        public static ulong SeedFrom(byte[] hostNonce, byte[] guestNonce)
        {
            var h = Sha256.Hash(hostNonce, guestNonce);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)h[i] << (i * 8);
            return v;
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
