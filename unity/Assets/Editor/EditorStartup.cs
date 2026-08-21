using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the project behave when opened INTERACTIVELY (the whole port has been driven headlessly,
/// so the editor had no last-scene preference and opened an empty untitled scene - pressing Play
/// showed sky and nothing else):
///
/// 1. playModeStartScene: pressing Play ALWAYS starts Battle.unity, whatever scene is open.
///    In-memory editor state, so it is re-applied on every domain reload - both here in the
///    [InitializeOnLoad] ctor AND again in the delayCall, because on a cold import (fresh
///    clone / deleted Library) the ctor can run before Battle.unity is registered in the asset
///    database and the first attempt silently loads null.
/// 2. On the first domain load of an editor session, if the active scene is untitled, open
///    Battle.unity so the Hierarchy shows the real game scene instead of a default sky.
///
/// Batch runs (-batchmode: tests, imports, builds) are never touched.
/// </summary>
[InitializeOnLoad]
public static class EditorStartup
{
    const string ScenePath = "Assets/Scenes/Battle.unity";
    const string SessionFlag = "srd.openedBattleScene";

    static EditorStartup()
    {
        if (Application.isBatchMode) return;

        ApplyPlayModeStartScene();
        EditorApplication.delayCall += OpenBattleIfUntitled;
    }

    static void ApplyPlayModeStartScene()
    {
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene != null) EditorSceneManager.playModeStartScene = scene;
    }

    static void OpenBattleIfUntitled()
    {
        // Mid-import or mid-compile: re-queue rather than open a scene whose dependencies are
        // still importing. This must sit ABOVE the session flag so a re-queue never consumes it.
        if (EditorApplication.isUpdating || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += OpenBattleIfUntitled;
            return;
        }

        // The asset database is ready here even on a cold import - retry the Play-scene pin.
        ApplyPlayModeStartScene();

        if (Application.isPlaying) return;
        if (SessionState.GetBool(SessionFlag, false)) return;
        SessionState.SetBool(SessionFlag, true);

        var active = EditorSceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(active.path)) return;      // a real scene is open - leave it
        if (active.isDirty && active.rootCount > 0) return;  // never discard deliberate work

        if (File.Exists(Path.Combine(Application.dataPath, "Scenes/Battle.unity")))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
