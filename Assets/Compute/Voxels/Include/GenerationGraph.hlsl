#ifndef TUNTENFISCH_VOXELS_GENERATION_GRAPH
#define TUNTENFISCH_VOXELS_GENERATION_GRAPH

#include "Assets/Compute/Include/Enumeration.hlsl"
#include "Assets/Compute/Voxels/Include/Noise.hlsl"
#include "Assets/Compute/Voxels/Include/Voxel.hlsl"
#include "Assets/Compute/Voxels/Include/Biomes.hlsl"
#include "Assets/Compute/Voxels/Include/Mountains.hlsl"

ENUM NodeType
{
    static const uint Position = 0;
    static const uint Transform = 1;
    static const uint DomainWarp = 2;
    static const uint Noise = 3;
    static const uint CSGPrimitive = 4;
    static const uint Material = 5;
    static const uint CSGOperation = 6;
    static const uint Output = 7;
    static const uint BiomeMap = 8;
    static const uint BiomeMixer = 9;
    static const uint Mountain = 10;
};

struct GenerationGraphNode
{
    uint nodeType;
    // Below data is populated depending on the type of node.
    float4x4 transformMatrix;
    NoiseParameters noiseParameters;
    CSGPrimitive csgPrimitive;
    uint materialIndex;
    CSGOperator csgOperator;
    GPUBiomeMapParameters biomeMapParameters;
    GPUBiomeMixerParameters biomeMixerParameters;
    GPUMountainParameters mountainParameters;
};

StructuredBuffer<GenerationGraphNode> generationGraphNodes;

uint numberOfGenerationGraphNodes;

struct GenerationGraphStack
{
    Voxel buffer[2];
    uint count;

    void PushVoxel(Voxel voxel)
    {
        [branch]
        switch(count++)
        {
            case 0:
                buffer[0] = voxel;
                break;

            default:
                buffer[1] = voxel;
                break;
        }
    }

    void PushValueAndGradient(float4 valueAndGradient)
    {
        PushVoxel(Voxel::Create(valueAndGradient, 0));
    }

    void PushPosition(float3 position)
    {
        PushVoxel(Voxel::Create(float4(0.0f, position), 0));
    }

    Voxel PopVoxel()
    {
        return buffer[--count];
    }

    float4 PopValueAndGradient()
    {
        return PopVoxel().valueAndGradient;
    }

    float3 PopPosition()
    {
        return PopVoxel().GetGradient();
    }

    static GenerationGraphStack Create()
    {
        GenerationGraphStack stack;
        stack.count = 0;

        return stack;
    }
};

Voxel EvaluateGenerationGraph(float3 position)
{
    Voxel voxel;
    GenerationGraphStack stack = GenerationGraphStack::Create();
    ResetBiomeContext();
    ResetMountainDebug();

    for (uint nodeIndex = 0; nodeIndex < numberOfGenerationGraphNodes; nodeIndex++)
    {
        GenerationGraphNode node = generationGraphNodes[nodeIndex];

        [branch]
        switch(node.nodeType)
        {
            case NodeType::Position:
                stack.PushPosition(position);
                break;

            case NodeType::Transform:
                stack.PushPosition(mul(node.transformMatrix, float4(stack.PopPosition(), 1.0f)).xyz);
                break;

            case NodeType::DomainWarp:
            {
                float3 warpedPosition = stack.PopPosition();
                NoiseParameters baseParameters = node.noiseParameters;
                float3 baseOffset = EvaluateWarpOffset(warpedPosition, baseParameters);
                float warpMix = GetBiomeWarpMixStrength();
                uint biomeCount = GetBiomeCount();

                if (warpMix > 1e-3f && biomeCount > 0u)
                {
                    float attenuation = GetBiomeWarpAttenuation();
                    float3 biomeOffset = 0.0f;
                    float contributionSum = 0.0f;

                    [unroll]
                    for (uint i = 0u; i < 4u; ++i)
                    {
                        float contribution = GetBiomeWeightMasked(i);

                        if (contribution <= 1e-4f)
                        {
                            continue;
                        }

                        NoiseParameters biomeParameters = baseParameters;
                        biomeParameters.initialAmplitude = GetBiomeWarpStrength(i) * attenuation;
                        biomeParameters.initialFrequency = GetBiomeWarpFrequency(i);
                        float3 offset = EvaluateWarpOffset(warpedPosition, biomeParameters);
                        biomeOffset += offset * contribution;
                        contributionSum += contribution;
                    }

                    if (contributionSum > 1e-4f)
                    {
                        biomeOffset /= contributionSum;
                    }
                    else
                    {
                        biomeOffset = baseOffset;
                    }

                    float3 finalOffset = lerp(baseOffset, biomeOffset, warpMix);
                    stack.PushPosition(warpedPosition + finalOffset);
                }
                else
                {
                    stack.PushPosition(warpedPosition + baseOffset);
                }
                break;
            }

            case NodeType::BiomeMap:
            {
                float3 samplePosition = stack.PopPosition();
                float2 uv = samplePosition.xz * climateUVScale + climateUVOffset;
                float temperature = SampleClimate(climateTemperature, uv);
                float moisture = SampleClimate(climateMoisture, uv);
                float4 weights = ComputeBiomeWeights(temperature, moisture, node.biomeMapParameters);
                StoreBiomeWeights(weights, node.biomeMapParameters.biomeCount);
                stack.PushPosition(samplePosition);
                break;
            }

            case NodeType::BiomeMixer:
            {
                float3 passthrough = stack.PopPosition();
                StoreBiomeMix(node.biomeMixerParameters);
                stack.PushPosition(passthrough);
                break;
            }

            case NodeType::Noise:
            {
                float3 noisePosition = stack.PopPosition();
                NoiseParameters baseParameters = node.noiseParameters;
                float4 baseValue = GenerateFBMNoise(noisePosition, baseParameters);
                float mixStrength = GetBiomeMixStrength();
                uint biomeCount = GetBiomeCount();

                if (mixStrength > 1e-3f && biomeCount > 0u)
                {
                    float4 blendedValue = 0.0f;
                    float weightSum = 0.0f;

                    [unroll]
                    for (uint i = 0u; i < 4u; ++i)
                    {
                        float contribution = GetBiomeWeightMasked(i);

                        if (contribution <= 1e-4f)
                        {
                            continue;
                        }

                        NoiseParameters biomeParameters = baseParameters;
                        biomeParameters.initialAmplitude = GetBiomeBaseAmplitude(i);
                        biomeParameters.initialFrequency = GetBiomeBaseFrequency(i);
                        float4 sample = GenerateFBMNoise(noisePosition, biomeParameters);
                        blendedValue += sample * contribution;
                        weightSum += contribution;
                    }

                    if (weightSum > 1e-4f)
                    {
                        blendedValue /= weightSum;
                        baseValue = lerp(baseValue, blendedValue, mixStrength);
                    }
                }

                baseValue.x += GetBiomeHeightOffset();
                stack.PushValueAndGradient(baseValue);
                break;
            }

            case NodeType::Mountain:
            {
                float3 mountainPosition = stack.PopPosition();
                NoiseParameters baseParameters = node.noiseParameters;
                baseParameters.noiseAxes = NoiseAxes::XZ;
                baseParameters.noiseType = NoiseType::Ridge;
                float4 mountainValue;
                EvaluateMountain(mountainPosition, baseParameters, node.mountainParameters, mountainValue);
                stack.PushValueAndGradient(mountainValue);
                break;
            }

            case NodeType::CSGPrimitive:
                stack.PushValueAndGradient(EvaluateCSGPrimitive(stack.PopPosition(), node.csgPrimitive));
                break;

            case NodeType::Material:
                stack.PushVoxel(Voxel::Create(stack.PopValueAndGradient(), node.materialIndex));
                break;

            case NodeType::CSGOperation:
                Voxel rhs = stack.PopVoxel();
                Voxel lhs = stack.PopVoxel();
                stack.PushVoxel(ApplyCSGOperator(lhs, rhs, node.csgOperator));
                break;

            case NodeType::Output:
                voxel = stack.PopVoxel();
                break;
        }
    }

    return voxel;
}

#endif
