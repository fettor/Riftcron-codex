#if UNITY_EDITOR
using System.Collections.Generic;
using Tuntenfisch.World.Buffers;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Tuntenfisch.World.Editor
{
    [CustomEditor(typeof(WorldSettings))]
    public class WorldSettingsEditor : UnityEditor.Editor
    {
        private int m_regionX;
        private int m_regionY;
        private static readonly List<RegionBufferStats> s_bufferStats = new List<RegionBufferStats>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Planner Validation", EditorStyles.boldLabel);
            m_regionX = EditorGUILayout.IntField("Region X", m_regionX);
            m_regionY = EditorGUILayout.IntField("Region Y", m_regionY);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Rebuild & Report Buffers"))
                {
                    ValidateRegionPlan();
                }
            }
        }

        private void ValidateRegionPlan()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Region validation is only available in play mode.");
                return;
            }

            WorldSettings settings = (WorldSettings)target;
#if UNITY_2023_1_OR_NEWER
            RegionPlanner planner = Object.FindFirstObjectByType<RegionPlanner>();
#else
            RegionPlanner planner = Object.FindObjectOfType<RegionPlanner>();
#endif

            if (planner == null)
            {
                Debug.LogWarning("No RegionPlanner found in the active scene.");
                return;
            }

            RegionKey regionKey = new RegionKey(m_regionX, m_regionY);
            RegionPlan plan = planner.RebuildPlan(regionKey);

            float3 regionDimensions = settings.GetRegionDimensionsInWorldUnits();
            float3 regionCenter = new float3((regionKey.X + 0.5f) * regionDimensions.x, 0.0f, (regionKey.Y + 0.5f) * regionDimensions.z);

            ChunkGenerationBindings bindings = WorldManager.GetChunkGenerationBindings(regionCenter);
            WorldManager.CollectRegionBufferStats(s_bufferStats);

            RegionBufferStats bufferStats = default;
            bool statsFound = false;

            foreach (RegionBufferStats stats in s_bufferStats)
            {
                if (stats.Region.Equals(regionKey))
                {
                    bufferStats = stats;
                    statsFound = true;
                    break;
                }
            }

            string bufferInfo = statsFound
                ? $"GPU buffers → splines: {bufferStats.SplineCount}, stamps: {bufferStats.StampCount}"
                : "GPU buffers → unavailable";

            Debug.Log($"Region {regionKey} rebuilt. Plan → splines: {plan.Splines.Count}, stamps: {plan.Stamps.Count}. {bufferInfo}. Bindings valid: {bindings.IsValid}", planner);
        }
    }
}
#endif
