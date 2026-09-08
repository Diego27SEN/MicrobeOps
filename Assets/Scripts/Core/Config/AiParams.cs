#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Tuning for the Adaptive difficulty: it plays Hard with variable noise, picking the k-th
    /// best cell instead of the best one. Values from design doc section 4.6.
    /// </summary>
    public sealed class AdaptiveParams
    {
        /// <summary>How many of the player's recent shots are evaluated per adjustment window.</summary>
        public int WindowShots { get; set; } = 20;

        public double TargetWinRate { get; set; } = 0.50;

        /// <summary>k = 0 means the AI plays optimally.</summary>
        public int NoiseFloor { get; set; }

        /// <summary>k = 6 means the AI plays roughly like Medium.</summary>
        public int NoiseCeiling { get; set; } = 6;

        /// <summary>How much k moves per evaluated window.</summary>
        public int AdjustStep { get; set; } = 1;
    }

    /// <summary>Per-difficulty behaviour and the QA acceptance targets that go with it.</summary>
    public sealed class AiDifficultyParams
    {
        public AiDifficulty Difficulty { get; set; }

        /// <summary>Chance of deliberately ignoring an obvious follow-up. Only Easy uses it.</summary>
        public double IgnoreObviousFollowUpChance { get; set; }

        /// <summary>Restrict hunting to cells where (x + y) % 2 == 0 while the smallest live ship is 2 long.</summary>
        public bool UseParityHunt { get; set; }

        /// <summary>Count the exact number of possible ship placements per cell given what is known.</summary>
        public bool UseProbabilityDensity { get; set; }

        /// <summary>Infer the ship's axis from the second hit and extend along it.</summary>
        public bool UseLineInference { get; set; }

        public bool IsAdaptive { get; set; }

        /// <summary>
        /// QA acceptance target: average shots needed to sink the whole classic fleet
        /// (17 cells over 10 000 simulated matches). Not an aspiration, a test threshold.
        /// </summary>
        public double MaxAverageShotsToClearClassic { get; set; }

        /// <summary>Target win rate against an average player. Used for balance dashboards.</summary>
        public double TargetWinRateVsAveragePlayer { get; set; }
    }

    public sealed class AiParams
    {
        public IReadOnlyList<AiDifficultyParams> Difficulties { get; set; } = Array.Empty<AiDifficultyParams>();

        public AdaptiveParams Adaptive { get; set; } = new AdaptiveParams();

        /// <summary>
        /// AI placements are rejected when more than this fraction of ship cells sit on the border,
        /// or when two large ships lie parallel and adjacent: patterns human players learn to
        /// exploit. Design doc section 4.6.
        /// </summary>
        public double MaxBorderCellRatio { get; set; } = 0.60;

        public int MaxPlacementRetries { get; set; } = 10;

        /// <summary>Strategy used for the automatic shot fired when a turn times out.</summary>
        public AiDifficulty TimeoutShotDifficulty { get; set; } = AiDifficulty.Medium;

        public AiDifficultyParams For(AiDifficulty difficulty)
        {
            for (int i = 0; i < Difficulties.Count; i++)
            {
                if (Difficulties[i].Difficulty == difficulty) return Difficulties[i];
            }
            throw new ArgumentOutOfRangeException(nameof(difficulty), "No parameters configured for " + difficulty);
        }
    }
}
