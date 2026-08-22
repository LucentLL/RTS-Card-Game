using System.Collections;
using System.IO;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View;
using SpawnRowDuel.View.Cards;
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

        static IEnumerator LoadBattle()
        {
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(3);
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
            var cam = Camera.main;
            Assert.IsNotNull(cam, "no camera in the battle scene");

            var board = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            var ui = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            board.Create();
            ui.Create();

            var panel = Resources.Load<UnityEngine.UIElements.PanelSettings>("HudPanelSettings");
            Assert.IsNotNull(panel, "HudPanelSettings is missing - run tools/regen-fonts.sh");

            var prevCamTarget = cam.targetTexture;
            var prevRect = cam.rect;
            var prevPanelTarget = panel.targetTexture;
            var prevClear = panel.clearColor;
            var prevClearValue = panel.colorClearValue;

            cam.rect = new Rect(0f, 0f, 1f, 1f);        // the HUD shrinks the viewport; we want it all
            cam.targetTexture = board;
            panel.targetTexture = ui;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0f, 0f, 0f, 0f);

            yield return Frames(3);                     // let the panel repaint into its target
            cam.Render();

            var shot = Blend(board, ui);

            cam.targetTexture = prevCamTarget;
            cam.rect = prevRect;
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
