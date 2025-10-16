using System;
using Unity.Mathematics;

namespace Tuntenfisch.World
{
    /// <summary>
    /// Immutable identifier for a region in the world planner grid.
    /// The grid is addressed in the XZ plane, using integer coordinates.
    /// </summary>
    [Serializable]
    public readonly struct RegionKey : IEquatable<RegionKey>
    {
        public int X { get; }
        public int Y { get; }

        public RegionKey(int x, int y)
        {
            X = x;
            Y = y;
        }

        public RegionKey(int2 value)
        {
            X = value.x;
            Y = value.y;
        }

        public int2 AsInt2() => new int2(X, Y);

        public bool Equals(RegionKey other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is RegionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(RegionKey left, RegionKey right) => left.Equals(right);
        public static bool operator !=(RegionKey left, RegionKey right) => !left.Equals(right);

        public override string ToString() => $"RegionKey({X}, {Y})";
    }
}
