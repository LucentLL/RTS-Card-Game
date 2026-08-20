using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The placeholder HUD, engine-truthful: everything shown is read from GameState each frame,
    /// and the one action button submits real commands. IMGUI on purpose - it needs no font
    /// asset while the glyph/TMP plan is still open (GAPS P0), and it scales itself for phone
    /// DPI so it is readable on the Pages test surface.
    /// </summary>
    [RequireComponent(typeof(MatchController))]
    public class MatchHud : MonoBehaviour
    {
        private MatchController _match;
        private BoardInput _input;
        private string _hint = "";
        private float _hintUntil;

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _input = GetComponent<BoardInput>();
        }

        void OnGUI()
        {
            if (_match == null || _match.Engine == null) return;
            var s = _match.Engine.State;

            // Scale the whole layer for small high-DPI screens: design in ~460 logical px width.
            float scale = Mathf.Max(1f, Screen.width / 460f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            var label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            label.normal.textColor = Color.white;
            var small = new GUIStyle(label) { fontSize = 11 };
            small.normal.textColor = new Color(0.75f, 0.78f, 0.85f);

            // ---- top: the two players ----------------------------------------------------
            GUI.Box(new Rect(6, 6, w - 12, 64), GUIContent.none);
            var foe = s.P(Side.Foe);
            var you = s.P(Side.You);

            GUI.Label(new Rect(12, 8, w - 24, 18),
                "FOE   life " + foe.Life + "   mana " + foe.Mana +
                "   hand " + foe.Hand.Count + "   deck " + foe.Deck.Count +
                "   workers " + Workers(s, Side.Foe), label);

            GUI.Label(new Rect(12, 26, w - 24, 18),
                "YOU   life " + you.Life + "   mana " + you.Mana +
                "   hand " + you.Hand.Count + "   deck " + you.Deck.Count +
                "   workers " + Workers(s, Side.You), label);

            GUI.Label(new Rect(12, 46, w - 24, 16),
                "turn " + s.TurnNumber + " · " + (s.Turn == Side.You ? "YOUR" : "FOE") +
                " turn · " + s.Phase.ToString().ToUpperInvariant() +
                "    [Tab] angle    seed " + (_match.Seed % 100000), small);

            // ---- bottom: the one contextual action ---------------------------------------
            if (!s.IsOver && s.Turn == Side.You && s.Phase != TurnPhase.End)
            {
                string caption = s.Phase == TurnPhase.Upkeep ? "HARVEST"
                    : s.Phase == TurnPhase.Draw ? "DRAW" : "END TURN";

                var btn = new GUIStyle(GUI.skin.button) { fontSize = 20 };
                var rect = new Rect(w / 2f - 90, h - 64, 180, 52);
                if (GUI.Button(rect, caption, btn))
                {
                    ICommand cmd = s.Phase == TurnPhase.Upkeep ? new HarvestCommand(Side.You)
                        : s.Phase == TurnPhase.Draw ? (ICommand)new DrawForTurnCommand(Side.You)
                        : new EndTurnCommand(Side.You);
                    var why = _match.TryHuman(cmd);
                    if (why != Rejection.None) Hint(HintFor(why));
                }
            }
            else if (s.IsOver)
            {
                var over = new GUIStyle(label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, h / 2f - 20, w, 40), "MATCH OVER — " + s.Outcome, over);
            }

            // ---- hint + hover readout ----------------------------------------------------
            if (Time.unscaledTime < _hintUntil && _hint.Length > 0)
            {
                var hint = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
                hint.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(0, h - 92, w, 20), _hint, hint);
            }

            if (_input != null && _input.Hover.HasValue)
                GUI.Label(new Rect(12, 74, w - 24, 16), "cell " + _input.Hover.Value, small);

            // ---- log ---------------------------------------------------------------------
            var log = _match.Log;
            int lines = Mathf.Min(6, log.Count);
            float y = h - 92 - lines * 15f;
            for (int i = log.Count - lines; i < log.Count; i++)
            {
                GUI.Label(new Rect(12, y, w - 24, 16), log[i], small);
                y += 15f;
            }
        }

        static string Workers(GameState s, Side side)
        {
            var p = s.P(side);
            return p.Workers[0].ReadyCount + "/" + p.Workers[0].Count + " · " +
                   p.Workers[1].ReadyCount + "/" + p.Workers[1].Count + " · " +
                   p.Workers[2].ReadyCount + "/" + p.Workers[2].Count;
        }

        static string HintFor(Rejection why)
        {
            switch (why)
            {
                case Rejection.ShortfallUnsettled: return "Settle the worker shortfall first";
                case Rejection.WrongPhase: return "Not in this phase";
                case Rejection.NotYourTurn: return "Not your turn";
                default: return why.ToString();
            }
        }

        void Hint(string text)
        {
            _hint = text;
            _hintUntil = Time.unscaledTime + 2.5f;
        }
    }
}
