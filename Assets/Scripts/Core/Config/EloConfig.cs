#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>A visible rank band. Design doc section 4.8.</summary>
    public readonly struct RankTier
    {
        /// <summary>Localization key suffix, e.g. <c>rank.admiral</c>. Never a display string.</summary>
        public readonly string Id;
        public readonly int MinRating;

        public RankTier(string id, int minRating)
        {
            Id = id;
            MinRating = minRating;
        }
    }

    /// <summary>Elo parameters from design doc section 4.8, mirrored under <c>SEASON_CONFIG</c>.</summary>
    public sealed class EloConfig
    {
        public int StartingRating { get; set; } = 1000;

        /// <summary>Rating floor. A player never drops below this.</summary>
        public int RatingFloor { get; set; } = 100;

        /// <summary>K-factor for players with fewer than <see cref="KFactorNoviceGames"/> matches.</summary>
        public int KFactorNovice { get; set; } = 40;
        public int KFactorNoviceGames { get; set; } = 10;

        public int KFactorIntermediate { get; set; } = 32;
        public int KFactorIntermediateGames { get; set; } = 50;

        public int KFactorVeteran { get; set; } = 24;

        /// <summary>Matches played before the rating becomes visible.</summary>
        public int PlacementMatches { get; set; } = 5;

        /// <summary>Extra rating lost on top of the defeat when abandoning a ranked match.</summary>
        public int AbandonPenalty { get; set; } = 15;

        /// <summary>Abandons in a day before a matchmaking cooldown kicks in.</summary>
        public int AbandonsBeforeCooldown { get; set; } = 3;

        public int AbandonCooldownSeconds { get; set; } = 300;

        public IReadOnlyList<RankTier> Tiers { get; set; } = Array.Empty<RankTier>();

        public int KFactorFor(int gamesPlayed)
        {
            if (gamesPlayed < KFactorNoviceGames) return KFactorNovice;
            if (gamesPlayed < KFactorIntermediateGames) return KFactorIntermediate;
            return KFactorVeteran;
        }

        public string TierIdFor(int rating)
        {
            string id = string.Empty;
            for (int i = 0; i < Tiers.Count; i++)
            {
                if (rating >= Tiers[i].MinRating) id = Tiers[i].Id;
            }
            return id;
        }
    }

    /// <summary>Season length and soft reset. Design doc section 4.8.</summary>
    public sealed class SeasonConfig
    {
        public int DurationDays { get; set; } = 60;

        /// <summary>Soft reset: new = <see cref="SoftResetAnchor"/> + (old - anchor) * <see cref="SoftResetFactor"/>.</summary>
        public int SoftResetAnchor { get; set; } = 1000;
        public double SoftResetFactor { get; set; } = 0.5;

        /// <summary>Ranked runs on a fixed board and rule set.</summary>
        public string RankedBoardId { get; set; } = BoardConfig.ClassicId;
        public string RankedRuleSetId { get; set; } = RuleFlags.ClassicId;

        /// <summary>Leaderboard id template, e.g. <c>ranked_elo_s{0}</c>.</summary>
        public string LeaderboardIdFormat { get; set; } = "ranked_elo_s{0}";

        public int ApplySoftReset(int rating)
        {
            return SoftResetAnchor + (int)Math.Round((rating - SoftResetAnchor) * SoftResetFactor, MidpointRounding.AwayFromZero);
        }
    }
}
