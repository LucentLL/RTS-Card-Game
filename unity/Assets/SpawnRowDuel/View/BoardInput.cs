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
        private readonly List<CellRef> _legalAttacks = new List<CellRef>();
        private int _seenVersion = -1;

        public CellRef? Hover { get { return _hover; } }
        public CellRef? Selected { get { return _selected; } }

        /// <summary>
        /// Select a cell from outside the input layer - the upkeep prompt uses it to put the
        /// first over-extended creature under the player's nose, the way the JS popped its
        /// settle menu automatically instead of waiting to be found.
        /// </summary>
        public void SelectFromUi(CellRef cell)
        {
            ClearSelection();
            _selected = cell;
            _board.Paint(cell, _board.SelectMaterial);
            LightLegal(cell);
        }

        public void ClearSelectionFromUi() { ClearSelection(); }

        static readonly Rect FullScreen = new Rect(0f, 0f, 1f, 1f);

        private const float TiltedPitch = 42f;
        private const float TopDownPitch = 84f;

        private bool _tilted = true;
        private float _blend = 1f;

        /// <summary>
        /// Which of the two angles is showing (spec 09: Tilted, the diorama, or Top-Down). The
        /// camera eases toward it, so setting this is a request rather than a jump.
        /// </summary>
        public bool Tilted { get { return _tilted; } set { _tilted = value; } }

        /// <summary>
        /// Where the camera actually IS between the two angles: 1 tilted, 0 top-down. Read it
        /// rather than <see cref="Tilted"/> for anything that has to change WITH the swing - the
        /// standees fade out on the way to top-down, and a boolean would pop them.
        /// </summary>
        public float TiltBlend { get { return Mathf.SmoothStep(0f, 1f, _blend); } }

        void Awake()
        {
            _board = GetComponent<BoardView>();
            _match = GetComponent<MatchController>();
            if (Cam == null) Cam = Camera.main;
        }

        void Update()
        {
            UpdateCamera();

            // Input arbitration: IMGUI consuming an event never blocks legacy Input, and Update
            // runs before the frame's GUI events - so without this gate a tap on the build menu
            // (or the log) would ALSO tap the board cell behind it, and a tap on the opaque
            // bands would raycast through an extrapolated ray. Taps and hover exist only inside
            // the camera viewport and outside the published HUD panels.
            if (_match != null && !_match.MatchStarted) { UpdateHover(true); return; }

            bool overUi = Cam == null
                || !Cam.pixelRect.Contains((Vector2)Input.mousePosition)
                || HudLayout.Blocks(Input.mousePosition);

            UpdateHover(overUi);

            if (_match != null && _match.Version != _seenVersion)
            {
                _seenVersion = _match.Version;
                RepaintHighlights();
            }

            if (Input.GetMouseButtonDown(0) && !overUi) Tap(_hover);
            if (Input.GetMouseButtonDown(1)) ClearSelection();
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Space)) _tilted = !_tilted;
        }

        void Tap(CellRef? cell)
        {
            if (_match == null || !_match.MatchStarted) return;
            if (!cell.HasValue) { ClearSelection(); return; }

            // 1. an armed play/build consumes the tap (an illegal drop keeps the card armed)
            if (_match != null && _match.TryCellTap(cell.Value)) return;

            // 1b. moving banked ◆ from one card to another: the next tap names the destination
            if (_match != null && _match.SendFrom.HasValue)
            {
                _match.TrySendBankedMana(cell.Value);
                ClearSelection();
                return;
            }

            // 2. a lit legal move for the selected creature executes it
            if (_selected.HasValue && _legalMoves.Contains(cell.Value))
            {
                _match.TryMove(_selected.Value, cell.Value);
                ClearSelection();
                return;
            }

            // 3. a lit enemy object declares an attack - aim, then tap the target
            if (_selected.HasValue && _legalAttacks.Contains(cell.Value))
            {
                _match.TryAttack(_selected.Value, cell.Value);
                ClearSelection();
                return;
            }

            // 4. otherwise select, and light what the engine says this unit may do
            ClearSelection();
            _selected = cell;
            _board.Paint(cell.Value, _board.SelectMaterial);
            LightLegal(cell.Value);
        }

        void LightLegal(CellRef from)
        {
            if (_match == null) return;
            _legalMoves.Clear();
            _legalMoves.AddRange(_match.LegalMovesFor(from));
            _legalAttacks.Clear();
            _legalAttacks.AddRange(_match.LegalAttacksFor(from));
            foreach (var c in _legalMoves)
            {
                _highlighted.Add(c);
                _board.Paint(c, _board.HoverMaterial);
            }
            foreach (var c in _legalAttacks)
            {
                _highlighted.Add(c);
                _board.Paint(c, _board.HoverMaterial);
            }
        }

        void ClearSelection()
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();
            _legalMoves.Clear();
            _legalAttacks.Clear();
            if (_selected.HasValue) _board.Restore(_selected.Value);
            _selected = null;
        }

        /// <summary>Armed-play highlights follow the controller's probe results.</summary>
        void RepaintHighlights()
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();

            if (_match.Pending != MatchController.Intent.None || _match.SendFrom.HasValue)
            {
                if (_selected.HasValue) { _board.Restore(_selected.Value); _selected = null; }
                _legalMoves.Clear();
                _legalAttacks.Clear();
                foreach (var c in _match.LegalCells)
                {
                    _highlighted.Add(c);
                    _board.Paint(c, _board.HoverMaterial);
                }
            }
            else if (_selected.HasValue)
            {
                LightLegal(_selected.Value);
            }
        }

        /// <summary>
        /// May the bare hover paint this cell?
        ///
        /// Only when nothing is armed. While a card or a build is waiting for a cell, the lit
        /// cells ARE the engine's answer to "where may this go" - and the hover paints with the
        /// same material, so a finger resting on the foe's ground lit it up like a legal drop.
        /// That is what "it gives me the option to place it on the opponent's side, even though it
        /// doesn't allow me" was: not a rules bug, a hover that outranked the rules.
        /// </summary>
        bool HoverMayLight(CellRef cell)
        {
            if (_match == null) return true;
            if (_match.Pending == MatchController.Intent.None && !_match.SendFrom.HasValue) return true;
            return _match.LegalCells.Contains(cell);
        }

        void UpdateCamera()
        {
            if (Cam == null) return;

            // The camera renders the WHOLE SCREEN. It used to be inset to the gap between the
            // HUD bands, which is tidy and wrong: it makes the field stop at a bar instead of
            // running behind the battlements, and it leaves a strip of nothing wherever the wall
            // above it is transparent - which is every gap between two merlons. The walls are
            // drawn over the field now, and the board is framed into the WINDOW between their
            // rails rather than into a viewport (see Frame).
            if (Cam.rect != FullScreen) Cam.rect = FullScreen;

            float target = _tilted ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime * 2.6f);
            float t = Mathf.SmoothStep(0f, 1f, _blend);

            float pitch = Mathf.Lerp(TopDownPitch, TiltedPitch, t);
            var rot = Quaternion.Euler(pitch, 0f, 0f);

            float dist, rise;
            Frame(rot, out dist, out rise);

            Cam.transform.rotation = rot;
            Cam.transform.position = -(rot * Vector3.forward) * dist + (rot * Vector3.up) * rise;
        }

        /// <summary>
        /// The board's ground corners, in world space. These are what the camera has to CENTRE on:
        /// they are the thing a player reads as "the board".
        /// </summary>
        Vector3[] Corners()
        {
            // Half a cell past the outermost column centres is the board's actual edge, and that
            // is ALL the width budgeted now.
            //
            // Width is the expensive axis: the picture is width-limited at the near corners,
            // where perspective magnifies most, so anything budgeted here comes straight off the
            // board's size on screen. The worker files used to be budgeted here at 0.85 and they
            // have moved behind the back rows for exactly that reason (MatchController.MakePawn).
            float halfW = Rules.Board.Columns * _board.ColPitch * 0.5f + 0.06f;
            float halfD = (Rules.Board.Rows - 1) * 0.5f * _board.RowPitch + _board.RowPitch * 0.5f;

            return new[]
            {
                new Vector3(-halfW, 0f, halfD), new Vector3(halfW, 0f, halfD),
                new Vector3(-halfW, 0f, -halfD), new Vector3(halfW, 0f, -halfD),
            };
        }

        /// <summary>
        /// Room for what STANDS on the back rows, over the row's centre rather than the board's
        /// edge - a figure stands on a tile, not past it. Only a distance constraint: including
        /// these in the centring pushed the board a hundred pixels down the screen to hold empty
        /// air above the foe's back row, and that air was most of the gap being complained about.
        /// </summary>
        Vector3[] Headroom()
        {
            float backRow = (Rules.Board.Rows - 1) * 0.5f * _board.RowPitch;
            return new[]
            {
                new Vector3(0f, 1.05f, backRow), new Vector3(0f, 1.05f, -backRow),
            };
        }

        /// <summary>
        /// Frame the board so it FILLS the viewport, rather than merely fitting inside it.
        ///
        /// The old fit solved for distance alone, with the camera aimed at the board's centre -
        /// and under perspective that is not the same thing as filling the screen. The near edge
        /// projects far larger than the far edge, so pulling back until the near edge fits leaves
        /// the far edge stranded around the middle of the screen with a third of the picture above
        /// it doing nothing. That gap is what the empty grass at the top of the board was.
        ///
        /// So there are two unknowns, not one: how far back (dist) and how far UP the camera's own
        /// up-axis it slides (rise), which is what re-centres the projected trapezoid. Distance is
        /// exact for a given rise - a point needs `|x|/fit - z` of it - and the rise is relaxed
        /// toward whatever centres the projection, a few passes being plenty since each one lands
        /// most of the remaining error.
        /// </summary>
        void Frame(Quaternion rot, out float dist, out float rise)
        {
            var pts = Corners();
            var head = Headroom();
            var inv = Quaternion.Inverse(rot);
            for (int i = 0; i < pts.Length; i++) pts[i] = inv * pts[i];      // once, into camera axes
            for (int i = 0; i < head.Length; i++) head[i] = inv * head[i];

            float tanV = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Cam.aspect;
            float fitH = tanH * 0.995f;

            // The WINDOW the board is framed into: the screen minus what the hands hang over at
            // each edge. It is off-centre whenever those two differ, so the fit works in a
            // normalised height `win` and an offset `mid`, both in NDC - and the camera is aimed
            // so the board's projection lands on `mid`, not on the middle of the screen.
            float h = Mathf.Max(1f, Screen.height);
            float topFrac = Mathf.Clamp(HudLayout.TopPx / h, 0f, 0.4f);
            float botFrac = Mathf.Clamp(HudLayout.BottomPx / h, 0f, 0.4f);
            float win = 1f - topFrac - botFrac;
            float mid = botFrac - topFrac;
            float fitV = tanV * win * 0.995f;

            rise = 0f;
            dist = 2f;
            for (int pass = 0; pass < 12; pass++)
            {
                float c = mid / Mathf.Max(0.0001f, win);
                dist = 2f;
                for (int i = 0; i < pts.Length; i++)
                {
                    dist = Mathf.Max(dist, Mathf.Abs(pts[i].x) / fitH - pts[i].z);
                    dist = Mathf.Max(dist, NeedV(pts[i].y - rise, pts[i].z, fitV, c));
                }
                for (int i = 0; i < head.Length; i++)
                    dist = Mathf.Max(dist, NeedV(head[i].y - rise, head[i].z, fitV, c));

                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < pts.Length; i++)
                {
                    float ndc = (pts[i].y - rise) / ((pts[i].z + dist) * fitV);
                    lo = Mathf.Min(lo, ndc);
                    hi = Mathf.Max(hi, ndc);
                }

                // `mid` is in SCREEN ndc and the window is `win` of it, so the same offset is
                // mid/win in window units - which is what lo and hi are measured in
                float off = 0.5f * (lo + hi) - mid / Mathf.Max(0.0001f, win);
                if (Mathf.Abs(off) < 0.001f) break;
                rise += off * fitV * dist;          // NDC error back into world units, at pivot depth
            }
        }

        /// <summary>
        /// How far back this point needs the camera, to land inside a window whose centre sits at
        /// <paramref name="c"/> window-heights off the screen's own centre. Symmetric `|y|/fit - z`
        /// is the c = 0 case, and quietly over-pays by |c| when the two bands differ.
        /// </summary>
        static float NeedV(float y, float z, float fitV, float c)
        {
            float u = y / fitV;
            float edge = u >= 0f ? 1f + c : 1f - c;
            return Mathf.Abs(u) / Mathf.Max(0.0001f, edge) - z;
        }

        void UpdateHover(bool overUi)
        {
            CellRef? found = null;
            if (Cam != null && !overUi)
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
                && !_highlighted.Contains(_hover.Value) && HoverMayLight(_hover.Value))
                _board.Paint(_hover.Value, _board.HoverMaterial);
        }
    }
}
