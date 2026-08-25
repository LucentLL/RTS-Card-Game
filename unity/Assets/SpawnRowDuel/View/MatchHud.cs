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
        private bool _buildMenuOpen;
        private Vector2 _buildScroll;
        private Vector2 _handScroll;
        private float _scale = 1f;
        private int _lastLogCount;
        private float _logShownUntil;
        private readonly HashSet<int> _chosenBlockers = new HashSet<int>();
        private PendingRequest _seenPending;
        private bool _upgradeMenuOpen;
        private int _chargeAmount;
        private int _chargeCellId = -1;
        private int _upkeepPromptedTurn = -1;
        private CommanderId _pickYou = new CommanderId("fire");
        private CommanderId _pickFoe = new CommanderId("water");
        private Vector2 _selectScrollYou, _selectScrollFoe;

        private GUIStyle _label, _small, _tiny, _button, _bigButton, _cardName, _center;

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

            // The unit overlays are NOT here any more. They floated a cell and a half above each
            // slot, which put the foe's back row behind the castle wall and made the layer answer
            // by dropping those labels - the rows you attack into were the rows with no numbers.
            // UnitVitals hangs them off each tile's near edge instead, in UI Toolkit, where ♥ and
            // ⚔ have a font that can draw them.
            DrawLog(w);

            // NOTHING is painted across the bottom here any more. IMGUI draws AFTER every UI
            // Toolkit panel, so a band painted here lands ON TOP of the hand rather than behind
            // it - that was the dark bar through the cards. The hand owns its own backdrop, and
            // the turn controls moved to the right-edge rail where they overlap nothing.

            if (s.IsOver)
            {
                var over = new GUIStyle(_label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, h / 2f - 20, w, 40), "MATCH OVER — " + s.Outcome, over);
                return;
            }

            PromptUpkeepOffender(s);

            // the hand is UI Toolkit now (HandBar) - real card faces, same band, same selection
            DrawModeRow(s, w, h);
            DrawSideRail(s, w, h);
            if (_buildMenuOpen) DrawBuildMenu(s, w, h);
            else if (_upgradeMenuOpen) DrawUpgradeMenu(s, w, h);
            else DrawChargePanel(s, w, h);
            DrawChoicePanel(s, w, h);

            if (Time.unscaledTime < _hintUntil && _hint.Length > 0 && !_buildMenuOpen)
                GUI.Label(new Rect(0, h - BottomH - 22, w, 20), _hint, _center);
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

            if (GUI.Button(new Rect(w / 2f - 150, h - 62, 145, 34), "🎲 RANDOM FOE", _button))
                _pickFoe = all[Mathf.Abs((int)(Time.realtimeSinceStartup * 1000f)) % all.Count].Id;

            if (GUI.Button(new Rect(w / 2f + 5, h - 62, 145, 34), "▶ START DUEL", _bigButton))
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
        void DrawArenaRow(float w, float h)
        {
            GUI.Label(new Rect(0, h - 132, w, 16), "ARENA", _center);

            var all = World.Biomes.All;
            const float bw = 84f, gap = 6f;
            float x = w / 2f - (all.Length * bw + (all.Length - 1) * gap) / 2f;

            for (int i = 0; i < all.Length; i++)
            {
                bool on = World.TerrainField.Requested == all[i];
                var old = GUI.color;
                if (on) GUI.color = Gold;
                if (GUI.Button(new Rect(x, h - 114, bw, 24), World.Biomes.NameOf(all[i]), _button))
                    World.TerrainField.Requested = all[i];
                GUI.color = old;
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

        void DrawLog(float w)
        {
            var log = _match.Log;
            int lines = Mathf.Min(3, log.Count);
            if (lines <= 0) return;

            // auto-hide: the panel sits over the foe's board corner and (correctly) blocks taps
            // there, so it only lingers a few seconds after something actually happened
            if (log.Count != _lastLogCount)
            {
                _lastLogCount = log.Count;
                _logShownUntil = Time.unscaledTime + 5f;
            }
            if (Time.unscaledTime > _logShownUntil) return;

            // RIGHT of the top bar: the left corner under it belongs to the inspect card now, the
            // way Master Duel reserves that corner for whatever you are looking at.
            var panel = new Rect(w * 0.45f, TopH, w * 0.55f, lines * 14f + 4f);
            Panel(panel, PanelSoft);
            HudLayout.LogPx = new Rect(panel.x * _scale, panel.y * _scale,
                                       panel.width * _scale, panel.height * _scale);
            float y = TopH + 2;
            for (int i = log.Count - lines; i < log.Count; i++)
            {
                GUI.Label(new Rect(panel.x + 8, y, panel.width - 12, 14), log[i], _small);
                y += 14f;
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
            var hand = s.P(Side.You).Hand;
            bool myTurn = s.Turn == Side.You;

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
                    if (GUI.Button(new Rect(w / 2f - 125, by, 120, 24), "SUMMON ◆" + c.Cost, _button))
                        Arm(Rules.PlayMode.Summon);
                    if (GUI.Button(new Rect(w / 2f + 5, by, 120, 24), "SET ◆1", _button))
                        Arm(Rules.PlayMode.Set);
                }
                else if (_match.Engine.Catalog.TrySpell(id, out sp))
                {
                    if (sp.IsTrap)
                    {
                        if (GUI.Button(new Rect(w / 2f - 60, by, 120, 24), "SET TRAP ◆1", _button))
                            Arm(Rules.PlayMode.SetTrap);
                    }
                    else if (!SpellTargeting.HasAnyTarget(s, sp, Side.You))
                        GUI.Label(new Rect(0, by, w, 24), "no legal target for " + sp.Name, _center);
                    else if (GUI.Button(new Rect(w / 2f - 60, by, 120, 24), "CAST ◆" + sp.Cost, _button))
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

                var owned = s.At(cell);
                bool canSend = owned != null && owned.Owner == Side.You && owned.Bank > 0
                    && s.Phase == TurnPhase.Action;

                var ch = s.At(cell) as ChargeUnit;
                if (ch != null && ch.Owner == Side.You && s.Phase == TurnPhase.Action)
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
                if (atk != null && atk.Owner == Side.You && s.Phase == TurnPhase.Action
                    && !atk.IsWorker && !atk.Sick && !atk.Tapped)
                {
                    var wall = new DeclareAttackCommand(Side.You, cell, atk.Id, new WallTarget(Side.Foe));
                    bool wallOk = _match.Engine.CanApply(wall) == Rejection.None;

                    // Worker stacks are attackable by the rules and were unreachable from the
                    // board, because a pool is not a cell - it needs its own button.
                    var zones = new[] { WorkerZone.Back, WorkerZone.Front, WorkerZone.Center };
                    var legalZones = new List<WorkerZone>();
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var st = new DeclareAttackCommand(Side.You, cell, atk.Id,
                            new WorkerStackTarget(Side.Foe, zones[i]));
                        if (_match.Engine.CanApply(st) == Rejection.None) legalZones.Add(zones[i]);
                    }

                    if (wallOk || legalZones.Count > 0)
                    {
                        float x = w / 2f - 125;
                        if (wallOk)
                        {
                            float ww = legalZones.Count > 0 ? 130 : 250;
                            if (GUI.Button(new Rect(x, by, ww, 24), "⚔ WALL", _button)) Try(wall);
                            x += ww + 5;
                        }
                        float zw = legalZones.Count > 0
                            ? Mathf.Min(60f, (w / 2f + 125 - x) / legalZones.Count - 4) : 0;
                        for (int i = 0; i < legalZones.Count; i++)
                        {
                            var z = legalZones[i];
                            int n = s.P(Side.Foe).Workers[(int)z].Count;
                            if (GUI.Button(new Rect(x, by, zw, 24), "⚒" + ZoneTag(z) + n, _button))
                                Try(new DeclareAttackCommand(Side.You, cell, atk.Id,
                                    new WorkerStackTarget(Side.Foe, z)));
                            x += zw + 4;
                        }
                        return;
                    }
                }

                // your structure: the in-place upgrade chain, and moving its banked ◆ off it.
                // Both fit, because a structure about to be upgraded is exactly when you want to
                // decide where its stored mana goes.
                var bld = s.At(cell) as StructureUnit;
                bool canUpgrade = bld != null && bld.Owner == Side.You
                    && s.Phase == TurnPhase.Action && UpgradeTargetsFor(s, cell, bld).Count > 0;

                if (canUpgrade || canSend)
                {
                    float bw = (canUpgrade && canSend) ? 122f : 250f;
                    float x = w / 2f - 125;
                    if (canUpgrade)
                    {
                        if (GUI.Button(new Rect(x, by, bw, 24),
                                _upgradeMenuOpen ? "CLOSE" : "⬆ UPGRADE", _button))
                            _upgradeMenuOpen = !_upgradeMenuOpen;
                        x += bw + 6;
                    }
                    if (canSend && GUI.Button(new Rect(x, by, bw, 24),
                            "◆ SEND " + owned.Bank, _button))
                    {
                        _upgradeMenuOpen = false;
                        _match.BeginSendMana(cell);
                    }
                    return;
                }

                // Upkeep settle: Move is the lit cells; Pay / Sacrifice live here
                var cr = s.At(cell) as CreatureUnit;
                if (cr != null && cr.Owner == Side.You && !cr.IsWorker && s.Phase == TurnPhase.Upkeep)
                {
                    var cat = _match.Engine.Catalog;
                    var zone = Rules.Board.ZoneForRow(Side.You, cell.Row);
                    int deficit = Upkeep.ZoneDeficit(s, Side.You, zone, cat);
                    int pay = Mathf.Min(cr.Upkeep, deficit);

                    GUI.enabled = pay > 0 && !cr.PaidUpkeep && s.P(Side.You).Mana >= pay;
                    if (GUI.Button(new Rect(w / 2f - 125, by, 120, 24), "PAY ◆" + pay, _button))
                        Try(new UpkeepPayCommand(Side.You, cell, cr.Id));
                    GUI.enabled = true;
                    if (GUI.Button(new Rect(w / 2f + 5, by, 120, 24), "SACRIFICE", _button))
                        Try(new UpkeepSacrificeCommand(Side.You, cell, cr.Id));
                    return;
                }
            }

            // idle upkeep guidance when the harvest is locked
            if (myTurn && s.Phase == TurnPhase.Upkeep
                && !Upkeep.HarvestUnlocked(s, Side.You, _match.Engine.Catalog))
                GUI.Label(new Rect(0, by, w, 24),
                    "shortfall ⚒" + Upkeep.TotalDeficit(s, Side.You, _match.Engine.Catalog)
                    + " — move the flagged creature to a lit cell, PAY its keep, or SACRIFICE it",
                    _center);
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

            bool mine = s.Turn == Side.You;
            bool resolving = s.Phase == TurnPhase.Action && s.Combat.HasDeclarations;
            bool acting = mine && s.Phase != TurnPhase.End;

            // measure first, so the rail can be centred vertically and its blocker rect published
            float trackH = 5 * 15f + 8f;
            float btnH = acting ? 34f : 0f;
            float buildH = (acting && s.Phase == TurnPhase.Action) ? 26f : 0f;
            float totalH = trackH + (btnH > 0 ? btnH + 5f : 0f) + (buildH > 0 ? buildH + 4f : 0f);

            float y = Mathf.Max(TopH + 8f, h * 0.5f - totalH * 0.5f);
            var rail = new Rect(x, y, railW, totalH);
            Panel(rail, PanelSoft);
            HudLayout.RailPx = new Rect(rail.x * _scale, rail.y * _scale,
                                        rail.width * _scale, rail.height * _scale);

            DrawPhaseTrack(s, new Rect(x + 4, y + 4, railW - 8, trackH - 8));
            y += trackH;

            if (!acting) { _buildMenuOpen = false; return; }

            string caption = s.Phase == TurnPhase.Upkeep ? "HARVEST"
                : s.Phase == TurnPhase.Draw ? "DRAW"
                : resolving ? "⚔ " + s.Combat.Declarations.Count
                : "END TURN";

            GUI.enabled = s.Pending == null;
            if (GUI.Button(new Rect(x + 5, y + 5, railW - 10, btnH), caption, _button))
            {
                _selectedHandIndex = -1;
                _buildMenuOpen = false;
                _match.CancelPending();
                Try(s.Phase == TurnPhase.Upkeep ? new HarvestCommand(Side.You)
                    : s.Phase == TurnPhase.Draw ? (ICommand)new DrawForTurnCommand(Side.You)
                    : resolving ? new ResolveCombatCommand(Side.You)
                    : new EndTurnCommand(Side.You));
            }
            GUI.enabled = true;
            y += btnH + 5f;

            if (buildH > 0f
                && GUI.Button(new Rect(x + 5, y + 4, railW - 10, buildH),
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

        /// <summary>A solid panel clamped inside the board region; scrolls when taller.</summary>
        void DrawBuildMenu(GameState s, float w, float h)
        {
            var cat = _match.Engine.Catalog;
            var list = cat.BuildList(s.P(Side.You).Commander);

            const float rowH = 26f;
            const float pw = 280f;
            float contentH = list.Count * rowH;
            float regionTop = TopH + 6;
            float regionBottom = h - BottomH - 6;
            float ph = Mathf.Min(contentH + 12, regionBottom - regionTop);
            float py = regionTop + (regionBottom - regionTop - ph) / 2f;
            var panel = new Rect(w / 2f - pw / 2f, py, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            _buildScroll = GUI.BeginScrollView(
                new Rect(panel.x + 4, panel.y + 6, pw - 8, ph - 12), _buildScroll,
                new Rect(0, 0, pw - 28, contentH), false, contentH > ph - 12);

            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                bool can = Placement.CanBuild(s, Side.You, def, cat);
                GUI.enabled = can;
                if (GUI.Button(new Rect(2, i * rowH, pw - 32, rowH - 3),
                        def.Name + "   ◆" + def.Cost + "   ⚒" +
                        (def.Support >= 0 ? "+" : "") + def.Support, _button))
                {
                    _match.BeginBuild(def);
                    _buildMenuOpen = false;
                    if (_match.LegalCells.Count == 0)
                    {
                        _match.CancelPending();
                        Hint("No legal cell for the " + def.Name);
                    }
                }
                GUI.enabled = true;
            }

            GUI.EndScrollView();
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
            if (s.Turn != Side.You || s.Phase != TurnPhase.Action) { _chargeAmount = 0; return; }
            if (_match.SendFrom.HasValue) return;

            var cell = _input.Selected.Value;
            var ch = s.At(cell) as ChargeUnit;
            if (ch == null || ch.Owner != Side.You) { _chargeAmount = 0; return; }

            if (_chargeCellId != ch.Id) { _chargeCellId = ch.Id; _chargeAmount = 0; }

            int mana = s.P(Side.You).Mana;
            int remaining = Mathf.Max(0, ch.Card.Cost - ch.Invested);
            _chargeAmount = Mathf.Clamp(_chargeAmount, 0, mana);

            const float pw = 300f, rowH = 28f;
            float ph = 152f;
            float regionTop = TopH + 6;
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
            if (GUI.Button(new Rect(panel.x + 8, y, 40, rowH - 2), "−", _button))
                _chargeAmount = Mathf.Max(0, _chargeAmount - 1);
            GUI.Label(new Rect(panel.x + 52, y + 4, 60, 20), "◆" + _chargeAmount, _center);
            if (GUI.Button(new Rect(panel.x + 116, y, 40, rowH - 2), "+", _button))
                _chargeAmount = Mathf.Min(mana, _chargeAmount + 1);

            GUI.enabled = remaining > 0 && mana > 0;
            if (GUI.Button(new Rect(panel.x + 162, y, 60, rowH - 2), "FILL", _button))
                _chargeAmount = Mathf.Min(mana, remaining);
            GUI.enabled = mana > 0;
            if (GUI.Button(new Rect(panel.x + 228, y, 64, rowH - 2), "ALL ◆" + mana, _button))
                _chargeAmount = mana;
            GUI.enabled = true;
            y += rowH + 4;

            GUI.enabled = _chargeAmount > 0 && _chargeAmount <= mana;
            if (GUI.Button(new Rect(panel.x + 8, y, (pw - 24) / 2f, rowH), "POUR ◆" + _chargeAmount, _button))
            {
                Try(new PourIntoChargeCommand(Side.You, cell, ch.Id, _chargeAmount));
                _chargeAmount = 0;
            }
            GUI.enabled = ch.Invested >= ch.Card.Cost;
            int bankOnFlip = Mathf.Max(0, ch.Invested - ch.Card.Cost);
            if (GUI.Button(new Rect(panel.x + 16 + (pw - 24) / 2f, y, (pw - 24) / 2f, rowH),
                    bankOnFlip > 0 ? "FLIP (bank ◆" + bankOnFlip + ")" : "FLIP UP", _button))
            {
                Try(new FlipChargeCommand(Side.You, cell, ch.Id));
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
            if (bld == null || bld.Owner != Side.You || s.Phase != TurnPhase.Action)
            {
                _upgradeMenuOpen = false;
                return;
            }

            var targets = UpgradeTargetsFor(s, cell, bld);
            if (targets.Count == 0) { _upgradeMenuOpen = false; return; }

            const float rowH = 28f, pw = 300f;
            float ph = targets.Count * rowH + 34;
            float regionTop = TopH + 6;
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
                var cmd = new UpgradeStructureCommand(Side.You, cell, bld.Id, def.Bid);
                var why = _match.Engine.CanApply(cmd);
                GUI.enabled = why == Rejection.None;
                string label = def.Name + "   ◆" + def.Cost + "   " + Stat.Hp(def.MaxHp)
                             + "   ⚒" + (def.Support >= 0 ? "+" : "") + def.Support;
                if (GUI.Button(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
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

        // ---- board overlays -------------------------------------------------------------------

        /// <summary>
        /// A parked combat choice YOU must answer: assign blockers to an incoming attack,
        /// pick the absorber for your gang-blocked attacker, or pick who your creature strikes
        /// back at. An opaque centered panel that publishes its rect so the board cannot be
        /// tapped through it; there is no cancel - the duel waits on the answer.
        /// </summary>
        void DrawChoicePanel(GameState s, float w, float h)
        {
            var pending = s.Pending;
            if (pending == null || pending.Responder != Side.You) return;

            if (!ReferenceEquals(pending, _seenPending))
            {
                _seenPending = pending;
                _chosenBlockers.Clear();
            }

            const float rowH = 26f;
            const float pw = 300f;

            string title = "";
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
                title = "BLOCK " + UnitLabel(s, blocker.AttackerId) + "?";
                options = blocker.Eligible;
            }
            else if (absorber != null)
            {
                title = "ASSIGN THE BLOW — " + UnitLabel(s, absorber.AttackerId) + " is gang-blocked";
                options = absorber.Blockers;
            }
            else if (retaliation != null)
            {
                title = "STRIKE BACK — " + UnitLabel(s, retaliation.DefenderId) + " retaliates at:";
                options = retaliation.Attackers;
            }
            else return;

            int extraRows = blocker != null ? 2 : 1;           // commit/pass rows
            float contentH = (options.Length + extraRows) * rowH + 30;
            float regionTop = TopH + 6;
            float regionBottom = h - BottomH - 6;
            float ph = Mathf.Min(contentH, regionBottom - regionTop);
            float py = regionTop + (regionBottom - regionTop - ph) / 2f;
            var panel = new Rect(w / 2f - pw / 2f, py, pw, ph);

            Panel(panel, PanelColor);
            HudLayout.MenuPx = new Rect(panel.x * _scale, panel.y * _scale,
                                        panel.width * _scale, panel.height * _scale);

            GUI.Label(new Rect(panel.x + 8, panel.y + 4, pw - 16, 22), title, _small);
            float y = panel.y + 28;

            for (int i = 0; i < options.Length; i++)
            {
                string label = UnitLabel(s, options[i].UnitId);
                if (blocker != null)
                {
                    bool on = _chosenBlockers.Contains(i);
                    if (GUI.Button(new Rect(panel.x + 8, y, pw - 16, rowH - 3),
                            (on ? "✔ " : "   ") + label, _button))
                    {
                        if (on) _chosenBlockers.Remove(i);
                        else _chosenBlockers.Add(i);
                    }
                }
                else
                {
                    if (GUI.Button(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
                        Try(new RespondCommand(Side.You, new IndexChosen(i)));
                }
                y += rowH;
            }

            if (blocker != null)
            {
                if (GUI.Button(new Rect(panel.x + 8, y, (pw - 20) / 2f, rowH - 3),
                        "COMMIT (" + _chosenBlockers.Count + ")", _button))
                {
                    var picks = new List<UnitRef>();
                    for (int i = 0; i < options.Length; i++)
                        if (_chosenBlockers.Contains(i)) picks.Add(options[i]);
                    Try(new RespondCommand(Side.You, new BlockersChosen(picks.ToArray())));
                }
                if (GUI.Button(new Rect(panel.x + 12 + (pw - 20) / 2f, y, (pw - 20) / 2f, rowH - 3),
                        "LET IT THROUGH", _button))
                    Try(new RespondCommand(Side.You, new BlockersChosen(new UnitRef[0])));
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
            float regionTop = TopH + 6;
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
                if (GUI.Button(new Rect(panel.x + 8, y, pw - 16, rowH - 3), label, _button))
                    Try(new RespondCommand(Side.You, new TrapChosen(req.ArmedTraps[i])));
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

            if (GUI.Button(new Rect(panel.x + 8, y, pw - 16, rowH - 3), "HOLD", _button))
                Try(new RespondCommand(Side.You, TrapChosen.Passed));
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
            if (_input == null || s.Turn != Side.You || s.Phase != TurnPhase.Upkeep) return;
            if (s.TurnNumber == _upkeepPromptedTurn) return;

            CellRef cell;
            int unitId;
            if (!Upkeep.TryFindOffender(s, Side.You, _match.Engine.Catalog, out cell, out unitId))
                return;

            _upkeepPromptedTurn = s.TurnNumber;
            _input.SelectFromUi(cell);
            Hint("Upkeep shortfall — this creature needs a worker, a payment, or its life");
        }

        void Arm(Rules.PlayMode mode)
        {
            _match.BeginPlay(_selectedHandIndex, mode);
            if (_match.LegalCells.Count == 0)
            {
                _match.CancelPending();
                Hint("No legal cell for that right now");
            }
        }

        void Try(ICommand cmd)
        {
            var why = _match.TryHuman(cmd);
            if (why != Rejection.None) Hint(MatchController.Hint(why));
        }

        void Hint(string text)
        {
            _hint = text;
            _hintUntil = Time.unscaledTime + 2.5f;
        }
    }
}
