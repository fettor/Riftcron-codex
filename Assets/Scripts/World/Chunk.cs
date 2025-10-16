using System;
using System.Collections.Generic;
using Tuntenfisch.Generics;
using Tuntenfisch.Generics.Pool;
using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.DC;
using Tuntenfisch.Voxels.Materials;
using Tuntenfisch.Voxels.Volume;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tuntenfisch.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class Chunk : MonoBehaviour, IPoolable
    {
        private int m_currentLOD;
        private int m_targetLOD;
        private int m_vertexCount;
        private int m_triangleCount;

        private Mesh m_mesh;
        private MeshFilter m_meshFilter;
        private MeshRenderer m_meshRenderer;
        private MeshCollider m_meshCollider;
        private OnMeshGenerated m_onMeshGeneratedDelegate;

        private ComputeBuffer m_voxelVolumeBuffer;
        private IRequest m_request;
        private JobHandle m_bakeJobHandle;
        private List<GPUVoxelVolumeCSGOperation> m_voxelVolumeCSGOperations;
        private ChunkFlags m_flags;
        private PackedVoxel[] m_packedVoxelCache;
        private bool m_packedVoxelCacheValid;
        private static int s_overlapVoxelCount = -1;

        private void Awake()
        {
            WorldManager.VoxelConfig.MaterialConfig.OnDirtied += ApplyRenderMaterial;
            InitializeMeshComponents();
            ApplyRenderMaterial();
            m_voxelVolumeCSGOperations = new List<GPUVoxelVolumeCSGOperation>();
        }

        private void Update()
        {
            if (m_flags == 0)
            {
                return;
            }

            if ((m_flags & ChunkFlags.VoxelVolumeRegenerationRequested) == ChunkFlags.VoxelVolumeRegenerationRequested)
            {
                m_flags &= ~ChunkFlags.VoxelVolumeRegenerationRequested;
                ChunkGenerationBindings generationBindings = WorldManager.GetChunkGenerationBindings(transform.position);
                WorldManager.VoxelVolume.GenerateVoxelVolume(m_voxelVolumeBuffer, transform.position, generationBindings);
            }

            if ((m_flags & ChunkFlags.CSGOperationPerformed) == ChunkFlags.CSGOperationPerformed)
            {
                m_flags &= ~ChunkFlags.CSGOperationPerformed;
                WorldManager.VoxelVolume.ApplyVoxelVolumeCSGOperations(m_voxelVolumeBuffer, transform.position, m_voxelVolumeCSGOperations);
                m_voxelVolumeCSGOperations.Clear();
            }

            if ((m_flags & ChunkFlags.MeshRegenerationRequested) == ChunkFlags.MeshRegenerationRequested && (m_flags & ChunkFlags.IsBakingMesh) != ChunkFlags.IsBakingMesh && m_request == null)
            {
                m_flags &= ~ChunkFlags.MeshRegenerationRequested;
                m_request = WorldManager.DualContouring.RequestMeshAsync
                (
                    m_voxelVolumeBuffer,
                    m_currentLOD,
                    m_targetLOD,
                    m_vertexCount,
                    m_triangleCount,
                    transform.position,
                    m_onMeshGeneratedDelegate
                );
            }

            if ((m_flags & ChunkFlags.IsBakingMesh) == ChunkFlags.IsBakingMesh && m_bakeJobHandle.IsCompleted)
            {
                m_flags &= ~ChunkFlags.IsBakingMesh;
                m_meshCollider.sharedMesh = null;
                m_meshCollider.sharedMesh = m_mesh;
            }
        }

        private void OnDestroy()
        {
            WorldManager.VoxelConfig.MaterialConfig.OnDirtied -= ApplyRenderMaterial;
            ReleaseBuffers();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelVolumeDimensions);
        }

        public void OnAcquire()
        {
            m_currentLOD = m_targetLOD = m_vertexCount = m_triangleCount = -1;
            CreateBuffers();
            m_packedVoxelCache = null;
            m_packedVoxelCacheValid = false;
            gameObject.SetActive(true);
        }

        public void OnRelease()
        {
            m_meshFilter.sharedMesh = null;
            m_meshCollider.sharedMesh = null;
            m_request?.Cancel();
            m_request = null;
            m_voxelVolumeCSGOperations.Clear();
            m_flags = 0;
            m_packedVoxelCache = null;
            m_packedVoxelCacheValid = false;
            gameObject.SetActive(false);
        }

        public bool GetMaterialFromRaycastHit(RaycastHit hit, out MaterialIndex materialIndex)
        {
            materialIndex = default;

            if (hit.triangleIndex >= m_triangleCount)
            {
                return false;
            }

            using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(m_mesh);
            {
                Mesh.MeshData meshData = meshDataArray[0];
                NativeArray<int> triangles = meshData.GetIndexData<int>();
                NativeArray<GPUVertex> vertices = meshData.GetVertexData<GPUVertex>();

                float shortestDistanceSquared = float.MaxValue;

                for (int index = 0; index < 3; index++)
                {
                    GPUVertex vertex = vertices[triangles[3 * hit.triangleIndex + index]];
                    float distanceSquared = math.lengthsq(hit.transform.TransformPoint(vertex.Position) - hit.point);

                    if (distanceSquared < shortestDistanceSquared)
                    {
                        shortestDistanceSquared = distanceSquared;
                        materialIndex = vertex.MaterialIndex;
                    }
                }
            }

            return true;
        }

        private void CreateBuffers()
        {
            if (m_voxelVolumeBuffer?.count != WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelCount)
            {
                m_voxelVolumeBuffer?.Release();
                m_voxelVolumeBuffer = new ComputeBuffer(WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelCount, 2 * sizeof(uint));
            }

            m_packedVoxelCache = null;
            m_packedVoxelCacheValid = false;
        }

        private void ReleaseBuffers()
        {
            if (m_voxelVolumeBuffer != null)
            {
                m_voxelVolumeBuffer.Release();
                m_voxelVolumeBuffer = null;
            }

            m_packedVoxelCache = null;
            m_packedVoxelCacheValid = false;
        }

        public void RegenerateVoxelVolume()
        {
            m_packedVoxelCacheValid = false;
            m_flags |= ChunkFlags.VoxelVolumeRegenerationRequested;
        }

        public void RegenerateMesh(int lod = -1)
        {
            if (lod != -1 && lod != m_targetLOD)
            {
                m_targetLOD = lod;
                m_flags |= ChunkFlags.MeshRegenerationRequested;
            }
            else if (lod == -1)
            {
                m_flags |= ChunkFlags.MeshRegenerationRequested;
            }
        }

        public void ApplyCSGPrimitiveOperation(GPUCSGOperator csgOperator, GPUCSGPrimitive csgPrimitive, MaterialIndex materialIndex, Matrix4x4 worldToObjectMatrix)
        {
            m_voxelVolumeCSGOperations.Add(new GPUVoxelVolumeCSGOperation(csgOperator, csgPrimitive, materialIndex, worldToObjectMatrix));
            m_flags |= ChunkFlags.CSGOperationPerformed | ChunkFlags.MeshRegenerationRequested;
            m_packedVoxelCacheValid = false;
        }

        private void OnMeshGenerated(NativeArray<GPUVertex> vertices, int vertexCount, int vertexStartIndex, NativeArray<int> triangles, int triangleCount, int triangleStartIndex)
        {
            m_request = null;
            m_currentLOD = m_targetLOD;
            m_vertexCount = vertexCount;
            m_triangleCount = triangleCount;

            if (vertexCount == 0 || triangleCount == 0)
            {
                m_meshFilter.sharedMesh = null;
                m_meshCollider.sharedMesh = null;

                return;
            }

            m_mesh.SetVertexBufferParams(vertexCount, GPUVertex.Attributes);
            m_mesh.SetIndexBufferParams(triangleCount, IndexFormat.UInt32);
#if !UNITY_EDITOR
            MeshUpdateFlags flags = MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices;
            m_mesh.SetVertexBufferData(vertices, vertexStartIndex, 0, vertexCount, 0, flags);
            m_mesh.SetIndexBufferData(triangles, triangleStartIndex, 0, triangleCount, flags);
            m_mesh.SetSubMesh(0, new SubMeshDescriptor(0, triangleCount), flags);
            m_mesh.RecalculateBounds(flags);
#else
            m_mesh.SetVertexBufferData(vertices, vertexStartIndex, 0, vertexCount);
            m_mesh.SetIndexBufferData(triangles, triangleStartIndex, 0, triangleCount);
            m_mesh.SetSubMesh(0, new SubMeshDescriptor(0, triangleCount));
            m_mesh.RecalculateBounds(MeshUpdateFlags.DontValidateIndices);
#endif
            m_meshFilter.sharedMesh = null;
            m_meshFilter.sharedMesh = m_mesh;

            m_bakeJobHandle = new BakeJob(m_mesh.GetInstanceID()).Schedule();
            m_flags |= ChunkFlags.IsBakingMesh;
        }

        private void InitializeMeshComponents()
        {
            m_mesh = new Mesh();
            m_mesh.MarkDynamic();
            m_meshFilter = GetComponent<MeshFilter>();
            m_meshRenderer = GetComponent<MeshRenderer>();
            m_meshCollider = GetComponent<MeshCollider>();
            m_onMeshGeneratedDelegate = OnMeshGenerated;
        }

        private static int VoxelsPerAxis => WorldManager.VoxelConfig.VoxelVolumeConfig.NumberOfVoxelsAlongAxis;

        public bool TryGetVoxel(int3 localCoordinate, out PackedVoxel voxel)
        {
            voxel = default;

            if (!IsLocalCoordinateInBounds(localCoordinate) || !TryAcquirePackedVoxelBuffer(out PackedVoxel[] buffer))
            {
                return false;
            }

            voxel = buffer[CalculateVoxelIndex(localCoordinate)];
            return true;
        }

        public bool TrySetVoxel(int3 localCoordinate, PackedVoxel voxel) => TrySetVoxelInternal(localCoordinate, voxel, true);

        public void SetVoxelToAir(int3 localCoordinate)
        {
            TrySetVoxelInternal(localCoordinate, PackedVoxel.Empty, true);
        }

        public int3 WorldToLocalVoxelCoordinate(float3 worldPosition)
        {
            float spacing = WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelSpacing;
            float3 offset = 0.5f * ((float3)VoxelsPerAxis - 1.0f);
            float3 local = (worldPosition - (float3)transform.position) / spacing + offset;
            return new int3((int)math.round(local.x), (int)math.round(local.y), (int)math.round(local.z));
        }

        private bool TryAcquirePackedVoxelBuffer(out PackedVoxel[] buffer)
        {
            buffer = null;

            if (!EnsurePackedVoxelCache())
            {
                return false;
            }

            buffer = m_packedVoxelCache;
            return true;
        }

        private bool EnsurePackedVoxelCache()
        {
            if (m_voxelVolumeBuffer == null)
            {
                return false;
            }

            int voxelCount = WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelCount;
            if (m_packedVoxelCache == null || m_packedVoxelCache.Length != voxelCount)
            {
                m_packedVoxelCache = new PackedVoxel[voxelCount];
                m_packedVoxelCacheValid = false;
            }

            if (!m_packedVoxelCacheValid)
            {
                m_voxelVolumeBuffer.GetData(m_packedVoxelCache);
                m_packedVoxelCacheValid = true;
            }

            return true;
        }

        private static bool IsLocalCoordinateInBounds(int3 coordinate)
        {
            int size = VoxelsPerAxis;
            return coordinate.x >= 0 && coordinate.x < size &&
                   coordinate.y >= 0 && coordinate.y < size &&
                   coordinate.z >= 0 && coordinate.z < size;
        }

        private static int CalculateVoxelIndex(int3 coordinate)
        {
            int size = VoxelsPerAxis;
            return coordinate.x + coordinate.y * size + coordinate.z * size * size;
        }

        private bool TrySetVoxelFromNeighbor(int3 localCoordinate, PackedVoxel voxel)
        {
            return TrySetVoxelInternal(localCoordinate, voxel, false);
        }

        private bool TrySetVoxelInternal(int3 localCoordinate, PackedVoxel voxel, bool propagateToNeighbors)
        {
            if (!IsLocalCoordinateInBounds(localCoordinate) || !TryAcquirePackedVoxelBuffer(out PackedVoxel[] buffer) || m_voxelVolumeBuffer == null)
            {
                return false;
            }

            int index = CalculateVoxelIndex(localCoordinate);
            buffer[index] = voxel;
            m_voxelVolumeBuffer.SetData(buffer, index, index, 1);
            MarkVoxelDataModified();

            if (propagateToNeighbors)
            {
                SynchronizeNeighborBorderVoxels(localCoordinate, voxel);
            }

            return true;
        }

        private void SynchronizeNeighborBorderVoxels(int3 localCoordinate, PackedVoxel voxel)
        {
            int overlap = OverlapVoxelCount;
            int maxIndex = VoxelsPerAxis - 1;
            int positiveThreshold = maxIndex - (overlap - 1);
            int negativeThreshold = overlap - 1;

            int xState = localCoordinate.x <= negativeThreshold ? -1 : (localCoordinate.x >= positiveThreshold ? 1 : 0);
            int yState = localCoordinate.y <= negativeThreshold ? -1 : (localCoordinate.y >= positiveThreshold ? 1 : 0);
            int zState = localCoordinate.z <= negativeThreshold ? -1 : (localCoordinate.z >= positiveThreshold ? 1 : 0);

            if (xState == 0 && yState == 0 && zState == 0)
            {
                return;
            }

            int3 baseChunkCoordinate = WorldManager.GetChunkCoordinate((float3)transform.position);

            int xIterations = xState == 0 ? 1 : 2;
            int yIterations = yState == 0 ? 1 : 2;
            int zIterations = zState == 0 ? 1 : 2;

            for (int xi = 0; xi < xIterations; ++xi)
            {
                int dx = xState == 0 ? 0 : (xi == 0 ? 0 : xState);

                for (int yi = 0; yi < yIterations; ++yi)
                {
                    int dy = yState == 0 ? 0 : (yi == 0 ? 0 : yState);

                    for (int zi = 0; zi < zIterations; ++zi)
                    {
                        int dz = zState == 0 ? 0 : (zi == 0 ? 0 : zState);

                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            continue;
                        }

                        int3 direction = new int3(dx, dy, dz);
                        int3 neighborChunkCoordinate = baseChunkCoordinate + direction;

                        if (!WorldManager.TryGetChunk(neighborChunkCoordinate, out Chunk neighbor))
                        {
                            continue;
                        }

                        int3 neighborLocalCoordinate = new int3(
                            TransformCoordinateForNeighbor(localCoordinate.x, dx),
                            TransformCoordinateForNeighbor(localCoordinate.y, dy),
                            TransformCoordinateForNeighbor(localCoordinate.z, dz));

                        neighbor.TrySetVoxelFromNeighbor(neighborLocalCoordinate, voxel);
                    }
                }
            }
        }

        private int TransformCoordinateForNeighbor(int coordinate, int direction)
        {
            if (direction == 0)
            {
                return coordinate;
            }

            int maxIndex = VoxelsPerAxis - 1;
            int overlap = OverlapVoxelCount;
            int positiveStart = maxIndex - (overlap - 1);

            if (direction > 0)
            {
                return math.clamp(coordinate - positiveStart, 0, overlap - 1);
            }

            return math.clamp(positiveStart + coordinate, positiveStart, maxIndex);
        }

        private void MarkVoxelDataModified()
        {
            m_flags |= ChunkFlags.MeshRegenerationRequested;
            m_packedVoxelCacheValid = true;
        }

        private static int OverlapVoxelCount
        {
            get
            {
                if (s_overlapVoxelCount < 0)
                {
                    var voxelConfig = WorldManager.VoxelConfig.VoxelVolumeConfig;
                    float spacing = math.max(voxelConfig.VoxelSpacing, 1e-4f);
                    float chunkSpan = voxelConfig.VoxelVolumeDimensions.x;
                    float chunkStep = WorldManager.ChunkDimensions.x;
                    float cellOverlapFloat = (chunkSpan - chunkStep) / spacing;
                    int cellOverlap = math.max(0, (int)math.round(cellOverlapFloat));
                    s_overlapVoxelCount = math.max(1, cellOverlap + 1);
                }

                return s_overlapVoxelCount;
            }
        }

        internal static void ResetCachedOverlapVoxelCount()
        {
            s_overlapVoxelCount = -1;
        }

        private void ApplyRenderMaterial() => m_meshRenderer.material = WorldManager.VoxelConfig.MaterialConfig.RenderMaterial;

        [Flags]
        private enum ChunkFlags
        {
            VoxelVolumeRegenerationRequested = 1,
            CSGOperationPerformed = 2,
            MeshRegenerationRequested = 4,
            IsBakingMesh = 8
        }
    }
}
