using System;
using System.Collections.Generic;
using Tuntenfisch.World.Math;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.World.Planning
{
    /// <summary>
    /// Deterministic region planner that converts world settings into region-scale plans.
    /// This phase focuses on providing a stable contract; feature providers plug in later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RegionPlanner : MonoBehaviour
    {
        public WorldSettings Settings => m_worldSettings;
        public event Action<RegionPlan> RegionPlanBuilt;

        [SerializeField]
        private WorldSettings m_worldSettings;
        [SerializeField, Range(1, 512)]
        private int m_climateTileResolution = 32;

        private readonly Dictionary<RegionKey, RegionPlan> m_planCache = new Dictionary<RegionKey, RegionPlan>();
        private readonly List<Texture2D> m_ownedTextures = new List<Texture2D>();

        private void Awake()
        {
            if (m_worldSettings == null)
            {
                Debug.LogError($"{nameof(RegionPlanner)} requires a reference to {nameof(WorldSettings)}.", this);
            }
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in m_ownedTextures)
            {
                DestroyTexture(texture);
            }

            m_ownedTextures.Clear();
        }

        public bool TryGetPlan(RegionKey regionKey, out RegionPlan plan)
        {
            if (m_planCache.TryGetValue(regionKey, out plan))
            {
                return true;
            }

            if (m_worldSettings == null)
            {
                plan = null;
                return false;
            }

            plan = BuildRegionPlan(regionKey);
            m_planCache[regionKey] = plan;
            RegionPlanBuilt?.Invoke(plan);
            return true;
        }

        public RegionPlan RebuildPlan(RegionKey regionKey)
        {
            if (m_planCache.TryGetValue(regionKey, out RegionPlan existing))
            {
                ReleasePlanResources(existing);
                m_planCache.Remove(regionKey);
            }

            RegionPlan plan = BuildRegionPlan(regionKey);
            m_planCache[regionKey] = plan;
            RegionPlanBuilt?.Invoke(plan);
            return plan;
        }

        public void ClearCache()
        {
            foreach (RegionPlan plan in m_planCache.Values)
            {
                ReleasePlanResources(plan);
            }

            m_planCache.Clear();
        }

        private RegionPlan BuildRegionPlan(RegionKey regionKey)
        {
            uint regionSeed = m_worldSettings.GetRegionSeed(regionKey);
            RegionMeta meta = new RegionMeta(
                m_worldSettings.SeaLevel,
                m_worldSettings.SeaLevel,
                biomePaletteKey: 0u,
                flags: 0u,
                temperatureTextureHandle: 0u,
                moistureTextureHandle: 1u);

            RegionPlan plan = new RegionPlan(regionKey, meta);
            plan.AddWaterLevel(m_worldSettings.SeaLevel);

            Texture2D temperatureTile = CreateClimateTileTexture(regionKey, regionSeed, 0u);
            Texture2D moistureTile = CreateClimateTileTexture(regionKey, regionSeed, 1u);
            plan.AddClimateTile(temperatureTile);
            plan.AddClimateTile(moistureTile);

            return plan;
        }

        private void ReleasePlanResources(RegionPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            foreach (Texture2D texture in plan.ClimateTiles)
            {
                DestroyTexture(texture);
                m_ownedTextures.Remove(texture);
            }

            plan.ClearTransientData();
        }

        private Texture2D CreateClimateTileTexture(RegionKey regionKey, uint regionSeed, uint salt)
        {
            int resolution = math.clamp(m_climateTileResolution, 1, 512);
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = $"Climate_{regionKey.X}_{regionKey.Y}_{salt}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[resolution * resolution];

            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    int index = y * resolution + x;
                    uint sampleSeed = DeterministicRng.Hash(regionSeed, (uint)(salt * 73856093u + (uint)index));
                    float value = DeterministicRng.Range01(sampleSeed);
                    byte channel = (byte)math.clamp(math.round(value * 255.0f), 0.0f, 255.0f);
                    pixels[index] = new Color32(channel, channel, channel, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            m_ownedTextures.Add(texture);
            return texture;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }
    }
}
