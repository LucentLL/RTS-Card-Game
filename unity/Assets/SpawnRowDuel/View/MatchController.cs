using System.Collections.Generic;
using SpawnRowDuel.Ai;
using SpawnRowDuel.Data;
using SpawnRowDuel.Net;
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

        /// <summary>
        /// Every event, as it is drained, for the surfaces that ANIMATE rather than render. The
        /// contract from Events.cs holds: render(GameState) is the truth at rest, react(GameEvent)
        /// is transient flair, and a listener that drops one costs an animation and never a wrong
        /// board.
        /// </summary>
        public event System.Action<GameEvent> Observed;

        /// <summary>
        /// A presentation hold: the AI stops proposing commands until this passes.
        ///
        /// A whole combat resolves inside one Apply, so without it the opponent's next move lands
        /// while the clash that just happened is still on screen - and the thing a player was
        /// trying to read gets overwritten by the thing that happened next. Only the combat
        /// theatre sets it, only for as long as a cut-in runs, and it never blocks YOUR input.
        /// </summary>
        public static float HoldUntil;

        public static void Hold(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            if (until > HoldUntil) HoldUntil = until;
        }

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

        private Dictionary<string, CardDefinition> _defByName;

        /// <summary>Yours warm, theirs cold - the same pair the worker capsules were tinted with
        /// before they became a number.</summary>
        public static readonly Color YouTint = new Color(0.85f, 0.70f, 0.25f);
        public static readonly Color FoeTint = new Color(0.35f, 0.50f, 0.85f);

        void Awake()
        {
            if (Board == null) Board = GetComponent<BoardView>();

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

        /// <summary>
        /// The live multiplayer session, or null in solo. When it is set it owns the engine, the
        /// AI is off, and every command the player makes goes out on the wire as well as into
        /// the board.
        /// </summary>
        public NetSession Net { get; private set; }

        public bool IsNetworked { get { return Net != null; } }

        public void StartMatch(CommanderId you, CommanderId foe, ulong seed)
        {
            StartMatch(you, foe, seed, null, null);
        }

        /// <summary>
        /// The same, with decks supplied. A null deck is rolled from the commander's pools the way
        /// it always was; a non-null one is a deck the player BUILT, which is the whole point of
        /// having built it.
        /// </summary>
        public void StartMatch(CommanderId you, CommanderId foe, ulong seed,
                               List<HandCard> youDeck, List<HandCard> foeDeck)
        {
            DropNet("started a solo duel");    // never merely dropped: the sockets need closing
            TakeSeat(Side.You);                // solo always sits at the near edge
            HoldUntil = 0f;                    // static: a new match must not inherit a stale hold
            var state = MatchSetup.NewMatch(Catalog, you, foe, youDeck, foeDeck, seed, RulesOptions.JsParity);
            Engine = new DuelEngine(state, Catalog);
            _ai = new AiDriver(Engine, new ScriptedAiPolicy(Seat.Remote));

            RollBattlefield(true);

            _log.Clear();
            Push("— " + Catalog.Commander(you).Name + " vs " + Catalog.Commander(foe).Name + " —");
            Push("— Your turn · Upkeep — ⛏ Harvest to begin —");
            Touch();
        }

        /// <summary>
        /// PUT THE MATCH DOWN. There is no duel after this until somebody starts one.
        ///
        /// Nothing used to do this, and `MatchStarted` is simply `Engine != null` - so quitting a
        /// duel left the whole match sitting in the controller, and "start a new duel" walked back
        /// into it. The commander select is drawn only while no match exists (MatchHud.OnGUI), so
        /// the select never appeared either: the player pressed Duel and was handed the board they
        /// had just walked away from, mid-turn.
        ///
        /// Everything that is ABOUT a match goes with it, not just the engine. A stale aim, a
        /// half-armed play, a confirm waiting on an answer or a log from the last duel are all
        /// things the next one would inherit, and the AI driver holds a reference to the engine
        /// that is going away.
        /// </summary>
        /// <summary>
        /// Let go of a live duel's session, and CLOSE IT.
        ///
        /// `Dispose` is the only path to the transport's own Dispose, and the transport is up to
        /// three MQTT websockets. `MultiplayerUi` hands ownership over on purpose (it nulls its own
        /// references the moment the session is adopted) precisely so the session has one owner -
        /// and that owner never closed it, so every Duel-a-Friend leaked its sockets for the rest
        /// of the process.
        /// </summary>
        void DropNet(string why)
        {
            if (Net == null) return;
            Net.Leave(why);                    // the other player is owed the news...
            Net.Dispose();                     // ...and the sockets are owed closing
            Net = null;
        }

        public void EndMatch(string why = "left the match")
        {
            DropNet(why);

            Engine = null;
            _ai = null;
            _aiFaulted = false;

            Pending = Intent.None;
            PendingBuild = null;
            SendFrom = null;
            LegalCells.Clear();

            Assault = null;
            AssaultCell = null;
            AssaultLabel = null;
            Asking = null;

            HoldUntil = 0f;                    // static: a presentation hold outlives nothing
            _log.Clear();
            Touch();                           // every painter re-reads and finds nothing to draw
        }

        /// <summary>
        /// Where this battle is fought. Every match was a meadow, because Grass is the static
        /// default and nothing ever changed it outside the settings menu.
        ///
        /// Rolled off the OPENING STATE HASH rather than a local random, which costs nothing and
        /// buys the thing that matters in a duel: both players are standing in the same field.
        /// The hash is the one number both engines have computed and agreed on before either has
        /// moved, so no message has to carry the choice and the two cannot disagree.
        ///
        /// Shore is left out of the roll. It is the tide biome, it is beautiful, and half its
        /// board spends the match under water.
        /// </summary>
        public static readonly World.BiomeId[] Battlefields =
        {
            World.BiomeId.Grass, World.BiomeId.Sand, World.BiomeId.Ash,
            World.BiomeId.Snow, World.BiomeId.Earth,
        };

        /// <summary>
        /// True while the player has NAMED an arena on the commander screen. Static, like the
        /// biome it governs, and false by default so a duel nobody has an opinion about is rolled
        /// rather than being a meadow forever.
        /// </summary>
        public static bool ArenaChosen;

        void RollBattlefield(bool honourTheMenu)
        {
            if (Engine == null) return;
            if (honourTheMenu && ArenaChosen) return;     // the player named the ground
            World.TerrainField.Requested = BattlefieldFor(Engine.Hash());
        }

        /// <summary>The mapping itself, pure, so it can be tested without a match.</summary>
        public static World.BiomeId BattlefieldFor(ulong hash)
        {
            hash ^= hash >> 33; hash *= 0xFF51AFD7ED558CCDUL; hash ^= hash >> 33;  // murmur3 mix
            return Battlefields[(int)(hash % (ulong)Battlefields.Length)];
        }

        /// <summary>
        /// Take over a match a NetSession has agreed with another player. The engine comes FROM
        /// the session - both peers built it from the same MatchConfig and checked the same
        /// opening hash - and this side never constructs one of its own, because a board built
        /// here could differ from theirs by a byte, and that is the entire failure mode netcode
        /// has.
        /// </summary>
        public void AdoptNetMatch(NetSession session)
        {
            Net = session;
            TakeSeat(session.LocalSide);
            HoldUntil = 0f;
            Engine = session.Engine;
            _ai = null;                        // there is a person over there
            _aiFaulted = false;
            CancelPending();

            // NOT the menu's choice: both peers must be in the same field, and one player's
            // arena button cannot bind the other's board.
            RollBattlefield(false);

            _log.Clear();
            var mine = Catalog.Commander(Engine.State.P(Seat.Local).Commander);
            var theirs = Catalog.Commander(Engine.State.P(Seat.Remote).Commander);
            Push("- " + mine.Name + " vs " + theirs.Name + " -");
            Push(Engine.State.Turn == Seat.Local ? "- Your turn -" : "- Their turn -");
            Touch();
        }

        /// <summary>
        /// Which end of the board we are sitting at. ASSIGNED on every match start, never merely
        /// reset: a seat that leaked out of a multiplayer game would yaw the next campaign
        /// battle's camera and address every command to the wrong side.
        /// </summary>
        void TakeSeat(Side local)
        {
            Seat.Take(local);
            _names.Clear();                    // last match's dead are not this one's
            if (Board != null) Board.ApplySeat();
        }

        /// <summary>
        /// Drive the session once a frame. It is the only thing in the netcode that moves time,
        /// so the whole protocol - advertising, pings, retries, the opponent's commands landing -
        /// happens here and nowhere else.
        ///
        /// A rebuilt match (a reconnect handed us the game back) replaces the engine underneath
        /// us, so the engine is re-read every frame rather than cached at adoption.
        /// </summary>
        void PumpNet()
        {
            if (Net == null) return;

            Net.Pump(Time.unscaledDeltaTime);

            if (Net.Engine != null && !ReferenceEquals(Net.Engine, Engine))
            {
                Engine = Net.Engine;
                CancelPending();
                EndAssault();
                Push("- reconnected -");
                Touch();
            }
        }

        void Update()
        {
            PumpNet();
            if (Engine == null) return;
            PumpEvents();
            ExpireAssault();
            Autopilot();
            // Board objects are NOT reconciled here any more. They were, once - a tinted plinth
            // disc with a billboarded FieldArt-or-CardArt quad standing on it - and that system
            // outlived its replacement without anyone noticing, so every unit was being drawn
            // twice: once by this, once by StandeeLayer. The plinth is the bright ellipse that
            // sat under every figure. CardPlateLayer (the card, flat on the tile) and
            // StandeeLayer (the cut-out, hovering over it) own the board now.
        }

        /// <summary>
        /// The attack group is a live interaction, not a stored one: the moment its declarations
        /// are gone (combat resolved) or the moment it stops being your action phase, a tap on
        /// your own creature has to mean "select" again.
        /// </summary>
        void ExpireAssault()
        {
            if (Assault == null) return;
            var s = Engine.State;
            if (!s.Combat.HasDeclarations || s.Turn != Seat.Local || s.Phase != TurnPhase.Action)
                EndAssault();
        }

        // ---- commands from the HUD / input ------------------------------------------------

        public Rejection TryHuman(ICommand cmd)
        {
            var why = Submit(cmd);
            if (why != Rejection.None) return why;
            PumpEvents();                      // NOW, not next frame - see PumpEvents
            Touch();
            return Rejection.None;
        }

        /// <summary>
        /// The one door out of the view into the rules. In solo it is the engine; in a duel it is
        /// the session, which applies the command here AND puts it on the wire in the same
        /// breath. Nothing in the view calls Engine.Apply directly any more, because a command
        /// that reached the board without reaching the opponent is a desync.
        /// </summary>
        public Rejection Submit(ICommand cmd)
        {
            if (Net != null) return Net.Submit(cmd);
            var r = Engine.Apply(cmd);
            return r.Status == CommandStatus.Rejected ? r.Rejection : Rejection.None;
        }

        /// <summary>
        /// Pure what-if, for lighting up legal cells. It goes through the session in a duel so
        /// that the session's extra gates - not your turn, still catching up after a reconnect -
        /// grey the board out rather than letting a tap be refused after the fact.
        /// </summary>
        public Rejection Probe(ICommand cmd)
        {
            return Net != null ? Net.CanSubmit(cmd) : Engine.CanApply(cmd);
        }

        /// <summary>Arm a hand play; the next legal-cell tap completes it.</summary>
        public void BeginPlay(int handIndex, Rules.PlayMode mode)
        {
            Pending = Intent.PlayCard;
            PendingHandIndex = handIndex;
            PendingMode = mode;
            PendingBuild = null;
            ProbeLegalCells(cell => new PlayCardCommand(Seat.Local, handIndex, mode, cell));
        }

        public void BeginBuild(StructureDef def)
        {
            Pending = Intent.Build;
            PendingBuild = def;
            ProbeLegalCells(cell => new BuildStructureCommand(Seat.Local, def.Bid, def.Element, cell));
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
                if (Probe(new SendBankedManaCommand(Seat.Local, from, cell)) == Rejection.None)
                    LegalCells.Add(cell);
            }
            Touch();
        }

        public void TrySendBankedMana(CellRef to)
        {
            if (!SendFrom.HasValue) return;
            var why = TryHuman(new SendBankedManaCommand(Seat.Local, SendFrom.Value, to));
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
                if (Probe(make(cell)) == Rejection.None) LegalCells.Add(cell);
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
                ? (ICommand)new PlayCardCommand(Seat.Local, PendingHandIndex, PendingMode, cell)
                : new BuildStructureCommand(Seat.Local, PendingBuild.Bid, PendingBuild.Element, cell);

            // A drop onto a card of your own RAZES it. That is a legal play and sometimes the
            // right one, and it is never what a mis-aimed finger meant, so it is asked first.
            var standing = Engine.State.At(cell);
            if (standing != null && standing.Owner == Seat.Local)
            {
                var no = Probe(cmd);
                if (no != Rejection.None) { Push("· " + Hint(no)); return true; }
                int banked = Mana.OnCard(standing);
                AskConfirm(cmd, "Play over " + NameOf(standing),
                           "It is destroyed to make room"
                           + (banked > 0 ? " — its ◆" + banked + " carries over." : "."));
                return true;
            }

            // TryHuman, not Submit. This was the ONE human command path that skipped the event
            // pump - every other one goes through TryHuman - so a summon or a build left its
            // events (UnitSummoned, ManaChanged, and on a trap the whole cascade) sitting in the
            // sink until some later frame drained them, with a full LateUpdate in between. The
            // contract at PumpEvents says immediately after every command, and it says it because
            // a listener that animates a board it has not been told about yet is the whole class
            // of bug this is in.
            var why = Submit(cmd);
            if (why != Rejection.None)
            {
                Push("· " + Hint(why));
                return true;               // consumed - the armed card stays armed
            }

            PumpEvents();
            CancelPending();
            return true;
        }

        /// <summary>
        /// May this cell's creature declare an attack RIGHT NOW?
        ///
        /// DeclareAttackHandler's attacker ladder (CombatHandlers.cs), lifted so the view can ask
        /// the same question of many cells at once without building a command per cell. It is the
        /// membership test for a drag-selection: a gesture must never pick up a creature the
        /// engine would refuse, or the group silently loses members at declare time.
        ///
        /// Deliberately attacker-side ONLY. There is no reach or column gate in Validate, so a
        /// creature that passes this may attack anything the anchor may - which is what lets a
        /// group share one target set instead of intersecting N of them.
        /// </summary>
        public static bool IsReadyAttacker(GameState s, CellRef cell)
        {
            if (s == null) return false;
            if (s.Turn != Seat.Local || s.Phase != TurnPhase.Action) return false;

            var c = s.At(cell) as CreatureUnit;
            return c != null && c.Owner == Seat.Local
                && !c.IsWorker && !c.Sick && !c.Tapped && c.Hp > 0;
        }

        /// <summary>Enemy cells this creature may legally declare an attack on right now.</summary>
        public List<CellRef> LegalAttacksFor(CellRef from)
        {
            var targets = new List<CellRef>();
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null || u.Owner != Seat.Local) return targets;

            foreach (var kv in Engine.State.Objects())
            {
                if (kv.Value.Owner == Seat.Local) continue;
                var cmd = new DeclareAttackCommand(Seat.Local, from, u.Id,
                    new UnitTarget(kv.Key, kv.Value.Id), true);
                if (Probe(cmd) == Rejection.None) targets.Add(kv.Key);
            }
            return targets;
        }

        // ---- the attack group ------------------------------------------------------------

        /// <summary>
        /// What this turn's attack is aimed at, once anything has declared against it.
        ///
        /// Combat v3 has always had the JOINT attack - several creatures declared against one
        /// target, blocked from the union of the rows they cross - and there is deliberately no
        /// group command for it (spec 03 §6.2): a joint attack IS N declarations sharing a target.
        /// The board made the player spell that out one creature at a time, though - select, aim,
        /// tap the target, select the next, aim again, tap the same target again - and re-picking
        /// a target you have already picked is not a decision. While an assault is live, tapping
        /// one of your ready creatures JOINS it.
        ///
        /// It is live only as long as the declarations it belongs to: a resolved combat, the end
        /// of your action phase, or ✕ CANCEL under the board clears it. So a tap on your own
        /// creature means "select" again the moment the attack is over, and can never quietly
        /// become a declaration on some later turn.
        /// </summary>
        public AttackTarget Assault { get; private set; }

        /// <summary>Where the assault is aimed, when it is aimed at a unit - so the board can ring
        /// the target even with no attacker selected.</summary>
        public CellRef? AssaultCell { get; private set; }

        /// <summary>
        /// Is the card on this cell one of the creatures currently declared into the attack?
        ///
        /// Reported so the board can LIGHT THEM. A declaration taps the attacker and otherwise
        /// leaves it standing where it was, so a group of three attacking creatures looked exactly
        /// like a group of three creatures - and two copies of the same card with different health
        /// are indistinguishable in a hand-sized picture. Which ones are swinging is the single
        /// most important thing on the board while an attack is being built, and it was being
        /// carried entirely by the player's memory.
        /// </summary>
        public bool IsAttacking(CellRef cell)
        {
            if (Engine == null) return false;
            var decls = Engine.State.Combat.Declarations;
            for (int i = 0; i < decls.Count; i++)
                if (decls[i].Attacker == cell) return true;
            return false;
        }

        /// <summary>What it is aimed at, in words, for the mode row.</summary>
        public string AssaultLabel { get; private set; }

        /// <summary>How many creatures are in it.</summary>
        public int AssaultSize
        {
            get { return Engine == null ? 0 : Engine.State.Combat.Declarations.Count; }
        }

        /// <summary>
        /// Every attack the PLAYER declares goes through here - the board's taps, the wall button,
        /// the worker-stack buttons - so the assault is set in one place instead of in three of
        /// the four paths that can declare one.
        ///
        /// BLOCKERS ARE DEFERRED, always (the spec 03 s12 cadence the AI already attacked with).
        /// A declaration used to park a BlockerRequest on the defender there and then, which puts
        /// the defender inside the attacker's own decision: against a human that reads as "the
        /// opponent gets to respond before I have even said whether this is a single attack or a
        /// group", and every further tap meets ChoicePending while they think about it. The whole
        /// assault is now declared first and answered afterwards, one declaration at a time, with
        /// the defender seeing the complete attack - which is the order this was always meant to
        /// happen in, and the order two people at a table would do it in:
        ///
        ///     attacker: pick the attackers, pick the target, CONFIRM
        ///     defender: pick the blockers, COMMIT - once per declaration
        ///     then the pairing choices alternate, attacker, defender, until nothing is unassigned
        ///
        /// <see cref="ConfirmAssault"/> is the confirm, and no CHOICE is put to the other seat
        /// before it. (Each declaration itself does cross the wire as it is made - lockstep sends
        /// every command as it applies it - so a duelling defender watches the assault form. They
        /// are not asked to answer it, and until the confirm <see cref="WithdrawAssault"/> can
        /// still take the whole thing back.)
        /// </summary>
        public Rejection Declare(CellRef from, AttackTarget target, string label)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null) return Rejection.NoSuchUnit;

            var why = TryHuman(new DeclareAttackCommand(Seat.Local, from, u.Id, target, true));
            if (why != Rejection.None) return why;

            Assault = target;
            AssaultLabel = label;
            var ut = target as UnitTarget;
            AssaultCell = ut != null ? ut.Cell : (CellRef?)null;

            Touch();
            return Rejection.None;
        }

        /// <summary>
        /// The attacker's CONFIRM: the group is closed, and combat resolves.
        ///
        /// This is the moment the defender is first asked anything. Resolution collects the
        /// deferred blocker answers in declaration order before a single blow lands, so the
        /// defender answers each attack knowing the whole shape of the assault.
        /// </summary>
        public Rejection ConfirmAssault()
        {
            if (Engine == null || !Engine.State.Combat.HasDeclarations)
                return Rejection.NothingDeclared;

            var why = TryHuman(new ResolveCombatCommand(Seat.Local));
            if (why != Rejection.None) return why;

            EndAssault();
            return Rejection.None;
        }

        /// <summary>A tap on an enemy object while one of your ready creatures is selected.</summary>
        public Rejection TryAttack(CellRef from, CellRef targetCell)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            var t = Engine.State.At(targetCell);
            if (u == null || t == null) return Rejection.NoSuchUnit;
            return Declare(from, new UnitTarget(targetCell, t.Id), NameOf(t));
        }

        public Rejection TryAttackWall(CellRef from)
        {
            return Declare(from, new WallTarget(Seat.Remote), "the wall");
        }

        /// <summary>Would this creature be allowed to join the standing assault? The engine's own
        /// answer - the same probe the lit cells are.</summary>
        public bool CanJoinAssault(CellRef from)
        {
            if (Assault == null || Engine == null) return false;
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null || u.Owner != Seat.Local) return false;
            return Probe(new DeclareAttackCommand(Seat.Local, from, u.Id, Assault, true))
                   == Rejection.None;
        }

        public Rejection JoinAssault(CellRef from)
        {
            if (Assault == null) return Rejection.NothingDeclared;
            return Declare(from, Assault, AssaultLabel);
        }

        /// <summary>Forget the AIM. The declarations themselves are untouched - this is the view
        /// letting go of an assault the engine has already resolved or expired, never a cancel.
        /// <see cref="WithdrawAssault"/> is the cancel.</summary>
        public void EndAssault()
        {
            if (Assault == null) return;
            Assault = null;
            AssaultCell = null;
            AssaultLabel = null;
            Touch();
        }

        /// <summary>
        /// CANCEL: take the whole assault back, and stand its attackers up again.
        ///
        /// Aiming used to be one-way. Tapping a target DECLARED, and a declaration taps the
        /// attacker - so a misaimed group, or simply a change of mind, cost those creatures their
        /// whole turn with no way back, and the button that looked like the way out ("LATER") only
        /// stopped the view from talking about an attack that was still standing in the engine.
        /// The engine has a real retract now (WithdrawAttackCommand); this is its one caller.
        /// </summary>
        public Rejection WithdrawAssault()
        {
            if (Engine == null) return Rejection.NothingDeclared;
            if (!Engine.State.Combat.HasDeclarations) { EndAssault(); return Rejection.None; }

            var why = TryHuman(new WithdrawAttackCommand(Seat.Local));
            if (why != Rejection.None) return why;

            EndAssault();
            return Rejection.None;
        }

        /// <summary>
        /// Is there an attack of yours standing on the board right now, waiting to be confirmed?
        ///
        /// The ENGINE's answer, not the view's. <see cref="Assault"/> is the aim - what a further
        /// tap would join - and it is dropped in places the declarations survive: a reconnect
        /// hands the match back with its combat intact and the aim gone. The row under the board
        /// is the only way to confirm or cancel now, so it has to appear for the declarations
        /// rather than for the aim, or a reconnect mid-assault would strand them.
        /// </summary>
        public bool AttackStanding
        {
            get
            {
                if (Engine == null) return false;
                var s = Engine.State;
                return s.Combat.HasDeclarations && !s.Combat.Resolving
                    && s.Turn == Seat.Local && s.Phase == TurnPhase.Action;
            }
        }

        /// <summary>
        /// What the standing attack is aimed at, in words.
        ///
        /// Read off the DECLARATIONS, not off <see cref="AssaultLabel"/>, and only when they agree.
        /// Nothing stops declarations with different targets coexisting - DeclareAttackHandler
        /// simply appends, and after a reconnect a fresh declaration lands beside the ones that
        /// survived - and the aim's label names only the newest of them. "⚔3 on Brinekin" for an
        /// attack where one of the three is on Brinekin is a lie the player would resolve on.
        /// </summary>
        public string StandingAttackLabel
        {
            get
            {
                if (Engine == null || !Engine.State.Combat.HasDeclarations)
                    return AssaultLabel ?? "their line";

                var s = Engine.State;
                var decls = s.Combat.Declarations;
                var d = decls[0];
                for (int i = 1; i < decls.Count; i++)
                    if (!SameTarget(d, decls[i])) return "several targets";

                if (d.Kind == DeclarationKind.Wall) return "the wall";
                if (d.Kind == DeclarationKind.WorkerStack)
                    return "their " + (d.TargetZone == WorkerZone.Back ? "back"
                                     : d.TargetZone == WorkerZone.Front ? "front" : "centre")
                         + " workers";

                CellRef at;
                bool onBoard;
                var t = s.FindById(d.TargetUnitId, out at, out onBoard);
                return t != null ? NameOf(t) : "their line";
            }
        }

        static bool SameTarget(AttackDeclaration a, AttackDeclaration b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == DeclarationKind.Unit) return a.TargetUnitId == b.TargetUnitId;
            if (a.Kind == DeclarationKind.Wall) return a.TargetSide == b.TargetSide;
            return a.TargetSide == b.TargetSide && a.TargetZone == b.TargetZone;
        }

        // The old SettleDefenderChoice lived here: a pump that made the AI answer the
        // BlockerRequest your declaration had just parked, before your next tap could meet it as
        // ChoicePending. Deferring the blockers removes the request it existed to drain - nothing
        // is parked on the defender until CONFIRM - and against a human there was never an AI to
        // pump anyway. The answers now arrive during resolution, on the autopilot's own beat,
        // where the theatre can pace them.

        /// <summary>What to call a thing that has just been attacked.</summary>
        /// <summary>What to call a thing that has just been attacked. Public because the group
        /// declare in BoardInput labels its assault the same way a single one does.</summary>
        public static string NameOfObject(BoardObject o) { return NameOf(o); }

        static string NameOf(BoardObject o)
        {
            var c = o as CreatureUnit;
            if (c != null) return c.Name;
            var b = o as StructureUnit;
            if (b != null) return string.IsNullOrEmpty(b.Name) ? b.DefId.Value : b.Name;
            return "the face-down card";
        }

        /// <summary>Every cell this creature may step to, discovered by probing the engine.</summary>
        public List<CellRef> LegalMovesFor(CellRef from)
        {
            var moves = new List<CellRef>();
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null || u.Owner != Seat.Local) return moves;

            System.Span<CellRef> buf = stackalloc CellRef[Rules.Board.MaxStepTargets];
            int n = Rules.Board.StepTargets(from, buf);
            for (int i = 0; i < n; i++)
                if (Probe(new MoveUnitCommand(Seat.Local, from, buf[i], u.Id)) == Rejection.None)
                    moves.Add(buf[i]);
            return moves;
        }

        /// <summary>
        /// Which of those cells cost one of your own cards to enter. The tap ASKS before it
        /// commits - a move that razes your own Citadel is legal, is sometimes right, and must
        /// never happen because a finger landed one row over.
        /// </summary>
        public bool MoveRazes(CellRef to)
        {
            var occ = Engine.State.At(to);
            return occ != null && occ.Owner == Seat.Local;
        }

        public Rejection TryMove(CellRef from, CellRef to)
        {
            var u = Engine.State.At(from) as CreatureUnit;
            if (u == null) return Rejection.NoSuchUnit;

            var cmd = new MoveUnitCommand(Seat.Local, from, to, u.Id);
            if (MoveRazes(to))
            {
                var why = Probe(cmd);
                if (why != Rejection.None) return why;
                AskConfirm(cmd, u.Name + " advances onto " + NameOf(Engine.State.At(to)),
                           "The row is full — it is destroyed to make room.", to);
                return Rejection.None;
            }
            return TryHuman(cmd);
        }

        // ---- the one destructive tap ---------------------------------------------------------

        /// <summary>
        /// A command the player has aimed but not yet agreed to.
        ///
        /// Exactly one thing needs this: a play or a move that lands on a card of your own and
        /// razes it. Everything else on this board is either reversible or obviously what it
        /// looks like. Held on the controller rather than in the HUD so the answer goes through
        /// the same one door every other command does - in a duel that is the session, and a
        /// confirmation that skipped it would be a desync.
        /// </summary>
        public sealed class Ask
        {
            public ICommand Command;
            public string What;      // "Magmaw advances onto The Foundry"
            public string Cost;      // "It is destroyed to make room."

            /// <summary>The cell to SELECT instead, when the tap that raised this could just as
            /// well have meant "pick that card up". A move onto your own card is the one place
            /// the two readings of a tap collide.</summary>
            public CellRef? Instead;
        }

        public Ask Asking { get; private set; }

        void AskConfirm(ICommand cmd, string what, string cost)
        {
            AskConfirm(cmd, what, cost, null);
        }

        void AskConfirm(ICommand cmd, string what, string cost, CellRef? instead)
        {
            Asking = new Ask { Command = cmd, What = what, Cost = cost, Instead = instead };
            Touch();
        }

        /// <summary>Answer it. false simply drops the command - nothing was submitted.</summary>
        public Rejection ResolveAsk(bool yes)
        {
            var ask = Asking;
            Asking = null;
            if (ask == null || !yes) { Touch(); return Rejection.None; }

            var why = TryHuman(ask.Command);
            if (why == Rejection.None) CancelPending();
            Touch();
            return why;
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

        /// <summary>
        /// The art record for anything standing on the board - the one place that knows a
        /// structure is not found the way a creature is.
        ///
        /// A creature answers to its display name. A structure does not: its board identity is a
        /// StructId plus a resolved forge colour, and only the catalog can turn that pair into the
        /// export key the database is filed under. Both board layers ask here rather than each
        /// carrying its own half-right guess, which is how the old standee system ended up being
        /// the only one that could find a forge.
        /// </summary>
        public CardDefinition DefOfObject(BoardObject o)
        {
            var cr = o as CreatureUnit;
            if (cr != null) return DefOf(cr.Name) ?? DefOf(cr.Card.Value);

            var b = o as StructureUnit;
            if (b != null) return DefOfStructure(b.DefId, b.Color) ?? DefOf(b.Name);

            return null;                                   // charges and traps stay face-down
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
                case Rejection.CellOccupied: return "That spot is taken";
                case Rejection.MissingPrereq: return "Missing a prerequisite structure";
                case Rejection.RowLacksWorkers: return "That row has no workers to spare";
                case Rejection.MoveAlreadySpent: return "Its move is spent";
                case Rejection.ChargeUnderfunded: return "Pour more ◆ before flipping";
                case Rejection.DeclarationsPending:
                    return "Confirm the attack below — ⚔ ATTACK, or ✕ CANCEL it";
                case Rejection.NothingDeclared: return "Nothing has been declared";
                case Rejection.BlockersCommitted:
                    return "Too late to call it off — the defenders are already in";
                case Rejection.ChoicePending: return "Waiting on a choice";
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

            // a cut-in is on screen: let the player watch it before the next move lands
            if (Time.unscaledTime < HoldUntil) return;

            if (Net != null) { NetAutopilot(s); return; }

            _beat += Time.deltaTime;
            if (_beat < 0.35f) return;

            if (s.Pending != null && s.Pending.Responder != Seat.Remote) return;
            _beat = 0f;

            var report = new AiDriver.Report();
            if (_ai.Step(report))
            {
                PumpEvents();
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
            {
                Apply(new BeginTurnCommand(TurnMachine.Other(s.Turn)));
                return;
            }

            // ...and if nobody wants to act and the turn is NOT over, the match is wedged.
            //
            // Once _aiFaulted latches, the branch above it is skipped for good and the hand-off is
            // guarded on there being no parked choice - so a policy that faults while a choice is
            // parked on the foe leaves a board that paints perfectly and answers nothing, forever,
            // with one error five seconds in the log and then silence. Say it again, and keep
            // saying it: a stall a player cannot see is a stall they report as "it froze".
            if (_aiFaulted && Time.unscaledTime >= _stallSaidAt + 6f)
            {
                _stallSaidAt = Time.unscaledTime;
                Push("· the opponent is stuck in " + s.Phase
                     + (s.Pending != null ? " waiting on a choice" : "") + " - the match cannot go on");
            }
        }

        float _stallSaidAt = -99f;

        /// <summary>
        /// The duel's version of the opponent: there isn't one. Nobody here plays for the other
        /// side - their commands arrive over the wire and are applied by the session - so the
        /// only thing left to do automatically is the turn hand-off.
        ///
        /// And that is OURS to issue, not theirs. BeginTurn is validated as "the incoming side
        /// only" (BeginTurnHandler), so each peer starts its own turn. Doing it here rather than
        /// waiting for the other end also means the hand-off costs no round trip.
        /// </summary>
        void NetAutopilot(GameState s)
        {
            if (s.Pending != null) return;
            if (s.Phase != TurnPhase.End) return;
            if (TurnMachine.Other(s.Turn) != Seat.Local) return;    // their turn to begin

            _beat += Time.deltaTime;
            if (_beat < 0.25f) return;                              // let the end of a turn land
            _beat = 0f;

            Apply(new BeginTurnCommand(Seat.Local));
        }

        void Apply(ICommand cmd)
        {
            if (Submit(cmd) == Rejection.None) { PumpEvents(); Touch(); }
        }

        // ---- events -> log ----------------------------------------------------------------

        /// <summary>
        /// Drain the engine's events to the log, the field and the theatre.
        ///
        /// Called IMMEDIATELY after every command, not once per frame at the top of Update. The
        /// difference is not tidiness: a listener that animates has to see the board as it was
        /// BEFORE the events it is being told about, and the only copy of that is a snapshot taken
        /// at the end of the previous frame. Pumping a frame late puts a LateUpdate in between -
        /// the snapshot is refreshed to the state AFTER the fight, and the cut-in for the blow
        /// that killed a creature can no longer find the creature it killed.
        ///
        /// Update still pumps as well, as a catch-all for anything applied from outside this
        /// class. Draining twice is free: the sink empties.
        /// </summary>
        void PumpEvents()
        {
            foreach (var ev in Engine.DrainEvents())
            {
                var line = Describe(ev);
                if (line != null) Push(line);
                Blow(ev);
                if (Observed != null) Observed(ev);
                Touch();
            }
            RememberNames();                   // LAST - see the field's own doc comment
        }

        /// <summary>
        /// Who was standing where, as of the end of this batch of events.
        ///
        /// A whole combat resolves inside one Apply, so by the time its events are described the
        /// loser is already off the board and there is nothing left to read a name off - which is
        /// why the log has always said "a creature falls" and could not say who took what. The
        /// board at the END of one pump is the board BEFORE the next one, so remembering it here
        /// is enough to name the dead exactly once, when it matters.
        ///
        /// It only ever grows by the units a single match creates - a few dozen - and is cleared
        /// when a seat is taken, which every match start does.
        /// </summary>
        readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        void RememberNames()
        {
            foreach (var kv in Engine.State.Objects())
            {
                var c = kv.Value as CreatureUnit;
                if (c != null && !c.IsWorker) { _names[c.Id] = c.Name; continue; }
                var b = kv.Value as StructureUnit;
                if (b != null) _names[b.Id] = string.IsNullOrEmpty(b.Name) ? b.DefId.Value : b.Name;
            }
        }

        /// <summary>
        /// The events the FIELD should feel. A card landing, a spell going off, a wall taking a
        /// hit - each rolls a ring of wind out through the grass from where it happened.
        ///
        /// Deliberately a small list, and deliberately not "every event": grass that twitches at
        /// every mana tick is noise, and noise is what a reactive effect has to avoid to keep
        /// meaning "something just happened there".
        /// </summary>
        void Blow(GameEvent ev)
        {
            if (Board == null) return;

            var summoned = ev as UnitSummoned;
            if (summoned != null) { World.TerrainField.Gust(Board.WorldOf(summoned.At), 0.9f); return; }

            var raised = ev as StructureRaised;
            if (raised != null) { World.TerrainField.Gust(Board.WorldOf(raised.At), 1f); return; }

            var flipped = ev as CardFlipped;
            if (flipped != null) { World.TerrainField.Gust(Board.WorldOf(flipped.At), 0.8f); return; }

            var sprung = ev as TrapSprung;
            if (sprung != null) { World.TerrainField.Gust(Board.WorldOf(sprung.At), 1f); return; }

            var cast = ev as SpellResolved;
            if (cast != null && cast.HasTarget)
            { World.TerrainField.Gust(Board.WorldOf(cast.Target), 1f); return; }

            // OnBoard, because a pool worker's `At` is its ZONE ROW, not a cell it ever stood in
            var killed = ev as UnitDestroyed;
            if (killed != null && killed.OnBoard)
            { World.TerrainField.Gust(Board.WorldOf(killed.At), 0.75f); return; }
        }

        /// <summary>
        /// Put a line in the match log from OUTSIDE the controller.
        ///
        /// The log is the engine's narration and the view has no business writing to it - with
        /// one exception, which is the view failing. A HUD that throws takes every control off the
        /// screen and leaves a board that paints and answers nothing, and on a build whose only
        /// test surface is a public URL the log is the one channel that can say so.
        /// </summary>
        public void Note(string line) { Push(line); }

        void Push(string line)
        {
            _log.Add(line);
            if (_log.Count > 40) _log.RemoveAt(0);
        }

        string Describe(GameEvent ev)
        {
            var turn = ev as TurnStarted;
            if (turn != null)
                return "— " + (turn.Side == Seat.Local ? "Your" : "Foe") + " turn " +
                       turn.TurnNumber + " · Upkeep —";

            var harvest = ev as HarvestCollected;
            if (harvest != null)
                return (Engine.State.Turn == Seat.Local ? "You harvest ◆" : "Foe harvests ◆") + harvest.Amount;

            var drawn = ev as CardDrawn;
            if (drawn != null)
                return Engine.State.Turn == Seat.Local ? "You draw " + drawn.Card.Value : "Foe draws a card";

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

            // What a blow actually did - the number that floats over the card and then, until
            // now, existed nowhere else. The main combat path sums a whole gang's damage into one
            // batch before applying it, so it carries no single source; the trigger sites (a
            // tower, a trap, a Detonate) do, and say so.
            var hit = ev as DamageApplied;
            if (hit != null && hit.Amount > 0)
            {
                string blow = "⚔" + Stat.Show(hit.Amount)
                            + (hit.Tier == DamageTier.FirstStrike ? " (first strike)" : "");
                string left = Remaining(hit.TargetId);
                return (hit.SourceId != 0
                        ? NameOf(hit.SourceId) + " hits " + NameOf(hit.TargetId) + " for " + blow
                        : NameOf(hit.TargetId) + " takes " + blow) + left;
            }

            var destroyed = ev as UnitDestroyed;
            if (destroyed != null && destroyed.OnBoard)
            {
                // a card that died face-down keeps its secret: it never had a name to print
                if (destroyed.Kind == UnitKind.Charge)
                    return "An unfinished face-down card is destroyed";
                if (destroyed.Kind == UnitKind.Trap) return "A set trap is destroyed";
                return NameOf(destroyed.UnitId)
                     + (destroyed.Kind == UnitKind.Building ? " is razed" : " falls");
            }

            var declared = ev as AttackDeclared;
            if (declared != null) return "⚔ " + NameOf(declared.AttackerId) + " declares an attack";

            var withdrawn = ev as AttackWithdrawn;
            if (withdrawn != null)
                return (withdrawn.Attacker == Seat.Local ? "You call off " : "They call off ")
                     + (withdrawn.DeclarationCount == 1 ? "the attack"
                                                        : "the attack — " + withdrawn.DeclarationCount
                                                          + " stand down");

            var blocks = ev as BlockersAssigned;
            if (blocks != null)
                return blocks.BlockerIds.Length == 0
                    ? "The attack is let through"
                    : blocks.BlockerIds.Length + " blocker(s) interpose";

            var wall = ev as WallStruck;
            if (wall != null)
                return (wall.Defender == Seat.Local ? "Your" : "The enemy") + " wall is stormed for ⚔" +
                       Stat.Show(wall.Amount) + " — ♥" + Stat.Show(wall.LifeRemaining) + " remains";

            var bounced = ev as UnitBounced;
            if (bounced != null)
                return (bounced.Cause == BounceCause.Undertow ? "Undertow! " : "") +
                       "A creature is hurled back to " +
                       (bounced.ToHand == Seat.Local ? "your" : "their") + " hand";

            var sprung = ev as TrapSprung;
            if (sprung != null) return sprung.Card.Value + " springs!";

            var token = ev as TokenSpawned;
            if (token != null)
                return (token.Owner == Seat.Local ? "You conjure " : "They conjure ") + token.Name +
                       " (" + Stat.Line(token.Attack, token.Hp) + ")";

            var hatched = ev as CreatureHatched;
            if (hatched != null)
                return "It hatches! " + hatched.NewName + " " + Stat.Atk(hatched.Attack) +
                       "/" + Stat.Hp(hatched.Hp);

            var grew = ev as ChrysalisGrew;
            if (grew != null) return "A cocoon swells (" + grew.Count + "/" + grew.HatchAt + ")";

            var cast = ev as SpellResolved;
            if (cast != null)
                return (cast.Caster == Seat.Local ? "You cast " : "They cast ") + cast.Card.Value;

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

            // off the board already - the events being described are what took it off
            string remembered;
            return _names.TryGetValue(unitId, out remembered) ? remembered : "A unit";
        }

        /// <summary>" — ♥120 left", or nothing at all for something that did not survive the blow
        /// being described. The state has already moved, so this is the hp AFTER the damage.</summary>
        string Remaining(int unitId)
        {
            CellRef at;
            bool onBoard;
            var o = Engine.State.FindById(unitId, out at, out onBoard);
            if (o == null || !onBoard) return "";

            var c = o as CreatureUnit;
            if (c != null) return c.Hp > 0 ? " — ♥" + Stat.Show(c.Hp) + " left" : "";
            var b = o as StructureUnit;
            if (b != null) return b.Hp > 0 ? " — ♥" + Stat.Show(b.Hp) + " left" : "";
            return "";
        }

        // ---- worker pawns -------------------------------------------------------------------

        /// <summary>
        /// THE NUMBER, not the pills.
        ///
        /// Each row's workforce used to be a file of little capsules standing off the side of the
        /// board - one per body in the pool, five abreast, growing outward. They were pills: you
        /// could see there were some and you could not see how many without counting, and the one
        /// thing the figure has to say is a SIGNED number, because a row in deficit is the whole
        /// upkeep mechanic and a row of capsules cannot show a minus.
        ///
        /// The row it belongs to. MatchHud projects this and hangs the number off the row's own
        /// end on screen, which is where the capsules used to stand.
        /// </summary>
        public RowKey WorkerRow(Side side, WorkerZone zone)
        {
            return Rules.Board.RowFor(side, (SlotName)zone);
        }

        /// <summary>The signed workforce of one row: structures minus upkeep, plus the homeland's
        /// own hands in the back row. Negative IS the shortfall - that is what upkeep settles.</summary>
        public int WorkerFigure(Side side, WorkerZone zone)
        {
            return WorkerMath.RowWorkers(Engine.State, side, zone, Catalog);
        }
    }
}
