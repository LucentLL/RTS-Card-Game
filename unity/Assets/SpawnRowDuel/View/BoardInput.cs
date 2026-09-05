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
        private readonly List<CellRef> _joiners = new List<CellRef>();
        private int _seenVersion = -1;

        // ── the drag-selection ────────────────────────────────────────────────────────────

        /// <summary>
        /// The creatures picked up by a drag, in the order they were crossed - which becomes the
        /// order they declare in.
        ///
        /// <c>_selected</c> is NOT repurposed for this. Six controls act on exactly one cell -
        /// ⚔ WALL, the three worker stacks, ◆ SEND, ⬆ UPGRADE, the charge readout and the upkeep
        /// PAY/SACRIFICE row - and quietly making it plural would change all of them. Instead the
        /// group's first member IS the anchor and <c>_selected</c> keeps pointing at it, so every
        /// one of those controls goes on meaning what it meant.
        /// </summary>
        private readonly List<CellRef> _group = new List<CellRef>();
        private readonly List<CellRef> _groupScratch = new List<CellRef>();

        /// <summary>
        /// The BLOCK sweep's own ledger, kept apart from <c>_group</c> on purpose.
        ///
        /// A block drag and an attack drag are the same gesture pointed at opposite halves of a
        /// fight, and they used to share the list - so sweeping across your defenders left them in
        /// the group, which is what every "am I acting with this card" question reads (IsPicked).
        /// Your blockers came out lit as though they were selected attackers, on the opponent's
        /// turn, and stayed that way until some later command happened to prune the group. The
        /// ledger is only here to make the sweep a SET rather than a toggle; the answer itself
        /// lives on the request, in MatchHud.
        /// </summary>
        private readonly HashSet<int> _blockSwept = new HashSet<int>();

        enum DragKind { None, Sweep, Band, Block }

        private DragKind _dragKind;
        private bool _dragging, _dragAllowed, _dragMoved, _dragTouch;
        private int _dragFinger = -1;
        private Vector2 _pressPanel, _nowPanel, _lastSamplePanel;
        private CellRef? _pressCell;
        private Cards.HandBar _hand;
        private MatchHud _hud;

        public CellRef? Hover { get { return _hover; } }
        public CellRef? Selected { get { return _selected; } }

        /// <summary>Everything the current drag has picked up. Empty when nothing is grouped.</summary>
        public IReadOnlyList<CellRef> Group { get { return _group; } }

        /// <summary>Is this cell one the player is acting WITH - the single selection, the group's
        /// anchor, or any member of it? The card layers tint from this.</summary>
        public bool IsPicked(CellRef cell)
        {
            if (_selected.HasValue && _selected.Value == cell) return true;
            for (int i = 0; i < _group.Count; i++) if (_group[i] == cell) return true;
            return false;
        }

        /// <summary>The band rectangle to draw, in PANEL units, or null when no band is being
        /// dragged. A sweep draws none - the cards lighting up under the finger are the feedback.</summary>
        public Rect? BandRect
        {
            get
            {
                if (!_dragging || _dragKind != DragKind.Band || !_dragMoved) return null;
                float x0 = Mathf.Min(_pressPanel.x, _nowPanel.x), x1 = Mathf.Max(_pressPanel.x, _nowPanel.x);
                float y0 = Mathf.Min(_pressPanel.y, _nowPanel.y), y1 = Mathf.Max(_pressPanel.y, _nowPanel.y);
                return new Rect(x0, y0, x1 - x0, y1 - y0);
            }
        }

        /// <summary>What the selected unit may attack right now - the engine's own answer, lit on
        /// the board and read by the vitals layer so a target says what it has left.</summary>
        public IReadOnlyList<CellRef> LegalAttacks { get { return _legalAttacks; } }
        public IReadOnlyList<CellRef> LegalMoves { get { return _legalMoves; } }

        /// <summary>Your creatures that may still pile into the attack already aimed.</summary>
        public IReadOnlyList<CellRef> Joiners { get { return _joiners; } }

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
            _hand = GetComponent<Cards.HandBar>();
            _hud = GetComponent<MatchHud>();
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

            UpdatePointer(overUi);
            if (Input.GetMouseButtonDown(1)) ClearSelection();
            if (Input.GetKeyDown(KeyCode.Escape) && _group.Count > 0) ClearSelection();
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Space)) _tilted = !_tilted;
        }

        // ── the pointer machine ───────────────────────────────────────────────────────────

        /// <summary>
        /// Press, drag, release - and the reason a board tap now happens on RELEASE.
        ///
        /// A card fills its whole tile (CardPlateLayer.Fill = 1), so the cards tile the board edge
        /// to edge and a drag that selects creatures has to be allowed to START on one. That means
        /// the press cannot commit anything. Three things keep that safe: the tap uses the cell the
        /// PRESS landed on rather than the one under the release, so a slipped finger cannot
        /// retarget it; a drag shorter than the slop is still a tap; and an armed hand card still
        /// consumes the tap whole, because TryCellTap returns true for every tap while a play is
        /// armed.
        ///
        /// The gesture's legality is decided ONCE, at press, and latched. WallBands treats a held
        /// pointer inside a tower span as "looking" and opens that wall under the drag, which
        /// republishes the blocked bands - re-reading them mid-drag would cancel the gesture the
        /// player is in the middle of making.
        /// </summary>
        void UpdatePointer(bool overUi)
        {
            var panel = _hand != null && _hand.PanelReady ? _hand.PanelSize()
                                                          : new Vector2(Screen.width, Screen.height);

            bool touch = Input.touchCount > 0;
            bool down, held, up;
            Vector2 devicePx;

            if (touch)
            {
                // ONE finger owns the gesture: a second landing mid-drag must not move it.
                var t = Input.GetTouch(0);
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).fingerId == _dragFinger) { t = Input.GetTouch(i); break; }

                devicePx = t.position;
                down = t.phase == TouchPhase.Began;
                held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                if (down) _dragFinger = t.fingerId;
                else if (_dragging && t.fingerId != _dragFinger) return;
            }
            else
            {
                devicePx = Input.mousePosition;
                down = Input.GetMouseButtonDown(0);
                held = Input.GetMouseButton(0);
                up = Input.GetMouseButtonUp(0);
            }

            var pt = BoardProjection.ScreenToPanel(devicePx, panel);

            if (down)
            {
                _dragging = true;
                _dragMoved = false;
                _dragTouch = touch;
                _pressPanel = _nowPanel = _lastSamplePanel = pt;
                _pressCell = _hover;
                _dragKind = DragKind.None;

                _dragAllowed = !overUi && _match != null && _match.MatchStarted
                            && _match.Asking == null
                            && _match.Pending == MatchController.Intent.None
                            && !_match.SendFrom.HasValue;

                if (_dragAllowed) _dragKind = ClassifyPress();

                // FALL THROUGH IF THE BUTTON IS ALREADY BACK UP.
                //
                // Legacy Input reports GetMouseButtonDown and GetMouseButtonUp on the SAME frame
                // whenever the press and the release both land between two polls - a 50 ms click
                // on a 30 fps WebGL build, and a certainty across any long frame (a GC spike, a
                // wall opening, the AI's turn - exactly when an impatient player clicks). A bare
                // `return` here threw that release away, and no later frame could recover it:
                // `up` is true for one frame only, so every frame after walked into `if (!up)
                // return` below. The tap simply never happened - and for a press on one of your
                // own ready creatures, ClassifyPress had already run BeginGroup and cleared the
                // selection, so a fast click DESELECTED and selected nothing. `_dragging` also
                // stayed true, which left the band marquee following a cursor with no button
                // held. Falling through hands this frame to the release path, where a gesture
                // that never travelled is already a tap on the cell it started from.
                if (!up) return;
            }

            if (!_dragging) return;
            _nowPanel = pt;

            float slop = (_dragTouch ? 16f : 8f) * HudLayout.Scale;
            if (!_dragMoved && (pt - _pressPanel).sqrMagnitude > slop * slop) _dragMoved = true;

            if (held && _dragMoved && _dragAllowed
                && (_dragKind == DragKind.Sweep || _dragKind == DragKind.Block))
                SweepTo(pt, panel);

            if (!up) return;

            // THE RECTANGLE IS READ BEFORE THE DRAG IS TORN DOWN. BandRect answers null unless
            // `_dragging` is still true (it is a "what is being dragged right now" question), so
            // clearing the flag first and calling CommitBand second made the band select nothing,
            // ever: the marquee drew under the finger and vanished on release with an empty group.
            var band = BandRect;

            _dragging = false;
            _dragFinger = -1;

            // A gesture that never travelled - or one that was never allowed to be a gesture - is
            // a TAP, on the cell it started from.
            if (!_dragMoved || !_dragAllowed || _dragKind == DragKind.None)
            {
                if (!overUi) Tap(_pressCell);
                _dragKind = DragKind.None;
                return;
            }

            if (_dragKind == DragKind.Band && band.HasValue) CommitBand(band.Value, panel);
            _dragKind = DragKind.None;
        }

        /// <summary>What kind of drag a press begins, decided by what is under it.</summary>
        DragKind ClassifyPress()
        {
            var s = _match.Engine != null ? _match.Engine.State : null;
            if (s == null) return DragKind.None;

            // a parked blocker choice turns the whole board into the answer to it
            if (_hud != null && _hud.AwaitingBlockers)
            {
                _blockSwept.Clear();
                return DragKind.Block;
            }

            if (_pressCell.HasValue && MatchController.IsReadyAttacker(s, _pressCell.Value))
            {
                BeginGroup();
                return DragKind.Sweep;
            }

            // A band needs somewhere to start that is not a unit - and a finger cannot see through
            // itself, so the rectangle is the mouse's alone.
            return _dragTouch ? DragKind.None : DragKind.Band;
        }

        void BeginGroup()
        {
            ClearSelection();
            _group.Clear();
        }

        // ── sweep ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Add everything the pointer crossed since the last sample.
        ///
        /// SAMPLED ALONG THE SEGMENT rather than tested where it stopped: a finger moving quickly
        /// covers half a row in a frame, and a gesture that only looked at its endpoints would skip
        /// the units in between - which are exactly the ones it was swept through to collect.
        /// </summary>
        void SweepTo(Vector2 pt, Vector2 panel)
        {
            float step = Mathf.Max(6f, 10f * HudLayout.Scale);
            float dist = Vector2.Distance(_lastSamplePanel, pt);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 32);

            for (int i = 1; i <= steps; i++)
                SweepAdd(Vector2.Lerp(_lastSamplePanel, pt, i / (float)steps), panel);

            _lastSamplePanel = pt;
        }

        void SweepAdd(Vector2 pt, Vector2 panel)
        {
            if (BoardProjection.HiddenByWalls(pt.y, panel)) return;

            CellRef hit;
            if (!CellUnder(pt, panel, out hit)) return;
            if (!Eligible(hit)) return;

            // SETS, never toggles: sweeping back over a unit must not drop it, so two strokes and
            // a hand-ticked row compose instead of undoing each other.
            if (_dragKind == DragKind.Block)
            {
                if (!_blockSwept.Add(hit.Index)) return;
                int b = _hud != null ? _hud.BlockerIndexOf(hit) : -1;
                if (b >= 0) _hud.SetBlockerPick(b, true);
                return;
            }

            for (int i = 0; i < _group.Count; i++) if (_group[i] == hit) return;

            _group.Add(hit);
            AfterGroupChanged();
        }

        /// <summary>
        /// Which cell a panel point is over: the unit's own box first, then the nearest projected
        /// cell centre inside a forgiving radius.
        ///
        /// The box first because the player aims at the FIGURE, which stands toward the camera of
        /// its tile and rises well above it - hit-testing the tile alone misses the thing they are
        /// looking at by most of its height. The radius second because a tilted board's far rows
        /// are only a few pixels deep and a finger is not.
        /// </summary>
        bool CellUnder(Vector2 pt, Vector2 panel, out CellRef hit)
        {
            hit = default(CellRef);
            if (_hand == null || Cam == null || !_hand.PanelReady) return false;

            var s = _match.Engine != null ? _match.Engine.State : null;
            float snap = 22f * HudLayout.Scale;
            float best = snap * snap;
            bool found = false;

            foreach (var kv in _board.Cells)
            {
                bool structure = s != null && s.At(kv.Key) is StructureUnit;

                Rect box;
                if (BoardProjection.TryUnitBox(_hand, Cam, _board, kv.Key, structure, out box)
                    && box.Contains(pt))
                {
                    hit = kv.Key;
                    return true;
                }

                Vector2 centre;
                if (!_hand.TryProject(Cam, _board.WorldOf(kv.Key), out centre)) continue;
                float d = (centre - pt).sqrMagnitude;
                if (d < best) { best = d; hit = kv.Key; found = true; }
            }
            return found;
        }

        /// <summary>May this cell join the gesture in progress?</summary>
        bool Eligible(CellRef cell)
        {
            var s = _match.Engine != null ? _match.Engine.State : null;
            if (s == null) return false;

            if (_dragKind == DragKind.Block)
                return _hud != null && _hud.BlockerIndexOf(cell) >= 0;

            return MatchController.IsReadyAttacker(s, cell);
        }

        void AfterGroupChanged()
        {
            _selected = _group[0];
            RepaintHighlights();
        }

        // ── band ──────────────────────────────────────────────────────────────────────────

        void CommitBand(Rect r, Vector2 panel)
        {
            BeginGroup();
            foreach (var kv in _board.Cells)
            {
                Vector2 centre;
                if (!_hand.TryProject(Cam, _board.WorldOf(kv.Key), out centre)) continue;
                if (!r.Contains(centre)) continue;
                if (BoardProjection.HiddenByWalls(centre.y, panel)) continue;
                if (!Eligible(kv.Key)) continue;
                _group.Add(kv.Key);
            }

            if (_group.Count == 0) { ClearSelection(); return; }
            _selected = _group[0];
            RepaintHighlights();
        }

        // ── declaring as a group ──────────────────────────────────────────────────────────

        /// <summary>
        /// Declare the whole group at one target, in the order it was picked up.
        ///
        /// A joint attack IS N declarations sharing a target, so this is a loop over the same door
        /// a single attack goes through - every member via Declare/JoinAssault, so in a duel each
        /// is its own wire frame, applied in the same order on both seats.
        ///
        /// The assault opens on the first member that SUCCEEDS, not on the first member: Declare
        /// records the assault only after a successful submit, so opening it on a failure would
        /// make every JoinAssault after it answer NothingDeclared.
        ///
        /// A refused member is SKIPPED rather than aborting the rest - the declarations already
        /// made stand, and ✕ CANCEL under the board takes the whole assault back if the group that
        /// arrived was not the one the player wanted. The exception is a gate flipping underneath
        /// us: a parked choice, the turn ending, the match ending. Every remaining member would
        /// fail identically, so it stops there.
        /// </summary>
        public void DeclareGroup(AttackTarget target, string label)
        {
            _groupScratch.Clear();
            _groupScratch.AddRange(_group);
            ClearSelection();

            int joined = 0, refused = 0;
            for (int i = 0; i < _groupScratch.Count; i++)
            {
                var from = _groupScratch[i];
                Rejection why;

                if (_match.Assault == null) why = _match.Declare(from, target, label);
                else if (!_match.CanJoinAssault(from)) { refused++; continue; }
                else why = _match.JoinAssault(from);

                if (why == Rejection.None) { joined++; continue; }
                if (why == Rejection.ChoicePending || why == Rejection.GameOver
                 || why == Rejection.NotYourTurn || why == Rejection.WrongPhase) break;
                refused++;
            }

            if (_groupScratch.Count > 1)
                _match.Note("- " + joined + " of " + _groupScratch.Count + " joined the attack"
                            + (refused > 0 ? " (" + refused + " could not)" : ""));
        }

        /// <summary>The group when there is one, nothing otherwise. Every button that declares an
        /// attack asks here first, so the wall and the worker stacks became group attacks without
        /// having to know that groups exist.</summary>
        public bool DeclareGroupOrSingle(AttackTarget target, string label)
        {
            if (_group.Count == 0) return false;
            DeclareGroup(target, label);
            return true;
        }

        /// <summary>Drop members the engine would no longer accept. Runs on every repaint, so a
        /// creature that dies, moves, taps or has its turn ended leaves the group by itself.</summary>
        void PruneGroup()
        {
            if (_group.Count == 0) return;
            var s = _match.Engine != null ? _match.Engine.State : null;

            for (int i = _group.Count - 1; i >= 0; i--)
                if (s == null || !MatchController.IsReadyAttacker(s, _group[i])) _group.RemoveAt(i);
        }

        void PaintGroup()
        {
            for (int i = 0; i < _group.Count; i++)
            {
                _highlighted.Add(_group[i]);
                _board.Paint(_group[i], _board.SelectMaterial);
            }
        }

        void Tap(CellRef? cell)
        {
            if (_match == null || !_match.MatchStarted) return;

            // A pending confirm is MODAL. Its panel blocks the taps under it, but a tap anywhere
            // else would queue a second command behind an answer that has not been given - and
            // the held command would then commit against a board that had moved.
            if (_match.Asking != null) return;

            // A PARKED CHOICE owns the board. Every branch below submits a command, and while a
            // choice is parked the engine refuses all of them with ChoicePending - so without this
            // the whole ladder ran against a frozen engine and answered nothing. A tap during YOUR
            // blocker choice toggles that defender instead.
            var parked = _match.Engine != null ? _match.Engine.State.Pending : null;
            if (parked != null)
            {
                if (cell.HasValue && _hud != null && _hud.AwaitingBlockers)
                {
                    int i = _hud.BlockerIndexOf(cell.Value);
                    if (i >= 0) _hud.SetBlockerPick(i, !_hud.BlockerPicked(i));
                }
                return;
            }

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

            // 3. a lit enemy object declares an attack - aim, then tap the target.
            //
            // A GROUP goes first: the whole selection swings at the one target, in the order it
            // was picked up. The group's legal targets are the anchor's, which is exact rather
            // than convenient - DeclareAttackHandler has no reach or column gate, so every
            // rejection left is attacker-side and the ready filter has already excluded it.
            if (_group.Count > 0 && _legalAttacks.Contains(cell.Value))
            {
                var t = _match.Engine.State.At(cell.Value);
                if (t != null)
                {
                    DeclareGroup(new UnitTarget(cell.Value, t.Id), MatchController.NameOfObject(t));
                    return;
                }
            }

            if (_selected.HasValue && _legalAttacks.Contains(cell.Value))
            {
                _match.TryAttack(_selected.Value, cell.Value);
                ClearSelection();
                return;
            }

            // 3b. one of your ready creatures JOINS the attack that is already aimed. A joint
            // attack is N declarations sharing a target (spec 03 §6.2) and the target has already
            // been picked, so the second attacker only has to say "me too".
            if (_match.Assault != null && _match.CanJoinAssault(cell.Value))
            {
                _match.JoinAssault(cell.Value);
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

            // A MULTI-SELECTION HAS NO MOVE. There is no group move command, and lighting the
            // anchor's move cells for a group of five would offer a march that walks exactly one
            // of them - worse than offering nothing.
            if (_group.Count <= 1) _legalMoves.AddRange(_match.LegalMovesFor(from));

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
            _joiners.Clear();
            for (int i = 0; i < _group.Count; i++) _board.Restore(_group[i]);
            _group.Clear();
            if (_selected.HasValue) _board.Restore(_selected.Value);
            _selected = null;
        }

        /// <summary>Armed-play highlights follow the controller's probe results.</summary>
        void RepaintHighlights()
        {
            foreach (var c in _highlighted) _board.Restore(c);
            _highlighted.Clear();
            _joiners.Clear();          // the vitals ring these, and an ended assault has none

            // The group is re-checked against the engine on EVERY repaint, not merely when it is
            // built: a member that dies, is bounced, taps, or simply has the turn end under it is
            // no longer a legal attacker, and a stale group would declare into a rejection.
            PruneGroup();

            if (_match.Pending != MatchController.Intent.None || _match.SendFrom.HasValue)
            {
                // arming a hand card DESTROYS the group rather than orphaning it: the lit cells
                // now mean "where may this card go", which is a different question entirely
                ClearSelection();
                foreach (var c in _match.LegalCells)
                {
                    _highlighted.Add(c);
                    _board.Paint(c, _board.HoverMaterial);
                }
            }
            else if (_group.Count > 0)
            {
                _selected = _group[0];          // the anchor: every single-cell control still works
                PaintGroup();
                LightLegal(_group[0]);
            }
            else if (_selected.HasValue)
            {
                LightLegal(_selected.Value);
            }
            else if (_match.Assault != null)
            {
                LightJoiners();
            }
        }

        /// <summary>
        /// With an attack aimed and nobody selected, the board shows the ASSAULT: its target, and
        /// every creature of yours that may still join it. One CanApply per cell, on a repaint
        /// rather than a frame - the same probe every other highlight here is.
        /// </summary>
        void LightJoiners()
        {
            _joiners.Clear();
            for (int i = 0; i < Rules.Board.Cells; i++)
            {
                var c = CellRef.FromIndex(i);
                if (!_match.CanJoinAssault(c)) continue;
                _joiners.Add(c);
                _highlighted.Add(c);
                _board.Paint(c, _board.HoverMaterial);
            }

            if (_match.AssaultCell.HasValue)
            {
                _highlighted.Add(_match.AssaultCell.Value);
                _board.Paint(_match.AssaultCell.Value, _board.SelectMaterial);
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

            // Half a turn for the far seat. The board is one absolute grid and the rules never
            // mirror, so "which end am I sitting at" is answered here, once, by moving the
            // camera - not by mirroring coordinates, which would put the two engines on
            // different boards. Everything downstream billboards or reads Seat, so this is the
            // whole of the geometric half of playing as the guest.
            var rot = Quaternion.Euler(pitch, Seat.CameraYaw, 0f);

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
