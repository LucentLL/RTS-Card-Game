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
float SrdHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
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

/// Two octaves, not four. This is the cloud field's budget: it is evaluated once per SCREEN pixel
/// in the overlay pass, and at four octaves that is ~50 hashes a pixel on a phone for detail the
/// blur of a cloud shadow throws away anyway.
float SrdFbm2(float2 p)
{
    return SrdValueNoise(p) * 0.62 + SrdValueNoise(p * 2.03 + 17.7) * 0.38;
}

/// Cloud LIGHT, in `shadowMin`..1 - multiply it into a colour to cast the shadow.
/// Domain-warped so the shadow edges are ragged rather than blobby.
float SrdCloudLight(float2 world, float time, float2 dir, float scale, float speed,
                    float contrast, float threshold, float shadowMin, float divergence,
                    float warpScale, float warpFreq)
{
    // the warp is trigonometric rather than another noise lookup - same raggedness, a tenth the cost
    float2 warp = warpScale * float2(sin(world.y * warpFreq), cos(world.x * warpFreq));
    float2 p = (world + warp) / max(scale, 0.0001);

    float2 d1 = normalize(SrdRotate(dir, divergence));
    float2 d2 = normalize(SrdRotate(dir, -divergence));

    float s1 = SrdFbm2(p + time * speed * d1);
    float s2 = SrdFbm2(p * 0.8 + time * speed * d2 * 0.89 * PI / 3.0);

    float light = saturate(s1 * s2 + threshold);
    light = (light - 0.5) * contrast + 0.5;
    return clamp(light + threshold, shadowMin, 1.0);
}

#endif
