using System.Collections.Generic;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Campaign;
using SpawnRowDuel.View.Cards;
using SpawnRowDuel.View.Decks;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// The deck builder: three columns - what you are looking at, what you have built, and what
    /// you may still take.
    ///
    /// The search is one lowercase substring over name, type, colour, keyword and tribe joined
    /// together, which is why typing "dragon", "first strike" or "wizard" all work without any of
    /// them being a field the player has to know exists. The filters are all toggle-off-on-repeat
    /// for the same reason: nothing here should need explaining.
    ///
    /// Structures are absent on purpose. They are built by the commander during a duel, not drawn,
    /// so they are not deck cards at all (spec 09 §14.3).
    /// </summary>
    public sealed class DeckBuilderUi
    {
        readonly GameShell _shell;
        readonly ICardCatalog _cat;
        readonly ElementPalette _palette;
        readonly MatchController _match;

        CardTextService _text;
        CardArtIndex _art;

        List<SavedDeck> _saved;
        SavedDeck _deck;
        int _editIndex = -1;
        string _selected;

        string _query = "";
        string _typeFilter = "";        // "", "creature", "spell"
        Element _elemFilter = Element.Divine;   // Divine = "no element filter"
        int _costFilter;                // 0 = none, 6 = "6+"
        Keyword _kwFilter = Keyword.None;
        bool _fsFilter;
        string _tagFilter = "";
        string _sort = "type";

        VisualElement _root, _detail, _deckCol, _poolCol;
        Label _counter, _hint, _poolCount;
        System.Action _onBack;

        public DeckBuilderUi(GameShell shell, ICardCatalog cat, ElementPalette palette, MatchController match)
        {
            _shell = shell; _cat = cat; _palette = palette; _match = match;
        }

        public void Build(VisualElement root, System.Action onBack)
        {
            _root = root; _onBack = onBack;
            if (_cat == null) return;

            if (_text == null) _text = new CardTextService(_cat);
            if (_art == null && _match != null) _art = new CardArtIndex(_match.Database);

            _saved = DeckStore.Load(_cat);
            if (_deck == null) _deck = new SavedDeck { Commander = new CommanderId("fire") };

            Render();
        }

        void Render()
        {
            _root.Clear();

            var bg = new VisualElement();
            UiKit.Fill(bg);
            bg.style.backgroundColor = new Color(0.035f, 0.04f, 0.06f, 1f);
            _root.Add(bg);

            var page = UiKit.Box(_root);
            UiKit.Fill(page);
            page.style.paddingLeft = 10f * UiKit.S; page.style.paddingRight = 10f * UiKit.S;
            page.style.paddingTop = 8f * UiKit.S; page.style.paddingBottom = 8f * UiKit.S;

            BuildTopBar(page);

            var cols = UiKit.Row(page);
            cols.style.flexGrow = 1f;
            cols.style.alignItems = Align.Stretch;
            cols.style.marginTop = 8f * UiKit.S;

            _detail = Column(cols, 0.9f);
            _deckCol = Column(cols, 1.3f);
            _poolCol = Column(cols, 1.7f);

            RenderDetail();
            RenderDeck();
            RenderPool();
            RefreshCounter();
        }

        static VisualElement Column(VisualElement parent, float grow)
        {
            var v = UiKit.Glass(parent, 9f);
            v.style.flexGrow = grow;
            v.style.flexBasis = 0f;
            v.style.marginRight = 7f * UiKit.S;
            v.style.overflow = Overflow.Hidden;
            UiKit.Radius(v, 6f);
            return v;
        }

        // ── top bar ─────────────────────────────────────────────────────────────────────

        void BuildTopBar(VisualElement page)
        {
            var bar = UiKit.Row(page);
            bar.style.justifyContent = Justify.SpaceBetween;

            var left = UiKit.Row(bar);
            UiKit.Btn(left, "‹ back", () => { if (_onBack != null) _onBack(); }, 13f, UiKit.Dim)
                .style.marginRight = 10f * UiKit.S;
            UiKit.Text(left, "Deck Builder", 20f, UiFont.DisplayBlack, UiKit.Gold);

            var name = UiKit.Field(left, _deck.Name, 200f,
                                   v => { _deck.Name = v; RefreshCounter(); }, "deck name");
            name.style.marginLeft = 14f * UiKit.S;

            var right = UiKit.Row(bar);
            _counter = UiKit.Text(right, "0/40", 15f, UiFont.DisplayBlack, UiKit.Ink);
            _counter.style.marginRight = 10f * UiKit.S;

            UiKit.Btn(right, "Save Deck", SaveDeck, 13f, UiKit.Gold).style.marginRight = 6f * UiKit.S;
            UiKit.Btn(right, "Duel with it", DuelWithDeck, 13f).style.marginRight = 6f * UiKit.S;
            UiKit.Btn(right, "Load…", OpenLoad, 13f, UiKit.Dim);

            _hint = UiKit.Text(page, "", 11.5f, UiFont.BodyItalic, UiKit.Dim);
            _hint.style.marginTop = 3f * UiKit.S;
        }

        void RefreshCounter()
        {
            if (_counter == null) return;
            int total = _deck.Total;
            var err = DeckRules.FirstError(_cat, _deck);
            _counter.text = total + "/" + DeckRules.Size;
            _counter.style.color = err == null ? new Color(0.60f, 0.82f, 0.50f) : new Color(0.88f, 0.65f, 0.60f);

            if (_hint == null) return;
            if (string.IsNullOrEmpty(_deck.Name)) _hint.text = "Name your deck.";
            else if (_editIndex < 0 && _saved.Count >= DeckRules.MaxDecks)
                _hint.text = "You have " + DeckRules.MaxDecks + " decks — load one to edit or overwrite it.";
            else _hint.text = err ?? "Ready to save.";
        }

        // ── detail column ───────────────────────────────────────────────────────────────

        void RenderDetail()
        {
            _detail.Clear();
            UiKit.Text(_detail, "Detail", 13f, UiFont.DisplayBold, UiKit.Dim);

            if (string.IsNullOrEmpty(_selected))
            {
                UiKit.Text(_detail, "Select a card to see its details.", 12f, UiFont.BodyItalic, UiKit.Dim)
                    .style.marginTop = 8f * UiKit.S;
                return;
            }

            CardFaceModel model;
            if (!ModelOf(_selected, out model)) return;

            var full = _text.Full(new CardId(model.Name));
            if (!string.IsNullOrEmpty(full)) model.Rules = full;

            var face = new CardFace();
            float w = Mathf.Min(210f * UiKit.S, _detail.resolvedStyle.width > 1f
                                ? _detail.resolvedStyle.width - 24f * UiKit.S : 210f * UiKit.S);
            face.Bind(model, _palette, w);
            face.style.marginTop = 8f * UiKit.S;
            face.style.alignSelf = Align.Center;
            _detail.Add(face);

            var stepper = UiKit.Row(_detail);
            stepper.style.justifyContent = Justify.Center;
            stepper.style.marginTop = 10f * UiKit.S;

            var key = _selected;
            UiKit.Btn(stepper, "−", () => Bump(key, -1), 16f).style.marginRight = 10f * UiKit.S;
            UiKit.Text(stepper, _deck.CountOf(key) + " / " + DeckRules.MaxCopies, 15f, UiFont.DisplayBlack, UiKit.Ink);
            UiKit.Btn(stepper, "+", () => Bump(key, +1), 16f).style.marginLeft = 10f * UiKit.S;
        }

        void Bump(string key, int delta)
        {
            int n = Mathf.Clamp(_deck.CountOf(key) + delta, 0, DeckRules.MaxCopies);
            _deck.Set(key, n);
            RenderDetail();
            RenderDeck();
            RenderPool();
            RefreshCounter();
        }

        // ── deck column ─────────────────────────────────────────────────────────────────

        void RenderDeck()
        {
            _deckCol.Clear();

            CommanderDef cc;
            _cat.TryCommander(_deck.Commander, out cc);

            var head = UiKit.Row(_deckCol);
            UiKit.Text(head, "Leader", 13f, UiFont.DisplayBold, UiKit.Dim).style.marginRight = 8f * UiKit.S;
            var pick = UiKit.Btn(head, cc != null ? cc.Name : _deck.Commander.Value, OpenLeaderPicker, 13f);
            pick.style.flexGrow = 1f;

            RenderStats(_deckCol);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1f;
            list.style.marginTop = 6f * UiKit.S;
            _deckCol.Add(list);

            var rows = new List<string>(_deck.Cards.Keys);
            rows.Sort(CompareKeys);

            foreach (var key in rows)
            {
                CardFaceModel m;
                if (!ModelOf(key, out m)) continue;

                var row = UiKit.Row(list);
                row.style.marginBottom = 2f * UiKit.S;

                var chip = UiKit.Text(row, "◆" + m.Cost, 11f, UiFont.DisplayBold,
                                      GlobeView.ElementColour(m.Element));
                chip.style.width = 26f * UiKit.S;

                var nameLabel = UiKit.Text(row, m.Name, 12f, UiFont.BodyRegular, UiKit.Ink);
                nameLabel.style.flexGrow = 1f;

                UiKit.Text(row, "×" + _deck.CountOf(key), 12f, UiFont.DisplayBold, UiKit.Gold)
                    .style.marginRight = 6f * UiKit.S;

                var k = key;
                var minus = UiKit.Btn(row, "−", () => Bump(k, -1), 11f);
                minus.style.marginRight = 3f * UiKit.S;
                UiKit.Btn(row, "+", () => Bump(k, +1), 11f);

                row.RegisterCallback<PointerDownEvent>(e => { _selected = k; RenderDetail(); });
            }
        }

        /// <summary>Counts and the mana curve - the two things that tell you whether a deck will
        /// actually do anything on turn two.</summary>
        void RenderStats(VisualElement parent)
        {
            int creatures = 0, spells = 0;
            var curve = new int[7];

            foreach (var kv in _deck.Cards)
            {
                CardFaceModel m;
                if (!ModelOf(kv.Key, out m)) continue;
                if (m.Kind == CardKindFace.Creature) creatures += kv.Value; else spells += kv.Value;
                curve[Mathf.Clamp(m.Cost, 0, 6)] += kv.Value;
            }

            var row = UiKit.Row(parent);
            row.style.marginTop = 6f * UiKit.S;
            UiKit.Text(row, "⚔ " + creatures, 12f, UiFont.DisplayBold, UiKit.Ink).style.marginRight = 10f * UiKit.S;
            UiKit.Text(row, "✦ " + spells, 12f, UiFont.DisplayBold, UiKit.Ink);

            int max = 1;
            for (int i = 0; i < curve.Length; i++) if (curve[i] > max) max = curve[i];

            var bars = UiKit.Row(parent);
            bars.style.height = 54f * UiKit.S;
            bars.style.alignItems = Align.FlexEnd;
            bars.style.marginTop = 4f * UiKit.S;

            for (int c = 0; c <= 6; c++)
            {
                var col = UiKit.Box(bars);
                col.style.flexGrow = 1f;
                col.style.alignItems = Align.Center;
                col.style.justifyContent = Justify.FlexEnd;
                col.style.marginRight = 2f * UiKit.S;

                int v = curve[c];
                var fill = new VisualElement();
                fill.style.width = 16f * UiKit.S;
                fill.style.height = (v > 0 ? Mathf.Max(8f, 40f * v / max) : 2f) * UiKit.S;
                fill.style.backgroundColor = v > 0 ? new Color(0.72f, 0.62f, 0.32f) : new Color(1f, 1f, 1f, 0.10f);
                col.Add(fill);

                if (v > 0) UiKit.Text(col, v.ToString(), 9f, UiFont.DisplayBold, UiKit.Ink);
                UiKit.Text(col, c == 6 ? "6+" : c.ToString(), 9f, UiFont.BodyRegular, UiKit.Dim);
            }
        }

        // ── pool column ─────────────────────────────────────────────────────────────────

        void RenderPool()
        {
            _poolCol.Clear();

            CommanderDef cc;
            if (!_cat.TryCommander(_deck.Commander, out cc)) return;

            var head = UiKit.Row(_poolCol);
            UiKit.Text(head, "Cards", 13f, UiFont.DisplayBold, UiKit.Dim).style.marginRight = 8f * UiKit.S;
            _poolCount = UiKit.Text(head, "", 11.5f, UiFont.BodyRegular, UiKit.Dim);

            var search = UiKit.Field(_poolCol, _query, 260f, v =>
            {
                _query = (v ?? "").ToLowerInvariant();
                RenderPoolGrid();
            }, "search name, keyword, tribe…");
            search.style.marginTop = 4f * UiKit.S;

            BuildFilterRows(_poolCol, cc);

            _poolGrid = new ScrollView(ScrollViewMode.Vertical);
            _poolGrid.style.flexGrow = 1f;
            _poolGrid.style.marginTop = 5f * UiKit.S;
            _poolCol.Add(_poolGrid);

            RenderPoolGrid();
        }

        ScrollView _poolGrid;

        void BuildFilterRows(VisualElement parent, CommanderDef cc)
        {
            var types = UiKit.Row(parent);
            types.style.marginTop = 5f * UiKit.S;
            types.style.flexWrap = Wrap.Wrap;
            Chip(types, "All", _typeFilter == "", () => { _typeFilter = ""; RenderPool(); });
            Chip(types, "⚔ Creatures", _typeFilter == "creature", () => { _typeFilter = "creature"; RenderPool(); });
            Chip(types, "✦ Spells", _typeFilter == "spell", () => { _typeFilter = "spell"; RenderPool(); });

            if (AnyFilter())
                Chip(types, "Clear ✕", false, () =>
                {
                    _query = ""; _typeFilter = ""; _elemFilter = Element.Divine;
                    _costFilter = 0; _kwFilter = Keyword.None; _fsFilter = false; _tagFilter = "";
                    RenderPool();
                });

            var elems = UiKit.Row(parent);
            elems.style.marginTop = 3f * UiKit.S;
            elems.style.flexWrap = Wrap.Wrap;
            foreach (var col in cc.Colors)
            {
                var el = col;
                var d = ElementOf(el);
                var chip = UiKit.Btn(elems, "", () =>
                {
                    _elemFilter = _elemFilter == el ? Element.Divine : el;
                    RenderPool();
                }, 11f);
                chip.text = "";
                chip.style.paddingLeft = 4f * UiKit.S; chip.style.paddingRight = 4f * UiKit.S;
                chip.style.marginRight = 3f * UiKit.S;
                if (_elemFilter == el) UiKit.Border(chip, GlobeView.ElementColour(el), 2f);
                UiKit.Badge(chip, d != null ? d.Glyph : "", GlobeView.ElementColour(el), 16f);
            }
            Chip(elems, "◇ Neutral", _elemFilter == Element.None, () =>
            {
                _elemFilter = _elemFilter == Element.None ? Element.Divine : Element.None;
                RenderPool();
            });

            var costs = UiKit.Row(parent);
            costs.style.marginTop = 3f * UiKit.S;
            costs.style.flexWrap = Wrap.Wrap;
            for (int c = 1; c <= 6; c++)
            {
                int cost = c;
                Chip(costs, "◆" + (c == 6 ? "6+" : c.ToString()), _costFilter == cost,
                     () => { _costFilter = _costFilter == cost ? 0 : cost; RenderPool(); });
            }

            var kws = UiKit.Row(parent);
            kws.style.marginTop = 3f * UiKit.S;
            kws.style.flexWrap = Wrap.Wrap;
            Chip(kws, "First Strike", _fsFilter, () =>
            {
                _fsFilter = !_fsFilter;
                if (_fsFilter) _kwFilter = Keyword.None;
                RenderPool();
            });
            foreach (var kw in KeywordOrder)
            {
                if (!PoolHasKeyword(cc, kw)) continue;
                var k = kw;
                Chip(kws, KeywordLabel(kw), _kwFilter == kw, () =>
                {
                    _kwFilter = _kwFilter == k ? Keyword.None : k;
                    if (_kwFilter != Keyword.None) _fsFilter = false;
                    RenderPool();
                });
            }

            var sorts = UiKit.Row(parent);
            sorts.style.marginTop = 3f * UiKit.S;
            sorts.style.flexWrap = Wrap.Wrap;
            UiKit.Text(sorts, "sort ", 10.5f, UiFont.BodyRegular, UiKit.Dim);
            Sorter(sorts, "type", "Type");
            Sorter(sorts, "cost", "◆ ↑");
            Sorter(sorts, "costdesc", "◆ ↓");
            Sorter(sorts, "name", "A–Z");
            Sorter(sorts, "atk", "⚔ ↓");
        }

        void Sorter(VisualElement parent, string value, string label)
        {
            Chip(parent, label, _sort == value, () => { _sort = value; RenderPoolGrid(); });
        }

        static readonly Keyword[] KeywordOrder =
        {
            Keyword.Detonate, Keyword.Undertow, Keyword.Entrench, Keyword.Scour,
            Keyword.Chrysalis, Keyword.Overcharge, Keyword.Ward, Keyword.Reap,
        };

        static string KeywordLabel(Keyword k) { return k.ToString(); }

        bool PoolHasKeyword(CommanderDef cc, Keyword kw)
        {
            foreach (var col in cc.Colors)
                foreach (var c in _cat.PoolOf(col))
                    if (c.Keyword == kw) return true;
            return false;
        }

        bool AnyFilter()
        {
            return _typeFilter.Length > 0 || _elemFilter != Element.Divine || _costFilter != 0
                   || _kwFilter != Keyword.None || _fsFilter || _tagFilter.Length > 0 || _query.Length > 0;
        }

        void Chip(VisualElement parent, string label, bool on, System.Action onClick)
        {
            var b = UiKit.Btn(parent, label, onClick, 10.5f, on ? UiKit.Gold : UiKit.Dim);
            b.style.paddingLeft = 7f * UiKit.S; b.style.paddingRight = 7f * UiKit.S;
            b.style.paddingTop = 3f * UiKit.S; b.style.paddingBottom = 3f * UiKit.S;
            b.style.marginRight = 3f * UiKit.S;
            if (on) UiKit.Border(b, UiKit.Gold, 1.4f);
        }

        void RenderPoolGrid()
        {
            if (_poolGrid == null) return;
            _poolGrid.Clear();

            CommanderDef cc;
            if (!_cat.TryCommander(_deck.Commander, out cc)) return;

            var keys = DeckRules.PoolFor(_cat, cc);
            var shown = new List<string>();
            foreach (var key in keys) if (Matches(key)) shown.Add(key);
            shown.Sort(CompareKeys);

            if (_poolCount != null) _poolCount.text = shown.Count + " match" + (shown.Count == 1 ? "" : "es");

            var grid = UiKit.Box(_poolGrid);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            // a name can appear twice on a dual leader - the element disambiguates it
            var seen = new Dictionary<string, int>();
            foreach (var key in shown)
            {
                CardFaceModel m;
                if (!ModelOf(key, out m)) continue;
                int n;
                seen[m.Name] = seen.TryGetValue(m.Name, out n) ? n + 1 : 1;
            }

            foreach (var key in shown)
            {
                CardFaceModel m;
                if (!ModelOf(key, out m)) continue;
                int n;
                bool dupe = seen.TryGetValue(m.Name, out n) && n > 1;
                grid.Add(Tile(key, m, dupe));
            }
        }

        VisualElement Tile(string key, CardFaceModel m, bool disambiguate)
        {
            var colour = GlobeView.ElementColour(m.Element);
            var tile = UiKit.Box(null);
            tile.style.width = 112f * UiKit.S;
            tile.style.marginRight = 5f * UiKit.S;
            tile.style.marginBottom = 5f * UiKit.S;
            tile.style.backgroundColor = new Color(colour.r * 0.16f, colour.g * 0.16f, colour.b * 0.16f, 0.95f);
            UiKit.Border(tile, _selected == key ? UiKit.Gold : new Color(colour.r, colour.g, colour.b, 0.55f), 1.4f);
            UiKit.Radius(tile, 4f);

            if (m.Art != null)
            {
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.style.height = 52f * UiKit.S;
                art.style.backgroundImage = new StyleBackground(m.Art);
                art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                tile.Add(art);
            }

            var head = UiKit.Row(tile);
            head.style.paddingLeft = 4f * UiKit.S; head.style.paddingRight = 4f * UiKit.S;
            head.style.paddingTop = 2f * UiKit.S;
            UiKit.Text(head, "◆" + m.Cost, 10.5f, UiFont.DisplayBlack, colour)
                .style.marginRight = 4f * UiKit.S;
            var nm = UiKit.Text(head, disambiguate ? m.Name + " (" + CampaignRules(m.Element) + ")" : m.Name,
                                10f, UiFont.DisplayBold, UiKit.Ink);
            nm.style.flexGrow = 1f;

            var foot = UiKit.Row(tile);
            foot.style.paddingLeft = 4f * UiKit.S; foot.style.paddingRight = 4f * UiKit.S;
            foot.style.paddingBottom = 3f * UiKit.S;
            foot.style.justifyContent = Justify.SpaceBetween;
            UiKit.Text(foot, m.ShowStats ? Stat.Line(m.Attack, m.MaxHp) : m.Ribbon,
                       9.5f, UiFont.BodyRegular, UiKit.Dim);

            int have = _deck.CountOf(key);
            var count = UiKit.Text(foot, have > 0 ? "×" + have : "", 10.5f, UiFont.DisplayBold, UiKit.Gold);
            count.style.marginLeft = 4f * UiKit.S;

            tile.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_selected == key) Bump(key, +1);
                else { _selected = key; RenderDetail(); RenderPoolGrid(); }
                e.StopPropagation();
            });
            return tile;
        }

        static string CampaignRules(Element el)
        {
            return SpawnRowDuel.Campaign.CampaignRules.Name(el);
        }

        // ── matching and sorting ────────────────────────────────────────────────────────

        bool Matches(string key)
        {
            CardFaceModel m;
            if (!ModelOf(key, out m)) return false;

            if (_typeFilter == "creature" && m.Kind != CardKindFace.Creature) return false;
            if (_typeFilter == "spell" && m.Kind == CardKindFace.Creature) return false;
            if (_elemFilter != Element.Divine && m.Element != _elemFilter) return false;
            if (_costFilter > 0)
            {
                if (_costFilter == 6 ? m.Cost < 6 : m.Cost != _costFilter) return false;
            }

            CreatureCard c = null;
            _cat.TryCreature(new CardId(m.Name), out c);

            if (_fsFilter && (c == null || !c.FirstStrike)) return false;
            if (_kwFilter != Keyword.None && (c == null || c.Keyword != _kwFilter)) return false;
            if (_tagFilter.Length > 0)
            {
                if (c == null) return false;
                string tags = c.Tribe + " " + c.Subtype;
                if (tags.ToLowerInvariant().IndexOf(_tagFilter.ToLowerInvariant(), System.StringComparison.Ordinal) < 0)
                    return false;
            }

            if (_query.Length > 0)
            {
                var hay = m.Name + " " + m.TypeLine + " " + CampaignRules(m.Element) + " "
                          + (c != null ? c.Keyword + " " + c.Tribe + " " + c.Subtype
                                         + (c.FirstStrike ? " first strike" : "") : "spell trap");
                if (hay.ToLowerInvariant().IndexOf(_query, System.StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }

        int CompareKeys(string a, string b)
        {
            CardFaceModel ma, mb;
            if (!ModelOf(a, out ma) || !ModelOf(b, out mb)) return string.CompareOrdinal(a, b);

            switch (_sort)
            {
                case "cost":
                    if (ma.Cost != mb.Cost) return ma.Cost - mb.Cost;
                    break;
                case "costdesc":
                    if (ma.Cost != mb.Cost) return mb.Cost - ma.Cost;
                    break;
                case "name":
                    break;
                case "atk":
                    if (ma.Attack != mb.Attack) return mb.Attack - ma.Attack;
                    break;
                default:
                    int ra = ma.Kind == CardKindFace.Creature ? 0 : 2;
                    int rb = mb.Kind == CardKindFace.Creature ? 0 : 2;
                    if (ra != rb) return ra - rb;
                    if (ma.Cost != mb.Cost) return ma.Cost - mb.Cost;
                    break;
            }
            return string.Compare(ma.Name, mb.Name, System.StringComparison.Ordinal);
        }

        bool ModelOf(string key, out CardFaceModel model)
        {
            model = default(CardFaceModel);
            Element el; string name;
            if (!DeckRules.Split(key, out el, out name)) return false;

            CreatureCard c;
            if (_cat.TryCreature(new CardId(name), out c))
            {
                model = CardFaceModel.OfCreature(c, _text, _art);
                return true;
            }
            SpellCard s;
            if (_cat.TrySpell(new CardId(name), out s))
            {
                model = CardFaceModel.OfSpell(s, _text, _art);
                return true;
            }
            return false;
        }

        ElementDef ElementOf(Element el)
        {
            foreach (var d in _cat.Elements) if (d.El == el) return d;
            return null;
        }

        // ── leaders, saving, duelling ───────────────────────────────────────────────────

        void OpenLeaderPicker()
        {
            var scrim = UiKit.Scrim(_root, null);
            var box = UiKit.Glass(scrim, 14f);
            box.style.width = 460f * UiKit.S;
            box.style.maxHeight = Screen.height * 0.8f;
            UiKit.Radius(box, 8f);

            UiKit.Text(box, "Choose a leader", 17f, UiFont.DisplayBlack, UiKit.Gold);
            UiKit.Text(box, "Changing leader drops any card that is now off-colour.",
                       11f, UiFont.BodyItalic, UiKit.Dim).style.marginTop = 3f * UiKit.S;

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1f;
            list.style.marginTop = 8f * UiKit.S;
            box.Add(list);

            var solo = UiKit.Box(list);
            UiKit.Text(solo, "Solo", 12f, UiFont.DisplayBold, UiKit.Dim);
            var dual = UiKit.Box(list);
            UiKit.Text(dual, "Compacts", 12f, UiFont.DisplayBold, UiKit.Dim).style.marginTop = 8f * UiKit.S;

            foreach (var cc in _cat.Commanders)
            {
                var target = cc.Dual ? dual : solo;
                var row = UiKit.Row(target);
                var id = cc.Id;
                var b = UiKit.Btn(row, "", () => { PickLeader(id); scrim.RemoveFromHierarchy(); }, 12f);
                b.style.flexGrow = 1f;
                b.style.flexDirection = FlexDirection.Row;
                b.style.justifyContent = Justify.FlexStart;
                b.text = "";
                foreach (var col in cc.Colors)
                {
                    var d = ElementOf(col);
                    UiKit.Badge(b, d != null ? d.Glyph : "", GlobeView.ElementColour(col), 17f)
                        .style.marginRight = 3f * UiKit.S;
                }
                UiKit.Text(b, cc.Name + "  ♥" + cc.Hp + " · ⚒ " + cc.Workers, 12f, UiFont.BodyRegular, UiKit.Ink)
                    .style.marginLeft = 4f * UiKit.S;
            }

            UiKit.Btn(box, "Cancel", () => scrim.RemoveFromHierarchy(), 12f, UiKit.Dim)
                .style.marginTop = 8f * UiKit.S;
        }

        void PickLeader(CommanderId id)
        {
            _deck.Commander = id;

            CommanderDef cc;
            if (_cat.TryCommander(id, out cc))
            {
                var legal = new HashSet<string>(DeckRules.PoolFor(_cat, cc));
                var drop = new List<string>();
                foreach (var kv in _deck.Cards) if (!legal.Contains(kv.Key)) drop.Add(kv.Key);
                foreach (var key in drop) _deck.Cards.Remove(key);
            }

            _elemFilter = Element.Divine;
            _kwFilter = Keyword.None;
            _fsFilter = false;
            _selected = null;
            Render();
        }

        void SaveDeck()
        {
            if (string.IsNullOrEmpty(_deck.Name)) { RefreshCounter(); return; }
            if (!DeckRules.IsLegal(_cat, _deck)) { RefreshCounter(); return; }

            if (_editIndex >= 0 && _editIndex < _saved.Count) _saved[_editIndex] = _deck.Clone();
            else if (_saved.Count < DeckRules.MaxDecks) { _saved.Add(_deck.Clone()); _editIndex = _saved.Count - 1; }
            else { RefreshCounter(); return; }

            DeckStore.Save(_saved);
            _hint.text = "Saved.";
        }

        void OpenLoad()
        {
            var scrim = UiKit.Scrim(_root, null);
            var box = UiKit.Glass(scrim, 14f);
            box.style.width = 360f * UiKit.S;
            UiKit.Radius(box, 8f);
            UiKit.Text(box, "Your decks", 17f, UiFont.DisplayBlack, UiKit.Gold);

            if (_saved.Count == 0)
                UiKit.Text(box, "None saved yet.", 12f, UiFont.BodyItalic, UiKit.Dim)
                    .style.marginTop = 6f * UiKit.S;

            for (int i = 0; i < _saved.Count; i++)
            {
                int index = i;
                var d = _saved[i];
                var row = UiKit.Row(box);
                row.style.marginTop = 4f * UiKit.S;

                var open = UiKit.Btn(row, d.Name + "  (" + d.Total + ")", () =>
                {
                    _deck = _saved[index].Clone();
                    _editIndex = index;
                    _selected = null;
                    scrim.RemoveFromHierarchy();
                    Render();
                }, 12f);
                open.style.flexGrow = 1f;

                UiKit.Btn(row, "✕", () =>
                {
                    _saved.RemoveAt(index);
                    DeckStore.Save(_saved);
                    scrim.RemoveFromHierarchy();
                    OpenLoad();
                }, 11f, UiKit.Danger).style.marginLeft = 5f * UiKit.S;
            }

            UiKit.Btn(box, "New deck", () =>
            {
                _deck = new SavedDeck { Commander = new CommanderId("fire") };
                _editIndex = -1;
                _selected = null;
                scrim.RemoveFromHierarchy();
                Render();
            }, 12f).style.marginTop = 8f * UiKit.S;

            UiKit.Btn(box, "Close", () => scrim.RemoveFromHierarchy(), 12f, UiKit.Dim);
        }

        /// <summary>
        /// Take the deck straight into a duel. The JS could not do this from the campaign and only
        /// half did it from solo; a deck you cannot play is a spreadsheet.
        /// </summary>
        void DuelWithDeck()
        {
            if (!DeckRules.IsLegal(_cat, _deck)) { RefreshCounter(); return; }

            var pile = DeckRules.ToDrawPile(_cat, _deck, new Pcg32((ulong)Random.Range(1, int.MaxValue)));

            var foes = _cat.Commanders;
            var foe = foes[Random.Range(0, foes.Count)].Id;
            _shell.StartSkirmish(_deck.Commander, foe, pile);
        }
    }
}
