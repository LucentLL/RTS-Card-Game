using System;
using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Placeholder interaction layer: raycast cell picking, adjacency preview, and the two
    /// camera angle presets (Top-Down / Tilted) the board has always had - now with the camera
    /// FIT to the viewport instead of parked at a fixed distance, so a portrait phone frames
    /// the whole board edge to edge instead of a letterboxed band.
    ///
    /// Uses legacy Input because activeInputHandler is Both; taps arrive as mouse events on
    /// WebGL. The proper input stack (marquee, drag-drop summon) is milestone 9 proper.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public class BoardInput : MonoBehaviour
    {
        public Camera Cam;

        private BoardView _board;
        private CellRef? _hover;
        private CellRef? _selected;
        private readonly List<CellRef> _highlighted = new List<CellRef>();

        public CellRef? Hover { get { return _hover; } }
        public CellRef? Selected { get { return _selected; } }

        // The two locked angles: Tilted is the signature diorama, Top-Down the flat read.
        private const float TiltedPitch = 42f;
        private const float TopDownPitch = 84f;

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

            float pitch = Mathf.Lerp(TopDownPitch, TiltedPitch, t);
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            float dist = FitDistance(rot);

            Cam.transform.rotation = rot;
            Cam.transform.position = -(rot * Vector3.forward) * dist;
        }

        /// <summary>
        /// The smallest camera distance that keeps every board extreme - four corners plus the
        /// two wall bars - inside the frustum, for the CURRENT aspect ratio. On a portrait
        /// phone the horizontal fit dominates and the board spans the full width; on a desktop
        /// the vertical fit leaves headroom for the HUD.
        /// </summary>
        float FitDistance(Quaternion rot)
        {
            float cellPitch = _board.CellSize + _board.CellGap;
            float halfW = Rules.Board.Columns * cellPitch * 0.5f + 0.25f;
            // walls sit at virtual rows -1 and 5: three row-pitches out from the center row
            float halfD = 3f * cellPitch + 0.45f;

            var extremes = new[]
            {
                new Vector3(-halfW, 0f, halfD), new Vector3(halfW, 0f, halfD),
                new Vector3(-halfW, 0f, -halfD), new Vector3(halfW, 0f, -halfD),
                new Vector3(0f, 0.45f, halfD), new Vector3(0f, 0.45f, -halfD),
            };

            float tanV = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Cam.aspect;

            // Margins: a touch of horizontal breathing room; more vertically, where the HUD
            // header and the action button live.
            float fitH = tanH * 0.94f;
            float fitV = tanV * 0.80f;

            var inv = Quaternion.Inverse(rot);
            float need = 2f;
            for (int i = 0; i < extremes.Length; i++)
            {
                var p = inv * extremes[i];       // camera-space direction, camera at distance d
                need = Mathf.Max(need, Mathf.Abs(p.x) / fitH - p.z);
                need = Mathf.Max(need, Mathf.Abs(p.y) / fitV - p.z);
            }
            return need * 1.02f;
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

            // Exactly what the rules engine says is one step away - Board.Neighbours, never a
            // view-side reimplementation, so the picture cannot disagree with the rules.
            Span<CellRef> buf = stackalloc CellRef[8];
            int n = Rules.Board.Neighbours(_selected.Value, buf);
            for (int i = 0; i < n; i++)
            {
                _highlighted.Add(buf[i]);
                _board.Paint(buf[i], _board.HoverMaterial);
            }
        }
    }
}
