using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View;
using SpawnRowDuel.View.Shell;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SpawnRowDuel.PlayTests
{
    /// <summary>
    /// A WHOLE MATCH, RENDERED.
    ///
    /// The existing probe plays six turns by calling `engine.Apply` in a tight loop and then takes
    /// one picture, so the visual stack sees the board twice: empty, and finished. That is the
    /// right shape for judging a still, and it is the wrong shape for finding the fault a player
    /// hits on turn seven - a crash inside Unity's own sprite render-node preparation, which only
    /// happens after the board has been built up and torn down a few hundred times with the layers
    /// live.
    ///
    /// So this yields a FRAME between plies. Every summon, every fight, every death runs through
    /// CardPlateLayer, StandeeLayer, UnitVitals, CombatTheatre and TerrainField the way it does in
    /// a real game, and anything those layers leak or dangle accumulates the way it does there.
    ///
    /// Two things are asserted, and the second is the point:
    ///   * nothing logs an exception (LogAssert.NoUnexpectedReceived, via the default handler);
    ///   * the SPRITE COUNT comes back down. A layer that keeps its renderers after the unit is
    ///     gone grows the sprite scene without bound, and an unbounded sprite scene is exactly
    ///     what `PrepareSpriteRenderNodes` walks off the end of.
    ///
    /// Not in the default gate - it needs play mode and a graphics device. Run it with
    /// tools/screenshot.sh (same harness) or by filter.
    /// </summary>
    public class LongMatchRenderTests
    {
        [UnityTest]
        public IEnumerator AWholeMatch_RendersWithoutLeakingSprites()
        {
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            Assert.IsNotNull(match, "the Battle scene has no MatchController");

            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(3);

            var you = new Ai.ScriptedAiPolicy(Side.You);
            var foe = new Ai.ScriptedAiPolicy(Side.Foe);
            var engine = match.Engine;

            int peak = 0, plies = 0;
            var counts = new List<int>();

            for (int i = 0; i < 1200 && !engine.State.IsOver; i++)
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
                plies++;

                // A FRAME, not a tight loop: this is where the view actually runs.
                yield return null;

                int live = Object.FindObjectsByType<SpriteRenderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                if (live > peak) peak = live;
                if ((i & 31) == 0) counts.Add(live);
            }

            Assert.Greater(plies, 120, "the match ended too early to stress anything");

            // let the layers prune whatever the last command killed
            yield return Frames(4);
            int settled = Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Debug.Log("[stress] plies=" + plies + " turns=" + engine.State.TurnNumber
                      + " peakSprites=" + peak + " settled=" + settled
                      + " over=" + engine.State.IsOver
                      + " samples=" + string.Join(",", counts));

            // The board holds 35 cells; a plate is 8 renderers and a standee 2, so a FULL board is
            // about 350. Anything far past that is a layer keeping renderers it no longer draws.
            Assert.Less(peak, 700,
                "the sprite scene grew past what a full board can account for - peak " + peak);
            Assert.Less(settled, peak / 2 + 120,
                "sprites did not come back down after the board emptied - settled " + settled
                + " against a peak of " + peak);
        }

        static IEnumerator LoadBattle()
        {
#if UNITY_EDITOR
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1600, 900, "SRD stress");
#endif
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(3);

            var shell = Object.FindFirstObjectByType<GameShell>();
            if (shell != null) shell.Show(ShellScreen.Skirmish);
            yield return Frames(2);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }
    }
}
