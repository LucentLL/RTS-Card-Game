using System.Collections.Generic;
using SpawnRowDuel.Net;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using SpawnRowDuel.View.Decks;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// The whole of multiplayer's front door: a password, a banner, and Host or Join.
    ///
    /// There is no account, no lobby list, no matchmaking and no server of ours. Two people agree
    /// a password out of band - said aloud, sent in a chat - and both type it in. Everything they
    /// send is sealed with a key derived from it, and the channel they meet on is named by a
    /// one-way hash of it, so the public relay carrying the duel learns neither who is playing
    /// nor what they are doing.
    /// </summary>
    public sealed class MultiplayerUi
    {
        readonly GameShell _shell;
        readonly ICardCatalog _cat;
        readonly VisualElement _root;

        NetSession _session;
        IMessageTransport _transport;

        string _password = "";
        string _name = "";
        CommanderId _commander;
        SavedDeck _deck;                    // null == roll one from the commander's pools
        List<SavedDeck> _saved;

        Label _status;
        Label _detail;
        VisualElement _panel;
        bool _busy;

        public MultiplayerUi(GameShell shell, ICardCatalog cat, VisualElement root)
        {
            _shell = shell;
            _cat = cat;
            _root = root;

            _saved = DeckStore.Load(cat);
            _commander = cat.Commanders[0].Id;
            _name = SystemInfo.deviceName;
            if (string.IsNullOrEmpty(_name) || _name.Length > 18) _name = "Player";

            Build();
        }

        public bool Busy { get { return _busy; } }

        // ── the screen ──────────────────────────────────────────────────────────────────

        VisualElement _page;

        void Build()
        {
            if (_page != null && _page.parent != null) _page.RemoveFromHierarchy();

            var page = UiKit.Box(_root);
            _page = page;
            UiKit.Fill(page);
            page.style.paddingLeft = 22f * UiKit.S; page.style.paddingRight = 22f * UiKit.S;
            page.style.paddingTop = 16f * UiKit.S; page.style.paddingBottom = 16f * UiKit.S;

            var head = UiKit.Row(page);
            UiKit.Btn(head, "← menu", Leave, 13f);
            var titles = UiKit.Box(head);
            titles.style.marginLeft = 16f * UiKit.S;
            UiKit.Text(titles, "Duel a Friend", 24f, UiFont.DisplayBlack, UiKit.Gold);
            UiKit.Text(titles,
                "Agree a password between you. One hosts, the other joins - that is the whole of it. "
                + "There is no account and no server: you meet over public infrastructure, and "
                + "every message is encrypted with the password before it leaves your machine.",
                12f, UiFont.BodyRegular, UiKit.Dim);

            var body = UiKit.Row(page);
            body.style.marginTop = 14f * UiKit.S;
            body.style.flexGrow = 1f;
            body.style.minHeight = 0f;
            body.style.alignItems = Align.Stretch;   // two full-height columns, not two centred ones

            BuildLeft(body);
            BuildRight(body);
        }

        void BuildLeft(VisualElement body)
        {
            var col = UiKit.Glass(body, 14f);
            col.style.width = 360f * UiKit.S;
            col.style.marginRight = 14f * UiKit.S;
            col.style.flexShrink = 0f;
            UiKit.Radius(col, 8f);

            UiKit.Text(col, "PASSWORD", 11f, UiFont.DisplayBlack, UiKit.Dim);
            var pwRow = UiKit.Row(col);
            var pw = UiKit.Field(pwRow, _password, 250f, v => _password = v,
                                 "say it aloud to your friend");
            var suggest = UiKit.Btn(pwRow, "suggest", delegate
            {
                _password = JoinCode.Make();
                pw.value = _password;
            }, 12f);
            suggest.style.marginLeft = 6f * UiKit.S;

            UiKit.Text(col,
                "Anything you both type identically will do - spacing and capitals are ignored. "
                + "A suggested code is far harder for a stranger to guess than a word you chose.",
                10.5f, UiFont.BodyItalic, UiKit.Dim).style.marginBottom = 10f * UiKit.S;

            UiKit.Text(col, "YOUR NAME", 11f, UiFont.DisplayBlack, UiKit.Dim);
            UiKit.Field(col, _name, 250f, v => _name = v, "shown to your opponent")
                .style.marginBottom = 12f * UiKit.S;

            UiKit.Text(col, "YOUR DECK", 11f, UiFont.DisplayBlack, UiKit.Dim);
            var deckRow = UiKit.Box(col);
            RebuildDeckButtons(deckRow);

            var actions = UiKit.Row(col);
            actions.style.marginTop = 16f * UiKit.S;

            var host = UiKit.Btn(actions, "Host a duel", delegate { Start(NetRole.Host); }, 15f,
                                 UiKit.Gold);
            host.style.flexGrow = 1f;

            var join = UiKit.Btn(actions, "Join a duel", delegate { Start(NetRole.Guest); }, 15f);
            join.style.flexGrow = 1f;
            join.style.marginLeft = 8f * UiKit.S;

            _status = UiKit.Text(col, "", 12.5f, UiFont.BodyRegular, UiKit.Ink);
            _status.style.marginTop = 12f * UiKit.S;
            _status.style.whiteSpace = WhiteSpace.Normal;

            _detail = UiKit.Text(col, "", 10.5f, UiFont.BodyItalic, UiKit.Dim);
            _detail.style.whiteSpace = WhiteSpace.Normal;

            var cancel = UiKit.Btn(col, "stop looking", Stop, 12f);
            cancel.style.marginTop = 8f * UiKit.S;
            cancel.style.display = DisplayStyle.None;
            _cancel = cancel;

            _panel = col;
        }

        VisualElement _cancel;

        void RebuildDeckButtons(VisualElement into)
        {
            into.Clear();

            var roll = UiKit.Btn(into, DeckLabel(null), delegate { PickDeck(null, into); }, 12.5f,
                                 _deck == null ? UiKit.Gold : UiKit.Ink);
            roll.style.marginBottom = 3f * UiKit.S;

            for (int i = 0; i < _saved.Count; i++)
            {
                var d = _saved[i];
                if (!DeckRules.IsLegal(_cat, d)) continue;
                var b = UiKit.Btn(into, DeckLabel(d), delegate { PickDeck(d, into); }, 12.5f,
                                  _deck == d ? UiKit.Gold : UiKit.Ink);
                b.style.marginBottom = 3f * UiKit.S;
            }
        }

        string DeckLabel(SavedDeck d)
        {
            if (d == null) return "Random deck from my banner";
            return d.Name + "  (" + _cat.Commander(d.Commander).Name + ")";
        }

        void PickDeck(SavedDeck d, VisualElement into)
        {
            _deck = d;
            if (d != null) _commander = d.Commander;
            RebuildDeckButtons(into);
            RebuildBanners();
        }

        VisualElement _banners;

        void BuildRight(VisualElement body)
        {
            var col = UiKit.Box(body);
            col.style.flexGrow = 1f;
            col.style.minHeight = 0f;            // let it shrink; otherwise the list overflows

            UiKit.Text(col, "YOUR BANNER", 11f, UiFont.DisplayBlack, UiKit.Dim);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            col.Add(scroll);

            _banners = UiKit.Box(scroll);
            _banners.style.flexDirection = FlexDirection.Row;
            _banners.style.flexWrap = Wrap.Wrap;
            RebuildBanners();
        }

        void RebuildBanners()
        {
            if (_banners == null) return;
            _banners.Clear();

            for (int i = 0; i < _cat.Commanders.Count; i++)
            {
                var cc = _cat.Commanders[i];
                bool picked = cc.Id == _commander;
                var colour = cc.Colors.Length > 0
                    ? Campaign.GlobeView.ElementColour(cc.Colors[0]) : UiKit.Ink;

                var chip = UiKit.Glass(_banners, 7f);
                chip.style.width = 150f * UiKit.S;
                chip.style.marginRight = 6f * UiKit.S;
                chip.style.marginBottom = 6f * UiKit.S;
                UiKit.Radius(chip, 5f);
                if (picked) UiKit.Border(chip, UiKit.Gold, 2f);

                UiKit.Text(chip, cc.Name, 13f, UiFont.DisplayBlack, picked ? UiKit.Gold : colour);
                UiKit.Text(chip, "♥" + cc.Hp + " · ⚒ " + cc.Workers, 10.5f, UiFont.BodyRegular,
                           UiKit.Dim);

                var id = cc.Id;
                chip.RegisterCallback<ClickEvent>(delegate
                {
                    if (_busy) return;
                    _commander = id;
                    _deck = null;                      // a banner change abandons a foreign deck
                    Build();
                });
            }
        }

        // ── connecting ──────────────────────────────────────────────────────────────────

        void Start(NetRole role)
        {
            if (_busy) return;

            var pass = PasswordChannel.Normalise(_password);
            if (pass.Length < 3)
            {
                Say("Type a password you have both agreed - at least a few characters.", null);
                return;
            }

            Stop();
            _busy = true;
            if (_cancel != null) _cancel.style.display = DisplayStyle.Flex;

            var random = new NetRandom();
            _transport = new RelayTransport(new PlatformWebSocketFactory(), random);

            _session = new NetSession(role, PasswordChannel.Derive(pass), _cat, _transport, random);
            _session.Begin(_commander, DrawPile(), _name, RulesOptions.JsParity.FlagBits);

            Say(role == NetRole.Host
                ? "Hosting. Tell your friend the password and have them press Join."
                : "Looking for the host on that password...", null);
        }

        /// <summary>
        /// The deck as an ORDERED draw pile, or null to let the shared match seed roll one. The
        /// order is decided here and crosses the wire, so both engines shuffle identically - the
        /// alternative, each side shuffling its own, is a desync on the first draw.
        /// </summary>
        List<HandCard> DrawPile()
        {
            if (_deck == null) return null;
            return DeckRules.ToDrawPile(_cat, _deck, new Pcg32((ulong)Random.Range(1, int.MaxValue)));
        }

        void Stop()
        {
            if (_session != null) { _session.Leave("left the lobby"); _session = null; }
            if (_transport != null) { _transport.Dispose(); _transport = null; }
            _busy = false;
            if (_cancel != null) _cancel.style.display = DisplayStyle.None;
        }

        void Leave()
        {
            Stop();
            _shell.Show(ShellScreen.MainMenu);
        }

        /// <summary>Driven by the shell while this screen is up. Once a match exists the
        /// MatchController takes over pumping and this screen goes away.</summary>
        public void Tick()
        {
            if (_session == null) return;

            _session.Pump(Time.unscaledDeltaTime);

            switch (_session.Phase)
            {
                case SessionPhase.Playing:
                    var session = _session;
                    _session = null;                   // hand it over; do not pump it twice
                    _transport = null;
                    _busy = false;
                    _shell.BeginNetMatch(session);
                    return;

                case SessionPhase.Failed:
                    Say(_session.Status, null);
                    _busy = false;
                    if (_cancel != null) _cancel.style.display = DisplayStyle.None;
                    _session = null;
                    if (_transport != null) { _transport.Dispose(); _transport = null; }
                    return;

                default:
                    Say(_session.Status, Detail());
                    return;
            }
        }

        string Detail()
        {
            if (_transport == null) return null;

            switch (_transport.Status)
            {
                case TransportStatus.Connected:
                    return "connected via " + _transport.Description;
                case TransportStatus.Connecting:
                    return "reaching the relay...";
                default:
                    return _transport.LastError == null
                        ? "no relay is answering - check your connection"
                        : "no relay is answering (" + _transport.LastError + ")";
            }
        }

        void Say(string status, string detail)
        {
            if (_status != null) _status.text = status ?? "";
            if (_detail != null) _detail.text = detail ?? "";
        }
    }

    /// <summary>
    /// A suggested password: four words and a number.
    ///
    /// The point is entropy people will actually accept. A password someone thinks of is
    /// typically in the first few thousand guesses, and the topic name is a public, offline,
    /// remotely-checkable function of it - so "dragon" is a game a stranger can find. Four words
    /// from this list plus three digits is about forty bits, which at the rate a GPU can drive
    /// PBKDF2 is months of work to sweep, for the prize of joining a friendly card game.
    /// </summary>
    public static class JoinCode
    {
        static readonly string[] Words =
        {
            "amber","anvil","arrow","ashen","aspen","badge","basin","beacon","birch","blade",
            "bloom","bluff","brass","briar","brine","bronze","cairn","candle","canyon","cedar",
            "chalk","cider","cinder","clay","cloak","clover","cobalt","comet","copper","coral",
            "crane","creek","crest","crown","dagger","dawn","delta","drift","dusk","eagle",
            "ember","fable","falcon","fern","flint","forge","fossil","frost","gale","garnet",
            "glade","glass","glide","gorge","granite","grove","harbor","harvest","hazel","heron",
            "hollow","ivory","jasper","kelp","lantern","larch","ledge","lichen","linen","lupine",
            "maple","marble","marsh","meadow","mesa","mica","mist","moss","nettle","oak",
            "ochre","onyx","opal","orchard","osprey","otter","pebble","pewter","pillar","pine",
            "plume","prairie","quarry","quartz","quill","raven","reed","ridge","rill","river",
            "rowan","rust","sable","sage","sandbar","sapling","shale","shore","silver","slate",
            "sorrel","spire","spruce","stag","steppe","stone","summit","swift","talon","tamarisk",
            "thistle","thorn","tide","timber","topaz","torrent","tundra","umber","vale","vellum",
            "willow","wren",
        };

        public static string Make()
        {
            var random = new NetRandom();
            var bytes = random.Bytes(8);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(Words[bytes[i] % Words.Length]);
            }
            int digits = ((bytes[4] << 8 | bytes[5]) & 0x3FF) % 1000;
            sb.Append('-').Append(digits.ToString("000"));
            return sb.ToString();
        }
    }
}
