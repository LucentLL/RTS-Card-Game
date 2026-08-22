Shader "SpawnRowDuel/Grass"
{
    // Blades of grass, one camera-facing quad each, swaying in a wind field.
    //
    // Ported in structure from Dynamic 2D Grass (MIT, Jomoho Games, based on original work by
    // Dylearn). Three things came across, and they are the three that matter:
    //
    //  1. DUAL SCROLLING NOISE. One noise slid past the camera and read as a texture. Two, pulled
    //     a few degrees apart and scrolled at different rates, multiply into gusts.
    //  2. A QUANTISED CLOCK, with a per-blade phase offset. Snapping the sway to ~7 steps a second
    //     is what makes it read as drawn rather than tweened; the phase offset is what stops the
    //     whole field stepping on the same frame, which just looks like dropped frames.
    //  3. SHEAR FROM THE BASE. The blade's foot is pinned and only its tip moves, weighted by
    //     height along the quad - so a field bends, it does not slide.
    //
    // The blade itself is drawn in the fragment rather than sampled from an atlas: the source's
    // art is CC BY 4.0 and this project generates its art in code anyway.
    Properties
    {
        _ColorA     ("Blade A", Color) = (0.30, 0.52, 0.22, 1)
        _ColorB     ("Blade B", Color) = (0.42, 0.63, 0.26, 1)
        _RootColor  ("Root", Color) = (0.16, 0.28, 0.13, 1)

        _Width      ("Blade width", Float) = 0.16
        _Height     ("Blade height", Float) = 0.34
        _Taper      ("Tip taper", Range(0,1)) = 0.86
        _Curve      ("Blade curve", Float) = 0.09

        _WindDir    ("Wind direction (xz)", Vector) = (1, 0.4, 0, 0)
        _WindScale  ("Wind scale", Float) = 0.12
        _WindSpeed  ("Wind speed", Float) = 0.55
        _WindGain   ("Wind gain", Float) = 5.0
        _WindBias   ("Wind bias", Range(-1,1)) = 0.0
        _Sway       ("Sway", Float) = 0.22
        _Framerate  ("Stepped framerate", Float) = 7.0

        // the press field: R is how flattened the grass is, and its GRADIENT is which way it lies
        _DispTex    ("Displacement", 2D) = "black" {}
        _DispOrigin ("Displacement origin (xz)", Vector) = (-18, -14, 0, 0)
        _DispSize   ("Displacement size (xz)", Vector) = (36, 28, 0, 0)
        _PushDist   ("Push distance", Float) = 0.34
        _Flatten    ("Flatten", Range(0,1)) = 0.85

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
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GrassUnlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;   // the blade's FOOT, one value for all four corners
                float2 corner     : TEXCOORD0;  // x -0.5..0.5 across, y 0..1 up
                float2 seeds      : TEXCOORD1;  // per-blade, for phase and colour
                float4 color      : COLOR;      // r tint mix, g height scale, b width scale
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner     : TEXCOORD0;
                float3 footWS     : TEXCOORD1;
                float2 tintLean   : TEXCOORD2;  // x colour mix, y signed lean for the tip highlight
            };

            TEXTURE2D(_DispTex);
            SAMPLER(sampler_DispTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA, _ColorB, _RootColor;
                float _Width, _Height, _Taper, _Curve;
                float4 _WindDir;
                float _WindScale, _WindSpeed, _WindGain, _WindBias, _Sway, _Framerate;
                float4 _DispOrigin, _DispSize, _DispTex_TexelSize;
                float _PushDist, _Flatten;
                float _CloudScale, _CloudSpeed, _CloudContrast, _CloudThreshold, _CloudShadowMin;
                float4 _CloudDir;
                float _CloudAmount;
            CBUFFER_END

            // Transient gusts: xy centre, z ring radius, w strength. Four is the budget - a gust
            // lasts under a second and nothing in this game plays five cards at once.
            #define SRD_GUSTS 4
            float4 _Gusts[SRD_GUSTS];

            float SampleDisp(float2 world)
            {
                float2 uv = (world - _DispOrigin.xy) / _DispSize.xy;
                if (any(uv < 0.0) || any(uv > 1.0)) return 0.0;
                return SAMPLE_TEXTURE2D_LOD(_DispTex, sampler_DispTex, uv, 0).r;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 foot = TransformObjectToWorld(v.positionOS.xyz);

                // A per-blade phase, then a clock quantised to _Framerate. Both halves are load
                // bearing: without the quantisation the sway tweens and looks CG, and without the
                // phase every blade in the field steps on the same frame and it looks like lag.
                float phase = frac(v.seeds.x * 7.13);
                float t = round((_Time.y + phase / max(_Framerate, 0.001)) * _Framerate) / max(_Framerate, 0.001);

                float lean = SrdDualScroll(foot.xz * _WindScale, normalize(_WindDir.xy),
                                           t, _WindSpeed, 10.0, _WindGain, _WindBias);

                // ── what is standing on this blade ──────────────────────────────────────────
                // The press field's VALUE says how flat the grass is here; its GRADIENT says
                // which way it lies, because grass falls away from whatever is pressing it. That
                // pair is the whole displacement idea, and it is the reference's, not ours.
                float press = SampleDisp(foot.xz);
                float2 ts = _DispSize.xy * _DispTex_TexelSize.xy;
                float2 grad = float2(SampleDisp(foot.xz + float2(ts.x, 0)) - SampleDisp(foot.xz - float2(ts.x, 0)),
                                     SampleDisp(foot.xz + float2(0, ts.y)) - SampleDisp(foot.xz - float2(0, ts.y)));
                float2 pushXZ = -grad * 6.0;

                // gusts: a ring travelling outward from wherever the card landed
                [unroll] for (int gi = 0; gi < SRD_GUSTS; gi++)
                {
                    float4 g = _Gusts[gi];
                    float2 rel = foot.xz - g.xy;
                    float dist = length(rel) + 1e-4;
                    float ring = exp(-pow((dist - g.z) * 1.1, 2.0)) * g.w;   // a broad band, not a hairline
                    press = max(press, ring * 0.55);
                    pushXZ += (rel / dist) * ring;
                }

                press = saturate(press);

                // Camera-facing, exactly as the 2D original is by construction. It also solves the
                // top-down problem for free: as the camera pitches over, camUp lies down into the
                // ground plane and the blades lie down with it instead of standing off their tile.
                float3 camRight = normalize(UNITY_MATRIX_V._m00_m01_m02);
                float3 camUp    = normalize(UNITY_MATRIX_V._m10_m11_m12);

                // Vertex colour is stored as bytes, so every channel is 0..1 and the per-blade
                // scales are REMAPPED here rather than written out of range - writing 1.28 into a
                // Color32 silently clamps to 1 and every blade comes out the same size.
                float h = _Height * (0.70 + v.color.g * 0.60) * (1.0 - press * _Flatten);
                float wdt = _Width * (0.75 + v.color.b * 0.50);
                float curveSign = v.color.a * 2.0 - 1.0;

                float3 pos = foot
                           + camRight * (v.corner.x * wdt)
                           + camUp * (v.corner.y * h)
                           + camRight * (curveSign * _Curve * v.corner.y * v.corner.y)  // its own arc
                           + camRight * (lean * _Sway * v.corner.y)    // shear: the foot stays put
                           // pressed grass lies OUT, in world space, away from whatever pressed it
                           + float3(pushXZ.x, 0, pushXZ.y) * _PushDist * v.corner.y;

                o.positionCS = TransformWorldToHClip(pos);
                o.corner = v.corner;
                o.footWS = foot;
                o.tintLean = float2(frac(v.seeds.y * 3.71), lean);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The blade shape, cut in the fragment: a quadratic taper, so it keeps its body
                // most of the way up and narrows near the tip. A linear taper drew a spike, and a
                // field of spikes reads as scratches on the lens rather than as grass.
                float y = i.corner.y;
                float halfW = 0.5 * (1.0 - _Taper * y * y);
                float a = smoothstep(halfW, halfW * 0.55, abs(i.corner.x));
                clip(a - 0.02);

                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, i.tintLean.x);
                col = lerp(_RootColor.rgb, col, saturate(y * 1.6 + 0.15));   // dark at the root

                // a bright edge on the side the wind is pushing toward - a blade catches light
                col *= 1.0 + i.tintLean.y * sign(i.corner.x) * 0.10 * y;

                // as on the ground: normally off, because the overlay pass shades the whole scene
                if (_CloudAmount > 0.001)
                {
                    float cloud = SrdCloudLight(i.footWS.xz, _Time.y, normalize(_CloudDir.xy),
                                                _CloudScale, _CloudSpeed, _CloudContrast,
                                                _CloudThreshold, _CloudShadowMin, 10.0, 0.9, 0.12);
                    col *= lerp(1.0, cloud, _CloudAmount);
                }
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
