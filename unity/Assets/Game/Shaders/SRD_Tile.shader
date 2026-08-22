Shader "SpawnRowDuel/Tile"
{
    // A board cell as a MARKING on the ground, not a slab standing on it.
    //
    // The board used to be 0.12-thick cubes: solid, opaque, and the reason the field read at the
    // wrong scale - a creature standing on a raised plinth next to knee-high grass is a figurine on
    // a table, not an army in a meadow. A cell is a translucent wash with a brighter rim now, the
    // terrain shows through it, and the grass grows up past it.
    //
    // The rim is what keeps the grid legible once the fill is see-through: an edge reads as a
    // boundary at any opacity, where a flat tint at 30% just reads as a stain.
    Properties
    {
        _BaseColor ("Fill", Color) = (0.30, 0.13, 0.10, 0.34)
        _EdgeColor ("Edge", Color) = (1, 1, 1, 0.55)
        _EdgeWidth ("Edge width", Range(0, 0.5)) = 0.055
        _Fresnel   ("Grazing lift", Range(0, 1)) = 0.25
    }

    SubShader
    {
        // AFTER the terrain, BEFORE the grass: the markings lie on the ground and the grass grows
        // over them, which is the right way round for a field somebody has drawn a board onto.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-70" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "TileOverlay"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _EdgeColor;
                float _EdgeWidth, _Fresnel;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // distance to the nearest edge, in UV
                float2 d = min(i.uv, 1.0 - i.uv);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, min(d.x, d.y));

                float3 col = lerp(_BaseColor.rgb, _EdgeColor.rgb, edge);
                float a = lerp(_BaseColor.a, _EdgeColor.a, edge);

                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
