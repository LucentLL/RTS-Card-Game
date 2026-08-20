using System;
using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Raycast cell picking, the two camera angles, and the tap flows: an armed hand card or
    /// build lands on a lit cell, a selected creature marches to a lit neighbour. Every "may I"
    /// is a CanApply probe through the controller - the highlights are the engine's own answer,
    /// never a view-side guess.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public class BoardInput : MonoBehaviour
    {
        public Camera Cam;

        private BoardView _board;
        private MatchController _match;
        private CellRef? _hover;
        private CellRef? _selected;
        private readonly List<CellRef> _highlighted = new List<CellRef>();
        private readonly List<CellRef> _legalMoves = new List<CellRef>();
        private int _seenVersion = -1;

        public CellRef? Hover { get { return _hover; } }
        public CellRef? Selected { get { return _selected; } }

        private const float TiltedPitch = 42f;
        private const float TopDownPitch = 84f;

        private bool _tilted = true;
        private float _blend = 1f;

        void Awake()
        {
            _board = GetComponent<BoardView>();
            _match = GetComponent<MatchController>();
            if (Cam == null) Cam = Camera.main;
        }

        void Update()
        {
            UpdateCamera();
            UpdateHover();

            if (_match != null && _match.Version != _seenVersion)
            {
                _seenVersion = _match.Version;
                RepaintHighlights();
            }

            if (Input.GetMouseButtonDown(0)) Tap(_hover);
            if (Input.GetMouseButtonDown(1)) ClearSelection();
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Space)) _tilted = !_tilted;
        }

        void Tap(CellRef? cell)
        {
            if (!cell.HasValue) { ClearSelection(); return; }

            // 1. an armed play/build consumes the tap (an illegal drop keeps the card armed)
            if (_match != null && _match.TryCellTap(cell.Value)) return;

            // 2. a lit legal move for the selected creature executes it
            if (_selected.HasValue && _legalMoves.Contains(cell.Value))
            {
                _match.TryMove(_selected.Value, cell.Value);
                ClearSelection();
                return;
            }

            // 3. otherwise select, and light what the engine says this unit may do
            ClearSelection();
            _selected = cell;
            _board.Paint(cell.Value, _board.SelectMaterial);

            if (_match != null)
            {
                _legalMoves.Clear();
                _legalMoves.AddRange(_match.LegalMovesFor(cell.Value));
                foreach (var c in _legalMoves)
                {
                    _highlighted.Add(c);
                    _board.Paint(c, _board.HoverMaterial);
                }
            }
        }

        void ClearSelection()
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();
            _legalMoves.Clear();
            if (_selected.HasValue) _board.Restore(_selected.Value);
            _selected = null;
        }

        /// <summary>Armed-play highlights follow the controller's probe results.</summary>
        void RepaintHighlights()
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();

            if (_match.Pending != MatchController.Intent.None)
            {
                if (_selected.HasValue) { _board.Restore(_selected.Value); _selected = null; }
                _legalMoves.Clear();
                foreach (var c in _match.LegalCells)
                {
                    _highlighted.Add(c);
                    _board.Paint(c, _board.HoverMaterial);
                }
            }
            else if (_selected.HasValue)
            {
                _legalMoves.Clear();
                _legalMoves.AddRange(_match.LegalMovesFor(_selected.Value));
                foreach (var c in _legalMoves)
                {
                    _highlighted.Add(c);
                    _board.Paint(c, _board.HoverMaterial);
                }
            }
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

        float FitDistance(Quaternion rot)
        {
            float cellPitch = _board.CellSize + _board.CellGap;
            // +1.2 budgets the worker-pawn strips hugging each edge (MatchController.MakePawn)
            float halfW = Rules.Board.Columns * cellPitch * 0.5f + 1.2f;
            float halfD = 3f * cellPitch + 0.45f;

            var extremes = new[]
            {
                new Vector3(-halfW, 0f, halfD), new Vector3(halfW, 0f, halfD),
                new Vector3(-halfW, 0f, -halfD), new Vector3(halfW, 0f, -halfD),
                new Vector3(0f, 1.1f, halfD), new Vector3(0f, 1.1f, -halfD),   // standee headroom
            };

            float tanV = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Cam.aspect;
            float fitH = tanH * 0.94f;
            float fitV = tanV * 0.72f;      // headroom for the HUD header, hand strip and button

            var inv = Quaternion.Inverse(rot);
            float need = 2f;
            for (int i = 0; i < extremes.Length; i++)
            {
                var p = inv * extremes[i];
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

            if (_hover.HasValue && !_highlighted.Contains(_hover.Value)
                && !(_selected.HasValue && _selected.Value == _hover.Value))
                _board.Restore(_hover.Value);

            _hover = found;

            if (_hover.HasValue && !(_selected.HasValue && _selected.Value == _hover.Value)
                && !_highlighted.Contains(_hover.Value))
                _board.Paint(_hover.Value, _board.HoverMaterial);
        }
    }
}
