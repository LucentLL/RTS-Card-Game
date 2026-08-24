using System.Collections.Generic;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Campaign;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// The world map screen: the HUD around the globe, the markers pinned to it, and the three
    /// overlays that sit on top of it (attack confirm, turn log, toast).
    ///
    /// The markers are UI, not world geometry. They carry garrison numbers and element ideographs,
    /// and text on a sphere is either a billboard that fights the depth buffer or a font atlas in
    /// world space; projecting an anchor to the screen each frame is cheaper than both and keeps
    /// the gated glyph chain, which is the only thing that can draw 炎 at all.
    /// </summary>
    public sealed class WorldMapUi
    {
        readonly GameShell _shell;
        readonly VisualElement _root;
        readonly ICardCatalog _cat;

        VisualElement _markers, _overlay;
        Label _toast;
        float _toastUntil;

        readonly Dictionary<int, Marker> _pins = new Dictionary<int, Marker>();

        sealed class Marker
        {
            public VisualElement Root;
            public Label Main, Sub;
        }

        public WorldMapUi(GameShell shell, VisualElement root, ICardCatalog cat)
        {
            _shell = shell; _root = root; _cat = cat;
        }

        CampaignService Camp { get { return _shell.Campaign; } }
        CampaignState S { get { return _shell.Campaign.State; } }

        public void Build()
        {
            _root.Clear();
            _pins.Clear();

            _markers = new VisualElement { pickingMode = PickingMode.Ignore };
            UiKit.Fill(_markers);
            _root.Add(_markers);

            BuildHud();
            BuildLegend();

            _toast = UiKit.Text(_root, "", 12.5f, UiFont.BodyRegular, UiKit.Ink);
            _toast.style.position = Position.Absolute;
            _toast.style.left = 0; _toast.style.right = 0;
            _toast.style.bottom = 74f * UiKit.S;
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toast.style.display = DisplayStyle.None;

            if (_shell.Globe != null) _shell.Globe.OnTerritoryPicked = OnPick;
        }

        void BuildHud()
        {
            var bar = UiKit.Row(_root);
            bar.style.position = Position.Absolute;
            bar.style.left = 10f * UiKit.S; bar.style.right = 10f * UiKit.S;
            bar.style.top = 8f * UiKit.S;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.alignItems = Align.FlexStart;

            var left = UiKit.Glass(bar, 9f);
            UiKit.Radius(left, 6f);
            var faction = UiKit.Row(left);
            var def = ElementOf(S.Faction);
            var colour = GlobeView.ElementColour(S.Faction);
            UiKit.Badge(faction, def != null ? def.Glyph : "", colour, 22f);
            UiKit.Text(faction, def != null ? def.Name : CampaignRules.Name(S.Faction), 16f,
                       UiFont.DisplayBlack, colour).style.marginLeft = 7f * UiKit.S;

            var stats = UiKit.Row(left);
            stats.style.marginTop = 4f * UiKit.S;
            Stat(stats, "Turn", S.Turn.ToString());
            Stat(stats, "Lands", CampaignRules.PlayerTerritoryCount(S) + "/" + S.Map.Territories.Length);
            Stat(stats, "Capitals", CampaignRules.CapitalsHeld(S) + "/8");

            var allies = UiKit.Row(left);
            allies.style.marginTop = 4f * UiKit.S;
            UiKit.Text(allies, "Allies", 10.5f, UiFont.BodyRegular, UiKit.Dim)
                .style.marginRight = 6f * UiKit.S;
            bool any = false;
            foreach (var el in CampaignRules.Majors)
            {
                if (!S.Allies.Contains(el)) continue;
                var d = ElementOf(el);
                UiKit.Badge(allies, d != null ? d.Glyph : "", GlobeView.ElementColour(el), 17f)
                    .style.marginRight = 3f * UiKit.S;
                any = true;
            }
            if (!any) UiKit.Text(allies, "none yet", 10.5f, UiFont.BodyItalic, UiKit.Dim);

            var right = UiKit.Row(bar);
            UiKit.Btn(right, "End Turn ▶", EndTurn, 14f, new Color(0.60f, 0.82f, 0.50f))
                .style.marginRight = 6f * UiKit.S;
            UiKit.Btn(right, "New", ConfirmReset, 12f, UiKit.Dim).style.marginRight = 6f * UiKit.S;
            UiKit.Btn(right, "Menu", () => _shell.Show(ShellScreen.MainMenu), 12f, UiKit.Dim);
        }

        void Stat(VisualElement parent, string label, string value)
        {
            var v = UiKit.Row(parent);
            v.style.marginRight = 10f * UiKit.S;
            UiKit.Text(v, label, 10.5f, UiFont.BodyRegular, UiKit.Dim)
                .style.marginRight = 4f * UiKit.S;
            UiKit.Text(v, value, 12.5f, UiFont.DisplayBold, UiKit.Ink);
        }

        void BuildLegend()
        {
            var legend = UiKit.Row(_root);
            legend.style.position = Position.Absolute;
            legend.style.left = 0; legend.style.right = 0;
            legend.style.bottom = 8f * UiKit.S;
            legend.style.justifyContent = Justify.Center;
            legend.style.flexWrap = Wrap.Wrap;

            UiKit.Text(legend, "drag the globe · tap a territory", 11f, UiFont.BodyItalic, UiKit.Dim)
                .style.marginRight = 12f * UiKit.S;

            foreach (var el in CampaignRules.Majors)
            {
                var d = ElementOf(el);
                var chip = UiKit.Row(legend);
                chip.style.marginRight = 8f * UiKit.S;
                UiKit.Badge(chip, d != null ? d.Glyph : "", GlobeView.ElementColour(el), 15f);
                UiKit.Text(chip, d != null ? d.Name : CampaignRules.Name(el), 10.5f,
                           UiFont.BodyRegular, GlobeView.ElementColour(el))
                    .style.marginLeft = 3f * UiKit.S;
            }
        }

        ElementDef ElementOf(Element el)
        {
            if (_cat == null) return null;
            foreach (var d in _cat.Elements) if (d.El == el) return d;
            return null;
        }

        // ── per frame ───────────────────────────────────────────────────────────────────

        public void Tick()
        {
            bool overlayOpen = _overlay != null;
            if (_shell.Globe != null) _shell.Globe.Tick(!overlayOpen && !PointerOverHud());

            UpdateMarkers();

            if (_toast != null && _toast.style.display == DisplayStyle.Flex
                && Time.unscaledTime > _toastUntil)
                _toast.style.display = DisplayStyle.None;
        }

        /// <summary>The HUD sits over the globe; a tap on it must not also spin the world.</summary>
        bool PointerOverHud()
        {
            var p = (Vector2)Input.mousePosition;
            float y = Screen.height - p.y;
            return y < 74f * UiKit.S || y > Screen.height - 30f * UiKit.S;
        }

        void UpdateMarkers()
        {
            var globe = _shell.Globe;
            if (globe == null || globe.Cam == null) return;

            foreach (var t in S.Map.Territories)
            {
                Marker m;
                if (!_pins.TryGetValue(t.Id, out m)) { m = NewMarker(); _pins[t.Id] = m; }

                if (!globe.AnchorFacing(t.Id)) { m.Root.style.display = DisplayStyle.None; continue; }

                var world = globe.AnchorWorld(t.Id);
                var sp = globe.Cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) { m.Root.style.display = DisplayStyle.None; continue; }

                var cap = CampaignRules.CapitalDesignation(S.Map, t.Id);
                bool isCapital = cap != Element.None;
                bool mine = t.Owner == S.Faction;
                bool attackable = CampaignRules.IsAttackable(S.Map, S.Faction, t.Id);

                float size = (isCapital ? 30f : 22f) * UiKit.S;
                m.Root.style.display = DisplayStyle.Flex;
                m.Root.style.width = size; m.Root.style.height = size;
                m.Root.style.left = sp.x - size * 0.5f;
                m.Root.style.top = (Screen.height - sp.y) - size * 0.5f;

                Color ring;
                float ringW;
                if (attackable)
                {
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.2f);
                    ring = new Color(0.851f, 0.714f, 0.290f, pulse);
                    ringW = 2.6f;
                }
                else if (mine) { ring = Color.white; ringW = 2f; }
                else { ring = new Color(1f, 1f, 1f, 0.30f); ringW = 1.4f; }
                UiKit.Border(m.Root, ring, ringW);
                UiKit.Radius(m.Root, size * 0.5f / UiKit.S);

                if (isCapital)
                {
                    var d = ElementOf(cap);
                    m.Main.text = d != null ? d.Glyph : "◆";
                    m.Main.style.fontSize = 13f * UiKit.S;
                    m.Main.style.color = GlobeView.ElementColour(cap);
                    m.Sub.style.display = DisplayStyle.Flex;
                    m.Sub.text = t.Garrison.ToString();
                }
                else
                {
                    m.Main.text = t.Garrison.ToString();
                    m.Main.style.fontSize = 12f * UiKit.S;
                    m.Main.style.color = UiKit.Ink;
                    m.Sub.style.display = DisplayStyle.None;
                }
            }
        }

        Marker NewMarker()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0.031f, 0.024f, 0.055f, 0.78f);
            _markers.Add(root);

            var main = UiKit.Text(root, "", 12f, UiFont.Cjk, UiKit.Ink);
            main.style.unityTextAlign = TextAnchor.MiddleCenter;

            var sub = UiKit.Text(root, "", 9.5f, UiFont.DisplayBold, UiKit.Dim);
            sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            sub.style.display = DisplayStyle.None;

            return new Marker { Root = root, Main = main, Sub = sub };
        }

        // ── interaction ─────────────────────────────────────────────────────────────────

        void OnPick(int territoryId)
        {
            if (_overlay != null) return;
            var t = S.Map.Of(territoryId);
            if (t == null) return;

            if (t.Owner == S.Faction)
            {
                bool own = CampaignRules.CapitalDesignation(S.Map, territoryId) == S.Faction;
                Toast("Your territory — garrison " + t.Garrison + (own ? ".  Your capital." : "."));
                return;
            }

            if (!CampaignRules.IsAttackable(S.Map, S.Faction, territoryId))
            {
                Toast(CampaignRules.Name(t.Owner)
                      + " land — not on your front. Advance to a bordering territory first.");
                return;
            }

            OpenAttack(territoryId);
        }

        void Toast(string text)
        {
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
            _toastUntil = Time.unscaledTime + 2.6f;
        }

        void OpenAttack(int territoryId)
        {
            var t = S.Map.Of(territoryId);
            var prize = CampaignRules.CapitalPrize(S, territoryId);
            var designation = CampaignRules.CapitalDesignation(S.Map, territoryId);

            _overlay = UiKit.Scrim(_root, CloseOverlay);
            var box = UiKit.Glass(_overlay, 16f);
            box.style.width = 420f * UiKit.S;
            box.style.maxHeight = Screen.height * 0.82f;
            UiKit.Radius(box, 8f);

            var head = UiKit.Row(box);
            UiKit.Text(head, "March on " + CampaignRules.Name(t.Owner) + " ground", 18f,
                       UiFont.DisplayBlack, UiKit.Ink);
            if (prize != Element.None)
                UiKit.Text(head, " — " + CampaignRules.Name(prize).ToUpperInvariant() + " CAPITAL", 15f,
                           UiFont.DisplayBold, UiKit.Gold);
            else if (designation == S.Faction)
                UiKit.Text(head, " — YOUR CAPITAL", 15f, UiFont.DisplayBold, UiKit.Gold);

            string note = prize != Element.None
                ? "Take it to absorb " + CampaignRules.Name(prize)
                  + " — its remaining lands and its dual deck become yours."
                : designation == S.Faction
                    ? "Your throne, held by another — retake it."
                    : "";
            if (note.Length > 0)
                UiKit.Text(box, note, 12f, UiFont.BodyRegular, UiKit.Dim).style.marginTop = 5f * UiKit.S;

            UiKit.Text(box, "Garrison " + t.Garrison, 12.5f, UiFont.DisplayBold, UiKit.Ink)
                .style.marginTop = 6f * UiKit.S;
            UiKit.Text(box, "March under which banner?", 12f, UiFont.BodyRegular, UiKit.Dim)
                .style.marginTop = 8f * UiKit.S;

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.maxHeight = 240f * UiKit.S;
            box.Add(list);

            foreach (var cid in CampaignRules.AvailableCommanders(S))
            {
                CommanderDef cc;
                if (_cat == null || !_cat.TryCommander(cid, out cc)) continue;

                var row = UiKit.Row(list);
                var btn = UiKit.Btn(row, "", () => { CloseOverlay(); _shell.AttackTerritory(territoryId, cid); }, 13f);
                btn.text = "";
                btn.style.flexGrow = 1f;
                btn.style.flexDirection = FlexDirection.Row;
                btn.style.alignItems = Align.Center;
                btn.style.justifyContent = Justify.FlexStart;

                foreach (var col in cc.Colors)
                {
                    var d = ElementOf(col);
                    UiKit.Badge(btn, d != null ? d.Glyph : "", GlobeView.ElementColour(col), 20f)
                        .style.marginRight = 4f * UiKit.S;
                }
                var textCol = UiKit.Box(btn);
                textCol.style.marginLeft = 4f * UiKit.S;
                UiKit.Text(textCol, cc.Name, 14f, UiFont.DisplayBold, UiKit.Ink);
                UiKit.Text(textCol, "♥" + cc.Hp + " · ⚒ " + cc.Workers, 10.5f, UiFont.BodyRegular, UiKit.Dim);
            }

            UiKit.Btn(box, "Cancel", CloseOverlay, 13f, UiKit.Dim).style.marginTop = 8f * UiKit.S;
        }

        void CloseOverlay()
        {
            if (_overlay == null) return;
            _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        void EndTurn()
        {
            if (_overlay != null) return;
            var log = Camp.EndTurn();

            if (S.Lost)
            {
                _overlay = UiKit.Scrim(_root);
                var dead = UiKit.Glass(_overlay, 20f);
                dead.style.alignItems = Align.Center;
                UiKit.Radius(dead, 8f);
                UiKit.Text(dead, "YOUR BANNER HAS FALLEN", 24f, UiFont.DisplayBlack, UiKit.Danger);
                UiKit.Text(dead, "The last of your holdings is lost. The campaign is over.", 12.5f,
                           UiFont.BodyRegular, UiKit.Ink).style.marginTop = 6f * UiKit.S;
                UiKit.Btn(dead, "New Campaign", () =>
                {
                    Camp.Delete();
                    _shell.Show(ShellScreen.FactionSelect);
                }, 14f, UiKit.Gold).style.marginTop = 12f * UiKit.S;
                return;
            }

            if (_shell.Globe != null) _shell.Globe.Recolour();
            Build();
            ShowTurnLog(log);
        }

        void ShowTurnLog(IReadOnlyList<CampaignEvent> log)
        {
            _overlay = UiKit.Scrim(_root, CloseOverlay);
            var box = UiKit.Glass(_overlay, 16f);
            box.style.width = 380f * UiKit.S;
            UiKit.Radius(box, 8f);

            UiKit.Text(box, "Turn " + S.Turn + " — the world stirs", 17f, UiFont.DisplayBlack, UiKit.Gold);

            int shown = 0;
            foreach (var e in log)
            {
                if (e.Kind != CampaignEventKind.AiCaptured && e.Kind != CampaignEventKind.AiRepulsed) continue;
                UiKit.Text(box, e.Text, 12f, UiFont.BodyRegular,
                           e.From == S.Faction ? new Color(0.88f, 0.65f, 0.60f) : UiKit.Ink)
                    .style.marginTop = 5f * UiKit.S;
                shown++;
            }
            if (shown == 0)
                UiKit.Text(box, "The map lies quiet this turn.", 12f, UiFont.BodyItalic, UiKit.Dim)
                    .style.marginTop = 6f * UiKit.S;

            UiKit.Btn(box, "Continue", CloseOverlay, 13f).style.marginTop = 12f * UiKit.S;
        }

        void ConfirmReset()
        {
            if (_overlay != null) return;
            _overlay = UiKit.Scrim(_root, CloseOverlay);
            var box = UiKit.Glass(_overlay, 16f);
            box.style.width = 360f * UiKit.S;
            box.style.alignItems = Align.Center;
            UiKit.Radius(box, 8f);

            UiKit.Text(box, "Abandon this campaign?", 18f, UiFont.DisplayBlack, new Color(0.88f, 0.65f, 0.60f));
            UiKit.Text(box, "Your conquered lands and alliances are lost, and a new world is drawn.",
                       12f, UiFont.BodyRegular, UiKit.Dim).style.marginTop = 6f * UiKit.S;

            var row = UiKit.Row(box);
            row.style.marginTop = 12f * UiKit.S;
            UiKit.Btn(row, "Start over", () =>
            {
                Camp.Delete();
                _shell.Show(ShellScreen.FactionSelect);
            }, 13f, UiKit.Danger).style.marginRight = 8f * UiKit.S;
            UiKit.Btn(row, "Keep playing", CloseOverlay, 13f);
        }
    }
}
