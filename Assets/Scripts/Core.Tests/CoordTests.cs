#nullable enable

using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    // Only the constraint model is used across this suite (Assert.That / Is.*), because it is the
    // one API that behaves identically on Unity's bundled NUnit 3 and on the NUnit 4 the headless
    // runner uses. Classic asserts moved to ClassicAssert in NUnit 4 and would not compile in both.
    [TestFixture]
    public sealed class CoordTests
    {
        [Test]
        public void TryParse_OnValidA1Notation_ReturnsZeroBasedCoord()
        {
            bool parsed = Coord.TryParse("C7", out Coord coord);

            Assert.That(parsed, Is.True);
            Assert.That(coord.X, Is.EqualTo(2));
            Assert.That(coord.Y, Is.EqualTo(6));
        }

        [Test]
        public void TryParse_OnLowercaseNotation_IsCaseInsensitive()
        {
            Assert.That(Coord.TryParse("a1", out Coord coord), Is.True);
            Assert.That(coord, Is.EqualTo(new Coord(0, 0)));
        }

        [Test]
        public void TryParse_OnLargestBoardCorner_ParsesL12()
        {
            Assert.That(Coord.TryParse("L12", out Coord coord), Is.True);
            Assert.That(coord, Is.EqualTo(new Coord(11, 11)));
        }

        [TestCase("")]
        [TestCase("C")]
        [TestCase("7")]
        [TestCase("C0")]
        [TestCase("CC7")]
        [TestCase("C7x")]
        [TestCase("!1")]
        public void TryParse_OnMalformedInput_ReturnsFalse(string input)
        {
            Assert.That(Coord.TryParse(input, out _), Is.False);
        }

        [Test]
        public void ToA1_RoundTripsThroughTryParse_ForEveryCellOfTheLargestBoard()
        {
            for (byte y = 0; y < 12; y++)
            {
                for (byte x = 0; x < 12; x++)
                {
                    Coord original = new Coord(x, y);
                    Assert.That(Coord.TryParse(original.ToA1(), out Coord parsed), Is.True);
                    Assert.That(parsed, Is.EqualTo(original), "Round trip failed for " + original.ToA1());
                }
            }
        }

        [Test]
        public void ToIndex_UsesRowMajorOrder()
        {
            Assert.That(new Coord(2, 6).ToIndex(10), Is.EqualTo(62));
            Assert.That(new Coord(0, 0).ToIndex(10), Is.EqualTo(0));
            Assert.That(new Coord(9, 9).ToIndex(10), Is.EqualTo(99));
        }

        [Test]
        public void FromIndex_IsTheInverseOfToIndex()
        {
            const int width = 12;
            for (int index = 0; index < width * 12; index++)
            {
                Coord coord = Coord.FromIndex(index, width);
                Assert.That(coord.ToIndex(width), Is.EqualTo(index));
            }
        }

        [Test]
        public void Equality_ComparesBothAxes()
        {
            Coord subject = new Coord(3, 4);
            Coord same = Coord.FromIndex(new Coord(3, 4).ToIndex(10), 10);
            Coord transposed = new Coord(4, 3);

            Assert.That(subject, Is.EqualTo(same));
            Assert.That(subject, Is.Not.EqualTo(transposed));
            Assert.That(subject == same, Is.True);
            Assert.That(subject != transposed, Is.True);
        }
    }
}
