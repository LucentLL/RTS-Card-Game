using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The two castle walls and everything set into them: each side's vitals, the foe's hand of
    /// backs, and the turn/phase read-out (spec 09 §4).
    ///
    /// Three things move here at once, and they are the same thing:
    ///
    /// - The walls are no longer ON the field. They were two red slabs lying past each back row,
    ///   which meant the field had to reserve a row's depth at each end for them and the board
    ///   ended up floating in the middle of the screen with weather all around it. A wall is the
    ///   edge of the world you fight in front of, so it is the screen edge: retracted battlements
    ///   at top and bottom, and the field runs from one to the other.
    /// - The two players are SPLIT. One four-line block in the top-left corner listing both sides'
    ///   numbers reads as a scoreboard; a wall each, theirs above you and yours below, reads as
    ///   two keeps facing each other, and you never have to check which row is yours.
    /// - The foe's hand EXISTS. It was not drawn at all - only a count buried in that block - and
    ///   "how many cards do they have" is a question you ask before every attack.
    ///
    /// This is not a MonoBehaviour: HandBar owns the UIDocument, and a second one on the same
    /// object would be a second panel with its own sorting order to keep in step. It takes the
    /// root and builds into it, under the hand, so the cards are held IN FRONT of the stone.
    /// </summary>
    public sealed class WallBands
    {
        /// <summary>The tower spans at each end, which is where the vitals go (spec 09 §4.1).</summary>
        public const float TowerSpan = 0.21f;

        VisualElement _foeStone, _youStone, _foeHand;
        Vitals _foe, _you;
        Label _turn, _phase;

        struct Vitals
        {
            public VisualElement Root;
            public Label Life, Mana, Piles, Workers;
        }

        string _handSig = "";
        string _stoneSig = "";

        public void Attach(VisualElement root)
        {
            _foeStone = Stone(root);
            _youStone = Stone(root);

            _foeHand = new VisualElement { pickingMode = PickingMode.Ignore };
            _foeHand.style.position = Position.Absolute;
            _foeHand.style.overflow = Overflow.Hidden;
            root.Add(_foeHand);

            _foe = MakeVitals(root);
            _you = MakeVitals(root);

            _turn = Text("", UiFont.DisplayBold, 15);
            _turn.style.position = Position.Absolute;
            _turn.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(_turn);

            _phase = Text("", UiFont.BodyBold, 11);
            _phase.style.position = Position.Absolute;
            _phase.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(_phase);
        }

        static VisualElement Stone(VisualElement root)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0;
            root.Add(v);
            return v;
        }

        Vitals MakeVitals(VisualElement root)
        {
            var v = new Vitals();
            v.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Root.style.position = Position.Absolute;
            v.Root.style.flexDirection = FlexDirection.Column;
            v.Root.style.justifyContent = Justify.Center;

            v.Life = Text("", UiFont.DisplayBlack, 19);
            v.Mana = Text("", UiFont.DisplayBold, 13);
            v.Piles = Text("", UiFont.BodyRegular, 11);
            v.Workers = Text("", UiFont.BodyRegular, 11);

            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.FlexEnd;
            v.Life.style.marginRight = 8;
            top.Add(v.Life);
            top.Add(v.Mana);

            v.Root.Add(top);
            v.Root.Add(v.Piles);
            v.Root.Add(v.Workers);
            root.Add(v.Root);
            return v;
        }

        /// <summary>Position and repaint. Cheap enough to run every frame; the raster is cached.</summary>
        public void Layout(GameState s, ElementPalette palette, float panelW)
        {
            float scale = HudLayout.Scale;
            Rescale();                       // font sizes are real pixels; a resize re-applies them
            int w = Mathf.Max(16, Mathf.RoundToInt(panelW));
            int over = Mathf.RoundToInt(HudLayout.WallOverhang * scale);
            int topH = Mathf.RoundToInt(HudLayout.TopPx) + over;
            int botH = Mathf.RoundToInt(HudLayout.BottomPx) + over;

            var foeEl = palette.Of(s.P(Side.Foe).PrimaryColor).Color;
            var youEl = palette.Of(s.P(Side.You).PrimaryColor).Color;

            // The stone is rastered at its real pixel size, so its courses are pixel-sized rather
            // than stretched - which means a resize has to rebuild it, and nothing else does.
            string sig = w + "/" + topH + "/" + botH + "/" + ((Color32)foeEl).GetHashCode()
                       + "/" + ((Color32)youEl).GetHashCode();
            if (sig != _stoneSig)
            {
                _stoneSig = sig;
                _foeStone.style.backgroundImage =
                    new StyleBackground(WallTextures.Band(true, foeEl, w, topH, over, scale));
                _youStone.style.backgroundImage =
                    new StyleBackground(WallTextures.Band(false, youEl, w, botH, over, scale));
            }

            _foeStone.style.top = 0; _foeStone.style.height = topH;
            _youStone.style.bottom = 0; _youStone.style.height = botH;

            float tower = panelW * TowerSpan;
            float pad = 10f * scale;

            Fill(_foe, s, Side.Foe, foeEl);
            Fill(_you, s, Side.You, youEl);

            _foe.Root.style.left = pad;
            _foe.Root.style.top = 0;
            _foe.Root.style.width = tower - pad;
            _foe.Root.style.height = HudLayout.TopPx;

            _you.Root.style.left = pad;
            _you.Root.style.bottom = 0;
            _you.Root.style.width = tower - pad;
            _you.Root.style.height = HudLayout.BottomPx;

            _turn.text = "TURN " + s.TurnNumber;
            _phase.text = (s.Turn == Side.You ? "YOUR TURN · " : "FOE TURN · ")
                        + s.Phase.ToString().ToUpperInvariant();
            var live = s.Turn == Side.You ? new Color(1f, 0.85f, 0.4f) : new Color(0.65f, 0.8f, 1f);
            _turn.style.color = live;
            _phase.style.color = live;

            float turnW = tower - pad;
            _turn.style.right = pad; _turn.style.width = turnW;
            _turn.style.top = HudLayout.TopPx * 0.16f;
            _phase.style.right = pad; _phase.style.width = turnW;
            _phase.style.top = HudLayout.TopPx * 0.52f;

            LayoutFoeHand(s, palette, panelW, scale);
        }

        static void Fill(Vitals v, GameState s, Side side, Color element)
        {
            var p = s.P(side);
            v.Life.text = "♥" + p.Life;
            v.Life.style.color = side == Side.You ? new Color(1f, 0.78f, 0.72f)
                                                  : new Color(0.80f, 0.86f, 1f);
            v.Mana.text = "◆" + p.Mana;
            v.Mana.style.color = element;
            v.Piles.text = "hand " + p.Hand.Count + " · deck " + p.Deck.Count + " · gy " + p.Grave.Count;
            v.Piles.style.color = new Color(0.74f, 0.77f, 0.84f);
            v.Workers.text = "⚒ " + p.Workers[0].ReadyCount + "/" + p.Workers[0].Count + "·"
                                  + p.Workers[1].ReadyCount + "/" + p.Workers[1].Count + "·"
                                  + p.Workers[2].ReadyCount + "/" + p.Workers[2].Count;
            v.Workers.style.color = new Color(0.68f, 0.71f, 0.79f);
        }

        /// <summary>
        /// Their hand, face down, fanned across the middle span of their wall.
        ///
        /// Full backs rather than the peeking arrangement yours uses: a peek works for your hand
        /// because the banner it shows names the card, and the back of a card has nothing to read.
        /// What has to carry from across the board is HOW MANY, so all of them are visible.
        /// </summary>
        void LayoutFoeHand(GameState s, ElementPalette palette, float panelW, float scale)
        {
            int n = s.P(Side.Foe).Hand.Count;
            var sleeve = palette.Of(s.P(Side.Foe).PrimaryColor).Color;

            float band = HudLayout.TopPx;
            float cardH = band * 0.82f;
            float cardW = cardH / CardFace.Aspect;

            float spanL = panelW * TowerSpan, spanR = panelW * (1f - TowerSpan);
            _foeHand.style.left = spanL;
            _foeHand.style.width = spanR - spanL;
            _foeHand.style.top = 0;
            _foeHand.style.height = band;

            string sig = n + "/" + Mathf.RoundToInt(cardH) + "/" + Mathf.RoundToInt(panelW)
                       + "/" + ((Color32)sleeve).GetHashCode();
            if (sig == _handSig) return;
            _handSig = sig;

            _foeHand.Clear();
            if (n <= 0) return;

            float avail = (spanR - spanL) - 8f * scale;
            float step = Mathf.Min(cardW * 0.86f, n > 1 ? (avail - cardW) / (n - 1) : 0f);
            float x0 = ((spanR - spanL) - (step * (n - 1) + cardW)) * 0.5f;
            var back = CardPlateTextures.Back(sleeve);

            for (int i = 0; i < n; i++)
            {
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.style.position = Position.Absolute;
                card.style.left = x0 + i * step;
                card.style.width = cardW;
                card.style.height = cardH;
                card.style.backgroundImage = new StyleBackground(back);

                // a shallow fan, hinged at the top edge the way a held hand hangs
                float t = n > 1 ? (i / (float)(n - 1)) - 0.5f : 0f;
                card.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(0));
                card.style.rotate = new Rotate(new Angle(t * 7f, AngleUnit.Degree));
                card.style.top = band * 0.09f + Mathf.Abs(t) * band * 0.05f;

                _foeHand.Add(card);
            }
        }

        static Label Text(string s, UiFont face, float size)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            var font = ViewAssets.Font(face);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            l.style.fontSize = size * HudLayout.Scale;
            l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.textShadow = new TextShadow
            {
                offset = new Vector2(0f, 1f),
                blurRadius = 2f,
                color = new Color(0f, 0f, 0f, 0.85f),
            };
            return l;
        }

        void Rescale()
        {
            Size(_foe.Life, 19); Size(_foe.Mana, 13); Size(_foe.Piles, 11); Size(_foe.Workers, 11);
            Size(_you.Life, 19); Size(_you.Mana, 13); Size(_you.Piles, 11); Size(_you.Workers, 11);
            Size(_turn, 15); Size(_phase, 11);
        }

        static void Size(Label l, float size)
        {
            if (l != null) l.style.fontSize = size * HudLayout.Scale;
        }
    }
}
