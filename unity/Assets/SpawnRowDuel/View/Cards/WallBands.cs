using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The two castle walls and everything set into them: each side's vitals, the foe's hand of
    /// backs, and the turn read-out (spec 09 §4).
    ///
    /// The walls are the screen's own top and bottom edges rather than objects on the field - a
    /// wall is the edge of the world you fight in front of, not a thing you fight over - and they
    /// SLIDE (spec 09 §4.4). Retracted, a wall shows one rail: your hand hangs off yours and their
    /// life pool reads off theirs, which is all either wall owes you while you are playing. Look at
    /// one - hover it, or tap it on a phone - and it rises to its full tower windows: life, mana,
    /// piles, workers. Look away and it goes back down.
    ///
    /// That is why the furniture lives INSIDE the stone rather than beside it. The vitals stack
    /// away from the crest, so "what shows when the wall is down" is decided by the same slide that
    /// draws it, and there is no second layout that can disagree about the retracted state.
    ///
    /// The trigger is the TOWER spans only, never the middle. The middle span is where the hands
    /// are, and a wall that opened every time you reached for a card would spend the match sitting
    /// on the board you are trying to tap.
    ///
    /// This is not a MonoBehaviour: HandBar owns the UIDocument, and a second one on the same
    /// object would be a second panel with its own sorting order to keep in step. It takes the
    /// root and builds into it, under the hand, so the cards are held IN FRONT of the stone.
    /// </summary>
    public sealed class WallBands
    {
        /// <summary>The tower spans at each end, which is where the vitals go (spec 09 §4.1).</summary>
        public const float TowerSpan = 0.21f;

        /// <summary>How long a wall stays up after the last touch, on a device with no hover.</summary>
        const float Linger = 1.6f;
        const float SlideSeconds = 0.22f;

        VisualElement _foeStone, _youStone, _foeHand;
        Vitals _foe, _you;
        Label _turn, _phase;

        float _foeOpen, _youOpen;
        float _foeTouched = -99f, _youTouched = -99f;

        /// <summary>
        /// Probe hook: hold both walls up. A wall that only rises when it is looked at cannot be
        /// looked at by a batchmode still, and the extended state is the half of this surface that
        /// a screenshot would otherwise never show.
        /// </summary>
        public static bool ForceOpen;

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

            _foe = MakeVitals(_foeStone, true);
            _you = MakeVitals(_youStone, false);

            // Whose turn it is belongs to the SCREEN, not to the wall. Parented to the stone it
            // rode the slide, and a retracted wall is 30 units tall - which cut the top line clean
            // off. It is also not the foe's information; it is the match's.
            _turn = Text("", UiFont.DisplayBold, 15);
            _turn.style.position = Position.Absolute;
            _turn.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(_turn);

            _phase = Text("", UiFont.BodyBold, 11);
            _phase.style.position = Position.Absolute;
            _phase.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(_phase);

            // the hands are anchored to the SCREEN edge, not to the wall: a card you can reach for
            // must not move because the wall behind it happens to be rising
            _foeHand = new VisualElement { pickingMode = PickingMode.Ignore };
            _foeHand.style.position = Position.Absolute;
            _foeHand.style.overflow = Overflow.Hidden;
            root.Add(_foeHand);
        }

        static VisualElement Stone(VisualElement root)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0;
            root.Add(v);
            return v;
        }

        /// <summary>
        /// The left tower's read-out, stacked AWAY from the crest: life first, so a retracted wall
        /// shows the life pool and nothing else, and the rest arrives as the wall rises.
        /// </summary>
        Vitals MakeVitals(VisualElement stone, bool foe)
        {
            var v = new Vitals();
            v.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Root.style.position = Position.Absolute;
            v.Root.style.flexDirection = foe ? FlexDirection.ColumnReverse : FlexDirection.Column;

            v.Life = Text("", UiFont.DisplayBlack, 19);
            v.Mana = Text("", UiFont.DisplayBold, 14);
            v.Piles = Text("", UiFont.BodyRegular, 11);
            v.Workers = Text("", UiFont.BodyRegular, 11);

            // Life and mana share the rail line. Life alone is what the wall OWES you - it is the
            // wall's own hit points - but "what can I afford" is asked every turn too, and a
            // number you have to open a wall to read is a number you will misremember.
            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.style.flexDirection = FlexDirection.Row;
            rail.style.alignItems = Align.FlexEnd;
            v.Life.style.marginRight = 9;
            rail.Add(v.Life);
            rail.Add(v.Mana);

            v.Root.Add(rail);
            v.Root.Add(v.Piles);
            v.Root.Add(v.Workers);
            stone.Add(v.Root);
            return v;
        }

        /// <summary>Position and repaint. Cheap enough to run every frame; the raster is cached.</summary>
        public void Layout(GameState s, ElementPalette palette, float panelW)
        {
            float scale = HudLayout.Scale;
            Rescale();                       // font sizes are real pixels; a resize re-applies them

            int w = Mathf.Max(16, Mathf.RoundToInt(panelW));
            int over = Mathf.RoundToInt(HudLayout.WallOverhang * scale);
            int full = Mathf.RoundToInt(HudLayout.WallFullH * scale);
            float foeRail = HudLayout.RailTopPx, youRail = HudLayout.RailBottomPx;

            var foeEl = palette.Of(s.P(Side.Foe).PrimaryColor).Color;
            var youEl = palette.Of(s.P(Side.You).PrimaryColor).Color;

            // The stone is rastered at its real pixel size, so its courses are pixel-sized rather
            // than stretched - which means a resize rebuilds it, and nothing else does. It is
            // rastered at the FULL height and slid out of frame, the way the reference wall
            // translates rather than resizes: a stretched texture would betray the animation.
            string sig = w + "/" + full + "/" + over + "/" + ((Color32)foeEl).GetHashCode()
                       + "/" + ((Color32)youEl).GetHashCode();
            if (sig != _stoneSig)
            {
                _stoneSig = sig;
                _foeStone.style.backgroundImage =
                    new StyleBackground(WallTextures.Band(true, foeEl, w, full + over, over, scale));
                _youStone.style.backgroundImage =
                    new StyleBackground(WallTextures.Band(false, youEl, w, full + over, over, scale));
                _foeStone.style.height = full + over;
                _youStone.style.height = full + over;
            }

            float foeCur = Slide(ref _foeOpen, ref _foeTouched, true, panelW, over, full, foeRail);
            float youCur = Slide(ref _youOpen, ref _youTouched, false, panelW, over, full, youRail);

            _foeStone.style.top = -(full - foeCur);
            _youStone.style.bottom = -(full - youCur);

            // what the walls cover right now - the board must not take taps through them, and the
            // hands are opaque too even when the wall behind them is down
            HudLayout.TopBlockPx = Mathf.Max(foeCur, HudLayout.FoeHandBandPx);
            HudLayout.BottomBlockPx = Mathf.Max(youCur, HudLayout.HandBandPx);

            float tower = panelW * TowerSpan;
            float pad = 10f * scale;
            float crestPad = over + 4f * scale;

            Fill(_foe, s, Side.Foe, foeEl);
            Fill(_you, s, Side.You, youEl);

            _foe.Root.style.left = pad;
            _foe.Root.style.bottom = crestPad;        // the foe's crest is its LOWER edge
            _foe.Root.style.width = tower - pad;

            _you.Root.style.left = pad;
            _you.Root.style.top = crestPad;
            _you.Root.style.width = tower - pad;

            _turn.text = "TURN " + s.TurnNumber;
            _phase.text = s.Turn == Side.You ? "YOUR TURN" : "FOE TURN";
            var live = s.Turn == Side.You ? new Color(1f, 0.85f, 0.4f) : new Color(0.65f, 0.8f, 1f);
            _turn.style.color = live;
            _phase.style.color = live;

            _turn.style.right = pad; _turn.style.width = tower - pad;
            _turn.style.top = 3f * scale;
            _phase.style.right = pad; _phase.style.width = tower - pad;
            _phase.style.top = 20f * scale;

            LayoutFoeHand(s, palette, panelW, scale);
        }

        /// <summary>
        /// Raise the wall while it is being LOOKED AT, lower it when it is not.
        ///
        /// Only the tower spans count. The middle is where the hands are, and a wall that opened
        /// every time you reached for a card would spend the match sitting on the board.
        /// A mouse gets the wall for exactly as long as it hovers; a touch gets a linger, because
        /// a finger that has lifted is still looking.
        /// </summary>
        float Slide(ref float open, ref float touched, bool foe, float panelW,
                    float over, float full, float rail)
        {
            float cur = Mathf.Lerp(rail, full, open);

            var p = (Vector2)Input.mousePosition;
            float px = p.x, py = Screen.height - p.y;                 // top-left origin
            bool inTower = px < panelW * TowerSpan || px > panelW * (1f - TowerSpan);
            float band = Mathf.Max(rail, cur + over);
            bool inBand = foe ? py >= 0f && py <= band
                              : py <= Screen.height && py >= Screen.height - band;

            bool held = Input.GetMouseButton(0) || Input.touchCount > 0;
            bool hovering = Input.mousePresent && Input.touchCount == 0;
            bool looking = ForceOpen || (inTower && inBand && (held || hovering));

            float now = Time.unscaledTime;
            if (looking) touched = now;

            // hover answers immediately; a touch device has nothing to leave with, so it lingers
            float target = ForceOpen ? 1f
                         : hovering ? (looking ? 1f : 0f)
                                    : (now - touched < Linger ? 1f : 0f);
            open = Mathf.MoveTowards(open, target, Time.unscaledDeltaTime / SlideSeconds);
            return Mathf.Lerp(rail, full, open);
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
        /// Their hand, face down, hanging from their wall and standing proud of it - the mirror of
        /// yours, because a hand held at a wall is not a hand tucked inside one.
        ///
        /// Full backs rather than a peek: a peek works for your hand because the banner it shows
        /// names the card, and the back of a card has nothing to read. What has to carry from
        /// across the board is how many.
        /// </summary>
        void LayoutFoeHand(GameState s, ElementPalette palette, float panelW, float scale)
        {
            int n = s.P(Side.Foe).Hand.Count;
            var sleeve = palette.Of(s.P(Side.Foe).PrimaryColor).Color;

            float band = HudLayout.FoeHandBandPx;
            float cardH = band * 2.2f;
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

                // hung from above the screen edge, so what shows is the card's lower band and the
                // rest is behind their wall - the mirror of the way yours hangs below
                card.style.top = -(cardH - band);
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
            Size(_foe.Life, 19); Size(_foe.Mana, 14); Size(_foe.Piles, 11); Size(_foe.Workers, 11);
            Size(_you.Life, 19); Size(_you.Mana, 14); Size(_you.Piles, 11); Size(_you.Workers, 11);
            Size(_turn, 15); Size(_phase, 11);
        }

        static void Size(Label l, float size)
        {
            if (l != null) l.style.fontSize = size * HudLayout.Scale;
        }
    }
}
