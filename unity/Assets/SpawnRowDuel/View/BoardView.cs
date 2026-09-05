using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Builds the board at runtime FROM the rules geometry, so the view can never drift out of
    /// agreement with the engine: if Board says a cell is not creature-standable, no cell object
    /// is created for it. Placeholder visuals - the real standee/tile work is milestone 13.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("Materials (assigned by SceneBootstrap)")]
        public Material CellMaterial;
        public Material LaneMaterial;
        public Material StructureSlotMaterial;
        public Material HoverMaterial;

        /// <summary>Worker pawns. Opaque, unlike the tiles - a worker is a figure standing on the
        /// ground, and it was borrowing the tile's translucent wash and reading as a smear.</summary>
        public Material PawnMaterial;
        public Material SelectMaterial;

        [Header("Row tints (assigned by SceneBootstrap; null falls back to CellMaterial)")]
        public Material FoeBackMaterial;
        public Material FoeFrontMaterial;
        public Material YouFrontMaterial;
        public Material YouBackMaterial;

        [Header("Layout")]
        public float CellSize = 1f;
        public float CellGap = 0.08f;

        /// <summary>
        /// How much DEEPER a row is than a column is wide.
        ///
        /// Square cells are the obvious choice and they are the wrong one, because the board is
        /// not seen square-on: at the tilted angle depth is foreshortened by sin(42°) ≈ 0.67, so
        /// square ground cells project as tiles twice as wide as they are tall, and a 7x5 board of
        /// them is a letterbox that can only ever fill a fraction of the screen's height. The
        /// picture is width-limited - the fit is decided at the near corners - so the slack was
        /// always vertical, and stretching the rows is what spends it: the board reaches from one
        /// wall to the other, and a cell reads as a square because on screen it now is one.
        /// </summary>
        public float RowStretch = 1.45f;

        public float ColPitch { get { return CellSize + CellGap; } }
        public float RowPitch { get { return (CellSize + CellGap) * RowStretch; } }

        /// <summary>
        /// How thick a cell is. Almost nothing: a cell is a MARKING on the terrain, not a slab.
        ///
        /// It was 0.12 and solid, and that is what made the whole scene read at the wrong scale -
        /// a creature on a raised plinth beside knee-high grass is a figurine on a table, not an
        /// army in a field. The box is still a box only because its collider is how BoardInput
        /// picks cells; the visible part is a translucent wash (SRD_Tile) that the terrain shows
        /// through and the grass grows up past.
        /// </summary>
        public const float CellThickness = 0.02f;

        /// <summary>
        /// Warm for your ground, cold for theirs, and darker at the back of each half.
        ///
        /// Keyed on the SEAT, not on the RowKey. This is the one seat-sensitive site with no
        /// Side.You in it to find: a mechanical sweep for "Side.You meaning me" walks straight
        /// past a switch over row names. Get it wrong and the guest sees the cold enemy wash on
        /// the two rows nearest them - their own deploy rows - which inverts the one channel that
        /// tells a player whose ground they are looking at.
        /// </summary>
        Material RowMaterial(RowKey row)
        {
            if (row == RowKey.Center) return null;

            bool youSide = row == RowKey.YouFront || row == RowKey.YouBack;
            bool mine = youSide == (Seat.Local == Side.You);
            bool back = row == RowKey.YouBack || row == RowKey.FoeBack;

            return mine ? (back ? YouBackMaterial : YouFrontMaterial)
                        : (back ? FoeBackMaterial : FoeFrontMaterial);
        }

        /// <summary>
        /// Re-tint every row for the current seat. The board is built at Awake, before anyone has
        /// said which end we are sitting at, so the seat is applied when the match starts.
        /// </summary>
        public void ApplySeat()
        {
            foreach (var kv in _cells)
            {
                var cell = kv.Key;
                if (cell.Row == RowKey.Center) continue;

                var m = Runtime(RowMaterial(cell.Row));
                if (m == null) continue;

                _restMaterials[cell] = m;
                var mr = kv.Value.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = m;
            }

            ApplyOverlay();
        }

        // ── the board overlay ─────────────────────────────────────────────────────────────

        /// <summary>How much of the board's own marking is drawn.</summary>
        public enum BoardOverlay
        {
            Colour = 0,     // warm your half, cold theirs, amber centre - the full wash
            DarkTint = 1,   // one neutral shade, no lines: the board as a shadow on the field
            Grid = 2,       // no fill, both sets of lines
            RowLines = 3,   // no fill, and only the lines that divide one ROW from the next
            Off = 4,        // nothing at rest
        }

        /// <summary>
        /// Static, like the figures toggle and the biome: every cell reads it and threading it
        /// through would buy nothing. It is a PRESENTATION choice and never a rule - the cells,
        /// their colliders and every legality probe are identical in all five modes.
        ///
        /// OFF by default. The wash was the loudest thing on the screen and it is telling the
        /// player something they can read off the board anyway: their own cards face them, the
        /// figures are tinted by owner, and the walls at each edge carry the two life pools. A
        /// painted grid over a field is a board game drawn on grass, and the point of the terrain
        /// is that it is ground. Nothing is lost that a player needs, because a LIT cell still
        /// lights in every mode - Paint re-enables the renderer it paints - and the lit cells are
        /// the engine's own answer to "where may this go".
        /// </summary>
        public static BoardOverlay Overlay = BoardOverlay.Off;

        public static string OverlayName(BoardOverlay o)
        {
            switch (o)
            {
                case BoardOverlay.DarkTint: return "DARK";
                case BoardOverlay.Grid: return "GRID";
                case BoardOverlay.RowLines: return "ROWS";
                case BoardOverlay.Off: return "OFF";
                default: return "COLOUR";
            }
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int EdgeAxisId = Shader.PropertyToID("_EdgeAxis");

        BoardOverlay _appliedOverlay = (BoardOverlay)(-1);

        /// <summary>
        /// The materials the cells actually wear.
        ///
        /// Runtime COPIES of the assigned assets, because the overlay is applied by writing colours
        /// onto them: mutating the assigned material would edit the project asset itself, and in
        /// the editor that is a change to a file nobody asked to change. The originals are kept so
        /// COLOUR can be restored exactly rather than approximately.
        /// </summary>
        readonly Dictionary<Material, Material> _runtime = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Color> _baseOf = new Dictionary<Material, Color>();
        readonly Dictionary<Material, Color> _edgeOf = new Dictionary<Material, Color>();

        Material Runtime(Material src)
        {
            if (src == null) return null;

            Material copy;
            if (_runtime.TryGetValue(src, out copy) && copy != null) return copy;

            copy = new Material(src) { name = src.name + " (board)", hideFlags = HideFlags.DontSave };
            _runtime[src] = copy;
            if (copy.HasProperty(BaseColorId)) _baseOf[copy] = copy.GetColor(BaseColorId);
            if (copy.HasProperty(EdgeColorId)) _edgeOf[copy] = copy.GetColor(EdgeColorId);
            return copy;
        }

        /// <summary>
        /// Write the current overlay onto every cell material, and switch the renderers off
        /// entirely for Off.
        ///
        /// A HIGHLIGHT still draws in every mode, Off included - Paint() re-enables the renderer it
        /// paints. That is not an exception to the setting, it is the point of it: the wash is
        /// decoration and can go, but the lit cells ARE the engine's answer to "where may this go",
        /// and a player who cannot see them cannot play.
        /// </summary>
        public void ApplyOverlay()
        {
            _appliedOverlay = Overlay;

            foreach (var kv in _runtime)
            {
                var m = kv.Value;
                if (m == null) continue;

                Color fill = _baseOf.ContainsKey(m) ? _baseOf[m] : Color.clear;
                Color edge = _edgeOf.ContainsKey(m) ? _edgeOf[m] : Color.clear;
                var axis = new Vector4(1f, 1f, 0f, 0f);

                switch (Overlay)
                {
                    case BoardOverlay.DarkTint:
                        // one shade for every row, at the wash's own weight, and no rim
                        fill = new Color(0.02f, 0.025f, 0.04f, fill.a * 0.9f);
                        edge.a = 0f;
                        break;

                    case BoardOverlay.Grid:
                        fill.a = 0f;
                        break;

                    case BoardOverlay.RowLines:
                        fill.a = 0f;
                        // v runs along the row's DEPTH, so its edges are the row boundaries
                        axis = new Vector4(0f, 1f, 0f, 0f);
                        break;

                    case BoardOverlay.Off:
                        break;                      // handled by the renderers, below
                }

                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, fill);
                if (m.HasProperty(EdgeColorId)) m.SetColor(EdgeColorId, edge);
                if (m.HasProperty(EdgeAxisId)) m.SetVector(EdgeAxisId, axis);
            }

            bool draw = Overlay != BoardOverlay.Off;
            foreach (var kv in _cells)
            {
                var mr = kv.Value.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                // a cell wearing a highlight keeps drawing; only the RESTING wash is switched off
                Material rest;
                bool resting = _restMaterials.TryGetValue(kv.Key, out rest)
                            && ReferenceEquals(mr.sharedMaterial, rest);
                mr.enabled = draw || !resting;
            }
        }

        void Update()
        {
            if (Overlay != _appliedOverlay) ApplyOverlay();
        }

        private readonly Dictionary<CellRef, Transform> _cells = new Dictionary<CellRef, Transform>();
        private readonly Dictionary<CellRef, Material> _restMaterials = new Dictionary<CellRef, Material>();

        public IReadOnlyDictionary<CellRef, Transform> Cells { get { return _cells; } }

        void Awake()
        {
            Build();
        }

        /// <summary>Cells that hold creatures. Center flanks are excluded.</summary>
        public int CreatureSlotCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _cells)
                    if (Board.IsRealSlot(kv.Key.Row, kv.Key.Col)) n++;
                return n;
            }
        }

        public Vector3 WorldOf(CellRef cell)
        {
            float x = (cell.Col - (Board.Columns - 1) / 2f) * ColPitch;
            float z = ((Board.Rows - 1) / 2f - (int)cell.Row) * RowPitch;
            return new Vector3(x, 0f, z);
        }

        public void Build()
        {
            foreach (var row in Board.AllRows)
            {
                var rowGo = new GameObject(row.ToString());
                rowGo.transform.SetParent(transform, false);

                for (int c = 0; c < Board.Columns; c++)
                {
                    // The center row's non-lane columns are structure ground, not creature slots.
                    bool creatureSlot = Board.IsRealSlot(row, c);
                    bool structureSlot = row == RowKey.Center && !creatureSlot;
                    if (!creatureSlot && !structureSlot) continue;

                    var cell = new CellRef(row, c);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = cell.ToString();
                    go.transform.SetParent(rowGo.transform, false);
                    go.transform.localPosition = WorldOf(cell);
                    go.transform.localScale = new Vector3(CellSize, CellThickness, CellSize * RowStretch);

                    // Rows are TINTED BY OWNER, as the reference board is: the foe's half reads
                    // cold, yours warm, the contested centre amber. Colour is doing real work
                    // here - "whose ground is this" decides where you may deploy and what a raid
                    // means, and a uniform grey board makes a player count rows to find out.
                    Material m = Runtime(structureSlot ? StructureSlotMaterial
                               : (row == RowKey.Center ? LaneMaterial
                               : (RowMaterial(row) != null ? RowMaterial(row) : CellMaterial)));

                    go.GetComponent<MeshRenderer>().sharedMaterial = m;

                    _cells[cell] = go.transform;
                    _restMaterials[cell] = m;
                }
            }

            // NO WALL SLABS. The two life targets used to be red boxes lying on the grass past
            // each back row, and a wall on the FIELD is a wall the field has to make room for -
            // it cost the board a row's depth at each end and left the play area floating in the
            // middle of the screen with weather on either side of it.
            //
            // A castle wall is not a thing on the ground you fight over; it is the edge of the
            // world you are fighting in front of. So it moved to the screen edge: the retracted
            // battlements are the HUD bands themselves (WallBands), and the field now runs from
            // one of them to the other.
        }

        public bool TryCellOf(Transform t, out CellRef cell)
        {
            foreach (var kv in _cells)
            {
                if (kv.Value == t) { cell = kv.Key; return true; }
            }
            cell = default(CellRef);
            return false;
        }

        /// <summary>
        /// Light a cell. The renderer is switched back ON whatever the overlay setting is: the lit
        /// cells are the engine's own answer to "where may this go", and OFF is a choice about
        /// DECORATION, not about being able to see the game.
        /// </summary>
        public void Paint(CellRef cell, Material m)
        {
            Transform t;
            if (!_cells.TryGetValue(cell, out t)) return;

            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = m;
            mr.enabled = true;
        }

        public void Restore(CellRef cell)
        {
            Material m;
            if (!_restMaterials.TryGetValue(cell, out m)) return;

            Transform t;
            if (!_cells.TryGetValue(cell, out t)) return;

            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = m;
            mr.enabled = Overlay != BoardOverlay.Off;      // back to whatever the setting says
        }
    }
}
