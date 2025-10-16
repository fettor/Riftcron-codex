#ifndef TUNTENFISCH_VOXELS_MATERIAL
#define TUNTENFISCH_VOXELS_MATERIAL

#include "Assets/Compute/Include/Enumeration.hlsl"
#include "Assets/Compute/Voxels/Include/FeatureSwitches.hlsl"

ENUM MaterialIndex
{
    static const uint Dirt = MATERIAL_ID_DIRT;
    static const uint Rock = MATERIAL_ID_ROCK;
    static const uint Sand = MATERIAL_ID_SAND;
    static const uint Grass = MATERIAL_ID_GRASS;
    static const uint Wood = MATERIAL_ID_WOOD;
    static const uint Leaves = MATERIAL_ID_LEAVES;
    static const uint OreBegin = MATERIAL_ID_ORE_BEGIN;
    static const uint OreEnd = MATERIAL_ID_ORE_END;
    static const uint OreCount = MATERIAL_ID_ORE_COUNT;
};

static const uint numberOfMaterials = 4;

#endif
