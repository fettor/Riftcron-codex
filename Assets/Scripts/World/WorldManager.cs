using System;
using System.Collections.Generic;
using Tuntenfisch.Generics;
using Tuntenfisch.Generics.Pool;
using Tuntenfisch.Voxels;
using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.DC;
using Tuntenfisch.Voxels.Materials;
using Tuntenfisch.Voxels.Volume;
using Tuntenfisch.World.Buffers;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Tuntenfisch.World
{
    [RequireComponent(typeof(VoxelConfig), typeof(VoxelVolume), typeof(DualContouring))]
    [RequireComponent(typeof(CSGUtility))]
    public class WorldManager : SingletonComponent<WorldManager>
    {
        public static VoxelConfig VoxelConfig => Instance.m_voxelConfig;
        public static VoxelVolume VoxelVolume => Instance.m_voxelVolume;
        public static DualContouring DualContouring => Instance.m_dualContouring;
        public static float3 ChunkDimensions => Instance != null ? Instance.m_chunkDimensions : 0.0f;
        public static Transform ViewerTransform => Instance != null ? Instance.m_viewer : null;
        public static ChunkGenerationBindings GetChunkGenerationBindings(float3 chunkWorldPosition) => Instance != null ? Instance.GetChunkGenerationBindingsInternal(chunkWorldPosition) : ChunkGenerationBindings.Empty;
        public static void CollectRegionBufferStats(List<RegionBufferStats> stats)
        {
            if (Instance != null)
            {
                Instance.CollectRegionBufferStatsInternal(stats);
            }
        }

        public static bool TryGetChunk(int3 chunkCoordinate, out Chunk chunk)
        {
            chunk = null;
            return Instance != null && Instance.m_chunks != null && Instance.m_chunks.TryGetValue(chunkCoordinate, out chunk);
        }

        public static bool TryGetChunkAtPosition(float3 worldPosition, out Chunk chunk)
        {
            chunk = null;

            if (Instance == null)
            {
                return false;
            }

            int3 chunkCoordinate = Instance.CalculateChunkCoordinate(worldPosition);
            return TryGetChunk(chunkCoordinate, out chunk);
        }

        public static int3 GetChunkCoordinate(float3 worldPosition)
        {
            return Instance != null ? Instance.CalculateChunkCoordinate(worldPosition) : default;
        }

        private float ViewDistanceSquared => m_lodDistancesSquared[m_lodDistancesSquared.Length - 1];

        [SerializeField]
        private Transform m_viewer;
        [SerializeField]
        private float m_updateInterval = 20.0f;
        [SerializeField]
        private GameObject m_chunkPrefab;
        [SerializeField]
        private RegionPlanner m_regionPlanner;
        [SerializeField]
        private int m_initialChunkPoolPopulation = 0;
        [SerializeField]
        private float[] m_lodDistances;
        [SerializeField, Min(0)]
        private int m_chunksAboveViewer = 2;
        [SerializeField, Min(0)]
        private int m_chunksBelowViewer = 2;

        private VoxelConfig m_voxelConfig;
        private VoxelVolume m_voxelVolume;
        private DualContouring m_dualContouring;
        private CSGUtility m_csgUtility;
        private BufferRegistry m_bufferRegistry;
        private ObjectPool<Chunk> m_chunkPool;
        private Dictionary<int3, Chunk> m_chunks;
        private List<int3> m_chunksOutsideOfViewDistance;
        private Queue<(int3, float3, int)> m_chunksToProcess;
        private HashSet<int3> m_processedChunkCoordinates;
        private float3 m_chunkDimensions;

        // We don't want to update the world every frame.
        private float3 m_lastViewerPosition;
        private float m_updateIntervalSquared;
        private float[] m_lodDistancesSquared;

        private void Start()
        {
            Assert.IsFalse(m_chunkPrefab.activeSelf);

            m_voxelConfig = GetComponent<VoxelConfig>();
            m_voxelConfig.VoxelVolumeConfig.OnLateDirtied += ApplyVoxelVolumeConfig;
            m_voxelConfig.DualContouringConfig.OnLateDirtied += ApplyDualContouringConfig;
            m_voxelConfig.GenerationGraph.OnLateDirtied += ApplyGenerationGraph;

            m_voxelVolume = GetComponent<VoxelVolume>();
            m_dualContouring = GetComponent<DualContouring>();
            m_csgUtility = GetComponent<CSGUtility>();

            if (m_regionPlanner == null)
            {
                m_regionPlanner = GetComponent<RegionPlanner>();
            }

            if (m_regionPlanner == null)
            {
#if UNITY_2023_1_OR_NEWER
                m_regionPlanner = FindFirstObjectByType<RegionPlanner>();
#else
                m_regionPlanner = FindObjectOfType<RegionPlanner>();
#endif
            }

            if (m_regionPlanner == null)
            {
                Debug.LogError($"{nameof(WorldManager)} requires a {nameof(RegionPlanner)} reference.", this);
            }

            m_bufferRegistry = new BufferRegistry();

            m_chunkPool = new ObjectPool<Chunk>(() => { return Instantiate(m_chunkPrefab, transform).GetComponent<Chunk>(); }, m_initialChunkPoolPopulation);
            m_chunks = new Dictionary<int3, Chunk>();
            m_chunksOutsideOfViewDistance = new List<int3>();
            m_chunksToProcess = new Queue<(int3, float3, int)>();
            m_processedChunkCoordinates = new HashSet<int3>();
            m_chunkDimensions = CalculateChunkDimensions();
            Chunk.ResetCachedOverlapVoxelCount();

            m_lastViewerPosition = m_viewer.position;
            m_updateIntervalSquared = math.pow(m_updateInterval, 2.0f);
            m_lodDistancesSquared = CalculateLodDistancesSquared();

            UpdateWorld(m_viewer.position);
        }

        private void Update()
        {
            if (math.lengthsq((float3)m_viewer.position - m_lastViewerPosition) >= m_updateIntervalSquared)
            {
                m_lastViewerPosition = m_viewer.position;
                UpdateWorld(m_viewer.position);
            }
        }

        private void OnDestroy()
        {
            m_voxelConfig.VoxelVolumeConfig.OnLateDirtied -= ApplyVoxelVolumeConfig;
            m_voxelConfig.DualContouringConfig.OnLateDirtied -= ApplyDualContouringConfig;
            m_voxelConfig.GenerationGraph.OnLateDirtied -= ApplyGenerationGraph;

            m_bufferRegistry?.Dispose();
            m_bufferRegistry = null;
        }

        private void OnValidate() => ApplySettings();

        public void DrawCSGPrimitiveHologram(CSGPrimitiveType primitiveType, float3 position, float3 scale)
        {
            m_csgUtility.DrawCSGPrimitiveHologram(primitiveType, Matrix4x4.TRS(position, quaternion.identity, scale));
        }

        public void ApplyCSGOperation(GPUCSGOperator csgOperator, GPUCSGPrimitive csgPrimitive, MaterialIndex materialIndex, float3 position, float3 scale)
        {
            const float scaleInflationFactor = 1.5f;

            Matrix4x4 worldToObjectMatrix = Matrix4x4.TRS(position, quaternion.identity, scale).inverse;

            // Inflate the scale a bit to ensure CSG operations near the boundary of chunks are processed by all nearby chunks.
            int3 minChunkCoordinate = CalculateChunkCoordinate(position - 0.5f * scaleInflationFactor * scale);
            int3 maxChunkCoordinate = CalculateChunkCoordinate(position + 0.5f * scaleInflationFactor * scale);

            for (int3 chunkCoordinate = minChunkCoordinate; chunkCoordinate.z <= maxChunkCoordinate.z; chunkCoordinate.z++)
            {
                for (chunkCoordinate.y = minChunkCoordinate.y; chunkCoordinate.y <= maxChunkCoordinate.y; chunkCoordinate.y++)
                {
                    for (chunkCoordinate.x = minChunkCoordinate.x; chunkCoordinate.x <= maxChunkCoordinate.x; chunkCoordinate.x++)
                    {
                        if (m_chunks.TryGetValue(chunkCoordinate, out Chunk chunk))
                        {
                            chunk.ApplyCSGPrimitiveOperation(csgOperator, csgPrimitive, materialIndex, worldToObjectMatrix);
                        }
                    }
                }
            }
        }

        public bool GetMaterialFromRaycastHit(RaycastHit hit, out MaterialIndex materialIndex)
        {
            materialIndex = default;

            if (!(hit.collider is MeshCollider))
            {
                return false;
            }

            int3 chunkCoordinate = CalculateChunkCoordinate(hit.point);

            if (m_chunks.TryGetValue(chunkCoordinate, out Chunk chunk) && chunk.GetMaterialFromRaycastHit(hit, out materialIndex))
            {
                return true;
            }

            return false;
        }

        private void UpdateWorld(float3 viewerPosition)
        {
            int3 viewerChunkCoordinate = CalculateChunkCoordinate(viewerPosition);

            DestroyChunksOutsideViewDistance(viewerPosition, viewerChunkCoordinate);
            CreateChunksWithinViewDistance(viewerPosition, viewerChunkCoordinate);
        }

        private void DestroyChunksOutsideViewDistance(float3 viewerPosition, int3 viewerChunkCoordinate)
        {
            foreach (KeyValuePair<int3, Chunk> pair in m_chunks)
            {
                float viewerToChunkDistanceSquared = math.lengthsq((float3)pair.Value.transform.position - viewerPosition);

                if (!IsChunkWithinVerticalRange(pair.Key.y, viewerChunkCoordinate.y) || viewerToChunkDistanceSquared > ViewDistanceSquared)
                {
                    m_chunksOutsideOfViewDistance.Add(pair.Key);
                }
            }

            foreach (int3 chunkCoordinate in m_chunksOutsideOfViewDistance)
            {
                m_chunkPool.Release(m_chunks[chunkCoordinate]);
                m_chunks.Remove(chunkCoordinate);
            }
            m_chunksOutsideOfViewDistance.Clear();
        }

        private void CreateChunksWithinViewDistance(float3 viewerPosition, int3 viewerChunkCoordinate)
        {
            m_processedChunkCoordinates.Clear();
            m_chunksToProcess.Clear();

            EnqueueChunk(viewerChunkCoordinate, viewerPosition, viewerChunkCoordinate);

            while (m_chunksToProcess.Count > 0)
            {
                (int3 chunkCoordinate, float3 chunkPosition, int lod) = m_chunksToProcess.Dequeue();

                if (m_chunks.TryGetValue(chunkCoordinate, out Chunk chunk))
                {
                    chunk.RegenerateMesh(lod);
                }
                else
                {
                    // Create new chunk.
                    chunk = m_chunkPool.Acquire();
                    chunk.transform.position = chunkPosition;
                    chunk.RegenerateVoxelVolume();
                    chunk.RegenerateMesh(lod);
                    m_chunks[chunkCoordinate] = chunk;
                }

                EnqueueChunk(chunkCoordinate + new int3(1, 0, 0), viewerPosition, viewerChunkCoordinate);
                EnqueueChunk(chunkCoordinate - new int3(1, 0, 0), viewerPosition, viewerChunkCoordinate);
                EnqueueChunk(chunkCoordinate + new int3(0, 1, 0), viewerPosition, viewerChunkCoordinate);
                EnqueueChunk(chunkCoordinate - new int3(0, 1, 0), viewerPosition, viewerChunkCoordinate);
                EnqueueChunk(chunkCoordinate + new int3(0, 0, 1), viewerPosition, viewerChunkCoordinate);
                EnqueueChunk(chunkCoordinate - new int3(0, 0, 1), viewerPosition, viewerChunkCoordinate);
            }
        }

        private void EnqueueChunk(int3 neighbourChunkCoordinate, float3 viewerPosition, int3 viewerChunkCoordinate)
        {
            if (!m_processedChunkCoordinates.Contains(neighbourChunkCoordinate))
            {
                if (IsChunkWithinVerticalRange(neighbourChunkCoordinate.y, viewerChunkCoordinate.y))
                {
                    float3 neighbourChunkPosition = neighbourChunkCoordinate * m_chunkDimensions;
                    float viewerToNeighbourChunkDistanceSquared = math.lengthsq(neighbourChunkPosition - viewerPosition);

                    if (viewerToNeighbourChunkDistanceSquared <= ViewDistanceSquared)
                    {
                        m_chunksToProcess.Enqueue((neighbourChunkCoordinate, neighbourChunkPosition, CalculateChunkLod(viewerToNeighbourChunkDistanceSquared)));
                    }
                }
            }
            m_processedChunkCoordinates.Add(neighbourChunkCoordinate);
        }

        private float3 CalculateChunkDimensions()
        {
            const int voxelOverlap = 1;

            float inflationFactor = 1.0f + (float)voxelOverlap / (VoxelConfig.VoxelVolumeConfig.NumberOfCellsAlongAxis - voxelOverlap);

            return VoxelConfig.VoxelVolumeConfig.VoxelVolumeDimensions / inflationFactor;
        }

        private int3 CalculateChunkCoordinate(float3 position) => new int3((int)math.round(position.x / m_chunkDimensions.x), (int)math.round(position.y / m_chunkDimensions.y), (int)math.round(position.z / m_chunkDimensions.z));

        private bool IsChunkWithinVerticalRange(int chunkY, int viewerChunkY)
        {
            int chunksAbove = math.max(0, m_chunksAboveViewer);
            int chunksBelow = math.max(0, m_chunksBelowViewer);

            return chunkY >= viewerChunkY - chunksBelow && chunkY <= viewerChunkY + chunksAbove;
        }

        private float[] CalculateLodDistancesSquared()
        {
            float[] lodDistancesSquared = new float[m_lodDistances.Length];

            for (int index = 0; index < lodDistancesSquared.Length; index++)
            {
                lodDistancesSquared[index] = math.pow(m_lodDistances[index], 2.0f);
            }

            return lodDistancesSquared;
        }

        private int CalculateChunkLod(float viewerToChunkDistanceSquared)
        {
            int lod = Array.BinarySearch(m_lodDistancesSquared, viewerToChunkDistanceSquared);

            if (lod < 0)
            {
                lod = ~lod;
            }

            if (lod == m_lodDistancesSquared.Length)
            {
                lod = 0;
            }

            return lod;
        }

        private void ApplySettings()
        {
            if (!Application.isPlaying || !gameObject.activeSelf || m_voxelConfig == null)
            {
                return;
            }

            m_updateIntervalSquared = math.pow(m_updateInterval, 2.0f);
            m_lodDistancesSquared = CalculateLodDistancesSquared();
            m_chunkDimensions = CalculateChunkDimensions();
            Chunk.ResetCachedOverlapVoxelCount();
            m_regionPlanner?.ClearCache();
            m_bufferRegistry?.Dispose();
            m_bufferRegistry = new BufferRegistry();

            foreach (Chunk chunk in m_chunks.Values)
            {
                m_chunkPool.Release(chunk);
            }
            m_chunks.Clear();

            UpdateWorld(m_viewer.position);
        }

        private ChunkGenerationBindings GetChunkGenerationBindingsInternal(float3 chunkWorldPosition)
        {
            if (m_regionPlanner == null || m_regionPlanner.Settings == null)
            {
                return ChunkGenerationBindings.Empty;
            }

            RegionKey regionKey = m_regionPlanner.Settings.GetRegionKeyFromWorldPosition(chunkWorldPosition);

            if (!m_regionPlanner.TryGetPlan(regionKey, out RegionPlan plan) || plan == null)
            {
                return ChunkGenerationBindings.Empty;
            }

            m_bufferRegistry?.UploadRegionPlan(plan);

            if (m_bufferRegistry == null)
            {
                return ChunkGenerationBindings.Empty;
            }

            ChunkBufferView bufferView = m_bufferRegistry.GetChunkView(regionKey);

            if (!bufferView.IsValid)
            {
                return ChunkGenerationBindings.Empty;
            }

            Texture temperature = GetClimateTexture(plan, 0);
            Texture moisture = GetClimateTexture(plan, 1);

            return new ChunkGenerationBindings(regionKey, bufferView.Splines, bufferView.Stamps, bufferView.MetaBuffer, temperature, moisture, bufferView.Meta);
        }

        private static Texture GetClimateTexture(RegionPlan plan, int index)
        {
            if (plan == null)
            {
                return Texture2D.grayTexture;
            }

            if (index >= 0 && index < plan.ClimateTiles.Count)
            {
                Texture2D tile = plan.ClimateTiles[index];

                if (tile != null)
                {
                    return tile;
                }
            }

            return Texture2D.grayTexture;
        }

        private void CollectRegionBufferStatsInternal(List<RegionBufferStats> stats)
        {
            if (stats == null)
            {
                return;
            }

            if (m_bufferRegistry == null)
            {
                stats.Clear();
                return;
            }

            m_bufferRegistry.CollectRegionStats(stats);
        }

        private void ApplyVoxelVolumeConfig() => ApplySettings();

        private void ApplyDualContouringConfig()
        {
            foreach (Chunk chunk in m_chunks.Values)
            {
                chunk.RegenerateMesh();
            }
        }

        private void ApplyGenerationGraph()
        {
            foreach (Chunk chunk in m_chunks.Values)
            {
                chunk.RegenerateVoxelVolume();
                chunk.RegenerateMesh();
            }
        }
    }
}
