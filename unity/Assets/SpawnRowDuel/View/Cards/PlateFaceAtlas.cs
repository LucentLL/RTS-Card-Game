using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// THE REAL CARD FACE, on a texture, so the card lying on a tile is the card that was in your
    /// hand - not a drawing of one.
    ///
    /// The plate used to be a hand-rolled raster: a 3x5 bitmap alphabet for the name, a hammer
    /// drawn out of two rectangles for the worker mark, an ability line spelled in capitals with
    /// the punctuation stripped. It was a reasonable answer to a real constraint - a plate is a
    /// world-space sprite and UI Toolkit does not go there - and it was still a second card frame,
    /// drifting from the first one glyph at a time. Twice asked for and this is the ask: "they
    /// should look exactly like cards in hand".
    ///
    /// So the constraint is removed rather than worked around. A PanelSettings with a
    /// targetTexture renders a UI Toolkit tree into a RenderTexture, and a RenderTexture is a
    /// texture like any other - a quad on a tile can sample it. One panel holds a GRID of real
    /// <see cref="CardFace"/> elements, each unit on the board owns a cell, and its plate is a
    /// quad whose UVs are that cell. Every card on the screen is now the same class with the same
    /// fonts, the same ⚒ glyph and the same generated ability line, laid out once.
    ///
    /// ONE panel and one texture rather than one per card, because a panel is not free and a board
    /// can hold thirty-five units. The sheet has a cell for every square on the board and one
    /// spare, so it cannot run out; the rastered frame it falls back to survives only for the case
    /// where the panel cannot be built at all - a stripped shader, a missing PanelSettings - where
    /// degrading to the old look beats a blank board on a platform this cannot be tested on from
    /// here. The face-DOWN sleeve is still rastered either way: a card back is not a card face.
    /// </summary>
    public sealed class PlateFaceAtlas : MonoBehaviour
    {
        /// <summary>
        /// One cell, in texels. A plate is about 110 screen pixels tall at 1600x900 and about 250
        /// on a 4K display, so 192 x 266 is crisp on the first and near enough 1 : 1 on the second.
        /// Bigger costs memory in squares: the whole sheet is Cols x Rows of these.
        /// </summary>
        public const int CellW = 192;
        public static readonly int CellH = Mathf.RoundToInt(CellW * CardFace.Aspect);

        /// <summary>
        /// BIGGER THAN THE BOARD, on purpose. A cell is claimed by a face-up unit standing on a
        /// square, there are Board.Cells = 35 squares, and 6 x 6 is 36 - so "the sheet is full"
        /// is a state the rules cannot reach, and nobody ever sees the rastered frame because the
        /// board got busy. It is still reachable if the panel itself cannot be built, which is the
        /// case the fallback is actually for.
        /// </summary>
        public const int Cols = 6, Rows = 6;
        public const int Cells = Cols * Rows;

        /// <summary>The sheet, once. Every plate quad samples it and only its UVs differ, so the
        /// whole board is one material and one texture.</summary>
        public Material Sheet { get; private set; }

        public bool Ready { get { return Sheet != null && _rt != null && _rt.IsCreated(); } }

        RenderTexture _rt;
        PanelSettings _panel;
        UIDocument _doc;
        GameObject _host;

        readonly CardFace[] _faces = new CardFace[Cells];
        readonly string[] _keys = new string[Cells];
        readonly int[] _owner = new int[Cells];
        readonly Dictionary<int, int> _cellOf = new Dictionary<int, int>();
        readonly List<int> _evicted = new List<int>();

        bool _failed;

        void Awake()
        {
            for (int i = 0; i < Cells; i++) _owner[i] = Free;
        }

        const int Free = int.MinValue;

        /// <summary>The sheet follows the board on and off. It is a root object (see Ensure), so
        /// nothing else would switch it off when the shell leaves the duel, and a panel repainting
        /// into a texture nobody samples is work for no picture.</summary>
        void OnEnable() { if (_host != null) _host.SetActive(true); }
        void OnDisable() { if (_host != null) _host.SetActive(false); }

        void OnDestroy()
        {
            if (_host != null) Destroy(_host);          // root-parented: nothing else takes it
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_panel != null) Destroy(_panel);
            if (Sheet != null) Destroy(Sheet);
        }

        /// <summary>
        /// Build the sheet, the panel and the document, once - and rebuild whatever the shell tore
        /// down. Returns false for good once anything is missing, so a broken rig costs one log
        /// line rather than one per frame forever.
        /// </summary>
        bool Ensure()
        {
            if (_failed) return false;
            if (Ready && _doc != null && _faces[0] != null && _faces[0].panel != null) return true;

            if (_rt == null)
            {
                // 24 BITS OF DEPTH, which is where the STENCIL lives - and UI Toolkit clips with
                // the stencil buffer. `overflow: hidden` is on the card's root, its art window, its
                // name column and its ability box, so a target with no stencil attachment does not
                // merely lose the clipping: every masked subtree drops out. The first version of
                // this asked for 0 and got thirty blank white cards with an element-coloured border
                // - the card's outermost box and nothing inside it.
                _rt = new RenderTexture(Cols * CellW, Rows * CellH, 24, RenderTextureFormat.ARGB32)
                {
                    name = "SRD Card Sheet",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    autoGenerateMips = false,
                };
                if (!_rt.Create()) return Fail("the card sheet render texture would not create");
            }

            if (_panel == null)
            {
                // INSTANTIATED FROM THE COMMITTED ASSET, never CreateInstance. A PanelSettings
                // built at runtime looks its UI shaders up by name, and the WebGL stripper deletes
                // every shader nothing serialized points at - the same trap HandBar.EnsurePanel
                // documents. A clone of the asset carries the asset's own references.
                var src = Resources.Load<PanelSettings>(HandBar.PanelResource);
                if (src == null) return Fail("HudPanelSettings is missing - run tools/regen-fonts.sh");

                _panel = Instantiate(src);
                _panel.name = "SRD Card Sheet Panel";
                _panel.hideFlags = HideFlags.HideAndDontSave;
                _panel.targetTexture = _rt;
                _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                _panel.scale = 1f;
                _panel.clearColor = true;
                _panel.colorClearValue = new Color(0f, 0f, 0f, 0f);
                _panel.sortingOrder = -100;      // never over the HUD, whatever else changes
            }

            if (Sheet == null)
            {
                var mat = SpriteMat.Unlit;
                if (mat == null) return Fail("Sprites/Default was stripped - no card sheet");
                Sheet = new Material(mat) { name = "SRD Card Sheet", hideFlags = HideFlags.HideAndDontSave };
                // BY NAME. Sprites/Default marks _MainTex [PerRendererData], which a SpriteRenderer
                // fills from a per-renderer block and a MeshRenderer does not fill at all, so the
                // slot has to be set on the material itself.
                Sheet.SetTexture("_MainTex", _rt);
                Sheet.mainTexture = _rt;
                Sheet.color = Color.white;
            }

            // A ROOT object, and NOT a child of the board.
            //
            // It has to be its own GameObject, because the board already carries HandBar's
            // UIDocument and one GameObject holds one. But it must not be parented under it
            // either: a UIDocument whose GameObject descends from another one is a NESTED
            // document, and Unity asserts that a nested document carries its parent's
            // PanelSettings - "Expected: SRD Card Sheet Panel == HudPanelSettings", thrown from
            // the setter itself. A nested panel is exactly what this is not; it renders to a
            // texture, not into the HUD.
            //
            // Its lifetime is this component's: OnEnable/OnDisable mirror the board so the sheet
            // stops repainting when the shell switches the duel off, and OnDestroy takes it away.
            if (_host == null)
            {
                _host = new GameObject("SRD card sheet") { hideFlags = HideFlags.HideInHierarchy };
                _host.SetActive(isActiveAndEnabled);
            }

            if (_doc == null)
            {
                _doc = _host.AddComponent<UIDocument>();
                _doc.panelSettings = _panel;
            }

            var root = _doc.rootVisualElement;
            if (root == null || root.panel == null) return false;    // attached next frame

            // Whatever survived a teardown belongs to a panel that is gone.
            root.Clear();
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.width = Cols * CellW;
            root.style.height = Rows * CellH;
            root.pickingMode = PickingMode.Ignore;

            _cellOf.Clear();
            for (int i = 0; i < Cells; i++)
            {
                var face = new CardFace { pickingMode = PickingMode.Ignore };
                face.style.position = Position.Absolute;
                face.style.left = (i % Cols) * CellW;
                face.style.top = (i / Cols) * CellH;
                face.style.display = DisplayStyle.None;
                root.Add(face);

                _faces[i] = face;
                _keys[i] = null;
                _owner[i] = Free;
            }
            return true;
        }

        bool Fail(string why)
        {
            Debug.LogWarning(why + " - board cards fall back to the rastered frame");
            _failed = true;
            return false;
        }

        /// <summary>
        /// Give this unit a cell, painting the face into it when anything about the card has
        /// changed, and hand back the cell's UV rectangle.
        ///
        /// <paramref name="key"/> is what "changed" means - the caller builds it from everything
        /// the face prints. Binding a CardFace runs a layout and two text fitters, so doing it per
        /// frame for a board of cards is not on; doing it when a number moves is nothing.
        /// </summary>
        public bool TryBind(int id, CardFaceModel model, ElementPalette palette,
                            string key, out Rect uv)
        {
            uv = default(Rect);
            if (!Ensure()) return false;

            int cell;
            if (!_cellOf.TryGetValue(id, out cell))
            {
                cell = FreeCell();
                if (cell < 0) return false;              // sheet full: the caller rasters instead
                _cellOf[id] = cell;
                _owner[cell] = id;
                _keys[cell] = null;                      // a reused cell holds someone else's card
            }

            if (_keys[cell] != key)
            {
                _keys[cell] = key;

                // BOUND AT SCALE ONE. Every floor and ceiling in CardFace.Bind is expressed in
                // HudLayout.Scale, which is the SCREEN's - "never smaller than this looks on a
                // 480-tall reference screen". A cell is not a screen: it is 192 texels wide
                // whatever the display is, so on a 4K monitor those floors would come out at four
                // times the size the card can hold and every ability box would be one shrunken
                // word. The sheet is a fixed surface, so it is laid out at the fixed scale.
                float was = HudLayout.Scale;
                HudLayout.Scale = 1f;
                try { _faces[cell].Bind(model, palette, CellW); }
                finally { HudLayout.Scale = was; }

                _faces[cell].style.display = DisplayStyle.Flex;
            }

            uv = UvOf(cell);
            return true;
        }

        public static Rect UvOf(int cell)
        {
            int c = cell % Cols, r = cell / Cols;
            float w = 1f / Cols, h = 1f / Rows;

            // UI Toolkit's origin is TOP left and a texture's is bottom left, so row 0 is the top
            // band of the sheet and its v runs from 1 - h to 1.
            return new Rect(c * w, 1f - (r + 1) * h, w, h);
        }

        /// <summary>Hand back the cells of units that are no longer on the board. A cell held by a
        /// dead unit is a cell the next summon cannot have.</summary>
        public void Sweep(HashSet<int> alive)
        {
            if (_cellOf.Count == 0) return;

            _evicted.Clear();
            foreach (var kv in _cellOf)
                if (!alive.Contains(kv.Key)) _evicted.Add(kv.Key);

            for (int i = 0; i < _evicted.Count; i++)
            {
                int cell = _cellOf[_evicted[i]];
                _cellOf.Remove(_evicted[i]);
                _owner[cell] = Free;
                _keys[cell] = null;
                if (_faces[cell] != null) _faces[cell].style.display = DisplayStyle.None;
            }
        }

        int FreeCell()
        {
            for (int i = 0; i < Cells; i++) if (_owner[i] == Free) return i;
            return -1;
        }
    }
}
