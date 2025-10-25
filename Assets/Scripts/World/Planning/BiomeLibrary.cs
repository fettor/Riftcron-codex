using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.World.Planning
{
    /// <summary>
    /// Library that defines globally indexed biomes and the parameter packs
    /// used for deterministic, branchless mixing on the GPU.
    /// The ordering of biomes in this asset is the authoritative index.
    /// </summary>
    [CreateAssetMenu(fileName = "BiomeLibrary", menuName = "World/Biome Library", order = 10)]
    public sealed class BiomeLibrary : ScriptableObject
    {
        public const int MaxBiomeCount = 4;

        public IReadOnlyList<BiomeDefinition> Biomes => m_biomes;
        public int BiomeCount => math.min(m_biomes?.Count ?? 0, MaxBiomeCount);

        [SerializeField]
        private List<BiomeDefinition> m_biomes = new List<BiomeDefinition>
        {
            new BiomeDefinition(
                "Temperate Plains",
                new Color(0.36f, 0.73f, 0.28f),
                new BiomeClimateSettings(
                    temperatureCenter: 0.58f,
                    temperatureRange: 0.26f,
                    moistureCenter: 0.57f,
                    moistureRange: 0.30f,
                    weightSharpness: 2.1f),
                BiomeTerrainParameters.Create(
                    heightOffset: 8.0f,
                    baseAmplitude: 20.0f,
                    baseFrequency: new float3(0.0016f, 0.0014f, 0.0016f),
                    warpStrength: 12.0f,
                    warpFrequency: new float3(0.0050f, 0.0040f, 0.0050f),
                    mountains: BiomeMountainParameters.Create(
                        amplitude: 120.0f,
                        remapExponent: 1.1f,
                        terraceSteps: 6.0f,
                        terraceBias: 0.35f))),
            new BiomeDefinition(
                "Arid Plateau",
                new Color(0.88f, 0.72f, 0.32f),
                new BiomeClimateSettings(
                    temperatureCenter: 0.78f,
                    temperatureRange: 0.20f,
                    moistureCenter: 0.22f,
                    moistureRange: 0.16f,
                    weightSharpness: 2.8f),
                BiomeTerrainParameters.Create(
                    heightOffset: 42.0f,
                    baseAmplitude: 24.0f,
                    baseFrequency: new float3(0.0013f, 0.0013f, 0.0011f),
                    warpStrength: 7.0f,
                    warpFrequency: new float3(0.0038f, 0.0038f, 0.0030f),
                    mountains: BiomeMountainParameters.Create(
                        amplitude: 48.0f,
                        remapExponent: 1.35f,
                        terraceSteps: 5.0f,
                        terraceBias: 0.30f))),
            new BiomeDefinition(
                "Boreal",
                new Color(0.38f, 0.60f, 0.88f),
                new BiomeClimateSettings(
                    temperatureCenter: 0.32f,
                    temperatureRange: 0.24f,
                    moistureCenter: 0.64f,
                    moistureRange: 0.28f,
                    weightSharpness: 2.4f),
                BiomeTerrainParameters.Create(
                    heightOffset: 18.0f,
                    baseAmplitude: 32.0f,
                    baseFrequency: new float3(0.0022f, 0.0019f, 0.0020f),
                    warpStrength: 19.0f,
                    warpFrequency: new float3(0.0080f, 0.0065f, 0.0075f),
                    mountains: BiomeMountainParameters.Create(
                        amplitude: 260.0f,
                        remapExponent: 1.7f,
                        terraceSteps: 7.0f,
                        terraceBias: 0.32f))),
            new BiomeDefinition(
                "Tropical Wetlands",
                new Color(0.24f, 0.54f, 0.34f),
                new BiomeClimateSettings(
                    temperatureCenter: 0.88f,
                    temperatureRange: 0.18f,
                    moistureCenter: 0.86f,
                    moistureRange: 0.18f,
                    weightSharpness: 3.2f),
                BiomeTerrainParameters.Create(
                    heightOffset: -4.0f,
                    baseAmplitude: 14.0f,
                    baseFrequency: new float3(0.0014f, 0.0012f, 0.0014f),
                    warpStrength: 15.0f,
                    warpFrequency: new float3(0.0070f, 0.0060f, 0.0070f),
                    mountains: BiomeMountainParameters.Create(
                        amplitude: 84.0f,
                        remapExponent: 1.25f,
                        terraceSteps: 5.0f,
                        terraceBias: 0.40f)))
        };

        private void OnValidate()
        {
            if (m_biomes == null)
            {
                m_biomes = new List<BiomeDefinition>();
            }

            while (m_biomes.Count > MaxBiomeCount)
            {
                m_biomes.RemoveAt(m_biomes.Count - 1);
            }

            for (int i = 0; i < m_biomes.Count; ++i)
            {
                m_biomes[i] = m_biomes[i].Clamped();
            }
        }

        /// <summary>
        /// Branchless computation of biome weights given temperature and moisture in [0,1].
        /// The returned vector always sums to one (within floating point tolerance).
        /// </summary>
        public float4 EvaluateWeights(float temperature, float moisture)
        {
            int count = BiomeCount;

            if (count == 0)
            {
                return new float4(1.0f, 0.0f, 0.0f, 0.0f);
            }

            float4 weights = 0.0f;
            float t = math.saturate(temperature);
            float m = math.saturate(moisture);

            for (int i = 0; i < count; ++i)
            {
                BiomeDefinition biome = m_biomes[i];
                float tempBlend = EvaluateAxis(t, biome.Climate.TemperatureCenter, biome.Climate.TemperatureRange);
                float moistureBlend = EvaluateAxis(m, biome.Climate.MoistureCenter, biome.Climate.MoistureRange);
                float weight = math.pow(tempBlend * moistureBlend, biome.Climate.WeightSharpness);
                weights[i] = weight;
            }

            float normalization = math.csum(weights);
            float safeDenominator = math.max(normalization, 1e-5f);
            weights /= safeDenominator;
            weights.w = count > 3 ? weights.w : 0.0f;
            return weights;
        }

        public BiomeDefinition GetBiome(int index)
        {
            if (index < 0 || index >= BiomeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_biomes[index];
        }

        private static float EvaluateAxis(float value, float center, float range)
        {
            float width = math.max(range, 1e-3f);
            float distance = math.abs(value - center);
            float normalized = math.saturate(1.0f - distance / width);
            // Smoothstep for softer transitions.
            return normalized * normalized * (3.0f - 2.0f * normalized);
        }
    }

    [Serializable]
    public struct BiomeDefinition
    {
        private const float k_minFrequency = 1e-4f;
        private const float k_maxFrequency = 0.25f;

        public string Id => m_id;
        public Color DebugColor => m_debugColor;
        public BiomeClimateSettings Climate => m_climate;
        public BiomeTerrainParameters Terrain => m_terrain;

        [SerializeField]
        private string m_id;
        [SerializeField]
        private Color m_debugColor;
        [SerializeField]
        private BiomeClimateSettings m_climate;
        [SerializeField]
        private BiomeTerrainParameters m_terrain;

        public BiomeDefinition(string id, Color debugColor, BiomeClimateSettings climate, BiomeTerrainParameters terrain)
        {
            m_id = string.IsNullOrWhiteSpace(id) ? "Biome" : id;
            m_debugColor = debugColor;
            m_climate = climate;
            m_terrain = terrain;
        }

        public BiomeDefinition Clamped()
        {
            BiomeClimateSettings climate = m_climate.Clamped();
            BiomeTerrainParameters terrain = m_terrain.ClampFrequencies(k_minFrequency, k_maxFrequency);
            return new BiomeDefinition(m_id, m_debugColor, climate, terrain);
        }

        public static BiomeDefinition CreateDefault(string id, Color color, float temperatureCenter, float moistureCenter)
        {
            BiomeClimateSettings climate = new BiomeClimateSettings
            (
                math.saturate(temperatureCenter),
                0.35f,
                math.saturate(moistureCenter),
                0.35f,
                2.0f
            );

            BiomeTerrainParameters terrain = BiomeTerrainParameters.Default;
            return new BiomeDefinition(id, color, climate, terrain);
        }
    }

    [Serializable]
    public struct BiomeClimateSettings
    {
        public float TemperatureCenter => m_temperatureCenter;
        public float TemperatureRange => m_temperatureRange;
        public float MoistureCenter => m_moistureCenter;
        public float MoistureRange => m_moistureRange;
        public float WeightSharpness => m_weightSharpness;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_temperatureCenter;
        [SerializeField, Range(0.05f, 1.0f)]
        private float m_temperatureRange;
        [SerializeField, Range(0.0f, 1.0f)]
        private float m_moistureCenter;
        [SerializeField, Range(0.05f, 1.0f)]
        private float m_moistureRange;
        [SerializeField, Range(0.5f, 8.0f)]
        private float m_weightSharpness;

        public BiomeClimateSettings(float temperatureCenter, float temperatureRange, float moistureCenter, float moistureRange, float weightSharpness)
        {
            m_temperatureCenter = math.saturate(temperatureCenter);
            m_temperatureRange = math.clamp(temperatureRange, 0.05f, 1.0f);
            m_moistureCenter = math.saturate(moistureCenter);
            m_moistureRange = math.clamp(moistureRange, 0.05f, 1.0f);
            m_weightSharpness = math.clamp(weightSharpness, 0.5f, 8.0f);
        }

        public BiomeClimateSettings Clamped()
        {
            return new BiomeClimateSettings(m_temperatureCenter, m_temperatureRange, m_moistureCenter, m_moistureRange, m_weightSharpness);
        }
    }

    [Serializable]
    public struct BiomeTerrainParameters
    {
        private const float k_minFrequency = 1e-4f;
        private const float k_maxFrequency = 0.25f;

        public static BiomeTerrainParameters Default => new BiomeTerrainParameters
        {
            m_heightOffset = 0.0f,
            m_baseAmplitude = 24.0f,
            m_baseFrequency = new float3(0.0025f, 0.0025f, 0.0025f),
            m_warpStrength = 18.0f,
            m_warpFrequency = new float3(0.01f, 0.01f, 0.01f),
            m_mountains = BiomeMountainParameters.Default
        };

        public float HeightOffset => m_heightOffset;
        public float BaseAmplitude => m_baseAmplitude;
        public float3 BaseFrequency => m_baseFrequency;
        public float WarpStrength => m_warpStrength;
        public float3 WarpFrequency => m_warpFrequency;
        public BiomeMountainParameters Mountains => m_mountains;

        [SerializeField, Range(-256.0f, 256.0f)]
        private float m_heightOffset;
        [SerializeField, Range(0.0f, 512.0f)]
        private float m_baseAmplitude;
        [SerializeField]
        private float3 m_baseFrequency;
        [SerializeField, Range(0.0f, 256.0f)]
        private float m_warpStrength;
        [SerializeField]
        private float3 m_warpFrequency;
        [SerializeField]
        private BiomeMountainParameters m_mountains;

        public static BiomeTerrainParameters Create(float heightOffset, float baseAmplitude, float3 baseFrequency, float warpStrength, float3 warpFrequency)
        {
            return Create(heightOffset, baseAmplitude, baseFrequency, warpStrength, warpFrequency, BiomeMountainParameters.Default);
        }

        public static BiomeTerrainParameters Create(float heightOffset, float baseAmplitude, float3 baseFrequency, float warpStrength, float3 warpFrequency, BiomeMountainParameters mountains)
        {
            BiomeMountainParameters mountainParameters = mountains.Validated();

            return new BiomeTerrainParameters
            {
                m_heightOffset = math.clamp(heightOffset, -256.0f, 256.0f),
                m_baseAmplitude = math.max(0.0f, baseAmplitude),
                m_baseFrequency = math.clamp(math.abs(baseFrequency), k_minFrequency, k_maxFrequency),
                m_warpStrength = math.max(0.0f, warpStrength),
                m_warpFrequency = math.clamp(math.abs(warpFrequency), k_minFrequency, k_maxFrequency),
                m_mountains = mountainParameters
            };
        }

        public BiomeTerrainParameters ClampFrequencies(float minFrequency, float maxFrequency)
        {
            float3 baseFreq = math.clamp(math.abs(m_baseFrequency), minFrequency, maxFrequency);
            float3 warpFreq = math.clamp(math.abs(m_warpFrequency), minFrequency, maxFrequency);
            BiomeMountainParameters mountains = m_mountains.Validated();

            return new BiomeTerrainParameters
            {
                m_heightOffset = m_heightOffset,
                m_baseAmplitude = math.max(0.0f, m_baseAmplitude),
                m_baseFrequency = baseFreq,
                m_warpStrength = math.max(0.0f, m_warpStrength),
                m_warpFrequency = warpFreq,
                m_mountains = mountains
            };
        }
    }

    [Serializable]
    public struct BiomeMountainParameters
    {
        public static BiomeMountainParameters Default => new BiomeMountainParameters
        {
            m_amplitude = 0.0f,
            m_remapExponent = 1.0f,
            m_terraceSteps = 6.0f,
            m_terraceBias = 0.35f
        };

        public float Amplitude => m_amplitude;
        public float RemapExponent => m_remapExponent;
        public float TerraceSteps => m_terraceSteps;
        public float TerraceBias => m_terraceBias;

        [Header("Mountains")]
        [SerializeField, Range(0.0f, 2048.0f)]
        private float m_amplitude;
        [SerializeField, Range(0.25f, 6.0f)]
        private float m_remapExponent;
        [SerializeField, Range(1.0f, 12.0f)]
        private float m_terraceSteps;
        [SerializeField, Range(0.0f, 1.0f)]
        private float m_terraceBias;

        public static BiomeMountainParameters Create(float amplitude, float remapExponent, float terraceSteps, float terraceBias)
        {
            BiomeMountainParameters parameters = new BiomeMountainParameters
            {
                m_amplitude = math.max(0.0f, amplitude),
                m_remapExponent = math.max(0.25f, remapExponent),
                m_terraceSteps = math.max(1.0f, terraceSteps),
                m_terraceBias = math.clamp(terraceBias, 0.0f, 1.0f)
            };

            return parameters.Validated();
        }

        public BiomeMountainParameters Validated()
        {
            BiomeMountainParameters parameters = this;
            parameters.m_amplitude = math.max(0.0f, parameters.m_amplitude);
            parameters.m_remapExponent = math.max(0.25f, parameters.m_remapExponent);
            parameters.m_terraceSteps = math.max(1.0f, parameters.m_terraceSteps);
            parameters.m_terraceBias = math.clamp(parameters.m_terraceBias, 0.0f, 1.0f);
            return parameters;
        }
    }
}
