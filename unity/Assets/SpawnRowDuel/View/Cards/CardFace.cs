using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The DM_Template card frame - ONE authoring source, reused at every scale (spec 09 §6.1).
    ///
    /// Anatomy, top to bottom, exactly as the reference stylesheet builds it:
    ///   ivory name banner (cost circle · element gem · name · race line)
    ///   → dominant art window → type lozenge riding the seam → white ability box
    ///   → footer stat bar (power · ⚒ chip · ♥ health)
    ///
    /// The element accent threads through the cost circle, the banner underline, the art ring, the
    /// lozenge and the outer border. Every size is expressed as a fraction of the card's WIDTH, so
    /// the same frame is a 90 px hand card, a 250 px inspect card or a board plate with no second
    /// layout - which was the whole point of the four-scales requirement.
    ///
    /// Built in C# rather than UXML/USS deliberately (DECISIONS D20): the project is developed
    /// headlessly from a shell, USS cannot express the gradients this frame is made of, and every
    /// tint has to be set from code anyway. The numbers below are the stylesheet's numbers.
    /// </summary>
    public sealed class CardFace : VisualElement
    {
        public const float Aspect = 1033f / 744f;      // the physical card proportion

        // fractions of card width, from src/styles/03_cards.css via spec 09 §6.1
        /// <summary>
        /// The card's bands, as fractions of its WIDTH, and they are a budget rather than four
        /// independent numbers: banner + art + rules + stats has to come to Aspect, 1.388.
        ///
        ///   0.135 + 0.944 + 0.194 + 0.115 = 1.388
        ///
        /// The art is the one that is fixed. It runs the full width of the card less a hair of
        /// frame either side, and it is SQUARE because every illustration in this project is - so
        /// its height follows from its width and the other three take what is left. That is the
        /// reference card's own arrangement: a thin name banner with the cost badge hanging off
        /// it into the picture, a picture that dominates, a modest ability box, a stat strip.
        ///
        /// The art box was 0.68 for a moment - square, but INSET, with frame showing either side.
        /// It fitted, and it wasted a fifth of the card's width on nothing.
        /// </summary>
        const float BannerH = 0.135f;
        const float ArtInset = 0.028f;
        const float ArtSide = 1f - 2f * ArtInset;
        const float StatsH = 0.115f;
        const float CostSize = 0.185f;
        const float GemSize = 0.145f;
        const float NameSize = 0.108f;
        const float TypeSize = 0.062f;
        const float RibbonSize = 0.072f;
        const float RulesSize = 0.100f;
        const float PowerSize = 0.200f;
        const float HpSize = 0.135f;
        const float ChipSize = 0.070f;

        readonly VisualElement _banner, _costCircle, _gem, _artWin, _art, _vignette, _ribbon, _rulesBox, _stats;
        readonly Label _cost, _gemGlyph, _name, _type, _ribbonText, _rules, _power, _hp, _chip;
        readonly VisualElement _stateChips, _names;

        float _width;
        float _nameSize;      // the size the name WANTS, before it is shrunk to fit its column
        string _fittedFor;    // the (name, size, column width) the current fit was measured for
        float _typeSize;      // ...and the race/type line under it, which clipped just as happily
        string _typeFittedFor;
        float _rulesSize;     // ...and the same pair for the ability box, which wraps
        string _rulesFittedFor;

        public CardFace()
        {
            AddToClassList("srd-card");
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;
            SetBorder(this, 1f, new Color(0.05f, 0.04f, 0.03f));
            SetRadius(this, 6f);
            style.backgroundColor = new Color(0.09f, 0.08f, 0.07f);

            // ── name banner ────────────────────────────────────────────────────────────────
            _banner = Row();
            _banner.style.backgroundImage = Background.FromTexture2D(CardTextures.Paper);
            _banner.style.alignItems = Align.Center;
            _banner.style.paddingLeft = 3; _banner.style.paddingRight = 4;
            _banner.style.borderBottomWidth = 1.5f;
            Add(_banner);

            // The cost badge HANGS OFF the banner into the picture, which is what lets the banner
            // be a third of its old height without the badge shrinking with it. It is the
            // reference card's arrangement and it is the only way the numbers add up: a banner
            // tall enough to contain a 0.185 badge is a banner that costs the art its width.
            _costCircle = new VisualElement { pickingMode = PickingMode.Ignore };
            _costCircle.style.position = Position.Absolute;
            _costCircle.style.backgroundImage = Background.FromTexture2D(CardTextures.Radial);
            _costCircle.style.alignItems = Align.Center;
            _costCircle.style.justifyContent = Justify.Center;
            _costCircle.style.flexShrink = 0;
            // added to the root LAST - see below

            _cost = Text("", UiFont.DisplayBlack);
            _cost.style.color = new Color(0.08f, 0.06f, 0.04f);
            _costCircle.Add(_cost);

            _gem = new VisualElement { pickingMode = PickingMode.Ignore };
            _gem.style.position = Position.Absolute;
            _gem.style.backgroundImage = Background.FromTexture2D(CardTextures.Gem);
            _gem.style.alignItems = Align.Center;
            _gem.style.justifyContent = Justify.Center;
            _gem.style.flexShrink = 0;
            // added to the root LAST - see below

            _gemGlyph = Text("", UiFont.Cjk);      // kanji-only: the static face is its PRIMARY font
            _gemGlyph.style.color = Color.white;
            _gemGlyph.style.unityTextOutlineWidth = 0.12f;
            _gemGlyph.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
            _gem.Add(_gemGlyph);

            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            names.style.flexGrow = 1;
            names.style.flexShrink = 1;

            // minWidth 0 and flexBasis 0, and they are the whole of "THE FOUNDRY" printing as
            // "THE FOUN". A flex item defaults to min-width:auto, which refuses to shrink below
            // its CONTENT - so a long name did not squeeze the column, it widened it past the
            // banner and got clipped by the card's own overflow. Worse, it took FitName with it:
            // that measures against this column's resolved width to decide how far to shrink, and
            // the width it was reading had already grown to fit the text, so the answer was
            // always "it fits".
            names.style.minWidth = 0;
            names.style.flexBasis = 0;
            names.style.overflow = Overflow.Hidden;
            names.style.marginLeft = 3;
            _banner.Add(names);
            _names = names;

            // The banner's width is not known at Bind time - it is a flex child, and the badge and
            // the gem that set its left padding are absolute. So the name is fitted when the
            // column's geometry actually resolves, and again whenever the card is resized.
            names.RegisterCallback<GeometryChangedEvent>(_ => FitName());
            names.RegisterCallback<GeometryChangedEvent>(_ => FitType());

            // alignSelf CENTER, not the column's default stretch. It keeps them centred AND makes
            // each label size to its own content - which is how Fit() reads the TRUE rendered
            // width off resolvedStyle instead of trusting MeasureTextSize, which lies about these.
            _name = Text("", UiFont.DisplayBlack);
            _name.style.color = new Color(0.10f, 0.078f, 0.04f);        // #1a140a
            _name.style.unityTextAlign = TextAnchor.MiddleCenter;
            _name.style.alignSelf = Align.Center;
            _name.style.whiteSpace = WhiteSpace.NoWrap;
            names.Add(_name);

            _type = Text("", UiFont.DisplayRegular);
            _type.style.unityTextAlign = TextAnchor.MiddleCenter;
            _type.style.letterSpacing = 1.4f;
            _type.style.alignSelf = Align.Center;
            _type.style.whiteSpace = WhiteSpace.NoWrap;
            names.Add(_type);

            // ── art window ─────────────────────────────────────────────────────────────────
            _artWin = new VisualElement { pickingMode = PickingMode.Ignore };
            // SQUARE, and inset, and sized in Bind rather than flexed. Every card illustration in
            // this project is square, and a window that flexed to whatever height was left came
            // out at three to two - so a third of every picture was cropped away to fill it. A
            // real trading card insets its art box and shows frame either side, which is the same
            // answer arrived at for the same reason.
            _artWin.style.flexGrow = 0;
            _artWin.style.flexShrink = 0;
            _artWin.style.alignSelf = Align.Center;
            _artWin.style.marginTop = 0;
            _artWin.style.overflow = Overflow.Hidden;
            SetBorder(_artWin, 1f, new Color(0f, 0f, 0f, 0.9f));
            Add(_artWin);

            _art = new VisualElement { pickingMode = PickingMode.Ignore };
            _art.style.position = Position.Absolute;
            _art.style.left = 0; _art.style.right = 0; _art.style.top = 0; _art.style.bottom = 0;
            _art.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _art.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            _artWin.Add(_art);

            _vignette = new VisualElement { pickingMode = PickingMode.Ignore };
            _vignette.style.position = Position.Absolute;
            _vignette.style.left = 0; _vignette.style.right = 0; _vignette.style.top = 0; _vignette.style.bottom = 0;
            _vignette.style.backgroundImage = Background.FromTexture2D(CardTextures.Vignette);
            _artWin.Add(_vignette);

            _stateChips = Row();
            _stateChips.style.position = Position.Absolute;
            _stateChips.style.right = 2; _stateChips.style.top = 2;
            _artWin.Add(_stateChips);

            // ── type lozenge, riding the seam ──────────────────────────────────────────────
            _ribbon = Row();
            _ribbon.style.alignSelf = Align.FlexStart;
            _ribbon.style.backgroundImage = Background.FromTexture2D(CardTextures.Sweep);
            _ribbon.style.paddingLeft = 6; _ribbon.style.paddingRight = 6;
            _ribbon.style.flexShrink = 0;
            SetRadius(_ribbon, 8f);
            Add(_ribbon);

            _ribbonText = Text("", UiFont.DisplayBold);
            _ribbonText.style.color = Color.white;
            _ribbonText.style.letterSpacing = 1.2f;
            _ribbonText.style.unityTextOutlineWidth = 0.08f;
            _ribbonText.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.6f);
            _ribbon.Add(_ribbonText);

            // ── ability box ────────────────────────────────────────────────────────────────
            _rulesBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _rulesBox.style.flexGrow = 1.45f;
            _rulesBox.style.flexShrink = 1;
            _rulesBox.style.overflow = Overflow.Hidden;
            _rulesBox.style.backgroundImage = Background.FromTexture2D(CardTextures.Paper);
            _rulesBox.style.unityBackgroundImageTintColor = new Color(1.06f, 1.06f, 1.04f);
            _rulesBox.style.paddingLeft = 4; _rulesBox.style.paddingRight = 4;
            _rulesBox.style.paddingTop = 2; _rulesBox.style.paddingBottom = 2;
            _rulesBox.style.marginLeft = 3; _rulesBox.style.marginRight = 3;
            SetBorder(_rulesBox, 1f, new Color(0f, 0f, 0f, 0.75f));
            SetRadius(_rulesBox, 6f);
            Add(_rulesBox);

            // Same reason the name is fitted from geometry: the box's height is a flex share of a
            // card whose width is not known until it is laid out.
            _rulesBox.RegisterCallback<GeometryChangedEvent>(_ => FitRules());

            _rules = Text("", UiFont.BodyRegular);
            _rules.style.color = new Color(0.13f, 0.11f, 0.08f);
            _rules.style.whiteSpace = WhiteSpace.Normal;
            _rulesBox.Add(_rules);

            // ── footer stat bar ────────────────────────────────────────────────────────────
            _stats = Row();
            _stats.style.alignItems = Align.Center;
            _stats.style.justifyContent = Justify.SpaceBetween;
            _stats.style.paddingLeft = 5; _stats.style.paddingRight = 5;
            _stats.style.flexShrink = 0;
            Add(_stats);

            // THE BADGE AND THE GEM GO ON LAST, because in UI Toolkit a sibling drawn later is
            // drawn on top. They are positioned to hang off the banner into the picture, and
            // added before the art window they were hanging BEHIND it - the picture clipped the
            // mana cost and the element clean off the card.
            Add(_costCircle);
            Add(_gem);

            _power = Text("", UiFont.DisplayBlack);
            _power.style.color = Color.white;
            _power.style.unityFontStyleAndWeight = FontStyle.Italic;
            _power.style.unityTextOutlineWidth = 0.22f;                 // the DM power number
            _power.style.unityTextOutlineColor = Color.black;
            _stats.Add(_power);

            _chip = Text("", UiFont.BodyBold);
            _chip.style.paddingLeft = 4; _chip.style.paddingRight = 4;
            SetRadius(_chip, 6f);
            _stats.Add(_chip);

            _hp = Text("", UiFont.DisplayBlack);
            _hp.style.color = new Color(1f, 0.60f, 0.54f);              // #ff9a8a
            _hp.style.unityTextOutlineWidth = 0.18f;
            _hp.style.unityTextOutlineColor = Color.black;
            _stats.Add(_hp);
        }

        /// <summary>Bind a card and lay the frame out for a card of this pixel width.</summary>
        public void Bind(CardFaceModel m, ElementPalette palette, float width)
        {
            var sw = palette.Of(m.Element);
            var ec = sw.Color;

            // Every FLOOR and CEILING on a font size in here is in real pixels, and a real pixel
            // is not a fixed fraction of anybody's screen. Left raw, a clamp that exists to stop
            // text going illegible on a small card silently becomes the DOMINANT term on a big
            // display: at 3200x1800 every one of these pinned at its ceiling, so the whole card
            // read at half the relative size it has at 1600x900. Scaled, they mean the same
            // thing - "never smaller than this looks on a 480-tall reference screen".
            float px = HudLayout.Scale;

            _width = width;
            style.width = width;
            style.height = width * Aspect;
            style.borderTopColor = ElementPalette.Mix(ec, Color.black, 0.55f);
            style.borderBottomColor = ElementPalette.Mix(ec, Color.black, 0.55f);
            style.borderLeftColor = ElementPalette.Mix(ec, Color.black, 0.55f);
            style.borderRightColor = ElementPalette.Mix(ec, Color.black, 0.55f);

            // banner
            _banner.style.height = width * BannerH;

            // the art box: full width less a hair of frame, and SQUARE, so a square illustration
            // lands in it whole and fills the card across
            float art = width * ArtSide;
            _artWin.style.width = art;
            _artWin.style.height = art;
            _banner.style.borderBottomColor = ec;

            // The badge straddles the banner's lower edge. Two thirds of it sits in the banner
            // and the last third hangs into the picture, which is where the reference puts it.
            float cost = width * CostSize;
            _costCircle.style.width = cost; _costCircle.style.height = cost;
            _costCircle.style.left = width * 0.022f;
            _costCircle.style.top = width * BannerH - cost * 0.62f;
            SetRadius(_costCircle, cost * 0.5f);
            _costCircle.style.unityBackgroundImageTintColor = ElementPalette.Mix(ec, Color.white, 0.72f);
            _cost.text = m.Cost.ToString();
            _cost.style.fontSize = cost * 0.62f;

            // A card with NO element draws no gem, and must not be charged for one either - that
            // was a seventh of the card's width held empty on every spell and every neutral
            // structure, taken straight off the name.
            bool hasGem = m.Element != Rules.Element.None;

            float gem = width * GemSize;
            _gem.style.width = gem; _gem.style.height = gem;
            _gem.style.left = width * 0.022f + cost + width * 0.012f;
            _gem.style.top = width * BannerH - gem * 0.58f;
            _gem.style.unityBackgroundImageTintColor = sw.Accent;
            _gemGlyph.text = sw.Glyph;
            _gemGlyph.style.fontSize = gem * 0.62f;
            _gem.style.display = hasGem ? DisplayStyle.Flex : DisplayStyle.None;

            // The name column starts CLEAR of the badge and the gem, which are absolute and no
            // longer take part in the banner's row layout.
            _banner.style.paddingLeft = width * 0.022f + cost + (hasGem ? gem : 0f) + width * 0.03f;

            _name.text = m.Name;
            _nameSize = Mathf.Clamp(width * NameSize, 8f * px, 14f * px);
            // Only seed the size before the first fit; after that FitName owns it, or re-binding
            // the same card would reset it to the unfitted size on every rebuild and the memo
            // below would decline to put it back.
            if (_fittedFor == null) _name.style.fontSize = _nameSize;
            FitName();
            _type.text = string.IsNullOrEmpty(m.TypeLine) ? "" : m.TypeLine.ToUpperInvariant();
            _typeSize = Mathf.Clamp(width * TypeSize, 6f * px, 10f * px);
            if (_typeFittedFor == null) _type.style.fontSize = _typeSize;
            FitType();
            _type.style.color = ElementPalette.Mix(ec, Color.black, 0.72f);

            // art
            _art.style.backgroundImage = m.Art != null
                ? Background.FromSprite(m.Art)
                : new StyleBackground(StyleKeyword.None);
            _art.style.backgroundColor = m.Art != null
                ? Color.clear
                : ElementPalette.Mix(sw.Deep, Color.black, 0.55f);       // the placeholder wash (G1)
            var ring = ElementPalette.Mix(ec, Color.black, 0.6f);
            _artWin.style.borderTopColor = ring; _artWin.style.borderBottomColor = ring;
            _artWin.style.borderLeftColor = ring; _artWin.style.borderRightColor = ring;

            // lozenge
            _ribbonText.text = m.Ribbon;
            _ribbonText.style.fontSize = Mathf.Clamp(width * RibbonSize, 6f * px, 12f * px);
            _ribbon.style.unityBackgroundImageTintColor = ec;
            _ribbon.style.marginTop = -(width * 0.055f);
            _ribbon.style.marginLeft = 4;

            // rules
            _rules.text = m.Rules;
            _rulesSize = Mathf.Clamp(width * RulesSize, 7f * px, 13f * px);
            if (_rulesFittedFor == null) _rules.style.fontSize = _rulesSize;
            _rulesBox.style.display = string.IsNullOrEmpty(m.Rules) ? DisplayStyle.None : DisplayStyle.Flex;
            FitRules();

            // stats
            _stats.style.display = m.ShowStats ? DisplayStyle.Flex : DisplayStyle.None;
            _stats.style.height = width * StatsH;
            _power.text = m.Attack > 0 ? Stat.Num(m.Attack) : "";
            _power.style.fontSize = Mathf.Clamp(width * PowerSize, 11f * px, 26f * px);
            _hp.text = Stat.Hp(m.Hp);
            _hp.style.fontSize = Mathf.Clamp(width * HpSize, 9f * px, 18f * px);

            if (m.HasWorkerChip)
            {
                _chip.style.display = DisplayStyle.Flex;
                _chip.text = "⚒" + (m.WorkerChip > 0 ? "+" : "") + m.WorkerChip;
                _chip.style.fontSize = Mathf.Clamp(width * ChipSize, 6f * px, 11f * px);
                bool plus = m.WorkerChip > 0;
                _chip.style.color = plus ? new Color(0.72f, 0.98f, 0.72f) : new Color(0.96f, 0.90f, 0.76f);
                _chip.style.backgroundColor = plus
                    ? new Color(0.08f, 0.22f, 0.10f, 0.95f)
                    : new Color(0.24f, 0.15f, 0.06f, 0.95f);
            }
            else _chip.style.display = DisplayStyle.None;

            BindStateChips(m, width);
        }

        /// <summary>
        /// Shrink the name until it FITS its column, instead of cutting it off at the edge.
        ///
        /// The label is NoWrap with overflow hidden, which on a long name meant the card simply
        /// stopped printing it partway - "Topple the Spi". A card name is an identifier: half of
        /// one is not a smaller version of it, it is a different card. So the size the layout
        /// asked for is treated as a MAXIMUM and the text is scaled down to whatever the column
        /// actually has, with a floor so it never becomes a grey smear.
        ///
        /// The quarter-pixel deadband is load-bearing. This runs from GeometryChangedEvent, and
        /// changing a font size is itself a geometry change - without it, a card whose fit lands
        /// between two sizes would relayout every frame forever.
        /// </summary>
        void FitName() { Fit(_name, _nameSize, 6f, ref _fittedFor); }

        /// <summary>The race/type line under the name, which was never fitted at all - so
        /// "STRUCTURE" under "The Foundry" printed as "STRUCTU".</summary>
        void FitType() { Fit(_type, _typeSize, 5f, ref _typeFittedFor); }

        /// <summary>
        /// Shrink one NoWrap line until it fits the name column, the way a real card does it.
        ///
        /// It used to ask <c>MeasureTextSize</c> how wide the text was, and for these labels that
        /// number is a LIE: caught in the act reporting 101 for a string the same element then
        /// rendered about 128 wide, against a 105-wide column. So the fit concluded "it fits" and
        /// "The Foundry" printed as "THE FOUND". The measuring path does not reproduce the SDF
        /// font asset these labels actually draw through.
        ///
        /// So nothing is measured. The labels are <c>alignSelf: Center</c>, which in this column
        /// makes each size to its OWN CONTENT rather than stretch to the column - so
        /// <c>resolvedStyle.width</c> IS the rendered width, straight from the layout engine that
        /// is about to draw it. Text width is linear in font size, so one step lands it, and the
        /// GeometryChangedEvent the size change causes re-enters here and confirms.
        ///
        /// The memo is what stops that re-entry becoming a loop: keyed on the text, the size the
        /// layout asked for and the column - none of which the fit itself changes.
        /// </summary>
        void Fit(Label label, float wanted, float floor, ref string memo)
        {
            if (_names == null || label == null || wanted <= 0f) return;
            if (string.IsNullOrEmpty(label.text)) return;

            // HandBar binds a face BEFORE it adds it, so there is no panel and no layout yet.
            // Nothing is lost by waiting: the GeometryChangedEvent fires when the card is
            // attached and laid out, which is the first moment the answer can be right anyway.
            if (label.panel == null) return;

            float avail = _names.resolvedStyle.width;
            float actual = label.resolvedStyle.width;
            float cur = label.resolvedStyle.fontSize;
            if (avail <= 1f || actual <= 1f || cur <= 0f) return;

            string key = label.text + "|" + wanted.ToString("F2") + "|" + avail.ToString("F1");
            if (key == memo) return;
            memo = key;

            // 0.98 of the column, not all of it: the last glyph should not sit on the clip edge.
            float target = Mathf.Min(wanted, cur * (avail * 0.98f) / actual);
            target = Mathf.Max(floor, target);

            if (Mathf.Abs(target - cur) > 0.15f) label.style.fontSize = target;
        }

        /// <summary>
        /// Shrink the rules text until the whole of it fits its paper box.
        ///
        /// The box is `overflow: Hidden` at a fixed share of the card, so text that does not fit
        /// is not shortened - it is CUT, mid-sentence, with no sign that anything is missing. A
        /// hand card's three-word brief never noticed; the inspect card's full paragraph, which is
        /// the only place the game explains what a keyword does, would have shown its first line
        /// and silently swallowed the rest. Half an explanation looks exactly like the whole of a
        /// short one, which is the worst way for this to fail.
        ///
        /// Same shape as <see cref="FitName"/>: the laid-out size is a MAXIMUM, the fit is
        /// measured once per (text, size, box) and memoised, and there is a floor so a very long
        /// card does not become a grey smear. Height, not width, because the text wraps.
        /// </summary>
        void FitRules()
        {
            if (_rulesBox == null || _rules == null || _rulesSize <= 0f) return;
            if (string.IsNullOrEmpty(_rules.text) || _rules.panel == null) return;

            float availW = _rulesBox.resolvedStyle.width
                         - _rulesBox.resolvedStyle.paddingLeft - _rulesBox.resolvedStyle.paddingRight;
            float availH = _rulesBox.resolvedStyle.height
                         - _rulesBox.resolvedStyle.paddingTop - _rulesBox.resolvedStyle.paddingBottom;
            float cur = _rules.resolvedStyle.fontSize;
            if (availW <= 1f || availH <= 1f || cur <= 0f) return;

            string key = _rules.text + "|" + _rulesSize.ToString("F2")
                       + "|" + availW.ToString("F1") + "|" + availH.ToString("F1");
            if (key == _rulesFittedFor) return;
            _rulesFittedFor = key;

            // ONE measurement, solved rather than stepped. MeasureTextSize reads the element's
            // RESOLVED font size, and a style set this frame has not resolved yet - so a loop that
            // assigns a smaller size and measures again measures the same number every time and
            // shrinks nothing. (It looked like it worked, because the answer was already clipped.)
            //
            // Wrapped text in a fixed-width box scales as the SQUARE of the font size: at half the
            // size a line holds twice the words and the lines are half as tall. So the size that
            // just fills the box is cur * sqrt(availH / measured), and a little under that.
            var size = _rules.MeasureTextSize(_rules.text, availW, MeasureMode.AtMost,
                                                           0f, MeasureMode.Undefined);
            if (size.y <= 0.01f) return;

            float target = _rulesSize;
            float at = size.y * (_rulesSize / cur) * (_rulesSize / cur);   // height at the wanted size
            if (at > availH) target = Mathf.Max(6f, _rulesSize * Mathf.Sqrt(availH / at) * 0.94f);

            if (Mathf.Abs(target - cur) > 0.05f) _rules.style.fontSize = target;
        }

        /// <summary>Sick / tapped / moved / banked, as small chips over the art (spec 09 §3.7).</summary>
        void BindStateChips(CardFaceModel m, float width)
        {
            _stateChips.Clear();
            float px = HudLayout.Scale;
            float size = Mathf.Clamp(width * 0.13f, 8f * px, 15f * px);

            if (m.Sick) AddChip("💤", size, new Color(0.55f, 0.75f, 1f));
            if (m.Moved) AddChip("⤧", size, new Color(0.85f, 0.85f, 0.95f));
            if (m.Tapped) AddChip("⟳", size, new Color(1f, 0.85f, 0.55f));
            if (m.Bank > 0) AddChip("◆" + m.Bank, size, new Color(0.65f, 0.9f, 1f));
        }

        void AddChip(string text, float size, Color color)
        {
            var chip = Text(text, UiFont.BodyBold);
            chip.style.fontSize = size;
            chip.style.color = color;
            chip.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            chip.style.paddingLeft = 2; chip.style.paddingRight = 2;
            chip.style.marginLeft = 2;
            SetRadius(chip, 4f);
            _stateChips.Add(chip);
        }

        // ── small helpers ──────────────────────────────────────────────────────────────────

        static Label Text(string s, UiFont face)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            l.enableRichText = true;
            var font = ViewAssets.Font(face);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            return l;
        }

        static VisualElement Row()
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.flexDirection = FlexDirection.Row;
            return v;
        }

        static void SetBorder(VisualElement v, float w, Color c)
        {
            v.style.borderTopWidth = w; v.style.borderBottomWidth = w;
            v.style.borderLeftWidth = w; v.style.borderRightWidth = w;
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
        }

        static void SetRadius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r;
            v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r;
        }
    }
}
