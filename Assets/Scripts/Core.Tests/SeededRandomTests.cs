#nullable enable

using System;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class SeededRandomTests
    {
        [Test]
        public void Next_StaysWithinRange()
        {
            IRandom rng = new SeededRandom(1234);

            for (int i = 0; i < 10000; i++)
            {
                int value = rng.Next(100);
                Assert.That(value, Is.InRange(0, 99));
            }
        }

        [Test]
        public void Next_WithBoundOne_AlwaysReturnsZero()
        {
            IRandom rng = new SeededRandom(1234);
            Assert.That(rng.Next(1), Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Next_WithNonPositiveBound_Throws(int bound)
        {
            IRandom rng = new SeededRandom(1234);
            Assert.That(() => rng.Next(bound), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void SameSeed_ProducesTheSameSequence()
        {
            // This is the property AI determinism rests on (design doc section 12.2, invariant 7).
            // System.Random cannot provide it: its algorithm changed in .NET 6, so Unity's Mono and
            // the module's net9.0 runtime would disagree. Hence the hand-rolled generator.
            IRandom a = new SeededRandom(987654321);
            IRandom b = new SeededRandom(987654321);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(a.Next(144), Is.EqualTo(b.Next(144)));
            }
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            IRandom a = new SeededRandom(1);
            IRandom b = new SeededRandom(2);

            bool diverged = false;
            for (int i = 0; i < 100 && !diverged; i++)
            {
                if (a.Next(1000) != b.Next(1000)) diverged = true;
            }

            Assert.That(diverged, Is.True);
        }

        [Test]
        public void Distribution_IsReasonablyUniform()
        {
            IRandom rng = new SeededRandom(42);
            int[] buckets = new int[10];
            const int samples = 100000;

            for (int i = 0; i < samples; i++) buckets[rng.Next(buckets.Length)]++;

            int expected = samples / buckets.Length;
            for (int i = 0; i < buckets.Length; i++)
            {
                Assert.That(buckets[i], Is.InRange(expected * 0.9, expected * 1.1), "bucket " + i + " is skewed");
            }
        }

        [Test]
        public void ZeroSeed_DoesNotCollapseTheGenerator()
        {
            // A xorshift state of zero would emit zeroes forever; the constructor must avoid it.
            IRandom rng = new SeededRandom(0);

            bool sawNonZero = false;
            for (int i = 0; i < 50 && !sawNonZero; i++)
            {
                if (rng.Next(1000) != 0) sawNonZero = true;
            }

            Assert.That(sawNonZero, Is.True);
        }
    }
}
