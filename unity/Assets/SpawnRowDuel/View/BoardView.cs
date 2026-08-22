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
        public Material WallMaterial;
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
            float pitch = CellSize + CellGap;
            float x = (cell.Col - (Board.Columns - 1) / 2f) * pitch;
            float z = ((Board.Rows - 1) / 2f - (int)cell.Row) * pitch;
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
                    go.transform.localScale = new Vector3(CellSize, CellThickness, CellSize);

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

            BuildWall(Board.FoeWallRow, "Wall_Foe");
            BuildWall(Board.YouWallRow, "Wall_You");
        }

        void BuildWall(int virtualRow, string name)
        {
            float pitch = CellSize + CellGap;
            float z = ((Board.Rows - 1) / 2f - virtualRow) * pitch;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = new Vector3(0f, 0.2f, z);
            wall.transform.localScale = new Vector3(Board.Columns * pitch * 0.98f, 0.4f, 0.3f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = WallMaterial;
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
