using System.Runtime.InteropServices;

namespace Tuntenfisch.Voxels.Volume
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PackedVoxel
    {
        public uint PackedValueAndMaterialIndex;
        public uint PackedGradient;

        public PackedVoxel(uint packedValueAndMaterialIndex, uint packedGradient)
        {
            PackedValueAndMaterialIndex = packedValueAndMaterialIndex;
            PackedGradient = packedGradient;
        }

        public static PackedVoxel Empty => default;
    }
}
