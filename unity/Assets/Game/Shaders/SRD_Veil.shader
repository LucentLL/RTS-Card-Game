Shader "SpawnRowDuel/Veil"
{
    // What is in the AIR over the battlefield: sand streaming off the crests, snow blowing low
    // across the drifts, ash drifting, spray coming off the water.
    //
    // This is the layer that makes the ground look like weather is happening to it, and there is
    // no cheaper way to get it at this camera angle. The camera looks DOWN at 42 degrees, so a
    // stack of large horizontal sheets low over the ground reads as material streaming across it
    // rather than as a fog card standing up in the world - a billboard would show its edges the
    // moment anything moved.
    //
    // It draws LAST, over the cards, because that is the point: specks blowing across the board
    // are what put the board inside the weather instead of on top of a picture of it.
    Properties
    {
        _VeilColor ("Colour", Color) = (0.9, 0.84, 0.65, 1)
        _Amount    ("Amount", Range(0,2)) = 1
        _Speed     ("Speed", Float) = 1.5
        _Scale     ("Scale", Float) = 7
        _WindDir   ("Wind (xz)", Vector) = (1, 0, 0, 0)
        _SunDir    ("Sun (world)", Vector) = (0.2, 0.25, 0.9, 0)
        _SunColor  ("Sun colour", Color) = (1, 0.9, 0.75, 1)
        _NearFade  ("Near fade", Float) = 3.5
        _Specks    ("Specks", Range(0,2)) = 1
        _BoardHalf ("Board half-size (xz)", Vector) = (4, 3.3, 0, 0)
        _BoardClear("Clear the board", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always          // the sheets are thin air; depth-testing them against the dunes
        Cull Off              // just makes them vanish behind the crest they are blowing over

        Pass
        {
            Name "Veil"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;      // r: layer 0..1 (height), g: per-layer seed
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 layer      : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _VeilColor, _SunDir, _SunColor, _WindDir;
                float _Amount, _Speed, _Scale, _NearFade, _Specks;
                float4 _BoardHalf;
                float _BoardClear;
            CBUFFER_END

            float4 _Gusts[4];   // xy origin, z ring radius, w strength

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.layer = v.color.rg;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Amount < 0.001) discard;

                float2 w = i.positionWS.xz;
                float t = _Time.y;
                float2 wind = normalize(_WindDir.xz + float2(0.0001, 0));
                float layer = i.layer.x;
                float seed = i.layer.y * 37.0;

                // Higher sheets run faster and are stretched further - that shear is most of what
                // reads as wind rather than as a scrolling texture.
                float speed = _Speed * (0.55 + layer * 1.1);
                float2 uv = float2(dot(w, wind), dot(w, float2(-wind.y, wind.x)));
                uv.x -= t * speed;
                uv += seed;

                // Two scales: long torn sheets, and a finer break-up inside them.
                float sheet = SrdFbm(float2(uv.x * 0.055, uv.y * 0.42) * _Scale * 0.3);
                float broken = SrdFbm(float2(uv.x * 0.20, uv.y * 1.05) * _Scale * 0.3 + 13.0);

                float veil = saturate(sheet * 0.75 + broken * 0.45 - 0.52) * 2.2;

                // Thinner higher up, and never a hard lid at the top of the stack.
                veil *= (1.0 - layer * 0.55);

                // A gust shoves material outward in a ring - what a card landing throws up.
                float gust = 0.0;
                [unroll]
                for (int g = 0; g < 4; g++)
                {
                    float2 d = w - _Gusts[g].xy;
                    float r = length(d);
                    float ring = 1.0 - saturate(abs(r - _Gusts[g].z) / 1.6);
                    gust += ring * ring * _Gusts[g].w;
                }
                veil += saturate(gust) * 0.55 * (1.0 - layer * 0.4);

                // Individual specks catching the light. Sparse, small, and faster than the sheets.
                float speck = 0.0;
                if (_Specks > 0.001)
                {
                    float2 sp = float2(uv.x * 1.9, uv.y * 1.9) * 3.0;
                    float2 cell = floor(sp);
                    float2 f = frac(sp);
                    float2 at = float2(SrdHash(cell + seed), SrdHash(cell + seed + 3.1));
                    float lit = step(0.955, SrdHash(cell + seed + 7.7));
                    speck = lit * smoothstep(0.13, 0.0, length(f - at)) * _Specks;
                }

                // Over the BOARD the sheets thin out but the specks do not.
                //
                // This is the one place the scenery has to give way: a sheet of blowing sand
                // across a card is a card you cannot read, and no amount of atmosphere is worth
                // that. Specks crossing it are the opposite - they put the board inside the
                // weather at no cost to legibility, which is the whole effect anyway.
                float2 overBoard = saturate((abs(w) - _BoardHalf.xy) / 2.2 + 1.0);
                float clear = 1.0 - _BoardClear * (1.0 - saturate(max(overBoard.x, overBoard.y)));

                float alpha = saturate(veil * 0.17 * clear + speck * 0.42) * _Amount;

                // Never right on top of the camera - a sheet crossing the near plane is a smear.
                float dist = length(_WorldSpaceCameraPos - i.positionWS);
                alpha *= saturate((dist - _NearFade) / 3.0);

                // Airborne material is lit from the side and glows where it is between you and
                // the sun, which is exactly when blowing sand is most visible.
                float3 V = normalize(i.positionWS - _WorldSpaceCameraPos);
                float toward = saturate(dot(V, normalize(_SunDir.xyz)) * 0.5 + 0.5);
                float3 col = _VeilColor.rgb * (0.72 + 0.55 * toward)
                           + _SunColor.rgb * speck * 0.5;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
