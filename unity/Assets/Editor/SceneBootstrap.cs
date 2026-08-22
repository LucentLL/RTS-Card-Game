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

    static Material Mat(string name, Color c, float smoothness = 0.1f)
    {
        var path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
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

        var mCell = Mat("M_Cell", new Color(0.16f, 0.17f, 0.22f));
        var mLane = Mat("M_Lane", new Color(0.30f, 0.26f, 0.14f));
        var mStruct = Mat("M_Struct", new Color(0.20f, 0.16f, 0.12f));
        var mWall = Mat("M_Wall", new Color(0.30f, 0.13f, 0.13f));
        var mHover = Mat("M_Hover", new Color(0.35f, 0.55f, 0.42f), 0.35f);
        var mSelect = Mat("M_Select", new Color(0.85f, 0.70f, 0.25f), 0.5f);

        // the two halves of the board, tinted by owner - the reference reads cold-over-warm
        var mFoeBack = Mat("M_FoeBack", new Color(0.10f, 0.20f, 0.32f));
        var mFoeFront = Mat("M_FoeFront", new Color(0.13f, 0.24f, 0.36f));
        var mYouFront = Mat("M_YouFront", new Color(0.30f, 0.13f, 0.10f));
        var mYouBack = Mat("M_YouBack", new Color(0.25f, 0.10f, 0.08f));

        var mTerrain = ShaderMat("M_Terrain", "SpawnRowDuel/Terrain");
        var mGrass = ShaderMat("M_Grass", "SpawnRowDuel/Grass");
        var mClouds = ShaderMat("M_CloudShadow", "SpawnRowDuel/CloudShadow");
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

        var boardGo = new GameObject("Board");
        var view = boardGo.AddComponent<BoardView>();
        view.CellMaterial = mCell;
        view.LaneMaterial = mLane;
        view.StructureSlotMaterial = mStruct;
        view.WallMaterial = mWall;
        view.HoverMaterial = mHover;
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

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log("[scene] saved " + ScenePath + " (board is generated at runtime from Board geometry)");
    }
}
