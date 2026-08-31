Shader "SpawnRowDuel/Terrain"
{
    // The ground the board stands on.
    //
    // It used to be one flat quad with everything painted in the fragment. It is real geometry
    // now (TerrainMesh), because at the tilted angle the thing that reads as "landscape" is a
    // crest standing in front of the hollow behind it, and painting cannot do occlusion.
    //
    // Three layers, in the order they matter:
    //
    //   SHAPE     comes in on the vertices. Normals too, and the baked lighting - vertex colour
    //             carries sun exposure in R and sky openness in G, marched on the CPU when the
    //             biome was built. Long dune shadows for free, every frame, forever.
    //   SURFACE   wind streaks combed along the slope and a fine bump, both in wind space. This
    //             is the reference photograph's signature and most of what says the wind has
    //             been over this.
    //   AIR       distance haze. Without it the far dunes are the same colour as the near ones,
    //             which is exactly what makes a big landscape look like a small flat one.
    //
    // Biome is not a texture swap: it is the amounts below. Water is swell at 1 and embers at 0
    // rather than a different shader, and the next biome anybody wants is a row in a C# table.
    //
    // The sun here is the BIOME'S sun, not the scene's. The scene light sits at 48 degrees to
    // keep card faces legible; a dune field lit from 48 degrees is a beige carpet. The whole read
    // of this surface is a sun near the horizon, so it carries its own.
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

        _SunDir      ("Sun direction (world)", Vector) = (0.2, 0.25, 0.9, 0)
        _SunColor    ("Sun colour", Color) = (1, 0.93, 0.82, 1)
        _SkyColor    ("Sky ambient", Color) = (0.60, 0.71, 0.85, 1)
        _BounceColor ("Ground bounce", Color) = (0.29, 0.25, 0.20, 1)
        _SunIntensity("Sun intensity", Float) = 0.95
        _Ambient     ("Ambient intensity", Float) = 0.34
        _Sheen       ("Sheen", Range(0,2)) = 0.2
        _SheenPower  ("Sheen tightness", Float) = 20
        _ShadowDepth ("Baked shadow depth", Range(0,1)) = 0.45

        _WindDir     ("Wind direction (xz)", Vector) = (1, 0, 0, 0)
        _StreakAmount("Wind streaks", Range(0,2)) = 0.3
        _StreakScale ("Streak scale", Float) = 3
        _DetailBump  ("Detail bump", Range(0,3)) = 0.6
        _CrestLight  ("Crest light", Range(0,1)) = 0.2
        _TroughShade ("Trough shade", Range(0,1)) = 0.25
        _Sparkle     ("Sparkle", Range(0,2)) = 0

        _HazeColor   ("Haze", Color) = (0.78, 0.84, 0.88, 1)
        _HazeStart   ("Haze start", Float) = 10
        _HazeDensity ("Haze density", Range(0,1)) = 0.5

        _IslandExtent("Island half-size (xz)", Vector) = (10, 8, 0, 0)
        _FadeWidth   ("Edge fade", Float) = 2.5

        _CloudScale  ("Cloud size", Float) = 6.5
        _CloudSpeed  ("Cloud speed", Float) = 0.55
        _CloudShadowMin("Cloud shadow floor", Range(0,1)) = 0.66
        _CloudDir    ("Cloud direction", Vector) = (1, 0.35, 0, 0)
        _CloudAmount ("Cloud amount", Range(0,1)) = 1

        // What the board has pressed into the ground. R the hollow, G the rim shoved out of it.
        _DispTex     ("Displacement", 2D) = "black" {}
        _DispOrigin  ("Displacement origin", Vector) = (-18, -14, 0, 0)
        _DispSize    ("Displacement size", Vector) = (36, 28, 0, 0)
        _PressDepth  ("Press depth", Float) = 0.085
        _BermHeight  ("Berm height", Float) = 0.055
    }

    SubShader
    {
        // OPAQUE, and that is a change with a reason: the ground has relief now, so it has to
        // write depth or a far crest draws over the near one that should hide it. It also means
        // the tiles (Transparent-70) and the blades (Transparent-50) depth-test against the real
        // surface instead of against a flat plane they only happened to agree with.
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        Cull Back

        Pass
        {
            Name "TerrainLit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SRD_Noise.hlsl"

            TEXTURE2D(_DispTex);
            SAMPLER(sampler_DispTex);

            // Where the board has been pressed into the ground, as (hollow, rim).
            float2 SrdPress(float2 world, float4 origin, float4 size)
            {
                float2 uv = (world - origin.xy) / max(size.xy, 0.0001);
                if (any(uv < 0.0) || any(uv > 1.0)) return float2(0, 0);
                float4 t = SAMPLE_TEXTURE2D_LOD(_DispTex, sampler_DispTex, uv, 0);
                return t.rg;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 baked      : TEXCOORD2;   // r sun exposure, g sky openness, b height rise
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _Tint2, _Tint3, _Highlight;
                float _PatchScale, _Patch2Cut, _Patch3Cut, _Grain;
                float _WaveAmount, _RippleAmount, _EmberAmount, _MotionSpeed;
                float4 _SunDir, _SunColor, _SkyColor, _BounceColor;
                float _SunIntensity, _Ambient, _Sheen, _SheenPower, _ShadowDepth;
                float4 _DispOrigin, _DispSize;
                float _PressDepth, _BermHeight;
                float4 _WindDir;
                float _StreakAmount, _StreakScale, _DetailBump, _CrestLight, _TroughShade, _Sparkle;
                float4 _HazeColor;
                float _HazeStart, _HazeDensity;
                float4 _IslandExtent;
                float _FadeWidth;
                float _CloudScale, _CloudSpeed, _CloudShadowMin;
                float4 _CloudDir;
                float _CloudAmount;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);

                // The ground gives way under what is standing on it. Real displacement rather
                // than a painted shadow: the hollow and the rim of material shoved out of it
                // both move VERTICES, so a card sits in a dent with a silhouette, and the light
                // finds the rim the same way it finds a dune.
                float2 press = SrdPress(o.positionWS.xz, _DispOrigin, _DispSize);
                o.positionWS.y += press.g * _BermHeight - press.r * _PressDepth;

                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.baked = v.color;
                return o;
            }

            // Wind space: x along the wind, y across it. Streaks are stretched hard in x, which
            // is what makes them streaks and not blotches.
            float2 ToWind(float2 w, float2 dir)
            {
                return float2(dot(w, dir), dot(w, float2(-dir.y, dir.x)));
            }

            // Gradient of a value-noise field, by difference. Used to bend the normal without
            // needing a tangent frame or a texture.
            float2 NoiseGrad(float2 p, float e)
            {
                float c = SrdValueNoise(p);
                return float2(SrdValueNoise(p + float2(e, 0)) - c,
                              SrdValueNoise(p + float2(0, e)) - c) / e;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 w = i.positionWS.xz;
                float t = _Time.y;
                float2 wind = normalize(_WindDir.xz + float2(0.0001, 0));
                float2 wp = ToWind(w, wind);

                float3 N = normalize(i.normalWS);
                float sunExposure = i.baked.r;
                float skyOpen = i.baked.g;
                float rise = i.baked.b;

                // ── albedo: patches, then the wind's comb over the top ──────────────────────
                float n2 = SrdValueNoise(w * _PatchScale);
                float n3 = SrdValueNoise(w * _PatchScale * 0.43 + 31.7);

                float3 albedo = _BaseColor.rgb;
                albedo = lerp(albedo, _Tint2.rgb, smoothstep(_Patch2Cut - 0.06, _Patch2Cut + 0.06, n2));
                albedo = lerp(albedo, _Tint3.rgb, smoothstep(_Patch3Cut - 0.06, _Patch3Cut + 0.06, n3));

                // Streaks: noise squashed ~14:1 along the wind. Strongest on SLOPES - wind combs
                // a face it is blowing across and leaves the flats alone, and keying them to
                // slope is what stops the whole field looking like brushed metal.
                float slope = saturate(1.0 - N.y);
                float2 sp = float2(wp.x * 0.09, wp.y * 1.35) * _StreakScale;
                float streak = SrdFbm(sp) - 0.5;
                albedo *= 1.0 + streak * _StreakAmount * (0.35 + slope * 2.2);

                // Crests catch the light and hollows collect shade, on top of whatever the sun is
                // doing. It is the cheapest way to keep relief readable when the sun is behind you.
                albedo *= 1.0 + (rise - 0.5) * 2.0 * _CrestLight;
                albedo *= lerp(1.0 - _TroughShade, 1.0, saturate(skyOpen));

                albedo *= 1.0 + (SrdHash(floor(w * 26.0)) - 0.5) * _Grain;

                // ── detail normal ──────────────────────────────────────────────────────────
                // Fine ripples running across the wind, plus the streaks' own relief. Bending the
                // normal rather than darkening the albedo is what lets them catch the sun and
                // turn over as the light moves.
                float2 rp = float2(wp.x * 0.55, wp.y * 5.5);
                float2 g = NoiseGrad(rp, 0.35) * 0.55 + NoiseGrad(sp * 2.0, 0.5) * 0.35;
                float2 bend = float2(dot(g, float2(wind.x, -wind.y)), dot(g, float2(wind.y, wind.x)));
                N = normalize(N + float3(bend.x, 0, bend.y) * _DetailBump * 0.10);

                // ── what the board pressed into it ─────────────────────────────────────────
                // The vertices already moved; this is the SHADING of that dent. Sampling the
                // neighbourhood gives the slope of the hollow, which is what makes its rim catch
                // the sun - a displaced surface with an undisturbed normal reads as a decal.
                {
                    float2 texel = _DispSize.xy / 320.0;
                    float2 pc = SrdPress(w, _DispOrigin, _DispSize);
                    float2 px = SrdPress(w + float2(texel.x, 0), _DispOrigin, _DispSize);
                    float2 pz = SrdPress(w + float2(0, texel.y), _DispOrigin, _DispSize);

                    float hc = pc.g * _BermHeight - pc.r * _PressDepth;
                    float hx = px.g * _BermHeight - px.r * _PressDepth;
                    float hz = pz.g * _BermHeight - pz.r * _PressDepth;

                    N = normalize(N + float3(-(hx - hc) / texel.x, 0, -(hz - hc) / texel.y) * 2.2);

                    // Disturbed material is a different surface: packed and darker in the hollow,
                    // loose and pale on the rim where it has just been turned over.
                    albedo *= 1.0 - pc.r * 0.16;
                    albedo = lerp(albedo, albedo * 1.13 + _Highlight.rgb * 0.06, saturate(pc.g));
                }

                // ── water: the surface that actually moves ──────────────────────────────────
                if (_WaveAmount > 0.001)
                {
                    float t2 = t * _MotionSpeed;
                    float2 d1 = normalize(float2(0.86, 0.51));
                    float2 d2 = normalize(float2(-0.42, 0.91));
                    float s1 = sin(dot(w, d1 * 2.9) + t2 * 2.4);
                    float s2 = sin(dot(w, d2 * 4.3) + t2 * 3.1);
                    float swell = SrdFbm(w * 0.75 + float2(t2 * 0.16, t2 * 0.11)) * 2.0 - 1.0;

                    float2 wob = (d1 * cos(dot(w, d1 * 2.9) + t2 * 2.4) * 0.055
                               +  d2 * cos(dot(w, d2 * 4.3) + t2 * 3.1) * 0.035) * _WaveAmount;
                    N = normalize(N + float3(wob.x, 0, wob.y) * 3.0);

                    float surf = s1 * 0.5 + s2 * 0.32 + swell * 0.5;
                    albedo *= 1.0 + surf * 0.09 * _WaveAmount;

                    // Foam sits on the brink of a crest, not across the whole of it.
                    float foam = smoothstep(0.86, 1.05, surf);
                    albedo = lerp(albedo, _Highlight.rgb, foam * _WaveAmount * 0.55);
                }

                // ── sand: a slow travelling corduroy over the top of the streaks ────────────
                if (_RippleAmount > 0.001)
                {
                    float2 ripple = float2(wp.x, wp.y * 2.2) * 2.4 + float2(0, t * _MotionSpeed * 0.30);
                    float ridge = sin(ripple.y + SrdFbm(w * 0.5) * 6.2831);
                    albedo *= 1.0 + ridge * 0.055 * _RippleAmount;

                    // Loose sand streaming off the brinks, which is the thing the eye reads as wind.
                    float drift = smoothstep(0.72, 1.0,
                        SrdFbm(float2(wp.x * 0.12, wp.y * 1.1) * 2.0 - float2(t * _MotionSpeed * 0.9, 0)));
                    albedo = lerp(albedo, _Highlight.rgb, drift * _RippleAmount * 0.22 * saturate(rise * 1.4));
                }

                // ── ash: embers, and they belong in the hollows ─────────────────────────────
                // A POINT inside its cell, not the cell. Lighting the whole cell tiles the ground
                // with orange squares, which is what the first pass did and it looked like a bug.
                if (_EmberAmount > 0.001)
                {
                    float2 eg = w * 2.4;
                    float2 cell = floor(eg);
                    float2 f = frac(eg);

                    float seed = SrdHash(cell);
                    float2 spot = float2(SrdHash(cell + 1.7), SrdHash(cell + 4.3));
                    float lit = step(0.88, SrdHash(cell + 5.5));
                    float pulse = 0.55 + 0.45 * sin(t * (1.1 + seed * 2.2) + seed * 6.2831);

                    // Fire settles low: weight the glow toward the troughs.
                    float low = saturate(1.0 - rise * 1.5);
                    float glow = smoothstep(0.17, 0.02, length(f - spot));
                    albedo = lerp(albedo, _Highlight.rgb, glow * lit * pulse * _EmberAmount * (0.25 + low));
                }

                // ── light ───────────────────────────────────────────────────────────────────
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                // Wrapped diffuse. A hard terminator on a sand dune reads as plastic; sand and
                // snow both carry light a good way past 90 degrees.
                float ndl = dot(N, L);
                float diffuse = saturate((ndl + 0.35) / 1.35);

                float shadow = lerp(1.0 - _ShadowDepth, 1.0, sunExposure);
                float3 lit = _SunColor.rgb * diffuse * shadow * _SunIntensity;

                // Hemisphere ambient, gated by how much sky the point can see. Sun and ambient
                // are budgeted to sum to about one at full light - the first pass let them both
                // run at full strength and every biome came out bleached.
                float3 ambient = lerp(_BounceColor.rgb, _SkyColor.rgb, saturate(N.y * 0.5 + 0.5))
                                 * lerp(0.35, 1.0, skyOpen) * _Ambient;

                float3 col = albedo * (lit + ambient);

                // Grazing sheen. At a low sun this is the bright rim along every windward face,
                // and it is a large part of why the reference reads as sand rather than as paper.
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _SheenPower) * _Sheen * shadow;
                col += _SunColor.rgb * spec;

                // Point glints: snow crystals, sun on water. Only where the light is already
                // strong, or the whole surface fizzes.
                if (_Sparkle > 0.001)
                {
                    float2 sg = w * 42.0;
                    float spark = step(0.9965, SrdHash(floor(sg) + floor(t * 3.0) * 17.0));
                    col += _SunColor.rgb * spark * _Sparkle * saturate(diffuse * 1.4) * shadow * 1.6;
                }

                // ── cloud shadow ────────────────────────────────────────────────────────────
                // Normally OFF here and left to SRD_CloudShadow, which casts the same field over
                // the whole scene in one pass. Doing it in both places costs the cloud field twice
                // a pixel AND darkens the ground twice as hard as the pieces standing on it.
                if (_CloudAmount > 0.001)
                {
                    float cloud = SrdCloudLight(w, t, normalize(_CloudDir.xy),
                                                _CloudScale, _CloudSpeed, _CloudShadowMin);
                    col *= lerp(1.0, cloud, _CloudAmount);
                }

                // ── distance ────────────────────────────────────────────────────────────────
                // The one term that turns a big flat field into a landscape. There is no horizon
                // in frame at this camera angle, so haze is doing the whole job of depth.
                float dist = length(_WorldSpaceCameraPos.xz - w);
                float haze = 1.0 - exp(-max(0.0, dist - _HazeStart) * _HazeDensity * 0.055);
                col = lerp(col, _HazeColor.rgb, saturate(haze));

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
