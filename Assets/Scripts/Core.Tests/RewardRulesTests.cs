#nullable enable

using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class RewardRulesTests
    {
        private GameConfig _config = null!;
        private EconomyConfig _economy = null!;
        private BoardConfig _classic = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
            _economy = _config.Economy;
            _classic = _config.Board(BoardConfig.ClassicId);
        }

        private static PlayerDailyStats FreshDay()
        {
            return new PlayerDailyStats { DateUtc = "2026-07-23" };
        }

        /// <summary>A finished match that clears every anti-farm gate, so tests can vary one thing at a time.</summary>
        private MatchState RewardableMatch(
            MatchMode mode = MatchMode.Casual,
            PlayerSlot winner = PlayerSlot.A,
            MatchEndReason reason = MatchEndReason.FleetDestroyed,
            AiDifficulty? ai = null,
            int shotsA = 30,
            int shotsB = 30,
            long durationSeconds = 300)
        {
            MatchState state = new MatchState
            {
                MatchId = "m1",
                Mode = mode,
                BoardId = BoardConfig.ClassicId,
                RuleSetId = RuleFlags.ClassicId,
                Phase = MatchPhase.Finished,
                PlayerA = TestGame.PlayerA,
                PlayerB = ai.HasValue ? null : TestGame.PlayerB,
                AiDifficulty = ai,
                Winner = winner,
                EndReason = reason,
                CreatedAtUnixMs = TestGame.Epoch,
                FinishedAtUnixMs = TestGame.Epoch + (durationSeconds * 1000L),
                LastActionUnixMs = TestGame.Epoch + (durationSeconds * 1000L),
                BoardA = PlayerBoard.CreateEmpty(),
                BoardB = PlayerBoard.CreateEmpty()
            };

            state.BoardA.ShotsFired = shotsA;
            state.BoardB.ShotsFired = shotsB;
            return state;
        }

        // --- base payouts ----------------------------------------------------------------------

        [Test]
        public void Compute_OnAnOnlineWin_Pays100CoinAnd100Xp()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.Suppressed, Is.False);
            Assert.That(reward.BaseCoin, Is.EqualTo(100));
            Assert.That(reward.TotalCoin, Is.EqualTo(100));
            Assert.That(reward.TotalXp, Is.EqualTo(100));
        }

        [Test]
        public void Compute_OnAnOnlineLoss_StillPays40()
        {
            MatchState state = RewardableMatch();
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerB, _classic, _economy, FreshDay());

            Assert.That(reward.TotalCoin, Is.EqualTo(40));
            Assert.That(reward.TotalXp, Is.EqualTo(40));
        }

        [Test]
        public void Compute_OnTheFirstWinOfTheDay_AddsTheBonus()
        {
            MatchState state = RewardableMatch();
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.FirstWinBonusCoin, Is.EqualTo(150));
            Assert.That(reward.TotalCoin, Is.EqualTo(250));
            Assert.That(reward.TotalXp, Is.EqualTo(200));
        }

        [Test]
        public void Compute_OnAWinStreak_AddsTenPercentPerWin()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;
            stats.OnlineWinStreak = 3;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.StreakMultiplier, Is.EqualTo(0.30).Within(1e-9));
            Assert.That(reward.StreakBonusCoin, Is.EqualTo(30));
            Assert.That(reward.TotalCoin, Is.EqualTo(130));
        }

        [Test]
        public void Compute_CapsTheStreakBonusAtFiftyPercent()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;
            stats.OnlineWinStreak = 20;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.StreakMultiplier, Is.EqualTo(0.50).Within(1e-9));
            Assert.That(reward.StreakBonusCoin, Is.EqualTo(50));
        }

        [TestCase(AiDifficulty.Easy, 10)]
        [TestCase(AiDifficulty.Medium, 20)]
        [TestCase(AiDifficulty.Hard, 35)]
        [TestCase(AiDifficulty.Adaptive, 35)]
        public void Compute_OnAWinAgainstTheAi_PaysTheDifficultyRate(AiDifficulty difficulty, int expected)
        {
            MatchState state = RewardableMatch(MatchMode.VsAi, PlayerSlot.A, MatchEndReason.FleetDestroyed, difficulty);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.TotalCoin, Is.EqualTo(expected));
        }

        [Test]
        public void Compute_AgainstTheAi_GrantsNoFirstWinBonusAndNoStreak()
        {
            // Otherwise the cheapest way to farm the daily bonus would be to beat Easy once.
            MatchState state = RewardableMatch(MatchMode.VsAi, PlayerSlot.A, MatchEndReason.FleetDestroyed, AiDifficulty.Easy);
            PlayerDailyStats stats = FreshDay();
            stats.OnlineWinStreak = 5;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.FirstWinBonusCoin, Is.EqualTo(0));
            Assert.That(reward.StreakBonusCoin, Is.EqualTo(0));
            Assert.That(reward.TotalCoin, Is.EqualTo(10));
        }

        [Test]
        public void Compute_OnALocalMatch_PaysNothing()
        {
            MatchState state = RewardableMatch(MatchMode.LocalTwoPlayer);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.Suppressed, Is.True);
            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.LocalMatch));
            Assert.That(reward.TotalCoin, Is.EqualTo(0));
        }

        [Test]
        public void Compute_ForThePlayerWhoWalkedAway_PaysNothing()
        {
            MatchState state = RewardableMatch(MatchMode.Casual, PlayerSlot.A, MatchEndReason.Forfeit);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerB, _classic, _economy, FreshDay());

            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.OwnAbandon));
        }

        [Test]
        public void Compute_ForTheWinnerOfAnAbandonedMatch_StillPaysFull()
        {
            MatchState state = RewardableMatch(MatchMode.Casual, PlayerSlot.A, MatchEndReason.Abandoned);
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.TotalCoin, Is.EqualTo(100));
        }

        // --- anti-farm gates ---------------------------------------------------------------------

        [Test]
        public void Compute_OnAMatchShorterThan60Seconds_PaysNothing()
        {
            MatchState state = RewardableMatch(durationSeconds: 45);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.MatchTooShort));
        }

        [Test]
        public void Compute_WhenTheRewardedPlayerFiredTooFewShots_PaysNothing()
        {
            MatchState state = RewardableMatch(shotsA: 5);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.TooFewShots));
        }

        [Test]
        public void Compute_OnBlitz_AcceptsTheLowerShotFloor()
        {
            MatchState state = RewardableMatch(shotsA: 9, shotsB: 9);
            state.BoardId = BoardConfig.BlitzId;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _config.Board(BoardConfig.BlitzId), _economy, FreshDay());

            Assert.That(reward.Suppressed, Is.False);
        }

        [Test]
        public void Compute_WhenTheOpponentNeverActed_PaysNothing()
        {
            MatchState state = RewardableMatch(shotsB: 0);
            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay());

            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.OpponentInactive));
        }

        [Test]
        public void Compute_OnTheFourthMatchAgainstTheSameOpponentInADay_PaysNothing()
        {
            // Collusion control from design doc section 5.3.
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.MatchesVsThisOpponentLast24h = 3;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.PairRepetition));
        }

        [Test]
        public void Compute_OnTheThirdMatchAgainstTheSameOpponent_StillPays()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;
            stats.MatchesVsThisOpponentLast24h = 2;

            Assert.That(RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats).TotalCoin, Is.EqualTo(100));
        }

        [Test]
        public void Compute_AgainstTheAi_IsCappedAt150CoinPerDay()
        {
            MatchState state = RewardableMatch(MatchMode.VsAi, PlayerSlot.A, MatchEndReason.FleetDestroyed, AiDifficulty.Hard);
            PlayerDailyStats stats = FreshDay();
            stats.CoinFromAiToday = 130;
            stats.CoinEarnedToday = 130;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.TotalCoin, Is.EqualTo(20), "35 requested, 20 of daily AI headroom left");
            Assert.That(reward.CoinTrimmedByCap, Is.EqualTo(15));
        }

        [Test]
        public void Compute_AgainstTheAi_OnceTheDailyAiCapIsSpent_PaysNothing()
        {
            MatchState state = RewardableMatch(MatchMode.VsAi, PlayerSlot.A, MatchEndReason.FleetDestroyed, AiDifficulty.Hard);
            PlayerDailyStats stats = FreshDay();
            stats.CoinFromAiToday = 150;
            stats.CoinEarnedToday = 150;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.TotalCoin, Is.EqualTo(0));
            Assert.That(reward.SuppressionReason, Is.EqualTo(SuppressionReasons.DailyCapAi));
        }

        [Test]
        public void Compute_OnlineIsNotSubjectToTheAiCap()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;
            stats.CoinFromAiToday = 150;
            stats.CoinEarnedToday = 150;

            Assert.That(RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats).TotalCoin, Is.EqualTo(100));
        }

        [Test]
        public void Compute_RespectsTheHardDailyCeiling()
        {
            MatchState state = RewardableMatch();
            PlayerDailyStats stats = FreshDay();
            stats.FirstWinClaimedToday = true;
            stats.CoinEarnedToday = 2450;

            RewardBreakdown reward = RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, stats);

            Assert.That(reward.TotalCoin, Is.EqualTo(50));
            Assert.That(reward.CoinTrimmedByCap, Is.EqualTo(50));
        }

        [Test]
        public void Compute_OnAnUnfinishedMatch_Throws()
        {
            MatchState state = RewardableMatch();
            state.Phase = MatchPhase.InProgress;

            Assert.That(
                () => RewardRules.Compute(in state, TestGame.PlayerA, _classic, _economy, FreshDay()),
                Throws.TypeOf<System.InvalidOperationException>());
        }

        // --- level curve --------------------------------------------------------------------------

        [Test]
        public void TotalXpForLevel_StartsAtZero()
        {
            Assert.That(RewardRules.TotalXpForLevel(1, _economy.Levels), Is.EqualTo(0));
            Assert.That(RewardRules.TotalXpForLevel(2, _economy.Levels), Is.EqualTo(500));
            Assert.That(RewardRules.TotalXpForLevel(3, _economy.Levels), Is.EqualTo(1250));
        }

        [Test]
        public void TotalXpForLevel_ReachesAboutFourHundredSeventyTwoThousandAtTheCap()
        {
            // 472 500 is the whole curve; reaching level 60 costs everything but the last step.
            Assert.That(RewardRules.TotalXpForLevel(60, _economy.Levels), Is.EqualTo(472500 - _economy.Levels.XpForLevel(60)));
        }

        [TestCase(0, 1)]
        [TestCase(499, 1)]
        [TestCase(500, 2)]
        [TestCase(1249, 2)]
        [TestCase(1250, 3)]
        public void LevelForTotalXp_MatchesTheCurve(long totalXp, int expected)
        {
            Assert.That(RewardRules.LevelForTotalXp(totalXp, _economy.Levels), Is.EqualTo(expected));
        }

        [Test]
        public void LevelForTotalXp_IsCappedAtSixty()
        {
            Assert.That(RewardRules.LevelForTotalXp(99_999_999, _economy.Levels), Is.EqualTo(60));
        }

        [Test]
        public void LevelForTotalXp_IsTheInverseOfTotalXpForLevel()
        {
            for (int level = 1; level <= _economy.Levels.MaxLevel; level++)
            {
                long threshold = RewardRules.TotalXpForLevel(level, _economy.Levels);
                Assert.That(RewardRules.LevelForTotalXp(threshold, _economy.Levels), Is.EqualTo(level), "at level " + level);
            }
        }

        [Test]
        public void LevelUpCoin_Is200Plus25PerLevel()
        {
            Assert.That(RewardRules.LevelUpCoin(1, _economy.Levels), Is.EqualTo(225));
            Assert.That(RewardRules.LevelUpCoin(10, _economy.Levels), Is.EqualTo(450));
        }
    }
}
