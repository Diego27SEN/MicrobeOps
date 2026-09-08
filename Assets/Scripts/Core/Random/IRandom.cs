#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>
    /// The only randomness seam in Core. Core never touches ambient RNG.
    /// Tests use <see cref="SeededRandom"/>; Cloud Code uses a CSPRNG adapter; the offline
    /// client uses a Unity adapter.
    /// </summary>
    public interface IRandom
    {
        /// <summary>Uniform value in [0, maxExclusive).</summary>
        int Next(int maxExclusive);
    }

    /// <summary>
    /// Deterministic PRNG (xorshift64*, seeded through SplitMix64).
    /// <para>
    /// Hand-rolled on purpose: <see cref="System.Random"/> changed algorithm in .NET 6, so the
    /// same seed yields different sequences on Unity's Mono and on the module's net9.0 runtime.
    /// AI determinism is a tested invariant (design doc section 12.2, invariant 7), so the
    /// generator has to be ours.
    /// </para>
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        private ulong _state;

        public SeededRandom(ulong seed)
        {
            _state = SplitMix64(seed == 0UL ? 0x9E3779B97F4A7C15UL : seed);
            if (_state == 0UL) _state = 0x9E3779B97F4A7C15UL;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            if (maxExclusive == 1) return 0;

            // Rejection sampling: keeps the distribution uniform instead of biasing low values.
            uint bound = (uint)maxExclusive;
            uint limit = uint.MaxValue - (uint.MaxValue % bound) - 1u;
            uint sample;
            do
            {
                sample = (uint)(NextUInt64() >> 32);
            }
            while (sample > limit);

            return (int)(sample % bound);
        }

        public ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        private static ulong SplitMix64(ulong seed)
        {
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
