using System.Collections.Generic;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Campaign;
using SpawnRowDuel.View.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    public enum ShellScreen : byte
    {
        MainMenu = 0,
        FactionSelect = 1,
        WorldMap = 2,
        Challenge = 3,
        Battle = 4,
        DeckBuilder = 5,
        Skirmish = 6,       // the duel's own commander select drives itself
    }

    /// <summary>
    /// The front of the game: which screen is up, and what the battle scene is allowed to do while
    /// it is not the one showing.
    ///
    /// Before this there was no front - the duel booted straight into its own commander select and
    /// that was the whole product. The campaign needs somewhere to stand, the deck builder needs
    /// somewhere to return to, and both need the battle world switched OFF while they are up: the
    /// board, the terrain and the duel's own HUD all live on one GameObject, so hiding them is one
    /// SetActive rather than a rule every layer has to remember.
    /// </summary>
    public sealed class GameShell : MonoBehaviour
    {
        public MatchController Match;
        public GlobeView Globe;
        public Camera GlobeCamera;
        public GameObject BattleRoot;      // the board object: view, input, HUD, hand, plates
        public GameObject TerrainRoot;
        public Camera BattleCamera;

        public ShellScreen Screen { get; private set; }

        public readonly CampaignService Campaign = new CampaignService();

        UIDocument _doc;
        VisualElement _root;
        ElementPalette _palette;
        WorldMapUi _map;
        ChallengeUi _challenge;
        DeckBuilderUi _deck;

        ICardCatalog Catalog { get { return Match != null ? Match.Catalog : null; } }

        void Start()
        {
            Campaign.Load();
            EnsurePanel();
            Show(ShellScreen.MainMenu);
        }

        void EnsurePanel()
        {
            if (_doc != null) return;

            var panel = Resources.Load<PanelSettings>(Cards.HandBar.PanelResource);
            if (panel == null)
            {
                Debug.LogError("[shell] HudPanelSettings is missing - run tools/regen-fonts.sh");
                enabled = false;
                return;
            }

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = panel;
            _doc.sortingOrder = 20;              // over the duel's own hand and walls

            _root = _doc.rootVisualElement;
            UiKit.Fill(_root);
            _root.style.backgroundColor = Color.clear;
        }

        void Update()
        {
            if (_root == null) return;
            HudLayout.Recompute();

            if (Screen == ShellScreen.WorldMap && _map != null) _map.Tick();
            if (Screen == ShellScreen.Challenge && _challenge != null) _challenge.Tick();
            if (Screen == ShellScreen.Battle) WatchBattle();
        }

        // ── routing ─────────────────────────────────────────────────────────────────────

        public void Show(ShellScreen screen)
        {
            Screen = screen;
            _map = null;
            _challenge = null;
            if (_root != null) _root.Clear();

            bool battleWorld = screen == ShellScreen.Battle || screen == ShellScreen.Skirmish;
            bool globeWorld = screen == ShellScreen.WorldMap || screen == ShellScreen.Challenge;

            if (BattleRoot != null) BattleRoot.SetActive(battleWorld);
            if (TerrainRoot != null) TerrainRoot.SetActive(battleWorld);
            if (BattleCamera != null) BattleCamera.enabled = battleWorld;
            if (GlobeCamera != null) GlobeCamera.enabled = globeWorld;
            if (Globe != null) Globe.gameObject.SetActive(globeWorld);

            // the duel's IMGUI commander select is only allowed to speak on its own screen
            MatchHud.ShellSuppressed = screen != ShellScreen.Skirmish;

            if (_root == null) return;
            switch (screen)
            {
                case ShellScreen.MainMenu: BuildMainMenu(); break;
                case ShellScreen.FactionSelect: BuildFactionSelect(); break;
                case ShellScreen.WorldMap: BuildWorldMap(); break;
                case ShellScreen.DeckBuilder: BuildDeckBuilder(); break;
            }
        }

        void Backdrop()
        {
            var bg = new VisualElement();
            UiKit.Fill(bg);
            bg.style.backgroundColor = new Color(0.035f, 0.04f, 0.06f, 1f);
            _root.Add(bg);
        }

        // ── main menu ───────────────────────────────────────────────────────────────────

        void BuildMainMenu()
        {
            Backdrop();

            var col = UiKit.Box(_root);
            UiKit.Fill(col);
            col.style.alignItems = Align.Center;
            col.style.justifyContent = Justify.Center;

            var title = UiKit.Text(col, "SPAWN ROW DUEL", 34f, UiFont.DisplayBlack, UiKit.Gold);
            title.style.marginBottom = 4f * UiKit.S;
            UiKit.Text(col, "a card duel fought on ground you have to hold", 13f, UiFont.BodyItalic, UiKit.Dim)
                .style.marginBottom = 22f * UiKit.S;

            var menu = UiKit.Box(col);
            menu.style.width = 300f * UiKit.S;

            UiKit.Btn(menu, "Duel", () => Show(ShellScreen.Skirmish), 17f);
            UiKit.Btn(menu, Campaign.HasRunnableCampaign ? "Campaign — continue" : "Campaign", () =>
            {
                if (Campaign.HasRunnableCampaign) Show(ShellScreen.WorldMap);
                else Show(ShellScreen.FactionSelect);
            }, 17f);
            UiKit.Btn(menu, "Deck Builder", () => Show(ShellScreen.DeckBuilder), 17f);

            if (Campaign.State != null && Campaign.State.Lost)
                UiKit.Text(menu, "your last banner fell — a new world awaits", 11f, UiFont.BodyItalic, UiKit.Danger)
                    .style.marginTop = 6f * UiKit.S;
        }

        // ── faction select ──────────────────────────────────────────────────────────────

        void BuildFactionSelect()
        {
            Backdrop();
            if (Catalog == null) return;

            var page = UiKit.Box(_root);
            UiKit.Fill(page);
            page.style.paddingLeft = 22f * UiKit.S; page.style.paddingRight = 22f * UiKit.S;
            page.style.paddingTop = 16f * UiKit.S; page.style.paddingBottom = 16f * UiKit.S;

            var head = UiKit.Row(page);
            UiKit.Btn(head, "← menu", () => Show(ShellScreen.MainMenu), 13f);
            var titles = UiKit.Box(head);
            titles.style.marginLeft = 16f * UiKit.S;
            UiKit.Text(titles, "Choose Your Banner", 24f, UiFont.DisplayBlack, UiKit.Gold);
            UiKit.Text(titles,
                "Hold one home realm on a freshly-drawn world, then conquer it territory by territory. "
                + "Take an element's capital to absorb its lands and unlock its dual deck.",
                12f, UiFont.BodyRegular, UiKit.Dim);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.marginTop = 12f * UiKit.S;
            page.Add(scroll);

            var grid = UiKit.Box(scroll);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;

            foreach (var el in CampaignRules.Majors)
            {
                var def = ElementOf(el);
                if (def == null) continue;

                var colour = GlobeView.ElementColour(el);
                var card = UiKit.Glass(grid, 12f);
                card.style.width = 300f * UiKit.S;
                card.style.marginRight = 10f * UiKit.S;
                card.style.marginBottom = 10f * UiKit.S;
                UiKit.Radius(card, 7f);

                var top = UiKit.Row(card);
                UiKit.Badge(top, def.Glyph, colour, 30f);
                var names = UiKit.Box(top);
                names.style.marginLeft = 9f * UiKit.S;
                UiKit.Text(names, def.Name, 19f, UiFont.DisplayBlack, colour);
                UiKit.Text(names, "♥" + def.Hp + " · ⚒ " + def.Workers + " workers", 11f,
                           UiFont.BodyRegular, UiKit.Dim);

                UiKit.Text(card, def.Lore, 11.5f, UiFont.BodyItalic, UiKit.Ink)
                    .style.marginTop = 7f * UiKit.S;

                var pick = UiKit.Btn(card, "Raise the " + def.Name + " banner", () =>
                {
                    Campaign.Begin(el);
                    Show(ShellScreen.WorldMap);
                }, 13f, colour);
                pick.style.marginTop = 8f * UiKit.S;
            }
        }

        ElementDef ElementOf(Element el)
        {
            var cat = Catalog;
            if (cat == null) return null;
            foreach (var d in cat.Elements) if (d.El == el) return d;
            return null;
        }

        // ── world map ───────────────────────────────────────────────────────────────────

        void BuildWorldMap()
        {
            if (!Campaign.HasRunnableCampaign) { Show(ShellScreen.FactionSelect); return; }

            if (Globe != null)
            {
                Globe.Cam = GlobeCamera;
                Globe.Build(Campaign.State.Map, Campaign.State.Faction);
                int seat;
                if (Campaign.State.Map.Capitals.TryGetValue(Campaign.State.Faction, out seat))
                    Globe.AimAt(Campaign.State.Map.Of(seat).AnchorTile);
            }

            _map = new WorldMapUi(this, _root, Catalog);
            _map.Build();
        }

        // ── deck builder ────────────────────────────────────────────────────────────────

        void BuildDeckBuilder()
        {
            if (_deck == null) _deck = new DeckBuilderUi(this, Catalog, Palette, Match);
            _deck.Build(_root, () => Show(ShellScreen.MainMenu));
        }

        public ElementPalette Palette
        {
            get
            {
                if (_palette == null && Catalog != null) _palette = new ElementPalette(Catalog);
                return _palette;
            }
        }

        // ── the battle handoff ──────────────────────────────────────────────────────────

        int _resolvedFor = -1;

        /// <summary>Take a territory: park the target, play the challenge, then start the duel.</summary>
        public void AttackTerritory(int territoryId, CommanderId banner)
        {
            var s = Campaign.State;
            var t = s.Map.Of(territoryId);
            var defender = t.Owner;
            bool ownCapital = CampaignRules.CapitalDesignation(s.Map, territoryId) == defender;

            var request = Campaign.Launch(territoryId, banner);

            Screen = ShellScreen.Challenge;
            _map = null;
            _root.Clear();

            _challenge = new ChallengeUi(_root, Catalog, s.Faction, defender, ownCapital,
                                         () => StartCampaignBattle(request));
            _challenge.Build();
        }

        void StartCampaignBattle(BattleLaunchRequest request)
        {
            _resolvedFor = -1;
            Show(ShellScreen.Battle);
            Match.StartMatch(request.PlayerCommander, request.EnemyCommander, request.DeckSeed);
            BuildBattleOverlay(request.TerritoryId);
        }

        /// <summary>
        /// The only thing the shell puts on top of a campaign duel: a way out of it. Abandoning is
        /// a real outcome rather than a lost target - the assault simply never happened.
        /// </summary>
        void BuildBattleOverlay(int territoryId)
        {
            _root.Clear();
            var bar = UiKit.Row(_root);
            bar.style.position = Position.Absolute;
            bar.style.right = 8f * UiKit.S;
            bar.style.top = HudLayout.TopPx + 6f * UiKit.S;

            UiKit.Btn(bar, "↩ abandon", () =>
            {
                Campaign.Resolve(BattleOutcome.Abandoned);
                Show(ShellScreen.WorldMap);
            }, 12f, UiKit.Dim);
        }

        void WatchBattle()
        {
            if (Match == null || Match.Engine == null) return;
            var s = Match.Engine.State;
            if (!s.IsOver || !Campaign.BattlePending) return;
            if (_resolvedFor == Campaign.State.TargetTerritory.Value) return;

            _resolvedFor = Campaign.State.TargetTerritory.Value;
            bool won = s.Outcome == MatchOutcome.YouWin;
            var log = Campaign.Resolve(won ? BattleOutcome.PlayerWon : BattleOutcome.PlayerLost);
            ShowResult(won, log);
        }

        void ShowResult(bool won, IReadOnlyList<CampaignEvent> log)
        {
            _root.Clear();
            var scrim = UiKit.Scrim(_root);

            var box = UiKit.Glass(scrim, 20f);
            box.style.width = 420f * UiKit.S;
            box.style.alignItems = Align.Center;
            UiKit.Radius(box, 8f);

            string headline = "TERRITORY WON";
            var tint = UiKit.Gold;
            foreach (var e in log)
            {
                if (e.Kind == CampaignEventKind.CapitalTaken) headline = "CAPITAL TAKEN";
                if (e.Kind == CampaignEventKind.RealmUnited) headline = "THE REALM IS UNITED";
            }
            if (!won) { headline = "ASSAULT REPELLED"; tint = UiKit.Danger; }

            UiKit.Text(box, headline, 26f, UiFont.DisplayBlack, tint);

            foreach (var e in log)
            {
                if (string.IsNullOrEmpty(e.Text)) continue;
                UiKit.Text(box, e.Text, 12.5f, UiFont.BodyRegular, UiKit.Ink)
                    .style.marginTop = 6f * UiKit.S;
            }

            var row = UiKit.Row(box);
            row.style.marginTop = 14f * UiKit.S;
            if (Campaign.State.Completed)
                UiKit.Btn(row, "New Campaign", () => { Campaign.Delete(); Show(ShellScreen.FactionSelect); }, 14f);
            UiKit.Btn(row, "↩ World Map", () => Show(ShellScreen.WorldMap), 14f, UiKit.Gold);
        }

        /// <summary>Start a plain skirmish from the deck builder or the menu.</summary>
        public void StartSkirmish(CommanderId you, CommanderId foe, List<HandCard> youDeck)
        {
            Show(ShellScreen.Battle);
            Match.StartMatch(you, foe, (ulong)Random.Range(1, int.MaxValue), youDeck, null);
            _root.Clear();
            var bar = UiKit.Row(_root);
            bar.style.position = Position.Absolute;
            bar.style.right = 8f * UiKit.S;
            bar.style.top = HudLayout.TopPx + 6f * UiKit.S;
            UiKit.Btn(bar, "↩ menu", () => Show(ShellScreen.MainMenu), 12f, UiKit.Dim);
        }
    }
}
