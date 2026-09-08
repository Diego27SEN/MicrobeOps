#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>
    /// Reward computation. Design doc sections 5.2, 5.3 and 5.4.
    /// <para>
    /// Runs server-side only for online modes. The client never mirrors this maths: it receives a
    /// finished <see cref="RewardBreakdown"/> and paints it. Mirroring reward constants in the
    /// client was inherited technical debt and is not repeated here.
    /// </para>
    /// </summary>
    public static class RewardRules
    {
        /// <summary>
        /// Computes the payout for one player of a finished match, applying every anti-farm gate
        /// in order: local matches, own abandon, minimum duration, minimum shots, both players
        /// active, pair repetition, then the daily caps. A closed gate returns a suppressed
        /// breakdown with a machine-readable reason; it never throws and never silently pays out.
        /// </summary>
        public static RewardBreakdown Compute(in MatchState state, string playerId, BoardConfig board, EconomyConfig cfg, PlayerDailyStats stats)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            if (state.Phase != MatchPhase.Finished) throw new InvalidOperationException("Rewards are computed on finished matches only.");

            PlayerSlot? slot = state.SlotOf(playerId);
            if (slot == null) throw new ArgumentException("Player is not part of this match.", nameof(playerId));

            if (state.Mode == MatchMode.LocalTwoPlayer) return RewardBreakdown.None(SuppressionReasons.LocalMatch);

            bool won = state.Winner.HasValue && state.Winner.Value == slot.Value;
            bool ownAbandon = !won && IsAbandonEnd(state.EndReason);
            if (ownAbandon) return RewardBreakdown.None(SuppressionReasons.OwnAbandon);

            bool vsAi = state.IsAiMatch || state.IsBotFilled;

            string? gate = CheckAntiFarmGates(in state, slot.Value, board, cfg, stats, vsAi);
            if (gate != null) return RewardBreakdown.None(gate);

            Payout basePayout = BasePayoutFor(in state, slot.Value, won, vsAi, cfg);

            RewardBreakdown breakdown = new RewardBreakdown
            {
                BaseCoin = basePayout.Coin,
                BaseXp = basePayout.Xp
            };

            // The first-win bonus and the streak multiplier are online-only: they exist to reward
            // playing against people, not to make grinding the AI more profitable.
            if (won && !vsAi)
            {
                if (!stats.FirstWinClaimedToday)
                {
                    breakdown.FirstWinBonusCoin = cfg.FirstWinOfDayBonus.Coin;
                    breakdown.FirstWinBonusXp = cfg.FirstWinOfDayBonus.Xp;
                }

                double multiplier = Math.Min(stats.OnlineWinStreak * cfg.WinStreakStepMultiplier, cfg.WinStreakMaxMultiplier);
                breakdown.StreakMultiplier = multiplier;
                breakdown.StreakBonusCoin = (int)Math.Round(basePayout.Coin * multiplier, MidpointRounding.AwayFromZero);
            }

            int coin = breakdown.BaseCoin + breakdown.FirstWinBonusCoin + breakdown.StreakBonusCoin;
            breakdown.TotalXp = breakdown.BaseXp + breakdown.FirstWinBonusXp;

            breakdown.TotalCoin = ApplyDailyCaps(coin, vsAi, cfg, stats, out int trimmed, out string? capReason);
            breakdown.CoinTrimmedByCap = trimmed;

            if (breakdown.TotalCoin == 0 && capReason != null)
            {
                breakdown.Suppressed = true;
                breakdown.SuppressionReason = capReason;
            }

            return breakdown;
        }

        /// <summary>Level reached with a given total XP, capped at <c>MaxLevel</c>.</summary>
        public static int LevelForTotalXp(long totalXp, LevelConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (totalXp < 0) throw new ArgumentOutOfRangeException(nameof(totalXp));

            int level = 1;
            long consumed = 0;

            while (level < cfg.MaxLevel)
            {
                long needed = cfg.XpForLevel(level);
                if (totalXp < consumed + needed) break;
                consumed += needed;
                level++;
            }

            return level;
        }

        /// <summary>Cumulative XP needed to reach <paramref name="level"/> from zero.</summary>
        public static long TotalXpForLevel(int level, LevelConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));

            long total = 0;
            for (int n = 1; n < level; n++) total += cfg.XpForLevel(n);
            return total;
        }

        /// <summary>COIN granted on reaching <paramref name="level"/>.</summary>
        public static int LevelUpCoin(int level, LevelConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            return cfg.LevelUpCoinBase + (cfg.LevelUpCoinPerLevel * level);
        }

        private static string? CheckAntiFarmGates(in MatchState state, PlayerSlot slot, BoardConfig board, EconomyConfig cfg, PlayerDailyStats stats, bool vsAi)
        {
            AntiFarmCaps caps = cfg.AntiFarm;

            long durationMs = (state.FinishedAtUnixMs ?? state.LastActionUnixMs) - state.CreatedAtUnixMs;
            if (durationMs < caps.MinMatchDurationSeconds * 1000L) return SuppressionReasons.MatchTooShort;

            int minShots = string.Equals(board.Id, BoardConfig.BlitzId, StringComparison.Ordinal)
                ? caps.MinShotsForRewardBlitz
                : caps.MinShotsForReward;

            PlayerBoard own = slot == PlayerSlot.A ? state.BoardA : state.BoardB;
            PlayerBoard other = slot == PlayerSlot.A ? state.BoardB : state.BoardA;

            if (own.ShotsFired < minShots) return SuppressionReasons.TooFewShots;

            if (own.ShotsFired < caps.MinActionsPerPlayerAfterPlacement
                || other.ShotsFired < caps.MinActionsPerPlayerAfterPlacement)
            {
                return SuppressionReasons.OpponentInactive;
            }

            // Collusion: past the fourth match against the same person in a day, rewards go to zero
            // and analytics gets told about it.
            if (!vsAi && stats.MatchesVsThisOpponentLast24h >= caps.MaxMatchesPerPairPer24h)
            {
                return SuppressionReasons.PairRepetition;
            }

            return null;
        }

        private static Payout BasePayoutFor(in MatchState state, PlayerSlot slot, bool won, bool vsAi, EconomyConfig cfg)
        {
            if (vsAi)
            {
                if (!won) return cfg.AiLoss;

                AiDifficulty difficulty = state.AiDifficulty ?? AiDifficulty.Medium;
                return cfg.AiWin.TryGetValue(difficulty, out Payout payout) ? payout : cfg.AiLoss;
            }

            if (won)
            {
                bool opponentWalked = IsAbandonEnd(state.EndReason);
                return opponentWalked ? cfg.WinByOpponentAbandon : cfg.OnlineWin;
            }

            return cfg.OnlineLoss;
        }

        private static int ApplyDailyCaps(int coin, bool vsAi, EconomyConfig cfg, PlayerDailyStats stats, out int trimmed, out string? reason)
        {
            AntiFarmCaps caps = cfg.AntiFarm;
            int granted = coin;
            reason = null;

            if (vsAi)
            {
                int headroom = caps.DailyCoinCapFromAi - stats.CoinFromAiToday;
                if (headroom < granted)
                {
                    granted = headroom > 0 ? headroom : 0;
                    reason = SuppressionReasons.DailyCapAi;
                }
            }

            int totalHeadroom = caps.DailyCoinCapTotal - stats.CoinEarnedToday;
            if (totalHeadroom < granted)
            {
                granted = totalHeadroom > 0 ? totalHeadroom : 0;
                reason = SuppressionReasons.DailyCapTotal;
            }

            trimmed = coin - granted;
            if (trimmed == 0) reason = null;
            return granted;
        }

        private static bool IsAbandonEnd(MatchEndReason? reason)
        {
            return reason == MatchEndReason.Forfeit
                || reason == MatchEndReason.Abandoned
                || reason == MatchEndReason.TimeoutStrikes;
        }
    }
}
