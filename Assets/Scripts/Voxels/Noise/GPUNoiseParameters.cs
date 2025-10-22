using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.Voxels.Procedural
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUNoiseParameters
    {
        public static int SizeInBytes => s_sizeInBytes;

        private static readonly int s_sizeInBytes = Marshal.SizeOf<GPUNoiseParameters>();
        private const int k_minOctaves = 1;
        private const int k_maxOctaves = 32;

        [Header("General")]
        [SerializeField]
        private int m_seed;
        [SerializeField]
        private NoiseAxes m_noiseAxes;
        [SerializeField]
        private NoiseType m_noiseType;

        [Header("Fractional Brownian Motion")]
        [Range(1, 32)]
        [SerializeField]
        private int m_numberOfOctaves;
        [Min(1.0f)]
        [SerializeField]
        private float m_initialAmplitude;
        [SerializeField]
        private float3 m_initialFrequency;
        [Range(0.0f, 2.0f)]
        [SerializeField]
        private float m_persistence;
        [SerializeField]
        private float3 m_lacunarity;

        public static GPUNoiseParameters Create(uint seed, NoiseAxes noiseAxes, NoiseType noiseType, uint numberOfOctaves, float initialAmplitude, float3 initialFrequency, float persistence, float3 lacunarity)
        {
            GPUNoiseParameters parameters = default;
            parameters.m_seed = (int)seed;
            parameters.m_noiseAxes = noiseAxes;
            parameters.m_noiseType = noiseType;
            parameters.m_numberOfOctaves = Mathf.Clamp((int)numberOfOctaves, k_minOctaves, k_maxOctaves);
            parameters.m_initialAmplitude = Mathf.Max(0.0f, initialAmplitude);
            parameters.m_initialFrequency = initialFrequency;
            parameters.m_persistence = Mathf.Clamp(persistence, 0.0f, 2.0f);
            parameters.m_lacunarity = lacunarity;
            return parameters;
        }

        public GPUNoiseParameters WithNoiseAxes(NoiseAxes noiseAxes)
        {
            m_noiseAxes = noiseAxes;
            return this;
        }

        public GPUNoiseParameters WithNoiseType(NoiseType noiseType)
        {
            m_noiseType = noiseType;
            return this;
        }
    }
}
