Shader "SpawnRowDuel/Settle"
{
    // What has ALREADY landed: ash lying on the tiles, snow lying on the cards.
    //
    // Falling weather that never accumulates is a screensaver playing in front of the board. The
    // thing that puts the board out in the weather is the board getting COVERED - a set card that
    // is half grey by turn six, a lane you can only half read until something moves through it and
    // sweeps it clear.
    //
    // The layer is a sheet that follows the ground (built in TerrainField, so it lies on the
    // plateau over the board and on the dunes past it) drawn AFTER the cards and standees, and its
    // coverage comes from one channel of a texture the CPU grows over time and wipes wherever a
    // piece arrives or leaves. So "until they move" is not a special case in here: a cell whose
    // occupant changed had its patch of the texture set back to zero, and the ash starts again.
    //
    // Growth is a THRESHOLD against noise rather than a fade of alpha. Ash does not arrive as a
    // uniform grey wash getting stronger; it arrives in patches that spread and join. Thresholding
    // a fixed noise field with a rising level is that, exactly, and costs one fbm.
    Properties
    {
        _SettleTex   ("Coverage", 2D) = "black" {}
        _SettleOrigin("Coverage origin (xz)", Vector) = (-18, -14, 0, 0)
        _SettleSize  ("Coverage size (xz)", Vector) = (36, 28, 0, 0)

        _SettleColor ("Colour", Color) = (0.82, 0.79, 0.76, 1)
        _ShadeColor  ("Shade", Color) = (0.55, 0.53, 0.54, 1)
        _Amount      ("Amount", Range(0,1)) = 0
        _Grain       ("Patch scale", Float) = 1.7
        _Sparkle     ("Sparkle", Range(0,2)) = 0

        // ash gathers in the seams between tiles before it covers their faces
        _CellPitch   ("Cell pitch (xz)", Vector) = (1.08, 1.566, 0, 0)
        _CellHalf    ("Cell half (xz)", Vector) = (0.5, 0.725, 0, 0)
        _Groove      ("Seam gathering", Range(0,1)) = 0.6
        _BoardHalf   ("Board half (xz)", Vector) = (3.8, 3.9, 0, 0)

        _Extent      ("Sheet half-size (xz)", Vector) = (9, 9, 0, 0)
        _Fade        ("Rim fade", Float) = 2.5
    }

    SubShader
    {
        // Transparent+10: past Sprites/Default at 3000, which is where the card plates, the ground
        // shadows and the standee figures all live, and short of the veil (+20) and the falling
        // flakes (+30). Depth-tested, so a standing figure occludes the ash rather than wearing it.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "Settle"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            TEXTURE2D(_SettleTex);
            SAMPLER(sampler_SettleTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SettleOrigin, _SettleSize;
                float4 _SettleColor, _ShadeColor;
                float _Amount, _Grain, _Sparkle;
                float4 _CellPitch, _CellHalf, _BoardHalf, _Extent;
                float _Groove, _Fade;
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
                if (_Amount < 0.001) discard;

                float2 w = i.positionWS.xz;
                float2 uv = (w - _SettleOrigin.xy) / max(_SettleSize.xy, 0.0001);
                if (any(uv < 0.0) || any(uv > 1.0)) discard;

                float2 field = SAMPLE_TEXTURE2D(_SettleTex, sampler_SettleTex, uv).rg;
                float level = field.r;

                // G is the strip each standing figure hides. The sheet draws after the cards so
                // it can lie ON them, and the standees are sprites that write no depth - so
                // without this the ash would be painted across every figure's knees.
                float hidden = field.g;
                if (level < 0.004 || hidden > 0.985) discard;

                // ── where it lies ──────────────────────────────────────────────────────────
                // One coarse field decides which patches fill first and a fine one roughens their
                // edges. Both are fixed in world space, so a patch GROWS rather than crawling -
                // ash sliding across the ground as it thickens would be the giveaway.
                float coarse = SrdFbm(w * _Grain);
                float fine = SrdValueNoise(w * _Grain * 6.5);
                float thresh = coarse * 0.72 + fine * 0.28;

                float cover = smoothstep(thresh - 0.16, thresh + 0.07, level);

                // ── the seams ──────────────────────────────────────────────────────────────
                // Drift gathers against anything standing proud, and on this board the only edges
                // are the tile rims. Filling the grid lines first is what makes a covered board
                // read as a covered board and not as a dirty texture.
                float2 q = abs(frac(w / _CellPitch.xy + 0.5) - 0.5) * _CellPitch.xy;
                float2 toEdge = _CellHalf.xy - q;
                float edge = min(toEdge.x, toEdge.y);
                float onBoard = 1.0 - saturate((max(abs(w.x) - _BoardHalf.x,
                                                    abs(w.y) - _BoardHalf.y)) / 0.6);
                float groove = smoothstep(0.10, -0.02, edge) * onBoard * _Groove;
                cover = saturate(cover + groove * smoothstep(0.03, 0.40, level) * (1.0 - cover));

                // ── how it looks ───────────────────────────────────────────────────────────
                // Thin cover is the colour of dirty shade; thick cover is the colour of the stuff
                // itself. Interpolating between the two rather than fading one alpha is what keeps
                // a light dusting from looking like a translucent white sheet.
                float3 col = lerp(_ShadeColor.rgb, _SettleColor.rgb, saturate(cover * 1.25));
                col *= 0.92 + fine * 0.16;

                if (_Sparkle > 0.001)
                {
                    float spark = step(0.9975, SrdHash(floor(w * 60.0)));
                    col += spark * _Sparkle * cover * 0.6;
                }

                // never a hard rectangle at the sheet's rim
                float2 rim = abs(w) - (_Extent.xy - _Fade);
                float fade = 1.0 - saturate(max(rim.x, rim.y) / max(_Fade, 0.0001));

                // ...and it THINS away from the board.
                //
                // Ash lands everywhere, and a field that is uniformly buried in it is a field
                // that has lost the thing the biome is for: scorched ground is DARK, and letting
                // the cover run to the horizon turns the whole picture into pale concrete. What
                // has to read as covered is the part being played on, so the far ground keeps
                // about a third of the coverage and its own colour with it.
                float2 out2 = saturate((abs(w) - _BoardHalf.xy) / 2.5);
                fade *= lerp(1.0, 0.14, saturate(max(out2.x, out2.y)));

                float alpha = cover * _Amount * fade * (1.0 - hidden);
                clip(alpha - 0.004);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
