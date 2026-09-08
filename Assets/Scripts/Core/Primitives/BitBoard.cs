#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Grid occupancy as bits. Three <see cref="ulong"/> words cover 192 bits, which is enough for
    /// the largest board in scope (12x12 = 144 cells, design doc section 4.1).
    /// <para>
    /// Immutable by design: every mutating operation returns a new value. That is what lets
    /// <see cref="MatchState"/> be copied cheaply and compared in tests without aliasing bugs.
    /// </para>
    /// </summary>
    public readonly struct BitBoard : IEquatable<BitBoard>
    {
        /// <summary>Number of addressable bits.</summary>
        public const int Capacity = 192;

        /// <summary>Serialized size in bytes (three little-endian 64-bit words).</summary>
        public const int ByteCount = 24;

        private readonly ulong _w0;
        private readonly ulong _w1;
        private readonly ulong _w2;

        private BitBoard(ulong w0, ulong w1, ulong w2)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
        }

        public static BitBoard Empty
        {
            get { return default; }
        }

        public bool IsEmpty
        {
            get { return (_w0 | _w1 | _w2) == 0UL; }
        }

        public bool Get(int index)
        {
            ThrowIfOutOfRange(index);
            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if (word == 0) return (_w0 & mask) != 0UL;
            if (word == 1) return (_w1 & mask) != 0UL;
            return (_w2 & mask) != 0UL;
        }

        public BitBoard Set(int index)
        {
            ThrowIfOutOfRange(index);
            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if (word == 0) return new BitBoard(_w0 | mask, _w1, _w2);
            if (word == 1) return new BitBoard(_w0, _w1 | mask, _w2);
            return new BitBoard(_w0, _w1, _w2 | mask);
        }

        public BitBoard Clear(int index)
        {
            ThrowIfOutOfRange(index);
            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if (word == 0) return new BitBoard(_w0 & ~mask, _w1, _w2);
            if (word == 1) return new BitBoard(_w0, _w1 & ~mask, _w2);
            return new BitBoard(_w0, _w1, _w2 & ~mask);
        }

        public int PopCount
        {
            get { return PopCount64(_w0) + PopCount64(_w1) + PopCount64(_w2); }
        }

        public BitBoard Or(in BitBoard other)
        {
            return new BitBoard(_w0 | other._w0, _w1 | other._w1, _w2 | other._w2);
        }

        public BitBoard And(in BitBoard other)
        {
            return new BitBoard(_w0 & other._w0, _w1 & other._w1, _w2 & other._w2);
        }

        public BitBoard AndNot(in BitBoard other)
        {
            return new BitBoard(_w0 & ~other._w0, _w1 & ~other._w1, _w2 & ~other._w2);
        }

        public bool Intersects(in BitBoard other)
        {
            return ((_w0 & other._w0) | (_w1 & other._w1) | (_w2 & other._w2)) != 0UL;
        }

        /// <summary>True when every bit set in <paramref name="other"/> is also set here.</summary>
        public bool Contains(in BitBoard other)
        {
            return (other._w0 & ~_w0) == 0UL
                && (other._w1 & ~_w1) == 0UL
                && (other._w2 & ~_w2) == 0UL;
        }

        public IReadOnlyList<int> ToIndices()
        {
            List<int> result = new List<int>(PopCount);
            for (int i = 0; i < Capacity; i++)
            {
                if (Get(i)) result.Add(i);
            }
            return result;
        }

        public static BitBoard FromIndices(IReadOnlyList<int> indices)
        {
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            BitBoard board = Empty;
            for (int i = 0; i < indices.Count; i++)
            {
                board = board.Set(indices[i]);
            }
            return board;
        }

        /// <summary>
        /// Little-endian byte layout, written explicitly so the encoding is identical on every
        /// runtime the same state travels through (Unity client, Cloud Code, tests).
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[ByteCount];
            WriteWord(bytes, 0, _w0);
            WriteWord(bytes, 8, _w1);
            WriteWord(bytes, 16, _w2);
            return bytes;
        }

        public static BitBoard FromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != ByteCount) throw new ArgumentException("Expected " + ByteCount + " bytes.", nameof(bytes));
            return new BitBoard(ReadWord(bytes, 0), ReadWord(bytes, 8), ReadWord(bytes, 16));
        }

        public string ToBase64()
        {
            return Convert.ToBase64String(ToBytes());
        }

        public static BitBoard FromBase64(string base64)
        {
            if (base64 == null) throw new ArgumentNullException(nameof(base64));
            return FromBytes(Convert.FromBase64String(base64));
        }

        public bool Equals(BitBoard other)
        {
            return _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2;
        }

        public override bool Equals(object? obj)
        {
            return obj is BitBoard other && Equals(other);
        }

        public override int GetHashCode()
        {
            ulong mixed = _w0 ^ (_w1 * 0x9E3779B97F4A7C15UL) ^ (_w2 * 0xC2B2AE3D27D4EB4FUL);
            return (int)(mixed ^ (mixed >> 32));
        }

        public static bool operator ==(BitBoard a, BitBoard b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(BitBoard a, BitBoard b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return string.Concat("BitBoard(", PopCount.ToString(), " set)");
        }

        private static void ThrowIfOutOfRange(int index)
        {
            if ((uint)index >= Capacity) throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>
        /// SWAR population count. Hand-rolled because <c>System.Numerics.BitOperations</c> is not
        /// available on netstandard2.1, which is the target Core must keep to stay shareable
        /// with Unity.
        /// </summary>
        private static int PopCount64(ulong value)
        {
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
        }

        private static void WriteWord(byte[] target, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                target[offset + i] = (byte)(value >> (i * 8));
            }
        }

        private static ulong ReadWord(byte[] source, int offset)
        {
            ulong value = 0UL;
            for (int i = 0; i < 8; i++)
            {
                value |= (ulong)source[offset + i] << (i * 8);
            }
            return value;
        }
    }
}
