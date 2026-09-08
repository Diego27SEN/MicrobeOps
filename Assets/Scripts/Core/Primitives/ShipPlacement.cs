#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>
    /// One ship laid on the board: its class, the bow cell and the direction it extends towards
    /// (right for <see cref="Orientation.Horizontal"/>, down for <see cref="Orientation.Vertical"/>).
    /// Diagonal placement does not exist and never will (design doc section 4.3).
    /// </summary>
    public readonly struct ShipPlacement : IEquatable<ShipPlacement>
    {
        public readonly ShipClass Class;
        public readonly Coord Bow;
        public readonly Orientation Orientation;

        public ShipPlacement(ShipClass shipClass, Coord bow, Orientation orientation)
        {
            Class = shipClass;
            Bow = bow;
            Orientation = orientation;
        }

        /// <summary>Cell at <paramref name="offset"/> steps from the bow along the orientation.</summary>
        public Coord CellAt(int offset)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            return Orientation == Orientation.Horizontal
                ? new Coord((byte)(Bow.X + offset), Bow.Y)
                : new Coord(Bow.X, (byte)(Bow.Y + offset));
        }

        public bool Equals(ShipPlacement other)
        {
            return Class == other.Class && Bow == other.Bow && Orientation == other.Orientation;
        }

        public override bool Equals(object? obj)
        {
            return obj is ShipPlacement other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Class << 24) ^ ((int)Orientation << 20) ^ Bow.GetHashCode();
        }

        public static bool operator ==(ShipPlacement a, ShipPlacement b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ShipPlacement a, ShipPlacement b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return string.Concat(Class.ToString(), "@", Bow.ToString(), Orientation == Orientation.Horizontal ? "H" : "V");
        }
    }
}
