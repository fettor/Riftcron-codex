#ifndef TUNTENFISCH_VOXELS_CONTRACTS
#define TUNTENFISCH_VOXELS_CONTRACTS

// Shared data contracts between CPU and GPU. Keep layouts in sync with RegionPlan.cs.

struct SplineSeg
{
    float3 p0;
    float3 p1;
    float3 p2;
    float3 p3;
    float halfWidth;
    float depth;
    float softness;
    uint kind;
    uint materialOrTypeId;
    float3 boundsMin;
    float reserved0;
    float3 boundsMax;
    float reserved1;
};

struct Stamp
{
    float3 position;
    float reserved0;
    float4 rotation; // Quaternion (x, y, z, w)
    float3 scale;
    uint operation;
    float4 opParams; // Feature specific params (height, falloff, etc.)
    uint materialHint;
    uint reserved1;
    float3 boundsMin;
    float reserved2;
    float3 boundsMax;
    float reserved3;
};

struct RegionMeta
{
    float seaLevel;
    float waterTableLevel;
    uint biomePaletteKey;
    uint flags;
    uint temperatureTextureHandle;
    uint moistureTextureHandle;
    uint reservedTextureHandle0;
    uint reservedTextureHandle1;
};

// Enumerations (mirrors RegionPlan.cs)
static const uint SPLINE_KIND_ROAD = 0u;
static const uint SPLINE_KIND_RIVER = 1u;
static const uint SPLINE_KIND_ORE_VEIN = 2u;

static const uint STAMP_OPERATION_UNION = 0u;
static const uint STAMP_OPERATION_SUBTRACT = 1u;
static const uint STAMP_OPERATION_FLATTEN = 2u;
static const uint STAMP_OPERATION_ATLAS_UNION = 3u;
static const uint STAMP_OPERATION_ATLAS_SUBTRACT = 4u;

// Deterministic hashing (mirrors DeterministicRng.cs). Stateless, suitable for seed mixing.
static const uint HASH_C1 = 0x7FEB352Du;
static const uint HASH_C2 = 0x846CA68Bu;
static const uint HASH_GOLDEN_RATIO = 0x9E3779B9u;

uint DeterministicHash(uint value)
{
    value ^= HASH_GOLDEN_RATIO;
    value ^= value >> 16;
    value *= HASH_C1;
    value ^= value >> 15;
    value *= HASH_C2;
    value ^= value >> 16;
    return value;
}

uint DeterministicHash(uint seed, uint value)
{
    return DeterministicHash(seed ^ value);
}

float DeterministicHash01(uint seed)
{
    return DeterministicHash(seed) / 4294967295.0;
}

#endif // TUNTENFISCH_VOXELS_CONTRACTS
