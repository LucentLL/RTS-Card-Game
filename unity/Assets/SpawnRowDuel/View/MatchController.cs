using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Boots a real match on the real engine and keeps the scene a pure function of its state:
    /// every card on the board renders as a standee with its imported art, worker pools render
    /// as pawns, and every interaction - summon, set, build, upgrade-free move, flip - goes
    /// through DuelEngine commands. Legal cells are discovered by PROBING CanApply, never by a
    /// view-side reimplementation, so the picture cannot disagree with the rules.
    ///
    /// The opponent is still a command feeder on a timer (M11 replaces it with the scripted
    /// AI): harvest, draw, one greedy summon, end.
    /// </summary>
    public class MatchController : MonoBehaviour
    {
        public CardDatabase Database;          // assigned by SceneBootstrap; ships in the scene
        public BoardView Board;

        public DuelEngine Engine { get; private set; }

        /// <summary>Bumped on every state-shaped change so painters know to refresh.</summary>
        public int Version { get; private set; }

        private readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> Log { get { return _log; } }

        // ---- pending interaction (what the next board tap means) -------------------------
        public enum Intent : byte { None = 0, PlayCard = 1, Build = 2 }

        public Intent Pending { get; private set; }
        public int PendingHandIndex { get; private set; }
        public Rules.PlayMode PendingMode { get; private set; }
        public StructureDef PendingBuild { get; private set; }
        public readonly List<CellRef> LegalCells = new List<CellRef>();

        private float _beat;
        private bool _foeActed;
        private bool _foeAttacked;

        private readonly List<Transform>[,] _pawns = new List<Transform>[2, 3];
        private readonly Dictionary<int, Transform> _standees = new Dictionary<int, Transform>();
        private readonly List<int> _deadStandees = new List<int>();
        private MaterialPropertyBlock _mpb;
        private Dictionary<string, CardDefinition> _defByName;

        private static readonly Color YouTint = new Color(0.85f, 0.70f, 0.25f);
        private static readonly Color FoeTint = new Color(0.35f, 0.50f, 0.85f);
        private static readonly Color TappedTint = new Color(0.30f, 0.30f, 0.33f);

        void Awake()
        {
            if (Board == null) Board = GetComponent<BoardView>();
            _mpb = new MaterialPropertyBlock();
            for (int s = 0; s < 2; s++)
                for (int z = 0; z < 3; z++)
                    _pawns[s, z] = new List<Transform>();

            if (Database == null)
            {
                Debug.LogError("[match] no CardDatabase assigned - rerun SceneBootstrap.Build");
                enabled = false;
                return;
            }

            _defByName = new Dictionary<string, CardDefinition>(System.StringComparer.Ordinal);
            foreach (var d in Database.All)
                if (d != null && !_defByName.ContainsKey(d.DisplayName)) _defByName[d.DisplayName] = d;

            var catalog = Database.ToCatalog();
            ulong seed = (ulong)System.DateTime.Now.Ticks;
            var state = MatchSetup.NewMatch(catalog,
                new CommanderId("fire"), new CommanderId("water"), seed, RulesOptions.JsParity);
            Engine = new DuelEngine(state, catalog);

            Push("— Your turn · Upkeep — ⛏ Harvest to begin —");
        }

        void Update()
        {
            if (Engine == null) return;
            PumpEvents();
            Autopilot();
            ReconcilePawns();
            ReconcileStandees();
        }

        // ---- commands from the HUD / input ------------------------------------------------

        public Rejection TryHuman(ICommand cmd)
        {
            var r = Engine.Apply(cmd);
            if (r.Status == CommandStatus.Rejected) return r.Rejection;
            Touch();
            return Rejection.None;
        }

        /// <summary>Arm a hand play; the next legal-cell tap completes it.</summary>
        public void BeginPlay(int handIndex, Rules.PlayMode mode)
        {
            Pending = Intent.PlayCard;
            PendingHandIndex = handIndex;
            PendingMode = mode;
            PendingBuild = null;
            ProbeLegalCells(cell => new PlayCardCommand(Side.You, handIndex, mode, cell));
        }

        public void BeginBuild(StructureDef def)
        {
            Pending = Intent.Build;
            PendingBuild = def;
            ProbeLegalCells(cell => new BuildStructureCommand(Side.You, def.Bid, def.Element, cell));
        }

        public void CancelPending()
        {
            Pending = Intent.None;
            PendingBuild = null;
            LegalCells.Clear();
            Touch();
        }

        private void ProbeLegalCells(System.Func<CellRef, ICommand> make)
        {
            LegalCells.Clear();
            for (int i = 0; i < Rules.Board.Cells; i++)
            {
                var cell = CellRef.FromIndex(i);
                if (Engine.CanApply(make(cell)) == Rejection.None) LegalCells.Add(cell);
            }
            Touch();
        }

        /// <summary>
        /// A board tap while something is armed. An illegal drop keeps the selection - a
        /// fat-finger miss must never silently cancel the play (spec 04 s10.2).
        /// </summary>
        public bool TryCellTap(CellRef cell)
        {
            if (Pending == Intent.None) return false;

            ICommand cmd = Pending == Intent.PlayCard
                ? (ICommand)new PlayCardCommand(Side.You, PendingHandIndex, PendingMode, cell)
                : new BuildStructureCommand(Side.You, PendingBuild.Bid, PendingBuild.Element, cell);

            var r = Engine.Apply(cmd);
            if (r.Status == CommandStatus.Rejected)
            {
                Push("· " + Hint(r.Rejection));
                return true;               // consumed - the armed card stays armed
            }

            CancelPending();
            return true;
        }

        /// <summary>Enemy cells this creature may legally declare an attack on right now.</summary>
        public List<CellRef> LegalAttacksFor(CellRef from)
        {
            var targets = new List<CellRef>();
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null || u.Owner != Side.You) return targets;

            foreach (var kv in Engine.State.Objects())
            {
                if (kv.Value.Owner == Side.You) continue;
                var cmd = new DeclareAttackCommand(Side.You, from, u.Id,
                    new UnitTarget(kv.Key, kv.Value.Id));
                if (Engine.CanApply(cmd) == Rejection.None) targets.Add(kv.Key);
            }
            return targets;
        }

        /// <summary>A tap on an enemy object while one of your ready creatures is selected.</summary>
        public Rejection TryAttack(CellRef from, CellRef targetCell)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            var t = Engine.State.At(targetCell);
            if (u == null || t == null) return Rejection.NoSuchUnit;
            return TryHuman(new DeclareAttackCommand(Side.You, from, u.Id,
                new UnitTarget(targetCell, t.Id)));
        }

        public Rejection TryAttackWall(CellRef from)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null) return Rejection.NoSuchUnit;
            return TryHuman(new DeclareAttackCommand(Side.You, from, u.Id, new WallTarget(Side.Foe)));
        }

        /// <summary>Legal one-square moves for a unit, discovered by probing the engine.</summary>
        public List<CellRef> LegalMovesFor(CellRef from)
        {
            var moves = new List<CellRef>();
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null || u.Owner != Side.You) return moves;

            System.Span<CellRef> buf = stackalloc CellRef[8];
            int n = Rules.Board.Neighbours(from, buf);
            for (int i = 0; i < n; i++)
                if (Engine.CanApply(new MoveUnitCommand(Side.You, from, buf[i], u.Id)) == Rejection.None)
                    moves.Add(buf[i]);
            return moves;
        }

        public Rejection TryMove(CellRef from, CellRef to)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null) return Rejection.NoSuchUnit;
            return TryHuman(new MoveUnitCommand(Side.You, from, to, u.Id));
        }

        public CardDefinition DefOf(string displayName)
        {
            CardDefinition d;
            return _defByName.TryGetValue(displayName, out d) ? d : null;
        }

        public CardDefinition DefOfStructure(StructId bid, Element color)
        {
            var def = Engine.Catalog.Structure(bid, color);
            if (def == null) return null;
            CardDefinition d;
            return Database.TryByExportKey(def.ExportKey, out d) ? d : null;
        }

        public static string Hint(Rejection why)
        {
            switch (why)
            {
                case Rejection.ShortfallUnsettled: return "Settle the worker shortfall first";
                case Rejection.WrongPhase: return "Not in this phase";
                case Rejection.NotYourTurn: return "Not your turn";
                case Rejection.NotEnoughMana: return "Not enough ◆";
                case Rejection.NeedsOneMana: return "Setting costs ◆1";
                case Rejection.DestinationNotDeployable: return "Deploy to your own rows";
                case Rejection.CenterLaneForStructure: return "Structures take the dark flanks";
                case Rejection.CellOccupied: return "That spot is taken";
                case Rejection.MissingPrereq: return "Missing a prerequisite structure";
                case Rejection.RowLacksWorkers: return "That row has no workers to spare";
                case Rejection.MoveAlreadySpent: return "Its move is spent";
                case Rejection.ChargeUnderfunded: return "Pour more ◆ before flipping";
                default: return why.ToString();
            }
        }

        void Touch() { Version++; }

        // ---- the stand-in opponent --------------------------------------------------------

        void Autopilot()
        {
            var s = Engine.State;
            if (s.IsOver) return;

            _beat += Time.deltaTime;
            if (_beat < 0.7f) return;

            // parked choices the FOE must answer - the stand-in policy, not an AI:
            // block with the ported defending heuristic, absorb/retaliate at index 0 (the JS
            // defender's hardcoded pick). Choices for YOU wait on the HUD.
            if (s.Pending != null)
            {
                if (s.Pending.Responder != Side.Foe) return;
                _beat = 0f;

                var blockerReq = s.Pending as BlockerRequest;
                if (blockerReq != null)
                    Apply(new RespondCommand(Side.Foe,
                        new BlockersChosen(AiPolicy.ChooseInterceptors(s, blockerReq))));
                else
                    Apply(new RespondCommand(Side.Foe, new IndexChosen(0)));
                return;
            }

            if (s.Turn == Side.Foe)
            {
                _beat = 0f;
                switch (s.Phase)
                {
                    case TurnPhase.Upkeep: Apply(new HarvestCommand(Side.Foe)); break;
                    case TurnPhase.Draw:
                        Apply(new DrawForTurnCommand(Side.Foe));
                        _foeActed = false;
                        _foeAttacked = false;
                        break;
                    case TurnPhase.Action:
                        if (!_foeActed && FoeSummonsSomething()) { _foeActed = true; break; }
                        _foeActed = true;
                        if (!_foeAttacked && FoeDeclaresAnAttack()) break;
                        _foeAttacked = true;
                        if (s.Combat.HasDeclarations)
                        {
                            Apply(new ResolveCombatCommand(Side.Foe));
                            break;
                        }
                        Apply(new EndTurnCommand(Side.Foe));
                        break;
                    case TurnPhase.End: Apply(new BeginTurnCommand(Side.You)); break;
                }
            }
            else if (s.Phase == TurnPhase.End)
            {
                _beat = 0f;
                Apply(new BeginTurnCommand(Side.You == s.Turn ? Side.Foe : Side.You));
            }
        }

        /// <summary>Every ready foe creature storms your wall, one declaration per beat - the
        /// v3 texture (the AI always attacks the wall; defended walls cost bodies).</summary>
        bool FoeDeclaresAnAttack()
        {
            var s = Engine.State;
            foreach (var kv in s.Objects())
            {
                var c = kv.Value as CreatureUnit;
                if (c == null || c.Owner != Side.Foe || c.IsWorker) continue;
                if (c.Sick || c.Tapped || c.Hp <= 0) continue;

                // deferred: the s12 mirrored cadence - you answer blocks at resolve time,
                // seeing the foe's COMPLETE assault, not one declaration at a time blind
                var cmd = new DeclareAttackCommand(Side.Foe, kv.Key, c.Id,
                    new WallTarget(Side.You), true);
                if (Engine.CanApply(cmd) == Rejection.None)
                {
                    Apply(cmd);
                    return true;
                }
            }
            return false;
        }

        void Apply(ICommand cmd)
        {
            if (Engine.Apply(cmd).Applied) Touch();
        }

        /// <summary>
        /// One greedy summon per foe turn: costliest affordable card, back-row slots in the
        /// aiPickDeploySlot order. Pure legal commands - a placeholder for the M11 policy.
        /// </summary>
        bool FoeSummonsSomething()
        {
            var s = Engine.State;
            var hand = s.P(Side.Foe).Hand;

            int bestIdx = -1, bestCost = -1;
            for (int i = 0; i < hand.Count; i++)
            {
                CreatureCard c;
                if (!Engine.Catalog.TryCreature(hand[i].Id, out c)) continue;
                if (c.Cost <= s.P(Side.Foe).Mana && c.Cost > bestCost) { bestCost = c.Cost; bestIdx = i; }
            }
            if (bestIdx < 0) return false;

            int[] order = { 2, 4, 3, 1, 5, 0, 6 };      // the JS back-row preference
            var back = Rules.Board.RowFor(Side.Foe, SlotName.Back);
            var front = Rules.Board.RowFor(Side.Foe, SlotName.Front);
            for (int pass = 0; pass < 2; pass++)
            {
                var row = pass == 0 ? back : front;
                for (int i = 0; i < order.Length; i++)
                {
                    var cmd = new PlayCardCommand(Side.Foe, bestIdx, Rules.PlayMode.Summon,
                        new CellRef(row, order[i]));
                    if (Engine.CanApply(cmd) == Rejection.None)
                    {
                        Apply(cmd);
                        return true;
                    }
                }
            }
            return false;
        }

        // ---- events -> log ----------------------------------------------------------------

        void PumpEvents()
        {
            foreach (var ev in Engine.DrainEvents())
            {
                var line = Describe(ev);
                if (line != null) Push(line);
                Touch();
            }
        }

        void Push(string line)
        {
            _log.Add(line);
            if (_log.Count > 40) _log.RemoveAt(0);
        }

        string Describe(GameEvent ev)
        {
            var turn = ev as TurnStarted;
            if (turn != null)
                return "— " + (turn.Side == Side.You ? "Your" : "Foe") + " turn " +
                       turn.TurnNumber + " · Upkeep —";

            var harvest = ev as HarvestCollected;
            if (harvest != null)
                return (Engine.State.Turn == Side.You ? "You harvest ◆" : "Foe harvests ◆") + harvest.Amount;

            var drawn = ev as CardDrawn;
            if (drawn != null)
                return Engine.State.Turn == Side.You ? "You draw " + drawn.Card.Value : "Foe draws a card";

            var summoned = ev as UnitSummoned;
            if (summoned != null) return NameOf(summoned.UnitId) + " enters at " + summoned.At;

            var moved = ev as UnitMoved;
            if (moved != null) return NameOf(moved.UnitId) + " advances to " + moved.To;

            var raised = ev as StructureRaised;
            if (raised != null) return "The " + raised.Def.Value + " rises";

            var upgraded = ev as StructureUpgraded;
            if (upgraded != null) return upgraded.From.Value + " becomes " + upgraded.To.Value;

            var flipped = ev as CardFlipped;
            if (flipped != null)
                return NameOf(flipped.UnitId) + " surges into being" +
                       (flipped.Sick ? " — must rest" : " — battle-ready!");

            var drained = ev as ManaDrained;
            if (drained != null && drained.Lost > 0)
                return "◆" + drained.Lost + " unspent mana drains away" +
                       (drained.Kept > 0 ? " — vaults keep ◆" + drained.Kept : "");

            var yielded = ev as ManaYielded;
            if (yielded != null) return "A structure yields ◆" + yielded.Amount;

            var revived = ev as CreatureRevived;
            if (revived != null) return "The Reliquary returns " + revived.Card.Value;

            var destroyed = ev as UnitDestroyed;
            if (destroyed != null && destroyed.OnBoard) return "A " +
                (destroyed.Kind == UnitKind.Building ? "structure is razed" : "creature falls");

            var declared = ev as AttackDeclared;
            if (declared != null) return "⚔ " + NameOf(declared.AttackerId) + " declares an attack";

            var blocks = ev as BlockersAssigned;
            if (blocks != null)
                return blocks.BlockerIds.Length == 0
                    ? "The attack is let through"
                    : blocks.BlockerIds.Length + " blocker(s) interpose";

            var wall = ev as WallStruck;
            if (wall != null)
                return (wall.Defender == Side.You ? "Your" : "The enemy") + " wall is stormed for ⚔" +
                       wall.Amount + " — ♥" + wall.LifeRemaining + " remains";

            var bounced = ev as UnitBounced;
            if (bounced != null) return "Undertow! A creature is hurled back to hand";

            var sprung = ev as TrapSprung;
            if (sprung != null) return sprung.Card.Value + " springs!";

            var ended = ev as MatchEnded;
            if (ended != null) return "— MATCH OVER: " + ended.Outcome + " —";

            return null;
        }

        string NameOf(int unitId)
        {
            foreach (var kv in Engine.State.Objects())
            {
                if (kv.Value.Id != unitId) continue;
                var c = kv.Value as CreatureUnit;
                if (c != null) return c.Name;
                var b = kv.Value as StructureUnit;
                if (b != null) return b.DefId.Value;
            }
            return "A unit";
        }

        // ---- worker pawns -------------------------------------------------------------------

        void ReconcilePawns()
        {
            var s = Engine.State;
            for (int side = 0; side < 2; side++)
                for (int z = 0; z < 3; z++)
                {
                    var pool = s.Players[side].Workers[z].Members;
                    var pawns = _pawns[side, z];

                    while (pawns.Count < pool.Count) pawns.Add(MakePawn((Side)side, (WorkerZone)z, pawns.Count));
                    while (pawns.Count > pool.Count)
                    {
                        Destroy(pawns[pawns.Count - 1].gameObject);
                        pawns.RemoveAt(pawns.Count - 1);
                    }

                    for (int i = 0; i < pawns.Count; i++)
                        Tint(pawns[i], (Side)side, pool[i].Tapped, pool[i].Sick);
                }
        }

        Transform MakePawn(Side side, WorkerZone zone, int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = side + "_" + zone + "_worker" + index;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);

            float pitch = Board.CellSize + Board.CellGap;
            var row = Board.WorldOf(new CellRef(Rules.Board.RowFor(side, (SlotName)zone), 0));
            float edgeX = Rules.Board.Columns * pitch * 0.5f + 0.42f + (index / 4) * 0.32f;
            float x = side == Side.You ? -edgeX : edgeX;
            float z = row.z + ((index % 4) - 1.5f) * 0.27f;

            go.transform.localPosition = new Vector3(x, 0.36f, z);
            go.transform.localScale = new Vector3(0.22f, 0.3f, 0.22f);

            // a baked material so the shader variant survives build stripping (magenta fix)
            go.GetComponent<MeshRenderer>().sharedMaterial = Board.CellMaterial;
            return go.transform;
        }

        void Tint(Transform t, Side side, bool tapped, bool sick)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r == null) return;
            var tint = tapped ? TappedTint : (side == Side.You ? YouTint : FoeTint);
            if (sick) tint *= 0.55f;
            _mpb.SetColor("_BaseColor", tint);
            r.SetPropertyBlock(_mpb);
        }

        // ---- standees: every board object, rendered with its art ---------------------------

        void ReconcileStandees()
        {
            var s = Engine.State;
            var seen = new HashSet<int>();

            // Art quads billboard to the camera - a fixed lean goes edge-on (invisible) the
            // moment the player toggles to the top-down angle.
            var cam = Camera.main;
            var camRot = cam != null ? cam.transform.rotation : Quaternion.identity;

            foreach (var kv in s.Objects())
            {
                var cell = kv.Key;
                var o = kv.Value;
                seen.Add(o.Id);

                Transform t;
                if (!_standees.TryGetValue(o.Id, out t))
                {
                    t = MakeStandee(o);
                    _standees[o.Id] = t;
                }

                var target = Board.WorldOf(cell);
                t.localPosition = new Vector3(target.x, t.localPosition.y, target.z);

                var art = t.Find("art");
                if (art != null) art.rotation = camRot;

                var cr = o as CreatureUnit;
                if (cr != null)
                {
                    if (t.childCount > 0) Tint(t.GetChild(0), o.Owner, cr.Tapped, cr.Sick);
                    var sr = t.GetComponentInChildren<SpriteRenderer>();
                    if (sr != null)
                        sr.color = cr.Tapped ? new Color(0.5f, 0.5f, 0.55f)
                                 : cr.Sick ? new Color(0.75f, 0.75f, 0.8f) : Color.white;
                }
            }

            _deadStandees.Clear();
            foreach (var kv in _standees)
                if (!seen.Contains(kv.Key)) _deadStandees.Add(kv.Key);
            foreach (var id in _deadStandees)
            {
                Destroy(_standees[id].gameObject);
                _standees.Remove(id);
            }
        }

        Transform MakeStandee(BoardObject o)
        {
            var root = new GameObject("unit_" + o.Id);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;

            // plinth - a low disc the overlay label anchors to; owner-tinted
            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(plinth.GetComponent<Collider>());     // the CELL owns picking
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            plinth.transform.localScale = new Vector3(0.62f, 0.03f, 0.62f);
            plinth.GetComponent<MeshRenderer>().sharedMaterial = Board.CellMaterial;

            var tintTarget = root.transform;
            Tint(plinth.transform, o.Owner, false, false);

            Sprite art = ArtFor(o);
            if (art != null)
            {
                var spriteGo = new GameObject("art");
                spriteGo.transform.SetParent(root.transform, false);
                var sr = spriteGo.AddComponent<SpriteRenderer>();
                sr.sprite = art;
                float h = art.bounds.size.y;
                float scale = h > 0.01f ? 0.95f / h : 1f;
                spriteGo.transform.localScale = new Vector3(scale, scale, scale);
                spriteGo.transform.localPosition = new Vector3(0f, 0.12f + 0.95f * 0.5f, 0f);
                // rotation is set every frame in ReconcileStandees - full camera billboard
            }
            else
            {
                // face-downs and art-less cards: a card-back block (placeholders ship - G1)
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(block.GetComponent<Collider>());
                block.transform.SetParent(root.transform, false);
                block.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                block.transform.localScale = new Vector3(0.55f, 0.42f, 0.14f);
                block.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
                block.GetComponent<MeshRenderer>().sharedMaterial = Board.StructureSlotMaterial;
                Tint(block.transform, o.Owner, o is ChargeUnit || o is TrapUnit, false);
            }

            return tintTarget;
        }

        Sprite ArtFor(BoardObject o)
        {
            var cr = o as CreatureUnit;
            if (cr != null)
            {
                var def = DefOf(cr.Name) ?? DefOf(cr.Card.Value);
                if (def == null) return null;
                return def.FieldArt != null ? def.FieldArt : def.CardArt;
            }

            var b = o as StructureUnit;
            if (b != null)
            {
                var def = DefOfStructure(b.DefId, b.Color);
                if (def == null) return null;
                return def.FieldArt != null ? def.FieldArt : def.CardArt;
            }

            return null;                                   // charges and traps stay face-down
        }
    }
}
