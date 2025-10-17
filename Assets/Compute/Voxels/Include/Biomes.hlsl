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
    float3 baseFrequency;
    float baseAmplitude;
    float3 warpFrequency;
    float warpStrength;
    float heightOffset;
    float mixStrength;
    uint biomeCount;
    uint active;
};

static BiomeContext g_biomeContext;

void ResetBiomeContext()
{
    g_biomeContext.weights = float4(1.0f, 0.0f, 0.0f, 0.0f);
    g_biomeContext.baseFrequency = 0.0f;
    g_biomeContext.baseAmplitude = 0.0f;
    g_biomeContext.warpFrequency = 0.0f;
    g_biomeContext.warpStrength = 0.0f;
    g_biomeContext.heightOffset = 0.0f;
    g_biomeContext.mixStrength = 0.0f;
    g_biomeContext.biomeCount = 0u;
    g_biomeContext.active = 0u;
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

void StoreBiomeMix(GPUBiomeMixerParameters parameters)
{
    float mixStrength = saturate(parameters.mixStrength);
    float active = step(1e-4f, mixStrength) * step(0.5f, (float)parameters.biomeCount);

    float4 weights = g_biomeContext.weights;

    g_biomeContext.baseAmplitude = dot(weights, parameters.baseAmplitude);
    g_biomeContext.heightOffset = dot(weights, parameters.heightOffset);
    g_biomeContext.baseFrequency = float3(
        dot(weights, parameters.baseFrequencyX),
        dot(weights, parameters.baseFrequencyY),
        dot(weights, parameters.baseFrequencyZ));
    g_biomeContext.warpStrength = dot(weights, parameters.warpStrength);
    g_biomeContext.warpFrequency = float3(
        dot(weights, parameters.warpFrequencyX),
        dot(weights, parameters.warpFrequencyY),
        dot(weights, parameters.warpFrequencyZ));
    g_biomeContext.mixStrength = mixStrength * active;
    g_biomeContext.active = (uint)step(0.5f, g_biomeContext.mixStrength);
}

float3 GetBiomeBaseFrequency()
{
    return g_biomeContext.baseFrequency;
}

float GetBiomeBaseAmplitude()
{
    return g_biomeContext.baseAmplitude;
}

float3 GetBiomeWarpFrequency()
{
    return g_biomeContext.warpFrequency;
}

float GetBiomeWarpStrength()
{
    return g_biomeContext.warpStrength;
}

float GetBiomeHeightOffset()
{
    return g_biomeContext.heightOffset * g_biomeContext.mixStrength;
}

float GetBiomeMixStrength()
{
    return g_biomeContext.mixStrength;
}

float SampleClimate(Texture2D<float4> climateTex, float2 uv)
{
    return climateTex.SampleLevel(samplerLinearClamp, uv, 0.0f).x;
}

#endif // TUNTENFISCH_VOXELS_BIOMES
