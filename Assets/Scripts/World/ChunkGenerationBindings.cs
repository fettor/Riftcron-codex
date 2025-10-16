using Tuntenfisch.World.Buffers;
using UnityEngine;

namespace Tuntenfisch.World
{
    /// <summary>
    /// CPU-computed inputs that must be bound before running the GPU chunk generation kernels.
    /// </summary>
    public readonly struct ChunkGenerationBindings
    {
        public static ChunkGenerationBindings Empty => new ChunkGenerationBindings(default, BufferSlice.Empty, BufferSlice.Empty, null, null, null, default);

        public ChunkGenerationBindings(RegionKey regionKey, BufferSlice splines, BufferSlice stamps, ComputeBuffer regionMetaBuffer, Texture temperatureTexture, Texture moistureTexture, RegionMeta meta)
        {
            RegionKey = regionKey;
            Splines = splines;
            Stamps = stamps;
            RegionMetaBuffer = regionMetaBuffer;
            TemperatureTexture = temperatureTexture;
            MoistureTexture = moistureTexture;
            Meta = meta;
        }

        public RegionKey RegionKey { get; }
        public BufferSlice Splines { get; }
        public BufferSlice Stamps { get; }
        public ComputeBuffer RegionMetaBuffer { get; }
        public Texture TemperatureTexture { get; }
        public Texture MoistureTexture { get; }
        public RegionMeta Meta { get; }
        public bool IsValid => RegionMetaBuffer != null;
    }
}
