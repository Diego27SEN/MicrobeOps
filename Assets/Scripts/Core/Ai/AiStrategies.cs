#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>Shared board reasoning for the AI strategies.</summary>
    internal static class AiBoardMath
    {
        public static List<int> UntriedCells(in AiMemory memory, BoardConfig cfg)
        {
            List<int> cells = new List<int>(cfg.CellCount);
            for (int i = 0; i < cfg.CellCount; i++)
            {
                if (!memory.Shots.Get(i)) cells.Add(i);
            }
            return cells;
        }

        /// <summary>Untried cells orthogonally adjacent to a hit that has not been resolved yet.</summary>
        public static List<int> FollowUpCells(in AiMemory memory, BoardConfig cfg)
        {
            BitBoard unresolved = memory.UnresolvedHits;
            List<int> candidates = new List<int>();
            BitBoard seen = BitBoard.Empty;

            IReadOnlyList<int> hits = unresolved.ToIndices();
            for (int i = 0; i < hits.Count; i++)
            {
                int x = hits[i] % cfg.Width;
                int y = hits[i] / cfg.Width;

                AddIfCandidate(x - 1, y, cfg, in memory, ref seen, candidates);
                AddIfCandidate(x + 1, y, cfg, in memory, ref seen, candidates);
                AddIfCandidate(x, y - 1, cfg, in memory, ref seen, candidates);
                AddIfCandidate(x, y + 1, cfg, in memory, ref seen, candidates);
            }

            return candidates;
        }

        /// <summary>Untried cells orthogonally adjacent to one specific cell.</summary>
        public static List<int> UntriedNeighbours(Coord cell, in AiMemory memory, BoardConfig cfg)
        {
            List<int> candidates = new List<int>(4);
            BitBoard seen = BitBoard.Empty;

            AddIfCandidate(cell.X - 1, cell.Y, cfg, in memory, ref seen, candidates);
            AddIfCandidate(cell.X + 1, cell.Y, cfg, in memory, ref seen, candidates);
            AddIfCandidate(cell.X, cell.Y - 1, cfg, in memory, ref seen, candidates);
            AddIfCandidate(cell.X, cell.Y + 1, cfg, in memory, ref seen, candidates);

            return candidates;
        }

        public static int MinRemainingLength(in AiMemory memory)
        {
            int min = int.MaxValue;
            for (int i = 0; i < memory.RemainingShipLengths.Count; i++)
            {
                if (memory.RemainingShipLengths[i] < min) min = memory.RemainingShipLengths[i];
            }
            return min == int.MaxValue ? 1 : min;
        }

        public static Coord PickUniform(List<int> candidates, BoardConfig cfg, IRandom rng)
        {
            if (candidates.Count == 0) throw new InvalidOperationException("No cell left to shoot at.");
            return cfg.FromIndex(candidates[rng.Next(candidates.Count)]);
        }

        private static void AddIfCandidate(int x, int y, BoardConfig cfg, in AiMemory memory, ref BitBoard seen, List<int> candidates)
        {
            if (x < 0 || y < 0 || x >= cfg.Width || y >= cfg.Height) return;

            int index = (y * cfg.Width) + x;
            if (memory.Shots.Get(index) || seen.Get(index)) return;

            seen = seen.Set(index);
            candidates.Add(index);
        }
    }

    /// <summary>
    /// Easy: a careless player. It hunts uniformly at random and only ever chases the hit it just
    /// scored - it does not keep a list of loose ends, so once it loses the thread it never comes
    /// back for it. On top of that it walks away from even that obvious follow-up a quarter of the
    /// time.
    /// <para>
    /// Forgetting is what actually makes Easy weak. An AI that remembers every unresolved hit plays
    /// almost as well as Medium no matter how often it is distracted, because the loose end is
    /// still waiting for it on the next turn.
    /// </para>
    /// </summary>
    internal sealed class EasyAiStrategy : IAiStrategy
    {
        private readonly double _ignoreFollowUpChance;

        private Coord _lastShot;
        private bool _chasing;

        public EasyAiStrategy(AiDifficultyParams parameters)
        {
            _ignoreFollowUpChance = parameters.IgnoreObviousFollowUpChance;
        }

        public AiDifficulty Difficulty
        {
            get { return AiDifficulty.Easy; }
        }

        public Coord NextShot(in AiMemory memory, BoardConfig cfg, IRandom rng)
        {
            if (_chasing && !RollsToIgnore(rng))
            {
                List<int> neighbours = AiBoardMath.UntriedNeighbours(_lastShot, in memory, cfg);
                if (neighbours.Count > 0) return AiBoardMath.PickUniform(neighbours, cfg, rng);
            }

            return AiBoardMath.PickUniform(AiBoardMath.UntriedCells(in memory, cfg), cfg, rng);
        }

        public void Observe(Coord cell, ShotOutcome outcome, ShipClass? sunk)
        {
            _lastShot = cell;
            _chasing = outcome == ShotOutcome.Hit;
        }

        private bool RollsToIgnore(IRandom rng)
        {
            // Quantised to percent so the roll comes from the same integer generator as everything
            // else and stays reproducible from a seed.
            int threshold = (int)Math.Round(_ignoreFollowUpChance * 100.0);
            return rng.Next(100) < threshold;
        }
    }

    /// <summary>
    /// Medium: hunt and target. Random hunting, and on a hit it works the four orthogonal
    /// neighbours. No parity, no line inference. Roughly one hit per 4.2 shots.
    /// </summary>
    internal sealed class MediumAiStrategy : IAiStrategy
    {
        public AiDifficulty Difficulty
        {
            get { return AiDifficulty.Medium; }
        }

        public Coord NextShot(in AiMemory memory, BoardConfig cfg, IRandom rng)
        {
            List<int> followUps = AiBoardMath.FollowUpCells(in memory, cfg);
            if (followUps.Count > 0) return AiBoardMath.PickUniform(followUps, cfg, rng);

            return AiBoardMath.PickUniform(AiBoardMath.UntriedCells(in memory, cfg), cfg, rng);
        }

        public void Observe(Coord cell, ShotOutcome outcome, ShipClass? sunk)
        {
        }
    }

    /// <summary>
    /// Hard, and Adaptive on top of it.
    /// <para>
    /// The engine is a probability density map: for every ship still afloat, every legal placement
    /// on the cells not yet ruled out is enumerated, and each cell scores by how many placements
    /// cover it. That is the exact count of possible emplacements per cell given what is known -
    /// no sampling, no heuristics on top.
    /// </para>
    /// <para>
    /// Two refinements fall out of the same map rather than needing separate code. Parity hunting:
    /// while the smallest surviving ship is at least two cells long, only cells with
    /// <c>(x + y) % 2 == 0</c> are considered, which halves the search without ever skipping a
    /// ship. Line inference: once there are unresolved hits, only placements covering them count,
    /// weighted by the square of how many they cover, so a placement extending two collinear hits
    /// dominates one that merely brushes a single hit.
    /// </para>
    /// <para>
    /// Adaptive is the same map with deliberate noise: instead of the best cell it takes the k-th
    /// best, moving k between the configured floor and ceiling to keep the match close. k = 0
    /// plays exactly like Hard.
    /// </para>
    /// </summary>
    internal sealed class DensityAiStrategy : IAiStrategy
    {
        private readonly AiDifficulty _difficulty;
        private readonly AdaptiveParams? _adaptive;

        private int _noiseLevel;
        private int _shotsSinceAdjustment;

        public DensityAiStrategy(AiDifficulty difficulty, int initialNoiseLevel, AdaptiveParams? adaptive)
        {
            _difficulty = difficulty;
            _adaptive = adaptive;
            _noiseLevel = adaptive == null ? 0 : Clamp(initialNoiseLevel, adaptive.NoiseFloor, adaptive.NoiseCeiling);
        }

        public AiDifficulty Difficulty
        {
            get { return _difficulty; }
        }

        /// <summary>Current k. Exposed for tests and for balance dashboards.</summary>
        public int NoiseLevel
        {
            get { return _noiseLevel; }
        }

        public Coord NextShot(in AiMemory memory, BoardConfig cfg, IRandom rng)
        {
            if (_adaptive != null) MaybeAdjustNoise(in memory);

            int[] density = BuildDensityMap(in memory, cfg);

            List<int> best = CellsRankedByDensity(density, _adaptive == null ? 0 : _noiseLevel, out bool anyCandidate);
            if (anyCandidate) return AiBoardMath.PickUniform(best, cfg, rng);

            // Every remaining ship has been ruled out of every untried cell, which can only happen
            // with inconsistent input. Fall back to a legal move rather than throwing at a player.
            return AiBoardMath.PickUniform(AiBoardMath.UntriedCells(in memory, cfg), cfg, rng);
        }

        public void Observe(Coord cell, ShotOutcome outcome, ShipClass? sunk)
        {
            _shotsSinceAdjustment++;
        }

        private void MaybeAdjustNoise(in AiMemory memory)
        {
            AdaptiveParams adaptive = _adaptive!;
            if (_shotsSinceAdjustment < adaptive.WindowShots) return;

            _shotsSinceAdjustment = 0;

            double? own = memory.OwnAccuracyLastWindow;
            double? opponent = memory.OpponentAccuracyLastWindow;
            if (!own.HasValue || !opponent.HasValue) return;

            // Ahead of the human: add noise. Behind: sharpen up. The point is a close match, not a
            // win rate, which is why the comparison is against the human and not against a target.
            int step = own.Value > opponent.Value ? adaptive.AdjustStep : -adaptive.AdjustStep;
            _noiseLevel = Clamp(_noiseLevel + step, adaptive.NoiseFloor, adaptive.NoiseCeiling);
        }

        private static int[] BuildDensityMap(in AiMemory memory, BoardConfig cfg)
        {
            int[] density = new int[cfg.CellCount];

            BitBoard blocked = memory.Blocked;
            BitBoard unresolved = memory.UnresolvedHits;
            bool targeting = !unresolved.IsEmpty;

            for (int s = 0; s < memory.RemainingShipLengths.Count; s++)
            {
                int length = memory.RemainingShipLengths[s];

                AccumulatePlacements(density, cfg, blocked, unresolved, targeting, in memory, length, Orientation.Horizontal);
                AccumulatePlacements(density, cfg, blocked, unresolved, targeting, in memory, length, Orientation.Vertical);
            }

            if (!targeting) ApplyParityFilter(density, cfg, in memory);

            return density;
        }

        private static void AccumulatePlacements(
            int[] density,
            BoardConfig cfg,
            BitBoard blocked,
            BitBoard unresolved,
            bool targeting,
            in AiMemory memory,
            int length,
            Orientation orientation)
        {
            int spanX = orientation == Orientation.Horizontal ? length : 1;
            int spanY = orientation == Orientation.Vertical ? length : 1;
            if (spanX > cfg.Width || spanY > cfg.Height) return;

            for (int y = 0; y <= cfg.Height - spanY; y++)
            {
                for (int x = 0; x <= cfg.Width - spanX; x++)
                {
                    bool fits = true;
                    int covered = 0;

                    for (int i = 0; i < length && fits; i++)
                    {
                        int cx = orientation == Orientation.Horizontal ? x + i : x;
                        int cy = orientation == Orientation.Vertical ? y + i : y;
                        int index = (cy * cfg.Width) + cx;

                        if (blocked.Get(index)) fits = false;
                        else if (unresolved.Get(index)) covered++;
                    }

                    if (!fits) continue;
                    if (targeting && covered == 0) continue;

                    int weight = targeting ? covered * covered : 1;

                    for (int i = 0; i < length; i++)
                    {
                        int cx = orientation == Orientation.Horizontal ? x + i : x;
                        int cy = orientation == Orientation.Vertical ? y + i : y;
                        int index = (cy * cfg.Width) + cx;

                        if (!memory.Shots.Get(index)) density[index] += weight;
                    }
                }
            }
        }

        private static void ApplyParityFilter(int[] density, BoardConfig cfg, in AiMemory memory)
        {
            if (AiBoardMath.MinRemainingLength(in memory) < 2) return;

            int[] filtered = new int[density.Length];
            bool anyLeft = false;

            for (int index = 0; index < density.Length; index++)
            {
                int x = index % cfg.Width;
                int y = index / cfg.Width;

                if ((x + y) % 2 != 0) continue;

                filtered[index] = density[index];
                if (density[index] > 0) anyLeft = true;
            }

            // If the parity class is exhausted, hunting must continue on the other one.
            if (!anyLeft) return;

            Array.Copy(filtered, density, density.Length);
        }

        /// <summary>
        /// Cells at rank <paramref name="rank"/> of the density map. Rank 0 is the best set, which
        /// is what Hard always uses. Ties are returned together so the caller can break them with
        /// the injected RNG, keeping the choice deterministic per seed.
        /// </summary>
        private static List<int> CellsRankedByDensity(int[] density, int rank, out bool anyCandidate)
        {
            List<int> distinctScores = new List<int>();
            for (int i = 0; i < density.Length; i++)
            {
                if (density[i] <= 0) continue;
                if (!distinctScores.Contains(density[i])) distinctScores.Add(density[i]);
            }

            anyCandidate = distinctScores.Count > 0;
            if (!anyCandidate) return new List<int>();

            distinctScores.Sort();
            distinctScores.Reverse();

            int target = distinctScores[rank < distinctScores.Count ? rank : distinctScores.Count - 1];

            List<int> cells = new List<int>();
            for (int i = 0; i < density.Length; i++)
            {
                if (density[i] == target) cells.Add(i);
            }

            return cells;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
