using System;
using System.Collections.Generic;
using Tuntenfisch.Extensions;
using Tuntenfisch.World;
using Tuntenfisch.World.Buffers;
using Tuntenfisch.Voxels.Procedural;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.Voxels.Volume
{
    [RequireComponent(typeof(VoxelConfig))]
    public class VoxelVolume : MonoBehaviour
    {
        [SerializeField]
        private int m_voxelVolumeCSGOperationsBufferCapacity = 10;

        private VoxelConfig m_voxelConfig;
        private ComputeBuffer m_generationGraphNodesBuffer;
        private ComputeBuffer m_voxelVolumeCSGOperationsBuffer;
        private int m_generateKernel = -1;
        private int m_csgKernel = -1;

        private void Awake()
        {
            m_voxelConfig = GetComponent<VoxelConfig>();
            CacheKernelIndices();
            m_voxelConfig.GenerationGraph.Rebuild();
            m_voxelConfig.GenerationGraph.OnDirtied += ApplyGenerationGraph;
            ApplyGenerationGraph();
        }

        private void OnDestroy()
        {
            m_voxelConfig.GenerationGraph.OnDirtied -= ApplyGenerationGraph;
            ReleaseBuffers();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && gameObject.activeSelf && m_voxelConfig != null)
            {
                CreateBuffers();
                CacheKernelIndices();
            }
        }

        public void GenerateVoxelVolume(ComputeBuffer voxelVolumeBuffer, float3 worldPosition, in ChunkGenerationBindings bindings)
        {
            if (voxelVolumeBuffer == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeBuffer));
            }

            EnsureKernelIndices();
            if (!AreKernelsValid())
            {
                return;
            }

            if (!bindings.IsValid)
            {
                Debug.LogWarning("Chunk generation bindings are invalid. Region meta buffer missing.", this);
            }

            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.VoxelVolumeToWorldSpaceOffset, (Vector3)worldPosition);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_generateKernel, ComputeShaderProperties.VoxelVolume, voxelVolumeBuffer);
            BindRegionData(bindings);
            m_voxelConfig.VoxelVolumeConfig.Compute.Dispatch(m_generateKernel, m_voxelConfig.VoxelVolumeConfig.NumberOfVoxels);
        }

        public void ApplyVoxelVolumeCSGOperations(ComputeBuffer voxelVolumeBuffer, float3 worldPosition, List<GPUVoxelVolumeCSGOperation> voxelVolumeCSGOperations)
        {
            if (voxelVolumeBuffer == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeBuffer));
            }

            EnsureKernelIndices();
            if (!AreKernelsValid())
            {
                return;
            }

            if (voxelVolumeCSGOperations == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeCSGOperations));
            }

            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.VoxelVolumeToWorldSpaceOffset, (Vector3)worldPosition);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_csgKernel, ComputeShaderProperties.VoxelVolume, voxelVolumeBuffer);

            for (int index = 0; index < voxelVolumeCSGOperations.Count;)
            {
                int stride = math.min(m_voxelVolumeCSGOperationsBuffer.count, voxelVolumeCSGOperations.Count - index);

                m_voxelVolumeCSGOperationsBuffer.SetData(voxelVolumeCSGOperations, index, 0, stride);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(ComputeShaderProperties.NumberOfVoxelVolumeCSGOperations, stride);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_csgKernel, ComputeShaderProperties.VoxelVolumeCSGOperations, m_voxelVolumeCSGOperationsBuffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.Dispatch(m_csgKernel, m_voxelConfig.VoxelVolumeConfig.NumberOfVoxels);

                index += stride; 
            }
        }

        private void CreateBuffers()
        {
            if (m_generationGraphNodesBuffer == null || m_generationGraphNodesBuffer.count != m_voxelConfig.GenerationGraph.Nodes.Count)
            {
                m_generationGraphNodesBuffer?.Release();
                m_generationGraphNodesBuffer = new ComputeBuffer(math.max(m_voxelConfig.GenerationGraph.Nodes.Count, 1), GPUGenerationGraphNode.SizeInBytes);
            }

            if (m_voxelVolumeCSGOperationsBuffer == null || m_voxelVolumeCSGOperationsBuffer.count != m_voxelVolumeCSGOperationsBufferCapacity)
            {
                m_voxelVolumeCSGOperationsBuffer?.Release();
                m_voxelVolumeCSGOperationsBuffer = new ComputeBuffer(m_voxelVolumeCSGOperationsBufferCapacity, GPUVoxelVolumeCSGOperation.SizeInBytes);
            }
        }

        private void ReleaseBuffers()
        {
            if (m_generationGraphNodesBuffer != null)
            {
                m_generationGraphNodesBuffer.Release();
                m_generationGraphNodesBuffer = null;
            }

            if (m_voxelVolumeCSGOperationsBuffer != null)
            {
                m_voxelVolumeCSGOperationsBuffer.Release();
                m_voxelVolumeCSGOperationsBuffer = null;
            }
        }

        private void ApplyGenerationGraph()
        {
            EnsureKernelIndices();
            if (!AreKernelsValid())
            {
                return;
            }
            CreateBuffers();

            m_generationGraphNodesBuffer.SetData(m_voxelConfig.GenerationGraph.Nodes);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(ComputeShaderProperties.NumberOfGenerationGraphNodes, m_voxelConfig.GenerationGraph.Nodes.Count);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_generateKernel, ComputeShaderProperties.GenerationGraphNodes, m_generationGraphNodesBuffer);
        }

        private void BindRegionData(in ChunkGenerationBindings bindings)
        {
            EnsureKernelIndices();
            if (!AreKernelsValid())
            {
                return;
            }

            BindBufferSlice(ComputeShaderProperties.RegionSplines, ComputeShaderProperties.RegionSplinesStart, ComputeShaderProperties.RegionSplinesCount, bindings.Splines);
            BindBufferSlice(ComputeShaderProperties.RegionStamps, ComputeShaderProperties.RegionStampsStart, ComputeShaderProperties.RegionStampsCount, bindings.Stamps);

            if (bindings.RegionMetaBuffer != null)
            {
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_generateKernel, ComputeShaderProperties.RegionMeta, bindings.RegionMetaBuffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_csgKernel, ComputeShaderProperties.RegionMeta, bindings.RegionMetaBuffer);
            }

            Texture temperature = bindings.TemperatureTexture != null ? bindings.TemperatureTexture : Texture2D.grayTexture;
            Texture moisture = bindings.MoistureTexture != null ? bindings.MoistureTexture : Texture2D.grayTexture;

            m_voxelConfig.VoxelVolumeConfig.Compute.SetTexture(m_generateKernel, ComputeShaderProperties.ClimateTemperature, temperature);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetTexture(m_generateKernel, ComputeShaderProperties.ClimateMoisture, moisture);

            // Apply kernels do not sample climate textures directly; no binding required there.

            Vector2 uvScale = bindings.ClimateUvScale;
            Vector2 uvOffset = bindings.ClimateUvOffset;
            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.ClimateUvScale, new Vector4(uvScale.x, uvScale.y, 0.0f, 0.0f));
            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.ClimateUvOffset, new Vector4(uvOffset.x, uvOffset.y, 0.0f, 0.0f));
        }

        private void BindBufferSlice(int bufferPropertyId, int startPropertyId, int countPropertyId, BufferSlice slice)
        {
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(startPropertyId, slice.Start);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(countPropertyId, slice.Count);

            if (slice.Buffer != null)
            {
                EnsureKernelIndices();
                if (!AreKernelsValid())
                {
                    return;
                }
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_generateKernel, bufferPropertyId, slice.Buffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(m_csgKernel, bufferPropertyId, slice.Buffer);
            }
        }

        private void CacheKernelIndices()
        {
            ComputeShader compute = m_voxelConfig != null ? m_voxelConfig.VoxelVolumeConfig.Compute : null;

            if (compute == null)
            {
                Debug.LogWarning("VoxelVolumeConfig is missing a compute shader reference.", this);
                m_generateKernel = m_csgKernel = -1;
                return;
            }

            m_generateKernel = compute.FindKernel("GenerateVoxelVolume");
            m_csgKernel = compute.FindKernel("ApplyVoxelVolumeCSGOperations");

            if (m_generateKernel < 0 || m_csgKernel < 0)
            {
                Debug.LogError("Failed to locate voxel volume compute kernels.", this);
                m_generateKernel = m_csgKernel = -1;
                return;
            }

            try
            {
                compute.GetKernelThreadGroupSizes(m_generateKernel, out _, out _, out _);
                compute.GetKernelThreadGroupSizes(m_csgKernel, out _, out _, out _);
            }
            catch (Exception exception)
            {
                Debug.LogError($"VoxelVolume kernels could not query thread group sizes. Ensure the compute shader compiles without errors. {exception.Message}", this);
                m_generateKernel = m_csgKernel = -1;
            }
        }

        private void EnsureKernelIndices()
        {
            if (m_generateKernel < 0 || m_csgKernel < 0)
            {
                CacheKernelIndices();
            }
        }

        private bool AreKernelsValid()
        {
            if (m_generateKernel < 0 || m_csgKernel < 0)
            {
                Debug.LogError("Voxel volume compute kernels are not initialised.", this);
                return false;
            }

            return true;
        }
    }
}
