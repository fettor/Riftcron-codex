using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tuntenfisch.World.Buffers
{
    /// <summary>
    /// Central registry for GPU buffers that mirror region plans. Buffers are created lazily
    /// per region and reused across chunk builds. The chunk view currently spans the entire region;
    /// finer-grained slicing is added in later phases.
    /// </summary>
    public sealed class BufferRegistry : IDisposable
    {
        public BufferRegistry()
        {
            m_regionBuffers = new Dictionary<RegionKey, RegionBuffers>();
        }

        public void Dispose()
        {
            foreach (RegionBuffers buffers in m_regionBuffers.Values)
            {
                buffers.Release();
            }

            m_regionBuffers.Clear();
        }

        public void UploadRegionPlan(RegionPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!m_regionBuffers.TryGetValue(plan.RegionKey, out RegionBuffers buffers))
            {
                buffers = new RegionBuffers();
                m_regionBuffers.Add(plan.RegionKey, buffers);
            }

            buffers.Upload(plan);
        }

        public ChunkBufferView GetChunkView(RegionKey regionKey)
        {
            if (!m_regionBuffers.TryGetValue(regionKey, out RegionBuffers buffers))
            {
                return ChunkBufferView.Empty;
            }

            return new ChunkBufferView(
                new BufferSlice(buffers.SplineBuffer, 0, buffers.SplineCount),
                new BufferSlice(buffers.StampBuffer, 0, buffers.StampCount),
                buffers.MetaBuffer,
                buffers.Meta);
        }

        public void CollectRegionStats(List<RegionBufferStats> stats)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            stats.Clear();

            foreach (KeyValuePair<RegionKey, RegionBuffers> entry in m_regionBuffers)
            {
                RegionBuffers buffers = entry.Value;
                stats.Add(new RegionBufferStats(entry.Key, buffers.SplineCount, buffers.StampCount));
            }
        }

        public void RemoveRegion(RegionKey key)
        {
            if (m_regionBuffers.TryGetValue(key, out RegionBuffers buffers))
            {
                buffers.Release();
                m_regionBuffers.Remove(key);
            }
        }

        private readonly Dictionary<RegionKey, RegionBuffers> m_regionBuffers;

        private sealed class RegionBuffers
        {
            public ComputeBuffer SplineBuffer { get; private set; }
            public ComputeBuffer StampBuffer { get; private set; }
            public ComputeBuffer MetaBuffer { get; private set; }
            public int SplineCount { get; private set; }
            public int StampCount { get; private set; }
            public RegionMeta Meta { get; private set; }

            private readonly RegionMeta[] m_metaScratch = new RegionMeta[1];

            public void Upload(RegionPlan plan)
            {
                List<SplineSeg> splines = plan.MutableSplines;
                List<Stamp> stamps = plan.MutableStamps;

                SplineCount = splines.Count;
                StampCount = stamps.Count;
                Meta = plan.Meta;

                SplineBuffer = EnsureBuffer(SplineBuffer, SplineCount, SplineSeg.SizeInBytes);
                StampBuffer = EnsureBuffer(StampBuffer, StampCount, Stamp.SizeInBytes);
                MetaBuffer = EnsureBuffer(MetaBuffer, 1, RegionMeta.SizeInBytes);

                if (SplineCount > 0)
                {
                    SplineBuffer.SetData(splines);
                }

                if (StampCount > 0)
                {
                    StampBuffer.SetData(stamps);
                }

                m_metaScratch[0] = Meta;
                MetaBuffer.SetData(m_metaScratch);
            }

            public void Release()
            {
                SplineBuffer = ReleaseBuffer(SplineBuffer);
                StampBuffer = ReleaseBuffer(StampBuffer);
                MetaBuffer = ReleaseBuffer(MetaBuffer);
                SplineCount = 0;
                StampCount = 0;
                Meta = default;
            }

            private static ComputeBuffer EnsureBuffer(ComputeBuffer buffer, int count, int stride)
            {
                int targetCount = Mathf.Max(1, count);

                if (buffer == null || buffer.count < targetCount)
                {
                    buffer = ReleaseBuffer(buffer);
                    buffer = new ComputeBuffer(targetCount, stride, ComputeBufferType.Structured);
                }

                return buffer;
            }

            private static ComputeBuffer ReleaseBuffer(ComputeBuffer buffer)
            {
                if (buffer != null)
                {
                    buffer.Release();
                }

                return null;
            }
        }
    }

    public readonly struct BufferSlice
    {
        public static BufferSlice Empty => new BufferSlice(null, 0, 0);

        public BufferSlice(ComputeBuffer buffer, int start, int count)
        {
            Buffer = buffer;
            Start = start;
            Count = count;
        }

        public ComputeBuffer Buffer { get; }
        public int Start { get; }
        public int Count { get; }
        public bool IsValid => Buffer != null && Count > 0;
    }

    public readonly struct ChunkBufferView
    {
        public static ChunkBufferView Empty => new ChunkBufferView(BufferSlice.Empty, BufferSlice.Empty, null, default);

        public ChunkBufferView(BufferSlice splines, BufferSlice stamps, ComputeBuffer metaBuffer, RegionMeta meta)
        {
            Splines = splines;
            Stamps = stamps;
            MetaBuffer = metaBuffer;
            Meta = meta;
        }

        public BufferSlice Splines { get; }
        public BufferSlice Stamps { get; }
        public ComputeBuffer MetaBuffer { get; }
        public RegionMeta Meta { get; }
        public bool IsValid => MetaBuffer != null;
    }

    public readonly struct RegionBufferStats
    {
        public RegionBufferStats(RegionKey region, int splineCount, int stampCount)
        {
            Region = region;
            SplineCount = splineCount;
            StampCount = stampCount;
        }

        public RegionKey Region { get; }
        public int SplineCount { get; }
        public int StampCount { get; }
    }
}
