#ifndef SRD_NOISE_INCLUDED
#define SRD_NOISE_INCLUDED

// Wind and cloud fields for the battlefield terrain.
//
// The structure of both is ported from Dynamic 2D Grass (MIT, Jomoho Games, based on original
// work by Dylearn) - https://github.com/jomoho/dynamic-2d-grass. The idea worth taking is that a
// single scrolling noise reads as a repeating texture sliding past, and TWO of them, rotated a
// few degrees apart and scrolled at different rates, multiply into a field with no visible period.
// That is what makes the wind look like weather instead of an animation loop.
//
// The noise itself is generated rather than sampled from a texture: this project builds its assets
// in code (see CardTextures) and a value-noise fbm costs less than the import pipeline, the .meta
// churn and the WebGL stripping risk of four more texture assets.

// Hash-based value noise. Deterministic, no texture, no sampler state.
//
// The multipliers are Dave Hoskins' hash family (MIT), not the frac(p * 123.34) form this file
// shipped with. That one is only well behaved on a FRACTIONAL domain, and value noise feeds it
// INTEGER lattice points: frac(i * 123.34) is frac(i * 0.34), a ramp with a period of fifty, so
// the "noise" carried a repeating diagonal structure. Scrolled across the board it read as a
// printed sheet sliding past - which is what the cloud shadows looked like.
float SrdHash(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

/// Three decorrelated randoms from one lattice point: two to jitter a cell's centre, one for
/// everything else the cell has to decide - whether it carries a cloud at all, and how big.
float3 SrdHash23(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float SrdValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);          // smoothstep, so the field has no facets

    float a = SrdHash(i);
    float b = SrdHash(i + float2(1.0, 0.0));
    float c = SrdHash(i + float2(0.0, 1.0));
    float d = SrdHash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float SrdFbm(float2 p)
{
    float v = 0.0;
    float amp = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        v += amp * SrdValueNoise(p);
        p = p * 2.03 + 17.7;                     // the offset stops the octaves lining up
        amp *= 0.5;
    }
    return v;
}

/// Signed distance to a rounded box, in 2D. Negative inside, and the value outside is a real
/// distance, so a ring is `abs(d - r)` and nothing else.
///
/// Every ripple this project throws used to be a circle, because a circle is one length() call.
/// But nothing on this board is round: a card is a rounded rectangle, and the material shoved
/// aside by one piles up in that shape rather than in a disc around it. Same cost, right shape.
float SrdRoundBox(float2 p, float2 halfSize, float radius)
{
    float2 d = abs(p) - max(halfSize - radius, 0.0);
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

float2 SrdRotate(float2 v, float degrees)
{
    float a = radians(degrees);
    float s, c;
    sincos(a, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

/// The dual-scroll wind field, in -1..1. `divergence` is the angle the two samples pull apart by.
///
/// The PRODUCT of the two samples is the anti-periodicity trick and it is worth keeping, but it
/// cannot be used raw. Two noises in 0..1 multiply to a field with mean 0.25 and small variance,
/// so `(n1*n2 + bias - 0.5) * 2` sits pinned near zero: measured mean 0.053 out of a possible ±1,
/// which is a field that looks painted on rather than blown. Re-centre on the product's OWN mean
/// and give it gain, and the same field swings properly.
float SrdDualScroll(float2 pos, float2 dir, float time, float speed, float divergence,
                    float gain, float bias)
{
    float2 d1 = normalize(SrdRotate(dir, divergence));
    float2 d2 = normalize(SrdRotate(dir, -divergence));

    // the second sample runs at a different scale AND rate; matching either would beat visibly
    float n1 = SrdValueNoise(pos + time * speed * d1);
    float n2 = SrdValueNoise(pos * 0.8 + time * speed * d2 * 0.89 * PI / 3.0);

    return clamp((n1 * n2 - 0.25) * gain + bias, -1.0, 1.0);
}

/// How much CLOUD is over this patch of ground, 0..1.
///
/// Not a threshold of fbm. That is what was here, and a thresholded noise field is a flat plane
/// with hard-edged holes punched through it: on screen it read as a sheet of paper with clouds
/// drawn on it, sliding past. The clamp did the damage - most of the field sat pinned at the
/// shadow floor, so the moving thing was the LIT gap, with a step for an edge.
///
/// A cloud is built as a cloud instead: a jittered grid where some cells carry one round lump,
/// the lump's falloff is smooth all the way out, and neighbouring lumps ADD - so a lone cell is a
/// single puff and a run of them merges into one lobed cumulus. Every edge is a gradient, and a
/// gradient cannot draw a straight line.
///
/// The outline is then WARPED rather than overprinted. A second additive layer of smaller lumps
/// is the obvious way to get raggedness, and it sprays free-floating specks across the field
/// wherever the base is mid-valued; a displacement can only push the outline about.
///
/// 9 cells, one hash each - about what the two-octave fbm it replaces cost.
float SrdCloudCover(float2 p, float time, float gate, float radMin, float radVar, float squash,
                    float warpLow, float warpHigh)
{
    float2 w = p + warpLow  * float2(sin(p.y * 2.1 + time * 0.11), cos(p.x * 1.9 - time * 0.09))
                 + warpHigh * float2(sin(p.y * 5.3 - time * 0.19 + 1.7),
                                     cos(p.x * 4.7 + time * 0.23 + 0.6));

    float2 cell = floor(w);
    float2 f = w - cell;

    float cover = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float3 h = SrdHash23(cell + g);
            if (h.z < gate) continue;            // an empty sky is most of the sky

            // A surviving cell carries a FULL lump. Fading a cell in with its own hash was the
            // first version and it is what put a fleet of half-strength specks between the
            // clouds: density belongs in how many cells qualify, not in how solid each one is.
            float2 d = g + 0.15 + h.xy * 0.7 - f;
            float rad = radMin + radVar * frac(h.z * 11.3);
            cover += smoothstep(rad, rad * 0.15, length(d * float2(squash, 1.0)));
        }
    }
    return saturate(cover);
}

/// Cloud LIGHT, in `shadowMin`..1 - multiply it into a colour to cast the shadow.
/// The field is LIT by default and clouds pass over it, which is the way round a sunlit
/// battlefield works.
float SrdCloudLight(float2 world, float time, float2 dir, float scale, float speed, float shadowMin)
{
    float2 p = world / max(scale, 0.0001) - normalize(dir) * time * speed;
    float cover = SrdCloudCover(p, time, 0.62, 0.36, 0.42, 0.78, 0.22, 0.09);
    return 1.0 - cover * (1.0 - shadowMin);
}

#endif
