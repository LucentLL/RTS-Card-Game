using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Fx
{
    /// <summary>
    /// Who the board belongs to right now, said out loud the moment it changes.
    ///
    /// The screen already carried the answer - "TURN 11 / YOUR TURN" in the top right corner, and
    /// the phase track under it - but a value sitting in a corner is a thing you have to go and
    /// read. Nothing on the screen ever ANNOUNCED anything: the foe's whole turn could pass while
    /// the player was looking at their hand, and the only witness that it had happened at all was
    /// a word in 11px type changing colour eight hundred pixels away. "There should be a
    /// notification saying when turn has changed to other player."
    ///
    /// So the transition gets its own event. It is drawn across the middle of the board - the one
    /// place the eye is already - and it is gone in a second and a half, because a banner that
    /// outstays its welcome is a banner people learn to play around.
    ///
    /// BOTH DIRECTIONS, not just theirs. Half of "whose turn is it" is knowing the other half has
    /// ended, and a handover that is only ever announced in one direction teaches the player that
    /// silence means it is still their move - which is the belief the report is complaining about.
    ///
    /// It reads its side off <see cref="Seat"/> rather than off <c>Side.You</c>. The guest in a
    /// multiplayer duel is <c>Side.Foe</c> to the engine and would otherwise be told that every
    /// one of their own turns belonged to the opponent.
    ///
    /// UI Toolkit, in HandBar's overlay layer, for the same two reasons the cut-in is: IMGUI paints
    /// after every UI Toolkit panel and would land on top of the hand, and IMGUI is invisible to
    /// the batchmode probe - a banner drawn there could not be checked without a twenty-minute
    /// WebGL build.
    /// </summary>
    public sealed class TurnHerald : MonoBehaviour
    {
        /// <summary>Off drops the banner and leaves the corner read-out to say it - the twin of
        /// <see cref="CombatTheatre.CutIns"/>.</summary>
        public static bool Announce = true;

        // Front-loaded like the cut-in's fly-in: the band opens fast, the words hold, and the
        // whole thing is off the board before anyone has finished their first move.
        const float OpenSeconds = 0.20f;
        const float HoldSeconds = 0.95f;
        const float FadeSeconds = 0.38f;
        const float Life = OpenSeconds + HoldSeconds + FadeSeconds;

        MatchController _match;
        HandBar _hand;
        CombatTheatre _theatre;

        VisualElement _band, _bar, _rule;
        Label _who, _turn;
        int _builtFor = -1;

        float _born = -99f;
        bool _live;
        bool _mine;

        /// <summary>Announced but not started yet, because a battle cut-in is still on screen.
        /// The news waits; it is not thrown away.</summary>
        bool _pending;

        /// <summary>Which turn is currently being announced, so the same TurnStarted arriving
        /// twice - a resumed match, a replayed command log - cannot restart the banner.</summary>
        int _shown = -1;

        /// <summary>The duel this herald has been watching. A rematch puts TurnNumber back to 1,
        /// which <see cref="_shown"/> would otherwise read as "already announced".</summary>
        int _seenMatch = -1;

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _hand = GetComponent<HandBar>();
            _theatre = GetComponent<CombatTheatre>();
        }

        void OnEnable()
        {
            if (_match != null) _match.Observed += Observe;
        }

        void OnDisable()
        {
            if (_match != null) _match.Observed -= Observe;
        }

        void Observe(GameEvent ev)
        {
            var ended = ev as MatchEnded;
            if (ended != null)
            {
                // The match is over and MatchHud is about to print so across the middle of the
                // screen. Two announcements in the same place is one too many, and the loser does
                // not need to be told whose turn it is.
                _live = false;
                _pending = false;
                return;
            }

            var started = ev as TurnStarted;
            if (started == null) return;
            Raise(started.Side, started.TurnNumber);
        }

        /// <summary>
        /// RECORD ONLY. Events are drained from inside OnGUI on some paths (MatchHud -> TryHuman
        /// -> PumpEvents), and building or moving a VisualElement from there is a UI Toolkit tree
        /// edit inside somebody else's layout pass. Everything visible happens in LateUpdate.
        /// </summary>
        void Raise(Side side, int turnNumber)
        {
            if (turnNumber == _shown) return;
            _shown = turnNumber;
            _mine = Seat.Mine(side);
            _pending = true;
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null || _hand == null) return;
            if (!EnsureSurfaces()) return;

            // THE OPENING TURN HAS NO EVENT. MatchSetup enters turn 1 directly at Upkeep rather
            // than through the BeginTurn pipeline, so the only TurnStarted a duel ever emits for
            // its first turn is none - and a herald that listened for nothing else would stay
            // silent through exactly the hand-off that matters most, the coin-flip opener that
            // decides whether a multiplayer guest moves first. Seeded off MatchSerial instead,
            // which is bumped on every new match, rematch and reconnect.
            if (_match.MatchSerial != _seenMatch)
            {
                _seenMatch = _match.MatchSerial;
                _shown = -1;                       // a rematch puts TurnNumber back to 1
                _live = false;
                var fresh = _match.Engine.State;
                Raise(fresh.Turn, fresh.TurnNumber);
            }

            // A fight outranks a hand-off: they are drawn across the same middle of the board, and
            // a banner over a clash is two things saying different news in one place. The news
            // WAITS rather than being dropped - the turn still changed.
            if (_pending && (_theatre == null || !_theatre.Busy))
            {
                _pending = false;
                _born = Time.unscaledTime;
                _live = true;
            }

            float age = Time.unscaledTime - _born;
            if (!_live || !Announce || age >= Life)
            {
                if (_band.style.display != DisplayStyle.None)
                    _band.style.display = DisplayStyle.None;
                return;
            }

            Layout();

            // open, hold, fade
            float open = Mathf.Clamp01(age / OpenSeconds);
            float ease = 1f - (1f - open) * (1f - open) * (1f - open);      // out-cubic
            float fade = age <= OpenSeconds + HoldSeconds
                       ? 1f
                       : 1f - Mathf.Clamp01((age - OpenSeconds - HoldSeconds) / FadeSeconds);

            _band.style.display = DisplayStyle.Flex;
            _band.style.opacity = fade;

            // The band opens ACROSS, the words rise INTO it. A rectangle that fades up is a
            // rectangle; a rectangle that is drawn across the board is a thing arriving.
            //
            // The scale goes on the BAR, which is a sibling behind the text rather than its
            // parent: a VisualElement's transform applies to its whole subtree, so scaling the
            // container would have squeezed the words to a third of their width and stretched
            // them out as the band opened. Only the stone moves; the writing on it does not.
            _bar.transform.scale = new Vector3(ease, 1f, 1f);
            _who.transform.position = new Vector3(0f, (1f - ease) * 10f * HudLayout.Scale, 0f);
            _who.style.opacity = Mathf.Clamp01((ease - 0.35f) / 0.65f);
            _turn.style.opacity = _who.style.opacity;
        }

        /// <summary>
        /// The colours are the corner read-out's own, so the banner and the thing it is announcing
        /// agree: gold when the board is yours, cold blue when it is not.
        /// </summary>
        void Layout()
        {
            float px = HudLayout.Scale;
            var live = _mine ? new Color(1f, 0.85f, 0.4f) : new Color(0.65f, 0.8f, 1f);

            // Across the middle, clear of both walls and of the picked hand card rising out of the
            // bottom. Sized off the panel, not off Screen - they differ while the probe renders
            // this panel into a texture.
            var size = _hand.PanelSize();
            float h = 56f * px;
            _band.style.left = 0f;
            _band.style.right = 0f;
            _band.style.height = h;
            _band.style.top = size.y * 0.38f - h * 0.5f;

            _bar.style.backgroundColor = new Color(0.03f, 0.04f, 0.07f, 0.72f);
            _bar.style.borderTopWidth = Mathf.Max(1f, 1.5f * px);
            _bar.style.borderBottomWidth = Mathf.Max(1f, 1.5f * px);
            _bar.style.borderTopColor = live;
            _bar.style.borderBottomColor = live;

            _who.text = _mine ? "YOUR TURN" : "OPPONENT'S TURN";
            _who.style.color = live;
            _who.style.fontSize = 30f * px;

            _turn.text = "TURN " + _shown;
            _turn.style.color = new Color(0.74f, 0.77f, 0.84f);
            _turn.style.fontSize = 12f * px;

            _rule.style.height = Mathf.Max(1f, 1f * px);
            _rule.style.backgroundColor = new Color(live.r, live.g, live.b, 0.35f);
        }

        /// <summary>
        /// REBUILT WITH THE PANEL, for the reason CombatTheatre.EnsureSurfaces is: GameShell
        /// switches the whole board object off whenever the screen is not a duel, a disabled
        /// UIDocument tears its root down, and everything parented into it goes too. A non-null
        /// `_band` pointing at an orphan is a banner that announces every turn into nothing.
        /// </summary>
        bool EnsureSurfaces()
        {
            if (_hand == null || !_hand.PanelReady || _hand.OverlayLayer == null) return false;
            if (_band != null && _builtFor == _hand.PanelGeneration) return true;

            _builtFor = _hand.PanelGeneration;

            _band = new VisualElement { pickingMode = PickingMode.Ignore };
            _band.style.position = Position.Absolute;
            _band.style.display = DisplayStyle.None;
            _band.style.alignItems = Align.Center;
            _band.style.justifyContent = Justify.Center;

            // The stone itself: out of flow, filling the band, and the only thing that is scaled.
            // Added FIRST so the words sit in front of it.
            _bar = new VisualElement { pickingMode = PickingMode.Ignore };
            _bar.style.position = Position.Absolute;
            _bar.style.left = 0; _bar.style.right = 0;
            _bar.style.top = 0; _bar.style.bottom = 0;
            _band.Add(_bar);

            _who = Text(UiFont.DisplayBlack);
            _who.style.letterSpacing = 3f;
            _band.Add(_who);

            _rule = new VisualElement { pickingMode = PickingMode.Ignore };
            _rule.style.width = Length.Percent(22f);
            _rule.style.marginTop = 2f;
            _rule.style.marginBottom = 2f;
            _band.Add(_rule);

            _turn = Text(UiFont.BodyBold);
            _turn.style.letterSpacing = 2f;
            _band.Add(_turn);

            _hand.OverlayLayer.Add(_band);
            return true;
        }

        static Label Text(UiFont face)
        {
            var l = new Label("") { pickingMode = PickingMode.Ignore };
            var font = ViewAssets.Font(face);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.textShadow = new TextShadow
            {
                offset = new Vector2(0f, 2f),
                blurRadius = 5f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            return l;
        }
    }
}
