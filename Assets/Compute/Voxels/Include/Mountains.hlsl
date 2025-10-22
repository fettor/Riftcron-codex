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

    float mixStrength = saturate(mountainParameters.mixStrength);
    uint availableBiomes = GetBiomeCount();
    uint biomeCount = min(mountainParameters.biomeCount, availableBiomes);
    float active = step(0.5f, (float)biomeCount);

    float amplitudeSum = 0.0f;
    float ridgeSharpnessSum = 0.0f;
    float3 frequencySum = 0.0f;
    float warpStrengthSum = 0.0f;
    float3 warpFrequencySum = 0.0f;
    float weightSum = 0.0f;

    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float mask = step((float)i, (float)biomeCount - 0.5f);
        float contribution = GetBiomeWeightMasked(i) * mask;
        amplitudeSum += mountainParameters.amplitude[i] * contribution;
        ridgeSharpnessSum += mountainParameters.ridgeSharpness[i] * contribution;
        frequencySum += GetMountainFrequency(i) * contribution;
        warpStrengthSum += mountainParameters.warpStrength[i] * contribution;
        warpFrequencySum += GetMountainWarpFrequency(i) * contribution;
        weightSum += contribution;
    }

    float invWeight = weightSum > 1e-4f ? rcp(weightSum) : 0.0f;
    float amplitudeTarget = amplitudeSum * invWeight;
    float ridgeSharpnessTarget = ridgeSharpnessSum * invWeight;
    float3 frequencyTarget = frequencySum * invWeight;
    float warpStrengthTarget = warpStrengthSum * invWeight;
    float3 warpFrequencyTarget = warpFrequencySum * invWeight;

    float biomeMix = mixStrength * active;
    float baseAmplitude = baseParameters.initialAmplitude;
    float amplitudeScale = lerp(1.0f, amplitudeTarget, biomeMix);
    float finalAmplitude = baseAmplitude * amplitudeScale;
    float3 finalFrequency = lerp(baseParameters.initialFrequency, frequencyTarget, biomeMix);
    float finalRidgeSharpness = lerp(1.0f, ridgeSharpnessTarget, biomeMix);
    float warpAttenuation = GetBiomeWarpAttenuation();
    float finalWarpStrength = lerp(0.0f, warpStrengthTarget, biomeMix) * warpAttenuation;
    float3 finalWarpFrequency = lerp(finalFrequency, warpFrequencyTarget, biomeMix);

    finalFrequency = max(finalFrequency, 1e-4f);
    finalWarpFrequency = max(finalWarpFrequency, 1e-4f);
    finalRidgeSharpness = max(finalRidgeSharpness, 1.0f);

    NoiseParameters warpParameters = baseParameters;
    warpParameters.noiseAxes = NoiseAxes::XYZ;
    warpParameters.initialAmplitude = finalWarpStrength;
    warpParameters.initialFrequency = finalWarpFrequency;
    float3 warpedPosition = position + EvaluateWarpOffset(position, warpParameters);

    NoiseParameters sampleParameters = baseParameters;
    sampleParameters.initialAmplitude = finalAmplitude;
    sampleParameters.initialFrequency = finalFrequency;
    sampleParameters.noiseAxes = NoiseAxes::XZ;
    sampleParameters.noiseType = NoiseType::Ridge;

    float4 sample = GenerateFBMNoise(warpedPosition, sampleParameters);

    float amplitudeMask = step(1e-4f, finalAmplitude);
    float amplitudeSafe = max(finalAmplitude, 1e-3f);
    float height = warpedPosition.y - sample.x;
    float normalizedHeight = saturate(height / amplitudeSafe) * amplitudeMask;
    float normalizedMask = step(1e-4f, normalizedHeight);
    float safeNorm = max(normalizedHeight, 1e-4f);
    float shapedNorm = pow(safeNorm, finalRidgeSharpness);
    float shapingFactor = finalRidgeSharpness * pow(safeNorm, max(finalRidgeSharpness - 1.0f, 0.0f));
    shapedNorm = lerp(normalizedHeight, shapedNorm, normalizedMask);
    shapingFactor = lerp(1.0f, shapingFactor, normalizedMask);

    float shapedHeight = shapedNorm * amplitudeSafe;
    float deltaHeight = (shapedHeight - height) * amplitudeMask;
    sample.x -= deltaHeight;
    sample.y *= shapingFactor;
    sample.w *= shapingFactor;
    sample.z = 1.0f;

    float dhdx = -sample.y;
    float dhdz = -sample.w;
    float slopeMagnitude = sqrt(dhdx * dhdx + dhdz * dhdz);
    float slopeAngleDeg = degrees(atan(slopeMagnitude));
    float2 plateauRange = mountainParameters.plateauSlopeRangeDeg;
    float plateau = 1.0f - smoothstep(plateauRange.x, plateauRange.y, slopeAngleDeg);

    g_mountainMask = saturate(shapedNorm * biomeMix);
    g_plateauMask = saturate(plateau);

    valueAndGrad = sample;
}

#endif // TUNTENFISCH_VOXELS_MOUNTAINS
