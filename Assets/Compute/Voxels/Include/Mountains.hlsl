#ifndef TUNTENFISCH_VOXELS_MOUNTAINS
#define TUNTENFISCH_VOXELS_MOUNTAINS

#include "Assets/Compute/Voxels/Include/Noise.hlsl"

struct GPUMountainParameters
{
    float4 amplitude;         // Per-biome amplitude multipliers.
    float4 remapExponent;     // Per-biome remap exponent (pow).
    float4 terraceSteps;      // Per-biome terrace steps.
    float4 terraceBias;       // Per-biome terrace bias.
    float4 baseParameters;    // x: base amplitude, y: base exponent, z: base steps, w: base bias.
    float4 warpParameters;    // x: large warp amplitude, y: large warp frequency, z: detail warp amplitude, w: detail warp frequency.
    float4 extraParameters0;  // x: mix strength, y: terrace power, z: unused, w: unused.
    float4 extraParameters1;  // x: plateau slope min, y: plateau slope max, z: unused, w: unused.
    uint biomeCount;
    uint warpSeed;            // Additional seed offset for detail warp.
    float2 padding;
};

static float g_mountainMask = 0.0f;
static float g_plateauMask = 0.0f;

struct HeightSample
{
    float value;
    float3 gradient;
};

void ResetMountainDebug()
{
    g_mountainMask = 0.0f;
    g_plateauMask = 0.0f;
}

float GetMountainMask()
{
    return saturate(g_mountainMask);
}

float GetPlateauMask()
{
    return saturate(g_plateauMask);
}

static HeightSample MakeHeightSample(float value, float3 gradient)
{
    HeightSample s;
    s.value = value;
    s.gradient = gradient;
    return s;
}

static HeightSample HeightSampleScale(HeightSample sample, float scale)
{
    HeightSample result;
    float safeScale = max(scale, 0.0f);
    result.value = sample.value * safeScale;
    result.gradient = sample.gradient * safeScale;
    return result;
}

static HeightSample HeightSampleSaturate(HeightSample sample)
{
    float clampMask = step(0.0f, sample.value) * (1.0f - step(1.0f, sample.value));
    HeightSample result;
    result.value = saturate(sample.value);
    result.gradient = sample.gradient * clampMask;
    return result;
}

static HeightSample HeightSamplePow(HeightSample sample, float exponent)
{
    float safeExponent = max(exponent, 1.0f);       // never < 1: no 1/x^(1-e) blow-up near 0
    float safeValue    = max(sample.value, 1e-4f);

    HeightSample r;
    r.value = pow(safeValue, safeExponent);

    // dy/dx = e * x^(e-1)  -> cap to keep SDF slopes reasonable for DC
    float deriv = safeExponent * pow(safeValue, safeExponent - 1.0f);
    r.gradient  = sample.gradient * min(deriv, 1.5f);   // 1.5 is a good, stable cap
    return r;
}

static HeightSample HeightSampleTerraceSoft(HeightSample sample, float steps, float bias, float softness, float maxDeriv)
{
    float stepCount = max(steps, 1.0f);
    float biasClamped = saturate(bias);
    float soft = saturate(softness);

    if (stepCount <= 1.001f)
    {
        return sample;
    }

    float t = sample.value * stepCount;
    float cell = floor(t);
    float frac = t - cell;

    float range = max(1.0f - biasClamped, 0.20f);  // at least 20% of a step
    float normalized = saturate((frac - biasClamped) / range);
    float smooth = lerp(normalized, normalized * normalized * (3.0f - 2.0f * normalized), 1.0f - soft);

    float derivative = 0.0f;
    if (normalized > 0.0f && normalized < 1.0f)
    {
        derivative = 6.0f * normalized * (1.0f - normalized) / range;
    }
    derivative = min(derivative, maxDeriv);

    float terracedValue = (cell + smooth) / stepCount;
    HeightSample result;
    result.value = terracedValue;
    result.gradient = sample.gradient * derivative;
    return result;
}

static HeightSample HeightSampleTerrace(HeightSample s, float steps, float bias)
{
    float stepCount   = max(steps, 1.0f);
    float biasClamped = saturate(bias);
    if (stepCount <= 1.001f) return s;

    float t    = s.value * stepCount;
    float cell = floor(t);
    float frac = t - cell;

    // Ensure a minimum transition width to avoid razor-thin edges.
    float range = max(1.0f - biasClamped, 0.20f);      // <-- at least 20% of each step
    float n     = saturate((frac - biasClamped) / range);
    float smooth= n * n * (3.0f - 2.0f * n);

    // Derivative of smoothstep, clamped to avoid infinite slopes.
    float deriv = 0.0f;
    if (n > 0.0f && n < 1.0f)
        deriv = 6.0f * n * (1.0f - n) / range;
    deriv = min(deriv, 1.25f);                         // <-- derivative cap

    HeightSample r;
    r.value    = (cell + smooth) / stepCount;
    r.gradient = s.gradient * deriv;                   // scale only by safe derivative
    return r;
}

static float3 SampleSimplexDisplacement(float3 position, uint seed)
{
    float3 seedOffset = CalculateOctaveOffset(seed, 0u);
    float3 d;
    d.x = SimplexNoiseGrad(position + seedOffset + float3(0.0f, 0.0f, 0.0f)).w;
    d.y = SimplexNoiseGrad(position + seedOffset + float3(19.1f, 7.3f, 11.8f)).w;
    d.z = SimplexNoiseGrad(position + seedOffset + float3(-23.7f, 5.2f, 37.3f)).w;
    return d;
}

static float3 ApplyMountainWarp(float3 position, float amplitude, float frequency, uint seed)
{
    float ampMask = step(1e-4f, abs(amplitude));
    float freqMask = step(1e-6f, abs(frequency));
    float active = ampMask * freqMask;

    if (active < 0.5f)
    {
        return position;
    }

    float3 displacement = SampleSimplexDisplacement(float3(position.x, 0.0f, position.z) * frequency, seed);
    position.x += amplitude * displacement.x;
    position.z += amplitude * displacement.z;
    return position;
}

static HeightSample SampleRidgedFBM(float3 position, NoiseParameters noiseParameters)
{
    float3 baseFrequency = max(abs(noiseParameters.initialFrequency), 1e-5f);
    float3 lacunarity = max(abs(noiseParameters.lacunarity), 1.0f);
    float gain = max(noiseParameters.persistence, 1e-4f);
    float offset = max(noiseParameters.initialAmplitude, 0.0f);
    uint octaves = min(max(noiseParameters.numberOfOctaves, 1u), 7u);

    float amplitude = 0.5f;
    float3 frequency = float3(baseFrequency.x, 1.0f, baseFrequency.z);
    float sum = 0.0f;
    float3 gradient = 0.0f;

    for (uint octave = 0u; octave < octaves; ++octave)
    {
        float3 octaveOffset = CalculateOctaveOffset(noiseParameters.seed, octave);
        float3 samplePosition = float3(position.x, 0.0f, position.z) * frequency + octaveOffset;
        float4 noise = SimplexNoiseGrad(samplePosition).wxyz;

        float value = noise.x;
        float3 grad = float3(noise.y * frequency.x, 0.0f, noise.w * frequency.z);

        float folded = abs(value);
        float ridge = max(offset - folded, 0.0f);
        ridge = ridge * (0.85f + 0.15f * ridge);
        float ridgeValue = ridge * ridge;
        float signValue = value >= 0.0f ? 1.0f : -1.0f;
        float3 ridgeGradient = -2.0f * ridge * signValue * grad;

        sum += ridgeValue * amplitude;
        gradient += ridgeGradient * amplitude;

        float blendGain = lerp(gain, gain * ridgeValue, 0.5f);
        amplitude *= blendGain;
        frequency.x *= lacunarity.x;
        frequency.z *= lacunarity.z;
    }

    HeightSample sample = MakeHeightSample(sum, gradient);
    return HeightSampleSaturate(sample);
}

static HeightSample SampleGlobalMountainField(float3 position, NoiseParameters baseParameters, GPUMountainParameters mountainParameters)
{
    float3 warped = position;
    warped = ApplyMountainWarp(warped, mountainParameters.warpParameters.x, mountainParameters.warpParameters.y, baseParameters.seed);
    warped = ApplyMountainWarp(warped, mountainParameters.warpParameters.z, mountainParameters.warpParameters.w, baseParameters.seed + mountainParameters.warpSeed + 97u);

    return SampleRidgedFBM(warped, baseParameters);
}

static float4 HeightSampleToSdf(float3 position, HeightSample height)
{
    float value = position.y - height.value;
    float3 gradient = float3(-height.gradient.x, 1.0f, -height.gradient.z);
    return float4(value, gradient);
}

void EvaluateMountain(float3 position, NoiseParameters baseParameters, GPUMountainParameters mountainParameters, out float4 valueAndGrad)
{
    ResetMountainDebug();

    uint availableBiomes = GetBiomeCount();
    uint biomeCount = min(mountainParameters.biomeCount, availableBiomes);
    float mixStrength = saturate(mountainParameters.extraParameters0.x);
    float terracePower = max(mountainParameters.extraParameters0.y, 1e-3f);
    float terracePower = max(mountainParameters.extraParameters0.y, 1.0f);
    float2 plateauSlopeRange = mountainParameters.extraParameters1.xy;

    HeightSample ridged = SampleGlobalMountainField(position, baseParameters, mountainParameters);

    float baseAmplitude = max(mountainParameters.baseParameters.x, 0.0f);
    float baseExponent = max(mountainParameters.baseParameters.y, 1e-3f);
    float baseSteps = max(mountainParameters.baseParameters.z, 1.0f);
    float baseBias = saturate(mountainParameters.baseParameters.w);

    float blendedAmplitude = baseAmplitude;
    float blendedExponent = baseExponent;
    float blendedSteps = baseSteps;
    float blendedBias = baseBias;

    if (mixStrength > 1e-3f && biomeCount > 0u)
    {
        float weightSum = 0.0f;
        float amplitudeSum = 0.0f;
        float exponentSum = 0.0f;
        float stepsSum = 0.0f;
        float biasSum = 0.0f;

        [unroll]
        for (uint i = 0u; i < 4u; ++i)
        {
            float enabled = step((float)i, (float)biomeCount - 0.5f);
            float weight = GetBiomeWeightMasked(i) * enabled;

            if (weight <= 1e-4f)
            {
                continue;
            }

            amplitudeSum += mountainParameters.amplitude[i] * weight;
            exponentSum += mountainParameters.remapExponent[i] * weight;
            stepsSum += mountainParameters.terraceSteps[i] * weight;
            biasSum += mountainParameters.terraceBias[i] * weight;
            weightSum += weight;
        }

        if (weightSum > 1e-4f)
        {
            float invWeight = rcp(weightSum);
            blendedAmplitude = lerp(baseAmplitude, amplitudeSum * invWeight, mixStrength);
            blendedExponent = lerp(baseExponent, exponentSum * invWeight, mixStrength);
            blendedSteps = lerp(baseSteps, stepsSum * invWeight, mixStrength);
            blendedBias = lerp(baseBias, biasSum * invWeight, mixStrength);
        }
    }
    
    blendedSteps = clamp(blendedSteps, 1.0f, 24.0f);  // avoid micro-steps
    blendedBias  = saturate(blendedBias);

    HeightSample terraced = HeightSampleTerraceSoft(ridged, blendedSteps, blendedBias, 0.35f, 1.25f);
    HeightSample shaped = HeightSamplePow(terraced, terracePower);
    blendedExponent = max(blendedExponent, 1.0f);
    HeightSample remapped = HeightSamplePow(shaped, blendedExponent);
    HeightSample normalized = HeightSampleSaturate(remapped);
    HeightSample finalHeight = HeightSampleScale(normalized, blendedAmplitude);
    

    valueAndGrad = HeightSampleToSdf(position, finalHeight);

    float dhdx = -valueAndGrad.y;
    float dhdz = -valueAndGrad.w;
    float slopeMagnitude = sqrt(dhdx * dhdx + dhdz * dhdz);
    float slopeAngleDeg = degrees(atan(slopeMagnitude));
    float plateau = 1.0f - smoothstep(plateauSlopeRange.x, plateauSlopeRange.y, slopeAngleDeg);

    g_mountainMask = saturate(finalHeight.value);
    g_plateauMask = saturate(plateau);
}

#endif // TUNTENFISCH_VOXELS_MOUNTAINS
