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

        // the open-sea swell: which way the train marches, how hard it bends the light, and how
        // much white it breaks off at the brink
        _SwellDir    ("Swell bearing (xz)", Vector) = (0.82, 0, 0.57, 0)
        _SwellHeight ("Swell relief", Float) = 0.10
        _SwellFoam   ("Whitecaps", Range(0,1)) = 0.5

        // the tide: a waterline that comes IN and goes OUT, with the wave train that drives it
        _TideAmount  ("Tide", Range(0,1)) = 0
        _TideDir     ("Seaward direction (xz)", Vector) = (0, 0, 1, 0)
        _TideLevel   ("Waterline, mean", Float) = 9
        _TideRange   ("Waterline swing", Float) = 3.4
        _TidePeriod  ("Tide period (s)", Float) = 26
        _TideFreeze  ("Frozen tide (-1 = live)", Float) = -1
        _WaveFreq    ("Wave lines per unit", Float) = 0.55
        _WaveSpeed   ("Wave line speed", Float) = 2.1
        _WaterColor  ("Water", Color) = (0.20, 0.42, 0.48, 1)
        _DeepColor   ("Deep water", Color) = (0.10, 0.24, 0.34, 1)
        _FoamColor   ("Foam", Color) = (0.94, 0.98, 0.99, 1)
        _BoardHalf   ("Board half-size (xz)", Vector) = (3.8, 3.9, 0, 0)
        _BoardWet    ("Water tint over the board", Range(0,1)) = 0.45
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
        _GustSwing   ("Gust swing", Range(0,1.5)) = 0.45
        _GustPeriod  ("Gust period (s)", Float) = 15

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

        // What the board has pressed into the ground. R is how hard - a card crushes the square it
        // lies on, and the value fills back in a PLY at a time once it leaves, at a rate the biome
        // sets, which is what the grass reads to know how flat to lie. G is the same number used as
        // a FLAG, and it is what switches on the impression the fragment draws from the pitch. A is
        // the same flag on a much faster clock: "something is standing here right now", which is
        // what the bright half of the impression is gated on so that a vacated square keeps its
        // hollow and loses its highlight.
        _DispTex     ("Displacement", 2D) = "black" {}
        _DispOrigin  ("Displacement origin", Vector) = (-18, -14, 0, 0)
        _DispSize    ("Displacement size", Vector) = (36, 28, 0, 0)

        // The card's own impression. Reach is in X units; Z is squashed to match, so the band is
        // the same fraction of the tile's margin - and the same width on screen - on both axes.
        _PressDepth  ("Dish depth", Float) = 0.075
        _CellPitch   ("Cell pitch (xz)", Vector) = (1.08, 1.566, 0, 0)
        _CellHalf    ("Card half-size (xz)", Vector) = (0.5, 0.725, 0, 0)
        _CardRound   ("Card corner radius", Float) = 0.06
        _RimReach    ("Rim width (x units)", Float) = 0.04
        _RimRelief   ("Rim relief", Float) = 0.11

        // ── the hollow, as a SHAPE rather than a stain ──────────────────────────────────────
        //
        // How far the ground steps down at the card's own outline, in X units. This is what makes
        // a vacated square read as a depression: a wall with a slope, lit by the same sun as the
        // field around it, bright on the near side and shaded on the far one. Everything the
        // impression used to be made of slumped with the crest, and what was left was a flat
        // multiply - a card-shaped stain that did not move when the light did.
        _HollowStep  ("Hollow wall step (x units)", Float) = 0.02
        // How much SKY the floor of the hollow loses. Spent on the ambient term ALONE, because a
        // dent is walled in, not shaded: the sun still reaches into it.
        _HollowShade ("Hollow sky occlusion", Range(0,1)) = 0.5
        // The hairline of contact shadow in the crack at a card's edge.
        _CrackShade  ("Contact crack", Range(0,1)) = 0.55
        // Packed, trodden ground under a card. Small, and it is meant to be: it is the blades
        // lying flat that carry this, and they stand back up on their own clock.
        _PressTint   ("Trodden tint", Range(0,1)) = 0.18
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

            // What the board has done to the ground here, as
            //   R how hard the grass is crushed, G how deep the hollow still is, B the dish,
            //   A whether something is standing on this square RIGHT NOW.
            //
            // G and A are the same flag on two clocks, and keeping them apart is the whole of
            // "the displaced terrain should stay displaced, but the highlight around the tile
            // should be removed". A hollow outlives the card that made it - it is filled back in a
            // ply at a time by whatever the field has blowing over it - while the pale crest of
            // turned earth and the sharp lip that catches the light do not: loose material slumps.
            // Read the HOLLOW out of G and every bright term out of A.
            float4 SrdPress(float2 world, float4 origin, float4 size)
            {
                float2 uv = (world - origin.xy) / max(size.xy, 0.0001);
                if (any(uv < 0.0) || any(uv > 1.0)) return float4(0, 0, 0, 0);
                return SAMPLE_TEXTURE2D_LOD(_DispTex, sampler_DispTex, uv, 0);
            }

            // The pile of material a card's edge shoves out: nothing at the card's own outline,
            // nothing again at the tile's margin, a rounded crest in between. Sin squared rather
            // than a triangle because loose material SLUMPS - a wall of it reads as a wall.
            float SrdRim(float d, float reach)
            {
                float s = sin(saturate(d / max(reach, 0.0001)) * 3.14159265);
                return s * s;
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
                float4 _SwellDir;
                float _SwellHeight, _SwellFoam;
                float _TideAmount, _TideLevel, _TideRange, _TidePeriod, _WaveFreq, _WaveSpeed;
                float _TideFreeze;
                float4 _BoardHalf;
                float _BoardWet;
                float4 _TideDir, _WaterColor, _DeepColor, _FoamColor;
                float4 _SunDir, _SunColor, _SkyColor, _BounceColor;
                float _SunIntensity, _Ambient, _Sheen, _SheenPower, _ShadowDepth;
                float4 _DispOrigin, _DispSize;
                float4 _CellPitch, _CellHalf;
                float _CardRound, _RimReach, _RimRelief, _PressDepth;
                float _HollowStep, _HollowShade, _CrackShade, _PressTint;
                float4 _WindDir;
                float _StreakAmount, _StreakScale, _DetailBump, _CrestLight, _TroughShade, _Sparkle;
                float _GustSwing, _GustPeriod;
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

                // The DISH, and only the dish. B is stamped broad and soft - half a unit across,
                // feathered over another half - so it spans four or five of the ground's 0.19-unit
                // vertices and the grid carries it without a stagger. The card's crisp EDGE is not
                // here and cannot be: at that spacing a 1.00-wide card is five vertices across and
                // its outline snapped 0.02 to 0.06 off the card, a different amount in every
                // column. The edge is shaded in the fragment, from the pitch, where it is exact.
                o.positionWS.y -= SrdPress(o.positionWS.xz, _DispOrigin, _DispSize).b * _PressDepth;


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

                // ── the impression a card leaves ───────────────────────────────────────────
                //
                // Drawn from the board's PITCH rather than from the press texture, and that is
                // the whole of this fix. The rim used to be stamped into _DispTex and displaced
                // as geometry, and at 0.19 units between ground vertices it could never take the
                // card's shape: it came out 1.30 x 1.70 around a 1.00 x 1.45 card, ROUNDER in
                // aspect than the card, corner-rounded to 0.37 against a card frame with square
                // corners, and standing 0.055 proud of a dent of 0.013 - four times the relief of
                // the thing it was supposed to belong to. A ring, not an impression.
                //
                // But a card only ever lies on a CELL, so its outline is arithmetic. Round the
                // world position to the nearest cell centre and the rounded-box distance to the
                // card is exact at PIXEL resolution, which is the only place a rim this fine can
                // be drawn at all.
                //
                // ── trodden ground, and hard enough to see ─────────────────────────────────
                // A card crushes the grass on its square and the bare earth under it is packed
                // and darker. At 0.16 that read as a faint tint and the honest answer to "is the
                // ground displaced at all?" was no.
                float4 pressW = SrdPress(w, _DispOrigin, _DispSize);
                // TRODDEN, and no more than trodden. This was 0.46 - a 46% multiply over a
                // card-shaped footprint - and stacked with the 0.30 the block below used to spend
                // it put a vacated square at 38% of the luminance of the field beside it. That is
                // not a dent, it is a stain, and it is what "the displaced terrain seems to have a
                // dark filter where the card was" was looking at. Both are now shading terms
                // instead, so all this has to carry is the packed earth itself. R is the blades'
                // own level and springs back on its own clock, so this lifts with them.
                albedo *= 1.0 - pressW.r * _PressTint;

                // ...and the DISH gets a normal. The vertices moved for it, but a moved vertex on
                // a 0.19-unit grid barely tilts the face it belongs to - sampling B either side
                // and bending the normal by the difference is what makes the dip catch the light.
                {
                    float2 e2 = _DispSize.xy / float2(320.0, 240.0);
                    float bx = SrdPress(w + float2(e2.x, 0), _DispOrigin, _DispSize).b;
                    float bz = SrdPress(w + float2(0, e2.y), _DispOrigin, _DispSize).b;
                    N = normalize(N + float3((bx - pressW.b) / e2.x, 0, (bz - pressW.b) / e2.y)
                                      * _PressDepth * 1.6);
                }
                // How much sky the ground is walled off from here. Gathered in the impression
                // block and spent at the LIGHT, on the ambient term alone - see the note where it
                // is set. Zero everywhere the board has not been.
                float pressAo = 0.0;
                {
                    float2 cell = round(w / _CellPitch.xy) * _CellPitch.xy;
                    float4 flag = SrdPress(cell, _DispOrigin, _DispSize);
                    float here  = flag.g;     // the hollow: outlives the card, fills by the ply
                    float crest = flag.a;     // the pale lip: gone within half a second of it
                    if (here > 0.01)
                    {
                        // Z into X units first. Depth is foreshortened by sin(42 deg) = 0.669 at
                        // this camera, so a band the same width in world units comes out two
                        // thirds as wide along the rows as across them, and the impression reads
                        // as an oval on a rectangular card. The tile's own margins are 0.040 and
                        // 0.058, whose ratio is 0.69 - within a hair of the foreshortening - so
                        // scaling by them lands the rim on the same pixel count either way.
                        //
                        // The reach itself is no longer held inside those margins. It was, and
                        // nine pixels of rim is a rim nobody can see; two neighbouring cards
                        // sharing a churned border is what would actually happen anyway.
                        float2 gap    = max(_CellPitch.xy * 0.5 - _CellHalf.xy, 0.0001);
                        float2 aspect = float2(1.0, gap.x / gap.y);
                        float2 rel    = (w - cell) * aspect;
                        float2 halfA  = _CellHalf.xy * aspect;

                        // The reach is CAPPED at the tile's own half-gap, and that is not a style
                        // choice - it is what stops a seam. Only the nearest cell is evaluated, so
                        // the rim is cut off dead at the halfway line between two squares; a reach
                        // wider than the gap is still at most of its height when it gets there,
                        // and the discontinuity draws a hard line down every tile boundary on the
                        // board. In the squashed space the two half-gaps are equal by
                        // construction (z was scaled by exactly gap.x/gap.y), so one cap does both
                        // axes. What makes the impression READ is the dish under it, the crushed
                        // grass on it and the contact shade in the crack - not width it cannot have.
                        float reach = min(_RimReach, gap.x);

                        // ── a hollow that is filling gets SMALLER, not just fainter ────────
                        //
                        // Every channel used to scale linearly with `here` and nothing else, so a
                        // print kept a pixel-sharp, full-size card outline at every depth and just
                        // dimmed - which the eye reads as a permanent hole somebody has turned the
                        // brightness down on, not as ground closing over. Material slumping into a
                        // dent takes the edges first: the corners round off, the walls fall inward,
                        // and what is left at the end is a soft dimple that stops being card-shaped
                        // before it stops being visible.
                        //
                        // Both terms stay INSIDE the cell - the footprint only ever shrinks and the
                        // corner radius is capped under the half-size - so nothing here can push
                        // the impression past the halfway line into a neighbour and draw a seam.
                        float fill    = 1.0 - here;
                        float2 halfF  = halfA * (1.0 - 0.22 * fill);
                        float roundF  = min(_CardRound + 0.36 * fill * min(halfA.x, halfA.y),
                                            min(halfF.x, halfF.y) * 0.92);

                        float d   = SrdRoundBox(rel, halfF, roundF);
                        float rim = SrdRim(d, reach);

                        // The slope, by difference. The steps are taken in world units and put
                        // through the same squash, so the gradient stays a world-space slope.
                        const float e = 0.004;
                        float rx = SrdRim(SrdRoundBox(rel + float2(e, 0) * aspect, halfF, roundF), reach);
                        float rz = SrdRim(SrdRoundBox(rel + float2(0, e) * aspect, halfF, roundF), reach);
                        // ON `crest`, NOT ON `here`, and this is the line that was reported.
                        //
                        // _RimRelief / e is a gain of FIFTY on a rim whose gradient peaks near 79
                        // per unit, so the normal comes out of normalize() very nearly horizontal:
                        // a bright wall two hundredths of a unit wide, and by some way the loudest
                        // thing about an impression. That is right under a card - the lip of turned
                        // material is what says the ground gave way - and wrong the moment the card
                        // is gone, which is what "the highlight around the tile should be removed"
                        // was about. Loose earth heaped on an edge is the FIRST thing to slump.
                        N = normalize(N + float3(-(rx - rim), 0, -(rz - rim)) * (_RimRelief * crest / e));

                        // ── THE WALL OF THE HOLLOW ─────────────────────────────────────────
                        //
                        // Every term above belongs to the CREST, which slumps - so a vacated
                        // square had no relief left at all, and the only thing still drawing it
                        // was a pair of albedo multiplies. A card-shaped rectangle at 38% of the
                        // field beside it, the same darkness from every angle, unmoved by where
                        // the sun was: a dark filter over the ground rather than a mark in it.
                        //
                        // A hollow is not a stain, it is a SHAPE. The ground stands at its own
                        // level out at the tile's margin, drops across the card's own outline and
                        // is flat and low underneath - and every bit of shading a real dent has
                        // follows from that wall meeting the light. So draw the wall and let the
                        // lighting do the rest: the near side takes the sun, the far side turns
                        // away from it, and the pair TURN OVER as the light moves, which is the
                        // thing no multiply can do.
                        //
                        // `lip` is the hollow's own height field, 1 on the floor and 0 at the
                        // margin, SQUARED - which puts the steep part against the card's outline
                        // and eases the outer end into undisturbed ground. That is the profile a
                        // hole in soft earth actually has: sharp where the object was, graded
                        // where the material slumped. Squaring also lands the outer end at zero
                        // gradient, so two neighbouring hollows meet at the midline without a
                        // crease down the tile boundary.
                        //
                        // Gated on `here`, NOT on `crest`: this one is the dent, and the dent is
                        // what is supposed to outlive the card.
                        float t0   = saturate(d / max(reach, 0.0001));
                        float lip  = (1.0 - t0) * (1.0 - t0);
                        float tx   = saturate(SrdRoundBox(rel + float2(e, 0) * aspect, halfF, roundF) / max(reach, 0.0001));
                        float tz   = saturate(SrdRoundBox(rel + float2(0, e) * aspect, halfF, roundF) / max(reach, 0.0001));
                        float lipx = (1.0 - tx) * (1.0 - tx);
                        float lipz = (1.0 - tz) * (1.0 - tz);
                        // POSITIVE where the crest's is negative, and that sign is the whole
                        // difference between the two: the crest is material standing proud of the
                        // ground, this is ground that has given way. h = -_HollowStep * lip, and a
                        // normal is (-dh/dx, 1, -dh/dz).
                        N = normalize(N + float3(lipx - lip, 0, lipz - lip) * (_HollowStep * here / e));

                        // The floor of a dent is walled in, so it sees less SKY - and that is all
                        // that is left of the old multiply. It is spent on the ambient term alone,
                        // at the light, so the sun still reaches into the hollow: lit from the
                        // side it comes out bright on one wall and dark on the other instead of
                        // uniformly grey, which is the difference between a depression and a hole
                        // cut out of the ground.
                        pressAo = lip * here * _HollowShade;

                        // Two surfaces, not one. On the crest, material that has just been turned
                        // over and is loose and pale; in the crack at the card's own edge, contact
                        // shade - and it is the shade that says the card is DOWN IN the ground
                        // rather than lying on top of it. A pale rim on its own reads as a halo.
                        //
                        // So they part company when the card leaves. The pale half goes entirely
                        // with the crest. The crack keeps a third: a hollow wants a dark edge, but
                        // at full strength it is the shadow cast by an EDGE SITTING IN the ground
                        // and there is no longer an edge in it.
                        //
                        // A HAIRLINE, either side of the outline - not `lip - rim`, which was ~1
                        // across the entire footprint and so was never a crack at all: it was the
                        // card-shaped multiply wearing a crack's name.
                        float crack = 1.0 - saturate(abs(d) / max(reach * 0.5, 0.0001));
                        albedo = lerp(albedo, albedo * 1.34 + _Highlight.rgb * 0.10, rim * crest);
                        albedo *= 1.0 - crack * _CrackShade * here * (0.34 + 0.66 * crest);
                    }
                }

                // ── the tide ─────────────────────────────────────────────────────────────────
                // A shore is not "sand with a water shader on it": it is a LINE that moves. The
                // whole read of a beach is the waterline running up and draining back, the wave
                // train marching in behind it, and the dark band of sand the last wave wet and the
                // next one has not reached yet. A surface that only ever flows one way is a river.
                //
                // Everything here happens on one axis: `along`, the distance seaward. The
                // waterline is a value on that axis, the tide moves it slowly, the swash moves it
                // faster and less far, and a low-frequency noise bends it so the shore is not
                // ruled with a straight edge.
                float waterMask = 1.0;          // deep water: the whole surface is water
                float breaker = 0.0, swashWet = 0.0;

                // Where the glitter is allowed to be. One everywhere by default - snow crystals
                // are scattered over the whole crust and should be - but the swell below pins it
                // to the crests, because sun on water is on the water that is FACING the sun.
                float sparkleGate = 1.0;

                if (_TideAmount > 0.001)
                {
                    float2 sea = normalize(_TideDir.xz + float2(0.0001, 0));
                    float along = dot(w, sea);
                    float across = dot(w, float2(-sea.y, sea.x));

                    // In and out, and the faster surge riding on it. _TideFreeze pins the slow
                    // term for the screenshot probe: a tide on a twenty-second breath is invisible
                    // in a still taken at the wrong second, and "is the sea there" is not a
                    // question a test should answer by luck.
                    float tide = _TideFreeze >= 0.0 ? (1.0 - _TideFreeze * 2.0)
                                                    : sin(t * 6.2831 / max(_TidePeriod, 0.001));
                    float swash = sin(t * 6.2831 / max(_TidePeriod * 0.17, 0.001)) * 0.30;
                    float shoreAt = _TideLevel + (tide + swash) * _TideRange;
                    shoreAt += (SrdFbm(float2(across * 0.09, t * 0.02)) - 0.5) * 2.2;

                    float depth = along - shoreAt;                      // > 0 is under water
                    waterMask = smoothstep(-0.15, 0.75, depth);

                    // WAVE LINES. Crests marching shoreward, packed closer and standing taller as
                    // they shoal - which is the cue that says which way the water is moving, and
                    // the one thing a scrolling noise can never say.
                    float shoal = exp(-max(depth, 0.0) * 0.10);
                    float crestPhase = along * _WaveFreq * (1.0 + shoal * 0.35)
                                     + t * _WaveSpeed
                                     + SrdFbm(float2(across * 0.16, along * 0.05)) * 2.6;
                    float crest = sin(crestPhase);
                    float train = smoothstep(0.10, 0.80, crest) * shoal;

                    // ...and they BREAK on the line: a bright band of foam pinned to the water's
                    // edge wherever it currently is.
                    breaker = exp(-depth * depth * 1.6) * (0.55 + 0.45 * crest);
                    breaker = saturate(breaker + train * waterMask * 0.95);

                    // trough dark, crest bright: the lines have to be readable AS lines, because
                    // they are the only thing on a flat sheet of water that says which way it is
                    // travelling
                    albedo *= 1.0 + crest * 0.13 * waterMask * _TideAmount;

                    // Behind the retreating water the sand stays dark and holds a lace of foam for
                    // a moment. Without it the tide slides over dry sand like a decal.
                    swashWet = saturate((depth + 2.4) / 2.4) * (1.0 - waterMask);

                    // the whole band the sea works over is damp, and darker toward the water
                    float reach = saturate((along - (_TideLevel - _TideRange * 2.2))
                                           / max(_TideRange * 2.2, 0.001));
                    albedo *= lerp(1.0, 0.66, saturate(reach * 0.75 + swashWet * 0.45) * _TideAmount);

                    // The water itself, deeper further out - and THINNER over the board.
                    //
                    // The wash runs right across the playing surface, which is the whole point of
                    // a beach: at this camera angle there is no room for a sea past the far wall,
                    // so the tide has to be something that happens TO the board rather than behind
                    // it. But a card under three quarters of an opaque water tint is a card you
                    // cannot read, and no amount of sea is worth that - so over the tiles the
                    // water thins to a wet film and the foam does the describing instead.
                    float2 overBoard = saturate((abs(w) - _BoardHalf.xy) / 1.4 + 1.0);
                    float onBoard = 1.0 - saturate(max(overBoard.x, overBoard.y));
                    float tint = lerp(0.82, _BoardWet, onBoard);

                    float3 water = lerp(_WaterColor.rgb, _DeepColor.rgb, saturate(depth / 9.0));
                    albedo = lerp(albedo, water, waterMask * tint * _TideAmount);

                    // the surface tilts with the wave train, so the sun finds the crests
                    float slopeW = cos(crestPhase) * _WaveFreq * shoal * 0.35 * waterMask;
                    N = normalize(N + float3(sea.x, 0, sea.y) * slopeW);
                }

                // ── open water: a swell that MARCHES ────────────────────────────────────────
                //
                // The old version was two fixed sines and a scrolling fbm. Every term of it was
                // symmetric about its own mean and not one of them was long-crested, so the sea
                // had no crest LINES anywhere in it - and a surface with no lines cannot say which
                // way it is travelling. It shimmered; it never went anywhere.
                //
                // What reads as open water is a TRAIN: long crests lying across one bearing,
                // SHARPENED so the crest comes to a brink and the trough is broad and flat (that
                // asymmetry is the whole difference between a swell and a ripple tank), bent along
                // their own length so they are not ruled with a straightedge, and a second shorter
                // train crossing at 32 degrees so the field is a sea rather than corduroy.
                //
                // Not the shore's tide, deliberately. A tide is a LINE that runs up a beach and
                // drains back; this water has no beach and nothing recedes - it just keeps coming.
                if (_WaveAmount > 0.001 && waterMask > 0.001)
                {
                    float t2 = t * _MotionSpeed;
                    float2 d1 = normalize(_SwellDir.xz + float2(0.0001, 0));
                    float2 d2 = SrdRotate(d1, 32.0);

                    float k1 = 6.2831853 * _WaveFreq;
                    float k2 = k1 / 0.58;                       // the cross swell is the shorter one

                    // The bend. A slow noise sampled ACROSS each train and added to its phase:
                    // the crests keep their spacing and lose their straightness, which is what a
                    // real crest does. Sampled across only - a noise in both axes would break the
                    // lines up into blobs and put us back where we started.
                    float a1 = dot(w, float2(-d1.y, d1.x));
                    float a2 = dot(w, float2(-d2.y, d2.x));
                    float b1 = (SrdValueNoise(float2(a1 * 0.11, t2 * 0.05)) - 0.5) * 2.4;
                    float b2 = (SrdValueNoise(float2(a2 * 0.17 + 9.4, t2 * 0.04)) - 0.5) * 1.7;

                    float p1 = (dot(w, d1) - t2 * _WaveSpeed) * k1 + b1;
                    float p2 = (dot(w, d2) - t2 * _WaveSpeed * 0.78) * k2 + b2;

                    // (sin+1)/2 raised to a power: the crest peaks, the trough spreads out flat,
                    // and the derivative falls out of the same two terms, so the light gets the
                    // real slope of the water rather than a wobble bolted on beside it.
                    float c1 = 0.5 + 0.5 * sin(p1), c2 = 0.5 + 0.5 * sin(p2);
                    float q1 = pow(c1, 1.6), q2 = pow(c2, 1.2);
                    float e1 = q1 * c1, e2 = q2 * c2;
                    float g1 = 1.30 * q1 * cos(p1) * k1;
                    float g2 = 1.10 * q2 * cos(p2) * k2 * 0.55;

                    float surf = saturate(e1 + e2 * 0.55);
                    float2 slope = d1 * g1 + d2 * g2;

                    // Over the board it all THINS. The sea runs across the playing surface because
                    // at this camera there is nowhere else for it to be, and a card under a full
                    // swell is a card nobody can read.
                    float2 overBoard = saturate((abs(w) - _BoardHalf.xy) / 1.6 + 1.0);
                    float keep = lerp(0.42, 1.0, saturate(max(overBoard.x, overBoard.y)));

                    N = normalize(N + float3(slope.x, 0, slope.y) * _SwellHeight * waterMask * keep);

                    // Crest bright, trough deep. The colour swing is what carries the wave across
                    // the middle distance, where the surface is nearly edge-on and the normal has
                    // almost nothing left to catch.
                    albedo = lerp(albedo, _DeepColor.rgb,
                                  (1.0 - surf) * 0.30 * _WaveAmount * waterMask * keep);
                    albedo *= 1.0 + (surf - 0.42) * 0.34 * _WaveAmount * waterMask * keep;

                    // Whitecaps on the BRINK: the steep front face of the tallest crests only,
                    // broken along the crest by a noise so they are patches of white water and not
                    // a painted line following every wave.
                    float steep = saturate(-g1 / max(k1, 0.001));
                    float lace = SrdValueNoise(w * 3.2 + float2(t2 * 0.5, 0.0));
                    float cap = smoothstep(0.55, 0.92, e1)
                              * smoothstep(0.15, 0.60, steep)
                              * smoothstep(0.34, 0.66, lace);
                    albedo = lerp(albedo, _FoamColor.rgb, cap * _SwellFoam * waterMask * keep);

                    // ...and the glitter goes ON the crests. Point glints scattered evenly over a
                    // whole sea are a second particle field, which is exactly what the loose specs
                    // over this biome already read as; pinned to the moving crests they are sun on
                    // water, and they travel with the wave that carries them.
                    sparkleGate = smoothstep(0.35, 0.85, surf) * keep;
                }

                // the surf, over the top of whatever the water surface is doing
                if (_TideAmount > 0.001)
                {
                    float lace = SrdFbm(w * 5.5 + float2(0, t * 0.6));
                    float foamEdge = saturate(breaker * (0.6 + lace * 0.8));
                    float foamDry = swashWet * smoothstep(0.45, 0.85, lace) * 0.5;
                    albedo = lerp(albedo, _FoamColor.rgb,
                                  saturate(foamEdge + foamDry) * _TideAmount * 0.85);
                }

                // ── sand: a slow travelling corduroy over the top of the streaks ────────────
                if (_RippleAmount > 0.001)
                {
                    float2 ripple = float2(wp.x, wp.y * 2.2) * 2.4 + float2(0, t * _MotionSpeed * 0.30);
                    float ridge = sin(ripple.y + SrdFbm(w * 0.5) * 6.2831);
                    albedo *= 1.0 + ridge * 0.055 * _RippleAmount;

                    // Loose sand streaming off the brinks, which is the thing the eye reads as
                    // wind - and it BREATHES, on the same slow clock the veil uses. One rate held
                    // forever is a scrolling texture: the ground streamed at a constant rate while
                    // the air above it gusted, and the disagreement was as bad as either alone.
                    float breath = SrdFbm(float2(t / max(_GustPeriod, 0.1), 4.7)) * 2.0 - 1.0;
                    float gustNow = saturate(1.0 + breath * _GustSwing);

                    float drift = smoothstep(0.78 - gustNow * 0.10, 1.0,
                        SrdFbm(float2(wp.x * 0.12, wp.y * 1.1) * 2.0
                               - float2(t * _MotionSpeed * 0.9 * (0.5 + gustNow * 0.7), 0)));
                    albedo = lerp(albedo, _Highlight.rgb,
                                  drift * _RippleAmount * 0.26 * gustNow * saturate(rise * 1.4));
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

                // ...and a card's hollow is one more thing standing between this point and the
                // sky. On the AMBIENT only: the sun reaches into a dent, the sky does not, and
                // spending it here rather than on albedo is what leaves the hollow's near wall
                // bright while its far wall goes dark.
                ambient *= 1.0 - saturate(pressAo);

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
                    col += _SunColor.rgb * spark * _Sparkle * sparkleGate
                         * saturate(diffuse * 1.4) * shadow * 1.6;
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
