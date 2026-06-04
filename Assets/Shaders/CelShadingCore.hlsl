#ifndef CELSHADINGCORE_INCLUDED
#define CELSHADINGCORE_INCLUDED

// CelShadingCore.hlsl
// Shared helpers for CelShading.shader and PlayerProximity.shader.
// Include AFTER Core.hlsl and Lighting.hlsl.
// PURE ASCII only - Unity HLSL compiler rejects non-ASCII characters.

// ---------------------------------------------------------------------------
// CelStep - quantise v [0..1] into n discrete shading bands.
//   e = band edge softness: 0 = hard cut, 0.05 = slightly soft.
//   Branch-free smoothstep; e=0 degenerates to a hard step at band midpoint.
// ---------------------------------------------------------------------------
float CelStep(float v, float n, float e)
{
    float scaled = v * n;
    float s      = floor(scaled);
    float blend  = smoothstep(0.5 - e, 0.5 + e, frac(scaled));
    return saturate((s + blend) / n);
}

// ---------------------------------------------------------------------------
// BayerDither4x4 - returns a threshold in [0,1) for screen pixel (x,y).
// Closed-form, no arrays, no branches. Produces the exact standard 4x4 matrix:
//    0  8  2 10
//   12  4 14  6
//    3 11  1  9
//   15  7 13  5   (all divided by 16)
// Usage: clip(BayerDither4x4(posCS.xy) - (1.0 - alpha));
//   alpha = 1.0  -> all pixels pass (fully visible)
//   alpha = 0.0  -> all pixels clip (fully invisible)
// ---------------------------------------------------------------------------
float BayerDither4x4(float2 pos)
{
    int px    = int(fmod(abs(pos.x), 4.0));
    int py    = int(fmod(abs(pos.y), 4.0));
    int outer = ((px % 2) * 2 + (py % 2) * 3) % 4;
    int inner = ((px / 2) * 2 + (py / 2) * 3) % 4;
    return float(outer * 4 + inner) / 16.0;
}

#endif // CELSHADINGCORE_INCLUDED
