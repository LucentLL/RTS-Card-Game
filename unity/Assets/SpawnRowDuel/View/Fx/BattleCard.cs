using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Fx
{
    /// <summary>
    /// One card in the battle cut-in - `.bvcard` from the reference stylesheet
    /// (05_overlays_screens.css): name banner, art, type lozenge, and a stat bar with the lead
    /// figure on the left and hit points on the right.
    ///
    /// Deliberately NOT a <see cref="CardFace"/>. The full frame is a document - cost circle,
    /// element gem, ability box, worker chip - and a cut-in is a beat and a half long. What has to
    /// carry in that time is which two things are fighting and what it cost them, so the frame is
    /// stripped to a portrait and two numbers, and the numbers are the ones that CHANGE: the hp
    /// counts down and the blow is stamped over the art.
    /// </summary>
    public sealed class BattleCard : VisualElement
    {
        public struct Model
        {
            public string Name;
            public string Lead;                 // "⚔300" for a creature, "◆+2" / "▣" for anything else
            public int Hp, HpAfter, MaxHp, Damage;
            public bool Died, Foe, Wall, Structure;
            public Element Element;
            public Sprite Art;

            /// <summary>
            /// A card that is PLAYED rather than fought: a spell or a trap. It has no health, so the
            /// hp figure and the bar under it are hidden rather than drawn as ♥0 over an empty red
            /// bar - which is what a card with no statline looks like if you let the creature layout
            /// have it, and reads as "this thing is dead" instead of "this thing has no hp".
            /// </summary>
            public bool Played;

            /// <summary>Overrides the lozenge when set - "SPELL" / "TRAP".</summary>
            public string TypeName;
        }

        readonly VisualElement _art, _bar, _barFill, _stamp, _stats;
        readonly Label _name, _type, _lead, _hp, _damage, _stampText;

        Model _model;
        float _width;

        public BattleCard()
        {
            pickingMode = PickingMode.Ignore;
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.082f, 0.071f, 0.114f);
            Border(this, 2f, new Color(0.29f, 0.25f, 0.376f));
            Radius(this, 9f);

            _name = CombatTheatre.NewLabel(UiFont.DisplayBold, 11f);
            _name.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _name.style.color = new Color(0.906f, 0.933f, 0.984f);
            _name.style.whiteSpace = WhiteSpace.NoWrap;
            _name.style.overflow = Overflow.Hidden;
            Add(_name);

            _art = new VisualElement { pickingMode = PickingMode.Ignore };
            _art.style.flexGrow = 1f;
            _art.style.overflow = Overflow.Hidden;
            _art.style.alignItems = Align.Center;
            _art.style.justifyContent = Justify.Center;
            _art.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _art.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            Add(_art);

            // the blow, stamped over the portrait
            _damage = CombatTheatre.NewLabel(UiFont.DisplayBlack, 26f);
            _damage.style.position = Position.Absolute;
            _damage.style.left = 0; _damage.style.right = 0;
            _damage.style.color = new Color(1f, 0.35f, 0.28f);
            _damage.style.display = DisplayStyle.None;
            _art.Add(_damage);

            _stamp = new VisualElement { pickingMode = PickingMode.Ignore };
            _stamp.style.position = Position.Absolute;
            _stamp.style.left = 0; _stamp.style.right = 0;
            _stamp.style.backgroundColor = new Color(0.55f, 0.06f, 0.06f, 0.86f);
            _stamp.style.display = DisplayStyle.None;
            _art.Add(_stamp);

            _stampText = CombatTheatre.NewLabel(UiFont.DisplayBlack, 11f);
            _stampText.text = "DESTROYED";
            _stampText.style.color = Color.white;
            _stampText.style.letterSpacing = 1.4f;
            _stamp.Add(_stampText);

            _type = CombatTheatre.NewLabel(UiFont.BodyRegular, 8f);
            _type.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _type.style.color = new Color(0.804f, 0.737f, 0.949f);
            _type.style.letterSpacing = 1f;
            Add(_type);

            var stats = new VisualElement { pickingMode = PickingMode.Ignore };
            stats.style.flexDirection = FlexDirection.Row;
            stats.style.justifyContent = Justify.SpaceBetween;
            stats.style.alignItems = Align.Center;
            stats.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
            Add(stats);
            _stats = stats;

            _lead = CombatTheatre.NewLabel(UiFont.DisplayBlack, 13f);
            _lead.style.color = new Color(1f, 0.60f, 0.42f);
            stats.Add(_lead);

            _hp = CombatTheatre.NewLabel(UiFont.DisplayBlack, 13f);
            _hp.style.color = new Color(0.54f, 1f, 0.69f);
            stats.Add(_hp);

            // the health bar under the stat line: "how much of it is left" is a shape, and a
            // shape reads in a beat where a pair of numbers does not
            _bar = new VisualElement { pickingMode = PickingMode.Ignore };
            _bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
            Add(_bar);

            _barFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _barFill.style.height = Length.Percent(100f);
            _barFill.style.backgroundColor = new Color(0.54f, 1f, 0.69f);
            _bar.Add(_barFill);
        }

        public void Bind(Model m, ElementPalette palette, float width)
        {
            _model = m;
            _width = width;

            var sw = palette.Of(m.Element);
            style.width = width;
            style.height = width * CardFace.Aspect;
            Border(this, Mathf.Max(2f, width * 0.012f),
                   ElementPalette.Mix(sw.Color, Color.black, 0.45f));
            Radius(this, Mathf.Max(9f, width * 0.05f));

            // Every size below is a FRACTION of the card, with a floor and no ceiling. They used
            // to be clamped at 14 / 18 / 34 px, which is invisible while the card is 132 px wide
            // and absurd the moment it is 520: a poster-sized portrait with eight-point type on
            // it. A cut-in is one picture, so it has one scale.
            _name.style.paddingTop = width * 0.018f;
            _name.style.paddingBottom = width * 0.018f;
            _name.text = m.Name;
            _name.style.fontSize = Mathf.Max(8f, width * 0.105f);

            _art.style.backgroundImage = m.Art != null
                ? Background.FromSprite(m.Art)
                : new StyleBackground(StyleKeyword.None);
            _art.style.backgroundColor = m.Art != null
                ? Color.clear
                : ElementPalette.Mix(sw.Deep, Color.black, 0.45f);

            _type.text = !string.IsNullOrEmpty(m.TypeName) ? m.TypeName
                       : m.Wall ? "CASTLE WALL" : m.Structure ? "STRUCTURE" : "CREATURE";
            _type.style.fontSize = Mathf.Max(6f, width * 0.062f);
            _type.style.color = ElementPalette.Mix(sw.Accent, Color.white, 0.5f);

            // A played card keeps the stat bar - its cost lives there - but loses the half of it
            // that measures damage it cannot take.
            _hp.style.display = m.Played ? DisplayStyle.None : DisplayStyle.Flex;
            _bar.style.display = m.Played ? DisplayStyle.None : DisplayStyle.Flex;

            _lead.text = m.Lead;
            _lead.style.fontSize = Mathf.Max(9f, width * 0.135f);
            _hp.style.fontSize = Mathf.Max(9f, width * 0.135f);

            _stats.style.paddingLeft = width * 0.04f;
            _stats.style.paddingRight = width * 0.04f;
            _bar.style.height = Mathf.Max(3f, width * 0.035f);
            _damage.style.fontSize = Mathf.Max(14f, width * 0.26f);
            _damage.style.top = width * 0.32f;
            _stamp.style.top = width * 0.62f;
            _stampText.style.fontSize = Mathf.Max(8f, width * 0.105f);
            _stamp.style.paddingTop = width * 0.012f; _stamp.style.paddingBottom = width * 0.012f;

            ShowResult(false);
        }

        /// <summary>
        /// Before the clash the card reads as it WAS; after it, as it ended. Two states rather than
        /// one, because a cut-in that opens on the aftermath never shows the trade being made.
        /// </summary>
        public void ShowResult(bool after)
        {
            // A spell has no hp to count down and takes no blow: the only thing its card does after
            // the clash is stay on screen naming what was cast.
            if (_model.Played)
            {
                _damage.style.display = DisplayStyle.None;
                _stamp.style.display = DisplayStyle.None;
                return;
            }

            // DESTROYED beats the arithmetic. A raze deals no damage - it removes the card - so
            // HpAfter is still full, and a full green bar under the word DESTROYED reads as a
            // contradiction rather than as a card that was deleted without being hurt. Anything
            // that died is drawn at zero; in a normal fight the blow already took it there.
            int hp = after ? (_model.Died ? 0 : _model.HpAfter) : _model.Hp;
            _hp.text = Stat.Hp(hp);

            float frac = Mathf.Clamp01(hp / (float)Mathf.Max(1, _model.MaxHp));
            _barFill.style.width = Length.Percent(frac * 100f);
            _barFill.style.backgroundColor = frac > 0.5f ? new Color(0.54f, 1f, 0.69f)
                                           : frac > 0.2f ? new Color(1f, 0.85f, 0.4f)
                                                         : new Color(1f, 0.42f, 0.34f);

            bool hurt = after && _model.Damage > 0;
            _damage.text = hurt ? "-" + Stat.Show(_model.Damage) : "";
            _damage.style.display = hurt ? DisplayStyle.Flex : DisplayStyle.None;
            _hp.style.color = hurt ? new Color(1f, 0.55f, 0.45f) : new Color(0.54f, 1f, 0.69f);

            _stamp.style.display = after && _model.Died ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Border(VisualElement v, float w, Color c)
        {
            v.style.borderTopWidth = w; v.style.borderBottomWidth = w;
            v.style.borderLeftWidth = w; v.style.borderRightWidth = w;
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
        }

        static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r;
            v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r;
        }
    }
}
