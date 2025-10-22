using UnityEngine;

namespace Tuntenfisch.Voxels
{
    public static class ComputeShaderProperties
    {
        public static readonly int CellStride = Shader.PropertyToID("cellStride");
        public static readonly int CellVertexInfoLookupTable = Shader.PropertyToID("cellVertexInfoLookupTable");
        public static readonly int GeneratedTriangles = Shader.PropertyToID("generatedTriangles");
        public static readonly int GeneratedVertices0 = Shader.PropertyToID("generatedVertices0");
        public static readonly int GeneratedVertices1 = Shader.PropertyToID("generatedVertices1");
        public static readonly int GenerationGraphNodes = Shader.PropertyToID("generationGraphNodes");
        public static readonly int NumberOfGenerationGraphNodes = Shader.PropertyToID("numberOfGenerationGraphNodes");
        public static readonly int NumberOfVoxels = Shader.PropertyToID("numberOfVoxels");
        public static readonly int NumberOfVoxelVolumeCSGOperations = Shader.PropertyToID("numberOfVoxelVolumeCSGOperations");
        public static readonly int RegionSplines = Shader.PropertyToID("regionSplines");
        public static readonly int RegionSplinesStart = Shader.PropertyToID("regionSplinesStart");
        public static readonly int RegionSplinesCount = Shader.PropertyToID("regionSplinesCount");
        public static readonly int RegionStamps = Shader.PropertyToID("regionStamps");
        public static readonly int RegionStampsStart = Shader.PropertyToID("regionStampsStart");
        public static readonly int RegionStampsCount = Shader.PropertyToID("regionStampsCount");
        public static readonly int RegionMeta = Shader.PropertyToID("regionMeta");
        public static readonly int ClimateTemperature = Shader.PropertyToID("climateTemperature");
        public static readonly int ClimateMoisture = Shader.PropertyToID("climateMoisture");
        public static readonly int ClimateUvScale = Shader.PropertyToID("climateUVScale");
        public static readonly int ClimateUvOffset = Shader.PropertyToID("climateUVOffset");
        public static readonly int SchmitzParticleIterations = Shader.PropertyToID("schmitzParticleIterations");
        public static readonly int SchmitzParticleStepSize = Shader.PropertyToID("schmitzParticleStepSize");
        public static readonly int SubSampledCellVolumeFaces = Shader.PropertyToID("subSampledCellVolumeFaces");
        public static readonly int VoxelSpacing = Shader.PropertyToID("voxelSpacing");
        public static readonly int VoxelVolume = Shader.PropertyToID("voxelVolume");
        public static readonly int VoxelVolumeCSGOperations = Shader.PropertyToID("voxelVolumeCSGOperations");
        public static readonly int VoxelVolumeToWorldSpaceOffset = Shader.PropertyToID("voxelVolumeToWorldSpaceOffset");
        public static readonly int MountainMaskTexture = Shader.PropertyToID("mountainMaskTexture");
        public static readonly int WriteMountainMask = Shader.PropertyToID("writeMountainMask");
    }
}
