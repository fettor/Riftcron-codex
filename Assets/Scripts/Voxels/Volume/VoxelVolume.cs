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

        private void Awake()
        {
            m_voxelConfig = GetComponent<VoxelConfig>();
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
            }
        }

        public void GenerateVoxelVolume(ComputeBuffer voxelVolumeBuffer, float3 worldPosition, in ChunkGenerationBindings bindings)
        {
            if (voxelVolumeBuffer == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeBuffer));
            }

            if (!bindings.IsValid)
            {
                Debug.LogWarning("Chunk generation bindings are invalid. Region meta buffer missing.", this);
            }

            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.VoxelVolumeToWorldSpaceOffset, (Vector3)worldPosition);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(0, ComputeShaderProperties.VoxelVolume, voxelVolumeBuffer);
            BindRegionData(bindings);
            m_voxelConfig.VoxelVolumeConfig.Compute.Dispatch(0, m_voxelConfig.VoxelVolumeConfig.NumberOfVoxels);
        }

        public void ApplyVoxelVolumeCSGOperations(ComputeBuffer voxelVolumeBuffer, float3 worldPosition, List<GPUVoxelVolumeCSGOperation> voxelVolumeCSGOperations)
        {
            if (voxelVolumeBuffer == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeBuffer));
            }

            if (voxelVolumeCSGOperations == null)
            {
                throw new ArgumentNullException(nameof(voxelVolumeCSGOperations));
            }

            m_voxelConfig.VoxelVolumeConfig.Compute.SetVector(ComputeShaderProperties.VoxelVolumeToWorldSpaceOffset, (Vector3)worldPosition);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(1, ComputeShaderProperties.VoxelVolume, voxelVolumeBuffer);

            for (int index = 0; index < voxelVolumeCSGOperations.Count;)
            {
                int stride = math.min(m_voxelVolumeCSGOperationsBuffer.count, voxelVolumeCSGOperations.Count - index);

                m_voxelVolumeCSGOperationsBuffer.SetData(voxelVolumeCSGOperations, index, 0, stride);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(ComputeShaderProperties.NumberOfVoxelVolumeCSGOperations, stride);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(1, ComputeShaderProperties.VoxelVolumeCSGOperations, m_voxelVolumeCSGOperationsBuffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.Dispatch(1, m_voxelConfig.VoxelVolumeConfig.NumberOfVoxels);

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
            CreateBuffers();

            m_generationGraphNodesBuffer.SetData(m_voxelConfig.GenerationGraph.Nodes);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(ComputeShaderProperties.NumberOfGenerationGraphNodes, m_voxelConfig.GenerationGraph.Nodes.Count);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(0, ComputeShaderProperties.GenerationGraphNodes, m_generationGraphNodesBuffer);
        }

        private void BindRegionData(in ChunkGenerationBindings bindings)
        {
            BindBufferSlice(ComputeShaderProperties.RegionSplines, ComputeShaderProperties.RegionSplinesStart, ComputeShaderProperties.RegionSplinesCount, bindings.Splines);
            BindBufferSlice(ComputeShaderProperties.RegionStamps, ComputeShaderProperties.RegionStampsStart, ComputeShaderProperties.RegionStampsCount, bindings.Stamps);

            if (bindings.RegionMetaBuffer != null)
            {
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(0, ComputeShaderProperties.RegionMeta, bindings.RegionMetaBuffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(1, ComputeShaderProperties.RegionMeta, bindings.RegionMetaBuffer);
            }

            Texture temperature = bindings.TemperatureTexture != null ? bindings.TemperatureTexture : Texture2D.grayTexture;
            Texture moisture = bindings.MoistureTexture != null ? bindings.MoistureTexture : Texture2D.grayTexture;

            m_voxelConfig.VoxelVolumeConfig.Compute.SetTexture(0, ComputeShaderProperties.ClimateTemperature, temperature);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetTexture(0, ComputeShaderProperties.ClimateMoisture, moisture);
        }

        private void BindBufferSlice(int bufferPropertyId, int startPropertyId, int countPropertyId, BufferSlice slice)
        {
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(startPropertyId, slice.Start);
            m_voxelConfig.VoxelVolumeConfig.Compute.SetInt(countPropertyId, slice.Count);

            if (slice.Buffer != null)
            {
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(0, bufferPropertyId, slice.Buffer);
                m_voxelConfig.VoxelVolumeConfig.Compute.SetBuffer(1, bufferPropertyId, slice.Buffer);
            }
        }
    }
}
