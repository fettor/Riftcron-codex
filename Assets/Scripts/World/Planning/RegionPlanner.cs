using System;
using System.Collections.Generic;
using Tuntenfisch.World.Math;
using UnityEngine;

namespace Tuntenfisch.World.Planning
{
    /// <summary>
    /// Deterministic region planner that converts world settings into region-scale plans.
    /// This phase focuses on providing a stable contract; feature providers plug in later.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ClimateProvider))]
    public sealed class RegionPlanner : MonoBehaviour
    {
        public WorldSettings Settings => m_worldSettings;
        public ClimateProvider ClimateProvider => m_climateProvider;
        public event Action<RegionPlan> RegionPlanBuilt;

        [SerializeField]
        private WorldSettings m_worldSettings;
        [SerializeField]
        private ClimateProvider m_climateProvider;

        private readonly Dictionary<RegionKey, RegionPlan> m_planCache = new Dictionary<RegionKey, RegionPlan>();

        private void Awake()
        {
            if (m_worldSettings == null)
            {
                Debug.LogError($"{nameof(RegionPlanner)} requires a reference to {nameof(WorldSettings)}.", this);
            }

            if (m_climateProvider == null)
            {
                m_climateProvider = GetComponent<ClimateProvider>();
            }

            if (m_climateProvider == null)
            {
                Debug.LogError($"{nameof(RegionPlanner)} requires a {nameof(ClimateProvider)} component.", this);
            }
        }

        private void OnDestroy()
        {
            ClearCache();
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
                moistureTextureHandle: 0u);

            RegionPlan plan = new RegionPlan(regionKey, meta);
            plan.AddWaterLevel(m_worldSettings.SeaLevel);

            if (m_climateProvider != null)
            {
                m_climateProvider.PopulateClimateTiles(m_worldSettings, plan);
            }

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
            }

            foreach (Texture2D texture in plan.BiomeWeightTiles)
            {
                DestroyTexture(texture);
            }

            plan.ClearTransientData();
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
