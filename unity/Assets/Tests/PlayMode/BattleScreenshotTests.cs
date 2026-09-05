using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View;
using SpawnRowDuel.View.Cards;
using SpawnRowDuel.View.Shell;
using SpawnRowDuel.View.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SpawnRowDuel.PlayTests
{
    /// <summary>
    /// Screenshots of the REAL battle screen, taken in play mode from batchmode.
    ///
    /// A presentation milestone has no assertable oracle - "does this look right" is a question
    /// only an eye answers - so the thing to optimise is how fast an eye can get an answer. The
    /// alternative loop is build WebGL (6 min), stage, commit, push, wait for Pages, open a
    /// browser. This is one Unity run, and it captures the COMPOSITED frame: 3D board, standees,
    /// the UI Toolkit hand and the IMGUI HUD, exactly as a player sees them.
    ///
    /// Not part of the normal gate: it needs a graphics device and play mode. Run it with
    /// tools/screenshot.sh.
    /// </summary>
    public class BattleScreenshotTests
    {
        static string OutDir
        {
            get
            {
                var env = System.Environment.GetEnvironmentVariable("SRD_SHOT_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/Probe"));
            }
        }

        [UnityTest]
        public IEnumerator CaptureOpeningBoard()
        {
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(match, "the Battle scene has no MatchController");

            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(6);

            yield return Shoot("battle-open.png");
        }

        /// <summary>
        /// The same opening board seen from the GUEST'S seat.
        ///
        /// In a duel the guest's engine has them as Side.Foe - it must, because both engines have
        /// to be bit-identical and NewMatch draws You's deck before Foe's off one shared RNG
        /// stream. So the guest's screen is the one thing in multiplayer that no protocol test can
        /// check: their own rows have to be at the bottom, their own wall along their edge, their
        /// own hand face up, their ground warm and the opponent's cold.
        ///
        /// Compare this against battle-open.png - it should read as the same board from the other
        /// chair, not as a board with the colours swapped.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureGuestSeat()
        {
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(match);

            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(6);

            // Take the far seat the way AdoptNetMatch does, without needing a second machine.
            Seat.Take(Side.Foe);
            match.Board.ApplySeat();
            yield return Frames(30);            // the camera eases round; wait for it to arrive

            var input = Object.FindFirstObjectByType<BoardInput>();
            for (int i = 0; i < 240 && input != null && input.Cam != null
                 && Mathf.Abs(Mathf.DeltaAngle(input.Cam.transform.eulerAngles.y, 180f)) > 0.5f; i++)
                yield return null;

            yield return Shoot("battle-guest-seat.png");

            Seat.Take(Side.You);                // never leave a seat behind for the next test
            match.Board.ApplySeat();
            yield return Frames(2);
        }

        /// <summary>
        /// The guest's seat with a BOARD ON IT, which is the only version of that shot that can
        /// fail.
        ///
        /// battle-guest-seat.png is turn one: it proves the camera turned round, the rows are
        /// tinted for the right player and the hand is the guest's own - and it cannot prove
        /// anything about what STANDS on the board, because nothing does yet. Every figure is
        /// planted a fixed depth toward the camera of its tile, and "toward the camera" is the one
        /// direction that is not the same for the two seats: written as a bare world -Z it stands
        /// the guest's whole army at the FAR edge of every tile, leaning each billboard off the
        /// back of its own card and onto the row behind. From the guest's chair that reads as the
        /// units and buildings floating high above the cards they belong to - reported from a real
        /// game, invisible in solo, and invisible in the turn-one shot too.
        ///
        /// So: the same mid-game board as battle-mid.png, from the other chair. Every figure
        /// should stand at the NEAR edge of its own card, exactly as it does there.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureGuestSeatMidGame()
        {
            yield return PlayToMidGame();

            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(match);

            Seat.Take(Side.Foe);
            match.Board.ApplySeat();
            yield return Frames(30);

            var input = Object.FindFirstObjectByType<BoardInput>();
            for (int i = 0; i < 240 && input != null && input.Cam != null
                 && Mathf.Abs(Mathf.DeltaAngle(input.Cam.transform.eulerAngles.y, 180f)) > 0.5f; i++)
                yield return null;

            yield return Shoot("battle-guest-mid.png");

            Seat.Take(Side.You);
            match.Board.ApplySeat();
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator CaptureMidGameBoard()
        {
            yield return PlayToMidGame();

            // Pick a card, so the shot shows the two states the hand actually has: resting cards
            // peeking, the picked one risen, and the big inspect card beside it.
            var hud = Object.FindFirstObjectByType<MatchHud>();
            if (hud != null) hud.SelectHand(0);

            yield return Frames(6);
            yield return Shoot("battle-mid.png");
        }

        /// <summary>
        /// The same board from the top-down angle, where the cards lying on the tiles are the only
        /// thing left: the figures fade out with the swing, because an upright cut-out seen from
        /// above projects off its own tile and onto the row behind it.
        ///
        /// Nothing is forced here - StandeeLayer is left alone on purpose, so the shot is evidence
        /// of the top-down rule rather than a staged picture of it. It doubles as the plate layer's
        /// mirror: "is the card the right way up, the right size, cropped to the right part of its
        /// art" is a question the tilted shot cannot answer, because the standees stand on them.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTopDownPlates()
        {
            yield return PlayToMidGame();

            var input = Object.FindFirstObjectByType<BoardInput>();
            Assert.IsNotNull(input, "the Battle scene has no BoardInput");
            input.Tilted = false;

            // Wait for the SWING, not for a frame count. Batchmode runs uncapped, so a frame is
            // worth a fraction of the deltaTime it is worth in a player - 90 of them moved the
            // ease about a fifth of the way and the first version of this shot was a tilted board
            // wearing a top-down label.
            for (int i = 0; i < 2000 && input.TiltBlend > 0.005f; i++) yield return null;
            Assert.Less(input.TiltBlend, 0.01f, "the camera never reached the top-down angle");

            yield return Shoot("battle-plates.png");
        }

        /// <summary>
        /// A card SET face-down, with mana poured into it.
        ///
        /// The state that used to float "SET 1" over its tile - the card's own number, printed on
        /// the board, with the ◆ dropped by IMGUI's font. The sleeve carries it now, so this shot
        /// is the only witness that the badge lands on the card, reads at board size, and counts
        /// past one digit.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureSetCard()
        {
            yield return PlayToMidGame();

            var match = Object.FindFirstObjectByType<MatchController>();
            var engine = match.Engine;
            var s = engine.State;
            s.P(Side.You).Mana += 20;

            // A CREATURE face-down, not a trap: only a charge banks mana, and the badge being
            // tested is the bank. At mid-game hand[0] can easily be a spell and the near rows can
            // easily be full, so try every card against every cell.
            CellRef set = default(CellRef);
            bool placed = false;
            for (int h = 0; h < s.P(Side.You).Hand.Count && !placed; h++)
                for (int i = 0; i < Board.Cells && !placed; i++)
                {
                    var cell = CellRef.FromIndex(i);
                    if (!engine.Apply(new PlayCardCommand(Side.You, h, Rules.PlayMode.Set, cell)).Applied)
                        continue;
                    set = cell;
                    placed = true;
                }
            Assert.IsTrue(placed, "no legal cell to set a creature on");

            var charge = s.At(set);
            Assert.IsInstanceOf<ChargeUnit>(charge, "a set creature is a charge");
            for (int i = 0; i < 11; i++)
                Assert.IsTrue(engine.Apply(new PourIntoChargeCommand(Side.You, set, charge.Id, 1)).Applied,
                    "pouring into your own charge on your own action phase");

            // ... and a TRAP beside it, which is the other half of the picture: a trap consumed
            // its ◆1 rather than banking it, so what it shows is that ◆1. A face-down reading ◆1
            // is either a trap or a creature nobody has funded, and a face-down reading ◆12 is a
            // threat or a bluff - which is the whole reason the number is on the card.
            for (int h = 0; h < s.P(Side.You).Hand.Count; h++)
            {
                SpellCard sp;
                if (!engine.Catalog.TrySpell(s.P(Side.You).Hand[h].Id, out sp) || !sp.IsTrap) continue;

                bool trapped = false;
                for (int i = 0; i < Board.Cells && !trapped; i++)
                {
                    var cell = CellRef.FromIndex(i);
                    trapped = engine.Apply(
                        new PlayCardCommand(Side.You, h, Rules.PlayMode.SetTrap, cell)).Applied;
                }
                if (trapped) break;
            }

            yield return Frames(6);
            yield return Shoot("set-card.png");
        }

        /// <summary>
        /// The battle cut-in, mid-clash.
        ///
        /// The one surface with no other witness at all: a whole combat resolves inside one
        /// Apply, so the cards it draws no longer exist on the board by the time anything could
        /// look for them - the cut-in is drawn from CombatTheatre's one-frame-old snapshot, and a
        /// snapshot that has gone stale looks exactly like a snapshot that is working until you
        /// see the picture.
        ///
        /// The duel is STAGED, and has to be: a mid-game board is all structures, because the
        /// scripted AI builds its economy before it fights. The attack itself is still the
        /// engine's - CanApply is asserted before Apply - so the picture is of a legal fight.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureBattleCutIn()
        {
            yield return PlayToMidGame();

            var match = Object.FindFirstObjectByType<MatchController>();
            var engine = match.Engine;
            var s = engine.State;
            Side me = s.Turn;

            Assert.AreEqual(TurnPhase.Action, s.Phase, "the mid-game board should stop on an action phase");

            // A mid-game board is all STRUCTURES - the scripted AI builds before it fights - so
            // the fight is set up rather than found. Both duellists stand in the SAME row on
            // purpose: a same-row attack is an uninterposable duel (combat v3), so it resolves
            // inside the one Apply instead of parking on a blocker choice, and the shot catches
            // the whole trade rather than the declaration.
            var row = Board.RowFor(me, SlotName.Front);
            CellRef mineAt = default(CellRef), theirsAt = default(CellRef);
            int found = 0;
            for (int c = 0; c < Board.Columns && found < 2; c++)
            {
                var cell = new CellRef(row, c);
                if (!Board.IsRealSlot(row, c) || s.At(cell) != null) continue;
                if (found == 0) mineAt = cell; else theirsAt = cell;
                found++;
            }
            Assert.AreEqual(2, found, "no two free cells in the attacker's own row");

            var cat = engine.Catalog;
            var mineCard = cat.PoolOf(s.P(me).PrimaryColor)[7];            // the pool's heaviest
            var theirsCard = cat.PoolOf(s.P(TurnMachine.Other(me)).PrimaryColor)[5];

            var attacker = UnitFactory.MakeCreature(s, me, mineCard, mineCard.Element);
            attacker.Sick = false;
            s.Put(mineAt, attacker);

            var defender = UnitFactory.MakeCreature(s, TurnMachine.Other(me), theirsCard,
                                                    theirsCard.Element);
            defender.Sick = false;
            s.Put(theirsAt, defender);

            // Let them BE on the board for a frame. The cut-in draws from CombatTheatre's
            // one-frame-old snapshot - which is the only way to draw a card that the blow has
            // already removed - and a defender conjured and killed inside a single frame was
            // never snapshotted at all. A real one has stood there since it was summoned.
            yield return Frames(2);

            var cmd = new DeclareAttackCommand(me, mineAt, attacker.Id,
                new UnitTarget(theirsAt, defender.Id));
            Assert.AreEqual(Rejection.None, engine.CanApply(cmd), "the staged duel is not legal");

            // through the CONTROLLER, not the engine: it drains the events as each command lands,
            // which is what lets an animating listener see the board as it was before the blow.
            // Applying straight to the engine leaves the events for the next frame - by which
            // time the theatre's snapshot has been refreshed past the fight it is describing.
            Assert.AreEqual(Rejection.None, match.TryHuman(cmd));

            // DECLARING is not fighting: combat v3 resolves on its own command, and the cut-in
            // waits for the damage rather than the declaration. Any choice the resolution parks
            // is answered by the same scripted policy the opponent defends with.
            Assert.AreEqual(Rejection.None, match.TryHuman(new ResolveCombatCommand(me)));

            var defence = new Ai.ScriptedAiPolicy(TurnMachine.Other(me));
            for (int i = 0; i < 16 && engine.State.Pending != null; i++)
            {
                var answer = defence.Next(engine);
                if (answer == null || match.TryHuman(answer) != Rejection.None) break;
            }
            Assert.IsNull(engine.State.Pending, "the staged duel is still waiting on a choice");

            // past the fly-in and the clash, inside the hold - where the numbers are showing.
            // On the CLOCK, not on frames: the cut-in's beats are seconds and a batchmode frame
            // is worth a fraction of one.
            yield return Frames(2);
            yield return GameSeconds(0.5f);
            yield return Shoot("battle-cutin.png");
        }

        /// <summary>
        /// A JOINT attack: one target, three attackers, declared the way a player declares one -
        /// aim once, then tap the others.
        ///
        /// Two things have no other witness. On the board, the attack GROUP: after the first
        /// declaration the assault stays live, every creature that may still join is lit, and a tap
        /// on one joins instead of selecting it. In the theatre, the STACK: three declarations
        /// against one defender are told as one cut-in with the attackers fanned, rather than as
        /// three cut-ins of the same fight.
        ///
        /// The duel is STAGED - a mid-game board is all structures, because the scripted AI builds
        /// its economy before it fights - but every declaration goes through the controller's own
        /// funnel, so what the shot shows is the real flow and not a hand-built picture of it.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureAttackGroup()
        {
            yield return PlayToMidGame();

            var match = Object.FindFirstObjectByType<MatchController>();
            var engine = match.Engine;
            var s = engine.State;

            Assert.AreEqual(Side.You, s.Turn, "the group flow is the PLAYER's");
            Assert.AreEqual(TurnPhase.Action, s.Phase);

            var cat = engine.Catalog;
            var mine = new List<CellRef>();
            var row = Board.RowFor(Side.You, SlotName.Front);
            var pool = cat.PoolOf(s.P(Side.You).PrimaryColor);

            for (int c = 0; c < Board.Columns && mine.Count < 3; c++)
            {
                var cell = new CellRef(row, c);
                if (!Board.IsRealSlot(row, c) || s.At(cell) != null) continue;
                var u = UnitFactory.MakeCreature(s, Side.You, pool[mine.Count + 2],
                                                 pool[mine.Count + 2].Element);
                u.Sick = false;
                s.Put(cell, u);
                mine.Add(cell);
            }
            Assert.AreEqual(3, mine.Count, "no three free cells in your front row");

            // something of theirs worth ganging up on, in their own front row
            var foeRow = Board.RowFor(Side.Foe, SlotName.Front);
            CellRef target = default(CellRef);
            bool staged = false;
            for (int c = 0; c < Board.Columns && !staged; c++)
            {
                var cell = new CellRef(foeRow, c);
                if (!Board.IsRealSlot(foeRow, c) || s.At(cell) != null) continue;
                var theirs = cat.PoolOf(s.P(Side.Foe).PrimaryColor)[6];
                var u = UnitFactory.MakeCreature(s, Side.Foe, theirs, theirs.Element);
                u.Sick = false;
                s.Put(cell, u);
                target = cell;
                staged = true;
            }
            Assert.IsTrue(staged, "no free cell in the foe's front row");

            yield return Frames(2);                 // let the theatre snapshot the board as it is

            // aim: the first declaration opens the assault
            Assert.AreEqual(Rejection.None, match.TryAttack(mine[0], target));
            Assert.IsNotNull(match.Assault, "declaring did not open an attack group");
            Assert.AreEqual(target, match.AssaultCell);

            // and the others JOIN - the thing the board could not do before
            Assert.IsTrue(match.CanJoinAssault(mine[1]),
                "a ready creature could not join the attack it is standing next to");
            Assert.AreEqual(Rejection.None, match.JoinAssault(mine[1]));

            // Shot with the THIRD still out: what it has to show is the live group - the target
            // ringed, the creature that may still pile in lit, and the mode row saying so. The
            // highlights repaint off the controller's version bump, so nothing is cleared here:
            // clearing the selection would wipe them and nothing would ask for them back.
            yield return Frames(6);
            yield return Shoot("attack-group.png");

            Assert.IsTrue(match.CanJoinAssault(mine[2]));
            Assert.AreEqual(Rejection.None, match.JoinAssault(mine[2]));
            Assert.AreEqual(3, engine.State.Combat.Declarations.Count,
                "three taps did not make three declarations");

            // ... and the whole assault is told as ONE cut-in
            Assert.AreEqual(Rejection.None, match.TryHuman(new ResolveCombatCommand(Side.You)));

            var defence = new Ai.ScriptedAiPolicy(Side.Foe);
            for (int i = 0; i < 16 && engine.State.Pending != null; i++)
            {
                var answer = defence.Next(engine);
                if (answer == null || match.TryHuman(answer) != Rejection.None) break;
            }

            yield return Frames(2);
            yield return GameSeconds(0.5f);
            yield return Shoot("battle-stack.png");
        }

        /// <summary>
        /// Both walls RAISED - the state a still cannot catch on its own, because a wall only
        /// rises while it is being looked at and a batchmode camera never looks at anything.
        ///
        /// This is the half of the surface that has no other witness: the tower windows, what the
        /// vitals look like once they are all on screen, and how much board a wall borrows while
        /// it is up.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureWallsOpen()
        {
            yield return PlayToMidGame();

            WallBands.ForceOpen = true;
            yield return Frames(40);              // let the slide finish
            yield return Shoot("walls-open.png");
            WallBands.ForceOpen = false;          // static: do not leak it into the next shot
            yield return Frames(4);
        }

        /// <summary>
        /// One shot per biome, from the same board. Terrain is the one part of the view with no
        /// test that can fail: waves, ripples and embers are shader terms, and "does scorched
        /// ground look scorched" is not a thing an assertion knows. Four pictures is the gate.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureBiomes()
        {
            yield return PlayToMidGame();

            var all = Biomes.All;
            for (int i = 0; i < all.Length; i++)
            {
                TerrainField.Requested = all[i];
                yield return Frames(4);                  // the field rebuilds in LateUpdate
                yield return Shoot("biome-" + Biomes.NameOf(all[i]) + ".png");
            }

            TerrainField.Requested = BiomeId.Grass;   // static: do not leak a biome
        }

        /// <summary>
        /// A board that weather has been landing on for a while.
        ///
        /// The falling half of the weather photographs itself; the SETTLED half does not, because
        /// it takes twenty-five seconds of match to build and a probe frame is taken in one. So
        /// the field is filled directly and the shot answers the only question that matters:
        /// does a covered board still read as a board.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureSettled()
        {
            yield return PlayToMidGame();

            var terrain = Object.FindFirstObjectByType<TerrainField>();
            Assert.IsNotNull(terrain, "the Battle scene has no TerrainField");

            foreach (var id in new[] { BiomeId.Ash, BiomeId.Snow })
            {
                TerrainField.Requested = id;
                yield return Frames(6);
                terrain.PrimeSettle(0.95f);
                yield return Frames(3);
                yield return Shoot("settled-" + Biomes.NameOf(id) + ".png");
            }

            TerrainField.Requested = BiomeId.Grass;
        }

        /// <summary>
        /// The shore at both ends of its breath.
        ///
        /// The tide is a twenty-second cycle and a screenshot is one instant of it, so the first
        /// shot of this beach was taken at low water with the sea off the top of the frame - a
        /// picture of an empty beach that looked exactly like a bug. The pair is the test: water
        /// well up the sand in one, drawn back with a wet band behind it in the other.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTide()
        {
            yield return PlayToMidGame();

            TerrainField.Requested = BiomeId.Shore;
            yield return Frames(6);

            TerrainField.TideFreeze = 0.95f;      // in
            yield return Frames(3);
            yield return Shoot("tide-in.png");

            TerrainField.TideFreeze = 0.05f;      // out
            yield return Frames(3);
            yield return Shoot("tide-out.png");

            TerrainField.TideFreeze = -1f;        // static: never leave the sea frozen
            TerrainField.Requested = BiomeId.Grass;
            yield return Frames(2);
        }

        /// <summary>
        /// The same frame twice, a fixed slice of GAME TIME apart, plus a gust in between.
        ///
        /// This exists because a still cannot fail the only question the terrain has: does it
        /// move. The first wind field shipped looking painted on - its sway term averaged 0.053
        /// out of a possible ±1 - and four screenshots could not tell me, because each one was
        /// individually fine. Diff the pair and the answer is a number.
        ///
        /// Wait on Time.time, not on a frame count: batchmode runs uncapped and a frame here is
        /// worth a fraction of the deltaTime it is worth in a player.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureMotionPair()
        {
            yield return PlayToMidGame();
            yield return Frames(4);

            var terrain = Object.FindFirstObjectByType<TerrainField>();
            Assert.IsNotNull(terrain, "the Battle scene has no TerrainField");

            // a → b is WIND AND CLOUD ONLY, which is the pair that answers the complaint. Firing
            // the gust into this half would hide a dead wind field behind a working gust.
            yield return Shoot("motion-a.png");
            yield return GameSeconds(0.45f);
            yield return Shoot("motion-b.png");

            // b → c adds the gust, so the ring can be told apart from the weather
            TerrainField.Gust(Vector3.zero, 1f);
            yield return GameSeconds(0.78f);   // long enough for the ring to be IN the grass
            yield return Shoot("motion-c.png");
        }

        /// <summary>
        /// Every card in hand picked in turn, one shot each.
        ///
        /// A card face reported as garbled on the phone rendered correctly in every probe shot -
        /// because the probe only ever picked hand index 0. If the fault belongs to one card, or
        /// to the act of switching between them, it lives in the shots this test takes and nowhere
        /// in the ones that existed.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureEveryHandCard()
        {
            yield return PlayToMidGame();

            var hud = Object.FindFirstObjectByType<MatchHud>();
            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(hud);

            int hand = match.Engine.State.P(Side.You).Hand.Count;
            for (int i = 0; i < hand && i < 8; i++)
            {
                hud.SelectHand(i);
                yield return Frames(6);
                yield return Shoot("hand-" + i + ".png");
                hud.SelectHand(i);                       // toggles it back off
                yield return Frames(2);
            }
        }

        /// <summary>
        /// The front of the game: menu, banner select, the world map with its globe, the
        /// pre-battle challenge, and the deck builder.
        ///
        /// These are the screens with no other witness. The duel has an engine and 260 tests
        /// behind it; a menu has a screenshot or it has nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureShellScreens()
        {
            Reshape();
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(4);

            var shell = Object.FindFirstObjectByType<GameShell>();
            Assert.IsNotNull(shell, "the Battle scene has no GameShell - rerun SceneBootstrap");

            yield return Frames(3);
            yield return Shoot("shell-menu.png");

            shell.Show(ShellScreen.FactionSelect);
            yield return Frames(3);
            yield return Shoot("shell-faction.png");

            shell.Campaign.Begin(SpawnRowDuel.Rules.Element.Fire);
            shell.Show(ShellScreen.WorldMap);
            yield return Frames(6);
            yield return Shoot("shell-worldmap.png");

            // the first attackable territory, under the solo banner
            var s = shell.Campaign.State;
            int target = -1;
            foreach (var t in s.Map.Territories)
                if (shell.Campaign.IsAttackable(t.Id)) { target = t.Id; break; }
            Assert.GreaterOrEqual(target, 0, "a fresh map always has a front line");

            shell.AttackTerritory(target, SpawnRowDuel.Campaign.CampaignRules.Solo(s.Faction));
            yield return Frames(8);
            yield return Shoot("shell-challenge.png");

            shell.Campaign.Resolve(SpawnRowDuel.Campaign.BattleOutcome.Abandoned);
            shell.Show(ShellScreen.DeckBuilder);
            yield return Frames(6);
            yield return Shoot("shell-deckbuilder.png");

            // The multiplayer lobby, idle - no session is started, so nothing here touches a
            // network. It is the one screen a player meets before any of the netcode runs, so
            // it is worth being able to look at without two machines.
            shell.Show(ShellScreen.Multiplayer);
            yield return Frames(6);
            yield return Shoot("shell-multiplayer.png");

            shell.Show(ShellScreen.MainMenu);
            yield return Frames(2);
        }

        /// <summary>Wait on the CLOCK. Batchmode frames are worth a fraction of a player's.</summary>
        static IEnumerator GameSeconds(float seconds)
        {
            float until = Time.time + seconds;
            for (int i = 0; i < 40000 && Time.time < until; i++) yield return null;
        }

        static IEnumerator PlayToMidGame()
        {
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(match);
            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(2);

            // Play the opening out with the scripted AI on BOTH sides, so the shot has creatures,
            // structures and a real hand in it rather than an empty board nobody can judge.
            var you = new Ai.ScriptedAiPolicy(Side.You);
            var foe = new Ai.ScriptedAiPolicy(Side.Foe);
            var engine = match.Engine;

            for (int i = 0; i < 400 && !engine.State.IsOver; i++)
            {
                var cmd = you.Next(engine);
                if (cmd == null) cmd = foe.Next(engine);
                if (cmd == null)
                {
                    if (engine.State.Pending == null && engine.State.Phase == TurnPhase.End)
                        cmd = new BeginTurnCommand(TurnMachine.Other(engine.State.Turn));
                    else break;
                }
                if (!engine.Apply(cmd).Applied) break;
                if (engine.State.TurnNumber >= 6 && engine.State.Turn == Side.You
                    && engine.State.Phase == TurnPhase.Action) break;
            }
        }

        /// <summary>
        /// The title screen at a LANDSCAPE PHONE size - the shape the build is actually played
        /// at, and the one the menu was wrong for.
        ///
        /// It was laid out against HudLayout.Scale's default of 1, because Start() shows the menu
        /// before the first Update ever recomputes it, and nothing rebuilt it afterwards. On any
        /// screen bigger than 480 short-edge that is a postage stamp in a black field. This shot
        /// is the one that can fail if it comes back.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureMenuOnAPhone()
        {
#if UNITY_EDITOR
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(880, 400, "SRD phone");
#endif
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(6);

            var shell = Object.FindFirstObjectByType<GameShell>();
            Assert.IsNotNull(shell);
            shell.Show(ShellScreen.MainMenu);
            yield return Frames(6);

            yield return Shoot("shell-menu-phone.png");
            Reshape();                      // never leave the probe in a phone shape
            yield return Frames(2);
        }

        static IEnumerator LoadBattle()
        {
            Reshape();
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(3);

            // The shell boots to the main menu and switches the battle world OFF. Every duel shot
            // has to walk in through the same door a player does.
            var shell = Object.FindFirstObjectByType<GameShell>();
            if (shell != null) shell.Show(ShellScreen.Skirmish);
            yield return Frames(2);
        }

        /// <summary>
        /// Force the game view to a LANDSCAPE PHONE shape.
        ///
        /// Batchmode's game view is 640x480, and the aspect is not a detail here: the two wall
        /// bands take a fixed share of the height whatever the screen is, so a 4:3 probe judges
        /// the board's framing against a gap far narrower than the deployed build ever has. The
        /// first shots of the filled framing looked half-empty for that reason alone.
        /// </summary>
        static void Reshape()
        {
#if UNITY_EDITOR
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1600, 900, "SRD probe");
#endif
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // The UI panel lays out against Screen, which in batchmode is the game view size, so the
        // capture matches it - a 1280 target with a 640 panel put the hand in a corner at half
        // scale and made the shot lie about the layout.
        static int ShotW { get { return Mathf.Max(640, Screen.width); } }
        static int ShotH { get { return Mathf.Max(480, Screen.height); } }

        /// <summary>
        /// Composite the board and the UI into one image, by hand.
        ///
        /// Neither of the obvious routes works headlessly: `WaitForEndOfFrame` never resumes in
        /// batchmode (the run hangs until something kills it), and `ScreenCapture.CaptureScreenshot`
        /// writes nothing because there is no swap chain to capture. So the camera renders into a
        /// texture on demand, the UI panel renders into a second one, and they are blended here.
        ///
        /// The IMGUI HUD is absent from these shots by consequence - OnGUI has no target texture -
        /// which is tolerable while IMGUI is the layer being replaced.
        /// </summary>
        static IEnumerator Shoot(string name)
        {
            // A menu screen has NO live camera at all - the shell switches both of them off,
            // because there is nothing behind a menu worth the frame. The shot is then the UI
            // panel over a flat ground rather than a failed assertion.
            var cam = LiveCamera();

            var board = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            var ui = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            board.Create();
            ui.Create();

            var panel = Resources.Load<UnityEngine.UIElements.PanelSettings>("HudPanelSettings");
            Assert.IsNotNull(panel, "HudPanelSettings is missing - run tools/regen-fonts.sh");

            var prevCamTarget = cam != null ? cam.targetTexture : null;
            var prevPanelTarget = panel.targetTexture;
            var prevClear = panel.clearColor;
            var prevClearValue = panel.colorClearValue;

            // The camera's own VIEWPORT is kept. It used to be forced to the whole screen here,
            // on the reasoning that a shot should show everything - but the viewport is exactly
            // what the wall bands take away, so the shot was of a layout the game never renders:
            // the board ran under the HUD instead of between the two walls. Clearing the target
            // first is what the discarded pixels need, since nothing draws there any more.
            ClearTo(board, cam != null ? cam.backgroundColor : new Color(0.035f, 0.04f, 0.06f, 1f));
            if (cam != null) cam.targetTexture = board;
            panel.targetTexture = ui;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0f, 0f, 0f, 0f);

            yield return Frames(3);                     // let the panel repaint into its target
            if (cam != null) cam.Render();

            var shot = Blend(board, ui);

            if (cam != null) cam.targetTexture = prevCamTarget;
            panel.targetTexture = prevPanelTarget;
            panel.clearColor = prevClear;
            panel.colorClearValue = prevClearValue;

            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, name);
            File.WriteAllBytes(path, shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            board.Release();
            ui.Release();

            Debug.Log("shot wrote " + path);
        }

        /// <summary>Whichever camera is currently switched on - the duel's, or the globe's.</summary>
        static Camera LiveCamera()
        {
            var all = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in all) if (c.enabled && c.gameObject.activeInHierarchy) return c;
            return Camera.main;
        }

        static void ClearTo(RenderTexture rt, Color c)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, c);
            RenderTexture.active = prev;
        }

        static Texture2D Blend(RenderTexture under, RenderTexture over)
        {
            var a = Read(under);
            var b = Read(over);
            var pa = a.GetPixels();
            var pb = b.GetPixels();
            for (int i = 0; i < pa.Length; i++)
            {
                float alpha = pb[i].a;
                if (alpha <= 0f) continue;
                pa[i] = Color.Lerp(pa[i], pb[i], alpha);
            }
            a.SetPixels(pa);
            a.Apply();
            Object.DestroyImmediate(b);
            return a;
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
    }
}
