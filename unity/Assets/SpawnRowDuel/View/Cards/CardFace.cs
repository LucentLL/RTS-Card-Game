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
        readonly VisualElement _stateChips;

        float _width;

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
            Add(_costCircle);

            _cost = Text("", UiFont.DisplayBlack);
            _cost.style.color = new Color(0.08f, 0.06f, 0.04f);
            _costCircle.Add(_cost);

            _gem = new VisualElement { pickingMode = PickingMode.Ignore };
            _gem.style.position = Position.Absolute;
            _gem.style.backgroundImage = Background.FromTexture2D(CardTextures.Gem);
            _gem.style.alignItems = Align.Center;
            _gem.style.justifyContent = Justify.Center;
            _gem.style.flexShrink = 0;
            Add(_gem);

            _gemGlyph = Text("", UiFont.Cjk);      // kanji-only: the static face is its PRIMARY font
            _gemGlyph.style.color = Color.white;
            _gemGlyph.style.unityTextOutlineWidth = 0.12f;
            _gemGlyph.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
            _gem.Add(_gemGlyph);

            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            names.style.flexGrow = 1;
            names.style.flexShrink = 1;
            names.style.overflow = Overflow.Hidden;
            names.style.marginLeft = 3;
            _banner.Add(names);

            _name = Text("", UiFont.DisplayBlack);
            _name.style.color = new Color(0.10f, 0.078f, 0.04f);        // #1a140a
            _name.style.unityTextAlign = TextAnchor.MiddleCenter;
            _name.style.whiteSpace = WhiteSpace.NoWrap;
            _name.style.overflow = Overflow.Hidden;
            names.Add(_name);

            _type = Text("", UiFont.DisplayRegular);
            _type.style.unityTextAlign = TextAnchor.MiddleCenter;
            _type.style.letterSpacing = 1.4f;
            _type.style.whiteSpace = WhiteSpace.NoWrap;
            _type.style.overflow = Overflow.Hidden;
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

            float gem = width * GemSize;
            _gem.style.width = gem; _gem.style.height = gem;
            _gem.style.left = width * 0.022f + cost + width * 0.012f;
            _gem.style.top = width * BannerH - gem * 0.58f;
            _gem.style.unityBackgroundImageTintColor = sw.Accent;
            _gemGlyph.text = sw.Glyph;
            _gemGlyph.style.fontSize = gem * 0.62f;
            _gem.style.display = m.Element == Rules.Element.None ? DisplayStyle.None : DisplayStyle.Flex;

            // The name column starts CLEAR of the badge and the gem, which are absolute and no
            // longer take part in the banner's row layout.
            _banner.style.paddingLeft = width * 0.022f + cost + gem + width * 0.03f;

            _name.text = m.Name;
            _name.style.fontSize = Mathf.Clamp(width * NameSize, 8f, 14f);
            _type.text = string.IsNullOrEmpty(m.TypeLine) ? "" : m.TypeLine.ToUpperInvariant();
            _type.style.fontSize = Mathf.Clamp(width * TypeSize, 6f, 10f);
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
            _ribbonText.style.fontSize = Mathf.Clamp(width * RibbonSize, 6f, 12f);
            _ribbon.style.unityBackgroundImageTintColor = ec;
            _ribbon.style.marginTop = -(width * 0.055f);
            _ribbon.style.marginLeft = 4;

            // rules
            _rules.text = m.Rules;
            _rules.style.fontSize = Mathf.Clamp(width * RulesSize, 7f, 13f);
            _rulesBox.style.display = string.IsNullOrEmpty(m.Rules) ? DisplayStyle.None : DisplayStyle.Flex;

            // stats
            _stats.style.display = m.ShowStats ? DisplayStyle.Flex : DisplayStyle.None;
            _stats.style.height = width * StatsH;
            _power.text = m.Attack > 0 ? Stat.Num(m.Attack) : "";
            _power.style.fontSize = Mathf.Clamp(width * PowerSize, 11f, 26f);
            _hp.text = Stat.Hp(m.Hp);
            _hp.style.fontSize = Mathf.Clamp(width * HpSize, 9f, 18f);

            if (m.HasWorkerChip)
            {
                _chip.style.display = DisplayStyle.Flex;
                _chip.text = "⚒" + (m.WorkerChip > 0 ? "+" : "") + m.WorkerChip;
                _chip.style.fontSize = Mathf.Clamp(width * ChipSize, 6f, 11f);
                bool plus = m.WorkerChip > 0;
                _chip.style.color = plus ? new Color(0.72f, 0.98f, 0.72f) : new Color(0.96f, 0.90f, 0.76f);
                _chip.style.backgroundColor = plus
                    ? new Color(0.08f, 0.22f, 0.10f, 0.95f)
                    : new Color(0.24f, 0.15f, 0.06f, 0.95f);
            }
            else _chip.style.display = DisplayStyle.None;

            BindStateChips(m, width);
        }

        /// <summary>Sick / tapped / moved / banked, as small chips over the art (spec 09 §3.7).</summary>
        void BindStateChips(CardFaceModel m, float width)
        {
            _stateChips.Clear();
            float size = Mathf.Clamp(width * 0.13f, 8f, 15f);

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
