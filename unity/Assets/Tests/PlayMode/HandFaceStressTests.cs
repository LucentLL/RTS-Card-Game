using System.Collections;
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
    /// A hunt, not a gate.
    ///
    /// The player's WebGL build dies with `RuntimeError: index out of bounds` - a wasm trap, so no
    /// managed catch survives it - and immediately before it the HAND CARDS render as white shards
    /// and black triangles while the board, terrain, standees and IMGUI rail are all fine. Corrupt
    /// texture CONTENT plus a later trap is the signature of an out-of-bounds write into whatever
    /// backs those faces.
    ///
    /// So drive the exact sequence the screenshots show, in the Editor, and see whether the Editor
    /// (which bounds-checks what wasm does not) says anything: fresh match, turn 1, own action
    /// phase, sand biome, a Foundry raised on the back row, a hand card picked, both wall bands
    /// forced open, the camera swung between tilt and top-down - and the panel actually REPAINTED
    /// each time, because a mesh that is never generated cannot corrupt anything.
    /// </summary>
    public class HandFaceStressTests
    {
        static string OutDir
        {
            get
            {
                var env = System.Environment.GetEnvironmentVariable("SRD_SHOT_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/Hunt"));
            }
        }

        [UnityTest]
        public IEnumerator Turn1_PickEveryCard_ArmEveryMode_WallsOpen_NothingLogged()
        {
            _viewW = 1600; _viewH = 900;
            yield return DriveTurn1("hunt");
        }

        /// <summary>
        /// The same sequence on the shape the game is actually PLAYED on. The deployed build lives
        /// on a landscape phone, and the hand's layout is width math: the middle span between the
        /// two towers, a step that collapses when the cards will not fit, an inspect card sized off
        /// whatever the board band has left. None of that is exercised by a 16:9 desktop still.
        /// </summary>
        [UnityTest]
        public IEnumerator Turn1_LandscapePhone_NothingLogged()
        {
            _viewW = 812; _viewH = 375;
            yield return DriveTurn1("phone");
        }

        /// <summary>And a deliberately CRAMPED one, where the middle span is nearly nothing.</summary>
        [UnityTest]
        public IEnumerator Turn1_TinyViewport_NothingLogged()
        {
            _viewW = 480; _viewH = 240;
            yield return DriveTurn1("tiny");
        }

        IEnumerator DriveTurn1(string prefix)
        {
            TerrainField.Requested = BiomeId.Sand;          // the screenshots are dunes
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            var hud = Object.FindFirstObjectByType<MatchHud>();
            var input = Object.FindFirstObjectByType<BoardInput>();
            Assert.IsNotNull(match, "the Battle scene has no MatchController");
            Assert.IsNotNull(hud, "the Battle scene has no MatchHud");
            Assert.IsNotNull(input, "the Battle scene has no BoardInput");

            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(20);

            var engine = match.Engine;
            var s = engine.State;
            TestContext.WriteLine("HUNT: turn=" + s.TurnNumber + " side=" + s.Turn + " phase=" + s.Phase
                      + " hand=" + s.P(Side.You).Hand.Count);

            // Walk the opening to the player's own ACTION phase - the match opens on YOUR upkeep,
            // so harvest and draw have to happen first. The scripted AI is borrowed only to get
            // through them; the loop stops the instant the action phase arrives, so turn 1 is
            // still untouched, which is what the screenshots show.
            var walker = new Ai.ScriptedAiPolicy(Side.You);
            for (int i = 0; i < 60 && !(s.Turn == Side.You && s.Phase == TurnPhase.Action); i++)
            {
                var cmd = walker.Next(engine);
                if (cmd == null) break;
                if (!engine.Apply(cmd).Applied) break;
                yield return null;
            }
            yield return Frames(10);
            TestContext.WriteLine("HUNT: at action? turn=" + s.TurnNumber + " side=" + s.Turn + " phase=" + s.Phase);

            s.P(Side.You).Mana += 50;                       // afford whatever the hand holds

            yield return Paint(prefix + "-00-open.png");

            // ── the Foundry, on the back row, centre column ────────────────────────────────
            var foundry = engine.Catalog.Structure(new StructId("foundry"), Element.None);
            Assert.IsNotNull(foundry, "no foundry in the catalog");
            match.BeginBuild(foundry);
            yield return Frames(4);
            TestContext.WriteLine("HUNT: foundry legal cells = " + match.LegalCells.Count);

            bool built = false;
            for (int c = 0; c < 7 && !built; c++)
            {
                int col = 3 + ((c % 2 == 0) ? c / 2 : -(c / 2 + 1));   // 3,2,4,1,5,0,6
                if (col < 0 || col > 6) continue;
                var cell = new CellRef(RowKey.YouBack, col);
                if (match.Probe(new BuildStructureCommand(Side.You, foundry.Bid, foundry.Element, cell))
                    != Rejection.None) continue;
                built = match.TryCellTap(cell);
            }
            TestContext.WriteLine("HUNT: foundry built = " + built);
            yield return Frames(20);
            yield return Paint(prefix + "-01-foundry.png");

            // ── both walls held open, which is the state the second screenshot is in ───────
            WallBands.ForceOpen = true;
            yield return Frames(20);
            yield return Paint(prefix + "-02-walls-open.png");

            // ── every hand card picked, every mode armed, walls toggling under it ──────────
            int hand = s.P(Side.You).Hand.Count;
            Assert.Greater(hand, 0, "turn 1 with an empty hand");

            for (int i = 0; i < hand; i++)
            {
                hud.SelectHand(i);                          // picks; a second call would unpick
                yield return Frames(6);

                match.BeginPlay(i, Rules.PlayMode.Summon);  // "SUMMON n"
                yield return Frames(6);
                match.BeginPlay(i, Rules.PlayMode.Set);     // "SET 1"
                yield return Frames(6);
                match.BeginPlay(i, Rules.PlayMode.SetTrap);
                yield return Frames(4);
                match.BeginPlay(i, Rules.PlayMode.Cast);
                yield return Frames(4);
                match.CancelPending();

                WallBands.ForceOpen = (i % 2 == 0);         // slide them, do not just hold them
                yield return Frames(8);

                yield return Paint(prefix + "-03-card" + i + ".png");

                hud.SelectHand(i);                          // unpick, so the next pick is a change
                yield return Frames(4);
            }

            LogAssert.NoUnexpectedReceived();
            TestContext.WriteLine("HUNT: hand sweep clean");

            // ── the camera swing, both ways, with a card held ──────────────────────────────
            WallBands.ForceOpen = true;
            hud.SelectHand(0);
            match.BeginPlay(0, Rules.PlayMode.Summon);
            yield return Frames(6);

            input.Tilted = false;
            for (int i = 0; i < 2000 && input.TiltBlend > 0.005f; i++) yield return null;
            yield return Frames(20);
            yield return Paint(prefix + "-04-topdown.png");

            input.Tilted = true;
            for (int i = 0; i < 2000 && input.TiltBlend < 0.995f; i++) yield return null;
            yield return Frames(20);
            yield return Paint(prefix + "-05-tilted.png");

            // ── and then just SIT there for a couple of hundred frames, repainting, because a
            //    per-frame throw that only fires on the twentieth repaint would hide in six ──
            for (int i = 0; i < 240; i++)
            {
                if (i % 40 == 0) WallBands.ForceOpen = !WallBands.ForceOpen;
                if (i % 60 == 0) hud.SelectHand(i / 60 % Mathf.Max(1, s.P(Side.You).Hand.Count));
                yield return null;
            }
            yield return Paint(prefix + "-06-settled.png");

            WallBands.ForceOpen = false;
            LogAssert.NoUnexpectedReceived();
            TestContext.WriteLine("HUNT: finished clean");
        }

        /// <summary>
        /// The same board, but with the hand REBUILT over and over: HandBar only rebuilds when its
        /// signature changes, so a leak or an overrun in CardFace construction needs the hand to
        /// churn before it shows. Every rebuild allocates a fresh CardFace per card and hands its
        /// text to the SDF atlas, which is the one shared, growable buffer on this path.
        /// </summary>
        [UnityTest]
        public IEnumerator HandRebuiltHundredsOfTimes_NothingLogged()
        {
            TerrainField.Requested = BiomeId.Sand;
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            var hud = Object.FindFirstObjectByType<MatchHud>();
            Assert.IsNotNull(match);
            Assert.IsNotNull(hud);

            match.StartMatch(new CommanderId("fire"), new CommanderId("water"), 909);
            yield return Frames(20);

            var s = match.Engine.State;
            s.P(Side.You).Mana += 200;
            WallBands.ForceOpen = true;

            int hand = Mathf.Max(1, s.P(Side.You).Hand.Count);
            for (int i = 0; i < 300; i++)
            {
                hud.SelectHand(i % hand);                   // flips the signature every iteration
                yield return null;
                if (i % 50 == 0) yield return Paint("hunt-churn-" + i + ".png");
            }

            yield return Paint("hunt-churn-end.png");
            WallBands.ForceOpen = false;
            LogAssert.NoUnexpectedReceived();
            TestContext.WriteLine("HUNT: churn finished clean");
        }

        /// <summary>
        /// Does a SESSION grow without bound?
        ///
        /// The Editor has gigabytes; a WebGL heap does not. If the view leaks textures, sprites,
        /// meshes or materials per unit / per plate / per match, then a long session eventually
        /// exhausts the wasm heap - and an allocator that cannot grow is exactly how you get
        /// garbage texture CONTENT first and a linear-memory trap second, with a completely clean
        /// Editor run in between. So play four whole matches out, walking back through the menu
        /// between them the way the shell does, and count what is alive.
        /// </summary>
        [UnityTest]
        public IEnumerator FourWholeMatches_ResourceCountsDoNotRunAway()
        {
            TerrainField.Requested = BiomeId.Sand;
            yield return LoadBattle();

            var match = Object.FindFirstObjectByType<MatchController>();
            var shell = Object.FindFirstObjectByType<GameShell>();
            Assert.IsNotNull(match);

            Sample("boot");
            int firstTex = CountOf<Texture2D>();
            int firstSpr = CountOf<Sprite>();
            int firstMesh = CountOf<Mesh>();
            int firstMat = CountOf<Material>();

            for (int round = 0; round < 4; round++)
            {
                match.StartMatch(new CommanderId("fire"), new CommanderId("water"),
                                 (ulong)(909 + round));
                yield return Frames(10);

                var engine = match.Engine;
                var you = new Ai.ScriptedAiPolicy(Side.You);
                var foe = new Ai.ScriptedAiPolicy(Side.Foe);

                int applied = 0;
                for (int i = 0; i < 4000 && !engine.State.IsOver; i++)
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
                    applied++;
                    if (applied % 8 == 0) yield return null;   // let the view churn on the state
                }

                yield return Frames(30);
                yield return Paint("hunt-match" + round + ".png");
                Sample("match" + round + " turns=" + engine.State.TurnNumber + " cmds=" + applied);

                if (shell != null)
                {
                    shell.Show(ShellScreen.MainMenu);
                    yield return Frames(20);
                    shell.Show(ShellScreen.Skirmish);
                    yield return Frames(20);
                }
                Sample("after menu round trip " + round);
            }

            System.GC.Collect();
            Resources.UnloadUnusedAssets();
            yield return Frames(10);
            Sample("after unload");

            int lastTex = CountOf<Texture2D>();
            int lastSpr = CountOf<Sprite>();
            int lastMesh = CountOf<Mesh>();
            int lastMat = CountOf<Material>();
            TestContext.WriteLine("HUNT: delta tex=" + (lastTex - firstTex)
                                  + " sprite=" + (lastSpr - firstSpr)
                                  + " mesh=" + (lastMesh - firstMesh)
                                  + " material=" + (lastMat - firstMat));

            LogAssert.NoUnexpectedReceived();
        }

        static int CountOf<T>() where T : Object
        {
            return Resources.FindObjectsOfTypeAll<T>().Length;
        }

        static void Sample(string tag)
        {
            TestContext.WriteLine("HUNT SAMPLE " + tag
                + " | tex=" + CountOf<Texture2D>()
                + " rt=" + CountOf<RenderTexture>()
                + " sprite=" + CountOf<Sprite>()
                + " mesh=" + CountOf<Mesh>()
                + " mat=" + CountOf<Material>()
                + " go=" + CountOf<GameObject>()
                + " | mono=" + (UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20) + "MB"
                + " total=" + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20) + "MB");
        }

        // ── harness, lifted from BattleScreenshotTests ────────────────────────────────────

        /// <summary>The game-view shape the next LoadBattle uses. The deployed build is played on a
        /// landscape phone, and layout math is where an extreme shape bites.</summary>
        static int _viewW = 1600, _viewH = 900;

        static IEnumerator LoadBattle()
        {
            Reshape(_viewW, _viewH);
            var op = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return Frames(3);

            var shell = Object.FindFirstObjectByType<GameShell>();
            if (shell != null) shell.Show(ShellScreen.Skirmish);
            yield return Frames(2);
        }

        static void Reshape(int w, int h)
        {
#if UNITY_EDITOR
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution((uint)w, (uint)h, "SRD hunt");
#endif
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static int ShotW { get { return Mathf.Max(640, Screen.width); } }
        static int ShotH { get { return Mathf.Max(480, Screen.height); } }

        /// <summary>
        /// Force a real REPAINT of both surfaces and read it back.
        ///
        /// The readback is the point as much as the file is: a UI Toolkit panel that never paints
        /// never builds a mesh, and a mesh that is never built cannot be the corrupt thing. This
        /// puts the camera and the panel through a full render every time it is called.
        /// </summary>
        static IEnumerator Paint(string name)
        {
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

            ClearTo(board, cam != null ? cam.backgroundColor : new Color(0.035f, 0.04f, 0.06f, 1f));
            if (cam != null) cam.targetTexture = board;
            panel.targetTexture = ui;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0f, 0f, 0f, 0f);

            yield return Frames(3);
            if (cam != null) cam.Render();

            var shot = Blend(board, ui);

            if (cam != null) cam.targetTexture = prevCamTarget;
            panel.targetTexture = prevPanelTarget;
            panel.clearColor = prevClear;
            panel.colorClearValue = prevClearValue;

            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, name), shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            board.Release();
            ui.Release();
        }

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
            var a = ReadBack(under);
            var b = ReadBack(over);
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
            {
                float t = pb[i].a / 255f;
                if (t <= 0f) continue;
                pa[i] = new Color32(
                    (byte)(pa[i].r * (1f - t) + pb[i].r * t),
                    (byte)(pa[i].g * (1f - t) + pb[i].g * t),
                    (byte)(pa[i].b * (1f - t) + pb[i].b * t),
                    255);
            }
            a.SetPixels32(pa);
            a.Apply();
            Object.DestroyImmediate(b);
            return a;
        }

        static Texture2D ReadBack(RenderTexture rt)
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
