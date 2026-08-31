Shader "SpawnRowDuel/Fall"
{
    // What comes DOWN out of the sky: snow, and ash falling slower than snow.
    //
    // Separate from SRD_Veil, which is material the wind drags ACROSS the ground. The two are
    // different weather and they need different geometry: drifting sand belongs on horizontal
    // sheets lying over the dunes, and falling snow belongs on the screen, because a horizontal
    // sheet cannot show vertical motion no matter what you scroll on it.
    //
    // So this is a screen-covering pass with layers of flakes falling down it at different
    // speeds and sizes. The near layers are big, fast and soft; the far ones are small, slow and
    // sharp, which is the whole of the parallax and costs nothing.
    Properties
    {
        _FallColor ("Colour", Color) = (1, 1, 1, 1)
        _Amount    ("Amount", Range(0,3)) = 0
        _Speed     ("Fall speed", Float) = 0.35
        _Drift     ("Sideways drift", Float) = 0.15
        _Size      ("Flake size", Float) = 1
        _Layers    ("Layers", Range(1,5)) = 4
        _Swirl     ("Swirl", Range(0,2)) = 0.5
    }

    SubShader
    {
        // Over everything, including the cards. Falling weather in front of the board is what
        // puts the board out in it, which is the point.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Fall"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _FallColor;
                float _Amount, _Speed, _Drift, _Size, _Layers, _Swirl;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                // The mesh is a [-1,1] quad going to clip space UNTRANSFORMED, so the pass covers
                // the screen whatever the camera is doing and can never be culled by it.
                Varyings o;
                float4 clip = float4(v.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.positionCS = clip;
                o.uv = v.positionOS.xy + 0.5;
                return o;
            }

            // One layer of flakes on a scrolling grid. Each cell holds at most one flake, placed
            // by hash inside it, so they never form rows.
            float Layer(float2 uv, float t, float cells, float speed, float drift, float radius,
                        float seed)
            {
                // Aspect-correct so flakes are round rather than stretched by the window.
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                float2 p = float2(uv.x * aspect, uv.y) * cells;

                // Down the screen, with a slow sideways wander so it is not a rain of vertical
                // tracks. The wander is a function of HEIGHT, so every flake in a column shares
                // it and the whole curtain leans together the way real fall does.
                p.y += t * speed;
                p.x += sin(p.y * 0.35 + seed) * _Swirl * drift * cells * 0.12;
                p.x += t * drift;

                float2 cell = floor(p);
                float2 f = frac(p);

                float2 at = float2(SrdHash(cell + seed), SrdHash(cell + seed + 5.3));
                float present = step(0.62, SrdHash(cell + seed + 11.1));
                float scale = 0.6 + SrdHash(cell + seed + 2.2) * 0.8;

                float d = length((f - at) * float2(1.0, 1.0));
                return present * smoothstep(radius * scale, 0.0, d);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Amount < 0.001) discard;

                float t = _Time.y;
                float acc = 0.0;
                int layers = (int)round(_Layers);

                [unroll(5)]
                for (int l = 0; l < layers; l++)
                {
                    // Near layers: fewer, bigger, faster. Far layers: many, small, slow.
                    float k = l / max(1.0, (float)(layers - 1));
                    float cells = lerp(14.0, 52.0, k);
                    float speed = _Speed * lerp(1.5, 0.45, k);
                    float radius = (0.34 * _Size) * lerp(1.0, 0.42, k);
                    float weight = lerp(1.0, 0.55, k);

                    acc += Layer(i.uv, t, cells, speed, _Drift, radius, 17.0 * (l + 1)) * weight;
                }

                float alpha = saturate(acc) * _Amount;
                return half4(_FallColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
