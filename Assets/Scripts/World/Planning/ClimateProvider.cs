using System;
using Tuntenfisch.Voxels;
using Tuntenfisch.World.Math;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.World.Planning
{
    /// <summary>
    /// Deterministic climate planner that evaluates temperature and moisture
    /// fields in world space and writes per-region tiles for GPU sampling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClimateProvider : MonoBehaviour
    {
        public int TileResolution => math.clamp(m_tileResolution, 16, 512);
        public BiomeLibrary BiomeLibrary => m_biomeLibrary;

        [Header("General")]
        [SerializeField, Range(16, 512)]
        private int m_tileResolution = 128;
        [SerializeField]
        private BiomeLibrary m_biomeLibrary;
        [SerializeField]
        private VoxelConfig m_voxelConfig;

        [Header("Temperature Noise")]
        [SerializeField]
        private ClimateNoiseSettings m_temperatureNoise = new ClimateNoiseSettings(0.0008f, 4, 1.0f, 2.1f, 0.55f);
        [SerializeField, Range(-1.0f, 1.0f)]
        private float m_temperatureBias = 0.0f;

        [Header("Moisture Noise")]
        [SerializeField]
        private ClimateNoiseSettings m_moistureNoise = new ClimateNoiseSettings(0.0010f, 4, 0.8f, 2.15f, 0.5f);
        [SerializeField, Range(-1.0f, 1.0f)]
        private float m_moistureBias = 0.0f;

        private void Awake()
        {
            if (m_voxelConfig == null)
            {
                m_voxelConfig = GetComponent<VoxelConfig>();
            }
        }

        public void PopulateClimateTiles(WorldSettings settings, RegionPlan plan)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            BiomeLibrary library = m_biomeLibrary != null ? m_biomeLibrary : Resources.Load<BiomeLibrary>("World/BiomeLibrary");

            if (m_biomeLibrary == null && library != null)
            {
                m_biomeLibrary = library;
            }

            int resolution = TileResolution;
            RegionKey regionKey = plan.RegionKey;

            uint worldSeedMixed = DeterministicRng.Hash((uint)settings.WorldSeed, 0xC1FCE55Du);
            float3 chunkDimensions = ResolveChunkDimensions(settings);
            float2 regionSpan = new float2(settings.RegionSpanInChunks.x, settings.RegionSpanInChunks.y);
            float2 regionSize = new float2(chunkDimensions.x * regionSpan.x, chunkDimensions.z * regionSpan.y);
            float chunkWidth = chunkDimensions.x;
            float chunkDepth = chunkDimensions.z;
            float2 regionOrigin = new float2(regionKey.X * regionSize.x - 0.5f * chunkWidth, regionKey.Y * regionSize.y - 0.5f * chunkDepth);

            Color[] temperaturePixels = new Color[resolution * resolution];
            Color[] moisturePixels = new Color[resolution * resolution];

            int biomeCount = library != null ? library.BiomeCount : 0;
            Color[][] biomeWeightPixels = null;

            if (biomeCount > 0)
            {
                biomeWeightPixels = new Color[BiomeLibrary.MaxBiomeCount][];

                for (int i = 0; i < biomeCount; ++i)
                {
                    biomeWeightPixels[i] = new Color[resolution * resolution];
                }
            }

            float latitudeScale = 1.0f / math.max(regionSize.y, 1.0f);

            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    int index = y * resolution + x;
                    float u = resolution > 1 ? x / (float)(resolution - 1) : 0.0f;
                    float v = resolution > 1 ? y / (float)(resolution - 1) : 0.0f;
                    float2 worldXZ = regionOrigin + new float2(u, v) * regionSize;

                    float temperature = EvaluateField(worldXZ, worldSeedMixed, m_temperatureNoise, 0xFA16C10Bu, m_temperatureBias);
                    // Subtle latitudinal gradient (z points "north").
                    float latitudeFactor = 0.5f * (worldXZ.y * latitudeScale);
                    temperature = math.saturate(temperature + latitudeFactor);

                    float moisture = EvaluateField(worldXZ, worldSeedMixed, m_moistureNoise, 0x8F3CF311u, m_moistureBias);
                    moisture = math.saturate(moisture);

                    temperaturePixels[index] = EncodeSingleChannel(temperature);
                    moisturePixels[index] = EncodeSingleChannel(moisture);

                    if (biomeCount > 0 && biomeWeightPixels != null)
                    {
                        float4 weights = library.EvaluateWeights(temperature, moisture);

                        for (int biomeIndex = 0; biomeIndex < biomeCount; ++biomeIndex)
                        {
                            biomeWeightPixels[biomeIndex][index] = EncodeSingleChannel(weights[biomeIndex]);
                        }
                    }
                }
            }

            Texture2D temperatureTexture = CreateTileTexture($"Climate_Temp_{regionKey.X}_{regionKey.Y}", resolution, temperaturePixels);
            Texture2D moistureTexture = CreateTileTexture($"Climate_Moist_{regionKey.X}_{regionKey.Y}", resolution, moisturePixels);

            int temperatureHandle = plan.ClimateTiles.Count;
            plan.AddClimateTile(temperatureTexture);
            int moistureHandle = plan.ClimateTiles.Count;
            plan.AddClimateTile(moistureTexture);

            if (biomeCount > 0 && biomeWeightPixels != null)
            {
                for (int i = 0; i < biomeCount; ++i)
                {
                    string name = $"Climate_Weight{i}_{regionKey.X}_{regionKey.Y}";
                    Texture2D weightTexture = CreateTileTexture(name, resolution, biomeWeightPixels[i]);
                    plan.AddBiomeWeightTile(weightTexture);
                }
            }

            RegionMeta meta = plan.Meta;
            meta.TemperatureTextureHandle = (uint)temperatureHandle;
            meta.MoistureTextureHandle = (uint)moistureHandle;
            plan.SetMeta(meta);
        }

        private Texture2D CreateTileTexture(string name, int resolution, Color[] pixels)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color EncodeSingleChannel(float value)
        {
            float clamped = math.saturate(value);
            return new Color(clamped, clamped, clamped, 1.0f);
        }

        private float EvaluateField(float2 worldXZ, uint regionSeed, ClimateNoiseSettings settings, uint salt, float bias)
        {
            float noise = FractalValueNoise(worldXZ, settings, DeterministicRng.Hash(regionSeed, salt));
            float value = 0.5f + 0.5f * noise + bias * 0.5f;
            return math.saturate(value);
        }

        private static float FractalValueNoise(float2 position, ClimateNoiseSettings settings, uint seed)
        {
            int octaves = math.clamp(settings.Octaves, 1, 8);
            float amplitude = math.max(1e-3f, settings.Amplitude);
            float lacunarity = math.max(1.0f, settings.Lacunarity);
            float persistence = math.clamp(settings.Persistence, 0.1f, 1.0f);

            float sum = 0.0f;
            float totalAmplitude = 0.0f;
            float frequency = math.max(settings.Frequency, 1e-4f);

            for (int i = 0; i < octaves; ++i)
            {
                float2 samplePos = position * frequency;
                float noise = ValueNoise(samplePos, DeterministicRng.Hash(seed, (uint)i));
                sum += amplitude * noise;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            if (totalAmplitude <= 0.0f)
            {
                return 0.0f;
            }

            return math.clamp(sum / totalAmplitude, -1.0f, 1.0f);
        }

        private static float ValueNoise(float2 position, uint seed)
        {
            int2 cell = (int2)math.floor(position);
            float2 local = position - cell;
            float2 smooth = local * local * (3.0f - 2.0f * local);

            float v00 = HashToSigned01(Hash(seed, cell));
            float v10 = HashToSigned01(Hash(seed, cell + new int2(1, 0)));
            float v01 = HashToSigned01(Hash(seed, cell + new int2(0, 1)));
            float v11 = HashToSigned01(Hash(seed, cell + new int2(1, 1)));

            float vx0 = math.lerp(v00, v10, smooth.x);
            float vx1 = math.lerp(v01, v11, smooth.x);

            return math.lerp(vx0, vx1, smooth.y);
        }

        private static uint Hash(uint seed, int2 coordinate)
        {
            uint hx = DeterministicRng.Hash(seed, unchecked((uint)coordinate.x * 374761393u));
            return DeterministicRng.Hash(hx, unchecked((uint)coordinate.y * 668265263u));
        }

        private static float HashToSigned01(uint hash)
        {
            return DeterministicRng.Range01(hash) * 2.0f - 1.0f;
        }

        private float3 ResolveChunkDimensions(WorldSettings settings)
        {
            float3 chunkDimensions = WorldManager.ChunkDimensions;

            if (chunkDimensions.x > 0.0f && chunkDimensions.z > 0.0f)
            {
                return chunkDimensions;
            }

            VoxelConfig voxelConfig = m_voxelConfig;

            if (voxelConfig == null)
            {
                voxelConfig = GetComponent<VoxelConfig>();
            }

            if (voxelConfig != null && voxelConfig.VoxelVolumeConfig != null)
            {
                float3 voxelVolumeDimensions = voxelConfig.VoxelVolumeConfig.VoxelVolumeDimensions;
                const int voxelOverlap = 1;
                int cells = voxelConfig.VoxelVolumeConfig.NumberOfCellsAlongAxis;
                float inflationFactor = 1.0f + (float)voxelOverlap / math.max(1, cells - voxelOverlap);
                return voxelVolumeDimensions / inflationFactor;
            }

            return new float3(settings.ChunkDimensionsInBlocks.x, settings.ChunkDimensionsInBlocks.y, settings.ChunkDimensionsInBlocks.z) * settings.MetersPerUnit;
        }

        [Serializable]
        private struct ClimateNoiseSettings
        {
            public float Frequency => m_frequency;
            public int Octaves => m_octaves;
            public float Persistence => m_persistence;
            public float Lacunarity => m_lacunarity;
            public float Amplitude => m_amplitude;

            [SerializeField]
            private float m_frequency;
            [SerializeField, Range(1, 8)]
            private int m_octaves;
            [SerializeField, Range(0.1f, 1.0f)]
            private float m_persistence;
            [SerializeField, Range(1.0f, 4.0f)]
            private float m_lacunarity;
            [SerializeField, Range(0.0f, 2.0f)]
            private float m_amplitude;

            public ClimateNoiseSettings(float frequency, int octaves, float persistence, float lacunarity, float amplitude)
            {
                m_frequency = math.max(frequency, 1e-4f);
                m_octaves = math.clamp(octaves, 1, 8);
                m_persistence = math.clamp(persistence, 0.1f, 1.0f);
                m_lacunarity = math.clamp(lacunarity, 1.0f, 4.0f);
                m_amplitude = math.clamp(amplitude, 0.0f, 2.0f);
            }
        }
    }
}
