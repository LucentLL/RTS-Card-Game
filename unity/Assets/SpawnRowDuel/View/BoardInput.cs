using System;
using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Placeholder interaction layer: raycast cell picking, adjacency preview, and the two camera
    /// angle presets (Top-Down / Tilted) that the board has always had.
    ///
    /// This deliberately replaces the browser's CSS elementFromPoint hit-testing with a real
    /// physics raycast - which is the whole reason the port went to genuine 3D. The proper input
    /// stack (Input System, marquee group-select, drag-drop summon) is milestone 9; this exists so
    /// the deployed build is something you can actually poke at.
    ///
    /// Uses legacy Input because activeInputHandler is set to Both. IMGUI is used for the readout
    /// on purpose: it needs no font asset, and the project has no TMP fallback chain yet, so
    /// anything else would render tofu.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public class BoardInput : MonoBehaviour
    {
        public Camera Cam;

        private BoardView _board;
        private CellRef? _hover;
        private CellRef? _selected;
        private readonly List<CellRef> _highlighted = new List<CellRef>();

        // Angle presets. "Tilted" is the signature diorama look; Top-Down is the other.
        private static readonly Vector3 TiltedPos = new Vector3(0f, 6.4f, -6.9f);
        private static readonly Vector3 TiltedRot = new Vector3(42f, 0f, 0f);
        private static readonly Vector3 TopDownPos = new Vector3(0f, 9.2f, -0.6f);
        private static readonly Vector3 TopDownRot = new Vector3(84f, 0f, 0f);

        private bool _tilted = true;
        private float _blend = 1f;

        void Awake()
        {
            _board = GetComponent<BoardView>();
            if (Cam == null) Cam = Camera.main;
        }

        void Update()
        {
            UpdateCamera();
            UpdateHover();

            if (Input.GetMouseButtonDown(0)) Select(_hover);
            if (Input.GetMouseButtonDown(1)) Select(null);
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Space)) _tilted = !_tilted;
        }

        void UpdateCamera()
        {
            if (Cam == null) return;
            float target = _tilted ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime * 2.6f);
            float t = Mathf.SmoothStep(0f, 1f, _blend);
            Cam.transform.position = Vector3.Lerp(TopDownPos, TiltedPos, t);
            Cam.transform.rotation = Quaternion.Euler(Vector3.Lerp(TopDownRot, TiltedRot, t));
        }

        void UpdateHover()
        {
            CellRef? found = null;
            if (Cam != null)
            {
                RaycastHit hit;
                var ray = Cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out hit, 100f))
                {
                    CellRef c;
                    if (_board.TryCellOf(hit.transform, out c)) found = c;
                }
            }

            if (Nullable.Equals(found, _hover)) return;

            if (_hover.HasValue && !IsHighlighted(_hover.Value) && !IsSelected(_hover.Value))
                _board.Restore(_hover.Value);

            _hover = found;

            if (_hover.HasValue && !IsSelected(_hover.Value))
                _board.Paint(_hover.Value, _board.HoverMaterial);
        }

        bool IsSelected(CellRef c) { return _selected.HasValue && _selected.Value == c; }
        bool IsHighlighted(CellRef c) { return _highlighted.Contains(c); }

        void Select(CellRef? cell)
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();
            if (_selected.HasValue) _board.Restore(_selected.Value);

            _selected = cell;
            if (!_selected.HasValue) return;

            _board.Paint(_selected.Value, _board.SelectMaterial);

            // Show exactly what the rules engine says is one step away. This is Board.Neighbours,
            // not a view-side reimplementation - the picture cannot disagree with the rules.
            Span<CellRef> buf = stackalloc CellRef[8];
            int n = Board.Neighbours(_selected.Value, buf);
            for (int i = 0; i < n; i++)
            {
                _highlighted.Add(buf[i]);
                _board.Paint(buf[i], _board.HoverMaterial);
            }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;

            GUI.Box(new Rect(10, 10, 340, 132), GUIContent.none);
            var y = 16f;
            Action<string> line = s => { GUI.Label(new Rect(20, y, 330, 20), s, style); y += 19f; };

            line("Spawn Row Duel - board scaffold");
            line("angle: " + (_tilted ? "Tilted (diorama)" : "Top-Down") + "   [Tab/Space]");
            line("hover: " + (_hover.HasValue ? _hover.Value.ToString() : "-"));
            line("selected: " + (_selected.HasValue ? _selected.Value.ToString() : "-")
                 + (_selected.HasValue ? "   neighbours: " + _highlighted.Count : ""));
            line("left-click select, right-click clear");
            line("placeholder - no cards or rules wired up yet");
        }
    }
}
