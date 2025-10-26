#ifndef TUNTENFISCH_VOXELS_MOUNTAINS
#define TUNTENFISCH_VOXELS_MOUNTAINS

#include "Assets/Compute/Voxels/Include/Noise.hlsl"

// ------------------------------------------------------------
// Constants (safe but still “fantasy”)
// ------------------------------------------------------------
static const float MTN_MAX_TOP_FREQ = 0.02;  // cycles/m (=> wavelength >= 50 m)
static const uint  MTN_MAX_OCTAVES  = 7;

static const float MTN_MIN_EXP      = 1.0;   // never <1 (prevents flatten-to-1 / blow-ups)
static const float MTN_POW_DCAP     = 1.5;   // cap derivative from pow()
static const float MTN_TERR_DCAP    = 1.25;  // cap derivative from terrace()
static const float MTN_MAX_WARP_STRETCH = 3.5;

// Make terrace edges clearly wide so they can't form near-vertical bands.
static const float MTN_TERR_MINW    = 0.35;  // >=35% of each step is smoothing

// Global slope budget: amplitude * |∇(normalized shape)| <= budget
// 2.0 ≈ tan(63.4°): steep but safe for DC; lower to smooth more.
static const float MTN_SLOPE_BUDGET = 2.0;

// ------------------------------------------------------------
// Parameter layout (unchanged)
// ------------------------------------------------------------
struct GPUMountainParameters
{
    float4 amplitude;       // Per-biome amplitude multipliers.
    float4 remapExponent;   // Per-biome remap exponent.
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
// Height sample helpers
// ------------------------------------------------------------
struct HeightSample { float value; float3 gradient; };

static HeightSample MakeHeightSample(float v, float3 g) { HeightSample s; s.value=v; s.gradient=g; return s; }

static HeightSample HeightSampleScale(HeightSample s, float k)
{
    k = max(k, 0.0f);
    return MakeHeightSample(s.value * k, s.gradient * k);
}

// Clamp to [0,1] and zero gradient where clamped.
static HeightSample HeightSampleSaturate(HeightSample s)
{
    float keep = step(0.0f, s.value) * (1.0f - step(1.0f, s.value));
    return MakeHeightSample(saturate(s.value), s.gradient * keep);
}

static HeightSample HeightSampleLerp(HeightSample a, HeightSample b, float t)
{
    t = saturate(t);
    return MakeHeightSample(lerp(a.value, b.value, t), lerp(a.gradient, b.gradient, t));
}

// ------------------------------------------------------------
// Slope‑safe shaping
// ------------------------------------------------------------
static HeightSample HeightSamplePow(HeightSample s, float exponent)
{
    float e      = max(exponent, MTN_MIN_EXP);
    float base   = max(s.value, 1e-4f);
    float outVal = pow(base, e);

    float deriv  = e * pow(base, e - 1.0f);
    deriv        = min(deriv, MTN_POW_DCAP);

    return MakeHeightSample(outVal, s.gradient * deriv);
}

// Terrace with bounded derivative and a guaranteed minimum edge width.
static HeightSample HeightSampleTerrace(HeightSample s, float steps, float bias)
{
    float stepCount   = max(steps, 1.0f);
    float biasClamped = saturate(bias);
    if (stepCount <= 1.001f) return s;

    float t    = s.value * stepCount;
    float cell = floor(t);
    float frac = t - cell;

    // Ensure a wide transition band per step.
    float range = max(1.0f - biasClamped, MTN_TERR_MINW);
    float n     = saturate((frac - biasClamped) / range);
    float sm    = n * n * (3.0f - 2.0f * n);   // smoothstep

    float dsm = 0.0f;
    if (n > 0.0f && n < 1.0f) dsm = 6.0f * n * (1.0f - n) / range;
    dsm = min(dsm, MTN_TERR_DCAP);

    float terr = (cell + sm) / stepCount;
    return MakeHeightSample(terr, s.gradient * dsm);
}

// ------------------------------------------------------------
// Warping (XZ‑only) & Ridged FBM with frequency cap
// ------------------------------------------------------------
struct MountainDisplacementSample
{
    float3 value;
    float2 gradX;
    float2 gradZ;
};

static MountainDisplacementSample SampleSimplexDisplacement(float3 p, uint seed)
{
    MountainDisplacementSample sample;
    float3 off = CalculateOctaveOffset(seed, 0u);

    float4 nx = SimplexNoiseGrad(p + off + float3( 0.0f, 0.0f,  0.0f));
    float4 ny = SimplexNoiseGrad(p + off + float3(19.1f, 7.3f, 11.8f));
    float4 nz = SimplexNoiseGrad(p + off + float3(-23.7f, 5.2f, 37.3f));

    sample.value = float3(nx.w, ny.w, nz.w);
    sample.gradX = float2(nx.x, nx.z);
    sample.gradZ = float2(nz.x, nz.z);
    return sample;
}

// XZ-only displacement; also returns Jacobian updates through dPos_dX/dPos_dZ.
static float3 ApplyMountainWarp(float3 pos, float amp, float freq, uint seed, inout float2 dPos_dX, inout float2 dPos_dZ)
{
    float3 newPos    = pos;
    float2 new_dPos_dX = dPos_dX;
    float2 new_dPos_dZ = dPos_dZ;

    if (abs(amp) >= 1e-4f && abs(freq) >= 1e-6f)
    {
        float3 basePos = float3(newPos.x, 0.0f, newPos.z);
        MountainDisplacementSample sample = SampleSimplexDisplacement(basePos * freq, seed);

        float2 disp = float2(sample.value.x, sample.value.z) * amp;
        newPos.x += disp.x;
        newPos.z += disp.y;

        float2 derivX = sample.gradX * (amp * freq);
        float2 derivZ = sample.gradZ * (amp * freq);

        float L00 = 1.0f + derivX.x;
        float L01 = derivX.y;
        float L10 = derivZ.x;
        float L11 = 1.0f + derivZ.y;

        float2 prev_dPos_dX = dPos_dX;
        float2 prev_dPos_dZ = dPos_dZ;

        new_dPos_dX = float2(
            L00 * prev_dPos_dX.x + L01 * prev_dPos_dX.y,
            L10 * prev_dPos_dX.x + L11 * prev_dPos_dX.y);

        new_dPos_dZ = float2(
            L00 * prev_dPos_dZ.x + L01 * prev_dPos_dZ.y,
            L10 * prev_dPos_dZ.x + L11 * prev_dPos_dZ.y);

        float stretchX = length(new_dPos_dX);
        float stretchZ = length(new_dPos_dZ);
        float stretch  = max(max(stretchX, stretchZ), 1.0f);
        if (stretch > MTN_MAX_WARP_STRETCH)
        {
            float scale = MTN_MAX_WARP_STRETCH / stretch;
            newPos.x = pos.x + (newPos.x - pos.x) * scale;
            newPos.z = pos.z + (newPos.z - pos.z) * scale;
            new_dPos_dX = lerp(float2(1.0f, 0.0f), new_dPos_dX, scale);
            new_dPos_dZ = lerp(float2(0.0f, 1.0f), new_dPos_dZ, scale);
        }
    }

    dPos_dX  = new_dPos_dX;
    dPos_dZ  = new_dPos_dZ;
    return newPos;
}

// Ridged fBM (XZ‑only) with octave cap and top‑frequency cap.
static HeightSample SampleRidgedFBM(float3 position, NoiseParameters np)
{
    float3 baseFreq = max(abs(np.initialFrequency), 1e-6f);
    float3 lac      = max(abs(np.lacunarity), 1.0f);
    uint   octaves  = min(max(np.numberOfOctaves, 1u), MTN_MAX_OCTAVES);

    // Cap highest octave frequency so wavelength >= 1/MTN_MAX_TOP_FREQ
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

        float4 n   = SimplexNoiseGrad(q).wxyz;   // x=value, y/z/w=grad components
        float  v   = n.x;
        float3 g   = float3(n.y * freq.x, 0.0f, n.w * freq.z);

        float  folded = abs(v);
        float  ridge  = max(offset - folded, 0.0f);
        ridge         = ridge * (0.85f + 0.15f * ridge); // tiny soften
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
    float2 dPos_dX = float2(1.0f, 0.0f);
    float2 dPos_dZ = float2(0.0f, 1.0f);

    pw = ApplyMountainWarp(pw, mp.warpParameters.x, mp.warpParameters.y, base.seed, dPos_dX, dPos_dZ);
    pw = ApplyMountainWarp(pw, mp.warpParameters.z, mp.warpParameters.w, base.seed + mp.warpSeed + 97u, dPos_dX, dPos_dZ);
    HeightSample sample = SampleRidgedFBM(pw, base);
    float2 warpedGrad = float2(sample.gradient.x, sample.gradient.z);
    float gradX = dot(warpedGrad, dPos_dX);
    float gradZ = dot(warpedGrad, dPos_dZ);
    sample.gradient = float3(gradX, sample.gradient.y, gradZ);
    return sample;
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

    // 0) Base normalized ridge field (XZ-only) with gradients
    HeightSample ridged = SampleGlobalMountainField(position, baseParameters, mp);

    // 1) Biome parameter blend (same semantics)
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

    // Guards
    blendExp   = max(blendExp, MTN_MIN_EXP);
    blendSteps = clamp(blendSteps, 1.0f, 24.0f);
    blendBias  = saturate(blendBias);

    // 2) Gentle sharpening (pow-safe)
    HeightSample sharp = HeightSamplePow(ridged, blendExp);

    // 3) Compute local slope (in degrees) to build a "plateau mask"
    //    We fade terracing out on steep slopes to avoid tier walls/spikes.
    float2 slopeRange = mp.extraParameters1.xy;  // (min,max) degrees
    // Slope estimates from both pre- and post-sharpening to avoid false "flat" reads.
    float slopeMagBase = length(ridged.gradient.xz);
    float slopeDegBase = degrees(atan(slopeMagBase));
    float plateauBase  = 1.0f - smoothstep(slopeRange.x, slopeRange.y, slopeDegBase);

    float slopeMagSharp = length(sharp.gradient.xz);
    float slopeDegSharp = degrees(atan(slopeMagSharp));
    float plateauSharp  = 1.0f - smoothstep(slopeRange.x, slopeRange.y, slopeDegSharp);

    float plateauMask = saturate(plateauBase * plateauSharp);
    g_plateauMask = plateauMask;

    // 4) Terracing (soft) but *slope-aware*: only strong on plateaus/gentle slopes
    HeightSample hardTerr = HeightSampleTerrace(sharp, blendSteps, blendBias);
    float terrRamp = saturate((blendSteps - 1.0f) / 8.0f); // 0..~1
    float terrStrength = terrRamp * plateauMask * plateauMask; // heavily bias toward truly flat areas
    HeightSample terraced = HeightSampleLerp(sharp, hardTerr, terrStrength);

    // 5) Optional secondary shaping via terrace power (>=1, slope‑safe)
    HeightSample shaped = HeightSamplePow(terraced, terracePow);

    // 6) Local slope limiter BEFORE amplitude: keep final slope bounded
    float gmag     = length(shaped.gradient.xz);
    float localAmp = blendAmp;
    if (gmag > 1e-5f)
    {
        float allowed = MTN_SLOPE_BUDGET / gmag;     // max amplitude at this point
        localAmp      = min(blendAmp, allowed);
    }

    // 7) Scale to meters; NO clamp after amplitude
    HeightSample meters = HeightSampleScale(shaped, localAmp);

    // 8) SDF
    valueAndGrad = HeightSampleToSdf(position, meters);

    // Debug mask (post-shaping, pre-scale)
    g_mountainMask = saturate(shaped.value);
}

#endif // TUNTENFISCH_VOXELS_MOUNTAINS
