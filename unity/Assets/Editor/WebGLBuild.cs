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

        // ── the two settings that decide whether a crash can be diagnosed at all ──────────
        //
        // The project shipped at exceptionSupport 1, "explicitly thrown exceptions only", and
        // that does not mean what it sounds like: it tells IL2CPP to STOP EMITTING the checks
        // for null references, array bounds and invalid casts, and Unity documents all three as
        // undefined behaviour from then on. So a stray index does not throw - it scribbles the
        // heap, the page keeps running with corrupt pixels in it, and some seconds later the
        // runtime dies with "RuntimeError: index out of bounds" and twenty-five unnamed wasm
        // frames pointing at wherever the damage happened to surface. That is exactly the report
        // we got, and it is why reading the source could not place it.
        //
        // 2 puts the checks back: an out-of-range write becomes an IndexOutOfRangeException that
        // names its own type and method, is caught and logged, and the next frame still runs.
        // Not 3 - 3 additionally builds stack traces, which is the expensive half in both size
        // and speed, and this is a turn-based card game downloaded over Pages onto a phone.
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;

        // ...and symbols, which cost nothing at runtime - the loader fetches the sidecar only
        // when it is formatting a stack. Without them "wasm-function[579]" is all anybody gets,
        // including for a fault inside Unity's own UI renderer or texture upload, where
        // exception support has no reach at all.
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.External;

        // ── the heap the whole game lives in ──────────────────────────────────────────────
        //
        // It started at THIRTY-TWO megabytes and grew 20% at a time, which is too small for a game
        // that loads four hundred textures, a URP pipeline, two UI Toolkit panels and a
        // fifty-thousand-vertex terrain before a card is played. Half a gigabyte up front, growth
        // still available to 2GB behind it.
        //
        // TWO CORRECTIONS to the reasoning this line was originally committed with (a3a1d07),
        // recorded because the crash it was meant to fix CAME BACK at 512MB, on a phone, with the
        // same stack:
        //
        //   1. "Every growth step is a new ArrayBuffer and a full copy" is the ASM.JS model. This
        //      is a wasm build: the shipped framework's own growMemory is wasmMemory.grow(pages)
        //      followed by updateMemoryViews(), which grows in place against reserved address
        //      space and rebuilds the JS typed-array views. There is no copy to fail, so that was
        //      never the mechanism.
        //   2. "Indirect call to null under MemoryManager::Reallocate is the heap being out of
        //      room" does not follow either. This build links ABORTING_MALLOC=1, so an exhausted
        //      heap aborts with "Aborted(OOM)" - a different message entirely. A null function
        //      pointer at that site is consistent with GetAllocator(label) simply returning null.
        //
        // So do not reach for this number again when the crash recurs: it is not established that
        // the heap is the problem, and on a low-RAM Android device a 512MB up-front reservation is
        // HARDER to satisfy than a small one that grows. Lowering it back toward 256 is a
        // legitimate experiment. The evidence actually worth collecting is in the browser CONSOLE
        // (see the errorHandler in Assets/WebGLTemplates/Fullscreen/index.html, which now logs the
        // live heap size, the device memory and the build stamp at the moment of the crash).
        PlayerSettings.WebGL.memorySize = 512;
        PlayerSettings.WebGL.initialMemorySize = 512;

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
            return;
        }

        // ...and quit EXPLICITLY, because the shell no longer passes -quit. With -quit the editor
        // was free to shut down while Bee was still linking, which cancelled the build at a
        // different step every time it happened.
        EditorApplication.Exit(0);
    }
}
