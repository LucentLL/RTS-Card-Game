using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the placeholder Battle scene: a 7x5 board laid out to the geometry in
/// docs/unity/spec/01_board_geometry_state.md (C=7, ROWS=5, CENTER_LANES 1/3/5),
/// viewed from the "Tilted" diorama angle. Placeholder only — it exists to prove
/// the render + build pipeline end to end. Idempotent.
/// </summary>
public static class SceneBootstrap
{
    const string Dir = "Assets/Scenes";
    const string ScenePath = Dir + "/Battle.unity";

    const int Cols = 7;
    static readonly string[] Rows = { "foeBack", "foeFront", "center", "youFront", "youBack" };
    static readonly int[] CenterLanes = { 1, 3, 5 };

    static Material Mat(string name, Color c)
    {
        var path = $"Assets/Settings/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        return m;
    }

    public static void Build()
    {
        Directory.CreateDirectory(Dir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var matBase = Mat("M_Cell", new Color(0.16f, 0.17f, 0.22f));
        var matLane = Mat("M_Lane", new Color(0.30f, 0.26f, 0.14f));
        var matWall = Mat("M_Wall", new Color(0.28f, 0.13f, 0.13f));

        var root = new GameObject("Board");

        for (int r = 0; r < Rows.Length; r++)
        {
            var rowGo = new GameObject(Rows[r]);
            rowGo.transform.SetParent(root.transform);

            for (int c = 0; c < Cols; c++)
            {
                var cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cell.name = $"{Rows[r]}_{c}";
                cell.transform.SetParent(rowGo.transform);
                cell.transform.localPosition = new Vector3(c - (Cols - 1) / 2f, 0f, (Rows.Length - 1) / 2f - r);
                cell.transform.localScale = new Vector3(0.92f, 0.12f, 0.92f);

                bool isCenterRow = Rows[r] == "center";
                bool isLane = System.Array.IndexOf(CenterLanes, c) >= 0;
                var mr = cell.GetComponent<MeshRenderer>();
                mr.sharedMaterial = isCenterRow ? (isLane ? matLane : matBase) : matBase;
            }
        }

        // Walls at rows 0 and 6 are life targets (spec/03). Placeholder markers.
        foreach (var (z, nm) in new[] { (3.4f, "Wall_Foe"), (-3.4f, "Wall_You") })
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = nm;
            wall.transform.SetParent(root.transform);
            wall.transform.localPosition = new Vector3(0f, 0.22f, z);
            wall.transform.localScale = new Vector3(Cols * 0.98f, 0.44f, 0.30f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = matWall;
        }

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        cam.transform.position = new Vector3(0f, 6.2f, -6.6f);
        cam.transform.rotation = Quaternion.Euler(42f, 0f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log($"[scene] saved {ScenePath} with {Cols * Rows.Length} cells + 2 walls");
    }
}
