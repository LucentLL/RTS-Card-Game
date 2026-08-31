using System.IO;
using SpawnRowDuel.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Battle scene. The scene itself is deliberately thin - a camera, a light, and one
/// BoardView object. The board is generated at runtime from the rules geometry so the view cannot
/// drift out of agreement with the engine. Idempotent.
/// </summary>
public static class SceneBootstrap
{
    const string SceneDir = "Assets/Scenes";
    const string ScenePath = SceneDir + "/Battle.unity";
    const string MatDir = "Assets/Settings";

    /// <summary>An opaque URP Lit material asset - the scene has to reference it or the WebGL
    /// stripper takes the shader with it.</summary>
    static Material LitMat(string name, Color c)
    {
        var path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A board cell: a translucent fill with a brighter rim, on SRD_Tile.</summary>
    static Material TileMat(string name, Color fill, Color edge)
    {
        var m = ShaderMat(name, "SpawnRowDuel/Tile");
        if (m == null) return null;
        m.SetColor("_BaseColor", fill);
        m.SetColor("_EdgeColor", edge);
        m.SetFloat("_EdgeWidth", 0.055f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>
    /// A material ASSET on one of the project's own shaders.
    ///
    /// It has to be an asset, and the scene has to reference it: the terrain shaders are only ever
    /// reached through a material built at runtime, and the WebGL stripper deletes shaders nothing
    /// serialized points at. This is the same rule that already governs the sprite anchor and the
    /// physics collider in this scene - anything created at runtime needs a baked witness.
    /// </summary>
    static Material ShaderMat(string name, string shader)
    {
        var path = MatDir + "/" + name + ".mat";
        var s = Shader.Find(shader);
        if (s == null)
        {
            Debug.LogError("[scene] shader not found: " + shader);
            return null;
        }

        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(s);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = s;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static void Build()
    {
        Directory.CreateDirectory(SceneDir);
        Directory.CreateDirectory(MatDir);

        // Cells are TRANSLUCENT MARKINGS on the terrain now, not opaque slabs. The wash is faint
        // and the rim carries the grid, because an edge reads as a boundary at any opacity where a
        // flat 30% tint just reads as a stain.
        // Fills are SATURATED and the rim is faint, which is the opposite of the first attempt.
        // A pale tint at 26% over green grass desaturates toward grey from every angle, and the
        // board lost the one thing its colour is for: whose ground this is. Strong hue at a low
        // alpha keeps the grass looking like grass and still says red half / blue half.
        var rim = new Color(1f, 1f, 1f, 0.20f);
        var mCell = TileMat("M_Cell", new Color(0.45f, 0.48f, 0.62f, 0.28f), rim);
        var mLane = TileMat("M_Lane", new Color(0.88f, 0.74f, 0.26f, 0.32f), rim);
        var mStruct = TileMat("M_Struct", new Color(0.55f, 0.42f, 0.26f, 0.32f), rim);
        var mHover = TileMat("M_Hover", new Color(0.40f, 0.95f, 0.60f, 0.48f),
                             new Color(0.75f, 1f, 0.88f, 0.90f));
        var mSelect = TileMat("M_Select", new Color(1f, 0.82f, 0.28f, 0.52f),
                              new Color(1f, 0.95f, 0.60f, 0.95f));

        // the two halves of the board, tinted by owner - the reference reads cold-over-warm
        var mFoeBack = TileMat("M_FoeBack", new Color(0.16f, 0.44f, 0.90f, 0.40f), rim);
        var mFoeFront = TileMat("M_FoeFront", new Color(0.24f, 0.54f, 0.94f, 0.32f), rim);
        var mYouFront = TileMat("M_YouFront", new Color(0.90f, 0.30f, 0.18f, 0.32f), rim);
        var mYouBack = TileMat("M_YouBack", new Color(0.82f, 0.20f, 0.13f, 0.40f), rim);

        // No wall material: the two castle walls are the screen's top and bottom bands now
        // (WallBands), not slabs lying on the grass past each back row.

        // the worker pawns: opaque URP Lit, so a figure reads as a figure. They used to borrow
        // the tile material, which is a translucent marking wash.
        var mPawn = LitMat("M_Pawn", new Color(0.86f, 0.84f, 0.80f));

        // the campaign globe: vertex-coloured tiles, and flat-shaded borders over them
        var mGlobe = ShaderMat("M_Globe", "SpawnRowDuel/Globe");
        var mGlobeBorder = ShaderMat("M_GlobeBorder", "SpawnRowDuel/Globe");
        if (mGlobeBorder != null) mGlobeBorder.SetFloat("_Shade", 0f);

        var mTerrain = ShaderMat("M_Terrain", "SpawnRowDuel/Terrain");
        var mGrass = ShaderMat("M_Grass", "SpawnRowDuel/Grass");
        var mClouds = ShaderMat("M_CloudShadow", "SpawnRowDuel/CloudShadow");
        var mVeil = ShaderMat("M_Veil", "SpawnRowDuel/Veil");
        var mFall = ShaderMat("M_Fall", "SpawnRowDuel/Fall");
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        cam.transform.position = new Vector3(0f, 6.4f, -6.9f);
        cam.transform.rotation = Quaternion.Euler(42f, 0f, 0f);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

        // A baked collider so the scene itself references the Physics module - the runtime-
        // generated board is invisible to the engine stripper (belt to link.xml's braces).
        var anchor = new GameObject("PhysicsAnchor");
        var anchorCol = anchor.AddComponent<BoxCollider>();
        anchorCol.size = Vector3.one * 0.01f;
        anchor.transform.position = new Vector3(0f, -50f, 0f);

        // ...and a baked SpriteRenderer for the same reason: the card standees create theirs at
        // runtime, and without one serialized reference the sprite default shader would strip.
        var spriteAnchor = new GameObject("SpriteAnchor");
        spriteAnchor.transform.SetParent(anchor.transform, false);
        var anchorSprite = spriteAnchor.AddComponent<SpriteRenderer>();
        anchorSprite.sprite =
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/Cards/Creatures/Fire/sparkimp_cardart.png");
        if (anchorSprite.sprite == null)
            Debug.LogWarning("[scene] no anchor sprite - run the card importer / junction setup first");

        // The ground the board stands on. Its own object, not the board's: the board is generated
        // from the rules geometry and rebuilt whenever that changes, and the scenery must not be
        // something that can take the board down with it.
        var terrainGo = new GameObject("Terrain");
        var terrain = terrainGo.AddComponent<SpawnRowDuel.View.World.TerrainField>();
        terrain.TerrainMaterial = mTerrain;
        terrain.GrassMaterial = mGrass;
        terrain.CloudMaterial = mClouds;
        terrain.VeilMaterial = mVeil;
        terrain.FallMaterial = mFall;

        var boardGo = new GameObject("Board");
        var view = boardGo.AddComponent<BoardView>();
        view.CellMaterial = mCell;
        view.LaneMaterial = mLane;
        view.StructureSlotMaterial = mStruct;
        view.HoverMaterial = mHover;
        view.PawnMaterial = mPawn;
        view.SelectMaterial = mSelect;
        view.FoeBackMaterial = mFoeBack;
        view.FoeFrontMaterial = mFoeFront;
        view.YouFrontMaterial = mYouFront;
        view.YouBackMaterial = mYouBack;

        var input = boardGo.AddComponent<BoardInput>();
        input.Cam = cam;

        // The engine wiring: a real match on the real rules core, booted from the imported
        // card database. The serialized reference is what pulls the database (and every
        // CardDefinition it indexes) into the build.
        var match = boardGo.AddComponent<MatchController>();
        match.Board = view;
        match.Database = AssetDatabase.LoadAssetAtPath<SpawnRowDuel.Data.CardDatabase>(
            "Assets/Game/Data/CardDatabase.asset");
        if (match.Database == null)
            Debug.LogWarning("[scene] CardDatabase.asset missing - run the card importer first");

        boardGo.AddComponent<MatchHud>();

        // ── the campaign globe ──────────────────────────────────────────────────────────
        //
        // Its own camera and its own root, both switched off until the world map asks for them.
        // Sharing the duel's camera would mean one component owning two framings that have
        // nothing to do with each other, and BoardInput reframes its camera every single frame.
        var globeGo = new GameObject("Globe");
        globeGo.transform.position = Vector3.zero;
        var globe = globeGo.AddComponent<SpawnRowDuel.View.Campaign.GlobeView>();
        globe.TileMaterial = mGlobe;
        globe.BorderMaterial = mGlobeBorder;

        var globeCamGo = new GameObject("GlobeCamera") { };
        var globeCam = globeCamGo.AddComponent<Camera>();
        globeCam.clearFlags = CameraClearFlags.SolidColor;
        globeCam.backgroundColor = new Color(0.039f, 0.055f, 0.09f);
        globeCam.fieldOfView = 34f;
        globeCam.nearClipPlane = 0.05f;
        globeCam.transform.position = new Vector3(0f, 0f, -3.9f);
        globeCam.transform.rotation = Quaternion.identity;
        globeCam.enabled = false;

        var globeLightGo = new GameObject("GlobeRim");
        var globeLight = globeLightGo.AddComponent<Light>();
        globeLight.type = LightType.Directional;
        globeLight.intensity = 0.9f;
        globeLightGo.transform.rotation = Quaternion.Euler(28f, 34f, 0f);

        // ── the shell: menus, campaign flow, deck builder ───────────────────────────────
        var shellGo = new GameObject("Shell");
        var shell = shellGo.AddComponent<SpawnRowDuel.View.Shell.GameShell>();
        shell.Match = match;
        shell.Globe = globe;
        shell.GlobeCamera = globeCam;
        shell.BattleRoot = boardGo;
        shell.TerrainRoot = terrainGo;
        shell.BattleCamera = cam;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log("[scene] saved " + ScenePath + " (board is generated at runtime from Board geometry)");
    }
}
