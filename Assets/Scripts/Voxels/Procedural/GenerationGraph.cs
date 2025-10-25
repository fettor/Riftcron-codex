using System;
using System.Collections.Generic;
using System.Linq;
using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.Materials;
using Tuntenfisch.World.Planning;
using UnityEngine;
using UnityEngine.Assertions;
using XNode;

namespace Tuntenfisch.Voxels.Procedural
{
    [CreateAssetMenu(fileName = "Generation Graph", menuName = "Voxels/Generation Graph")]
    public class GenerationGraph : NodeGraph
    {
        public event Action OnDirtied;
        public event Action OnLateDirtied;

        public List<GPUGenerationGraphNode> Nodes => m_nodes;

        [SerializeField]
        private List<GPUGenerationGraphNode> m_nodes;

        public void Rebuild()
        {
            int outputNodeIndex = GetOutputNodeIndex();

            Assert.IsTrue(outputNodeIndex != -1, "Invalid noise graph: Output node is missing.");

            m_nodes ??= new List<GPUGenerationGraphNode>();
            m_nodes.Clear();

            foreach (GenerationGraphNode node in IterateOverGraphInPreorder((GenerationGraphNode)nodes[outputNodeIndex]))
            {
                Matrix4x4 transformMatrix = Matrix4x4.identity;
                GPUNoiseParameters noiseParameters = new GPUNoiseParameters();
                GPUCSGPrimitive csgPrimitive = new GPUCSGPrimitive();
                MaterialIndex materialIndex = default;
                GPUCSGOperator csgOperator = new GPUCSGOperator();
                GPUBiomeMapParameters biomeMapParameters = default;
                GPUBiomeMixerParameters biomeMixerParameters = default;
                GPUMountainParameters mountainParameters = default;

                switch (node.GetNodeType())
                {
                    case NodeType.Transform:
                        TransformNode transformNode = (TransformNode)node;
                        transformMatrix = transformNode.TransformMatrix;
                        break;

                    case NodeType.DomainWarp:
                        DomainWarpNode domainWarpNode = (DomainWarpNode)node;
                        noiseParameters = domainWarpNode.NoiseParameters;
                        break;

                    case NodeType.BiomeMap:
                        BiomeMapNode biomeMapNode = (BiomeMapNode)node;
                        biomeMapParameters = BuildBiomeMapParameters(biomeMapNode);
                        break;

                    case NodeType.BiomeMixer:
                        BiomeMixerNode biomeMixerNode = (BiomeMixerNode)node;
                        biomeMixerParameters = BuildBiomeMixerParameters(biomeMixerNode);
                        break;

                    case NodeType.Noise:
                        NoiseNode noiseNode = (NoiseNode)node;
                        noiseParameters = noiseNode.NoiseParameters;
                        break;

                    case NodeType.Mountain:
                        MountainNode mountainNode = (MountainNode)node;
                        noiseParameters = mountainNode.BaseNoiseParameters;
                        mountainParameters = BuildMountainParameters(mountainNode);
                        break;

                    case NodeType.CSGPrimitive:
                        CSGPrimitiveNode csgPrimitiveNode = (CSGPrimitiveNode)node;
                        csgPrimitive = csgPrimitiveNode.CSGPrimitive;
                        break;

                    case NodeType.Material:
                        MaterialNode materialNode = (MaterialNode)node;
                        materialIndex = materialNode.MaterialIndex;
                        break;

                    case NodeType.CSGOperation:
                        CSGOperationNode csgOperationNode = (CSGOperationNode)node;
                        csgOperator = csgOperationNode.CSGOperator;
                        break;
                }
                m_nodes.Add(new GPUGenerationGraphNode(node.GetNodeType(), transformMatrix, noiseParameters, csgPrimitive, materialIndex, csgOperator, biomeMapParameters, biomeMixerParameters, mountainParameters));
            }
            OnDirtied?.Invoke();
            OnLateDirtied?.Invoke();
        }

        private IEnumerable<GenerationGraphNode> IterateOverGraphInPreorder(GenerationGraphNode node)
        {
            if (node.GetNodeType() == NodeType.Position)
            {
                return Enumerable.Repeat(node, 1);
            }
            else
            {
                NodePort[] inputs = node.Inputs.ToArray();

                IEnumerable<GenerationGraphNode> leftNodes = Enumerable.Empty<GenerationGraphNode>();
                IEnumerable<GenerationGraphNode> rightNodes = Enumerable.Empty<GenerationGraphNode>();

                if (inputs.Length > 0)
                {
                    leftNodes = IterateOverGraphInPreorder((GenerationGraphNode)inputs[0].Connection.node);
                }

                if (inputs.Length > 1)
                {
                    rightNodes = IterateOverGraphInPreorder((GenerationGraphNode)inputs[1].Connection.node);
                }

                return Enumerable.Concat(Enumerable.Concat(leftNodes, rightNodes), Enumerable.Repeat(node, 1));
            }
        }

        private int GetOutputNodeIndex() => nodes.FindIndex((node) => ((GenerationGraphNode)node).GetNodeType() == NodeType.Output);

        private static GPUBiomeMapParameters BuildBiomeMapParameters(BiomeMapNode node)
        {
            GPUBiomeMapParameters parameters = default;
            BiomeLibrary library = node.BiomeLibrary;

            if (library == null || library.BiomeCount == 0)
            {
                parameters.WeightGain = node.WeightGain;
                parameters.Padding = Vector2.zero;
                return parameters;
            }

            int count = library.BiomeCount;

            Vector4 temperatureCenter = Vector4.zero;
            Vector4 temperatureRange = Vector4.one;
            Vector4 moistureCenter = Vector4.zero;
            Vector4 moistureRange = Vector4.one;
            Vector4 weightSharpness = Vector4.one;

            for (int i = 0; i < count; ++i)
            {
                BiomeDefinition biome = library.GetBiome(i);
                BiomeClimateSettings climate = biome.Climate;
                temperatureCenter[i] = climate.TemperatureCenter;
                temperatureRange[i] = climate.TemperatureRange;
                moistureCenter[i] = climate.MoistureCenter;
                moistureRange[i] = climate.MoistureRange;
                weightSharpness[i] = climate.WeightSharpness;
            }

            parameters.TemperatureCenter = temperatureCenter;
            parameters.TemperatureRange = temperatureRange;
            parameters.MoistureCenter = moistureCenter;
            parameters.MoistureRange = moistureRange;
            parameters.WeightSharpness = weightSharpness;
            parameters.WeightGain = Mathf.Max(0.0f, node.WeightGain);
            parameters.BiomeCount = (uint)count;
            parameters.Padding = Vector2.zero;
            return parameters;
        }

        private static GPUBiomeMixerParameters BuildBiomeMixerParameters(BiomeMixerNode node)
        {
            GPUBiomeMixerParameters parameters = default;
            BiomeLibrary library = node.BiomeLibrary;

            if (library == null || library.BiomeCount == 0)
            {
                parameters.MixStrength = node.MixStrength;
                parameters.Padding = Vector2.zero;
                return parameters;
            }

            int count = library.BiomeCount;
            Vector4 baseAmplitude = Vector4.zero;
            Vector4 heightOffset = Vector4.zero;
            Vector4 baseFreqX = Vector4.zero;
            Vector4 baseFreqY = Vector4.zero;
            Vector4 baseFreqZ = Vector4.zero;
            Vector4 warpStrength = Vector4.zero;
            Vector4 warpFreqX = Vector4.zero;
            Vector4 warpFreqY = Vector4.zero;
            Vector4 warpFreqZ = Vector4.zero;

            for (int i = 0; i < count; ++i)
            {
                BiomeDefinition biome = library.GetBiome(i);
                BiomeTerrainParameters terrain = biome.Terrain;
                baseAmplitude[i] = terrain.BaseAmplitude;
                heightOffset[i] = terrain.HeightOffset;
                baseFreqX[i] = terrain.BaseFrequency.x;
                baseFreqY[i] = terrain.BaseFrequency.y;
                baseFreqZ[i] = terrain.BaseFrequency.z;
                warpStrength[i] = terrain.WarpStrength;
                warpFreqX[i] = terrain.WarpFrequency.x;
                warpFreqY[i] = terrain.WarpFrequency.y;
                warpFreqZ[i] = terrain.WarpFrequency.z;
            }

            parameters.BaseAmplitude = baseAmplitude;
            parameters.HeightOffset = heightOffset;
            parameters.BaseFrequencyX = baseFreqX;
            parameters.BaseFrequencyY = baseFreqY;
            parameters.BaseFrequencyZ = baseFreqZ;
            parameters.WarpStrength = warpStrength;
            parameters.WarpFrequencyX = warpFreqX;
            parameters.WarpFrequencyY = warpFreqY;
            parameters.WarpFrequencyZ = warpFreqZ;
            parameters.MixStrength = Mathf.Clamp01(node.MixStrength);
            parameters.BiomeCount = (uint)count;
            parameters.Padding = Vector2.zero;
            return parameters;
        }

        private static GPUMountainParameters BuildMountainParameters(MountainNode node)
        {
            GPUMountainParameters parameters = default;
            BiomeLibrary library = node.BiomeLibrary;

            Vector2 plateauRange = node.PlateauSlopeRange;
            float low = Mathf.Clamp(plateauRange.x, 0.0f, 90.0f);
            float high = Mathf.Clamp(Mathf.Max(plateauRange.y, low + 0.1f), 0.0f, 90.0f);
            parameters.BaseParameters = new Vector4(node.BaseAmplitude, node.BaseRemapExponent, node.BaseTerraceSteps, node.BaseTerraceBias);
            float largeWarpAmplitude = node.LargeWarpAmplitude;
            float largeWarpFrequency = node.LargeWarpFrequency;
            float detailWarpAmplitude = node.DetailWarpAmplitude;
            float detailWarpFrequency = node.DetailWarpFrequency;
            parameters.WarpParameters = new Vector4(largeWarpAmplitude, largeWarpFrequency, detailWarpAmplitude, detailWarpFrequency);
            parameters.ExtraParameters0 = new Vector4(Mathf.Clamp01(node.MixStrength), node.TerracePower, 0.0f, 0.0f);
            parameters.ExtraParameters1 = new Vector4(low, high, 0.0f, 0.0f);
            parameters.WarpSeed = node.DetailWarpSeedOffset;
            parameters.Padding = Vector2.zero;

            if (library == null || library.BiomeCount == 0)
            {
                float baseExponent = node.BaseRemapExponent;
                float baseSteps = node.BaseTerraceSteps;
                float baseBias = node.BaseTerraceBias;
                parameters.Amplitude = Vector4.zero;
                parameters.RemapExponent = new Vector4(baseExponent, baseExponent, baseExponent, baseExponent);
                parameters.TerraceSteps = new Vector4(baseSteps, baseSteps, baseSteps, baseSteps);
                parameters.TerraceBias = new Vector4(baseBias, baseBias, baseBias, baseBias);
                parameters.BiomeCount = 0u;
                return parameters;
            }

            int count = library.BiomeCount;
            Vector4 amplitude = Vector4.zero;
            Vector4 remapExponent = Vector4.one * node.BaseRemapExponent;
            Vector4 terraceSteps = Vector4.one * node.BaseTerraceSteps;
            Vector4 terraceBias = Vector4.one * node.BaseTerraceBias;

            for (int i = 0; i < count; ++i)
            {
                BiomeDefinition biome = library.GetBiome(i);
                BiomeMountainParameters mountains = biome.Terrain.Mountains;
                amplitude[i] = Mathf.Max(0.0f, mountains.Amplitude);
                remapExponent[i] = Mathf.Max(0.25f, mountains.RemapExponent);
                terraceSteps[i] = Mathf.Max(1.0f, mountains.TerraceSteps);
                terraceBias[i] = Mathf.Clamp01(mountains.TerraceBias);
            }

            parameters.Amplitude = amplitude;
            parameters.RemapExponent = remapExponent;
            parameters.TerraceSteps = terraceSteps;
            parameters.TerraceBias = terraceBias;
            parameters.BiomeCount = (uint)count;
            return parameters;
        }
    }
}
