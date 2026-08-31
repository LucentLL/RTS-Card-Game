using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The player's hand, drawn with real card faces, in the reference build's two states
    /// (spec 09 §5.1).
    ///
    /// AT REST the cards hang below the screen edge and only their name banners peek - which is
    /// what makes the hand affordable: the band the board gives up is one banner tall, not one card
    /// tall. The PICKED card rises to full height and brings a large inspect card with it, because
    /// a hand-sized ability box is unreadable on a phone and "what does this card do" is the
    /// question the interface exists to answer.
    ///
    /// Selection stays MatchHud's: tapping a card calls back into it, so the placement flow, the
    /// mode row and the cancel paths have exactly one owner.
    ///
    /// Nothing else may paint over this band. IMGUI draws after every UI Toolkit panel, so an
    /// opaque HUD rectangle there does not sit behind the cards - it sits on top of them, which is
    /// what "the cards are too dark" turned out to be.
    /// </summary>
    public sealed class HandBar : MonoBehaviour
    {
        public const string PanelResource = "HudPanelSettings";

        /// <summary>How much taller a card is than the strip it peeks out of.</summary>
        public const float CardToPeek = 2.9f;

        MatchController _match;
        MatchHud _hud;

        PanelSettings _panel;
        UIDocument _doc;
        VisualElement _row;
        VisualElement _lift;
        CardFace _inspect;

        /// <summary>
        /// Two shared layers on this panel, so the board's own surfaces do not each have to own a
        /// UIDocument. A second document is a second panel with its own sorting order to keep in
        /// step, and the walls are already here for that reason.
        ///
        /// BOARD is under the hand - unit vitals and damage numbers belong behind a card you are
        /// holding up. OVERLAY is over everything, which is what a battle cut-in is for.
        /// </summary>
        public VisualElement BoardLayer { get; private set; }
        public VisualElement OverlayLayer { get; private set; }

        /// <summary>The panel is built lazily; nothing may draw into it before this is true.</summary>
        public bool PanelReady { get { return _doc != null && BoardLayer != null; } }

        /// <summary>The two castle walls, their vitals, and the foe's hand - built into this
        /// panel, under the cards, because the cards are held in FRONT of your wall.</summary>
        readonly WallBands _walls = new WallBands();

        ElementPalette _palette;
        CardTextService _text;
        CardArtIndex _art;

        string _signature = "";      // what is currently drawn, so a rebuild only happens on change

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _hud = GetComponent<MatchHud>();
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null) return;
            EnsurePanel();
            if (_doc == null) return;

            // Compute rather than read: LateUpdate runs before OnGUI, and on the first frames a
            // stale zero here put the cards under the bottom edge of the screen.
            HudLayout.Recompute();
            float peek = HudLayout.HandBandPx;

            _row.style.height = peek;
            _lift.style.height = peek;

            // The walls are drawn HERE rather than in IMGUI: IMGUI paints after every UI Toolkit
            // panel, so a band painted there lands on top of the cards instead of behind them -
            // which is what the dark bar through the hand turned out to be.
            //
            // And they lay out BEFORE the hand now, because the hand rides on them.
            _walls.Layout(_match.Engine.State, _palette, PanelWidth());

            // ON the screen edge - or on the WALL, when the wall is up. A card stopped short of
            // the edge with a lip of stone underneath looks stuck to it; a held hand stands proud
            // of the wall it is held at, which is what the peek being taller than the rail is for.
            // The hand used to be pinned to the edge, so when a tower window slid up the stonework
            // rose straight through the cards. One way only: the wall opens because it is being
            // looked at, never because a card was picked.
            _row.style.bottom = _walls.YouLift;
            _lift.style.bottom = _walls.YouLift;

            var hand = _match.Engine.State.P(Seat.Local).Hand;
            var sig = Signature(hand, peek);
            if (sig == _signature) return;
            _signature = sig;

            Rebuild(hand, peek);
        }

        /// <summary>
        /// The PANEL's width, not Screen.width: they differ whenever the panel renders into a
        /// texture, and the capture harness does exactly that.
        /// </summary>
        float PanelWidth()
        {
            if (_doc != null && _doc.rootVisualElement != null)
            {
                float w = _doc.rootVisualElement.resolvedStyle.width;
                if (w > 1f) return w;
            }
            return Screen.width;
        }

        string Signature(IReadOnlyList<HandCard> hand, float peek)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Mathf.RoundToInt(peek)).Append('/').Append(Screen.width)
              .Append('|').Append(_hud != null ? _hud.SelectedHandIndex : -1);
            for (int i = 0; i < hand.Count; i++) sb.Append('|').Append(hand[i].Id.Value);
            return sb.ToString();
        }

        void Rebuild(IReadOnlyList<HandCard> hand, float peek)
        {
            _row.Clear();
            _lift.Clear();
            _inspect.style.display = DisplayStyle.None;
            if (hand.Count == 0) return;

            float cardH = peek * CardToPeek;
            float cardW = cardH / CardFace.Aspect;

            float panelW = PanelWidth();

            // The hand belongs to the wall's MIDDLE SPAN (spec 09 §4.2): the towers at either end
            // are where the vitals and the piles are set, and a hand laid across the full width
            // would be holding its outer cards over them.
            float spanL = panelW * WallBands.TowerSpan;
            float spanR = panelW * (1f - WallBands.TowerSpan);
            float span = spanR - spanL;

            // The reference hand OVERLAPS its cards rather than shrinking them to nothing when the
            // hand is large; there is no hand-size cap in the rules, so the layout must cope.
            float step = cardW + 6f;
            if (hand.Count * step > span - 16f)
                step = Mathf.Max(cardW * 0.34f, (span - 16f) / hand.Count);

            float x0 = spanL + (span - (step * (hand.Count - 1) + cardW)) * 0.5f;
            int selected = _hud != null ? _hud.SelectedHandIndex : -1;

            for (int i = 0; i < hand.Count; i++)
            {
                CardFaceModel model;
                if (!CardFaceModel.TryOfCard(hand[i].Id, _match.Engine.Catalog, _text, _art, out model))
                    continue;

                var face = new CardFace();
                face.Bind(model, _palette, cardW);
                face.style.position = Position.Absolute;
                face.style.left = x0 + i * step;

                bool picked = i == selected;
                // at rest the card hangs below the strip, showing its banner only
                face.style.bottom = picked ? 0f : -(cardH - peek);

                int index = i;
                face.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (_hud != null) _hud.SelectHand(index);
                    _signature = "";
                    evt.StopPropagation();
                });

                // A resting card is CLIPPED to the strip, so it cannot spill over the action row
                // below it; the picked one goes in an unclipped overlay so it can rise clear.
                if (picked) _lift.Add(face); else _row.Add(face);
            }

            if (selected >= 0 && selected < hand.Count) ShowInspect(hand[selected].Id, cardH);
        }

        /// <summary>
        /// The big card - `.hc.big` in the reference (spec 09 §6.2): the same frame at a size where
        /// its rules text can be read, carrying the FULL ability text rather than the hand card's
        /// three-line brief.
        /// </summary>
        void ShowInspect(CardId id, float handCardH)
        {
            CardFaceModel model;
            if (!CardFaceModel.TryOfCard(id, _match.Engine.Catalog, _text, _art, out model)) return;

            var full = _text.Full(id);
            if (!string.IsNullOrEmpty(full)) model.Rules = full;

            // as tall as the board band allows, never taller than the band itself
            float room = Screen.height - HudLayout.TopPx - HudLayout.BottomPx - 24f;
            float h = Mathf.Min(Mathf.Clamp(Screen.height * 0.52f, handCardH, 460f),
                                Mathf.Max(160f, room));
            float w = h / CardFace.Aspect;

            _inspect.Bind(model, _palette, w);
            _inspect.style.display = DisplayStyle.Flex;
            // TOP-LEFT, under the status bar - Master Duel's corner for "what am I looking at".
            // It used to hang off the right edge just above the hand, to keep clear of the picked
            // card rising out of the strip; up here it is clear of that card by the whole board.
            _inspect.style.right = StyleKeyword.Auto;
            _inspect.style.bottom = StyleKeyword.Auto;
            _inspect.style.left = 12f;
            _inspect.style.top = HudLayout.TopPx + 12f;
        }

        void EnsurePanel()
        {
            if (_doc != null) return;

            _palette = new ElementPalette(_match.Engine.Catalog);
            _text = new CardTextService(_match.Engine.Catalog);
            _art = new CardArtIndex(_match.Database);

            // A Resources ASSET, not CreateInstance: a runtime-built PanelSettings finds its UI
            // shaders by name, and the WebGL stripper deletes shaders nothing serialized points at.
            _panel = Resources.Load<PanelSettings>(PanelResource);
            if (_panel == null)
            {
                Debug.LogError("HudPanelSettings is missing - run tools/regen-fonts.sh");
                enabled = false;
                return;
            }

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = _panel;

            var root = _doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            HudLayout.Recompute();
            _walls.Attach(root);              // first, so everything below is drawn on the stone

            BoardLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            BoardLayer.style.position = Position.Absolute;
            BoardLayer.style.left = 0; BoardLayer.style.right = 0;
            BoardLayer.style.top = 0; BoardLayer.style.bottom = 0;
            root.Add(BoardLayer);

            _inspect = new CardFace { pickingMode = PickingMode.Ignore };
            _inspect.style.position = Position.Absolute;
            _inspect.style.display = DisplayStyle.None;
            root.Add(_inspect);

            _row = new VisualElement();
            _row.style.position = Position.Absolute;
            _row.style.left = 0; _row.style.right = 0;
            _row.style.overflow = Overflow.Hidden;       // resting cards show a banner and no more
            _row.pickingMode = PickingMode.Ignore;
            root.Add(_row);

            _lift = new VisualElement();
            _lift.style.position = Position.Absolute;
            _lift.style.left = 0; _lift.style.right = 0;
            _lift.style.overflow = Overflow.Visible;     // the picked card rises OUT of the strip
            _lift.pickingMode = PickingMode.Ignore;
            root.Add(_lift);

            OverlayLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            OverlayLayer.style.position = Position.Absolute;
            OverlayLayer.style.left = 0; OverlayLayer.style.right = 0;
            OverlayLayer.style.top = 0; OverlayLayer.style.bottom = 0;
            root.Add(OverlayLayer);                      // LAST: a cut-in covers the hand too
        }

        /// <summary>
        /// World point → this panel's own coordinates, top-left origin.
        ///
        /// Screen and panel pixels are the same thing while the panel renders to the screen, and
        /// are NOT while it renders into a texture - which is exactly what the screenshot harness
        /// does, so anything that skipped this scaling would be right in the game and wrong in
        /// every probe shot.
        /// </summary>
        public bool TryProject(Camera cam, Vector3 world, out Vector2 panelPos)
        {
            panelPos = default(Vector2);
            if (cam == null || _doc == null || _doc.rootVisualElement == null) return false;

            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;                // behind the camera

            var root = _doc.rootVisualElement;
            float pw = root.resolvedStyle.width, ph = root.resolvedStyle.height;
            if (pw <= 1f) pw = Screen.width;
            if (ph <= 1f) ph = Screen.height;

            panelPos = new Vector2(sp.x / Mathf.Max(1f, Screen.width) * pw,
                                   (1f - sp.y / Mathf.Max(1f, Screen.height)) * ph);
            return true;
        }

        public Vector2 PanelSize()
        {
            if (_doc == null || _doc.rootVisualElement == null)
                return new Vector2(Screen.width, Screen.height);
            var root = _doc.rootVisualElement;
            float pw = root.resolvedStyle.width, ph = root.resolvedStyle.height;
            return new Vector2(pw > 1f ? pw : Screen.width, ph > 1f ? ph : Screen.height);
        }
    }
}
