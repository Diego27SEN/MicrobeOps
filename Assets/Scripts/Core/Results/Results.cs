#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>Outcome of validating a submitted placement.</summary>
    public readonly struct PlacementValidation
    {
        public readonly bool IsValid;

        /// <summary>Formatted <c>ERR_*</c> string when invalid, null otherwise.</summary>
        public readonly string? Error;

        private PlacementValidation(bool isValid, string? error)
        {
            IsValid = isValid;
            Error = error;
        }

        public static PlacementValidation Valid()
        {
            return new PlacementValidation(true, null);
        }

        public static PlacementValidation Invalid(string error)
        {
            if (string.IsNullOrEmpty(error)) throw new ArgumentException("Error code is required.", nameof(error));
            return new PlacementValidation(false, error);
        }
    }

    /// <summary>What a single shot did to a board, before rule flags redact anything.</summary>
    public readonly struct ShotResolution
    {
        public readonly ShotOutcome Outcome;
        public readonly ShipClass? SunkClass;

        /// <summary>Index into the target's fleet array, or -1 when nothing sank.</summary>
        public readonly int SunkShipIndex;

        public ShotResolution(ShotOutcome outcome, ShipClass? sunkClass, int sunkShipIndex)
        {
            Outcome = outcome;
            SunkClass = sunkClass;
            SunkShipIndex = sunkShipIndex;
        }

        public static ShotResolution Miss()
        {
            return new ShotResolution(ShotOutcome.Miss, null, -1);
        }
    }

    /// <summary>
    /// What the shooter is told about one shot. <see cref="SunkClass"/> is populated only when
    /// <c>announceSunkShipType</c> is on, <see cref="SunkCells"/> only when <c>revealSunkCells</c>
    /// is on (design doc section 4.5).
    /// </summary>
    public sealed class ShotResult
    {
        public Coord Cell { get; set; }
        public ShotOutcome Outcome { get; set; }
        public ShipClass? SunkClass { get; set; }
        public Coord[]? SunkCells { get; set; }
    }

    /// <summary>One ship sunk during a volley, redacted per rule flags.</summary>
    public sealed class SunkShipReport
    {
        public ShipClass? Class { get; set; }
        public Coord[]? Cells { get; set; }
    }

    /// <summary>
    /// Aggregate report for a Salvo volley: "two hits, three misses", without saying which shot
    /// was which. Used INSTEAD of per-cell <see cref="ShotResult"/>s when the rule set sets
    /// <c>salvoAggregateReport</c>. Emitting both would defeat the point of the variant.
    /// </summary>
    public sealed class VolleySummary
    {
        public int ShotsFired { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }

        /// <summary>Ships that sank in this volley. Sinking is announced even in aggregate mode.</summary>
        public SunkShipReport[] Sunk { get; set; } = Array.Empty<SunkShipReport>();
    }

    /// <summary>
    /// Result of applying a mutation to a <see cref="MatchState"/>. Failure carries a formatted
    /// <c>ERR_*</c> code and leaves the state untouched.
    /// </summary>
    public sealed class ApplyResult
    {
        public bool Success { get; private set; }
        public string? Error { get; private set; }

        /// <summary>
        /// Per-cell results. Empty when the rule set aggregates the volley, in which case
        /// <see cref="Volley"/> carries the report instead.
        /// </summary>
        public ShotResult[] Shots { get; private set; } = Array.Empty<ShotResult>();

        /// <summary>Non-null only under <c>salvoAggregateReport</c>.</summary>
        public VolleySummary? Volley { get; private set; }

        /// <summary>True when this mutation ended the match.</summary>
        public bool MatchEnded { get; private set; }

        public static ApplyResult Ok()
        {
            return new ApplyResult { Success = true };
        }

        public static ApplyResult Ok(ShotResult[] shots, bool matchEnded)
        {
            return new ApplyResult
            {
                Success = true,
                Shots = shots ?? Array.Empty<ShotResult>(),
                MatchEnded = matchEnded
            };
        }

        public static ApplyResult OkAggregated(VolleySummary volley, bool matchEnded)
        {
            return new ApplyResult
            {
                Success = true,
                Volley = volley ?? throw new ArgumentNullException(nameof(volley)),
                MatchEnded = matchEnded
            };
        }

        public static ApplyResult Failed(string error)
        {
            if (string.IsNullOrEmpty(error)) throw new ArgumentException("Error code is required.", nameof(error));
            return new ApplyResult { Success = false, Error = error };
        }
    }

    /// <summary>
    /// The computed reward for a finished match. The server computes it and the client paints it;
    /// the client never mirrors reward maths (design doc section 10.2).
    /// </summary>
    public sealed class RewardBreakdown
    {
        public int BaseCoin { get; set; }
        public int BaseXp { get; set; }

        public int FirstWinBonusCoin { get; set; }
        public int FirstWinBonusXp { get; set; }

        /// <summary>Streak bonus as a fraction, capped by <c>WinStreakMaxMultiplier</c>.</summary>
        public double StreakMultiplier { get; set; }
        public int StreakBonusCoin { get; set; }

        public int TotalCoin { get; set; }
        public int TotalXp { get; set; }

        public int LevelBefore { get; set; }
        public int LevelAfter { get; set; }

        /// <summary>True when an anti-farm gate zeroed the payout.</summary>
        public bool Suppressed { get; set; }

        /// <summary>Machine-readable reason, e.g. <c>daily_cap</c> or <c>pair_repetition</c>.</summary>
        public string? SuppressionReason { get; set; }

        /// <summary>How much of the payout was trimmed by a daily cap.</summary>
        public int CoinTrimmedByCap { get; set; }

        public static RewardBreakdown None(string reason)
        {
            return new RewardBreakdown { Suppressed = true, SuppressionReason = reason };
        }
    }

    /// <summary>
    /// Per-account daily counters that back the anti-farm gates. Stored server-side in
    /// <c>player.daily</c> and reset by the <c>RolloverDailies</c> scheduler job at 00:05 UTC.
    /// </summary>
    public sealed class PlayerDailyStats
    {
        /// <summary>UTC day this snapshot belongs to, as <c>yyyy-MM-dd</c>.</summary>
        public string DateUtc { get; set; } = string.Empty;

        public int CoinEarnedToday { get; set; }
        public int CoinFromAiToday { get; set; }
        public bool FirstWinClaimedToday { get; set; }
        public int OnlineWinStreak { get; set; }
        public int RewardedAdsWatchedToday { get; set; }
        public int InterstitialsShownToday { get; set; }
        public int RankedAbandonsToday { get; set; }

        /// <summary>Online matches against this specific opponent in the last 24 h.</summary>
        public int MatchesVsThisOpponentLast24h { get; set; }
    }

    /// <summary>Reasons a reward was suppressed. Kept as constants so analytics stays greppable.</summary>
    public static class SuppressionReasons
    {
        public const string MatchTooShort = "match_too_short";
        public const string TooFewShots = "too_few_shots";
        public const string OpponentInactive = "opponent_inactive";
        public const string PairRepetition = "pair_repetition";
        public const string DailyCapTotal = "daily_cap_total";
        public const string DailyCapAi = "daily_cap_ai";
        public const string LocalMatch = "local_match";
        public const string OwnAbandon = "own_abandon";
    }
}
