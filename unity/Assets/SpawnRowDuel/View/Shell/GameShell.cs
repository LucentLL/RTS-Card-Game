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
        Multiplayer = 7,    // the password lobby
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
        MultiplayerUi _multiplayer;

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

        Vector2Int _builtAt;
        float _biomeAt;
        int _biomeIndex;
        bool _swapped;
        VisualElement _scrim;

        /// <summary>How dark the title screen's wash sits when it is not mid-change. Barely: the
        /// text carries its own outline, the buttons carry their own panel, and the point of
        /// putting a battlefield back there was to be able to see it.</summary>
        const float ScrimBase = 0.16f;

        void Update()
        {
            if (_root == null) return;
            HudLayout.Recompute();

            // REBUILT ON RESIZE. Every size in this shell is a multiple of HudLayout.Scale, which
            // is decided by the screen's short edge - so a screen that changes shape after the
            // menu was built leaves it laid out for a screen that is not there any more. That is
            // not an edge case on the platform this ships to: a phone rotates, and the first tap
            // anywhere takes the build fullscreen.
            var now = new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (now != _builtAt && now.x > 0 && now.y > 0) Show(Screen);

            if (Screen == ShellScreen.MainMenu) CycleBattlefield();
            if (Screen == ShellScreen.WorldMap && _map != null) _map.Tick();
            if (Screen == ShellScreen.Challenge && _challenge != null) _challenge.Tick();
            if (Screen == ShellScreen.Multiplayer && _multiplayer != null) _multiplayer.Tick();
            if (Screen == ShellScreen.Battle) WatchBattle();
        }

        // ── routing ─────────────────────────────────────────────────────────────────────

        public void Show(ShellScreen screen)
        {
            // LEAVING A DUEL ENDS IT.
            //
            // Nothing used to put the match down, and `MatchController.MatchStarted` is just
            // `Engine != null` - so quitting to the menu left the whole duel sitting there, and
            // pressing Duel again handed it straight back, mid-turn, with the pieces still where
            // they were. The commander select is only drawn while no match exists, so the player
            // never even got the chance to pick.
            //
            // Gated on the screen actually CHANGING, which is not fussiness: `Show(Screen)` is
            // re-run on every resize (Update, above), and a skirmish started from MatchHud's own
            // select keeps the screen at Skirmish while it is played - so a bare "not Battle" test
            // would end that match the first time the phone was rotated. Battle itself is exempt
            // because every path into it starts its own match a moment later.
            if (screen != Screen && screen != ShellScreen.Battle && Match != null)
                Match.EndMatch();

            Screen = screen;
            _map = null;
            _challenge = null;
            if (screen != ShellScreen.Multiplayer) _multiplayer = null;
            ClearRoot();
            if (screen != ShellScreen.Battle) _battleExit = null;

            // BEFORE anything is built. Start() shows the menu, and Start() runs before the first
            // Update - so the boot menu was laid out against HudLayout.Scale's default of 1 and
            // then never rebuilt. On a big screen that is a postage stamp in the middle of a
            // black field, which is exactly what it looked like.
            HudLayout.Recompute();
            _builtAt = new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height);

            bool battleWorld = screen == ShellScreen.Battle || screen == ShellScreen.Skirmish;
            bool globeWorld = screen == ShellScreen.WorldMap || screen == ShellScreen.Challenge;

            // The menu stands on a REAL BATTLEFIELD - terrain and sky, no board. It is the same
            // world the duel is fought on, minus the one root that carries the cards, so it costs
            // nothing to build and the title screen stops being a black rectangle.
            bool scenery = screen == ShellScreen.MainMenu;

            if (BattleRoot != null) BattleRoot.SetActive(battleWorld);
            if (TerrainRoot != null) TerrainRoot.SetActive(battleWorld || scenery);
            if (BattleCamera != null) BattleCamera.enabled = battleWorld || scenery;
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
                case ShellScreen.Multiplayer: BuildMultiplayer(); break;

                // A duel has nothing for the shell to lay out except the way out of it - which is
                // exactly why it has to be put BACK here. This branch runs on every resize.
                case ShellScreen.Battle:
                    if (_battleExit != null) _battleExit();
                    break;
            }
        }

        static void Outline(VisualElement v, float width)
        {
            v.style.unityTextOutlineWidth = width;
            v.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        }

        void Backdrop() { _scrim = null; Backdrop(1f); }

        /// <summary>The wash the shell screens sit on. Opaque everywhere except the title screen,
        /// which is a SCRIM over the live battlefield - thin enough to see the ground through,
        /// and the thing that closes to black while the field is changed under it.</summary>
        VisualElement Backdrop(float alpha)
        {
            var bg = new VisualElement();
            UiKit.Fill(bg);
            bg.style.backgroundColor = new Color(0.02f, 0.022f, 0.03f, alpha);
            _root.Add(bg);
            return bg;
        }

        /// <summary>
        /// Walk the battlefields behind the title.
        ///
        /// The user's own idea, and it answers "too blank" better than any amount of chrome would:
        /// the menu's backdrop becomes the thing the game is about.
        ///
        /// EVERY field, not the duel's roll list. A match skips Shore and Deep Water because half
        /// their board spends the game under water - which is a rule about playing on them, not
        /// about looking at them, and they are the two best-looking fields in the set.
        ///
        /// The change is a DIP, not a cut. Applying a biome rebuilds the ground, the blades, the
        /// scatter and the settle sheet in one frame, so the swap can only ever be instant; what
        /// can be gradual is the light on it. The scrim closes to black over a second, the field
        /// changes while nothing can be seen, and it opens again over two. Long dwell between -
        /// this is a backdrop, and a backdrop that changes while you are reading the menu is a
        /// distraction rather than a view.
        /// </summary>
        const float Dwell = 17f, FadeOut = 1.0f, FadeIn = 2.0f;

        void CycleBattlefield()
        {
            if (_scrim == null) return;

            float since = Time.unscaledTime - _biomeAt;
            float a;

            if (since < Dwell) a = 0f;                                  // settled: show the field
            else if (since < Dwell + FadeOut) a = (since - Dwell) / FadeOut;
            else if (since < Dwell + FadeOut + FadeIn)
            {
                if (!_swapped) { NextBattlefield(); _swapped = true; }  // at full black
                a = 1f - (since - Dwell - FadeOut) / FadeIn;
            }
            else { _biomeAt = Time.unscaledTime; _swapped = false; a = 0f; }

            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));
            _scrim.style.backgroundColor = new Color(0.02f, 0.022f, 0.03f,
                                                     Mathf.Lerp(ScrimBase, 1f, k));
        }

        void NextBattlefield()
        {
            var fields = World.Biomes.All;
            if (fields == null || fields.Length == 0) return;
            _biomeIndex = (_biomeIndex + 1) % fields.Length;
            World.TerrainField.Requested = fields[_biomeIndex];
        }

        // ── main menu ───────────────────────────────────────────────────────────────────

        void BuildMainMenu()
        {
            _scrim = Backdrop(ScrimBase);
            _biomeAt = Time.unscaledTime;
            _swapped = false;

            var col = UiKit.Box(_root);
            UiKit.Fill(col);
            col.style.alignItems = Align.Center;
            col.style.justifyContent = Justify.Center;

            // SIZED TO THE SCREEN, not to a constant. UiKit.S is keyed on the short edge and
            // bottoms out at 1, which on a landscape phone leaves a 300px menu adrift in an
            // 850px screen. The title screen is four buttons and a name: it can afford to take
            // a real share of the display, and on a phone it has to.
            float wide = Mathf.Min(UnityEngine.Screen.width, UnityEngine.Screen.height * 1.9f);
            float menuW = Mathf.Clamp(wide * 0.46f, 280f, 620f * UiKit.S);
            float title = Mathf.Clamp(menuW * 0.115f, 26f, 64f);

            var head = UiKit.Text(col, "SPAWN ROW DUEL", title / UiKit.S, UiFont.DisplayBlack, UiKit.Gold);
            head.style.marginBottom = 4f * UiKit.S;

            var tag = UiKit.Text(col, "a card duel fought on ground you have to hold",
                                 Mathf.Clamp(title * 0.34f, 11f, 22f) / UiKit.S,
                                 UiFont.BodyItalic, UiKit.Ink);
            tag.style.marginBottom = 22f * UiKit.S;

            // The words now sit on GROUND, not on a flat wash, and the ground changes colour every
            // few seconds - so they carry their own contrast rather than borrowing it from the
            // backdrop. Dim grey on sand was almost gone.
            // 0.10 and 0.12, not 0.22 and 0.30. The width is a fraction of the glyph's own
            // weight, so what read as "a bit bolder" on the 60px title CLOSED UP the 16px
            // subtitle and swallowed it whole. CardFace's range (0.08-0.12) is the honest one.
            Outline(head, 0.10f);
            Outline(tag, 0.12f);

            var menu = UiKit.Box(col);
            menu.style.width = menuW;

            float btn = Mathf.Clamp(menuW * 0.062f, 16f, 30f) / UiKit.S;
            UiKit.Btn(menu, "Duel", () => Show(ShellScreen.Skirmish), btn);
            UiKit.Btn(menu, Campaign.HasRunnableCampaign ? "Campaign — continue" : "Campaign", () =>
            {
                if (Campaign.HasRunnableCampaign) Show(ShellScreen.WorldMap);
                else Show(ShellScreen.FactionSelect);
            }, btn);
            UiKit.Btn(menu, "Duel a Friend", () => Show(ShellScreen.Multiplayer), btn);
            UiKit.Btn(menu, "Deck Builder", () => Show(ShellScreen.DeckBuilder), btn);

            if (Campaign.State != null && Campaign.State.Lost)
                UiKit.Text(menu, "your last banner fell — a new world awaits", 11f, UiFont.BodyItalic, UiKit.Danger)
                    .style.marginTop = 6f * UiKit.S;
        }


        // -- multiplayer -----------------------------------------------------------------

        void BuildMultiplayer()
        {
            Backdrop();
            if (Catalog == null) return;
            _multiplayer = new MultiplayerUi(this, Catalog, _root);
        }

        /// <summary>
        /// A duel has been agreed with another player. The battle world comes up on the match the
        /// SESSION built - never on one built here, because two peers must be looking at boards
        /// that are identical to the byte, and the only board with that guarantee is the one the
        /// handshake agreed.
        /// </summary>
        public void BeginNetMatch(SpawnRowDuel.Net.NetSession session)
        {
            _multiplayer = null;
            Show(ShellScreen.Battle);
            if (Match != null) Match.AdoptNetMatch(session);
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
            _battleExit = null;
            ClearRoot();

            _challenge = new ChallengeUi(_root, Catalog, s.Faction, defender, ownCapital,
                                         () => StartCampaignBattle(request));
            _challenge.Build();
        }

        void StartCampaignBattle(BattleLaunchRequest request)
        {
            _resolvedFor = -1;
            _battleExit = null;
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
            ClearRoot();
            _battleExit = () => ExitBar("↩ abandon", () =>
            {
                Campaign.Resolve(BattleOutcome.Abandoned);
                Show(ShellScreen.WorldMap);
            });
            _battleExit();
        }

        /// <summary>
        /// How to rebuild the way out of the match in progress, or null when there is none.
        ///
        /// The shell REBUILDS ITSELF whenever the screen changes shape (every size here is a
        /// multiple of `HudLayout.Scale`), and the rebuild runs `Show`, whose switch has no case
        /// for a battle - there is nothing to lay out but this. So rotating a phone mid-duel
        /// cleared the exit button and never put it back, and the campaign's only "get me out of
        /// here" was gone for the rest of the match. It is remembered rather than reconstructed
        /// from the screen, because what the button DOES differs by how the duel was started.
        /// </summary>
        System.Action _battleExit;

        /// <summary>Empty the shell's layer, and forget what it was holding over the battle. Every
        /// `_root.Clear()` goes through here: a rect that outlives the button it describes would go
        /// on reserving a band of HUD and refusing board taps under a control that is not there.</summary>
        void ClearRoot()
        {
            if (_root != null) _root.Clear();
            HudLayout.ShellPx = new Rect();
        }

        /// <summary>
        /// The way out of a duel, hung off the top right corner - and PUBLISHED.
        ///
        /// This one button is the shell's whole footprint on a match in progress, and it sits on
        /// top of a HUD drawn by a different UI system. IMGUI and UI Toolkit are separate input
        /// paths that never see each other's handled events, so anything MatchHud draws under this
        /// runs on the same tap that presses it: the match log's CLOSE was pinned to the same
        /// corner and closing the log therefore abandoned the match. Reporting where the button
        /// ACTUALLY landed - after layout, in real pixels - is what lets MatchHud keep its panels
        /// clear of it and BoardInput refuse the tap, instead of both guessing at a magic number.
        /// </summary>
        void ExitBar(string label, System.Action onClick)
        {
            var bar = UiKit.Row(_root);
            bar.style.position = Position.Absolute;
            bar.style.right = 8f * UiKit.S;
            bar.style.top = HudLayout.TopPx + 6f * UiKit.S;

            UiKit.Btn(bar, label, onClick, 12f, UiKit.Dim);

            // worldBound is only meaningful once the layout pass has run, so it is read from the
            // event that says it has - and again on every reflow, because the shell rebuilds
            // itself at a new scale whenever the screen changes shape.
            bar.RegisterCallback<GeometryChangedEvent>(e => PublishExitBar(bar));
        }

        /// <summary>
        /// The exit button's rect, in DEVICE PIXELS.
        ///
        /// UI Toolkit answers in PANEL units and everything that reads HudLayout works in device
        /// pixels; on WebGL with a devicePixelRatio those are not the same scale (the same trap
        /// BoardProjection exists to hold in one place). Converting here, at the one place that
        /// has both the panel and the element, is what keeps the rect honest on a phone.
        /// </summary>
        void PublishExitBar(VisualElement bar)
        {
            var panel = _root != null ? _root.worldBound : new Rect();
            var b = bar.worldBound;
            if (panel.width <= 1f || panel.height <= 1f || b.width <= 0f || b.height <= 0f)
            {
                HudLayout.ShellPx = new Rect();
                return;
            }

            float kx = UnityEngine.Screen.width / panel.width;
            float ky = UnityEngine.Screen.height / panel.height;
            HudLayout.ShellPx = new Rect(b.x * kx, b.y * ky, b.width * kx, b.height * ky);
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
            _battleExit = null;              // the duel is over; there is nothing to abandon
            ClearRoot();
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
            _battleExit = null;
            Show(ShellScreen.Battle);
            Match.StartMatch(you, foe, (ulong)Random.Range(1, int.MaxValue), youDeck, null);
            ClearRoot();
            _battleExit = () => ExitBar("↩ menu", () => Show(ShellScreen.MainMenu));
            _battleExit();
        }
    }
}
