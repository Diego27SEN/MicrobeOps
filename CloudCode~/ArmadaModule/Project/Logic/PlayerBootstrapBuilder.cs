#nullable enable

using System;
using Armada.Core;

namespace Armada.CloudCode
{
    /// <summary>
    /// Assembles the boot payload from stored player data, or from defaults when there is none.
    /// <para>
    /// Pure and storage-free on purpose: it takes what was read and returns what to send, so the
    /// interesting part - what a brand new player looks like, when the daily reward is available,
    /// whether a rating is allowed to be visible - is testable without Cloud Save existing yet.
    /// The endpoint does the reading.
    /// </para>
    /// </summary>
    public static class PlayerBootstrapBuilder
    {
        /// <summary>A player nobody has seen before.</summary>
        public static PlayerBootstrap ForNewPlayer(string playerId, GameConfig config, long nowUnixMs)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            return new PlayerBootstrap
            {
                IsNewPlayer = true,
                Profile = new PlayerProfileDto { PlayerId = playerId, Level = 1 },
                Economy = new EconomyDto(),
                DailyState = new DailyStateDto
                {
                    DateUtc = UtcDay(nowUnixMs),

                    // A new player's first daily reward is waiting for them, not a day away.
                    DailyRewardAvailable = true
                },
                SeasonState = new SeasonStateDto
                {
                    Rating = config.Elo.StartingRating,
                    TierId = config.Elo.TierIdFor(config.Elo.StartingRating),
                    PlacementMatchesRemaining = config.Elo.PlacementMatches,
                    RatingVisible = false
                }
            };
        }

        /// <summary>
        /// Builds the daily block, rolling it over when the stored day is not today. The rollover
        /// also happens here, and not only in the scheduler, because a player who opens the app at
        /// 00:04 UTC must not see yesterday's counters.
        /// </summary>
        public static DailyStateDto BuildDailyState(PlayerDailyStats stored, long nowUnixMs)
        {
            if (stored == null) throw new ArgumentNullException(nameof(stored));

            string today = UtcDay(nowUnixMs);
            bool isToday = string.Equals(stored.DateUtc, today, StringComparison.Ordinal);

            if (!isToday)
            {
                return new DailyStateDto
                {
                    DateUtc = today,
                    DailyRewardAvailable = true,

                    // The win streak survives the day boundary; it breaks on a loss, not on a
                    // clock. Everything else is a daily counter and resets.
                    OnlineWinStreak = stored.OnlineWinStreak
                };
            }

            return new DailyStateDto
            {
                DateUtc = stored.DateUtc,
                DailyRewardAvailable = false,
                CoinEarnedToday = stored.CoinEarnedToday,
                CoinFromAiToday = stored.CoinFromAiToday,
                OnlineWinStreak = stored.OnlineWinStreak,
                FirstWinClaimedToday = stored.FirstWinClaimedToday
            };
        }

        /// <summary>
        /// Builds the season block. The rating stays hidden until the placement matches are done,
        /// which is the whole point of having them: a number that swings wildly for five games
        /// teaches nothing and reads as broken.
        /// </summary>
        public static SeasonStateDto BuildSeasonState(int rating, int rankedMatchesPlayed, int seasonNumber, long seasonEndsAtUnixMs, GameConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            int remaining = config.Elo.PlacementMatches - rankedMatchesPlayed;
            if (remaining < 0) remaining = 0;

            return new SeasonStateDto
            {
                SeasonNumber = seasonNumber,
                EndsAtUnixMs = seasonEndsAtUnixMs,
                Rating = rating,
                TierId = config.Elo.TierIdFor(rating),
                PlacementMatchesRemaining = remaining,
                RatingVisible = remaining == 0
            };
        }

        /// <summary>The UTC day as <c>yyyy-MM-dd</c>, which is the boundary every daily counter uses.</summary>
        public static string UtcDay(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-dd");
        }
    }
}
