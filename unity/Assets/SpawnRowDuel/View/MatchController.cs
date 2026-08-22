using System.Collections.Generic;
using SpawnRowDuel.Ai;
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
    /// The opponent is the real ScriptedAiPolicy - the ported foeTurn - pumped one command per
    /// beat by AiDriver so a human can watch it play. It goes through exactly the same command
    /// pipeline you do; there is no privileged AI path into the rules.
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
        private AiDriver _ai;
        private bool _aiFaulted;

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

            Catalog = Database.ToCatalog();
            // No match yet: the HUD shows the commander select first. Which commander you pick
            // decides your element pools and therefore your whole deck, so hard-coding it made
            // 35 of the 36 unreachable.
        }

        /// <summary>The catalog is available before a match exists - the select screen reads it.</summary>
        public ICardCatalog Catalog { get; private set; }

        public bool MatchStarted { get { return Engine != null; } }

        public void StartMatch(CommanderId you, CommanderId foe, ulong seed)
        {
            var state = MatchSetup.NewMatch(Catalog, you, foe, seed, RulesOptions.JsParity);
            Engine = new DuelEngine(state, Catalog);
            _ai = new AiDriver(Engine, new ScriptedAiPolicy(Side.Foe));

            _log.Clear();
            Push("— " + Catalog.Commander(you).Name + " vs " + Catalog.Commander(foe).Name + " —");
            Push("— Your turn · Upkeep — ⛏ Harvest to begin —");
            Touch();
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
            SendFrom = null;
            LegalCells.Clear();
            Touch();
        }

        // ---- moving banked mana between cards ---------------------------------------------

        /// <summary>The card whose banked ◆ is being moved, while the player picks a destination.</summary>
        public CellRef? SendFrom { get; private set; }

        /// <summary>
        /// startSendMana: arm the transfer. Banked mana is not decoration - it is what makes a
        /// face-down affordable next turn and what a play-on-top spends - so being able to move
        /// it off a card that is about to die is a real decision.
        /// </summary>
        public void BeginSendMana(CellRef from)
        {
            CancelPending();
            SendFrom = from;
            LegalCells.Clear();
            for (int i = 0; i < Rules.Board.Cells; i++)
            {
                var cell = CellRef.FromIndex(i);
                if (Engine.CanApply(new SendBankedManaCommand(Side.You, from, cell)) == Rejection.None)
                    LegalCells.Add(cell);
            }
            Touch();
        }

        public void TrySendBankedMana(CellRef to)
        {
            if (!SendFrom.HasValue) return;
            var why = TryHuman(new SendBankedManaCommand(Side.You, SendFrom.Value, to));
            if (why != Rejection.None) Push("· " + Hint(why));
            SendFrom = null;
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
            var def = Catalog.Structure(bid, color);
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


        // ---- the opponent -----------------------------------------------------------------

        /// <summary>
        /// The real scripted AI, one command per beat so a human can watch it think. It answers
        /// its own parked choices even on YOUR turn - it is the defender then - while choices
        /// addressed to you wait on the HUD.
        /// </summary>
        void Autopilot()
        {
            var s = Engine.State;
            if (s.IsOver) return;

            _beat += Time.deltaTime;
            if (_beat < 0.35f) return;

            if (s.Pending != null && s.Pending.Responder != Side.Foe) return;
            _beat = 0f;

            var report = new AiDriver.Report();
            if (_ai.Step(report))
            {
                Touch();
                return;
            }

            if (report.FirstRejection != Rejection.None && !_aiFaulted)
            {
                // An AI that proposes an illegal command is a policy bug. Say so once, loudly,
                // rather than stalling silently for the rest of the match.
                _aiFaulted = true;
                Push("· AI proposed an illegal " + report.FirstRejectionCommand
                     + " (" + report.FirstRejection + ")");
                Debug.LogError("[ai] illegal " + report.FirstRejectionCommand
                     + ": " + report.FirstRejection);
                return;
            }

            // nobody wants to act and a side has finished its turn: hand off
            if (s.Pending == null && s.Phase == TurnPhase.End)
                Apply(new BeginTurnCommand(TurnMachine.Other(s.Turn)));
        }

        void Apply(ICommand cmd)
        {
            if (Engine.Apply(cmd).Applied) Touch();
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
            if (bounced != null)
                return (bounced.Cause == BounceCause.Undertow ? "Undertow! " : "") +
                       "A creature is hurled back to " +
                       (bounced.ToHand == Side.You ? "your" : "their") + " hand";

            var sprung = ev as TrapSprung;
            if (sprung != null) return sprung.Card.Value + " springs!";

            var token = ev as TokenSpawned;
            if (token != null)
                return (token.Owner == Side.You ? "You conjure " : "They conjure ") + token.Name +
                       " (" + token.Attack / 500 + "/" + (token.Hp + 499) / 500 + ")";

            var hatched = ev as CreatureHatched;
            if (hatched != null)
                return "It hatches! " + hatched.NewName + " ⚔" + hatched.Attack / 500 +
                       "/♥" + (hatched.Hp + 499) / 500;

            var grew = ev as ChrysalisGrew;
            if (grew != null) return "A cocoon swells (" + grew.Count + "/" + grew.HatchAt + ")";

            var cast = ev as SpellResolved;
            if (cast != null)
                return (cast.Caster == Side.You ? "You cast " : "They cast ") + cast.Card.Value;

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
            else if (o is CreatureUnit && ((CreatureUnit)o).IsToken)
            {
                // Lumen and Shade have no registry card and so no art at all - give them a
                // small conjured orb rather than a card back, which reads as a face-down
                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(orb.GetComponent<Collider>());
                orb.transform.SetParent(root.transform, false);
                orb.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                orb.transform.localScale = new Vector3(0.34f, 0.34f, 0.34f);
                orb.GetComponent<MeshRenderer>().sharedMaterial = Board.CellMaterial;
                Tint(orb.transform, o.Owner, false, false);
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
