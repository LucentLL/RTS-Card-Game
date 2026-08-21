using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The placeholder HUD, laid out in BANDS so nothing ever overlaps the board: an opaque top
    /// bar (both players + turn/phase), an opaque bottom band (hand strip, contextual buttons,
    /// action row), and the 3D board rendering only in between - BoardInput shrinks the camera
    /// viewport to the gap HudLayout publishes. Menus are solid panels clamped on-screen and
    /// scroll when tall. Everything is read from GameState each frame; every tap is a command.
    ///
    /// Scaling is by the SHORT side of the screen (~480 logical units), so portrait and
    /// landscape both get a sane layout instead of landscape inheriting portrait's width math.
    ///
    /// IMGUI on purpose - no font asset needed while the glyph plan is open (GAPS P0).
    /// </summary>
    [RequireComponent(typeof(MatchController))]
    public class MatchHud : MonoBehaviour
    {
        // band heights, logical units
        const float TopH = 42f;
        const float ActionH = 46f;
        const float HandH = 82f;
        const float ModeH = 28f;
        const float BottomH = ActionH + HandH + ModeH;

        private MatchController _match;
        private BoardInput _input;
        private string _hint = "";
        private float _hintUntil;
        private int _selectedHandIndex = -1;
        private bool _buildMenuOpen;
        private Vector2 _buildScroll;
        private Vector2 _handScroll;
        private float _scale = 1f;
        private int _lastLogCount;
        private float _logShownUntil;
        private readonly HashSet<int> _chosenBlockers = new HashSet<int>();
        private PendingRequest _seenPending;

        private GUIStyle _label, _small, _tiny, _button, _bigButton, _cardName, _center;
        private GUIStyle _ovYou, _ovFoe, _ovNeutral;

        private static readonly Color PanelColor = new Color(0.055f, 0.06f, 0.085f, 1f);
        private static readonly Color PanelSoft = new Color(0.055f, 0.06f, 0.085f, 0.72f);
        private static readonly Color CardBack = new Color(0.10f, 0.11f, 0.15f, 1f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
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

            // overlay text must CLIP at its cell-pitch rect - spilled ink is how neighbouring
            // units' labels turned into unreadable stacked text
            _ovYou = new GUIStyle(_tiny) { clipping = TextClipping.Clip, wordWrap = false };
            _ovYou.normal.textColor = new Color(1f, 0.9f, 0.55f);
            _ovFoe = new GUIStyle(_ovYou);
            _ovFoe.normal.textColor = new Color(0.65f, 0.8f, 1f);
            _ovNeutral = new GUIStyle(_ovYou);
            _ovNeutral.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
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
            if (_match == null || _match.Engine == null) return;
            EnsureStyles();
            var s = _match.Engine.State;

            // scale by the SHORT side - landscape must not inherit portrait's width math
            float scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
            _scale = scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            // publish the reserved bands so the camera viewport stays out of them, and reset
            // the in-viewport blocker rects - the draws below re-publish the ones that exist
            HudLayout.TopPx = TopH * scale;
            HudLayout.BottomPx = BottomH * scale;
            HudLayout.MenuPx = new Rect();
            HudLayout.LogPx = new Rect();

            DrawUnitOverlays(s, scale, w, h);
            DrawTopBar(s, w);
            DrawLog(w);

            // the bottom band is ALWAYS painted opaque - the camera does not render behind it
            Panel(new Rect(0, h - BottomH, w, BottomH), PanelColor);

            if (s.IsOver)
            {
                var over = new GUIStyle(_label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, h / 2f - 20, w, 40), "MATCH OVER — " + s.Outcome, over);
                return;
            }

            DrawHand(s, w, h);
            DrawModeRow(s, w, h);
            DrawActionRow(s, w, h);
            if (_buildMenuOpen) DrawBuildMenu(s, w, h);
            DrawChoicePanel(s, w, h);

            if (Time.unscaledTime < _hintUntil && _hint.Length > 0 && !_buildMenuOpen)
                GUI.Label(new Rect(0, h - BottomH - 22, w, 20), _hint, _center);
        }

        // ---- top band -------------------------------------------------------------------------

        void DrawTopBar(GameState s, float w)
        {
            Panel(new Rect(0, 0, w, TopH), PanelColor);
            var foe = s.P(Side.Foe);
            var you = s.P(Side.You);

            GUI.Label(new Rect(10, 3, w - 210, 18),
                "FOE  ♥" + foe.Life + "  ◆" + foe.Mana + "  hand " + foe.Hand.Count +
                "  deck " + foe.Deck.Count + "  ⚒ " + Workers(s, Side.Foe), _label);
            GUI.Label(new Rect(10, 21, w - 210, 18),
                "YOU  ♥" + you.Life + "  ◆" + you.Mana + "  hand " + you.Hand.Count +
                "  deck " + you.Deck.Count + "  ⚒ " + Workers(s, Side.You), _label);

            var right = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight };
            right.normal.textColor = s.Turn == Side.You ? Gold : new Color(0.65f, 0.8f, 1f);
            GUI.Label(new Rect(w - 190, 3, 180, 18), "TURN " + s.TurnNumber, right);
            GUI.Label(new Rect(w - 190, 21, 180, 18),
                (s.Turn == Side.You ? "YOUR TURN · " : "FOE TURN · ") +
                s.Phase.ToString().ToUpperInvariant(), right);
        }

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

            var panel = new Rect(0, TopH, w * 0.55f, lines * 14f + 4f);
            Panel(panel, PanelSoft);
            HudLayout.LogPx = new Rect(panel.x * _scale, panel.y * _scale,
                                       panel.width * _scale, panel.height * _scale);
            float y = TopH + 2;
            for (int i = log.Count - lines; i < log.Count; i++)
            {
                GUI.Label(new Rect(8, y, w * 0.55f - 12, 14), log[i], _small);
                y += 14f;
            }
        }

        // ---- bottom band ----------------------------------------------------------------------

        void DrawHand(GameState s, float w, float h)
        {
            var hand = s.P(Side.You).Hand;
            float y0 = h - ActionH - HandH;
            if (hand.Count == 0) return;

            const float gap = 4f;
            float cw = 56f;
            float maxW = w - 12;
            if (hand.Count * (cw + gap) - gap > maxW)                 // shrink to fit, floor 38
                cw = Mathf.Max(38f, (maxW + gap) / hand.Count - gap);
            float chh = HandH - 6;
            float total = hand.Count * (cw + gap) - gap;

            // a hand too wide even at the floor scrolls horizontally - there is no hand cap
            bool scrolling = total > maxW;
            float x0;
            if (scrolling)
            {
                _handScroll = GUI.BeginScrollView(new Rect(6, y0, maxW, HandH), _handScroll,
                    new Rect(0, 0, total, HandH - 16), false, false);
                x0 = 0;
                y0 = -2;
            }
            else x0 = (w - total) / 2f;

            for (int i = 0; i < hand.Count; i++)
            {
                float x = x0 + i * (cw + gap);
                var rect = new Rect(x, y0 + 2, cw, chh);
                bool selected = i == _selectedHandIndex;

                Panel(rect, selected ? new Color(0.55f, 0.45f, 0.12f, 1f) : CardBack);

                var def = _match.DefOf(hand[i].Id.Value);
                if (def != null && def.CardArt != null)
                    GUI.DrawTexture(new Rect(x + 2, y0 + 4, cw - 4, chh - 26),
                        def.CardArt.texture, ScaleMode.ScaleAndCrop);

                CreatureCard c;
                SpellCard sp;
                string statLine = "";
                if (_match.Engine.Catalog.TryCreature(hand[i].Id, out c))
                    statLine = "◆" + c.Cost + "  " + c.Attack / 500 + "/" + c.Health / 500;
                else if (_match.Engine.Catalog.TrySpell(hand[i].Id, out sp))
                    statLine = "◆" + sp.Cost + (sp.IsTrap ? " trap" : " spell");

                GUI.Label(new Rect(x + 1, y0 + chh - 22, cw - 2, 12), hand[i].Id.Value, _cardName);
                GUI.Label(new Rect(x + 1, y0 + chh - 10, cw - 2, 11), statLine, _tiny);

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    _selectedHandIndex = selected ? -1 : i;
                    _buildMenuOpen = false;
                    _match.CancelPending();
                }
            }

            if (scrolling) GUI.EndScrollView();
        }

        /// <summary>The contextual strip above the hand: play modes, charge menu, upkeep settle.</summary>
        void DrawModeRow(GameState s, float w, float h)
        {
            float by = h - ActionH - HandH - ModeH + 2;
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
                    else
                        GUI.Label(new Rect(0, by, w, 24), "spells cast at M10 — hold on to it", _center);
                }
                return;
            }

            // a selected board cell: charge menu or upkeep settle
            if (_input != null && _input.Selected.HasValue && myTurn)
            {
                var cell = _input.Selected.Value;

                var ch = s.At(cell) as ChargeUnit;
                if (ch != null && ch.Owner == Side.You && s.Phase == TurnPhase.Action)
                {
                    int remaining = Mathf.Max(0, ch.Card.Cost - ch.Invested);
                    // portrait leaves ~100 px here - the invested/cost fraction is the part
                    // that matters; the name fits only when there is room
                    string caption = w >= 620
                        ? ch.Card.Name + " ◆" + ch.Invested + "/" + ch.Card.Cost
                        : "◆" + ch.Invested + "/" + ch.Card.Cost;
                    GUI.Label(new Rect(6, by, w / 2f - 135, 24), caption, _small);
                    if (remaining > 0 &&
                        GUI.Button(new Rect(w / 2f - 125, by, 120, 24), "FILL ◆" + remaining, _button))
                        Try(new PourIntoChargeCommand(Side.You, cell, ch.Id, remaining));
                    GUI.enabled = ch.Invested >= ch.Card.Cost;
                    if (GUI.Button(new Rect(w / 2f + 5, by, 120, 24), "FLIP", _button))
                        Try(new FlipChargeCommand(Side.You, cell, ch.Id));
                    GUI.enabled = true;
                    return;
                }

                // an aimed attacker: enemy targets are lit; the wall is a button
                var atk = s.At(cell) as CreatureUnit;
                if (atk != null && atk.Owner == Side.You && s.Phase == TurnPhase.Action
                    && _match.Engine.CanApply(new DeclareAttackCommand(Side.You, cell, atk.Id,
                        new WallTarget(Side.Foe))) == Rejection.None)
                {
                    GUI.Label(new Rect(6, by, w / 2f - 135, 24), "tap a target, or:", _small);
                    if (GUI.Button(new Rect(w / 2f - 125, by, 250, 24), "⚔ STRIKE THE WALL", _button))
                        Try(new DeclareAttackCommand(Side.You, cell, atk.Id, new WallTarget(Side.Foe)));
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
                    "shortfall — tap the over-extended creature: move it, PAY, or SACRIFICE", _center);
        }

        void DrawActionRow(GameState s, float w, float h)
        {
            float by = h - ActionH + 3;

            if (s.Turn != Side.You || s.Phase == TurnPhase.End) return;

            bool resolving = s.Phase == TurnPhase.Action && s.Combat.HasDeclarations;
            string caption = s.Phase == TurnPhase.Upkeep ? "HARVEST"
                : s.Phase == TurnPhase.Draw ? "DRAW"
                : resolving ? "⚔ RESOLVE (" + s.Combat.Declarations.Count + ")"
                : "END TURN";

            GUI.enabled = s.Pending == null;
            if (GUI.Button(new Rect(w / 2f - 75, by, 150, 40), caption, _bigButton))
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

            if (s.Phase == TurnPhase.Action)
            {
                if (GUI.Button(new Rect(w / 2f + 85, by, 90, 40), _buildMenuOpen ? "CLOSE" : "BUILD", _button))
                {
                    _buildMenuOpen = !_buildMenuOpen;
                    _selectedHandIndex = -1;
                    _match.CancelPending();
                }
            }
            else _buildMenuOpen = false;
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

        string UnitLabel(GameState s, int unitId)
        {
            CellRef at;
            bool onBoard;
            var o = s.FindById(unitId, out at, out onBoard);
            var c = o as CreatureUnit;
            if (c != null)
                return c.Name + " " + c.EffectiveAttack / 500 + "/" + (c.Hp + 499) / 500 +
                       (c.IsWorker ? " (worker)" : "");
            var b = o as StructureUnit;
            if (b != null) return b.DefId.Value;
            return "unit " + unitId;
        }

        void DrawUnitOverlays(GameState s, float scale, float w, float h)
        {
            var cam = _input != null ? _input.Cam : Camera.main;
            if (cam == null || _match.Board == null) return;

            float pitchWorld = _match.Board.CellSize + _match.Board.CellGap;

            foreach (var kv in s.Objects())
            {
                var world = _match.Board.WorldOf(kv.Key);
                var pt = cam.WorldToScreenPoint(world + new Vector3(0f, 1.15f, 0f));
                if (pt.z <= 0f) continue;
                float x = pt.x / scale;
                float y = h - pt.y / scale;
                if (y < TopH + 2 || y > h - BottomH - 14) continue;   // stay out of the bands

                // the label budget is the unit's ACTUAL on-screen cell pitch - a fixed width
                // is wider than a cell at every real resolution and neighbours collide
                var pt2 = cam.WorldToScreenPoint(world + new Vector3(pitchWorld, 1.15f, 0f));
                float pitchPx = Mathf.Max(24f, Mathf.Abs(pt2.x - pt.x) / scale);
                bool roomy = pitchPx >= 44f;

                var o = kv.Value;
                string text;
                GUIStyle st;

                var cr = o as CreatureUnit;
                var b = o as StructureUnit;
                if (cr != null)
                {
                    string stats = cr.EffectiveAttack / 500 + "/" + (cr.Hp + 499) / 500 +
                                   (cr.Bank > 0 ? " ◆" + cr.Bank : "");
                    text = roomy ? cr.Name + "\n" + stats : stats;   // tight cells keep the numbers
                    st = cr.Owner == Side.You ? _ovYou : _ovFoe;
                }
                else if (b != null)
                {
                    string stats = "♥" + (b.Hp + 499) / 500 + (b.Bank > 0 ? " ◆" + b.Bank : "");
                    text = roomy ? b.DefId.Value + "\n" + stats : stats;
                    st = b.Owner == Side.You ? _ovYou : _ovFoe;
                }
                else if (o is ChargeUnit)
                {
                    var chu = (ChargeUnit)o;
                    text = o.Owner == Side.You ? "SET ◆" + chu.Invested : "SET ?";
                    st = _ovNeutral;
                }
                else
                {
                    text = o.Owner == Side.You ? "TRAP" : "SET ?";
                    st = _ovNeutral;
                }

                GUI.Label(new Rect(x - pitchPx / 2f, y, pitchPx, roomy ? 26 : 13), text, st);
            }
        }

        // ---- helpers --------------------------------------------------------------------------

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

        static string Workers(GameState s, Side side)
        {
            var p = s.P(side);
            return p.Workers[0].ReadyCount + "/" + p.Workers[0].Count + "·" +
                   p.Workers[1].ReadyCount + "/" + p.Workers[1].Count + "·" +
                   p.Workers[2].ReadyCount + "/" + p.Workers[2].Count;
        }

        void Hint(string text)
        {
            _hint = text;
            _hintUntil = Time.unscaledTime + 2.5f;
        }
    }
}
