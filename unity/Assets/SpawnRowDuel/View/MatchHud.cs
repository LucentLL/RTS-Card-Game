using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The placeholder HUD: an opaque top bar (both players + turn/phase), the hand peeking at the
    /// very bottom edge, and the turn controls on a rail hugging the RIGHT edge at mid-height. The
    /// 3D board renders between the top bar and the hand - BoardInput shrinks the camera viewport
    /// to the gap HudLayout publishes. Menus are solid panels clamped on-screen and scroll when
    /// tall. Everything is read from GameState each frame; every tap is a command.
    ///
    /// The bands used to be three deep at the bottom - hand, mode row, action row - which cost the
    /// board a quarter of a 480-unit screen. The reference build does not do that: `.hand` sits at
    /// `bottom: 0` and `#boardBtns` hugs the right edge (its own comment calls it the Master Duel
    /// coin position), so the controls overlay a corner the board is not using and cost it nothing.
    ///
    /// Scaling is by the SHORT side of the screen (~480 logical units), so portrait and
    /// landscape both get a sane layout instead of landscape inheriting portrait's width math.
    ///
    /// IMGUI on purpose - no font asset needed while the glyph plan is open (GAPS P0).
    /// </summary>
    [RequireComponent(typeof(MatchController))]
    public class MatchHud : MonoBehaviour
    {
        /// <summary>
        /// Set by <see cref="Shell.GameShell"/>: the duel's own commander select is only allowed
        /// to speak on its own screen. Before the shell existed this WAS the front of the game -
        /// no match, so draw the setup - and left to itself it would draw over the main menu.
        /// </summary>
        public static bool ShellSuppressed;

        // band heights, logical units - defined by HudLayout, which the UI Toolkit surfaces read
        // too (they lay out before OnGUI has ever run)
        // What each edge keeps clear: the hand peeks, which are the deepest thing there. The
        // walls behind them slide and are NOT a fixed band any more (WallBands).
        const float TopH = HudLayout.FoeHandH;
        const float HandH = HudLayout.HandH;
        const float ModeH = HudLayout.ModeH;
        const float BottomH = HudLayout.HandH;

        /// <summary>
        /// How far past the top band the SHELL's own button reaches, in logical units - the
        /// ↩ abandon / ↩ menu row GameShell hangs over a match in progress.
        ///
        /// Every panel here that can reach the top of the screen starts below it. Not for looks:
        /// that button is UI Toolkit and these panels are IMGUI, the two are separate input paths,
        /// and a tap where they overlap runs BOTH of them. The match log's CLOSE was drawn inside
        /// it, so closing the log also abandoned the match.
        /// </summary>
        float ShellBarH
        {
            get
            {
                var bar = HudLayout.ShellPx;
                if (bar.width <= 0f || _scale <= 0f) return 0f;
                return Mathf.Clamp(bar.yMax / _scale - TopH, 0f, 80f);
            }
        }

        private MatchController _match;
        private BoardInput _input;
        private string _hint = "";
        private float _hintUntil;
        private int _selectedHandIndex = -1;

        /// <summary>The picked hand card. HandBar draws the lift; MatchHud owns the state, so the
        /// placement flow, the mode row and every cancel path still have one owner.</summary>
        public int SelectedHandIndex { get { return _selectedHandIndex; } }

        public void SelectHand(int index)
        {
            _selectedHandIndex = (_selectedHandIndex == index) ? -1 : index;
            _buildMenuOpen = false;
            _match.CancelPending();
        }

        // ── the blocker choice, reachable from the board ──────────────────────────────────
        //
        // The checkbox panel STAYS. It is the only way to reach a pool worker - BlockerRequest
        // offers those as UnitRef.Pool, which names no cell and can therefore never be swept - and
        // it is the numeric readout for what is committed. What the board adds is a second way to
        // fill the same set, for the blockers that are standing somewhere.

        /// <summary>
        /// Drop the ticks the moment the QUESTION changes.
        ///
        /// The reset used to live inside DrawChoicePanel, which is an OnGUI pass - and everything
        /// that READS this set now runs in LateUpdate, a frame earlier. So between a new request
        /// parking and the next OnGUI, the indices of the request just answered were still in the
        /// set and were being drawn against a different request's eligible list. Syncing at every
        /// door into the set costs one reference compare and cannot be a frame late.
        /// </summary>
        void SyncChoice()
        {
            var pending = _match != null && _match.Engine != null
                ? _match.Engine.State.Pending : null;
            if (ReferenceEquals(pending, _seenPending)) return;
            _seenPending = pending;
            _chosenBlockers.Clear();
        }

        /// <summary>Is a blocker choice parked on THIS player right now?</summary>
        public bool AwaitingBlockers
        {
            get
            {
                if (_match == null || _match.Engine == null) return false;
                SyncChoice();
                var p = _match.Engine.State.Pending as BlockerRequest;
                return p != null && p.Responder == Seat.Local;
            }
        }

        /// <summary>Which offered blocker stands on this cell, or -1. Pool workers are skipped by
        /// construction: UnitRef.AsCell THROWS for one, and a throw here is eaten by the HUD guard
        /// and takes every control on the screen with it.</summary>
        public int BlockerIndexOf(CellRef cell)
        {
            if (!AwaitingBlockers) return -1;
            var req = (BlockerRequest)_match.Engine.State.Pending;

            for (int i = 0; i < req.Eligible.Length; i++)
            {
                if (!req.Eligible[i].IsCell) continue;
                if (req.Eligible[i].AsCell == cell) return i;
            }
            return -1;
        }

        public bool BlockerPicked(int index) { SyncChoice(); return _chosenBlockers.Contains(index); }

        public void SetBlockerPick(int index, bool on)
        {
            if (index < 0) return;
            SyncChoice();
            if (on) _chosenBlockers.Add(index); else _chosenBlockers.Remove(index);
        }

        /// <summary>
        /// Is the card on this cell one you have COMMITTED to the block?
        ///
        /// The other half of the light the attacker already had. A declared attacker is tinted on
        /// its card and on its standee (MatchController.IsAttacking); a chosen defender was
        /// nowhere but a tick in a list, so on a board where two copies of one card differ only by
        /// the health left on them there was no way to see who you had put in front of the blow.
        /// Read every LateUpdate by the card layers, so it must stay allocation-free.
        /// </summary>
        public bool IsDefending(CellRef cell)
        {
            SyncChoice();                                      // never answer for a stale question
            if (_chosenBlockers.Count == 0) return false;      // the common case, second
            int i = BlockerIndexOf(cell);
            return i >= 0 && _chosenBlockers.Contains(i);
        }

        /// <summary>Offered the block and not yet in it - lit more faintly, because "who may
        /// stand here" is the question the panel is asking.</summary>
        public bool IsOfferedBlocker(CellRef cell)
        {
            int i = BlockerIndexOf(cell);
            return i >= 0 && !_chosenBlockers.Contains(i);
        }
        private bool _buildMenuOpen;

        /// <summary>The build-tree node the player last tapped, and the one the pointer is over.
        /// Catalog defs are immutable singletons, so holding the reference across frames is safe
        /// while the rebuilt BuildNode structs are not.</summary>
        private StructureDef _buildPickDef, _buildHover;
        private Vector2 _buildScroll;
        private Vector2 _handScroll;
        private float _scale = 1f;
        private int _lastLogCount;
        private float _logShownUntil;
        private bool _logOpen, _settingsOpen, _guiFaulted;
        private float _pendingSince;
        private Vector2 _logScroll;
        private Rect _rail;               // where DrawSideRail put itself, so the log can hang off it
        private readonly HashSet<int> _chosenBlockers = new HashSet<int>();
        private PendingRequest _seenPending;

        /// <summary>
        /// Which duel these panel flags belong to.
        ///
        /// A COUNTER rather than the engine itself, for two reasons. Holding the engine would pin
        /// the whole retired match - its state, both decks, every unit - alive for as long as the
        /// player sat in the menu, which is the one thing putting the match down was for. And the
        /// engine reference is swapped mid-match by a reconnect, which is the same duel continuing
        /// and has no business closing the player's menus.
        /// </summary>
        private int _seenMatch = -1;

        /// <summary>Leaving is REQUESTED here and acted on at the end of the pass - see the
        /// bottom of OnGUI.</summary>
        private bool _quitRequested;

        private Shell.GameShell _shell;

        Shell.GameShell FindShell()
        {
            if (_shell == null) _shell = FindFirstObjectByType<Shell.GameShell>();
            return _shell;
        }
        private bool _upgradeMenuOpen;
        private int _chargeAmount;
        private int _chargeCellId = -1;
        private int _upkeepPromptedTurn = -1;
        private CommanderId _pickYou = new CommanderId("fire");
        private CommanderId _pickFoe = new CommanderId("water");
        private Vector2 _selectScrollYou, _selectScrollFoe;

        private GUIStyle _label, _small, _tiny, _button, _bigButton, _cardName, _center, _wrap;
        private GUIStyle _group, _arrow, _nodeReady, _nodeCost, _nodeLocked, _nodeUp, _nodeUpOpen;

        private static readonly Color PanelColor = new Color(0.055f, 0.06f, 0.085f, 1f);

        private static readonly Color PanelSoft = new Color(0.055f, 0.06f, 0.085f, 0.72f);
        private static readonly Color CardBack = new Color(0.10f, 0.11f, 0.15f, 1f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();

            // The card-face surfaces attach themselves rather than being baked into the scene:
            // the battle scene is generated (SceneBootstrap) and a component added by hand there
            // would be lost on the next rebuild.
            if (GetComponent<Cards.HandBar>() == null) gameObject.AddComponent<Cards.HandBar>();
            if (GetComponent<Cards.CardPlateLayer>() == null) gameObject.AddComponent<Cards.CardPlateLayer>();
            if (GetComponent<Cards.StandeeLayer>() == null) gameObject.AddComponent<Cards.StandeeLayer>();
            if (GetComponent<Cards.UnitVitals>() == null) gameObject.AddComponent<Cards.UnitVitals>();
            if (GetComponent<Fx.CombatTheatre>() == null) gameObject.AddComponent<Fx.CombatTheatre>();
            if (GetComponent<Fx.TurnHerald>() == null) gameObject.AddComponent<Fx.TurnHerald>();
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 11 };
            _small.normal.textColor = new Color(0.75f, 0.78f, 0.85f);
            _tiny = new GUIStyle(_label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            _cardName = new GUIStyle(_label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
            _center = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            _center.normal.textColor = Gold;

            // The log's own style, wrapping, built ONCE. Both log panels need it and OnGUI runs
            // every frame, so `new GUIStyle(_small)` inside the draw was a per-frame allocation
            // for a style that never changes.
            _wrap = new GUIStyle(_small) { wordWrap = true };

            // ── build-tree styles ────────────────────────────────────────────────────
            // Built ONCE. OnGUI runs every frame and the tree draws up to fourteen nodes, so a
            // per-node "new GUIStyle" was fourteen allocations a frame for styles that never change.
            _group = new GUIStyle(_label) { fontSize = 9, wordWrap = true,
                                            alignment = TextAnchor.MiddleRight };
            _group.normal.textColor = new Color(0.52f, 0.56f, 0.66f);

            _arrow = new GUIStyle(_label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _arrow.normal.textColor = new Color(0.45f, 0.49f, 0.58f);

            // Two lines in a 124x32 node: the name, then either its price or why it is out of
            // reach. wordWrap stays OFF so a long name clips rather than stealing the second line.
            _nodeReady = new GUIStyle(GUI.skin.button) { fontSize = 10, wordWrap = false,
                                                        alignment = TextAnchor.MiddleCenter };
            _nodeReady.normal.textColor = Color.white;
            _nodeReady.hover.textColor = Gold;
            _nodeReady.active.textColor = Gold;

            // Affordable-later reads in the mana colour, because the note IS the price.
            _nodeCost = new GUIStyle(_nodeReady);
            _nodeCost.normal.textColor = new Color(0.62f, 0.72f, 0.88f);
            _nodeCost.hover.textColor = new Color(0.78f, 0.86f, 1f);

            // Prerequisite missing or nowhere to stand: the deepest grey that still reads.
            _nodeLocked = new GUIStyle(_nodeReady);
            _nodeLocked.normal.textColor = new Color(0.42f, 0.44f, 0.50f);
            _nodeLocked.hover.textColor = new Color(0.60f, 0.62f, 0.68f);

            // Upgrade tiers are a different KIND of unreachable - not blocked, just taken
            // somewhere else - so they get their own colour rather than a third shade of grey.
            _nodeUp = new GUIStyle(_nodeReady);
            _nodeUp.normal.textColor = new Color(0.50f, 0.46f, 0.38f);
            _nodeUp.hover.textColor = new Color(0.68f, 0.62f, 0.50f);

            _nodeUpOpen = new GUIStyle(_nodeReady);
            _nodeUpOpen.normal.textColor = Gold * 0.92f;
            _nodeUpOpen.hover.textColor = Gold;
        }


        /// <summary>
        /// One line's worth of height for a style, rounded up, with a hair of leading.
        ///
        /// Every row in this HUD used to be a hard-coded 14, chosen against an 11px font on a
        /// desktop. It is not a constant: GUIStyle.lineHeight comes from the font Unity actually
        /// resolved, and IMGUI CLIPS a label to its rect - so on a phone, where the same 11px
        /// style resolves to a taller line, every row in the log and the inspect panel had its
        /// descenders sliced off. Text that is drawn at all must be drawn whole.
        /// </summary>
        static float Row(GUIStyle st)
        {
            return Mathf.Ceil(st.lineHeight) + 2f;
        }

        /// <summary>A label that is guaranteed to fit what it is given, wrapping if it must.</summary>
        float Line(float x, float y, float w, string text, GUIStyle st)
        {
            float h = Mathf.Max(Row(st), st.CalcHeight(new GUIContent(text), w));
            GUI.Label(new Rect(x, y, w, h), text, st);
            return h;
        }

        static void Panel(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (_match == null) return;
            EnsureStyles();

            // A DIFFERENT ENGINE IS A DIFFERENT DUEL, and that is the test rather than "no match".
            //
            // This lives on the board object, which the shell switches off for every screen except
            // Battle and Skirmish - so OnGUI never runs while the player is in a menu, and the
            // moment they enter a campaign duel, a deck-builder skirmish or a multiplayer game the
            // shell shows the board and starts the match in the SAME call. The first OnGUI that
            // runs already has a match in front of it. Keyed on "no match" this reset was reachable
            // on exactly one of the four ways into a duel; keyed on identity it is reachable on all
            // of them, and on the way out as well.
            if (_seenMatch != _match.MatchSerial)
            {
                _seenMatch = _match.MatchSerial;

                // `_upkeepPromptedTurn` is the one that bites without looking like it would: it is
                // compared against the TURN NUMBER, and every match starts at one - so a shortfall
                // prompt already spent in the last duel is silently skipped in the first upkeep of
                // the next one.
                _selectedHandIndex = -1;
                _buildMenuOpen = _upgradeMenuOpen = _settingsOpen = _logOpen = false;
                _upkeepPromptedTurn = -1;
                _chargeCellId = -1;
                _chargeAmount = 0;
                _lastLogCount = 0;
                _guiFaulted = false;
                _seenPending = null;
                _chosenBlockers.Clear();
            }

            if (!_match.MatchStarted)
            {
                if (!ShellSuppressed) DrawCommanderSelect();
                return;
            }

            var s = _match.Engine.State;

            float scale = HudLayout.Recompute();
            _scale = scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            // publish the reserved bands so the camera viewport stays out of them, and reset
            // the in-viewport blocker rects - the draws below re-publish the ones that exist
            HudLayout.MenuPx = new Rect();
            HudLayout.LogPx = new Rect();
            HudLayout.RailPx = new Rect();
            HudLayout.ClearControls();

            // The unit overlays are NOT here any more. They floated a cell and a half above each
            // slot, which put the foe's back row behind the castle wall and made the layer answer
            // by dropping those labels - the rows you attack into were the rows with no numbers.
            // UnitVitals hangs them off each tile's near edge instead, in UI Toolkit, where ♥ and
            // ⚔ have a font that can draw them.
            // NOTHING is painted across the bottom here any more. IMGUI draws AFTER every UI
            // Toolkit panel, so a band painted here lands ON TOP of the hand rather than behind
            // it - that was the dark bar through the cards. The hand owns its own backdrop, and
            // the turn controls moved to the right-edge rail where they overlap nothing.

            if (s.IsOver)
            {
                var over = new GUIStyle(_label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, h / 2f - 20, w, 40), "MATCH OVER — " + s.Outcome, over);

                // ...AND A WAY OUT OF IT.
                //
                // This branch returns before the rail is drawn, so the SETTINGS gear - and the
                // QUIT MATCH behind it, the only exit a duel launched from the main menu has -
                // vanished at the exact moment the match ended. A finished skirmish, and a
                // finished duel against a friend, were dead ends with no controls on them at all:
                // the engine was never put down and, over the wire, the sockets stayed open.
                //
                // Only when nothing ELSE is offering one. A campaign duel already ends on the
                // shell's own result panel, with its own way back to the map.
                var shell = FindShell();
                if (shell == null || !shell.OffersExit)
                {
                    if (Btn(new Rect(w / 2f - 70, h / 2f + 28, 140, 30), "↩ LEAVE", _button))
                        _quitRequested = true;
                }
                return;
            }

            PromptUpkeepOffender(s);

            // ── every panel, behind one guard ───────────────────────────────────────────────
            //
            // OnGUI is a straight-line pass and every control the player has is downstream in it:
            // the mode row, the rail with END TURN and BUILD, the build menu, the charge panel and
            // the choice panel. So ONE exception anywhere in that sequence takes all of them off
            // the screen - not for a frame, but for the rest of the match, because it throws again
            // on the next pass and every pass after it.
            //
            // And it does not look like a crash while it happens. The board is drawn by six other
            // MonoBehaviours with their own LateUpdate, and Unity isolates a throw to the callback
            // it came from - so the grass keeps swaying, the standees keep bobbing, the phase track
            // keeps saying ACTION, and nothing on the screen responds to anything. "The game is
            // frozen in browser", exactly.
            //
            // This does not fix whatever throws. It makes the failure SURVIVABLE and, more to the
            // point, VISIBLE: the player gets a line in the log naming the exception instead of a
            // dead board, and on a build whose only test surface is a public URL that is the
            // difference between a report anyone can act on and "it froze".
            try
            {
                DrawPanels(s, w, h);
            }
            catch (System.Exception e)
            {
                if (!_guiFaulted)
                {
                    _guiFaulted = true;
                    Debug.LogException(e);
                    _match.Note("· HUD error: " + e.GetType().Name + " - " + e.Message);
                }

                // whatever else fails, keep a way to end the turn - and, since the rail stopped
                // being able to resolve combat, a way to finish or drop an attack. EndTurnHandler
                // refuses while declarations stand, so without this pair a fault mid-assault is a
                // turn that can never end: exactly the dead board this guard exists to prevent.
                try
                {
                    DrawSideRail(s, w, h);
                    if (s.Turn == Seat.Local && _match.AttackStanding)
                        DrawAttackRow(s, w, h - BottomH - ModeH + 2);
                }
                catch (System.Exception) { /* the fallback itself must never throw */ }
            }

            if (Time.unscaledTime < _hintUntil && _hint.Length > 0 && !_buildMenuOpen)
                GUI.Label(new Rect(0, h - BottomH - 22, w, 20), _hint, _center);

            // LAST, and outside the guard: leaving tears the match down, and every panel above
            // holds a reference to it.
            if (_quitRequested)
            {
                _quitRequested = false;
                var shell = FindShell();
                if (shell != null) shell.Show(Shell.ShellScreen.MainMenu);
            }
        }

        void DrawPanels(GameState s, float w, float h)
        {
            // the hand is UI Toolkit now (HandBar) - real card faces, same band, same selection
            DrawModeRow(s, w, h);
            DrawSideRail(s, w, h);
            DrawLog(w);
            if (_logOpen) DrawLogHistory(w, h);
            if (_settingsOpen) DrawSettings(w, h);
            // One reset point rather than one beside every "_buildMenuOpen = false": the menu
            // is closed from seven places and a stale pick would rebuild the inspect card the
            // next time it opened.
            if (!_buildMenuOpen) { _buildPickDef = null; _buildHover = null; }

            if (_buildMenuOpen) DrawBuildMenu(s, w, h);
            else if (_upgradeMenuOpen) DrawUpgradeMenu(s, w, h);
            else DrawChargePanel(s, w, h);
            DrawBandRect(w, h);
            DrawChoicePanel(s, w, h);
            DrawAskPanel(w, h);            // LAST: nothing outranks a question about destroying
        }

        /// <summary>
        /// The mouse's selection rectangle.
        ///
        /// Drawn with plain fills and NEVER through Btn or HudLayout.Control: registering it as a
        /// control would make BoardInput refuse taps inside it, which is precisely the region the
        /// gesture is about. It is inside DrawPanels' try, so a throw here is logged once and
        /// survivable rather than taking the whole HUD down.
        ///
        /// The sweep draws no rectangle at all - the cards lighting up under the finger are its
        /// feedback, and on a phone the finger covers a rectangle anyway.
        /// </summary>
        void DrawBandRect(float w, float h)
        {
            if (_input == null) return;
            var band = _input.BandRect;
            if (!band.HasValue) return;

            // panel units are the same units OnGUI draws in once GUI.matrix is scaled
            float k = _scale <= 0f ? 1f : 1f;
            var r = band.Value;
            var box = new Rect(r.x / _scale * k, r.y / _scale * k, r.width / _scale, r.height / _scale);

            float t = Mathf.Max(1f, 1.5f);
            Panel(box, new Color(0.45f, 0.95f, 1f, 0.12f));
            Panel(new Rect(box.x, box.y, box.width, t), BandEdge);
            Panel(new Rect(box.x, box.yMax - t, box.width, t), BandEdge);
            Panel(new Rect(box.x, box.y, t, box.height), BandEdge);
            Panel(new Rect(box.xMax - t, box.y, t, box.height), BandEdge);
        }

        static readonly Color BandEdge = new Color(0.55f, 0.98f, 1f, 0.85f);


        /// <summary>
        /// "This will destroy your own card - go ahead?"
        ///
        /// The only confirm in the game, and it earns its place: playing over one of your own
        /// cards and marching into a full row both raze something you paid for, and neither is
        /// undoable. Everything else on this board is either reversible or obviously what it
        /// looks like, and a game that asks about everything is a game nobody reads.
        ///
        /// The third button is not padding. A tap on your own card in a full row could equally
        /// mean "pick that one up instead", and that reading is the commoner of the two - so it
        /// is offered here rather than costing the player a cancel and a second tap.
        /// </summary>
        void DrawAskPanel(float w, float h)
        {
            var ask = _match.Asking;
            if (ask == null) return;

            const float pw = 320f, rowH = 26f;
            bool three = ask.Instead.HasValue;
            float ph = 30f + 22f + (three ? 2f : 1f) * rowH + 10f;
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            GUI.Label(new Rect(panel.x + 8, panel.y + 5, pw - 16, 20), ask.What, _small);
            GUI.Label(new Rect(panel.x + 8, panel.y + 24, pw - 16, 20), ask.Cost, _small);

            float y = panel.y + 48;
            float half = (pw - 20) / 2f;
            if (Btn(new Rect(panel.x + 8, y, half, rowH - 3), "DESTROY IT", _button))
                Try(_match.ResolveAsk(true));
            if (Btn(new Rect(panel.x + 12 + half, y, half, rowH - 3), "CANCEL", _button))
                _match.ResolveAsk(false);

            if (!three) return;
            y += rowH;
            if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3), "SELECT IT INSTEAD", _button))
            {
                var cell = ask.Instead.Value;
                _match.ResolveAsk(false);
                if (_input != null) _input.SelectFromUi(cell);
            }
        }

        void Try(Rejection why)
        {
            if (why != Rejection.None) Hint(MatchController.Hint(why));
        }

        /// <summary>
        /// Pick who you are before the duel starts. This is not decoration: `deckOf` builds your
        /// 40 cards from your commander's element pools, so the commander IS the deck until the
        /// deck builder lands at M15. Hard-coding fire-vs-water made 35 of the 36 unreachable.
        /// </summary>
        void DrawCommanderSelect()
        {
            float scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
            _scale = scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            HudLayout.TopPx = Screen.height;          // the whole screen is ours; no board yet
            HudLayout.BottomPx = 0;
            HudLayout.MenuPx = new Rect(0, 0, Screen.width, Screen.height);
            HudLayout.LogPx = new Rect();
            HudLayout.RailPx = new Rect();

            Panel(new Rect(0, 0, w, h), PanelColor);

            var title = new GUIStyle(_label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Gold;
            GUI.Label(new Rect(0, 14, w, 26), "CHOOSE YOUR COMMANDER", title);
            GUI.Label(new Rect(0, 40, w, 18),
                "your commander decides your elements, your build menu and your deck", _center);

            var cat = _match.Catalog;
            var all = cat.Commanders;

            const float rowH = 24f;
            float listTop = 66f;
            float listH = h - listTop - 140f;      // 92 for the footer, 48 more for the arena row
            float colW = Mathf.Min(300f, w / 2f - 12f);

            // yours on the left, the opponent's on the right - same list, two picks
            DrawCommanderColumn(new Rect(8, listTop, colW, listH), all, rowH, true);
            DrawCommanderColumn(new Rect(w - colW - 8, listTop, colW, listH), all, rowH, false);

            DrawArenaRow(w, h);

            var youDef = cat.Commander(_pickYou);
            var foeDef = cat.Commander(_pickFoe);
            GUI.Label(new Rect(0, h - 84, w, 18),
                "YOU: " + youDef.Name + " (" + Stat.Hp(youDef.Hp) + " ⚒" + youDef.Workers + ")"
                + "     vs     " + foeDef.Name
                + " (" + Stat.Hp(foeDef.Hp) + " ⚒" + foeDef.Workers + ")", _center);

            if (Btn(new Rect(w / 2f - 150, h - 62, 145, 34), "🎲 RANDOM FOE", _button))
                _pickFoe = all[Mathf.Abs((int)(Time.realtimeSinceStartup * 1000f)) % all.Count].Id;

            if (Btn(new Rect(w / 2f + 5, h - 62, 145, 34), "▶ START DUEL", _bigButton))
            {
                ulong seed = (ulong)System.DateTime.Now.Ticks;
                _match.StartMatch(_pickYou, _pickFoe, seed);
                _selectedHandIndex = -1;
                _buildMenuOpen = false;
                _upgradeMenuOpen = false;
                _upkeepPromptedTurn = -1;
            }

            GUI.Label(new Rect(0, h - 24, w, 18), youDef.Description, _center);
        }

        /// <summary>
        /// Where the duel is fought. Scenery and nothing else - the engine has never heard of a
        /// biome - but it is the first thing anybody sees, so it gets picked with the commanders
        /// rather than buried in an options screen. The campaign will set it from the map tile.
        /// </summary>
        /// <summary>
        /// Pick the ground, or leave it to the roll.
        ///
        /// A duel with nothing chosen lands wherever the opening hash puts it (MatchController
        /// .RollBattlefield) - which is the fix for "every battlefield was the meadow", meadow
        /// being the static default nobody ever changed. Touching a button here says otherwise,
        /// and then that IS the arena; ROLL hands it back.
        ///
        /// A duel over the wire ignores all of this: both peers have to be standing in the same
        /// field, and one player's menu cannot bind the other's.
        /// </summary>
        void DrawArenaRow(float w, float h)
        {
            GUI.Label(new Rect(0, h - 132, w, 16), "ARENA", _center);

            var all = World.Biomes.All;
            bool rolled = !MatchController.ArenaChosen;

            // Eight buttons at a fixed 84 ran off both edges the moment RANDOM joined the row.
            // The row is sized to the screen it is on instead - the labels are short and the
            // build is played on a phone.
            const float gap = 6f;
            int n = all.Length + 1;
            float bw = Mathf.Min(84f, (w - 16f - gap * (n - 1)) / n);
            float total = n * bw + (n - 1) * gap;
            float x = w / 2f - total / 2f;

            var old = GUI.color;
            if (rolled) GUI.color = Gold;
            if (Btn(new Rect(x, h - 114, bw, 24), "RANDOM", _button))
                MatchController.ArenaChosen = false;
            GUI.color = old;
            x += bw + gap;

            for (int i = 0; i < all.Length; i++)
            {
                bool on = !rolled && World.TerrainField.Requested == all[i];
                var was = GUI.color;
                if (on) GUI.color = Gold;
                if (Btn(new Rect(x, h - 114, bw, 24), World.Biomes.NameOf(all[i]), _button))
                {
                    World.TerrainField.Requested = all[i];
                    MatchController.ArenaChosen = true;
                }
                GUI.color = was;
                x += bw + gap;
            }
        }

        void DrawCommanderColumn(Rect area, IReadOnlyList<CommanderDef> all, float rowH, bool mine)
        {
            GUI.Label(new Rect(area.x, area.y - 18, area.width, 16),
                mine ? "YOUR COMMANDER" : "OPPONENT", _small);
            Panel(area, CardBack);

            float contentH = all.Count * rowH;
            var scroll = mine ? _selectScrollYou : _selectScrollFoe;
            scroll = GUI.BeginScrollView(area, scroll,
                new Rect(0, 0, area.width - 20, contentH), false, true);

            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                bool on = (mine ? _pickYou : _pickFoe).Value == c.Id.Value;
                var r = new Rect(2, i * rowH, area.width - 24, rowH - 2);
                if (on) Panel(r, new Color(0.55f, 0.45f, 0.12f, 1f));
                if (GUI.Button(r, "  " + c.Name + (c.Dual ? "  ✦" : ""), _button))
                {
                    if (mine) _pickYou = c.Id; else _pickFoe = c.Id;
                }
            }

            GUI.EndScrollView();
            if (mine) _selectScrollYou = scroll; else _selectScrollFoe = scroll;
        }

        // ---- top band -------------------------------------------------------------------------

        // The status bar is GONE from IMGUI. Both sides' vitals, the turn read-out and the foe's
        // hand are set into the two castle walls now (WallBands), drawn in the UI Toolkit panel -
        // which is the only surface with the gated glyph chain on it. That is not cosmetic: the
        // ♥, ◆ and ⚒ in this bar were being dropped silently, because OnGUI uses the built-in
        // font and the built-in font has none of them. The bar has been reading "FOE 10000 0
        // hand 4" on the deployed build for as long as it has existed.

        /// <summary>
        /// The last few lines of the log, in the RIGHT-HAND COLUMN above the turn rail.
        ///
        /// It used to run from the middle of the screen to the right edge along the top, and the
        /// middle of the top of the screen is exactly where the foe's board is: the panel covered
        /// their cards and, because it correctly blocks taps, covered the cells under them too.
        /// The right edge is the one strip of screen the board never needs - the turn rail already
        /// lives there - so the log stacks on top of the rail and the field is left alone.
        ///
        /// Still auto-hiding after five seconds. What it says is a thing that JUST happened; the
        /// whole match is behind the LOG button on the rail.
        /// </summary>
        void DrawLog(float w)
        {
            var log = _match.Log;
            int lines = Mathf.Min(4, log.Count);
            if (lines <= 0 || _logOpen) return;

            if (log.Count != _lastLogCount)
            {
                _lastLogCount = log.Count;
                _logShownUntil = Time.unscaledTime + 5f;
            }
            if (Time.unscaledTime > _logShownUntil) return;

            // Same width as the history panel, and for the same reason: this is the column that
            // has to hold a sentence. 168 was narrower than "Mistling enters at FoeFront[3]".
            const float panelW = 210f;
            float lineW = panelW - 14f;

            // MEASURE, then draw - the fix the history panel already got and this one did not.
            // Every entry used to be handed one fixed row of exactly Row(_small): anything longer
            // than the column ran off the right edge, and anything that wrapped had everything
            // past the first line sliced off. A log line is a sentence of unknown length, so the
            // only correct row height is the one the style reports for that line at this width.
            //
            // Walking BACKWARDS from the newest also settles what to drop when four wrapped lines
            // will not fit above the rail: the oldest ones. The newest is always drawn, whatever
            // its height, because a panel that appears and says nothing is worse than a tall one.
            float ceiling = TopH + 4f + ShellBarH;      // clear of the shell's ↩ abandon row
            float room = Mathf.Max(Row(_wrap) + 6f, _rail.y - 6f - ceiling);
            float height = 6f;
            int start = log.Count;
            while (start > 0 && log.Count - start < lines)
            {
                float lh = Mathf.Max(Row(_wrap), _wrap.CalcHeight(new GUIContent(log[start - 1]), lineW));
                if (start < log.Count && height + lh > room) break;
                height += lh;
                start--;
            }

            float top = Mathf.Max(ceiling, _rail.y - height - 6f);
            var panel = new Rect(w - panelW - 6f, top, panelW, height);

            Panel(panel, PanelSoft);
            HudLayout.LogPx = new Rect(panel.x * _scale, panel.y * _scale,
                                       panel.width * _scale, panel.height * _scale);
            float y = panel.y + 3f;
            for (int i = start; i < log.Count; i++)
                y += Line(panel.x + 7, y, lineW, log[i], _wrap);
        }


        /// <summary>The whole match, scrollable. Same right-hand column, floor to ceiling.</summary>
        void DrawLogHistory(float w, float h)
        {
            var log = _match.Log;
            const float panelW = 210f;

            // CLEAR OF THE SHELL'S OWN BUTTON, and its CLOSE moved to the far side of the header.
            //
            // This is what "closing the log quits the match" was. GameShell hangs ↩ abandon (or
            // ↩ menu) off the TOP RIGHT of the screen in UI Toolkit, at right: 8, top: TopPx + 6 -
            // and CLOSE was drawn at the top right of a panel pinned to the same corner, inside
            // it. IMGUI and UI Toolkit are separate input paths that never learn about each
            // other's handled events (the same reason the mode row had to be lifted off the hand),
            // so one tap ran BOTH: the log closed and the match was abandoned underneath it.
            //
            // Two changes, either of which would do and both of which are cheap: the panel starts
            // below the shell's row, and CLOSE sits on the LEFT of the header where nothing else
            // in this game has ever put a control.
            float top = TopH + 4f + ShellBarH;
            var panel = new Rect(w - panelW - 6f, top, panelW, h - top - HandH - ModeH - 8f);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            float head = Row(_label);
            if (Btn(new Rect(panel.x + 6, panel.y + 4, 50, head), "CLOSE", _small))
                _logOpen = false;
            GUI.Label(new Rect(panel.x + 62, panel.y + 4, panel.width - 70, head),
                      "MATCH LOG", _label);

            // Every line is measured and WRAPPED. A log line is a sentence of unknown length -
            // "Mistling enters at FoeFront[3]" is already wider than this column on a phone - and
            // a fixed row height silently sliced the bottom off every one of them.
            var body = _wrap;
            float bodyTop = panel.y + 8f + head;
            var view = new Rect(panel.x + 6, bodyTop, panel.width - 12, panel.yMax - bodyTop - 6f);

            float total = 4f;
            float lineW = view.width - 18f;
            for (int i = 0; i < log.Count; i++)
                total += Mathf.Max(Row(body), body.CalcHeight(new GUIContent(log[i]), lineW));

            var content = new Rect(0, 0, lineW, Mathf.Max(view.height, total));

            // pinned to the BOTTOM on open, because the interesting end of a log is the new end
            _logScroll = GUI.BeginScrollView(view, _logScroll, content);
            float ly = 2f;
            for (int i = 0; i < log.Count; i++)
                ly += Line(2f, ly, lineW - 4f, log[i], body);
            GUI.EndScrollView();
        }

        /// <summary>
        /// Camera angle and the way out. Both belong to the player rather than to the match, so
        /// they sit behind one button instead of taking rail space of their own.
        /// </summary>
        void DrawSettings(float w, float h)
        {
            const float panelW = 190f, panelH = 218f;
            var panel = new Rect(w * 0.5f - panelW * 0.5f, h * 0.5f - panelH * 0.5f, panelW, panelH);
            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            float x = panel.x + 10, cw = panel.width - 20, y = panel.y + 8;
            GUI.Label(new Rect(x, y, cw, 18), "SETTINGS", _label);
            y += 24;

            GUI.Label(new Rect(x, y, cw, 14), "Camera", _small);
            y += 16;
            bool tilted = _input == null || _input.Tilted;
            if (Btn(new Rect(x, y, cw, 24), tilted ? "TILTED" : "TOP-DOWN", _button) && _input != null)
                _input.Tilted = !tilted;
            y += 30;

            // THE BOARD'S OWN MARKING, in five steps from the full colour wash down to nothing.
            //
            // It cycles rather than listing five buttons: the modes are ordered - each shows
            // strictly less than the one before - so "press until it looks right" is the whole
            // interaction, and it costs one line instead of five.
            //
            // Nothing here touches a rule. The cells, their colliders and every legality probe are
            // identical in all five; even at OFF a lit cell still lights, because those lights are
            // the engine talking rather than decoration.
            GUI.Label(new Rect(x, y, cw, 14), "Battlefield overlay", _small);
            y += 16;
            if (Btn(new Rect(x, y, cw, 24), BoardView.OverlayName(BoardView.Overlay), _button))
                BoardView.Overlay = (BoardView.BoardOverlay)
                    (((int)BoardView.Overlay + 1) % 5);
            y += 30;

            if (Btn(new Rect(x, y, cw, 24), "RESUME", _button))
                _settingsOpen = false;
            y += 30;

            // The one destructive control on the screen, so it is the one that says what it does.
            //
            // It ASKS rather than acts. Leaving ends the match, and this button is drawn from the
            // middle of OnGUI - so quitting here nulled the engine and then let the rest of the
            // pass carry on drawing panels against it, which throws the moment one of them is a
            // choice panel reaching for the catalog. The request is honoured after the pass.
            if (Btn(new Rect(x, y, cw, 24), "QUIT MATCH", _button))
            {
                _settingsOpen = false;
                _quitRequested = true;
            }
        }

        // ---- bottom band ----------------------------------------------------------------------

        /// <summary>The contextual strip above the hand: play modes, charge menu, upkeep settle.</summary>
        void DrawModeRow(GameState s, float w, float h)
        {
            // ABOVE the risen card, not across it.
            //
            // This row used to sit in a reserved band that the picked card rose straight through,
            // so SUMMON and the card occupied the same pixels - and a tap there hit BOTH, because
            // IMGUI and UI Toolkit are separate input paths that never learn about each other's
            // handled events. The button ran, and the card's own PointerDown ran too and toggled
            // the selection off underneath it. Clearing the card's full height is the fix: no
            // overlap, no double delivery.
            float lifted = HandH * Cards.HandBar.CardToPeek;
            float by = _selectedHandIndex >= 0 ? h - lifted - ModeH + 2
                                               : h - BottomH - ModeH + 2;
            var hand = s.P(Seat.Local).Hand;
            bool myTurn = s.Turn == Seat.Local;

            // A STANDING ATTACK gets a LINE OF ITS OWN, above everything else, and does not
            // consume the row.
            //
            // It used to be one of the mutually exclusive branches below, which was survivable
            // only while the rail could also resolve combat. It cannot any more, and
            // EndTurnHandler refuses the turn while declarations stand - so every branch that
            // outranked it (an armed play, a picked hand card) became a way to hold an attack you
            // could neither swing nor drop on a turn you could not end. Its own line costs 26
            // logical units of a corner the board is not using and removes the whole class.
            //
            // Driven by the ENGINE's declarations rather than by the view's aim. They are the same
            // thing almost always, and the exception is the one that matters: a reconnect hands
            // the match back with its combat intact and the aim gone.
            if (myTurn && _match.AttackStanding)
            {
                DrawAttackRow(s, w, by - ModeH);
            }

            // an armed play: guidance only
            if (_match.Pending != MatchController.Intent.None)
            {
                GUI.Label(new Rect(0, by, w, 24),
                    "tap a lit cell to place — tap the card again to cancel", _center);
                return;
            }

            // selected hand card during your action: mode buttons
            if (_selectedHandIndex >= 0 && _selectedHandIndex < hand.Count
                && myTurn && s.Phase == TurnPhase.Action)
            {
                var id = hand[_selectedHandIndex].Id;
                CreatureCard c;
                SpellCard sp;
                if (_match.Engine.Catalog.TryCreature(id, out c))
                {
                    if (Btn(new Rect(w / 2f - 125, by, 120, 24), "SUMMON ◆" + c.Cost, _button))
                        Arm(Rules.PlayMode.Summon);
                    if (Btn(new Rect(w / 2f + 5, by, 120, 24), "SET ◆1", _button))
                        Arm(Rules.PlayMode.Set);
                }
                else if (_match.Engine.Catalog.TrySpell(id, out sp))
                {
                    if (sp.IsTrap)
                    {
                        if (Btn(new Rect(w / 2f - 60, by, 120, 24), "SET TRAP ◆1", _button))
                            Arm(Rules.PlayMode.SetTrap);
                    }
                    else if (!SpellTargeting.HasAnyTarget(s, sp, Seat.Local))
                        GUI.Label(new Rect(0, by, w, 24), "no legal target for " + sp.Name, _center);
                    else if (Btn(new Rect(w / 2f - 60, by, 120, 24), "CAST ◆" + sp.Cost, _button))
                        Arm(Rules.PlayMode.Cast);
                }
                return;
            }

            // a selected board cell: charge menu or upkeep settle
            if (_input != null && _input.Selected.HasValue && myTurn)
            {
                var cell = _input.Selected.Value;

                // moving banked ◆: the next board tap names the destination
                if (_match.SendFrom.HasValue)
                {
                    GUI.Label(new Rect(0, by, w, 24),
                        "tap one of your cards to store the ◆ there — or tap this one to cancel",
                        _center);
                    return;
                }

                // A GROUP outranks every per-cell control below: those all act on one card, and
                // with five selected the player is not asking about one card.
                if (_input != null && _input.Group.Count > 1)
                {
                    float gw = Mathf.Min(w - 16f, 440f);
                    float gx = (w - gw) / 2f;
                    GUI.Label(new Rect(gx, by, gw - 40f, 24),
                        "⚔" + _input.Group.Count + " selected — tap a target, or ⚔ WALL", _center);
                    if (Btn(new Rect(gx + gw - 34f, by, 34f, 24), "✕", _button))
                        _input.ClearSelectionFromUi();
                    return;
                }

                var owned = s.At(cell);
                bool canSend = owned != null && owned.Owner == Seat.Local && owned.Bank > 0
                    && s.Phase == TurnPhase.Action;

                var ch = s.At(cell) as ChargeUnit;
                if (ch != null && ch.Owner == Seat.Local && s.Phase == TurnPhase.Action)
                {
                    // the FULL stepper lives in the charge panel; this row just says where it is
                    GUI.Label(new Rect(0, by, w, 24),
                        ch.Card.Name + " — ◆" + ch.Invested + "/" + ch.Card.Cost
                        + (ch.Invested >= ch.Card.Cost ? " · ready to flip" : " · pour below"),
                        _center);
                    return;
                }

                // an aimed attacker: enemy cells are lit; the wall and the worker stacks are buttons
                var atk = s.At(cell) as CreatureUnit;
                if (atk != null && atk.Owner == Seat.Local && s.Phase == TurnPhase.Action
                    && !atk.IsWorker && !atk.Sick && !atk.Tapped)
                {
                    var wall = new DeclareAttackCommand(Seat.Local, cell, atk.Id, new WallTarget(Seat.Remote));
                    bool wallOk = _match.Probe(wall) == Rejection.None;

                    // Worker stacks are attackable by the rules and were unreachable from the
                    // board, because a pool is not a cell - it needs its own button.
                    var zones = new[] { WorkerZone.Back, WorkerZone.Front, WorkerZone.Center };
                    var legalZones = new List<WorkerZone>();
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var st = new DeclareAttackCommand(Seat.Local, cell, atk.Id,
                            new WorkerStackTarget(Seat.Remote, zones[i]));
                        if (_match.Probe(st) == Rejection.None) legalZones.Add(zones[i]);
                    }

                    if (wallOk || legalZones.Count > 0)
                    {
                        float x = w / 2f - 125;
                        if (wallOk)
                        {
                            float ww = legalZones.Count > 0 ? 130 : 250;
                            if (Btn(new Rect(x, by, ww, 24), "⚔ WALL", _button))
                                Declare(cell, new WallTarget(Seat.Remote), "the wall");
                            x += ww + 5;
                        }
                        float zw = legalZones.Count > 0
                            ? Mathf.Min(60f, (w / 2f + 125 - x) / legalZones.Count - 4) : 0;
                        for (int i = 0; i < legalZones.Count; i++)
                        {
                            var z = legalZones[i];
                            int n = s.P(Seat.Remote).Workers[(int)z].Count;
                            if (Btn(new Rect(x, by, zw, 24), "⚒" + ZoneTag(z) + n, _button))
                                Declare(cell, new WorkerStackTarget(Seat.Remote, z),
                                        "the " + ZoneName(z) + " workers");
                            x += zw + 4;
                        }
                        return;
                    }
                }

                // your structure: the in-place upgrade chain, and moving its banked ◆ off it.
                // Both fit, because a structure about to be upgraded is exactly when you want to
                // decide where its stored mana goes.
                var bld = s.At(cell) as StructureUnit;
                bool canUpgrade = bld != null && bld.Owner == Seat.Local
                    && s.Phase == TurnPhase.Action && UpgradeTargetsFor(s, cell, bld).Count > 0;

                if (canUpgrade || canSend)
                {
                    float bw = (canUpgrade && canSend) ? 122f : 250f;
                    float x = w / 2f - 125;
                    if (canUpgrade)
                    {
                        if (Btn(new Rect(x, by, bw, 24),
                                _upgradeMenuOpen ? "CLOSE" : "⬆ UPGRADE", _button))
                            _upgradeMenuOpen = !_upgradeMenuOpen;
                        x += bw + 6;
                    }
                    if (canSend && Btn(new Rect(x, by, bw, 24),
                            "◆ SEND " + owned.Bank, _button))
                    {
                        _upgradeMenuOpen = false;
                        _match.BeginSendMana(cell);
                    }
                    return;
                }

                // Upkeep settle: Move is the lit cells; Pay / Sacrifice live here
                var cr = s.At(cell) as CreatureUnit;
                if (cr != null && cr.Owner == Seat.Local && !cr.IsWorker && s.Phase == TurnPhase.Upkeep)
                {
                    var cat = _match.Engine.Catalog;
                    var zone = Rules.Board.ZoneForRow(Seat.Local, cell.Row);
                    int deficit = Upkeep.ZoneDeficit(s, Seat.Local, zone, cat);
                    int pay = Mathf.Min(cr.Upkeep, deficit);

                    GUI.enabled = pay > 0 && !cr.PaidUpkeep && s.P(Seat.Local).Mana >= pay;
                    if (Btn(new Rect(w / 2f - 125, by, 120, 24), "PAY ◆" + pay, _button))
                        Try(new UpkeepPayCommand(Seat.Local, cell, cr.Id));
                    GUI.enabled = true;
                    if (Btn(new Rect(w / 2f + 5, by, 120, 24), "SACRIFICE", _button))
                        Try(new UpkeepSacrificeCommand(Seat.Local, cell, cr.Id));
                    return;
                }
            }

            // idle upkeep guidance when the harvest is locked
            if (myTurn && s.Phase == TurnPhase.Upkeep
                && !Upkeep.HarvestUnlocked(s, Seat.Local, _match.Engine.Catalog))
                GUI.Label(new Rect(0, by, w, 24),
                    "shortfall ⚒" + Upkeep.TotalDeficit(s, Seat.Local, _match.Engine.Catalog)
                    + " — move the flagged creature to a lit cell, PAY its keep, or SACRIFICE it",
                    _center);
        }

        /// <summary>
        /// The attacker's half of the exchange, spelled out: how many are in, what they are on,
        /// and the two buttons that END the choosing.
        ///
        /// The defender has not been ASKED anything yet - the declarations are deferred, so
        /// ⚔ ATTACK is the moment they are first asked, and until then the group can still grow
        /// and the whole thing can still be taken back. (In a duel they can SEE it forming: the
        /// declaration crosses the wire immediately and the attackers lie down on their board too.
        /// The tell is accepted; see WithdrawAttackCommand.)
        ///
        /// A row that fits: laid out from a centred band rather than from fixed offsets either
        /// side of the middle, which ran off the left edge of a portrait phone.
        ///
        /// Its own method, on its own line, because it is drawn from TWO places and must never be
        /// crowded out of either. It is the only way to confirm or cancel an attack now, and
        /// EndTurnHandler refuses the turn while declarations stand - so anything that took this
        /// row off the screen would leave a player holding an attack they can neither swing nor
        /// drop, on a turn they cannot end. The HUD's fault path draws it for the same reason.
        /// </summary>
        void DrawAttackRow(GameState s, float w, float by)
        {
            float rowW = Mathf.Min(w - 16f, 520f);
            float rx = (w - rowW) / 2f;

            GUI.Label(new Rect(rx, by, rowW - 216f, 24),
                "⚔" + _match.AssaultSize + " on " + _match.StandingAttackLabel
                + (_match.Assault != null ? " — tap more to join" : ""), _center);

            // GREYED while a choice is parked, the way the rail already is. The engine refuses
            // every command but the answer to a parked request (CommandProcessor: ChoicePending),
            // so a lit button there is a button that fails - and a player whose taps stop working
            // reports that the game froze, which is the same complaint the rail's own guard and
            // its "waiting on foe" label exist to answer.
            GUI.enabled = s.Pending == null;

            if (Btn(new Rect(rx + rowW - 212f, by, 118f, 24), "⚔ ATTACK", _button))
                Confirm();

            // CANCEL, not LATER. It used to mean "stop talking about it": the declarations stood,
            // the attackers stayed spent for the turn, and the only way to spend them on anything
            // was to attack after all. It takes the attack back now, and the creatures stand up
            // again. A refusal is SAID - a defender who has already committed a blocker is the one
            // case that can refuse, and a button that silently does nothing reads as broken.
            if (Btn(new Rect(rx + rowW - 90f, by, 90f, 24), "✕ CANCEL", _button))
            {
                var no = _match.WithdrawAssault();
                if (no != Rejection.None) Hint(MatchController.Hint(no));
            }

            GUI.enabled = true;
        }

        /// <summary>
        /// The turn controls, hugging the RIGHT EDGE at mid-height - `#boardBtns` in the reference
        /// stylesheet, which calls it the Master Duel coin position.
        ///
        /// They used to be a full-width band across the bottom, and between that, the mode row and
        /// the hand the board was giving up a quarter of a 480-unit screen to three strips of
        /// chrome. A rail costs the board nothing: it overlays a corner the board is not using, and
        /// BoardInput refuses taps inside it (HudLayout.RailPx) so nothing falls through to a cell.
        ///
        /// The phase track above the buttons is the reference's too: a compact vertical list that
        /// lights the current phase, with Combat indented as the sub-phase of Action that it is.
        /// A turn machine the player cannot see is a turn machine the player fights.
        /// </summary>
        void DrawSideRail(GameState s, float w, float h)
        {
            const float railW = 92f;
            float x = w - railW - 6f;

            bool mine = s.Turn == Seat.Local;
            bool acting = mine && s.Phase != TurnPhase.End;

            // measure first, so the rail can be centred vertically and its blocker rect published
            float trackH = 5 * 15f + 8f;
            float btnH = acting ? 34f : 0f;
            float buildH = (acting && s.Phase == TurnPhase.Action) ? 26f : 0f;
            float totalH = trackH + (btnH > 0 ? btnH + 5f : 0f) + (buildH > 0 ? buildH + 4f : 0f);

            // LOG and the settings gear are always here, whoever's turn it is - the log because
            // the interesting lines are the ones the FOE just made, and settings because a player
            // who wants out should not have to wait for their turn to ask.
            totalH += 24f;

            float y = Mathf.Max(TopH + 8f, h * 0.5f - totalH * 0.5f);
            var rail = new Rect(x, y, railW, totalH);
            _rail = rail;
            Panel(rail, PanelSoft);
            HudLayout.RailPx = new Rect(rail.x * _scale, rail.y * _scale,
                                        rail.width * _scale, rail.height * _scale);

            DrawPhaseTrack(s, new Rect(x + 4, y + 4, railW - 8, trackH - 8));
            y += trackH;

            // A PARKED CHOICE, said out loud. While s.Pending is set the engine refuses every
            // command that is not an answer to it, so if the answer belongs to the other seat this
            // board takes no input at all - and until now it said nothing about why. A player
            // whose taps stop working reports that the game froze, and they are not wrong.
            if (s.Pending != null && s.Pending.Responder != Seat.Local)
            {
                if (_pendingSince <= 0f) _pendingSince = Time.unscaledTime;
                float held = Time.unscaledTime - _pendingSince;
                if (held > 1.5f)
                {
                    var wait = new GUIStyle(_tiny) { wordWrap = true };
                    wait.normal.textColor = held > 8f ? new Color(1f, 0.55f, 0.4f) : Gold;
                    GUI.Label(new Rect(x + 4, y, railW - 8, 26),
                              "waiting on foe " + Mathf.FloorToInt(held) + "s", wait);
                }
            }
            else if (AwaitingBlockers)
            {
                _pendingSince = 0f;
                var howto = new GUIStyle(_tiny) { wordWrap = true };
                howto.normal.textColor = Gold;
                GUI.Label(new Rect(x + 4, y, railW - 8, 30), "drag across your defenders", howto);
            }
            else _pendingSince = 0f;

            float footY = rail.yMax - 22f;
            if (Btn(new Rect(x + 5, footY, railW - 34, 18f), "LOG", _small))
            {
                _logOpen = !_logOpen;
                _settingsOpen = false;
                _buildMenuOpen = false;
                _logScroll.y = float.MaxValue;      // open on the newest line
            }
            // "SET", not a gear glyph. OnGUI draws with the built-in font, which has no ⚙ any
            // more than it had the ♥ that drove the vitals out of IMGUI in the first place.
            if (Btn(new Rect(x + railW - 27, footY, 22f, 18f), "SET", _tiny))
            {
                _settingsOpen = !_settingsOpen;
                _logOpen = false;
                _buildMenuOpen = false;
            }

            if (!acting) { _buildMenuOpen = false; return; }

            // THE RAIL DOES NOT ATTACK.
            //
            // It used to: with declarations standing, this button became "⚔ N" and resolved the
            // combat. So the same corner of the screen that says "end my turn" also said "swing",
            // the count that meant "N creatures are committed" looked like part of the phase
            // track it sits under, and the attack could be confirmed from two places at once while
            // it could be called off from only one. Confirming and cancelling an attack are the
            // same decision and they belong together, under the board, next to the creatures the
            // decision is about (DrawModeRow).
            //
            // END TURN still refuses while an attack stands - EndTurnHandler answers
            // DeclarationsPending - and the hint for that rejection points back down at the row.
            string caption = s.Phase == TurnPhase.Upkeep ? "HARVEST"
                : s.Phase == TurnPhase.Draw ? "DRAW"
                : "END TURN";

            GUI.enabled = s.Pending == null;
            if (Btn(new Rect(x + 5, y + 5, railW - 10, btnH), caption, _button))
            {
                _selectedHandIndex = -1;
                _buildMenuOpen = false;
                _match.CancelPending();
                Try(s.Phase == TurnPhase.Upkeep ? new HarvestCommand(Seat.Local)
                    : s.Phase == TurnPhase.Draw ? (ICommand)new DrawForTurnCommand(Seat.Local)
                    : new EndTurnCommand(Seat.Local));
            }
            GUI.enabled = true;
            y += btnH + 5f;

            if (buildH > 0f
                && Btn(new Rect(x + 5, y + 4, railW - 10, buildH),
                              _buildMenuOpen ? "CLOSE" : "BUILD", _button))
            {
                _buildMenuOpen = !_buildMenuOpen;
                _selectedHandIndex = -1;
                _match.CancelPending();
            }
        }

        static readonly string[] PhaseNames = { "UPKEEP", "DRAW", "ACTION", "COMBAT", "END" };

        void DrawPhaseTrack(GameState s, Rect area)
        {
            int now = s.Phase == TurnPhase.Upkeep ? 0
                    : s.Phase == TurnPhase.Draw ? 1
                    : s.Phase == TurnPhase.End ? 4
                    : (s.Combat.HasDeclarations ? 3 : 2);

            var step = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < PhaseNames.Length; i++)
            {
                bool sub = i == 3;                       // Combat is indented under Action
                var r = new Rect(area.x + (sub ? 9f : 0f), area.y + i * 15f,
                                 area.width - (sub ? 9f : 0f), 14f);

                if (i == now) Panel(r, sub ? new Color(0.72f, 0.42f, 0.22f, 0.95f) : Gold * 0.9f);

                step.normal.textColor = i == now ? new Color(0.10f, 0.08f, 0.04f)
                                      : i < now ? new Color(0.42f, 0.54f, 0.37f)
                                      : new Color(0.54f, 0.51f, 0.60f);
                GUI.Label(r, (sub ? "↳" : "") + PhaseNames[i], step);
            }
        }

        // ── build menu geometry (logical units) ─────────────────────────────────────────
        const float NodeW = 134f;
        const float NodeH = 30f;
        const float NodeGapX = 16f;      // the arrow gutter between tiers
        const float NodeGapY = 3f;

        /// <summary>
        /// The tier label runs down the LEFT of the tree rather than sitting on a row of its own.
        ///
        /// As a row each heading cost a full line and pushed the ten-row tree past the board band,
        /// so a tech tree - the one thing that has to be read whole - arrived scrolling. The tree
        /// is 434 wide inside a band about a thousand wide, so the space was there the entire time.
        /// </summary>
        const float GutterW = 112f;
        const float GutterGap = 10f;

        /// <summary>
        /// The build menu as a tech tree.
        ///
        /// The flat list it replaced could not express either relation in the data. Prerequisites
        /// are group headings ("REQUIRES A FORGE"); upgrades run left to right along a row with an
        /// arrow between tiers, so a branch like Outpost → Cannon Tower / Bastion is visible
        /// before you commit ◆2 to the Outpost.
        ///
        /// Nothing is hidden. A tier you cannot reach is drawn greyed WITH THE REASON on its
        /// second line, because "why is this dim" is the question a hidden row cannot answer, and
        /// the reasons differ in kind: too expensive, nowhere to stand, prerequisite missing, or
        /// upgrade-only. Selecting any node - reachable or not - shows its card on the left.
        ///
        /// Two taps to build, and that is for touch. There is no hover on a phone, so a menu that
        /// built on first tap built blind; the first tap highlights and inspects, the second
        /// commits. A mouse gets the same two clicks rather than a second rule to learn.
        /// </summary>
        void DrawBuildMenu(GameState s, float w, float h)
        {
            var cat = _match.Engine.Catalog;
            var rows = BuildTree.Build(s, Seat.Local, cat);

            int cols = 1;
            float contentH = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                int span = rows[i].Indent + rows[i].Nodes.Count;
                if (span > cols) cols = span;
                contentH += NodeH + NodeGapY;
            }
            contentH -= NodeGapY;

            float treeW = cols * NodeW + (cols - 1) * NodeGapX;
            float fullW = GutterW + GutterGap + treeW;
            float pw = Mathf.Min(fullW + 20f, w - 24f);
            float regionTop = TopH + 6 + ShellBarH;
            float regionBottom = h - BottomH - 6;
            float ph = Mathf.Min(contentH + 14f, regionBottom - regionTop);
            float py = regionTop + (regionBottom - regionTop - ph) / 2f;
            var panel = new Rect(w / 2f - pw / 2f, py, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            var view = new Rect(panel.x + 4, panel.y + 7, pw - 8, ph - 14);
            bool bars = contentH > view.height;
            float innerW = bars ? pw - 28 : pw - 8;

            // Captured BEFORE BeginScrollView, because IMGUI translates mousePosition into the
            // view's content space once inside it - and keeps translating it for a pointer that is
            // nowhere near the panel, so a node rect can "contain" a mouse out on the board.
            // Test containment in the outer space first, then map the point in by hand.
            var mouse = Event.current.mousePosition;
            bool inView = view.Contains(mouse);
            var contentMouse = mouse - view.position + _buildScroll;

            _buildScroll = GUI.BeginScrollView(view, _buildScroll,
                new Rect(0, 0, innerW, contentH), fullW > innerW, bars);

            StructureDef hovered = null;
            float y = 0f;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                // Set against the whole group in the gutter, so a four-row tier reads as one tier.
                if (row.Header != null)
                {
                    float span = row.GroupSpan * (NodeH + NodeGapY) - NodeGapY;
                    GUI.Label(new Rect(0, y, GutterW, span), row.Header, _group);
                }

                for (int k = 0; k < row.Nodes.Count; k++)
                {
                    var node = row.Nodes[k];
                    int col = row.Indent + k;
                    float x = GutterW + GutterGap + col * (NodeW + NodeGapX);
                    var r = new Rect(x, y, NodeW, NodeH);

                    // The arrow into this node - drawn from the gutter to its left, so a branch
                    // row's first node still shows what it grew out of.
                    if (col > 0)
                        GUI.Label(new Rect(x - NodeGapX, y, NodeGapX, NodeH), ">", _arrow);

                    if (inView && r.Contains(contentMouse)) hovered = node.Def;

                    bool picked = _buildPickDef != null && _buildPickDef.ExportKey == node.Def.ExportKey;
                    if (picked) Panel(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Gold * 0.8f);

                    // A picked node that CAN be built says so, because a selection ring alone does
                    // not tell the player that the next tap is the one that spends the mana.
                    string second =
                        picked && node.Actionable ? "tap again to place"
                        : node.State == BuildNodeState.Ready ? ReadyLine(node.Def)
                        : node.Note;

                    if (GUI.Button(r, node.Def.Name + "\n" + second, StyleFor(node.State)))
                        OnBuildNode(node);
                }

                y += NodeH + NodeGapY;
            }

            GUI.EndScrollView();

            // Hover wins over the pick so a mouse can sweep the tree and read each card, but the
            // pick is what survives a finger lifting off - which is the only state a phone has.
            // Repaint only: the layout pass carries a stale pointer and would blink the card.
            if (Event.current.type == EventType.Repaint) _buildHover = hovered;
        }

        /// <summary>
        /// The price line. A Vault is the only structure at zero support and saying "+0 workers"
        /// about it is noise, so the clause is dropped rather than printed empty; one worker is
        /// singular for the same reason a card that reads "+1 workers" looks unfinished.
        /// </summary>
        static string ReadyLine(StructureDef d)
        {
            string s = d.Cost + " mana";
            if (d.Support == 0) return s;
            int n = d.Support < 0 ? -d.Support : d.Support;
            return s + "   " + (d.Support > 0 ? "+" : "-") + n + (n == 1 ? " worker" : " workers");
        }

        /// <summary>First tap highlights and inspects; a second tap on the same node commits.</summary>
        void OnBuildNode(BuildNode node)
        {
            if (_buildPickDef == null || _buildPickDef.ExportKey != node.Def.ExportKey)
            {
                _buildPickDef = node.Def;
                return;
            }

            if (!node.Actionable) return;

            _match.BeginBuild(node.Def);
            _buildMenuOpen = false;
            _buildPickDef = null;
            if (_match.LegalCells.Count == 0)
            {
                _match.CancelPending();
                Hint("No legal cell for the " + node.Def.Name);
            }
        }

        GUIStyle StyleFor(BuildNodeState st)
        {
            switch (st)
            {
                case BuildNodeState.Ready: return _nodeReady;
                case BuildNodeState.NoMana: return _nodeCost;
                case BuildNodeState.UpgradeReady: return _nodeUpOpen;
                case BuildNodeState.UpgradeLater:
                case BuildNodeState.UpgradeLocked: return _nodeUp;
                default: return _nodeLocked;
            }
        }

        /// <summary>
        /// What the build menu wants shown in the inspect card, or null. HandBar owns that panel -
        /// it is a UI Toolkit CardFace and this is IMGUI - so the menu publishes its subject
        /// rather than drawing a second, worse card of its own.
        /// </summary>
        public StructureDef InspectStructure
        {
            get
            {
                if (!_buildMenuOpen) return null;
                return _buildHover != null ? _buildHover : _buildPickDef;
            }
        }

        /// <summary>
        /// The charge panel (the JS `drawPanel`, 14_spells_traps.js:135-160). This is the whole
        /// point of setting a card face-down: you drip ◆ into it across turns and flip it when it
        /// is paid off - so an all-or-nothing "fill to cost" button, which the engine simply
        /// REJECTS when you cannot afford the whole remainder, made the mechanic unreachable.
        ///
        /// Pouring past the cost is deliberate too: the surplus banks onto the unit when it
        /// flips, which is how a creature arrives already carrying mana.
        /// </summary>
        void DrawChargePanel(GameState s, float w, float h)
        {
            if (_input == null || !_input.Selected.HasValue) { _chargeAmount = 0; return; }
            if (s.Turn != Seat.Local || s.Phase != TurnPhase.Action) { _chargeAmount = 0; return; }
            if (_match.SendFrom.HasValue) return;

            var cell = _input.Selected.Value;
            var ch = s.At(cell) as ChargeUnit;
            if (ch == null || ch.Owner != Seat.Local) { _chargeAmount = 0; return; }

            if (_chargeCellId != ch.Id) { _chargeCellId = ch.Id; _chargeAmount = 0; }

            int mana = s.P(Seat.Local).Mana;
            int remaining = Mathf.Max(0, ch.Card.Cost - ch.Invested);
            _chargeAmount = Mathf.Clamp(_chargeAmount, 0, mana);

            const float pw = 300f, rowH = 28f;
            float ph = 152f;
            float regionTop = TopH + 6 + ShellBarH;
            float regionBottom = h - BottomH - 6;
            ph = Mathf.Min(ph, regionBottom - regionTop);
            var panel = new Rect(w / 2f - pw / 2f, regionBottom - ph, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            float y = panel.y + 6;
            GUI.Label(new Rect(panel.x + 8, y, pw - 16, 18),
                ch.Card.Name + "  " + Stat.Atk(ch.Card.Attack) + "/" + Stat.Hp(ch.Card.Health), _small);
            y += 18;

            int surplus = Mathf.Max(0, ch.Invested + _chargeAmount - ch.Card.Cost);
            GUI.Label(new Rect(panel.x + 8, y, pw - 16, 18),
                "invested ◆" + ch.Invested + " / ◆" + ch.Card.Cost + "   ·   your ◆" + mana
                + (surplus > 0 ? "   ·   ◆" + surplus + " would bank" : ""), _small);
            y += 22;

            // stepper
            if (Btn(new Rect(panel.x + 8, y, 40, rowH - 2), "−", _button))
                _chargeAmount = Mathf.Max(0, _chargeAmount - 1);
            GUI.Label(new Rect(panel.x + 52, y + 4, 60, 20), "◆" + _chargeAmount, _center);
            if (Btn(new Rect(panel.x + 116, y, 40, rowH - 2), "+", _button))
                _chargeAmount = Mathf.Min(mana, _chargeAmount + 1);

            GUI.enabled = remaining > 0 && mana > 0;
            if (Btn(new Rect(panel.x + 162, y, 60, rowH - 2), "FILL", _button))
                _chargeAmount = Mathf.Min(mana, remaining);
            GUI.enabled = mana > 0;
            if (Btn(new Rect(panel.x + 228, y, 64, rowH - 2), "ALL ◆" + mana, _button))
                _chargeAmount = mana;
            GUI.enabled = true;
            y += rowH + 4;

            GUI.enabled = _chargeAmount > 0 && _chargeAmount <= mana;
            if (Btn(new Rect(panel.x + 8, y, (pw - 24) / 2f, rowH), "POUR ◆" + _chargeAmount, _button))
            {
                Try(new PourIntoChargeCommand(Seat.Local, cell, ch.Id, _chargeAmount));
                _chargeAmount = 0;
            }
            GUI.enabled = ch.Invested >= ch.Card.Cost;
            int bankOnFlip = Mathf.Max(0, ch.Invested - ch.Card.Cost);
            if (Btn(new Rect(panel.x + 16 + (pw - 24) / 2f, y, (pw - 24) / 2f, rowH),
                    bankOnFlip > 0 ? "FLIP (bank ◆" + bankOnFlip + ")" : "FLIP UP", _button))
            {
                Try(new FlipChargeCommand(Seat.Local, cell, ch.Id));
                _chargeAmount = 0;
            }
            GUI.enabled = true;
        }

        /// <summary>
        /// The in-place upgrade chain (foundry → keep → citadel, outpost → tower | bastion, and
        /// the rest). A whole M7 subsystem that had no way into it from the board.
        /// </summary>
        void DrawUpgradeMenu(GameState s, float w, float h)
        {
            if (!_upgradeMenuOpen) return;
            if (_input == null || !_input.Selected.HasValue) { _upgradeMenuOpen = false; return; }

            var cell = _input.Selected.Value;
            var bld = s.At(cell) as StructureUnit;
            if (bld == null || bld.Owner != Seat.Local || s.Phase != TurnPhase.Action)
            {
                _upgradeMenuOpen = false;
                return;
            }

            var targets = UpgradeTargetsFor(s, cell, bld);
            if (targets.Count == 0) { _upgradeMenuOpen = false; return; }

            const float rowH = 28f, pw = 300f;
            float ph = targets.Count * rowH + 34;
            float regionTop = TopH + 6 + ShellBarH;
            float regionBottom = h - BottomH - 6;
            ph = Mathf.Min(ph, regionBottom - regionTop);
            var panel = new Rect(w / 2f - pw / 2f, regionBottom - ph, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            GUI.Label(new Rect(panel.x + 8, panel.y + 5, pw - 16, 20),
                "UPGRADE " + bld.DefId.Value + "  (" + Stat.Hp(bld.Hp) + ")", _small);

            float y = panel.y + 28;
            for (int i = 0; i < targets.Count; i++)
            {
                var def = targets[i];
                var cmd = new UpgradeStructureCommand(Seat.Local, cell, bld.Id, def.Bid);
                var why = _match.Probe(cmd);
                GUI.enabled = why == Rejection.None;
                string label = def.Name + "   ◆" + def.Cost + "   " + Stat.Hp(def.MaxHp)
                             + "   ⚒" + (def.Support >= 0 ? "+" : "") + def.Support;
                if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
                {
                    Try(cmd);
                    _upgradeMenuOpen = false;
                }
                GUI.enabled = true;
                if (why != Rejection.None)
                    GUI.Label(new Rect(panel.x + 12, y + 5, pw - 24, 18),
                        "                              " + MatchController.Hint(why), _tiny);
                y += rowH;
            }
        }

        /// <summary>Every tier this structure could become - the menu, before legality.</summary>
        List<StructureDef> UpgradeTargetsFor(GameState s, CellRef cell, StructureUnit b)
        {
            var outp = new List<StructureDef>();
            if (b.DefId.IsNone) return outp;
            var cat = _match.Engine.Catalog;
            var def = cat.Structure(b.DefId, b.Color);
            if (def == null) return outp;
            for (int i = 0; i < def.UpgradeTargets.Length; i++)
            {
                var t = cat.Structure(new StructId(def.UpgradeTargets[i]), b.Color);
                if (t != null) outp.Add(t);
            }
            return outp;
        }

        static string ZoneTag(WorkerZone z)
        {
            return z == WorkerZone.Back ? "B" : z == WorkerZone.Front ? "F" : "C";
        }

        static string ZoneName(WorkerZone z)
        {
            return z == WorkerZone.Back ? "back" : z == WorkerZone.Front ? "front" : "centre";
        }

        // ---- board overlays -------------------------------------------------------------------

        /// <summary>
        /// A parked combat choice YOU must answer: assign blockers to an incoming attack,
        /// pick the absorber for your gang-blocked attacker, or pick who your creature strikes
        /// back at. An opaque centered panel that publishes its rect so the board cannot be
        /// tapped through it; there is no cancel - the duel waits on the answer.
        /// </summary>
        void DrawChoicePanel(GameState s, float w, float h)
        {
            SyncChoice();
            var pending = s.Pending;
            if (pending == null || pending.Responder != Seat.Local) return;

            const float rowH = 26f;
            const float pw = 300f;

            // Every one of these panels answers the same question - WHICH CARD IS ON WHICH - so
            // each says the pairing out loud in a second line rather than making the player hold
            // the board in their head. The exchange alternates: the defender picks blockers per
            // declaration, then the attacker assigns each gang-blocked blow, then the defender
            // aims each retaliation, until nothing is unassigned.
            string title = "", pairing = "";
            UnitRef[] options = null;
            var blocker = pending as BlockerRequest;
            var absorber = pending as AbsorberRequest;
            var retaliation = pending as RetaliationRequest;
            var window = pending as ResponseWindowRequest;
            if (window != null)
            {
                DrawResponseWindow(s, window, w, h);
                return;
            }
            if (blocker != null)
            {
                // "attack 2 of 3": the whole assault was declared before you were asked anything,
                // so how far through it this one is belongs on the panel.
                title = blocker.DeclarationCount > 1
                    ? "DEFEND — attack " + (blocker.DeclarationIndex + 1) + " of "
                      + blocker.DeclarationCount
                    : "DEFEND";
                pairing = UnitLabel(s, blocker.AttackerId) + " → " + DeclaredTarget(s, blocker);
                options = blocker.Eligible;
            }
            else if (absorber != null)
            {
                title = "ASSIGN THE BLOW";
                pairing = UnitLabel(s, absorber.AttackerId) + " is gang-blocked by "
                        + absorber.Blockers.Length + " — which one takes it?";
                options = absorber.Blockers;
            }
            else if (retaliation != null)
            {
                title = "STRIKE BACK";
                pairing = UnitLabel(s, retaliation.DefenderId) + " was hit by "
                        + retaliation.Attackers.Length + " — which one does it hit back?";
                options = retaliation.Attackers;
            }
            else return;

            int extraRows = blocker != null ? 2 : 1;           // commit/pass rows
            float contentH = (options.Length + extraRows) * rowH + 48;
            float regionTop = TopH + 6 + ShellBarH;
            float regionBottom = h - BottomH - 6;
            float ph = Mathf.Min(contentH, regionBottom - regionTop);
            float py = regionTop + (regionBottom - regionTop - ph) / 2f;
            var panel = new Rect(w / 2f - pw / 2f, py, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            GUI.Label(new Rect(panel.x + 8, panel.y + 4, pw - 16, 20), title, _small);
            GUI.Label(new Rect(panel.x + 8, panel.y + 22, pw - 16, 20), pairing, _small);
            float y = panel.y + 46;

            for (int i = 0; i < options.Length; i++)
            {
                string label = UnitLabel(s, options[i].UnitId);
                if (blocker != null)
                {
                    bool on = _chosenBlockers.Contains(i);
                    if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3),
                            (on ? "✔ " : "   ") + label, _button))
                    {
                        if (on) _chosenBlockers.Remove(i);
                        else _chosenBlockers.Add(i);
                    }
                }
                else
                {
                    if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
                        Try(new RespondCommand(Seat.Local, new IndexChosen(i)));
                }
                y += rowH;
            }

            if (blocker != null)
            {
                if (Btn(new Rect(panel.x + 8, y, (pw - 20) / 2f, rowH - 3),
                        "BLOCK WITH " + _chosenBlockers.Count, _button))
                {
                    var picks = new List<UnitRef>();
                    for (int i = 0; i < options.Length; i++)
                        if (_chosenBlockers.Contains(i)) picks.Add(options[i]);
                    Try(new RespondCommand(Seat.Local, new BlockersChosen(picks.ToArray())));
                }
                if (Btn(new Rect(panel.x + 12 + (pw - 20) / 2f, y, (pw - 20) / 2f, rowH - 3),
                        "LET IT THROUGH", _button))
                    Try(new RespondCommand(Seat.Local, new BlockersChosen(new UnitRef[0])));
            }
        }

        /// <summary>
        /// The response window: your set traps, offered against something the opponent just did.
        /// One button per armed trap plus HOLD.
        ///
        /// The anti-tell pause the JS ran here (a constant-length "opponent may respond…" pill,
        /// 30_resp.js) is still owed - it belongs on the ACTING side, and it is a view concern
        /// by design, so it lands with the presentation pass rather than in the rules.
        /// </summary>
        void DrawResponseWindow(GameState s, ResponseWindowRequest req, float w, float h)
        {
            const float rowH = 26f;
            const float pw = 300f;

            string what = req.Trigger == TrapTrigger.Summon
                ? "The opponent summons " + UnitLabel(s, req.Subject.UnitId)
                : "Your line is struck" + (req.Subject.UnitId != 0
                    ? " — " + UnitLabel(s, req.Subject.UnitId) + " defends" : "");

            float contentH = req.ArmedTraps.Length * (rowH + 20) + rowH + 46;
            float regionTop = TopH + 6 + ShellBarH;
            float regionBottom = h - BottomH - 6;
            float ph = Mathf.Min(contentH, regionBottom - regionTop);
            float py = regionTop + (regionBottom - regionTop - ph) / 2f;
            var panel = new Rect(w / 2f - pw / 2f, py, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            GUI.Label(new Rect(panel.x + 8, panel.y + 4, pw - 16, 20), "RESPOND?", _small);
            GUI.Label(new Rect(panel.x + 8, panel.y + 22, pw - 16, 20), what, _small);

            float y = panel.y + 46;
            for (int i = 0; i < req.ArmedTraps.Length; i++)
            {
                var trapUnit = s.FindById(req.ArmedTraps[i].UnitId, out _, out _) as TrapUnit;
                string label = "⚠ " + (trapUnit != null ? trapUnit.Card.Value : "trap");
                if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
                    Try(new RespondCommand(Seat.Local, new TrapChosen(req.ArmedTraps[i])));
                y += rowH;

                // what it actually does - nobody should have to remember their own set cards
                SpellCard card;
                if (trapUnit != null && _match.Engine.Catalog.TrySpell(trapUnit.Card, out card))
                {
                    GUI.Label(new Rect(panel.x + 14, y - 4, pw - 22, 24), SpellEngine.TextOf(card),
                        _small);
                    y += 20;
                }
            }

            if (Btn(new Rect(panel.x + 8, y, pw - 16, rowH - 3), "HOLD", _button))
                Try(new RespondCommand(Seat.Local, TrapChosen.Passed));
        }

        /// <summary>What a parked blocker request's declaration is actually aimed at - the other
        /// half of "which card is attacking which", which the request itself does not carry.</summary>
        string DeclaredTarget(GameState s, BlockerRequest req)
        {
            if (req.DeclarationIndex < 0 || req.DeclarationIndex >= s.Combat.Declarations.Count)
                return "your line";

            var d = s.Combat.Declarations[req.DeclarationIndex];
            if (d.Kind == DeclarationKind.Wall)
                return d.TargetSide == Seat.Local ? "YOUR WALL" : "their wall";
            if (d.Kind == DeclarationKind.WorkerStack)
                return "your " + ZoneName(d.TargetZone) + " workers";
            return UnitLabel(s, d.TargetUnitId);
        }

        string UnitLabel(GameState s, int unitId)
        {
            CellRef at;
            bool onBoard;
            var o = s.FindById(unitId, out at, out onBoard);
            var c = o as CreatureUnit;
            if (c != null)
                return c.Name + " " + Stat.Line(c.EffectiveAttack, c.Hp) +
                       (c.IsWorker ? " (worker)" : "");
            var b = o as StructureUnit;
            if (b != null) return b.DefId.Value;
            if (o is ChargeUnit) return "a face-down card";
            if (o is TrapUnit) return "a set card";
            return "unit " + unitId;
        }

        // ---- helpers --------------------------------------------------------------------------

        /// <summary>
        /// The JS opened the settle menu on the first over-extended creature the moment upkeep
        /// began (`upkeepPick(off.key, off.i)`), so the shortfall could not be missed. Ours puts
        /// the offender under the cursor once per turn and then leaves the player alone.
        /// </summary>
        void PromptUpkeepOffender(GameState s)
        {
            if (_input == null || s.Turn != Seat.Local || s.Phase != TurnPhase.Upkeep) return;
            if (s.TurnNumber == _upkeepPromptedTurn) return;

            CellRef cell;
            int unitId;
            if (!Upkeep.TryFindOffender(s, Seat.Local, _match.Engine.Catalog, out cell, out unitId))
                return;

            _upkeepPromptedTurn = s.TurnNumber;
            _input.SelectFromUi(cell);
            Hint("Upkeep shortfall — this creature needs a worker, a payment, or its life");
        }

        /// <summary>
        /// Arm a play, and DROP THE CARD while it is armed.
        ///
        /// BeginPlay captures the hand index, so nothing downstream needs the selection any more -
        /// and leaving it set is not free. A picked card is lifted to the bottom of the screen at
        /// nearly three times its resting peek, which is about 100 x 139 logical pixels standing
        /// over your own two deploy rows: exactly the cells a summon is aimed at. It is the one
        /// pickable element on the hand panel, so a tap there can be taken by the card instead of
        /// by the lit cell under it, and the card's own handler toggles the selection off and
        /// cancels the play. Whether that or the board wins is a frame-ordering coin toss between
        /// two input systems that cannot see each other's events - and the honest fix is not to
        /// arbitrate it but to have nothing lying over the board to arbitrate.
        ///
        /// The armed branch of the mode row already reads nothing from the selection, and dropping
        /// the card back into the peek strip puts it inside the band BoardInput refuses outright.
        /// </summary>
        void Arm(Rules.PlayMode mode)
        {
            _match.BeginPlay(_selectedHandIndex, mode);
            if (_match.LegalCells.Count == 0)
            {
                _match.CancelPending();
                Hint("No legal cell for that right now");
                return;
            }
            _selectedHandIndex = -1;
        }

        /// <summary>
        /// A button that BLOCKS the board under it.
        ///
        /// Legacy Input cannot see IMGUI consume an event - Update even runs before the frame's
        /// GUI events - so a control drawn over the field is invisible to BoardInput unless
        /// somebody publishes its rect. Somebody used to be each panel, by hand, which worked for
        /// panels and quietly failed for every loose button: tapping ⚔ WALL declared nothing and
        /// selected the card underneath instead. Drawing one registers it now, so the two cannot
        /// drift apart again.
        ///
        /// Controls inside a scroll view do NOT go through this - their rects are in the view's
        /// own space, and the panel that owns the view publishes its own rect anyway.
        /// </summary>
        bool Btn(Rect r, string text, GUIStyle style)
        {
            HudLayout.Control(r);
            return GUI.Button(r, text, style);
        }

        void Try(ICommand cmd)
        {
            var why = _match.TryHuman(cmd);
            if (why != Rejection.None) Hint(MatchController.Hint(why));
        }

        /// <summary>An attack declared from a BUTTON rather than from a board tap. It goes through
        /// the controller's one declaration funnel, so the wall and the worker stacks open an
        /// attack group exactly as tapping a unit does.</summary>
        /// <summary>
        /// Every attack declared from a BUTTON goes through here - ⚔ WALL and the three worker
        /// stacks. Asking the input layer for a group first is what makes those group attacks
        /// too, without either button knowing that groups exist; and the wall is where the
        /// playtest says matches are actually won.
        /// </summary>
        void Declare(CellRef from, AttackTarget target, string label)
        {
            if (_input != null && _input.DeclareGroupOrSingle(target, label)) return;

            var why = _match.Declare(from, target, label);
            if (why != Rejection.None) Hint(MatchController.Hint(why));
        }

        /// <summary>Close the attack group and resolve it - the attacker's confirm.</summary>
        void Confirm()
        {
            var why = _match.ConfirmAssault();
            if (why != Rejection.None) Hint(MatchController.Hint(why));
        }

        void Hint(string text)
        {
            _hint = text;
            _hintUntil = Time.unscaledTime + 2.5f;
        }
    }
}
