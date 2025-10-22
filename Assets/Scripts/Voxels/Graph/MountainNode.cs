using Tuntenfisch.Attributes;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEngine;

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
        private struct MountainBaseRidgedSettings
        {
            [SerializeField]
            private uint m_seed;
            [SerializeField, Range(1, 12)]
            private int m_octaves;
            [SerializeField]
            private float m_initialAmplitude;
            [SerializeField]
            private Vector3 m_initialFrequency;
            [SerializeField, Range(0.0f, 1.5f)]
            private float m_persistence;
            [SerializeField]
            private Vector3 m_lacunarity;

            public static MountainBaseRidgedSettings Default => new MountainBaseRidgedSettings
            {
                m_seed = 1337u,
                m_octaves = 6,
                m_initialAmplitude = 1.0f,
                m_initialFrequency = new Vector3(0.0016f, 0.0f, 0.0016f),
                m_persistence = 0.5f,
                m_lacunarity = new Vector3(2.0f, 2.0f, 2.0f)
            };

            public MountainBaseRidgedSettings Validated()
            {
                MountainBaseRidgedSettings settings = this;
                settings.m_octaves = math.clamp(settings.m_octaves, 1, 32);
                settings.m_initialAmplitude = Mathf.Max(0.0f, settings.m_initialAmplitude);
                settings.m_initialFrequency.x = Mathf.Max(1e-5f, Mathf.Abs(settings.m_initialFrequency.x));
                settings.m_initialFrequency.y = 0.0f;
                settings.m_initialFrequency.z = Mathf.Max(1e-5f, Mathf.Abs(settings.m_initialFrequency.z));
                settings.m_persistence = Mathf.Clamp(settings.m_persistence, 0.0f, 2.0f);
                settings.m_lacunarity = new Vector3
                (
                    Mathf.Max(0.01f, Mathf.Abs(settings.m_lacunarity.x)),
                    Mathf.Max(0.01f, Mathf.Abs(settings.m_lacunarity.y)),
                    Mathf.Max(0.01f, Mathf.Abs(settings.m_lacunarity.z))
                );
                return settings;
            }

            public GPUNoiseParameters ToNoiseParameters()
            {
                MountainBaseRidgedSettings validated = Validated();
                float3 frequency = new float3(validated.m_initialFrequency.x, validated.m_initialFrequency.y, validated.m_initialFrequency.z);
                float3 lacunarity = new float3(validated.m_lacunarity.x, validated.m_lacunarity.y, validated.m_lacunarity.z);

                return GPUNoiseParameters.Create
                (
                    validated.m_seed,
                    NoiseAxes.XZ,
                    NoiseType.Ridge,
                    (uint)validated.m_octaves,
                    validated.m_initialAmplitude,
                    frequency,
                    validated.m_persistence,
                    lacunarity
                );
            }
        }

        public BiomeLibrary BiomeLibrary => m_biomeLibrary;
        public float MixStrength => m_mixStrength;
        public GPUNoiseParameters BaseNoiseParameters => m_baseRidgedSettings.ToNoiseParameters();
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

        [Header("Base Ridged FBM")]
        [InlineField]
        [SerializeField]
        private MountainBaseRidgedSettings m_baseRidgedSettings = MountainBaseRidgedSettings.Default;

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
            m_baseRidgedSettings = m_baseRidgedSettings.Validated();
            m_mixStrength = Mathf.Clamp01(m_mixStrength);
            m_plateauSlopeLow = Mathf.Clamp(m_plateauSlopeLow, k_minSlopeDeg, k_maxSlopeDeg);
            m_plateauSlopeHigh = Mathf.Clamp(Mathf.Max(m_plateauSlopeHigh, m_plateauSlopeLow + 0.1f), k_minSlopeDeg, k_maxSlopeDeg);
        }
    }
}
