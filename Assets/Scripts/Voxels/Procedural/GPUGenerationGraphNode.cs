using System;
using System.Runtime.InteropServices;
using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.Materials;
using UnityEngine;

namespace Tuntenfisch.Voxels.Procedural
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUGenerationGraphNode
    {
        public static int SizeInBytes => s_sizeInBytes;

        public NodeType NodeType => m_nodeType;
        public Matrix4x4 TransformMatrix => m_transformMatrix;
        public GPUNoiseParameters NoiseParameters => m_noiseParameters;
        public GPUCSGPrimitive CSGPrimitive => m_csgPrimitive;
        public GPUCSGOperator CSGOperator => m_csgOperator;
        public GPUBiomeMapParameters BiomeMapParameters => m_biomeMapParameters;
        public GPUBiomeMixerParameters BiomeMixerParameters => m_biomeMixerParameters;
        public GPUMountainParameters MountainParameters => m_mountainParameters;

        private readonly static int s_sizeInBytes = Marshal.SizeOf<GPUGenerationGraphNode>();

        [SerializeField]
        private NodeType m_nodeType;
        [SerializeField]
        private Matrix4x4 m_transformMatrix;
        [SerializeField]
        private GPUNoiseParameters m_noiseParameters;
        [SerializeField]
        private GPUCSGPrimitive m_csgPrimitive;
        [SerializeField]
        private MaterialIndex m_materialIndex;
        [SerializeField]
        private GPUCSGOperator m_csgOperator;
        [SerializeField]
        private GPUBiomeMapParameters m_biomeMapParameters;
        [SerializeField]
        private GPUBiomeMixerParameters m_biomeMixerParameters;
        [SerializeField]
        private GPUMountainParameters m_mountainParameters;

        public GPUGenerationGraphNode(
            NodeType nodeType,
            Matrix4x4 transformMatrix,
            GPUNoiseParameters noiseParameters,
            GPUCSGPrimitive csgPrimitive,
            MaterialIndex materialIndex,
            GPUCSGOperator csgOperator,
            GPUBiomeMapParameters biomeMapParameters,
            GPUBiomeMixerParameters biomeMixerParameters,
            GPUMountainParameters mountainParameters)
        {
            m_nodeType = nodeType;
            m_transformMatrix = transformMatrix;
            m_noiseParameters = noiseParameters;
            m_csgPrimitive = csgPrimitive;
            m_materialIndex = materialIndex;
            m_csgOperator = csgOperator;
            m_biomeMapParameters = biomeMapParameters;
            m_biomeMixerParameters = biomeMixerParameters;
            m_mountainParameters = mountainParameters;
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBiomeMapParameters
    {
        public Vector4 TemperatureCenter;
        public Vector4 TemperatureRange;
        public Vector4 MoistureCenter;
        public Vector4 MoistureRange;
        public Vector4 WeightSharpness;
        public float WeightGain;
        public uint BiomeCount;
        public Vector2 Padding;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBiomeMixerParameters
    {
        public Vector4 BaseAmplitude;
        public Vector4 HeightOffset;
        public Vector4 BaseFrequencyX;
        public Vector4 BaseFrequencyY;
        public Vector4 BaseFrequencyZ;
        public Vector4 WarpStrength;
        public Vector4 WarpFrequencyX;
        public Vector4 WarpFrequencyY;
        public Vector4 WarpFrequencyZ;
        public float MixStrength;
        public uint BiomeCount;
        public Vector2 Padding;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUMountainParameters
    {
        public Vector4 Amplitude;
        public Vector4 RemapExponent;
        public Vector4 TerraceSteps;
        public Vector4 TerraceBias;
        public Vector4 BaseParameters;
        public Vector4 WarpParameters;
        public Vector4 ExtraParameters0;
        public Vector4 ExtraParameters1;
        public uint BiomeCount;
        public uint WarpSeed;
        public Vector2 Padding;
    }
}
