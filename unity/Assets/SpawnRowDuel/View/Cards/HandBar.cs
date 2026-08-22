using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The player's hand, drawn with real card faces.
    ///
    /// This is the first surface to leave IMGUI behind. It occupies exactly the band MatchHud
    /// already reserves for the hand - the same pixels, published through HudLayout - so the board
    /// camera, the tap-blocking rules and the action row all keep working while the surfaces move
    /// across one at a time. A big-bang UI rewrite in the middle of a milestone is how a playable
    /// build stops being playable.
    ///
    /// Selection stays MatchHud's: tapping a card calls back into it, so the placement flow, the
    /// mode row and the cancel paths have exactly one owner.
    /// </summary>
    public sealed class HandBar : MonoBehaviour
    {
        public const string PanelResource = "HudPanelSettings";

        MatchController _match;
        MatchHud _hud;

        PanelSettings _panel;
        UIDocument _doc;
        VisualElement _row;

        readonly List<CardFace> _faces = new List<CardFace>();
        ElementPalette _palette;
        CardTextService _text;
        CardArtIndex _art;

        string _signature = "";      // what is currently drawn, so a rebuild only happens on change

        void Awake()
        {
            _match = GetComponent<MatchController>();
            _hud = GetComponent<MatchHud>();
        }

        void OnDestroy()
        {
            // _panel is a shared asset - never destroy it
        }

        void LateUpdate()
        {
            if (_match == null || _match.Engine == null) return;
            EnsurePanel();

            var s = _match.Engine.State;
            var hand = s.P(Side.You).Hand;

            float scale = HudLayout.Scale > 0f ? HudLayout.Scale : 1f;
            float bandH = HudLayout.HandBandPx > 0f ? HudLayout.HandBandPx : 82f * scale;
            float bottom = HudLayout.HandBandBottomPx;

            _row.style.bottom = bottom;
            _row.style.height = bandH;

            var sig = Signature(hand, bandH);
            if (sig == _signature) return;
            _signature = sig;

            Rebuild(hand, bandH);
        }

        string Signature(IReadOnlyList<HandCard> hand, float bandH)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Mathf.RoundToInt(bandH)).Append('|').Append(_hud != null ? _hud.SelectedHandIndex : -1);
            for (int i = 0; i < hand.Count; i++) sb.Append('|').Append(hand[i].Id.Value);
            return sb.ToString();
        }

        void Rebuild(IReadOnlyList<HandCard> hand, float bandH)
        {
            _row.Clear();
            _faces.Clear();
            if (hand.Count == 0) return;

            float cardH = Mathf.Max(40f, bandH - 6f);
            float cardW = cardH / CardFace.Aspect;

            // The reference hand overlaps its cards rather than shrinking them to nothing when the
            // hand is large (spec 09 §5.1); a negative margin is that overlap.
            float available = Screen.width - 24f;
            float step = cardW + 6f;
            if (hand.Count * step > available)
                step = Mathf.Max(cardW * 0.42f, available / hand.Count);

            for (int i = 0; i < hand.Count; i++)
            {
                CardFaceModel model;
                if (!CardFaceModel.TryOfCard(hand[i].Id, _match.Engine.Catalog, _text, _art, out model))
                    continue;

                var face = new CardFace();
                face.Bind(model, _palette, cardW);
                face.style.position = Position.Absolute;
                face.style.left = (Screen.width - (step * (hand.Count - 1) + cardW)) * 0.5f + i * step;
                face.style.bottom = 0f;

                bool selected = _hud != null && _hud.SelectedHandIndex == i;
                if (selected)
                {
                    face.style.bottom = cardH * 0.14f;               // the picked card lifts
                    face.style.scale = new Scale(new Vector3(1.06f, 1.06f, 1f));
                }

                int index = i;
                face.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (_hud != null) _hud.SelectHand(index);
                    _signature = "";                                 // force a redraw of the lift
                    evt.StopPropagation();
                });

                _row.Add(face);
                _faces.Add(face);
            }
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

            _row = new VisualElement();
            _row.style.position = Position.Absolute;
            _row.style.left = 0; _row.style.right = 0;
            _row.pickingMode = PickingMode.Ignore;
            root.Add(_row);
        }
    }
}
