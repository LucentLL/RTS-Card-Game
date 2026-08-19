using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Creates the URP pipeline + renderer assets and binds them to Graphics/Quality
/// settings. CLI project creation yields the built-in pipeline, so this reproduces
/// what the Hub's "Universal 3D" template does. Idempotent.
/// </summary>
public static class UrpBootstrap
{
    const string Dir = "Assets/Settings";
    const string RendererPath = Dir + "/RTS_Renderer.asset";
    const string PipelinePath = Dir + "/RTS_URPAsset.asset";

    public static void Configure()
    {
        Directory.CreateDirectory(Dir);

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
            Debug.Log("[urp] created renderer data");
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            Debug.Log("[urp] created pipeline asset");
        }

        AssetDatabase.SaveAssets();

        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int i = 0; i < QualitySettings.count; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }

        PlayerSettings.companyName = "LucentLL";
        PlayerSettings.productName = "RTS TCG";

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[urp] defaultRenderPipeline = {GraphicsSettings.defaultRenderPipeline?.name ?? "NULL"}");
        Debug.Log($"[urp] quality levels bound  = {QualitySettings.count}");
        Debug.Log($"[urp] product = {PlayerSettings.companyName} / {PlayerSettings.productName}");
    }
}
