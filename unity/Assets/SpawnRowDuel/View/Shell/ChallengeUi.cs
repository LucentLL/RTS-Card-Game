using SpawnRowDuel.Campaign;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Campaign;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// The four-line challenge two champions trade before a campaign duel.
    ///
    /// Fire-Emblem furniture, deliberately: portraits at the lower corners with the silent one
    /// dimmed, a header saying who marches on whom, text that types itself out, and a tap that
    /// completes the line before it advances - so an impatient player never loses a line they were
    /// still reading.
    ///
    /// The script is authored data (<see cref="ChallengeDialogue"/>); nothing here decides what is
    /// said, only how long it takes to say it.
    /// </summary>
    public sealed class ChallengeUi
    {
        const float CharSeconds = 0.014f;      // 14 ms a character, as the browser build types

        readonly VisualElement _root;
        readonly ICardCatalog _cat;
        readonly Element _attacker, _defender;
        readonly System.Action _onDone;
        readonly DialogueLine[] _lines;

        VisualElement _atkPortrait, _defPortrait, _advance;
        Label _speaker, _body;

        int _index;
        int _typed;
        float _nextChar;
        bool _done;

        public ChallengeUi(VisualElement root, ICardCatalog cat, Element attacker, Element defender,
                           bool defenderOwnCapital, System.Action onDone)
        {
            _root = root; _cat = cat; _attacker = attacker; _defender = defender; _onDone = onDone;
            _lines = ChallengeDialogue.Build(attacker, defender, defenderOwnCapital,
                                             new Pcg32((ulong)Random.Range(1, int.MaxValue)));
        }

        public void Build()
        {
            _root.Clear();

            var bg = new VisualElement();
            UiKit.Fill(bg);
            bg.style.backgroundColor = new Color(0.02f, 0.02f, 0.035f, 0.97f);
            _root.Add(bg);

            // a wash of each champion's colour behind their corner - the ambient glow the
            // reference paints under the figures
            Glow(bg, GlobeView.ElementColour(_attacker), true);
            Glow(bg, GlobeView.ElementColour(_defender), false);

            var header = UiKit.Row(_root);
            header.style.position = Position.Absolute;
            header.style.left = 0; header.style.right = 0;
            header.style.top = 14f * UiKit.S;
            header.style.justifyContent = Justify.Center;

            var ad = ElementOf(_attacker);
            var dd = ElementOf(_defender);
            UiKit.Badge(header, ad != null ? ad.Glyph : "", GlobeView.ElementColour(_attacker), 22f);
            var line = UiKit.Text(header, CampaignRules.Name(_attacker) + " marches on "
                                  + CampaignRules.Name(_defender), 14f, UiFont.DisplayBold, UiKit.Ink);
            line.style.marginLeft = 7f * UiKit.S;
            line.style.marginRight = 7f * UiKit.S;
            UiKit.Badge(header, dd != null ? dd.Glyph : "", GlobeView.ElementColour(_defender), 22f);

            var skip = UiKit.Btn(_root, "Skip ▸▸", Finish, 12f, UiKit.Dim);
            skip.style.position = Position.Absolute;
            skip.style.right = 12f * UiKit.S;
            skip.style.top = 10f * UiKit.S;

            _atkPortrait = Portrait(ChallengeDialogue.Champion(_attacker), true);
            _defPortrait = Portrait(ChallengeDialogue.Champion(_defender), false);

            var box = UiKit.Glass(_root, 14f);
            box.style.position = Position.Absolute;
            box.style.left = 8f * UiKit.S; box.style.right = 8f * UiKit.S;
            box.style.bottom = 10f * UiKit.S;
            box.style.minHeight = 92f * UiKit.S;
            UiKit.Radius(box, 8f);

            _speaker = UiKit.Text(box, "", 15f, UiFont.DisplayBlack, UiKit.Gold);
            _body = UiKit.Text(box, "", 14f, UiFont.BodyRegular, UiKit.Ink);
            _body.style.marginTop = 5f * UiKit.S;

            _advance = UiKit.Text(box, "▼", 13f, UiFont.BodyBold, UiKit.Gold);
            _advance.style.position = Position.Absolute;
            _advance.style.right = 12f * UiKit.S;
            _advance.style.bottom = 6f * UiKit.S;
            _advance.style.display = DisplayStyle.None;

            // the whole screen advances, so a phone never has to find a small target
            var catcher = new VisualElement();
            UiKit.Fill(catcher);
            catcher.RegisterCallback<PointerDownEvent>(e => Advance());
            _root.Add(catcher);
            catcher.SendToBack();
            catcher.BringToFront();
            skip.BringToFront();

            StartLine(0);
        }

        static void Glow(VisualElement parent, Color c, bool left)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.width = 420f * UiKit.S;
            v.style.height = 420f * UiKit.S;
            v.style.bottom = -80f * UiKit.S;
            if (left) v.style.left = -60f * UiKit.S; else v.style.right = -60f * UiKit.S;
            v.style.backgroundColor = new Color(c.r, c.g, c.b, 0.13f);
            UiKit.Radius(v, 210f);
            parent.Add(v);
        }

        VisualElement Portrait(string championName, bool attacker)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.bottom = 96f * UiKit.S;
            v.style.width = 190f * UiKit.S;
            v.style.height = 240f * UiKit.S;
            if (attacker) v.style.left = 14f * UiKit.S; else v.style.right = 14f * UiKit.S;

            var art = FindArt(championName);
            if (art != null)
            {
                v.style.backgroundImage = new StyleBackground(art);
                v.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                v.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
                v.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                // the defender faces back across the field
                if (!attacker) v.style.scale = new Scale(new Vector2(-1f, 1f));
            }
            else
            {
                v.style.justifyContent = Justify.FlexEnd;
                v.style.alignItems = Align.Center;
                var el = attacker ? _attacker : _defender;
                UiKit.Badge(v, ElementOf(el) != null ? ElementOf(el).Glyph : "",
                            GlobeView.ElementColour(el), 92f);
            }

            _root.Add(v);
            return v;
        }

        Sprite FindArt(string championName)
        {
            var shell = Object.FindFirstObjectByType<GameShell>();
            if (shell == null || shell.Match == null) return null;
            var def = shell.Match.DefOf(championName);
            if (def == null) return null;
            return def.FieldArt != null ? def.FieldArt : def.CardArt;
        }

        ElementDef ElementOf(Element el)
        {
            if (_cat == null) return null;
            foreach (var d in _cat.Elements) if (d.El == el) return d;
            return null;
        }

        // ── the typewriter ──────────────────────────────────────────────────────────────

        void StartLine(int i)
        {
            _index = i;
            _typed = 0;
            _nextChar = Time.unscaledTime;
            var line = _lines[i];

            _speaker.text = line.SpeakerName;
            _speaker.style.color = GlobeView.ElementColour(line.Speaker);
            _body.text = "";
            _advance.style.display = DisplayStyle.None;

            bool attackerSpeaks = line.Side == DialogueSide.Attacker;
            Lit(_atkPortrait, attackerSpeaks);
            Lit(_defPortrait, !attackerSpeaks);
        }

        static void Lit(VisualElement portrait, bool speaking)
        {
            if (portrait == null) return;
            portrait.style.opacity = speaking ? 1f : 0.45f;
            portrait.style.translate = new Translate(0f, speaking ? -6f * UiKit.S : 0f);
        }

        public void Tick()
        {
            if (_done) return;
            var text = _lines[_index].Text;
            if (_typed >= text.Length)
            {
                _advance.style.display = DisplayStyle.Flex;
                float bob = Mathf.Sin(Time.unscaledTime * 5f) * 2f * UiKit.S;
                _advance.style.translate = new Translate(0f, bob);
                return;
            }

            while (_typed < text.Length && Time.unscaledTime >= _nextChar)
            {
                _typed++;
                _nextChar += CharSeconds;
            }
            _body.text = text.Substring(0, _typed);
        }

        void Advance()
        {
            if (_done) return;
            var text = _lines[_index].Text;
            if (_typed < text.Length)
            {
                _typed = text.Length;          // a tap mid-line completes it rather than skipping it
                _body.text = text;
                return;
            }
            if (_index + 1 < _lines.Length) StartLine(_index + 1);
            else Finish();
        }

        void Finish()
        {
            if (_done) return;                 // idempotent: skip and the last tap must not both fire
            _done = true;
            if (_onDone != null) _onDone();
        }
    }
}
