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

        Material RowMaterial(RowKey row)
        {
            switch (row)
            {
                case RowKey.FoeBack: return FoeBackMaterial;
                case RowKey.FoeFront: return FoeFrontMaterial;
                case RowKey.YouFront: return YouFrontMaterial;
                case RowKey.YouBack: return YouBackMaterial;
                default: return null;
            }
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
                    Material m = structureSlot ? StructureSlotMaterial
                               : (row == RowKey.Center ? LaneMaterial
                               : (RowMaterial(row) != null ? RowMaterial(row) : CellMaterial));

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

        public void Paint(CellRef cell, Material m)
        {
            Transform t;
            if (_cells.TryGetValue(cell, out t))
                t.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        public void Restore(CellRef cell)
        {
            Material m;
            if (_restMaterials.TryGetValue(cell, out m)) Paint(cell, m);
        }
    }
}
