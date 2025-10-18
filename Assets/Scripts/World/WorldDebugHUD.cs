using System;
using System.Collections.Generic;
using Tuntenfisch.World.Buffers;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Tuntenfisch.World
{
    [DisallowMultipleComponent]
    public sealed class WorldDebugHUD : MonoBehaviour
    {
        [SerializeField]
        private WorldManager m_worldManager;
        [SerializeField]
        private RegionPlanner m_regionPlanner;
        [SerializeField]
        private Transform m_viewer;
        [SerializeField]
        private KeyCode m_toggleKey = KeyCode.F3;
        [SerializeField]
        private bool m_showOverlay = true;
        [SerializeField, Min(0)]
        private int m_regionGridRadius = 1;
        [SerializeField]
        private bool m_drawRegionGrid = true;

        private readonly List<RegionBufferStats> m_regionBufferStats = new List<RegionBufferStats>();

        private void Awake()
        {
            if (m_worldManager == null)
            {
#if UNITY_2023_1_OR_NEWER
                m_worldManager = FindFirstObjectByType<WorldManager>();
#else
                m_worldManager = FindObjectOfType<WorldManager>();
#endif
            }

            if (m_regionPlanner == null && m_worldManager != null)
            {
                m_regionPlanner = m_worldManager.GetComponent<RegionPlanner>();
            }

            if (m_viewer == null)
            {
                m_viewer = WorldManager.ViewerTransform;

                if (m_viewer == null && m_worldManager != null)
                {
                    m_viewer = m_worldManager.transform;
                }
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if (TryWasTogglePressedThisFrame())
            {
                m_showOverlay = !m_showOverlay;
            }
#else
            if (Input.GetKeyDown(m_toggleKey))
            {
                m_showOverlay = !m_showOverlay;
            }
#endif
        }

        private void OnGUI()
        {
            if (!m_showOverlay || m_worldManager == null)
            {
                return;
            }

            WorldManager.CollectRegionBufferStats(m_regionBufferStats);

            GUILayout.BeginArea(new Rect(16.0f, 16.0f, 360.0f, 220.0f), GUI.skin.window);

            int worldSeed = m_regionPlanner != null && m_regionPlanner.Settings != null ? m_regionPlanner.Settings.WorldSeed : 0;
            GUILayout.Label($"World Seed: {worldSeed}");

            if (m_viewer != null)
            {
                float3 viewerPos = (float3)m_viewer.position;
                int3 chunkCoord = WorldManager.GetChunkCoordinate(viewerPos);
                RegionKey regionKey = m_regionPlanner != null && m_regionPlanner.Settings != null ? m_regionPlanner.Settings.GetRegionKeyFromWorldPosition(viewerPos) : default;
                GUILayout.Label($"Viewer Chunk: {chunkCoord}");
                GUILayout.Label($"Viewer Region: {regionKey}");
            }

            GUILayout.Space(4.0f);
            GUILayout.Label($"Active Regions: {m_regionBufferStats.Count}");

            foreach (RegionBufferStats stats in m_regionBufferStats)
            {
                GUILayout.Label($"{stats.Region}: splines={stats.SplineCount}, stamps={stats.StampCount}");
            }

            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!m_drawRegionGrid || m_regionPlanner == null || m_regionPlanner.Settings == null || m_viewer == null)
            {
                return;
            }

            float3 regionDimensions = m_regionPlanner.Settings.GetRegionDimensionsInWorldUnits();
            float regionWidth = math.max(0.1f, regionDimensions.x);
            float regionDepth = math.max(0.1f, regionDimensions.z);
            float regionHeight = math.max(1.0f, regionDimensions.y);
            float chunkWidth = m_regionPlanner.Settings.ChunkDimensionsInBlocks.x * m_regionPlanner.Settings.MetersPerUnit;
            float chunkDepth = m_regionPlanner.Settings.ChunkDimensionsInBlocks.z * m_regionPlanner.Settings.MetersPerUnit;

            float3 viewerPos = (float3)m_viewer.position;
            RegionKey centerRegion = m_regionPlanner.Settings.GetRegionKeyFromWorldPosition(viewerPos);

            Gizmos.color = Color.yellow;

            for (int dx = -m_regionGridRadius; dx <= m_regionGridRadius; ++dx)
            {
                for (int dz = -m_regionGridRadius; dz <= m_regionGridRadius; ++dz)
                {
                    int regionX = centerRegion.X + dx;
                    int regionY = centerRegion.Y + dz;

                    float3 minCorner = new float3(regionX * regionWidth - 0.5f * chunkWidth, 0.0f, regionY * regionDepth - 0.5f * chunkDepth);
                    float3 center = minCorner + new float3(0.5f * regionWidth, 0.5f * regionHeight, 0.5f * regionDepth);
                    float3 size = new float3(regionWidth, regionHeight, regionDepth);

                    Gizmos.DrawWireCube(center, size);
                }
            }
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private bool TryWasTogglePressedThisFrame()
        {
            if (Keyboard.current == null)
            {
                return false;
            }

            if (!Enum.TryParse(m_toggleKey.ToString(), out Key key))
            {
                return false;
            }

            var control = Keyboard.current[key];
            return control != null && control.wasPressedThisFrame;
        }
#endif
    }
}
