Shader "SpawnRowDuel/Globe"
{
    // The campaign globe: vertex-coloured tiles, lit by a light fixed to the CAMERA rather than
    // to the world.
    //
    // That is not a shortcut, it is the look. A world light would leave half the planet in the
    // dark, and the dark half is where a third of the map's territories are - you would be
    // spinning the globe to read it rather than to reach it. The browser build made the same
    // choice with a hand-rolled `0.62 + 0.5 * max(0, n·L)`, and those constants are kept.
    Properties
    {
        _Shade ("Shade amount", Range(0,1)) = 1
        _Ambient ("Ambient floor", Range(0,1)) = 0.62
        _Gain ("Light gain", Range(0,2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // The globe is BACK-TO-FRONT, not depth-sorted, and that is worth saying out loud: this
        // pass has no LightMode tag, so URP draws it as SRPDefaultUnlit and its depth never
        // reaches the prepass - what actually decides who covers whom is Cull Back (which removes
        // the far hemisphere outright) and the order triangles are written in. A DepthOnly pass
        // is declared below so the globe at least contributes to the depth buffer for anything
        // that reads it, but GlobeView still emits each tile bottom-up on purpose.
        Pass
        {
            Name "GlobeUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalVS   : TEXCOORD0;
                float4 color      : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Shade, _Ambient, _Gain;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalVS = mul((float3x3)UNITY_MATRIX_V, nWS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 L = normalize(float3(-0.45, 0.55, 0.72));
                float lum = _Ambient + _Gain * max(0.0, dot(normalize(i.normalVS), L));
                float k = lerp(1.0, lum, _Shade);
                return half4(i.color.rgb * k, i.color.a);
            }
            ENDHLSL
        }

        // Depth only, so the globe is in the prepass and in the camera's depth texture rather
        // than being a hole in both.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vertDepth(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 fragDepth() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
