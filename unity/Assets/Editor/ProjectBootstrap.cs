using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>
/// One-shot batchmode helpers used to bring the project up to the configuration
/// documented in docs/unity/design/03_pipeline_build.md. Safe to re-run.
/// </summary>
public static class ProjectBootstrap
{
    static readonly string[] Packages =
    {
        "com.unity.render-pipelines.universal",
        "com.unity.inputsystem",
        "com.unity.test-framework",
    };

    public static void AddPackages()
    {
        foreach (var id in Packages)
        {
            var req = Client.Add(id);
            while (!req.IsCompleted) Thread.Sleep(100);

            if (req.Status == StatusCode.Success)
                Debug.Log($"[bootstrap] added {req.Result.packageId}");
            else
                Debug.LogError($"[bootstrap] FAILED {id}: {req.Error?.message}");
        }
        AssetDatabase.Refresh();
    }
}
