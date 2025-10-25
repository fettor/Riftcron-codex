using Tuntenfisch.Attributes;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Tuntenfisch.Voxels.Procedural
{
    [CreateNodeMenu("Generation Nodes/Mountains/Mountain Field", order = (int)NodeType.Mountain)]
    [NodeWidth(304)]
    [NodeTint(c_internalNodeColor)]
    public sealed class MountainNode : GenerationGraphNode
    {
        private const float k_minSlopeDeg = 0.0f;
        private const float k_maxSlopeDeg = 89.0f;

        [System.Serializable]
        private struct MountainRidgedSettings
        {
            private const float k_minFrequency = 1e-5f;
            private const float k_maxFrequency = 0.05f;

            [SerializeField]
            private uint m_seed;
            [SerializeField, Range(1, 12)]
            private int m_octaves;
            [SerializeField]
            private Vector3 m_baseFrequency;
            [SerializeField, Range(0.2f, 1.0f)]
            private float m_gain;
            [SerializeField, Range(0.5f, 1.5f)]
            private float m_offset;
            [SerializeField, Range(1.0f, 4.0f)]
            private float m_lacunarity;

            public static MountainRidgedSettings Default => new MountainRidgedSettings
            {
                m_seed = 1337u,
                m_octaves = 6,
                m_baseFrequency = new Vector3(0.0018f, 0.0010f, 0.0018f),
                m_gain = 0.5f,
                m_offset = 1.05f,
                m_lacunarity = 2.15f
            };

            public MountainRidgedSettings Validated()
            {
                MountainRidgedSettings settings = this;
                settings.m_octaves = math.clamp(settings.m_octaves, 1, 32);
                settings.m_baseFrequency = new Vector3
                (
                    Mathf.Clamp(Mathf.Abs(settings.m_baseFrequency.x), k_minFrequency, k_maxFrequency),
                    Mathf.Clamp(Mathf.Abs(settings.m_baseFrequency.y), 0.0f, k_maxFrequency),
                    Mathf.Clamp(Mathf.Abs(settings.m_baseFrequency.z), k_minFrequency, k_maxFrequency)
                );
                settings.m_gain = Mathf.Clamp(settings.m_gain, 0.2f, 1.2f);
                settings.m_offset = Mathf.Clamp(settings.m_offset, 0.5f, 1.6f);
                settings.m_lacunarity = Mathf.Clamp(settings.m_lacunarity, 1.0f, 4.0f);
                return settings;
            }

            public GPUNoiseParameters ToNoiseParameters()
            {
                MountainRidgedSettings validated = Validated();
                float3 frequency = new float3(validated.m_baseFrequency.x, validated.m_baseFrequency.y, validated.m_baseFrequency.z);
                float3 lacunarity = new float3(validated.m_lacunarity, validated.m_lacunarity, validated.m_lacunarity);

                return GPUNoiseParameters.Create(
                    validated.m_seed,
                    NoiseAxes.XYZ,
                    NoiseType.Ridge,
                    (uint)validated.m_octaves,
                    validated.m_offset,
                    frequency,
                    validated.m_gain,
                    lacunarity);
            }
        }

        [System.Serializable]
        private struct MountainWarpSettings
        {
            [SerializeField, Range(0.0f, 4096.0f)]
            private float m_amplitude;
            [SerializeField, Range(1e-4f, 1.0f)]
            private float m_frequency;
            [SerializeField]
            private int m_seedOffset;

            public static MountainWarpSettings Create(float amplitude, float frequency, int seedOffset)
            {
                MountainWarpSettings settings = default;
                settings.m_amplitude = Mathf.Max(0.0f, amplitude);
                settings.m_frequency = Mathf.Clamp(Mathf.Abs(frequency), 1e-4f, 1.0f);
                settings.m_seedOffset = seedOffset;
                return settings;
            }

            public MountainWarpSettings Validated()
            {
                MountainWarpSettings settings = this;
                settings.m_amplitude = Mathf.Max(0.0f, settings.m_amplitude);
                settings.m_frequency = Mathf.Clamp(Mathf.Abs(settings.m_frequency), 1e-4f, 1.0f);
                return settings;
            }

            public float Amplitude => Mathf.Max(0.0f, m_amplitude);
            public float Frequency => Mathf.Clamp(Mathf.Abs(m_frequency), 1e-4f, 1.0f);
            public uint SeedOffset => (uint)Mathf.Abs(m_seedOffset);
        }

        public BiomeLibrary BiomeLibrary => m_biomeLibrary;
        public float MixStrength => m_mixStrength;
        public GPUNoiseParameters BaseNoiseParameters => m_ridgedSettings.ToNoiseParameters();
        public float BaseAmplitude => math.max(0.0f, m_baseAmplitude);
        public float BaseRemapExponent => math.max(0.25f, m_baseExponent);
        public float BaseTerraceSteps => math.max(1.0f, m_baseTerraceSteps);
        public float BaseTerraceBias => math.clamp(m_baseTerraceBias, 0.0f, 1.0f);
        public float TerracePower => math.max(0.5f, m_terracePower);
        public float LargeWarpAmplitude => m_largeWarp.Validated().Amplitude;
        public float LargeWarpFrequency => m_largeWarp.Validated().Frequency;
        public float DetailWarpAmplitude => m_detailWarp.Validated().Amplitude;
        public float DetailWarpFrequency => m_detailWarp.Validated().Frequency;
        public uint DetailWarpSeedOffset => m_detailWarp.Validated().SeedOffset;
        public Vector2 PlateauSlopeRange => new Vector2(m_plateauSlopeLow, m_plateauSlopeHigh);
        public bool WriteMountainMask => m_writeMountainMask;

        [Input(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Override, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float m_position;

        [Output(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Override, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float2 m_valueAndGradient;

        [Header("Biome Reference")]
        [SerializeField]
        private BiomeLibrary m_biomeLibrary;

        [Header("Global Ridged Field")]
        [InlineField]
        [SerializeField]
        [FormerlySerializedAs("m_baseRidgedSettings")]
        private MountainRidgedSettings m_ridgedSettings = MountainRidgedSettings.Default;
        [InlineField]
        [SerializeField]
        private MountainWarpSettings m_largeWarp = MountainWarpSettings.Create(220.0f, 0.0065f, 0);
        [InlineField]
        [SerializeField]
        private MountainWarpSettings m_detailWarp = MountainWarpSettings.Create(40.0f, 0.035f, 37);

        [Header("Base Shaping")]
        [SerializeField, Range(0.0f, 2048.0f)]
        private float m_baseAmplitude = 120.0f;
        [SerializeField, Range(0.25f, 6.0f)]
        private float m_baseExponent = 1.0f;
        [SerializeField, Range(1.0f, 12.0f)]
        private float m_baseTerraceSteps = 6.0f;
        [SerializeField, Range(0.0f, 1.0f)]
        private float m_baseTerraceBias = 0.35f;
        [SerializeField, Range(0.5f, 3.0f)]
        private float m_terracePower = 1.2f;

        [Header("Mountain Mixing")]
        [SerializeField, Range(0.0f, 1.0f)]
        private float m_mixStrength = 1.0f;

        [Header("Plateau Mask Settings")]
        [SerializeField, Tooltip("Slope angle (degrees) below which the plateau mask approaches 1.")]
        private float m_plateauSlopeLow = 6.0f;
        [SerializeField, Tooltip("Slope angle (degrees) above which the plateau mask approaches 0.")]
        private float m_plateauSlopeHigh = 18.0f;

        [Header("Debug")]
        [SerializeField, Tooltip("When enabled, the mountain mask overlay will be written during chunk generation.")]
        private bool m_writeMountainMask = false;

        public override NodeType GetNodeType() => NodeType.Mountain;

        private void OnValidate()
        {
            m_ridgedSettings = m_ridgedSettings.Validated();
            m_largeWarp = m_largeWarp.Validated();
            m_detailWarp = m_detailWarp.Validated();
            m_baseAmplitude = Mathf.Max(0.0f, m_baseAmplitude);
            m_baseExponent = Mathf.Clamp(m_baseExponent, 0.25f, 6.0f);
            m_baseTerraceSteps = Mathf.Clamp(m_baseTerraceSteps, 1.0f, 24.0f);
            m_baseTerraceBias = Mathf.Clamp01(m_baseTerraceBias);
            m_terracePower = Mathf.Clamp(m_terracePower, 0.5f, 4.0f);
            m_mixStrength = Mathf.Clamp01(m_mixStrength);
            m_plateauSlopeLow = Mathf.Clamp(m_plateauSlopeLow, k_minSlopeDeg, k_maxSlopeDeg);
            m_plateauSlopeHigh = Mathf.Clamp(Mathf.Max(m_plateauSlopeHigh, m_plateauSlopeLow + 0.1f), k_minSlopeDeg, k_maxSlopeDeg);
        }
    }
}
