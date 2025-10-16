using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Tuntenfisch.World
{
    /// <summary>
    /// Region-level plan generated on the CPU. This is the only authoritative source
    /// of data that flows into the GPU chunk jobs through buffer uploads.
    /// </summary>
    [Serializable]
    public sealed class RegionPlan
    {
        public RegionKey RegionKey => m_regionKey;
        public RegionMeta Meta => m_meta;
        public IReadOnlyList<SplineSeg> Splines => m_splines;
        public IReadOnlyList<Stamp> Stamps => m_stamps;
        public IReadOnlyList<Texture2D> ClimateTiles => m_climateTiles;
        public IReadOnlyList<TownPlan> Towns => m_towns;
        public IReadOnlyList<float> WaterLevels => m_waterLevels;
        internal List<SplineSeg> MutableSplines => m_splines;
        internal List<Stamp> MutableStamps => m_stamps;

        public RegionPlan()
        {
            m_regionKey = default;
            m_meta = default;
        }

        public RegionPlan(RegionKey regionKey, RegionMeta meta)
        {
            m_regionKey = regionKey;
            m_meta = meta;
        }

        public void SetMeta(RegionMeta meta) => m_meta = meta;

        public void ClearTransientData()
        {
            m_splines.Clear();
            m_stamps.Clear();
            m_climateTiles.Clear();
            m_towns.Clear();
            m_waterLevels.Clear();
        }

        public void AddSpline(in SplineSeg spline) => m_splines.Add(spline);
        public void AddStamp(in Stamp stamp) => m_stamps.Add(stamp);
        public void AddClimateTile(Texture2D tile) => m_climateTiles.Add(tile);
        public void AddTown(TownPlan town) => m_towns.Add(town);
        public void AddWaterLevel(float height) => m_waterLevels.Add(height);

        [SerializeField]
        private RegionKey m_regionKey;
        [SerializeField]
        private RegionMeta m_meta;
        [SerializeField]
        private readonly List<SplineSeg> m_splines = new List<SplineSeg>();
        [SerializeField]
        private readonly List<Stamp> m_stamps = new List<Stamp>();
        [SerializeField]
        private readonly List<Texture2D> m_climateTiles = new List<Texture2D>(2);
        [SerializeField]
        private readonly List<TownPlan> m_towns = new List<TownPlan>();
        [SerializeField]
        private readonly List<float> m_waterLevels = new List<float>();
    }

    public enum SplineKind : uint
    {
        Road = 0u,
        River = 1u,
        OreVein = 2u
    }

    public enum StampOperation : uint
    {
        Union = 0u,
        Subtract = 1u,
        Flatten = 2u,
        AtlasUnion = 3u,
        AtlasSubtract = 4u
    }

    /// <summary>
    /// Cubic Hermite spline segment described by four control points.
    /// Memory layout: tightly packed to 16-byte boundaries (float4 padded) to match Contracts.hlsl.
    /// Units: world-space meters.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SplineSeg
    {
        public static int SizeInBytes => s_sizeInBytes;

        public float3 P0;
        public float3 P1;
        public float3 P2;
        public float3 P3;
        public float HalfWidth;
        public float Depth;
        public float Softness;
        public uint Kind;
        public uint MaterialOrTypeId;
        public float3 BoundsMin;
        public float Reserved0;
        public float3 BoundsMax;
        public float Reserved1;

        public SplineSeg(float3 p0, float3 p1, float3 p2, float3 p3, float halfWidth, float depth, float softness, SplineKind kind, uint materialOrTypeId, float3 boundsMin, float3 boundsMax)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
            HalfWidth = halfWidth;
            Depth = depth;
            Softness = softness;
            Kind = (uint)kind;
            MaterialOrTypeId = materialOrTypeId;
            BoundsMin = boundsMin;
            Reserved0 = 0.0f;
            BoundsMax = boundsMax;
            Reserved1 = 0.0f;
        }

        private static readonly int s_sizeInBytes = Marshal.SizeOf<SplineSeg>();
    }

    /// <summary>
    /// Analytic or atlas-based stamp.
    /// Layout mirrors Contracts.hlsl (16 byte alignment) and stores transforms in world space.
    /// OpParams is feature-specific (e.g., x = plateau height, y = falloff meters).
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Stamp
    {
        public static int SizeInBytes => s_sizeInBytes;

        public float3 Position;
        public float Reserved0;
        public float4 Rotation;
        public float3 Scale;
        public uint Operation;
        public float4 OpParams;
        public uint MaterialHint;
        public uint Reserved1;
        public float3 BoundsMin;
        public float Reserved2;
        public float3 BoundsMax;
        public float Reserved3;

        public Stamp(float3 position, quaternion rotation, float3 scale, StampOperation operation, float4 opParams, uint materialHint, float3 boundsMin, float3 boundsMax)
        {
            Position = position;
            Reserved0 = 0.0f;
            Rotation = new float4(rotation.value.x, rotation.value.y, rotation.value.z, rotation.value.w);
            Scale = scale;
            Operation = (uint)operation;
            OpParams = opParams;
            MaterialHint = materialHint;
            Reserved1 = 0u;
            BoundsMin = boundsMin;
            Reserved2 = 0.0f;
            BoundsMax = boundsMax;
            Reserved3 = 0.0f;
        }

        private static readonly int s_sizeInBytes = Marshal.SizeOf<Stamp>();
    }

    /// <summary>
    /// Compact region metadata: sea-levels, biome lookup keys, and bindless texture handles for climate tiles.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RegionMeta
    {
        public static int SizeInBytes => s_sizeInBytes;

        public float SeaLevel;
        public float WaterTableLevel;
        public uint BiomePaletteKey;
        public uint Flags;
        public uint TemperatureTextureHandle;
        public uint MoistureTextureHandle;
        public uint ReservedTextureHandle0;
        public uint ReservedTextureHandle1;

        public RegionMeta(float seaLevel, float waterTableLevel, uint biomePaletteKey, uint flags, uint temperatureTextureHandle, uint moistureTextureHandle)
        {
            SeaLevel = seaLevel;
            WaterTableLevel = waterTableLevel;
            BiomePaletteKey = biomePaletteKey;
            Flags = flags;
            TemperatureTextureHandle = temperatureTextureHandle;
            MoistureTextureHandle = moistureTextureHandle;
            ReservedTextureHandle0 = 0u;
            ReservedTextureHandle1 = 0u;
        }

        private static readonly int s_sizeInBytes = Marshal.SizeOf<RegionMeta>();
    }

    [Serializable]
    public struct TownPlan
    {
        public string Identifier;
        public float2 Center;
        public float Radius;
        public uint Population;
    }
}
