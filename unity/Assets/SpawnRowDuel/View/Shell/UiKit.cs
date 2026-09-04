using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// The handful of styled elements every menu screen is made of.
    ///
    /// UI Toolkit rather than IMGUI for one hard reason: the gated 76-glyph font chain lives on
    /// UITK panels, and these screens are full of ♥, ◆, ⚒ and the eight element ideographs. The
    /// same text through OnGUI silently drops every one of them.
    ///
    /// Sizes are authored in the same 480-unit logical space the rest of the HUD uses and scaled
    /// by <see cref="HudLayout.Scale"/>, so a phone and a monitor get the same layout rather than
    /// the same pixels.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color(0.92f, 0.93f, 0.97f);
        public static readonly Color Dim = new Color(0.70f, 0.72f, 0.80f);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.4f);
        public static readonly Color Danger = new Color(0.89f, 0.36f, 0.31f);
        public static readonly Color Panel = new Color(0.055f, 0.06f, 0.085f, 0.96f);
        public static readonly Color PanelSoft = new Color(0.07f, 0.075f, 0.105f, 0.88f);
        public static readonly Color Edge = new Color(0.62f, 0.55f, 0.32f, 0.55f);

        public static float S { get { return HudLayout.Scale; } }

        public static VisualElement Box(VisualElement parent = null)
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Column;
            if (parent != null) parent.Add(v);
            return v;
        }

        public static VisualElement Row(VisualElement parent = null)
        {
            var v = Box(parent);
            v.style.flexDirection = FlexDirection.Row;
            v.style.alignItems = Align.Center;
            return v;
        }

        /// <summary>A glass panel with the gold bracket edge the rest of the game uses.</summary>
        public static VisualElement Glass(VisualElement parent, float pad = 14f)
        {
            var v = Box(parent);
            v.style.backgroundColor = Panel;
            v.style.paddingLeft = pad * S; v.style.paddingRight = pad * S;
            v.style.paddingTop = pad * S; v.style.paddingBottom = pad * S;
            Border(v, Edge, 1f);
            return v;
        }

        public static void Border(VisualElement v, Color c, float w)
        {
            v.style.borderLeftWidth = w; v.style.borderRightWidth = w;
            v.style.borderTopWidth = w; v.style.borderBottomWidth = w;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
        }

        public static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r * S; v.style.borderTopRightRadius = r * S;
            v.style.borderBottomLeftRadius = r * S; v.style.borderBottomRightRadius = r * S;
        }

        public static Label Text(VisualElement parent, string text, float size,
                                 UiFont face = UiFont.BodyRegular, Color? color = null)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            var font = ViewAssets.Font(face);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            l.style.fontSize = size * S;
            l.style.color = color ?? Ink;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0;
            if (parent != null) parent.Add(l);
            return l;
        }

        public static Button Btn(VisualElement parent, string text, System.Action onClick,
                                 float size = 15f, Color? tint = null)
        {
            var b = new Button(() => { if (onClick != null) onClick(); }) { text = text };
            var font = ViewAssets.Font(UiFont.DisplayBold);
            if (font != null) b.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            b.style.fontSize = size * S;
            b.style.color = tint ?? Ink;
            b.style.backgroundColor = new Color(0.13f, 0.14f, 0.19f, 0.95f);
            b.style.paddingLeft = 14f * S; b.style.paddingRight = 14f * S;
            b.style.paddingTop = 7f * S; b.style.paddingBottom = 7f * S;
            b.style.marginLeft = 0; b.style.marginRight = 0;
            b.style.marginTop = 3f * S; b.style.marginBottom = 3f * S;
            Border(b, new Color(0.42f, 0.40f, 0.30f, 0.7f), 1f);
            Radius(b, 5f);
            if (parent != null) parent.Add(b);
            return b;
        }

        /// <summary>A gem carrying an element's ideograph - the badge the whole game identifies
        /// elements by. The kanji face is used DIRECTLY: Unity 6 refuses a static font asset as a
        /// fallback, so a kanji-only label has to name it as its primary.</summary>
        public static VisualElement Badge(VisualElement parent, string glyph, Color color, float size)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.width = size * S; v.style.height = size * S;
            v.style.alignItems = Align.Center;
            v.style.justifyContent = Justify.Center;
            v.style.backgroundColor = new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.9f);
            Border(v, color, 1.5f);
            Radius(v, size * 0.28f);
            var l = Text(v, glyph, size * 0.60f, UiFont.Cjk, color);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (parent != null) parent.Add(v);
            return v;
        }

        /// <summary>
        /// A text field that belongs to this HUD rather than to the editor. The stock one is a
        /// white box with black text, which is fine in an inspector and unreadable everywhere
        /// else - and the inner input element carries its own colours, so both have to be told.
        /// </summary>
        public static TextField Field(VisualElement parent, string value, float width,
                                      System.Action<string> onChange, string placeholder = null)
        {
            var t = new TextField { value = value };
            var font = ViewAssets.Font(UiFont.BodyRegular);
            if (font != null) t.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            t.style.fontSize = 13f * S;
            t.style.width = width * S;
            t.style.marginLeft = 0; t.style.marginRight = 0;
            t.style.color = Ink;

            var input = t.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.10f, 0.11f, 0.15f, 1f);
                input.style.color = Ink;
                Border(input, new Color(0.42f, 0.40f, 0.30f, 0.7f), 1f);
                Radius(input, 4f);
                input.style.paddingLeft = 6f * S; input.style.paddingRight = 6f * S;
                input.style.paddingTop = 4f * S; input.style.paddingBottom = 4f * S;
            }
            if (!string.IsNullOrEmpty(placeholder)) t.textEdition.placeholder = placeholder;
            if (onChange != null) t.RegisterValueChangedCallback(e => onChange(e.newValue));

            // A FOCUS RING. The rest state and the focused state were the same box, so the only
            // way to find out which field the keyboard was talking to was to type into it.
            if (input != null)
            {
                var rest = new Color(0.10f, 0.11f, 0.15f, 1f);
                var lit = new Color(0.13f, 0.14f, 0.19f, 1f);
                var restEdge = new Color(0.42f, 0.40f, 0.30f, 0.7f);
                t.RegisterCallback<FocusInEvent>(delegate
                {
                    Border(input, Gold, 1f);
                    input.style.backgroundColor = lit;
                });
                t.RegisterCallback<FocusOutEvent>(delegate
                {
                    Border(input, restEdge, 1f);
                    input.style.backgroundColor = rest;
                });
            }

            // ...and on a phone, a browser input of its own, because the player cannot open a
            // keyboard and a field nobody can type into is not a field.
            WebTextEntry.Attach(t, input, onChange, placeholder, 13f * S);

            if (parent != null) parent.Add(t);
            return t;
        }

        /// <summary>A full-screen scrim that eats taps meant for whatever is behind it.</summary>
        public static VisualElement Scrim(VisualElement parent, System.Action onOutside = null)
        {
            var v = new VisualElement();
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0; v.style.top = 0; v.style.bottom = 0;
            v.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
            v.style.alignItems = Align.Center;
            v.style.justifyContent = Justify.Center;
            if (onOutside != null)
                v.RegisterCallback<PointerDownEvent>(e => { if (e.target == v) onOutside(); });
            if (parent != null) parent.Add(v);
            return v;
        }

        public static void Fill(VisualElement v)
        {
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0; v.style.top = 0; v.style.bottom = 0;
        }
    }
}
