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
        _Grains    ("Grains", Range(0,2)) = 1
        _GrainColor("Grain colour", Color) = (1, 0.95, 0.84, 1)

        // how the wind varies: how far it breathes, how long a breath takes, how far it wanders
        _GustSwing ("Gust swing", Range(0,1.5)) = 0.45
        _GustPeriod("Gust period (s)", Float) = 15
        _GustWander("Gust wander (deg)", Float) = 12

        // a card landing shoves the air out in the shape of the card, not in a circle
        _GustHalf  ("Gust half-size (xz)", Vector) = (0.5, 0.72, 0, 0)
        _GustRound ("Gust corner radius", Float) = 0.22
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
                float _Amount, _Speed, _Scale, _NearFade, _Grains;
                float _GustSwing, _GustPeriod, _GustWander;
                float4 _GrainColor, _GustHalf;
                float _GustRound;
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
                float layer = i.layer.x;
                float seed = i.layer.y * 37.0;

                // ── the wind BREATHES ──────────────────────────────────────────────────────
                //
                // One speed, one bearing and one density held forever is a scrolling texture
                // however good the noise inside it is, and the desert is where that reads worst
                // because the air is the subject. Two slow noises on their own clocks: one swings
                // the strength from a lull to about double, the other wanders the bearing either
                // side of the dune trend. Neither is periodic against the other, so the field
                // never repeats - which is the whole point, since a repeat IS the tell.
                float breath = SrdFbm(float2(t / max(_GustPeriod, 0.1), 4.7)) * 2.0 - 1.0;
                float gustNow = saturate(1.0 + breath * _GustSwing);
                float swingDeg = (SrdFbm(float2(t / max(_GustPeriod * 1.7, 0.1), 19.3)) - 0.5)
                                 * 2.0 * _GustWander;
                float2 wind = SrdRotate(normalize(_WindDir.xz + float2(0.0001, 0)), swingDeg);

                // Higher sheets run faster and are stretched further - that shear is most of what
                // reads as wind rather than as a scrolling texture.
                float speed = _Speed * (0.55 + layer * 1.1);
                float2 uv = float2(dot(w, wind), dot(w, float2(-wind.y, wind.x)));
                uv.x -= t * speed;
                uv += seed;

                // The sheets TEAR as they travel. Scrolling one noise field moves a fixed
                // pattern past the camera however many octaves it has; displacing the field along
                // its own across-wind axis by a second, slower noise makes the streamers stretch
                // and part instead, which is what blowing sand actually does over a crest.
                float warp = SrdFbm(float2(uv.x * 0.085, uv.y * 0.5) * _Scale * 0.3 + 7.0) - 0.5;
                float2 su = float2(uv.x, uv.y + warp * 2.4);

                // Two scales: long torn sheets, and a finer break-up inside them.
                float sheet = SrdFbm(float2(su.x * 0.055, su.y * 0.42) * _Scale * 0.3);
                float broken = SrdFbm(float2(su.x * 0.20, su.y * 1.05) * _Scale * 0.3 + 13.0);

                // A smooth shoulder rather than a clamp. The old form pinned most of the field at
                // zero and let the rest come up with a hard edge, so the sheets had outlines.
                float veil = smoothstep(0.52, 0.94, sheet * 0.62 + broken * 0.38) * 1.7;

                // Thinner higher up, and never a hard lid at the top of the stack.
                veil *= (1.0 - layer * 0.55);

                // A gust shoves material outward in a ring - what a card landing throws up.
                float gust = 0.0;
                [unroll]
                for (int g = 0; g < 4; g++)
                {
                    float2 d = w - _Gusts[g].xy;
                    float r = SrdRoundBox(d, _GustHalf.xy, _GustRound);
                    float ring = 1.0 - saturate(abs(r - _Gusts[g].z) / 1.6);
                    gust += ring * ring * _Gusts[g].w;
                }
                veil += saturate(gust) * 0.55 * (1.0 - layer * 0.4);

                // ---- grains ----------------------------------------------------------------
                // Sand does not travel as dots. It SALTATES: a grain hops downwind in a long flat
                // arc, so what a camera catches is a dash - the grain smeared along its own path -
                // and a field of round specks at one size and one speed is the tell that this is a
                // noise texture rather than moving air. That is what the dunes had.
                //
                // Three passes of dashes then, each a different length at a different speed, each
                // grain sliding through its own cell over its own short life so it fades in, runs,
                // and fades out rather than blinking at a cell wall. And they are gated by the
                // SHEET: grains stream inside the gusts and the air between them is clear, which
                // is most of what makes the wind read as gusting rather than as constant.
                float grains = 0.0;
                if (_Grains > 0.001)
                {
                    // LOW DOWN, and not many. Sand saltates in the first foot of air, so the
                    // upper sheets carry almost none - and the first pass of this put grains on
                    // all five sheets at once, which stacked into a downpour. The dune wind runs
                    // nearly away from the camera, so a dash elongated along it projects as a
                    // vertical streak: the elongation has to stay SMALL or blowing sand comes out
                    // looking like rain, which is exactly what it came out looking like.
                    float low = saturate(1.0 - layer * 1.35);
                    if (low > 0.01)
                    {
                        float2 wu = float2(dot(w, wind), dot(w, float2(-wind.y, wind.x)));

                        [unroll] for (int gi = 0; gi < 2; gi++)
                        {
                            float k = (float)gi;
                            float gs = _Speed * (2.6 + k * 1.9) * (0.6 + gustNow * 0.6);
                            float2 q = float2((wu.x - t * gs) * (2.6 + k * 1.4),
                                              wu.y * (5.0 + k * 2.6)) + seed + k * 21.3;

                            float2 cell = floor(q);
                            float2 f = frac(q);
                            float3 h = SrdHash23(cell + k * 3.7);

                            float lit = step(0.88 - _Grains * 0.09, h.z);   // the air is mostly empty
                            float life = frac(h.x * 7.1 + t * (0.5 + h.y * 0.8));
                            float fade = sin(life * 3.14159);

                            float2 at = float2(frac(h.x + life * 0.55), h.y);
                            float2 d = (f - at) * float2(0.62, 1.0);       // barely elongated
                            grains += lit * fade * smoothstep(0.13, 0.0, length(d));
                        }

                        // ...and only inside the gusts. Grains everywhere at once is a texture.
                        grains *= _Grains * low * (0.20 + 1.30 * saturate(sheet * 1.6));
                    }
                }

                // Over the BOARD the sheets thin out but the specks do not.
                //
                // This is the one place the scenery has to give way: a sheet of blowing sand
                // across a card is a card you cannot read, and no amount of atmosphere is worth
                // that. Specks crossing it are the opposite - they put the board inside the
                // weather at no cost to legibility, which is the whole effect anyway.
                float2 overBoard = saturate((abs(w) - _BoardHalf.xy) / 2.2 + 1.0);
                float clear = 1.0 - _BoardClear * (1.0 - saturate(max(overBoard.x, overBoard.y)));

                // Grains fade out with distance as well as near the camera. A horizontal sheet
                // seen at a grazing angle packs its far half into a few rows of pixels, and
                // without this the top of the frame silts up with everything the sheet holds.
                float far = saturate((26.0 - length(_WorldSpaceCameraPos.xz - w)) / 11.0);
                float alpha = saturate(veil * 0.17 * clear + grains * 0.45 * far)
                            * _Amount * gustNow;

                // Never right on top of the camera - a sheet crossing the near plane is a smear.
                float dist = length(_WorldSpaceCameraPos - i.positionWS);
                alpha *= saturate((dist - _NearFade) / 3.0);

                // Airborne material is lit from the side and glows where it is between you and
                // the sun, which is exactly when blowing sand is most visible.
                float3 V = normalize(i.positionWS - _WorldSpaceCameraPos);
                float toward = saturate(dot(V, normalize(_SunDir.xyz)) * 0.5 + 0.5);
                float3 col = _VeilColor.rgb * (0.72 + 0.55 * toward)
                           + _GrainColor.rgb * _SunColor.rgb * grains * 0.55;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
