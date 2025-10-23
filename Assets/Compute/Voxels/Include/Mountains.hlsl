#ifndef TUNTENFISCH_VOXELS_MOUNTAINS
#define TUNTENFISCH_VOXELS_MOUNTAINS

struct GPUMountainParameters
{
    float4 amplitude;
    float4 ridgeSharpness;
    float4 frequencyX;
    float4 frequencyY;
    float4 frequencyZ;
    float4 warpStrength;
    float4 warpFrequencyX;
    float4 warpFrequencyY;
    float4 warpFrequencyZ;
    float mixStrength;
    uint biomeCount;
    float2 plateauSlopeRangeDeg;
};

static GPUMountainParameters g_activeMountainParameters;
static float g_mountainMask = 0.0f;
static float g_plateauMask = 0.0f;

struct MountainSample
{
    float4 valueAndGrad;
    float ridgeMask;
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

MountainSample SampleMountainLayer(float3 position, NoiseParameters baseParameters, float amplitude, float3 frequency, float warpStrength, float3 warpFrequency, float ridgeSharpness)
{
    MountainSample result;
    float3 warpedPosition = position;

    if (warpStrength > 1e-3f)
    {
        NoiseParameters warpParameters = baseParameters;
        warpParameters.noiseAxes = NoiseAxes::XYZ;
        warpParameters.initialAmplitude = warpStrength;
        warpParameters.initialFrequency = max(abs(warpFrequency), 1e-4f);
        warpedPosition += EvaluateWarpOffset(position, warpParameters);
    }

    NoiseParameters sampleParameters = baseParameters;
    sampleParameters.initialAmplitude = max(amplitude, 1e-3f);
    sampleParameters.initialFrequency = max(abs(frequency), 1e-4f);
    sampleParameters.noiseAxes = NoiseAxes::XZ;
    sampleParameters.noiseType = NoiseType::Ridge;

    float4 sample = GenerateFBMNoise(warpedPosition, sampleParameters);

    float amplitudeMask = step(1e-4f, amplitude);
    float amplitudeSafe = max(amplitude, 1e-3f);
    float height = warpedPosition.y - sample.x;
    float normalizedHeight = saturate(height / amplitudeSafe) * amplitudeMask;
    float ridgePower = max(ridgeSharpness, 1.0f);
    float safeNorm = max(normalizedHeight, 1e-4f);

    float shapedNormRaw = pow(safeNorm, ridgePower);
    float shapedNorm = lerp(normalizedHeight, shapedNormRaw, amplitudeMask);
    float slopeMultiplier = lerp(1.0f, ridgePower * pow(safeNorm, ridgePower - 1.0f), amplitudeMask);

    float shapedHeight = shapedNorm * amplitudeSafe;
    float deltaHeight = (shapedHeight - height) * amplitudeMask;
    sample.x -= deltaHeight;
    sample.yzw *= slopeMultiplier;

    result.valueAndGrad = sample;
    result.ridgeMask = saturate(shapedNorm);
    return result;
}

float3 GetMountainFrequency(uint index)
{
    return float3(
        g_activeMountainParameters.frequencyX[index],
        g_activeMountainParameters.frequencyY[index],
        g_activeMountainParameters.frequencyZ[index]);
}

float3 GetMountainWarpFrequency(uint index)
{
    return float3(
        g_activeMountainParameters.warpFrequencyX[index],
        g_activeMountainParameters.warpFrequencyY[index],
        g_activeMountainParameters.warpFrequencyZ[index]);
}

void EvaluateMountain(float3 position, NoiseParameters baseParameters, GPUMountainParameters mountainParameters, out float4 valueAndGrad)
{
    g_activeMountainParameters = mountainParameters;
    ResetMountainDebug();

    uint availableBiomes = GetBiomeCount();
    uint biomeCount = min(mountainParameters.biomeCount, availableBiomes);
    float active = step(0.5f, (float)biomeCount);
    float mixStrength = saturate(mountainParameters.mixStrength) * active;

    MountainSample baseSample = SampleMountainLayer(position, baseParameters, baseParameters.initialAmplitude, baseParameters.initialFrequency, 0.0f, baseParameters.initialFrequency, 1.0f);
    float4 finalValue = baseSample.valueAndGrad;
    float finalMask = baseSample.ridgeMask;

    float dominance = mixStrength > 1e-3f ? GetBiomeDominance() : 0.0f;
    float warpAttenuation = GetBiomeWarpAttenuation();

    if (mixStrength > 1e-3f && biomeCount > 0u)
    {
        float4 blendedValue = 0.0f;
        float blendedMask = 0.0f;
        float weightSum = 0.0f;

        [unroll]
        for (uint i = 0u; i < 4u; ++i)
        {
            float enabled = step((float)i, (float)biomeCount - 0.5f);
            float weight = GetBiomeWeightMasked(i) * enabled;

            if (weight <= 1e-4f)
            {
                continue;
            }

            float amplitude = mountainParameters.amplitude[i];
            float ridgeSharpness = max(mountainParameters.ridgeSharpness[i], 1.0f);
            float3 frequency = GetMountainFrequency(i);
            float warpStrength = mountainParameters.warpStrength[i] * warpAttenuation * dominance;
            float3 warpFrequency = GetMountainWarpFrequency(i);

            MountainSample sample = SampleMountainLayer(position, baseParameters, amplitude, frequency, warpStrength, warpFrequency, ridgeSharpness);
            blendedValue += sample.valueAndGrad * weight;
            blendedMask += sample.ridgeMask * weight;
            weightSum += weight;
        }

        if (weightSum > 1e-4f)
        {
            float invWeight = rcp(weightSum);
            blendedValue *= invWeight;
            blendedMask *= invWeight;
            finalValue = lerp(baseSample.valueAndGrad, blendedValue, mixStrength);
            finalMask = lerp(baseSample.ridgeMask, blendedMask, mixStrength);
        }
    }

    float dhdx = -finalValue.y;
    float dhdz = -finalValue.w;
    float slopeMagnitude = sqrt(dhdx * dhdx + dhdz * dhdz);
    float slopeAngleDeg = degrees(atan(slopeMagnitude));
    float2 plateauRange = mountainParameters.plateauSlopeRangeDeg;
    float plateau = 1.0f - smoothstep(plateauRange.x, plateauRange.y, slopeAngleDeg);

    g_mountainMask = saturate(finalMask);
    g_plateauMask = saturate(plateau);

    valueAndGrad = finalValue;
}

#endif // TUNTENFISCH_VOXELS_MOUNTAINS
