using Tuntenfisch.World;
using Unity.Mathematics;

namespace Tuntenfisch.World.Math
{
    /// <summary>
    /// Stateless hash-based RNG with a stable 32-bit mixing function.
    /// Mirrored in HLSL (see Contracts.hlsl) to ensure CPU/GPU determinism.
    /// </summary>
    public static class DeterministicRng
    {
        // Murmur3-style finalizer constants. Keep in sync with HLSL implementation.
        private const uint C1 = 0x7FEB352Du;
        private const uint C2 = 0x846CA68Bu;
        private const uint GoldenRatio32 = 0x9E3779B9u;

        public static uint Hash(int value) => Hash(unchecked((uint)value));

        public static uint Hash(uint value)
        {
            value ^= GoldenRatio32;
            value ^= value >> 16;
            value *= C1;
            value ^= value >> 15;
            value *= C2;
            value ^= value >> 16;
            return value;
        }

        public static uint Hash(uint seed, uint value) => Hash(seed ^ value);

        public static uint Hash(uint seed, int value) => Hash(seed, unchecked((uint)value));

        public static uint Hash(uint seed, RegionKey key)
        {
            uint h = Hash(seed, unchecked((uint)key.X));
            return Hash(h, unchecked((uint)key.Y));
        }

        public static float Range01(uint seed) => Hash(seed) / 4294967295.0f;

        public static float Range(float minInclusive, float maxInclusive, uint seed)
        {
            return math.lerp(minInclusive, maxInclusive, Range01(seed));
        }

        public static int Range(int minInclusive, int maxExclusive, uint seed)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint span = (uint)(maxExclusive - minInclusive);
            uint value = Hash(seed) % span;
            return minInclusive + (int)value;
        }
    }
}
