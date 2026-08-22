Shader "SpawnRowDuel/Terrain"
{
    // The ground the board stands on: one flat quad, everything else generated in the fragment.
    //
    // Biome is not a texture swap. The three motions - waves, ripples, embers - are separate
    // terms with their own amounts, so "water" is waves at 1 and embers at 0 rather than a
    // different shader, and a biome nobody has thought of yet is a row in a C# table.
    Properties
    {
        _BaseColor   ("Base",  Color) = (0.24, 0.42, 0.20, 1)
        _Tint2       ("Patch 2", Color) = (0.28, 0.47, 0.22, 1)
        _Tint3       ("Patch 3", Color) = (0.20, 0.36, 0.17, 1)
        _Highlight   ("Highlight", Color) = (0.62, 0.78, 0.48, 1)

        _PatchScale  ("Patch scale", Float) = 0.22
        _Patch2Cut   ("Patch 2 threshold", Range(0,1)) = 0.58
        _Patch3Cut   ("Patch 3 threshold", Range(0,1)) = 0.66
        _Grain       ("Grain", Range(0,0.3)) = 0.06

        _WaveAmount  ("Waves", Range(0,1)) = 0
        _RippleAmount("Ripples", Range(0,1)) = 0
        _EmberAmount ("Embers", Range(0,1)) = 0
        _MotionSpeed ("Motion speed", Float) = 0.35

        _IslandExtent("Island half-size (xz)", Vector) = (10, 8, 0, 0)
        _FadeWidth   ("Edge fade", Float) = 2.5

        // clouds, shared with SRD_CloudShadow so the ground and the pieces darken together
        _CloudScale  ("Cloud scale", Float) = 9.0
        _CloudSpeed  ("Cloud speed", Float) = 0.05
        _CloudContrast("Cloud contrast", Float) = 3.2
        _CloudThreshold("Cloud threshold", Float) = 0.16
        _CloudShadowMin("Cloud shadow floor", Range(0,1)) = 0.62
        _CloudDir    ("Cloud direction", Vector) = (1, 0.35, 0, 0)
        _CloudAmount ("Cloud amount", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "TerrainUnlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _Tint2, _Tint3, _Highlight;
                float _PatchScale, _Patch2Cut, _Patch3Cut, _Grain;
                float _WaveAmount, _RippleAmount, _EmberAmount, _MotionSpeed;
                float4 _IslandExtent;
                float _FadeWidth;
                float _CloudScale, _CloudSpeed, _CloudContrast, _CloudThreshold, _CloudShadowMin;
                float4 _CloudDir;
                float _CloudAmount;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 w = i.positionWS.xz;
                float t = _Time.y;

                // ── ground colour: two noise fields cutting patches out of the base ──────────
                float n2 = SrdValueNoise(w * _PatchScale);
                float n3 = SrdValueNoise(w * _PatchScale * 0.43 + 31.7);

                float3 col = _BaseColor.rgb;
                col = lerp(col, _Tint2.rgb, step(_Patch2Cut, n2));
                col = lerp(col, _Tint3.rgb, step(_Patch3Cut, n3));
                col *= 1.0 + (SrdHash(floor(w * 24.0)) - 0.5) * _Grain;   // per-texel grain

                // ── waves ───────────────────────────────────────────────────────────────────
                // Two sine trains at unrelated angles plus a slow drifting swell. The crest is cut
                // NARROW on purpose: a wide one turns the surface into floating ice, which is
                // exactly what the first pass looked like.
                if (_WaveAmount > 0.001)
                {
                    float t2 = t * _MotionSpeed;
                    float s1 = sin(dot(w, float2(3.1, 1.7)) + t2 * 2.6);
                    float s2 = sin(dot(w, float2(-2.2, 3.4)) + t2 * 1.9);
                    float s3 = SrdFbm(w * 1.1 + t2 * 0.22) * 2.0 - 1.0;
                    float surf = s1 * 0.5 + s2 * 0.35 + s3 * 0.5;

                    col *= 1.0 + surf * 0.11 * _WaveAmount;
                    float crest = smoothstep(0.82, 1.02, surf);
                    col = lerp(col, _Highlight.rgb, crest * _WaveAmount * 0.5);
                }

                // ── ripples: wind-blown sand, a slow travelling corduroy ─────────────────────
                if (_RippleAmount > 0.001)
                {
                    float2 rp = w * 2.6 + float2(0.0, t * _MotionSpeed * 0.35);
                    float ridge = sin(rp.y + SrdFbm(w * 0.55) * 6.2831);
                    col *= 1.0 + ridge * 0.09 * _RippleAmount;
                    float drift = smoothstep(0.90, 1.0, SrdValueNoise(w * 1.4 - float2(t * _MotionSpeed * 0.5, 0.0)));
                    col = lerp(col, _Highlight.rgb, drift * _RippleAmount * 0.16);
                }

                // ── embers: sparse points that breathe, so burnt ground still feels alight ───
                // A POINT inside its cell, not the cell. Lighting the whole cell tiles the ground
                // with orange squares, which is what the first pass did and it looked like a bug.
                if (_EmberAmount > 0.001)
                {
                    float2 g = w * 2.4;
                    float2 cell = floor(g);
                    float2 f = frac(g);

                    float seed = SrdHash(cell);
                    float2 spot = float2(SrdHash(cell + 1.7), SrdHash(cell + 4.3));
                    float lit = step(0.90, SrdHash(cell + 5.5));           // only a few cells burn
                    float pulse = 0.55 + 0.45 * sin(t * (1.1 + seed * 2.2) + seed * 6.2831);

                    float glow = smoothstep(0.16, 0.02, length(f - spot));
                    col = lerp(col, _Highlight.rgb, glow * lit * pulse * _EmberAmount);
                }

                // ── cloud shadow ────────────────────────────────────────────────────────────
                // Normally OFF here and left to SRD_CloudShadow, which casts the same field over
                // the whole scene in one pass. Doing it in both places costs the cloud field twice
                // a pixel AND darkens the ground twice as hard as the pieces standing on it.
                if (_CloudAmount > 0.001)
                {
                    float cloud = SrdCloudLight(w, t, normalize(_CloudDir.xy), _CloudScale, _CloudSpeed,
                                                _CloudContrast, _CloudThreshold, _CloudShadowMin,
                                                10.0, 0.9, 0.12);
                    col *= lerp(1.0, cloud, _CloudAmount);
                }

                // ── the island ends: fade rather than cut, so it reads as distance not a lid ─
                float2 d = abs(w) - _IslandExtent.xy;
                float edge = max(d.x, d.y);
                float alpha = 1.0 - smoothstep(-_FadeWidth, 0.0, edge);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
