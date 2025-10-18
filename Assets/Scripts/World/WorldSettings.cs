using Tuntenfisch.World.Math;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.World
{
    /// <summary>
    /// Canonical world settings shared by the CPU planner and GPU evaluators.
    /// The values here must remain stable across runs to guarantee determinism.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldSettings", menuName = "World/World Settings", order = 0)]
    public sealed class WorldSettings : ScriptableObject
    {
        public int WorldSeed => m_worldSeed;
        public float MetersPerUnit => m_metersPerUnit;
        public int3 ChunkDimensionsInBlocks => new int3(m_chunkSizeInBlocks.x, m_chunkSizeInBlocks.y, m_chunkSizeInBlocks.z);
        public int2 RegionSpanInChunks => new int2(m_regionSpanInChunks.x, m_regionSpanInChunks.y);
        public float SeaLevel => m_seaLevel;

        /// <summary>
        /// Returns the size of a full region in world units.
        /// The Y dimension uses a single chunk span by convention.
        /// </summary>
        public float3 GetRegionDimensionsInWorldUnits()
        {
            float3 chunkDimensions = ResolveChunkDimensions();
            return new float3(chunkDimensions.x * m_regionSpanInChunks.x, chunkDimensions.y, chunkDimensions.z * m_regionSpanInChunks.y);
        }

        /// <summary>
        /// Converts a world position to a region key using floor logic so boundaries are deterministic.
        /// </summary>
        public RegionKey GetRegionKeyFromWorldPosition(float3 worldPosition)
        {
            float3 chunkDimensions = ResolveChunkDimensions();
            float3 regionDimensions = new float3(chunkDimensions.x * m_regionSpanInChunks.x, chunkDimensions.y, chunkDimensions.z * m_regionSpanInChunks.y);
            float chunkWidth = chunkDimensions.x;
            float chunkDepth = chunkDimensions.z;
            int regionX = Mathf.FloorToInt((worldPosition.x + 0.5f * chunkWidth) / regionDimensions.x);
            int regionY = Mathf.FloorToInt((worldPosition.z + 0.5f * chunkDepth) / regionDimensions.z);
            return new RegionKey(regionX, regionY);
        }

        /// <summary>
        /// Combines this world seed with a region key to provide a deterministic RNG seed.
        /// </summary>
        public uint GetRegionSeed(RegionKey regionKey, uint salt = 0u)
        {
            unchecked
            {
                uint seed = (uint)m_worldSeed;
                seed = DeterministicRng.Hash(seed ^ (uint)regionKey.X);
                seed = DeterministicRng.Hash(seed ^ (uint)regionKey.Y);
                return DeterministicRng.Hash(seed ^ salt);
            }
        }

        private void OnValidate()
        {
            m_metersPerUnit = Mathf.Max(0.001f, m_metersPerUnit);
            m_chunkSizeInBlocks = new Vector3Int(math.max(1, m_chunkSizeInBlocks.x), math.max(1, m_chunkSizeInBlocks.y), math.max(1, m_chunkSizeInBlocks.z));
            m_regionSpanInChunks = new Vector2Int(math.max(1, m_regionSpanInChunks.x), math.max(1, m_regionSpanInChunks.y));
        }

        private float3 ResolveChunkDimensions()
        {
            float3 chunkDimensions = WorldManager.ChunkDimensions;

            if (chunkDimensions.x > 0.0f && chunkDimensions.z > 0.0f)
            {
                return chunkDimensions;
            }

            return new float3(m_chunkSizeInBlocks.x, m_chunkSizeInBlocks.y, m_chunkSizeInBlocks.z) * m_metersPerUnit;
        }

        [SerializeField]
        private int m_worldSeed = 1;
        [SerializeField, Min(0.001f)]
        private float m_metersPerUnit = 1.0f;
        [SerializeField]
        private Vector3Int m_chunkSizeInBlocks = new Vector3Int(32, 32, 32);
        [SerializeField]
        private Vector2Int m_regionSpanInChunks = new Vector2Int(8, 8);
        [SerializeField]
        private float m_seaLevel = 0.0f;
    }
}
