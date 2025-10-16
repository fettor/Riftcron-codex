#ifndef TUNTENFISCH_VOXELS_FEATURE_SWITCHES
#define TUNTENFISCH_VOXELS_FEATURE_SWITCHES

// Feature toggles (set to 1 to enable). Individual passes consult these values.
#define FEATURE_BASE_TERRAIN 1
#define FEATURE_MOUNTAINS 0
#define FEATURE_RIVERS 0
#define FEATURE_ROADS 0
#define FEATURE_STAMPS 0
#define FEATURE_CAVES 0
#define FEATURE_ORES 0
#define FEATURE_TREES 0

// Reserved material IDs (mirror MaterialIndex.cs). Ores occupy a contiguous range.
static const uint MATERIAL_ID_DIRT = 0u;
static const uint MATERIAL_ID_ROCK = 1u;
static const uint MATERIAL_ID_SAND = 2u;
static const uint MATERIAL_ID_GRASS = 3u;
static const uint MATERIAL_ID_WOOD = 4u;
static const uint MATERIAL_ID_LEAVES = 5u;
static const uint MATERIAL_ID_ORE_BEGIN = 32u;
static const uint MATERIAL_ID_ORE_END = 63u;
static const uint MATERIAL_ID_ORE_COUNT = MATERIAL_ID_ORE_END - MATERIAL_ID_ORE_BEGIN + 1u;

#endif // TUNTENFISCH_VOXELS_FEATURE_SWITCHES
