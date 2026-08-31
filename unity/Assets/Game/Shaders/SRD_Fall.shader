Shader "SpawnRowDuel/Fall"
{
    // What comes DOWN out of the sky: ash, and snow falling faster than ash.
    //
    // This used to be a screen-covering pass - layers of discs scrolling down the frame at an
    // angle. It was wrong in the way that mattered: nothing ever ARRIVED. Every flake crossed the
    // screen on the same diagonal at the same speed and left the bottom of the frame still
    // falling, so the board was a picture with weather printed over it rather than a board that
    // ash was landing on. It read as blocky because a screen-space disc is the same size whether
    // it is over the far wall or the near hand, which is not what perspective does to a flake.
    //
    // So the flakes live in the WORLD now. Each one is a quad with a fixed landing point on the
    // ground, and it spends its cycle falling to that point: high and drifting at the start, wobbling
    // down its own path, and in the last few percent it lies flat on the surface, fades, and hands
    // its coverage to the settle layer (SRD_Settle) which is what actually accumulates.
    //
    // Consequences worth having, all of them free:
    //   - PERSPECTIVE. A flake by the camera is big, one over the far wall is a speck.
    //   - DEPTH. ZTest is on, so a flake passes behind a standee and in front of the tile past it.
    //   - RANDOM. Every flake has its own rate, wobble and phase, so there is no shared diagonal.
    Properties
    {
        _FallColor ("Colour", Color) = (1, 1, 1, 1)
        _Amount    ("Amount", Range(0,3)) = 0
        _Speed     ("Fall rate (cycles/sec)", Float) = 0.14
        _Drift     ("Sideways wander", Float) = 0.6
        _Size      ("Flake size (world)", Float) = 0.06
        _Swirl     ("Wobbles per fall", Float) = 1.4
        _Height    ("Spawn height", Float) = 7
        _WindDir   ("Wind (xz)", Vector) = (1, 0, 0, 0)
        _Settle    ("Settle fraction", Range(0.01,0.3)) = 0.07
    }

    SubShader
    {
        // Over the cards but INSIDE the world - depth-tested, so the board occludes what falls
        // behind it. That is the half of "falling weather in front of the board" the screen-space
        // version could never have.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
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

            struct Attributes
            {
                float4 positionOS : POSITION;   // the LANDING point, one value for all four corners
                float2 corner     : TEXCOORD0;  // -0.5..0.5 both ways
                float2 seeds      : TEXCOORD1;  // x phase, y rate
                float4 color      : COLOR;      // r size, g wander bearing, b squash, a brightness
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner     : TEXCOORD0;
                float2 fade       : TEXCOORD1;  // x alpha, y brightness
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FallColor, _WindDir;
                float _Amount, _Speed, _Drift, _Size, _Swirl, _Height, _Settle;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 land = TransformObjectToWorld(v.positionOS.xyz);

                // Its own clock. Rate varies by nearly two to one across the field, so flakes
                // overtake each other on the way down - a curtain where everything falls at one
                // speed is a curtain, and reads as one.
                float phase = v.seeds.x;
                float rate = max(_Speed, 0.001) * (0.55 + v.seeds.y * 0.95);
                float cyc = frac(_Time.y * rate + phase);

                float h = _Height * (1.0 - cyc);

                // The wander CONVERGES on the landing point. Backwards from the physics - a real
                // flake wanders away from where it started - and identical on screen, with the one
                // property the physics cannot give: the flake arrives where the ash is accumulating.
                float bearing = v.color.g * 6.2831;
                float remain = 1.0 - cyc;
                float2 wobble = float2(sin(cyc * _Swirl * 6.2831 + bearing),
                                       cos(cyc * _Swirl * 5.13 + bearing * 1.7));
                float2 wind = normalize(_WindDir.xz + float2(0.0001, 0));
                float2 off = (wobble * 0.6 + wind * 1.1) * _Drift * remain;

                float3 pos = land + float3(off.x, h, off.y);

                // ── landing ────────────────────────────────────────────────────────────────
                // The last few percent of the cycle: the flake lies down flat on the surface and
                // fades. That hand-off is what makes ash look like it is settling rather than
                // vanishing at the floor - the settle layer picks the coverage up from here.
                float settle = saturate((cyc - (1.0 - _Settle)) / max(_Settle, 0.0001));

                float3 camRight = normalize(UNITY_MATRIX_V._m00_m01_m02);
                float3 camUp    = normalize(UNITY_MATRIX_V._m10_m11_m12);
                float3 right = lerp(camRight, float3(1, 0, 0), settle);
                float3 up    = lerp(camUp,    float3(0, 0, 1), settle);

                float size = _Size * (0.55 + v.color.r * 1.0) * (1.0 + settle * 0.35);
                float squash = 0.72 + v.color.b * 0.56;

                pos += right * (v.corner.x * size)
                     + up    * (v.corner.y * size * squash);

                o.positionCS = TransformWorldToHClip(pos);
                o.corner = v.corner;

                // in at the top of the fall, out as it settles - never a pop at either end
                o.fade = float2(saturate(cyc * 14.0) * (1.0 - settle * settle),
                                0.78 + v.color.a * 0.42);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Amount < 0.001) discard;

                float d = length(i.corner) * 2.0;
                float a = smoothstep(1.0, 0.18, d);        // soft edge, firm middle
                a *= i.fade.x * _Amount;
                clip(a - 0.004);

                return half4(_FallColor.rgb * i.fade.y, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
