using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Headless WebGL build for the GitHub Pages playtest surface.
/// Brotli + decompressionFallback is required: GitHub Pages does not send the
/// Content-Encoding header Unity's loader expects, so without the fallback the
/// build fails to parse at load time.
/// </summary>
public static class WebGLBuild
{
    public static void Build()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.template = "PROJECT:Fullscreen";
        PlayerSettings.runInBackground = true;

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Battle.unity" },
            locationPathName = "Build/WebGL",
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[build] result={s.result} size={s.totalSize / (1024 * 1024)}MB errors={s.totalErrors} warnings={s.totalWarnings}");
        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[build] FAILED");
            EditorApplication.Exit(1);
        }
    }
}
