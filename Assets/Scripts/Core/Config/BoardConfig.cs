#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>One ship slot in a fleet: which class it is and how long it is on this board.</summary>
    public readonly struct ShipSpec : IEquatable<ShipSpec>
    {
        public readonly ShipClass Class;
        public readonly int Length;

        public ShipSpec(ShipClass shipClass, int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            Class = shipClass;
            Length = length;
        }

        public bool Equals(ShipSpec other)
        {
            return Class == other.Class && Length == other.Length;
        }

        public override bool Equals(object? obj)
        {
            return obj is ShipSpec other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Class * 397) ^ Length;
        }

        public override string ToString()
        {
            return string.Concat(Class.ToString(), ":", Length.ToString());
        }
    }

    /// <summary>
    /// A board layout and the fleet that goes on it. Values come from design doc section 4.1 and
    /// are mirrored in Remote Config under <c>BOARD_CONFIGS</c>; the embedded copy lives in
    /// <see cref="DefaultGameConfig"/>.
    /// </summary>
    public sealed class BoardConfig
    {
        public const string ClassicId = "classic";
        public const string BlitzId = "blitz";
        public const string AdmiralId = "admiral";

        public string Id { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public IReadOnlyList<ShipSpec> Fleet { get; set; } = Array.Empty<ShipSpec>();

        public int CellCount
        {
            get { return Width * Height; }
        }

        /// <summary>Total cells occupied by the fleet. 12 on blitz, 17 on classic, 26 on admiral.</summary>
        public int FleetCellCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Fleet.Count; i++) total += Fleet[i].Length;
                return total;
            }
        }

        public bool Contains(Coord cell)
        {
            return cell.X < Width && cell.Y < Height;
        }

        public int ToIndex(Coord cell)
        {
            return cell.ToIndex(Width);
        }

        public Coord FromIndex(int index)
        {
            return Coord.FromIndex(index, Width);
        }
    }
}
