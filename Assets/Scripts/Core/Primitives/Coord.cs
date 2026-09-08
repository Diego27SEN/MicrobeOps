#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>
    /// A board cell. Internally always (x, y) with origin (0,0) at the top-left corner.
    /// The player-facing notation is columns A-L and rows 1-12 (see design doc section 4.1).
    /// </summary>
    public readonly struct Coord : IEquatable<Coord>
    {
        /// <summary>Highest column index the A1 notation can express (A..Z).</summary>
        public const int MaxColumn = 25;

        public readonly byte X;
        public readonly byte Y;

        public Coord(byte x, byte y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Row-major index inside a board of the given width.</summary>
        public int ToIndex(int width)
        {
            return (Y * width) + X;
        }

        public static Coord FromIndex(int index, int width)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            return new Coord((byte)(index % width), (byte)(index / width));
        }

        /// <summary>
        /// Parses player-facing A1 notation ("C7" =&gt; x:2, y:6). Case insensitive.
        /// Returns false for anything malformed; it does NOT check board bounds, that is
        /// <see cref="BoardConfig"/>'s job.
        /// </summary>
        public static bool TryParse(string a1Notation, out Coord coord)
        {
            coord = default;
            if (string.IsNullOrEmpty(a1Notation)) return false;

            string text = a1Notation.Trim();
            if (text.Length < 2) return false;

            char letter = char.ToUpperInvariant(text[0]);
            if (letter < 'A' || letter > 'Z') return false;

            int row = 0;
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') return false;
                row = (row * 10) + (c - '0');
                if (row > 256) return false;
            }

            if (row < 1) return false;

            coord = new Coord((byte)(letter - 'A'), (byte)(row - 1));
            return true;
        }

        /// <summary>Player-facing A1 notation. Only valid for columns within A..Z.</summary>
        public string ToA1()
        {
            if (X > MaxColumn) throw new InvalidOperationException("Column index out of A1 notation range.");
            return string.Concat(((char)('A' + X)).ToString(), (Y + 1).ToString());
        }

        public bool Equals(Coord other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is Coord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (X << 8) | Y;
        }

        public static bool operator ==(Coord a, Coord b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Coord a, Coord b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return X <= MaxColumn ? ToA1() : string.Concat("(", X.ToString(), ",", Y.ToString(), ")");
        }
    }
}
