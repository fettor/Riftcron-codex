#ifndef TUNTENFISCH_VOXELS_MOUNTAINS
#define TUNTENFISCH_VOXELS_MOUNTAINS

#include "Assets/Compute/Voxels/Include/Noise.hlsl"

// ------------------------------------------------------------
// Constants (chosen to be safe but still “fantasy”)
// ------------------------------------------------------------

// Highest spatial frequency allowed for the top octave (cycles per meter).
// => wavelength >= 1 / MTN_MAX_TOP_FREQ  (here: 50 m)
static const float MTN_MAX_TOP_FREQ = 0.02;

// Hard caps to prevent extreme settings from reintroducing artifacts.
static const uint  MTN_MAX_OCTAVES  = 7;
static const float MTN_MIN_EXP      = 1.0;  // no <1 exponents (flatten-to-1 / blow-ups)
static const float MTN_POW_DCAP     = 1.5;  // cap derivative amplification from pow()
static const float MTN_TERR_DCAP    = 1.25; // cap derivative amplification from terrace()
static const float MTN_TERR_MINW    = 0.20; // >=20% of each step is smoothing

// Slope budget in meters-per-meter (tan(maxSlopeAngle)).
// tan(72°) ≈ 3.077; 2.5 is a bit gentler and very stable for DC.
static const float MTN_SLOPE_BUDGET = 2.5;

// ------------------------------------------------------------
// Parameter layout (kept identical to your project)
// ------------------------------------------------------------
struct GPUMountainParameters
{
    float4 amplitude;       // Per-biome amplitude multipliers.
    float4 remapExponent;   // Per-biome exponent.
    float4 terraceSteps;    // Per-biome terrace steps.
    float4 terraceBias;     // Per-biome terrace bias.
    float4 baseParameters;  // x: base amplitude, y: base exponent, z: base steps, w: base bias.

    float4 warpParameters;  // x: large warp amp, y: large warp freq, z: detail warp amp, w: detail warp freq.
    float4 extraParameters0;// x: mix strength, y: terrace power, z: -, w: -.
    float4 extraParameters1;// x: plateau slope min, y: plateau slope max, z: -, w: -.

    uint  biomeCount;
    uint  warpSeed;
    float2 padding;
};

// Debug
static float g_mountainMask = 0.0f;
static float g_plateauMask  = 0.0f;
void  ResetMountainDebug()     { g_mountainMask = 0.0f; g_plateauMask = 0.0f; }
float GetMountainMask()        { return saturate(g_mountainMask); }
float GetPlateauMask()         { return saturate(g_plateauMask);  }

// ------------------------------------------------------------
// Height sample
// ------------------------------------------------------------
struct HeightSample { float value; float3 gradient; };
static HeightSample MakeHeightSample(float v, float3 g) { HeightSample s; s.value=v; s.gradient=g; return s; }

static HeightSample HeightSampleScale(HeightSample s, float k)
{
    k = max(k, 0.0f);
    return MakeHeightSample(s.value * k, s.gradient * k);
}

// Clamp value to [0,1] and zero gradient where clamped.
static HeightSample HeightSampleSaturate(HeightSample s)
{
    float keep = step(0.0f, s.value) * (1.0f - step(1.0f, s.value));
    return MakeHeightSample(saturate(s.value), s.gradient * keep);
}

// ------------------------------------------------------------
// Slope‑safe shaping primitives
// ------------------------------------------------------------

// y = pow(x, e) with e >= 1 and derivative cap -> avoids infinite/huge slopes.
static HeightSample HeightSamplePow(HeightSample s, float exponent)
{
    float e      = max(exponent, MTN_MIN_EXP);
    float base   = max(s.value, 1e-4f);
    float outVal = pow(base, e);

    float deriv  = e * pow(base, e - 1.0f);
    deriv        = min(deriv, MTN_POW_DCAP);

    return MakeHeightSample(outVal, s.gradient * deriv);
}

// Soft terrace in normalized space with bounded derivative and minimum transition width.
static HeightSample HeightSampleTerrace(HeightSample s, float steps, float bias)
{
    float stepCount   = max(steps, 1.0f);
    float biasClamped = saturate(bias);
    if (stepCount <= 1.001f) return s;

    float t    = s.value * stepCount;
    float cell = floor(t);
    float frac = t - cell;

    // Ensure a minimum smoothing width per step.
    float range = max(1.0f - biasClamped, MTN_TERR_MINW);
    float n     = saturate((frac - biasClamped) / range);
    float sm    = n * n * (3.0f - 2.0f * n);  // smoothstep

    float dsm = 0.0f;
    if (n > 0.0f && n < 1.0f) dsm = 6.0f * n * (1.0f - n) / range;
    dsm = min(dsm, MTN_TERR_DCAP);

    float terr = (cell + sm) / stepCount;
    return MakeHeightSample(terr, s.gradient * dsm);
}

// ------------------------------------------------------------
// Warping (XZ‑only) & Ridged FBM with frequency cap
// ------------------------------------------------------------

static float3 SampleSimplexDisplacement(float3 p, uint seed)
{
    float3 off = CalculateOctaveOffset(seed, 0u);
    float3 d;
    d.x = SimplexNoiseGrad(p + off + float3( 0.0f, 0.0f,  0.0f)).w;
    d.y = SimplexNoiseGrad(p + off + float3(19.1f, 7.3f, 11.8f)).w;
    d.z = SimplexNoiseGrad(p + off + float3(-23.7f, 5.2f, 37.3f)).w;
    return d;
}

// XZ‑only displacement; height must not depend on Y.
static float3 ApplyMountainWarp(float3 pos, float amp, float freq, uint seed)
{
    if (abs(amp) < 1e-4f || abs(freq) < 1e-6f) return pos;
    float3 d = SampleSimplexDisplacement(float3(pos.x, 0.0f, pos.z) * freq, seed);
    pos.x += amp * d.x;
    pos.z += amp * d.z;
    return pos;
}

// Ridged multifractal (XZ‑only) with:
//  - hard octave cap,
//  - cap on highest achievable frequency,
//  - tiny peak softening to avoid needle tops.
static HeightSample SampleRidgedFBM(float3 position, NoiseParameters np)
{
    // Prepare frequencies with a top‑frequency cap
    float3 baseFreq = max(abs(np.initialFrequency), 1e-6f);
    float3 lac      = max(abs(np.lacunarity), 1.0f);
    uint   octaves  = min(max(np.numberOfOctaves, 1u), MTN_MAX_OCTAVES);

    // Ensure the highest octave never exceeds MTN_MAX_TOP_FREQ in XZ
    float powX = pow(lac.x, (float)(octaves - 1u));
    float powZ = pow(lac.z, (float)(octaves - 1u));
    baseFreq.x = min(baseFreq.x, MTN_MAX_TOP_FREQ / max(powX, 1.0f));
    baseFreq.z = min(baseFreq.z, MTN_MAX_TOP_FREQ / max(powZ, 1.0f));

    float  gain   = max(np.persistence, 1e-4f);
    float  offset = max(np.initialAmplitude, 0.0f);

    float  amp    = 0.5f;
    float3 freq   = float3(baseFreq.x, 1.0f, baseFreq.z);
    float  sum    = 0.0f;
    float3 gsum   = 0.0f;

    [unroll]
    for (uint o=0u; o<octaves; ++o)
    {
        float3 off = CalculateOctaveOffset(np.seed, o);
        float3 q   = float3(position.x, 0.0f, position.z) * freq + off;

        float4 n   = SimplexNoiseGrad(q).wxyz;     // x = value, y/z/w = grad components
        float  v   = n.x;
        float3 g   = float3(n.y * freq.x, 0.0f, n.w * freq.z);

        float  folded = abs(v);
        float  ridge  = max(offset - folded, 0.0f);
        ridge         = ridge * (0.85f + 0.15f * ridge); // micro‑soften
        float  r2     = ridge * ridge;

        float  sgn    = (v >= 0.0f) ? 1.0f : -1.0f;
        float3 rg     = -2.0f * ridge * sgn * g;

        sum   += r2 * amp;
        gsum  += rg * amp;

        float blend = lerp(gain, gain * r2, 0.5f);
        amp        *= blend;
        freq.x     *= lac.x;
        freq.z     *= lac.z;
    }

    return HeightSampleSaturate(MakeHeightSample(sum, gsum));
}

// Global warps (XZ) then ridged
static HeightSample SampleGlobalMountainField(float3 position, NoiseParameters base, GPUMountainParameters mp)
{
    float3 pw = position;
    pw = ApplyMountainWarp(pw, mp.warpParameters.x, mp.warpParameters.y, base.seed);
    pw = ApplyMountainWarp(pw, mp.warpParameters.z, mp.warpParameters.w, base.seed + mp.warpSeed + 97u);
    return SampleRidgedFBM(pw, base);
}

// Convert height h(x,z) to SDF: value = y - h; gradient = (-∂h/∂x, 1, -∂h/∂z)
static float4 HeightSampleToSdf(float3 pos, HeightSample h)
{
    return float4(pos.y - h.value, -h.gradient.x, 1.0f, -h.gradient.z);
}

// ------------------------------------------------------------
// Main entry
// ------------------------------------------------------------
void EvaluateMountain(float3 position, NoiseParameters baseParameters, GPUMountainParameters mp, out float4 valueAndGrad)
{
    ResetMountainDebug();

    // Normalized ridged height with valid XZ gradients
    HeightSample ridged = SampleGlobalMountainField(position, baseParameters, mp);

    // --- Blend biome parameters (same semantics you had)
    uint available = GetBiomeCount();
    uint count     = min(mp.biomeCount, available);

    float mixStrength = saturate(mp.extraParameters0.x);
    float terracePow  = max(mp.extraParameters0.y, MTN_MIN_EXP);

    float baseAmp   = max(mp.baseParameters.x, 0.0f);
    float baseExp   = max(mp.baseParameters.y, MTN_MIN_EXP);
    float baseSteps = max(mp.baseParameters.z, 1.0f);
    float baseBias  = saturate(mp.baseParameters.w);

    float blendAmp   = baseAmp;
    float blendExp   = baseExp;
    float blendSteps = baseSteps;
    float blendBias  = baseBias;

    if (mixStrength > 1e-3f && count > 0u)
    {
        float wSum=0.0f, aSum=0.0f, eSum=0.0f, sSum=0.0f, bSum=0.0f;
        [unroll]
        for (uint i=0u; i<4u; ++i)
        {
            float enabled = step((float)i, (float)count - 0.5f);
            float w       = GetBiomeWeightMasked(i) * enabled;
            if (w <= 1e-4f) continue;

            aSum += mp.amplitude[i]     * w;
            eSum += mp.remapExponent[i] * w;
            sSum += mp.terraceSteps[i]  * w;
            bSum += mp.terraceBias[i]   * w;
            wSum += w;
        }

        if (wSum > 1e-4f)
        {
            float inv = rcp(wSum);
            blendAmp   = lerp(baseAmp,   aSum * inv, mixStrength);
            blendExp   = lerp(baseExp,   eSum * inv, mixStrength);
            blendSteps = lerp(baseSteps, sSum * inv, mixStrength);
            blendBias  = lerp(baseBias,  bSum * inv, mixStrength);
        }
    }

    // Guards so sliders/biomes can't re‑introduce unstable shapes
    blendExp   = max(blendExp, MTN_MIN_EXP);
    blendSteps = clamp(blendSteps, 1.0f, 24.0f);
    blendBias  = saturate(blendBias);

    // --- Shaping (slope‑safe)
    HeightSample sharp    = HeightSamplePow(ridged, blendExp);
    HeightSample terraced = HeightSampleTerrace(sharp, blendSteps, blendBias);
    HeightSample shaped   = HeightSamplePow(terraced, terracePow);

    // --- Local slope limiter BEFORE scaling to meters
    // Limit final slope: amplitude * |∇shaped| <= MTN_SLOPE_BUDGET  -> scale amplitude locally if needed.
    float gmag      = length(shaped.gradient.xz);
    float localAmp  = blendAmp;
    if (gmag > 1e-5f)
    {
        float allowed = MTN_SLOPE_BUDGET / gmag;         // max amplitude at this point
        localAmp      = min(blendAmp, allowed);
    }

    // --- Scale to meters; NO clamp after amplitude
    HeightSample meters = HeightSampleScale(shaped, localAmp);

    // --- SDF conversion
    valueAndGrad = HeightSampleToSdf(position, meters);

    // --- Debug masks
    float dhdx = -valueAndGrad.y, dhdz = -valueAndGrad.w;
    float slopeMag = sqrt(dhdx*dhdx + dhdz*dhdz);
    float slopeDeg = degrees(atan(slopeMag));

    float2 plateauSlopeRange = mp.extraParameters1.xy;
    float plateau = 1.0f - smoothstep(plateauSlopeRange.x, plateauSlopeRange.y, slopeDeg);

    g_mountainMask = saturate(shaped.value);
    g_plateauMask  = saturate(plateau);
}

#endif // TUNTENFISCH_VOXELS_MOUNTAINS
