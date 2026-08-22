Shader "SpawnRowDuel/CloudShadow"
{
    // Cloud shadows over the whole scene, as one multiply pass.
    //
    // The Godot original does this with a CanvasLayer ColorRect in `blend_mul` - a 2D game can
    // just multiply the screen. In 3D the same trick needs the extra step of asking, per pixel,
    // WHERE ON THE GROUND that pixel is: fire the view ray, intersect the ground plane, sample the
    // cloud field there. Doing it that way is what makes the shadow stay put on the board when the
    // camera swings between the two angles, instead of sliding around like a decal on the lens.
    //
    // A piece standing on the board is shaded by the cloud BEHIND it rather than the cloud it
    // stands in. That is the same approximation the original makes with its pseudo-3D Y offset,
    // and at this camera pitch the difference is under half a tile.
    Properties
    {
        _CloudScale     ("Cloud scale", Float) = 9.0
        _CloudSpeed     ("Cloud speed", Float) = 0.05
        _CloudContrast  ("Cloud contrast", Float) = 3.2
        _CloudThreshold ("Cloud threshold", Float) = 0.16
        _CloudShadowMin ("Cloud shadow floor", Range(0,1)) = 0.62
        _CloudDir       ("Cloud direction", Vector) = (1, 0.35, 0, 0)
        _CloudAmount    ("Cloud amount", Range(0,1)) = 1
        _GroundY        ("Ground height", Float) = 0
        _ShadowTint     ("Shadow tint", Color) = (0.72, 0.78, 1.0, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Overlay" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "CloudMultiply"
            Blend DstColor Zero        // multiply, the same as Godot's blend_mul
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 farWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float _CloudScale, _CloudSpeed, _CloudContrast, _CloudThreshold, _CloudShadowMin;
                float4 _CloudDir;
                float _CloudAmount, _GroundY;
                float4 _ShadowTint;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                // The mesh is a [-1,1] quad and goes to clip space UNTRANSFORMED, so the pass
                // covers the screen whatever the camera is doing and can never be culled by it.
                Varyings o;
                float4 clip = float4(v.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.positionCS = clip;

                float4 far = mul(UNITY_MATRIX_I_VP, float4(clip.xy, 1.0, 1.0));
                o.farWS = far.xyz / far.w;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = normalize(i.farWS - cam);

                // no ground under this pixel (looking at or above the horizon): multiply by white
                if (ray.y > -1e-4) return half4(1, 1, 1, 1);

                float3 hit = cam + ray * ((_GroundY - cam.y) / ray.y);

                float light = SrdCloudLight(hit.xz, _Time.y, normalize(_CloudDir.xy),
                                            _CloudScale, _CloudSpeed, _CloudContrast,
                                            _CloudThreshold, _CloudShadowMin, 10.0, 0.9, 0.12);

                // a shadow on a sunlit field is BLUER than the field, not just darker - tinting
                // toward the sky colour is most of what sells it
                float3 shade = lerp(_ShadowTint.rgb, float3(1, 1, 1), light);
                return half4(lerp(float3(1, 1, 1), shade, _CloudAmount), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
