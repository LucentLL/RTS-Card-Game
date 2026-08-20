using System.Collections.Generic;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Boots a real match on the real engine and keeps the scene a pure function of its state.
    ///
    /// This is the M9 slice, engine-wired: the deployed build now RUNS the rules core - phases,
    /// harvest, draw, the mana drain, worker pools - through the same DuelEngine command funnel
    /// everything else will use. The opponent is NOT an AI yet (that is M11): while it is the
    /// foe's turn this controller simply feeds the legal turn commands on a timer, which is
    /// exactly what the scripted policy will replace.
    /// </summary>
    public class MatchController : MonoBehaviour
    {
        public CardDatabase Database;          // assigned by SceneBootstrap; ships in the scene
        public BoardView Board;

        public DuelEngine Engine { get; private set; }
        public ulong Seed { get; private set; }

        private readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> Log { get { return _log; } }

        private float _beat;

        // worker pawn pools, keyed (side, zone) - reconciled to state every frame
        private readonly List<Transform>[,] _pawns = new List<Transform>[2, 3];
        private MaterialPropertyBlock _mpb;

        private static readonly Color YouTint = new Color(0.85f, 0.70f, 0.25f);
        private static readonly Color FoeTint = new Color(0.35f, 0.50f, 0.85f);
        private static readonly Color TappedTint = new Color(0.28f, 0.28f, 0.30f);

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

            var catalog = Database.ToCatalog();

            // The view picks the seed; the core only ever consumes it. Fixed commanders until
            // the character select lands (M15).
            Seed = (ulong)System.DateTime.Now.Ticks;
            var state = MatchSetup.NewMatch(catalog,
                new CommanderId("fire"), new CommanderId("water"), Seed, RulesOptions.JsParity);
            Engine = new DuelEngine(state, catalog);

            Push("— Your turn · Upkeep — ⛏ Harvest to begin —");
        }

        void Update()
        {
            if (Engine == null) return;
            PumpEvents();
            Autopilot();
            ReconcilePawns();
        }

        /// <summary>The HUD's one entry point. Rejections become hints, never exceptions.</summary>
        public Rejection TryHuman(ICommand cmd)
        {
            var r = Engine.Apply(cmd);
            if (r.Status == CommandStatus.Rejected) return r.Rejection;
            return Rejection.None;
        }

        /// <summary>
        /// The stand-in opponent: after a short beat, feed the next legal turn command. Also
        /// hands the turn across after the player's End phase.
        /// </summary>
        void Autopilot()
        {
            var s = Engine.State;
            if (s.IsOver) return;

            _beat += Time.deltaTime;
            if (_beat < 0.7f) return;

            if (s.Turn == Side.Foe)
            {
                _beat = 0f;
                switch (s.Phase)
                {
                    case TurnPhase.Upkeep: Engine.Apply(new HarvestCommand(Side.Foe)); break;
                    case TurnPhase.Draw: Engine.Apply(new DrawForTurnCommand(Side.Foe)); break;
                    case TurnPhase.Action: Engine.Apply(new EndTurnCommand(Side.Foe)); break;
                    case TurnPhase.End: Engine.Apply(new BeginTurnCommand(Side.You)); break;
                }
            }
            else if (s.Phase == TurnPhase.End)
            {
                _beat = 0f;
                Engine.Apply(new BeginTurnCommand(Side.Foe));
            }
        }

        void PumpEvents()
        {
            foreach (var ev in Engine.DrainEvents())
            {
                var line = Describe(ev);
                if (line != null) Push(line);
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
                return (Engine.State.Turn == Side.You ? "You harvest ◆" : "Foe harvests ◆") +
                       harvest.Amount + " (" + harvest.Zone.ToString().ToLowerInvariant() + ")";

            var drawn = ev as CardDrawn;
            if (drawn != null)
                return Engine.State.Turn == Side.You
                    ? "You draw " + drawn.Card.Value
                    : "Foe draws a card";

            var drained = ev as ManaDrained;
            if (drained != null)
                return drained.Lost > 0
                    ? "◆" + drained.Lost + " unspent mana drains away" +
                      (drained.Kept > 0 ? " — vaults keep ◆" + drained.Kept : "")
                    : "Vaults keep ◆" + drained.Kept;

            var yielded = ev as ManaYielded;
            if (yielded != null) return "A structure yields ◆" + yielded.Amount;

            var revived = ev as CreatureRevived;
            if (revived != null) return "The Reliquary returns " + revived.Card.Value + " to hand";

            var fired = ev as TowerFired;
            if (fired != null) return "A tower fires for " + fired.Amount;

            var ended = ev as MatchEnded;
            if (ended != null) return "— MATCH OVER: " + ended.Outcome + " —";

            return null;   // phase changes etc. are visible in the HUD already
        }

        // ---- worker pawns -------------------------------------------------------------------

        void ReconcilePawns()
        {
            var s = Engine.State;
            for (int side = 0; side < 2; side++)
            {
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
                        TintPawn(pawns[i], (Side)side, pool[i]);
                }
            }
        }

        Transform MakePawn(Side side, WorkerZone zone, int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = side + "_" + zone + "_worker" + index;
            Destroy(go.GetComponent<Collider>());   // pawns are scenery - the board owns picking
            go.transform.SetParent(transform, false);

            float pitch = Board.CellSize + Board.CellGap;
            var row = Board.WorldOf(new CellRef(Rules.Board.RowFor(side, (SlotName)zone), 0));
            float edgeX = (Rules.Board.Columns / 2f + 0.9f) * pitch;
            float x = side == Side.You ? -edgeX - index * 0.34f : edgeX + index * 0.34f;

            go.transform.localPosition = new Vector3(x, 0.36f, row.z);
            go.transform.localScale = new Vector3(0.22f, 0.3f, 0.22f);
            return go.transform;
        }

        void TintPawn(Transform pawn, Side side, CreatureUnit worker)
        {
            var baseTint = side == Side.You ? YouTint : FoeTint;
            var tint = worker.Tapped ? TappedTint : baseTint;
            if (worker.Sick) tint *= 0.55f;

            var r = pawn.GetComponent<MeshRenderer>();
            _mpb.SetColor("_BaseColor", tint);
            r.SetPropertyBlock(_mpb);
        }
    }
}
