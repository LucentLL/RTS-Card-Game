using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The placeholder HUD, engine-truthful and now interactive: a hand strip with the imported
    /// card art, a build menu off the commander's list, a charge menu for face-downs, and
    /// name/stat overlays on every board unit. Everything read from GameState each frame;
    /// every tap becomes a command. IMGUI on purpose - no font asset needed while the glyph
    /// plan is open (GAPS P0), and it scales itself for phone DPI.
    /// </summary>
    [RequireComponent(typeof(MatchController))]
    public class MatchHud : MonoBehaviour
    {
        private MatchController _match;
        private BoardInput _input;
        private string _hint = "";
        private float _hintUntil;
        private int _selectedHandIndex = -1;
        private bool _buildMenuOpen;

        private GUIStyle _label, _small, _tiny, _button, _cardName;

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
            _tiny.normal.textColor = Color.white;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            _cardName = new GUIStyle(_label) { fontSize = 9, alignment = TextAnchor.LowerCenter, wordWrap = true };
        }

        void OnGUI()
        {
            if (_match == null || _match.Engine == null) return;
            EnsureStyles();
            var s = _match.Engine.State;

            float scale = Mathf.Max(1f, Screen.width / 460f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            DrawUnitOverlays(s, scale, w, h);
            DrawTopBar(s, w);
            DrawLog(h);

            if (s.IsOver)
            {
                var over = new GUIStyle(_label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, h / 2f - 20, w, 40), "MATCH OVER — " + s.Outcome, over);
                return;
            }

            bool myAction = s.Turn == Side.You && s.Phase == TurnPhase.Action;

            if (_buildMenuOpen && myAction) DrawBuildMenu(s, w, h);
            else DrawHand(s, w, h);

            DrawActionButtons(s, w, h);
            DrawChargeMenu(s, w, h);

            if (Time.unscaledTime < _hintUntil && _hint.Length > 0)
            {
                var hint = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
                hint.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(0, h - 208, w, 20), _hint, hint);
            }
        }

        // ---- pieces ---------------------------------------------------------------------------

        void DrawTopBar(GameState s, float w)
        {
            GUI.Box(new Rect(6, 6, w - 12, 62), GUIContent.none);
            var foe = s.P(Side.Foe);
            var you = s.P(Side.You);

            GUI.Label(new Rect(12, 8, w - 24, 18),
                "FOE   ♥" + foe.Life + "   ◆" + foe.Mana + "   hand " + foe.Hand.Count +
                "   deck " + foe.Deck.Count + "   ⚒ " + Workers(s, Side.Foe), _label);
            GUI.Label(new Rect(12, 26, w - 24, 18),
                "YOU   ♥" + you.Life + "   ◆" + you.Mana + "   hand " + you.Hand.Count +
                "   deck " + you.Deck.Count + "   ⚒ " + Workers(s, Side.You), _label);
            GUI.Label(new Rect(12, 45, w - 24, 16),
                "turn " + s.TurnNumber + " · " + (s.Turn == Side.You ? "YOUR" : "FOE") + " turn · " +
                s.Phase.ToString().ToUpperInvariant() + "    [Tab] camera angle", _small);
        }

        void DrawLog(float h)
        {
            var log = _match.Log;
            int lines = Mathf.Min(4, log.Count);
            float y = 74;
            for (int i = log.Count - lines; i < log.Count; i++)
            {
                GUI.Label(new Rect(12, y, 440, 15), log[i], _small);
                y += 14f;
            }
        }

        void DrawHand(GameState s, float w, float h)
        {
            var hand = s.P(Side.You).Hand;
            if (hand.Count == 0) return;

            const float cw = 52f, chh = 74f, gap = 4f;
            float total = hand.Count * (cw + gap) - gap;
            float x0 = Mathf.Max(6, (w - total) / 2f);
            float y0 = h - 64 - chh - 8;

            for (int i = 0; i < hand.Count; i++)
            {
                float x = x0 + i * (cw + gap);
                if (x + cw > w - 6) break;                     // clip - a big hand just truncates
                var rect = new Rect(x, y0, cw, chh);
                bool selected = i == _selectedHandIndex;

                GUI.Box(rect, GUIContent.none);
                if (selected)
                {
                    var old = GUI.color;
                    GUI.color = new Color(1f, 0.85f, 0.3f);
                    GUI.Box(rect, GUIContent.none);
                    GUI.color = old;
                }

                var def = _match.DefOf(hand[i].Id.Value);
                if (def != null && def.CardArt != null)
                    GUI.DrawTexture(new Rect(x + 3, y0 + 3, cw - 6, chh - 24),
                        def.CardArt.texture, ScaleMode.ScaleAndCrop);

                CreatureCard c;
                SpellCard sp;
                string statLine = "";
                if (_match.Engine.Catalog.TryCreature(hand[i].Id, out c))
                    statLine = "◆" + c.Cost + "  " + c.Attack / 500 + "/" + c.Health / 500;
                else if (_match.Engine.Catalog.TrySpell(hand[i].Id, out sp))
                    statLine = "◆" + sp.Cost + (sp.IsTrap ? " trap" : " spell");

                GUI.Label(new Rect(x + 2, y0 + chh - 24, cw - 4, 12), hand[i].Id.Value, _cardName);
                GUI.Label(new Rect(x + 2, y0 + chh - 12, cw - 4, 12), statLine, _tiny);

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    _selectedHandIndex = selected ? -1 : i;
                    _match.CancelPending();
                }
            }

            // mode buttons for the selected card
            if (_selectedHandIndex >= 0 && _selectedHandIndex < hand.Count
                && s.Turn == Side.You && s.Phase == TurnPhase.Action)
            {
                var id = hand[_selectedHandIndex].Id;
                CreatureCard c;
                SpellCard sp;
                float by = y0 - 30;

                if (_match.Engine.Catalog.TryCreature(id, out c))
                {
                    if (GUI.Button(new Rect(w / 2f - 130, by, 120, 26), "SUMMON ◆" + c.Cost, _button))
                        Arm(Rules.PlayMode.Summon);
                    if (GUI.Button(new Rect(w / 2f + 10, by, 120, 26), "SET ◆1", _button))
                        Arm(Rules.PlayMode.Set);
                }
                else if (_match.Engine.Catalog.TrySpell(id, out sp))
                {
                    if (sp.IsTrap)
                    {
                        if (GUI.Button(new Rect(w / 2f - 60, by, 120, 26), "SET TRAP ◆1", _button))
                            Arm(Rules.PlayMode.SetTrap);
                    }
                    else
                        GUI.Label(new Rect(0, by, w, 20), "spells cast at M10 — hold on to it", CenterHint());
                }

                if (_match.Pending == MatchController.Intent.PlayCard)
                    GUI.Label(new Rect(0, by - 22, w, 20),
                        "tap a lit cell to place — tap the card again to cancel", CenterHint());
            }
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

        GUIStyle CenterHint()
        {
            var st = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = new Color(1f, 0.85f, 0.4f);
            return st;
        }

        void DrawActionButtons(GameState s, float w, float h)
        {
            if (s.Turn != Side.You || s.Phase == TurnPhase.End) return;

            string caption = s.Phase == TurnPhase.Upkeep ? "HARVEST"
                : s.Phase == TurnPhase.Draw ? "DRAW" : "END TURN";

            var main = new GUIStyle(GUI.skin.button) { fontSize = 19 };
            if (GUI.Button(new Rect(w / 2f - 70, h - 56, 140, 46), caption, main))
            {
                _selectedHandIndex = -1;
                _match.CancelPending();
                ICommand cmd = s.Phase == TurnPhase.Upkeep ? new HarvestCommand(Side.You)
                    : s.Phase == TurnPhase.Draw ? (ICommand)new DrawForTurnCommand(Side.You)
                    : new EndTurnCommand(Side.You);
                var why = _match.TryHuman(cmd);
                if (why != Rejection.None) Hint(MatchController.Hint(why));
            }

            if (s.Phase == TurnPhase.Action)
            {
                if (GUI.Button(new Rect(w - 96, h - 52, 88, 40), _buildMenuOpen ? "CLOSE" : "BUILD", _button))
                {
                    _buildMenuOpen = !_buildMenuOpen;
                    _selectedHandIndex = -1;
                    _match.CancelPending();
                }
            }
            else _buildMenuOpen = false;
        }

        void DrawBuildMenu(GameState s, float w, float h)
        {
            var cat = _match.Engine.Catalog;
            var list = cat.BuildList(s.P(Side.You).Commander);

            float rowH = 24f;
            float y0 = h - 64 - list.Count * rowH - 10;
            GUI.Box(new Rect(w / 2f - 130, y0 - 6, 260, list.Count * rowH + 12), GUIContent.none);

            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                bool can = Placement.CanBuild(s, Side.You, def, cat);
                var rect = new Rect(w / 2f - 124, y0 + i * rowH, 248, rowH - 2);

                GUI.enabled = can;
                if (GUI.Button(rect, def.Name + "   ◆" + def.Cost + "   ⚒" +
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
        }

        void DrawChargeMenu(GameState s, float w, float h)
        {
            if (_input == null || !_input.Selected.HasValue) return;
            var cell = _input.Selected.Value;
            var ch = s.At(cell) as ChargeUnit;
            if (ch == null || ch.Owner != Side.You) return;
            if (s.Turn != Side.You || s.Phase != TurnPhase.Action) return;

            int remaining = Mathf.Max(0, ch.Card.Cost - ch.Invested);
            float by = h - 172;

            GUI.Label(new Rect(0, by - 20, w, 18),
                "face-down " + ch.Card.Name + " — ◆" + ch.Invested + " of " + ch.Card.Cost + " invested",
                CenterHint());

            if (remaining > 0)
            {
                if (GUI.Button(new Rect(w / 2f - 130, by, 120, 26), "FILL ◆" + remaining, _button))
                {
                    var why = _match.TryHuman(new PourIntoChargeCommand(Side.You, cell, ch.Id, remaining));
                    if (why != Rejection.None) Hint(MatchController.Hint(why));
                }
            }

            GUI.enabled = ch.Invested >= ch.Card.Cost;
            if (GUI.Button(new Rect(w / 2f + 10, by, 120, 26), "FLIP", _button))
            {
                var why = _match.TryHuman(new FlipChargeCommand(Side.You, cell, ch.Id));
                if (why != Rejection.None) Hint(MatchController.Hint(why));
            }
            GUI.enabled = true;
        }

        void DrawUnitOverlays(GameState s, float scale, float w, float h)
        {
            var cam = _input != null ? _input.Cam : Camera.main;
            if (cam == null || _match.Board == null) return;

            foreach (var kv in s.Objects())
            {
                var world = _match.Board.WorldOf(kv.Key);
                var pt = cam.WorldToScreenPoint(world + new Vector3(0f, 1.15f, 0f));
                if (pt.z <= 0f) continue;
                float x = pt.x / scale;
                float y = h - pt.y / scale;

                var o = kv.Value;
                string text;
                Color tint;

                var cr = o as CreatureUnit;
                var b = o as StructureUnit;
                if (cr != null)
                {
                    text = cr.Name + "\n" + cr.EffectiveAttack / 500 + "/" +
                           (cr.Hp + 499) / 500 + (cr.Bank > 0 ? " ◆" + cr.Bank : "");
                    tint = cr.Owner == Side.You ? new Color(1f, 0.9f, 0.55f) : new Color(0.65f, 0.8f, 1f);
                }
                else if (b != null)
                {
                    text = b.DefId.Value + "\n♥" + (b.Hp + 499) / 500 +
                           (b.Bank > 0 ? " ◆" + b.Bank : "");
                    tint = b.Owner == Side.You ? new Color(1f, 0.9f, 0.55f) : new Color(0.65f, 0.8f, 1f);
                }
                else if (o is ChargeUnit)
                {
                    var chu = (ChargeUnit)o;
                    text = o.Owner == Side.You ? "SET ◆" + chu.Invested : "SET ?";
                    tint = new Color(0.8f, 0.8f, 0.85f);
                }
                else
                {
                    text = o.Owner == Side.You ? "TRAP" : "SET ?";   // the foe cannot tell
                    tint = new Color(0.8f, 0.8f, 0.85f);
                }

                var st = new GUIStyle(_tiny);
                st.normal.textColor = tint;
                GUI.Label(new Rect(x - 45, y, 90, 26), text, st);
            }
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
