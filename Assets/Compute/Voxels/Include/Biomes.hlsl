#ifndef TUNTENFISCH_VOXELS_BIOMES
#define TUNTENFISCH_VOXELS_BIOMES

struct GPUBiomeMapParameters
{
    float4 temperatureCenter;
    float4 temperatureRange;
    float4 moistureCenter;
    float4 moistureRange;
    float4 weightSharpness;
    float weightGain;
    uint biomeCount;
    float2 padding;
};

struct GPUBiomeMixerParameters
{
    float4 baseAmplitude;
    float4 heightOffset;
    float4 baseFrequencyX;
    float4 baseFrequencyY;
    float4 baseFrequencyZ;
    float4 warpStrength;
    float4 warpFrequencyX;
    float4 warpFrequencyY;
    float4 warpFrequencyZ;
    float mixStrength;
    uint biomeCount;
    float2 padding;
};

struct BiomeContext
{
    float4 weights;
    float4 baseAmplitudeSet;
    float4 heightOffsetSet;
    float4 baseFrequencyXSet;
    float4 baseFrequencyYSet;
    float4 baseFrequencyZSet;
    float4 warpStrengthSet;
    float4 warpFrequencyXSet;
    float4 warpFrequencyYSet;
    float4 warpFrequencyZSet;
    float mixStrength;
    float warpAttenuation;
    float primaryWeight;
    float secondaryWeight;
    uint biomeCount;
    uint active;
};

static BiomeContext g_biomeContext;

void ResetBiomeContext()
{
    g_biomeContext.weights = float4(1.0f, 0.0f, 0.0f, 0.0f);
    g_biomeContext.baseAmplitudeSet = 0.0f;
    g_biomeContext.heightOffsetSet = 0.0f;
    g_biomeContext.baseFrequencyXSet = 0.0f;
    g_biomeContext.baseFrequencyYSet = 0.0f;
    g_biomeContext.baseFrequencyZSet = 0.0f;
    g_biomeContext.warpStrengthSet = 0.0f;
    g_biomeContext.warpFrequencyXSet = 0.0f;
    g_biomeContext.warpFrequencyYSet = 0.0f;
    g_biomeContext.warpFrequencyZSet = 0.0f;
    g_biomeContext.mixStrength = 0.0f;
    g_biomeContext.warpAttenuation = 0.0f;
    g_biomeContext.primaryWeight = 0.0f;
    g_biomeContext.secondaryWeight = 0.0f;
    g_biomeContext.biomeCount = 0u;
    g_biomeContext.active = 0u;
}

float GetComponent(float4 value, uint index)
{
    if (index == 0u) return value.x;
    if (index == 1u) return value.y;
    if (index == 2u) return value.z;
    return value.w;
}

float3 GetFrequency(float4 x, float4 y, float4 z, uint index)
{
    return float3(GetComponent(x, index), GetComponent(y, index), GetComponent(z, index));
}

float EvaluateAxisWeight(float value, float center, float range)
{
    float width = max(range, 1e-3f);
    float distance = abs(value - center);
    float normalized = saturate(1.0f - distance / width);
    return normalized * normalized * (3.0f - 2.0f * normalized);
}

float4 ComputeBiomeWeights(float temperature, float moisture, GPUBiomeMapParameters parameters)
{
    float4 weights = 0.0f;

    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float active = step((float)i, (float)parameters.biomeCount - 0.5f);
        float tempWeight = EvaluateAxisWeight(temperature, parameters.temperatureCenter[i], parameters.temperatureRange[i]);
        float moistureWeight = EvaluateAxisWeight(moisture, parameters.moistureCenter[i], parameters.moistureRange[i]);
        float baseWeight = tempWeight * moistureWeight;
        baseWeight = max(baseWeight, 1e-6f);
        float sharpness = parameters.weightSharpness[i];
        float weight = exp(log(baseWeight) * sharpness);
        weights[i] = weight * active;
    }

    float gain = max(parameters.weightGain, 1e-3f);
    float4 safeWeights = max(weights, 1e-6f);
    weights = exp(log(safeWeights) * gain);

    float normalization = weights.x + weights.y + weights.z + weights.w;
    normalization = max(normalization, 1e-5f);
    return weights / normalization;
}

void StoreBiomeWeights(float4 weights, uint biomeCount)
{
    g_biomeContext.weights = weights;
    g_biomeContext.biomeCount = biomeCount;
    g_biomeContext.active = biomeCount > 0u ? 1u : 0u;
}

float4 GetBiomeWeights()
{
    return g_biomeContext.weights;
}

uint GetBiomeCount()
{
    return g_biomeContext.biomeCount;
}

float GetBiomeWeight(uint index)
{
    return GetComponent(g_biomeContext.weights, index);
}

float GetBiomeWeightMasked(uint index)
{
    float active = step((float)index, (float)g_biomeContext.biomeCount - 0.5f);
    return GetBiomeWeight(index) * active;
}

float GetBiomeBaseAmplitude(uint index)
{
    return GetComponent(g_biomeContext.baseAmplitudeSet, index);
}

float3 GetBiomeBaseFrequency(uint index)
{
    return float3(
        GetComponent(g_biomeContext.baseFrequencyXSet, index),
        GetComponent(g_biomeContext.baseFrequencyYSet, index),
        GetComponent(g_biomeContext.baseFrequencyZSet, index));
}

float GetBiomeHeightOffset(uint index)
{
    return GetComponent(g_biomeContext.heightOffsetSet, index);
}

float3 GetBiomeWarpFrequency(uint index)
{
    return float3(
        GetComponent(g_biomeContext.warpFrequencyXSet, index),
        GetComponent(g_biomeContext.warpFrequencyYSet, index),
        GetComponent(g_biomeContext.warpFrequencyZSet, index));
}

float GetBiomeWarpStrength(uint index)
{
    return GetComponent(g_biomeContext.warpStrengthSet, index);
}

void StoreBiomeMix(GPUBiomeMixerParameters parameters)
{
    float mixStrength = saturate(parameters.mixStrength);
    float active = step(1e-4f, mixStrength) * step(0.5f, (float)parameters.biomeCount);

    g_biomeContext.baseAmplitudeSet = parameters.baseAmplitude;
    g_biomeContext.heightOffsetSet = parameters.heightOffset;
    g_biomeContext.baseFrequencyXSet = parameters.baseFrequencyX;
    g_biomeContext.baseFrequencyYSet = parameters.baseFrequencyY;
    g_biomeContext.baseFrequencyZSet = parameters.baseFrequencyZ;
    g_biomeContext.warpStrengthSet = parameters.warpStrength;
    g_biomeContext.warpFrequencyXSet = parameters.warpFrequencyX;
    g_biomeContext.warpFrequencyYSet = parameters.warpFrequencyY;
    g_biomeContext.warpFrequencyZSet = parameters.warpFrequencyZ;

    float4 weights = g_biomeContext.weights;
    uint biomeCount = min(parameters.biomeCount, 4u);

    float primaryWeight = 0.0f;
    float secondaryWeight = 0.0f;

    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float enabled = step((float)i, (float)biomeCount - 0.5f);
        float weight = GetComponent(weights, i) * enabled;

        if (weight > primaryWeight)
        {
            secondaryWeight = primaryWeight;
            primaryWeight = weight;
        }
        else if (weight > secondaryWeight)
        {
            secondaryWeight = weight;
        }
    }

    float dominance = saturate(primaryWeight - secondaryWeight);
    float attenuation = smoothstep(0.15f, 0.55f, dominance) * active;

    g_biomeContext.mixStrength = mixStrength * active;
    g_biomeContext.warpAttenuation = attenuation;
    g_biomeContext.primaryWeight = primaryWeight;
    g_biomeContext.secondaryWeight = secondaryWeight;
    g_biomeContext.biomeCount = biomeCount;
    g_biomeContext.active = biomeCount > 0u ? 1u : 0u;
}

float3 GetBiomeBaseFrequency()
{
    float3 frequency = 0.0f;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float contribution = GetBiomeWeightMasked(i);
        if (contribution <= 1e-6f)
        {
            continue;
        }

        frequency += contribution * GetBiomeBaseFrequency(i);
    }

    return frequency;
}

float GetBiomeBaseAmplitude()
{
    float amplitude = 0.0f;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float contribution = GetBiomeWeightMasked(i);
        amplitude += contribution * GetBiomeBaseAmplitude(i);
    }

    return amplitude;
}

float GetBiomeWarpAttenuation()
{
    return g_biomeContext.warpAttenuation;
}

float3 GetBiomeWarpFrequency()
{
    float3 frequency = 0.0f;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float contribution = GetBiomeWeightMasked(i);
        if (contribution <= 1e-6f)
        {
            continue;
        }

        frequency += contribution * GetBiomeWarpFrequency(i);
    }

    return frequency;
}

float GetBiomeWarpStrength()
{
    float strength = 0.0f;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float contribution = GetBiomeWeightMasked(i);
        strength += contribution * GetBiomeWarpStrength(i);
    }

    return strength * g_biomeContext.warpAttenuation;
}

float GetBiomeHeightOffset()
{
    float offset = 0.0f;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float contribution = GetBiomeWeightMasked(i);
        offset += contribution * GetBiomeHeightOffset(i);
    }

    return offset * g_biomeContext.mixStrength;
}

float GetBiomeMixStrength()
{
    return g_biomeContext.mixStrength;
}

float GetBiomeWarpMixStrength()
{
    return g_biomeContext.mixStrength * g_biomeContext.warpAttenuation;
}

float GetBiomePrimaryWeight()
{
    return g_biomeContext.primaryWeight;
}

float GetBiomeSecondaryWeight()
{
    return g_biomeContext.secondaryWeight;
}

float GetBiomeDominance()
{
    return saturate(g_biomeContext.primaryWeight - g_biomeContext.secondaryWeight);
}

float SampleClimate(Texture2D<float4> climateTex, float2 uv)
{
    return climateTex.SampleLevel(samplerLinearClamp, uv, 0.0f).x;
}

#endif // TUNTENFISCH_VOXELS_BIOMES
