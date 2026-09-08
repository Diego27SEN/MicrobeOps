#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class BitBoardTests
    {
        [Test]
        public void Empty_HasNoBitsSet()
        {
            Assert.That(BitBoard.Empty.IsEmpty, Is.True);
            Assert.That(BitBoard.Empty.PopCount, Is.EqualTo(0));
        }

        [Test]
        public void Set_IsImmutable_AndLeavesTheOriginalUntouched()
        {
            BitBoard original = BitBoard.Empty;
            BitBoard modified = original.Set(42);

            Assert.That(original.Get(42), Is.False);
            Assert.That(modified.Get(42), Is.True);
        }

        [Test]
        public void SetAndGet_WorkAcrossAllThreeWords()
        {
            int[] probes = { 0, 63, 64, 127, 128, 143, BitBoard.Capacity - 1 };

            foreach (int index in probes)
            {
                BitBoard board = BitBoard.Empty.Set(index);
                Assert.That(board.Get(index), Is.True, "bit " + index + " should be set");
                Assert.That(board.PopCount, Is.EqualTo(1), "only bit " + index + " should be set");
            }
        }

        [Test]
        public void Set_IsIdempotent()
        {
            BitBoard board = BitBoard.Empty.Set(7).Set(7);
            Assert.That(board.PopCount, Is.EqualTo(1));
        }

        [Test]
        public void Clear_RemovesOnlyTheGivenBit()
        {
            BitBoard board = BitBoard.Empty.Set(10).Set(11).Clear(10);

            Assert.That(board.Get(10), Is.False);
            Assert.That(board.Get(11), Is.True);
            Assert.That(board.PopCount, Is.EqualTo(1));
        }

        [Test]
        public void PopCount_CountsEveryCellOfTheLargestBoard()
        {
            BitBoard board = BitBoard.Empty;
            for (int i = 0; i < 144; i++) board = board.Set(i);

            Assert.That(board.PopCount, Is.EqualTo(144));
        }

        [TestCase(-1)]
        [TestCase(BitBoard.Capacity)]
        public void Get_OutsideCapacity_Throws(int index)
        {
            Assert.That(() => BitBoard.Empty.Get(index), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Or_And_AndNot_BehaveAsSetOperations()
        {
            BitBoard a = BitBoard.FromIndices(new[] { 1, 2, 3 });
            BitBoard b = BitBoard.FromIndices(new[] { 3, 4 });

            Assert.That(a.Or(b).ToIndices(), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(a.And(b).ToIndices(), Is.EqualTo(new[] { 3 }));
            Assert.That(a.AndNot(b).ToIndices(), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Intersects_IsTrueOnlyWhenTheyShareABit()
        {
            BitBoard a = BitBoard.FromIndices(new[] { 1, 2 });

            Assert.That(a.Intersects(BitBoard.FromIndices(new[] { 2, 9 })), Is.True);
            Assert.That(a.Intersects(BitBoard.FromIndices(new[] { 3, 9 })), Is.False);
        }

        [Test]
        public void Contains_IsTrueWhenEveryBitOfTheOtherIsPresent()
        {
            BitBoard fleet = BitBoard.FromIndices(new[] { 5, 6, 7, 8 });

            Assert.That(fleet.Contains(BitBoard.FromIndices(new[] { 6, 7 })), Is.True);
            Assert.That(fleet.Contains(BitBoard.FromIndices(new[] { 6, 99 })), Is.False);
            Assert.That(fleet.Contains(BitBoard.Empty), Is.True);
        }

        [Test]
        public void ToIndices_ReturnsSetBitsInAscendingOrder()
        {
            IReadOnlyList<int> indices = BitBoard.FromIndices(new[] { 130, 4, 64 }).ToIndices();

            Assert.That(indices, Is.EqualTo(new[] { 4, 64, 130 }));
        }

        [Test]
        public void Base64_RoundTripsExactly()
        {
            BitBoard original = BitBoard.FromIndices(new[] { 0, 1, 63, 64, 100, 143 });
            BitBoard restored = BitBoard.FromBase64(original.ToBase64());

            Assert.That(restored, Is.EqualTo(original));
        }

        [Test]
        public void ToBytes_UsesLittleEndianLayout_SoTheWireFormatIsRuntimeIndependent()
        {
            byte[] bytes = BitBoard.Empty.Set(0).Set(8).ToBytes();

            Assert.That(bytes.Length, Is.EqualTo(BitBoard.ByteCount));
            Assert.That(bytes[0], Is.EqualTo(0x01));
            Assert.That(bytes[1], Is.EqualTo(0x01));
        }

        [Test]
        public void FromBytes_OnWrongLength_Throws()
        {
            Assert.That(() => BitBoard.FromBytes(new byte[8]), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Equality_ComparesContent()
        {
            BitBoard a = BitBoard.FromIndices(new[] { 3, 70 });
            BitBoard b = BitBoard.Empty.Set(70).Set(3);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }
    }
}
