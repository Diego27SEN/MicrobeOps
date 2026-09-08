#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// Guards the embedded config against the design doc. Every number asserted here is quoted
    /// from a numbered section, so when someone "just tweaks" a value in code without updating the
    /// document, this suite is what stops it. The document wins; this file is the mirror.
    /// </summary>
    [TestFixture]
    public sealed class DefaultGameConfigTests
    {
        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = DefaultGameConfig.Create();
        }

        // --- Section 4.1: boards and fleets -------------------------------------------------

        [TestCase(BoardConfig.BlitzId, 8, 8, 12, 4)]
        [TestCase(BoardConfig.ClassicId, 10, 10, 17, 5)]
        [TestCase(BoardConfig.AdmiralId, 12, 12, 26, 7)]
        public void Boards_MatchTheDesignDocGeometry(string id, int width, int height, int fleetCells, int shipCount)
        {
            BoardConfig board = _config.Board(id);

            Assert.That(board.Width, Is.EqualTo(width));
            Assert.That(board.Height, Is.EqualTo(height));
            Assert.That(board.FleetCellCount, Is.EqualTo(fleetCells));
            Assert.That(board.Fleet.Count, Is.EqualTo(shipCount));
        }

        [Test]
        public void ClassicFleet_IsFiveFourThreeThreeTwo()
        {
            Assert.That(LengthsOf(BoardConfig.ClassicId), Is.EqualTo(new[] { 5, 4, 3, 3, 2 }));
        }

        [Test]
        public void BlitzFleet_IsFourThreeThreeTwo()
        {
            Assert.That(LengthsOf(BoardConfig.BlitzId), Is.EqualTo(new[] { 4, 3, 3, 2 }));
        }

        [Test]
        public void AdmiralFleet_IsFiveFiveFourFourThreeThreeTwo()
        {
            Assert.That(LengthsOf(BoardConfig.AdmiralId), Is.EqualTo(new[] { 5, 5, 4, 4, 3, 3, 2 }));
        }

        [Test]
        public void EveryBoard_KeepsFleetDensityNearEighteenPercent()
        {
            for (int i = 0; i < _config.Boards.Count; i++)
            {
                BoardConfig board = _config.Boards[i];
                double density = (double)board.FleetCellCount / board.CellCount;
                Assert.That(density, Is.InRange(0.16, 0.19), board.Id + " density drifted");
            }
        }

        [Test]
        public void EveryBoard_FitsInASingleBitBoard()
        {
            for (int i = 0; i < _config.Boards.Count; i++)
            {
                Assert.That(_config.Boards[i].CellCount, Is.LessThanOrEqualTo(BitBoard.Capacity));
            }
        }

        [Test]
        public void NoShip_IsLongerThanTheShortestBoardSide()
        {
            for (int i = 0; i < _config.Boards.Count; i++)
            {
                BoardConfig board = _config.Boards[i];
                int shortestSide = board.Width < board.Height ? board.Width : board.Height;
                for (int s = 0; s < board.Fleet.Count; s++)
                {
                    Assert.That(board.Fleet[s].Length, Is.LessThanOrEqualTo(shortestSide), board.Id + " has an unplaceable ship");
                }
            }
        }

        // --- Section 4.3: rule flags --------------------------------------------------------

        [Test]
        public void ClassicRules_UseOneShotPerTurnAndAnnounceSinkings()
        {
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

            Assert.That(rules.AnnounceSunkShipType, Is.True);
            Assert.That(rules.RevealSunkCells, Is.True);
            Assert.That(rules.ExtraTurnOnHit, Is.False);
            Assert.That(rules.ShotsPerTurn, Is.EqualTo(ShotsPerTurnMode.Fixed));
            Assert.That(rules.FixedShotsPerTurn, Is.EqualTo(1));
        }

        [Test]
        public void BlitzRules_GrantAnExtraTurnOnHit()
        {
            Assert.That(_config.RuleSet(RuleFlags.BlitzId).ExtraTurnOnHit, Is.True);
        }

        [Test]
        public void SalvoRules_FireOnePerAfloatShipAndReportInAggregate()
        {
            RuleFlags rules = _config.RuleSet(RuleFlags.SalvoId);

            Assert.That(rules.ShotsPerTurn, Is.EqualTo(ShotsPerTurnMode.OneShotPerAfloatShip));
            Assert.That(rules.SalvoAggregateReport, Is.True);
            Assert.That(rules.AnnounceSunkShipType, Is.False);
        }

        [Test]
        public void DiagonalPlacement_IsOffEverywhere_Always()
        {
            for (int i = 0; i < _config.RuleSets.Count; i++)
            {
                Assert.That(_config.RuleSets[i].DiagonalPlacement, Is.False, _config.RuleSets[i].Id);
            }
        }

        // --- Section 4.4: timers ------------------------------------------------------------

        [Test]
        public void RealTimeTimers_Are90SecondsToDeployAnd30PerTurn()
        {
            TimerProfile timers = _config.Timers.RealTime;

            Assert.That(timers.PlacementSeconds, Is.EqualTo(90));
            Assert.That(timers.TurnSeconds, Is.EqualTo(30));
            Assert.That(timers.MaxConsecutiveTimeouts, Is.EqualTo(3));
            Assert.That(timers.ReconnectGraceSeconds, Is.EqualTo(45));
            Assert.That(timers.AbandonSeconds, Is.EqualTo(180));
            Assert.That(timers.RematchOfferSeconds, Is.EqualTo(30));
        }

        [Test]
        public void AsyncTimers_Give24HoursAndAllowTwoStrikes()
        {
            TimerProfile timers = _config.Timers.Async;

            Assert.That(timers.PlacementSeconds, Is.EqualTo(86400));
            Assert.That(timers.TurnSeconds, Is.EqualTo(86400));
            Assert.That(timers.MaxConsecutiveTimeouts, Is.EqualTo(2));
            Assert.That(timers.AbandonSeconds, Is.EqualTo(172800));
        }

        [Test]
        public void TimerProfile_IsSelectedByMatchMode()
        {
            Assert.That(_config.Timers.For(MatchMode.Async), Is.SameAs(_config.Timers.Async));
            Assert.That(_config.Timers.For(MatchMode.Ranked), Is.SameAs(_config.Timers.RealTime));
        }

        // --- Section 4.6: AI ----------------------------------------------------------------

        [Test]
        public void AllFourDifficulties_AreConfigured()
        {
            Assert.That(_config.Ai.Difficulties.Count, Is.EqualTo(4));
            Assert.That(_config.Ai.For(AiDifficulty.Easy), Is.Not.Null);
            Assert.That(_config.Ai.For(AiDifficulty.Medium), Is.Not.Null);
            Assert.That(_config.Ai.For(AiDifficulty.Hard), Is.Not.Null);
            Assert.That(_config.Ai.For(AiDifficulty.Adaptive), Is.Not.Null);
        }

        [Test]
        public void Hard_MustClearTheClassicFleetInAtMost48Shots()
        {
            // Not an aspiration. The design doc states that if Hard cannot get under roughly 48
            // shots for 17 cells, the algorithm is wrong.
            Assert.That(_config.Ai.For(AiDifficulty.Hard).MaxAverageShotsToClearClassic, Is.EqualTo(48.0));
        }

        [Test]
        public void OnlyHardAndAdaptive_UseParityAndProbabilityDensity()
        {
            Assert.That(_config.Ai.For(AiDifficulty.Easy).UseProbabilityDensity, Is.False);
            Assert.That(_config.Ai.For(AiDifficulty.Medium).UseProbabilityDensity, Is.False);
            Assert.That(_config.Ai.For(AiDifficulty.Hard).UseProbabilityDensity, Is.True);
            Assert.That(_config.Ai.For(AiDifficulty.Adaptive).UseProbabilityDensity, Is.True);
        }

        [Test]
        public void Easy_IgnoresAnObviousFollowUpAQuarterOfTheTime()
        {
            Assert.That(_config.Ai.For(AiDifficulty.Easy).IgnoreObviousFollowUpChance, Is.EqualTo(0.25));
        }

        [Test]
        public void AdaptiveParams_MatchTheDesignDoc()
        {
            AdaptiveParams adaptive = _config.Ai.Adaptive;

            Assert.That(adaptive.WindowShots, Is.EqualTo(20));
            Assert.That(adaptive.TargetWinRate, Is.EqualTo(0.50));
            Assert.That(adaptive.NoiseFloor, Is.EqualTo(0));
            Assert.That(adaptive.NoiseCeiling, Is.EqualTo(6));
            Assert.That(adaptive.AdjustStep, Is.EqualTo(1));
        }

        [Test]
        public void TimeoutShot_UsesMedium_NeitherGivingTheMatchAwayNorRewardingAfk()
        {
            Assert.That(_config.Ai.TimeoutShotDifficulty, Is.EqualTo(AiDifficulty.Medium));
        }

        // --- Section 5: economy -------------------------------------------------------------

        [Test]
        public void OnlineRewards_Are100OnAWinAnd40OnALoss()
        {
            Assert.That(_config.Economy.OnlineWin.Coin, Is.EqualTo(100));
            Assert.That(_config.Economy.OnlineWin.Xp, Is.EqualTo(100));
            Assert.That(_config.Economy.OnlineLoss.Coin, Is.EqualTo(40));
            Assert.That(_config.Economy.OnlineLoss.Xp, Is.EqualTo(40));
        }

        [Test]
        public void AbandoningYourOwnMatch_PaysNothing()
        {
            Assert.That(_config.Economy.LossByOwnAbandon.Coin, Is.EqualTo(0));
            Assert.That(_config.Economy.LossByOwnAbandon.Xp, Is.EqualTo(0));
        }

        [Test]
        public void LocalTwoPlayer_NeverGrantsEconomy()
        {
            Assert.That(_config.Economy.LocalTwoPlayer.Coin, Is.EqualTo(0));
            Assert.That(_config.Economy.LocalTwoPlayer.Xp, Is.EqualTo(0));
        }

        [TestCase(AiDifficulty.Easy, 10)]
        [TestCase(AiDifficulty.Medium, 20)]
        [TestCase(AiDifficulty.Hard, 35)]
        [TestCase(AiDifficulty.Adaptive, 35)]
        public void AiWinRewards_MatchTheDesignDoc(AiDifficulty difficulty, int coin)
        {
            Assert.That(_config.Economy.AiWin[difficulty].Coin, Is.EqualTo(coin));
        }

        [Test]
        public void FirstWinOfTheDay_Adds150CoinAnd100Xp()
        {
            Assert.That(_config.Economy.FirstWinOfDayBonus.Coin, Is.EqualTo(150));
            Assert.That(_config.Economy.FirstWinOfDayBonus.Xp, Is.EqualTo(100));
        }

        [Test]
        public void WinStreak_AddsTenPercentPerWinUpToFifty()
        {
            Assert.That(_config.Economy.WinStreakStepMultiplier, Is.EqualTo(0.10));
            Assert.That(_config.Economy.WinStreakMaxMultiplier, Is.EqualTo(0.50));
        }

        [Test]
        public void AntiFarmCaps_MatchTheDesignDoc()
        {
            AntiFarmCaps caps = _config.Economy.AntiFarm;

            Assert.That(caps.MinMatchDurationSeconds, Is.EqualTo(60));
            Assert.That(caps.MinShotsForReward, Is.EqualTo(12));
            Assert.That(caps.MinShotsForRewardBlitz, Is.EqualTo(8));
            Assert.That(caps.MaxMatchesPerPairPer24h, Is.EqualTo(3));
            Assert.That(caps.DailyCoinCapFromAi, Is.EqualTo(150));
            Assert.That(caps.DailyCoinCapTotal, Is.EqualTo(2500));
        }

        [Test]
        public void LevelCurve_Is500Plus250PerLevelUpTo60()
        {
            LevelConfig levels = _config.Economy.Levels;

            Assert.That(levels.MaxLevel, Is.EqualTo(60));
            Assert.That(levels.XpForLevel(1), Is.EqualTo(500));
            Assert.That(levels.XpForLevel(2), Is.EqualTo(750));
            Assert.That(levels.XpForLevel(60), Is.EqualTo(15250));
        }

        [Test]
        public void LevelCurve_TotalsAboutFourHundredSeventyTwoThousandXp()
        {
            LevelConfig levels = _config.Economy.Levels;
            long total = 0;
            for (int level = 1; level <= levels.MaxLevel; level++) total += levels.XpForLevel(level);

            Assert.That(total, Is.EqualTo(472500));
        }

        // --- Section 4.8: Elo and seasons ---------------------------------------------------

        [Test]
        public void EloBaseline_Is1000WithAFloorOf100()
        {
            Assert.That(_config.Elo.StartingRating, Is.EqualTo(1000));
            Assert.That(_config.Elo.RatingFloor, Is.EqualTo(100));
            Assert.That(_config.Elo.PlacementMatches, Is.EqualTo(5));
            Assert.That(_config.Elo.AbandonPenalty, Is.EqualTo(15));
        }

        [TestCase(0, 40)]
        [TestCase(9, 40)]
        [TestCase(10, 32)]
        [TestCase(49, 32)]
        [TestCase(50, 24)]
        [TestCase(500, 24)]
        public void KFactor_StepsDownWithExperience(int gamesPlayed, int expected)
        {
            Assert.That(_config.Elo.KFactorFor(gamesPlayed), Is.EqualTo(expected));
        }

        [TestCase(800, "recruit")]
        [TestCase(899, "recruit")]
        [TestCase(900, "sailor")]
        [TestCase(1100, "boatswain")]
        [TestCase(1300, "lieutenant")]
        [TestCase(1500, "captain")]
        [TestCase(1700, "commodore")]
        [TestCase(1900, "admiral")]
        [TestCase(2400, "admiral")]
        public void RankTiers_MatchTheDesignDocBands(int rating, string expectedTierId)
        {
            Assert.That(_config.Elo.TierIdFor(rating), Is.EqualTo(expectedTierId));
        }

        [TestCase(1000, 1000)]
        [TestCase(1900, 1450)]
        [TestCase(600, 800)]
        public void SoftReset_HalvesTheDistanceFrom1000(int oldRating, int expected)
        {
            Assert.That(_config.Season.ApplySoftReset(oldRating), Is.EqualTo(expected));
        }

        [Test]
        public void Season_LastsSixtyDaysOnClassicOnly()
        {
            Assert.That(_config.Season.DurationDays, Is.EqualTo(60));
            Assert.That(_config.Season.RankedBoardId, Is.EqualTo(BoardConfig.ClassicId));
            Assert.That(_config.Season.RankedRuleSetId, Is.EqualTo(RuleFlags.ClassicId));
        }

        // --- Section 4.9: matchmaking -------------------------------------------------------

        [Test]
        public void CasualQueue_OpensAt250AndExpandsTo1000()
        {
            MatchmakingQueueConfig casual = _config.Matchmaking.Casual;

            Assert.That(casual.InitialEloWindow, Is.EqualTo(250));
            Assert.That(casual.WindowExpansionPerSecond, Is.EqualTo(25));
            Assert.That(casual.MaxEloWindow, Is.EqualTo(1000));
            Assert.That(casual.TicketTtlSeconds, Is.EqualTo(120));
        }

        [Test]
        public void RankedQueue_OpensAt100AndExpandsTo600()
        {
            MatchmakingQueueConfig ranked = _config.Matchmaking.Ranked;

            Assert.That(ranked.InitialEloWindow, Is.EqualTo(100));
            Assert.That(ranked.MaxEloWindow, Is.EqualTo(600));
            Assert.That(ranked.TicketTtlSeconds, Is.EqualTo(180));
        }

        [Test]
        public void BotFill_IsCasualOnlyAfter45Seconds_AndNeverRanked()
        {
            Assert.That(_config.Matchmaking.Casual.BotFillAfterSeconds, Is.EqualTo(45));
            Assert.That(_config.Matchmaking.Casual.AllowBotFill, Is.True);
            Assert.That(_config.Matchmaking.Ranked.AllowBotFill, Is.False);
            Assert.That(_config.Matchmaking.For(MatchMode.Ranked).AllowBotFill, Is.False);
        }

        // --- Section 5.7: ads ---------------------------------------------------------------

        [Test]
        public void InterstitialCaps_MatchTheDesignDoc()
        {
            AdConfig ads = _config.Ads;

            Assert.That(ads.InterstitialEveryNMatches, Is.EqualTo(4));
            Assert.That(ads.InterstitialMinSecondsBetween, Is.EqualTo(240));
            Assert.That(ads.InterstitialMaxPerDay, Is.EqualTo(6));
            Assert.That(ads.NoInterstitialsForHoursAfterInstall, Is.EqualTo(24));
            Assert.That(ads.NeverInterstitialAfterLoss, Is.True);
        }

        [Test]
        public void SonarHintAd_IsOfflineOnly_BecauseOnlineItWouldBePayToWin()
        {
            Assert.That(_config.Ads.RewardedSonarHintAiOnly, Is.True);
        }

        [Test]
        public void Banners_AreNotUsed()
        {
            Assert.That(_config.Ads.BannersEnabled, Is.False);
        }

        // --- Section 8.3: kill switches -----------------------------------------------------

        [Test]
        public void AllKillSwitches_DefaultToOn()
        {
            KillSwitches switches = _config.Switches;

            Assert.That(switches.OnlineEnabled, Is.True);
            Assert.That(switches.RankedEnabled, Is.True);
            Assert.That(switches.AsyncEnabled, Is.True);
            Assert.That(switches.IapEnabled, Is.True);
            Assert.That(switches.AdsEnabled, Is.True);
            Assert.That(switches.BotFillEnabled, Is.True);
            Assert.That(switches.PushEnabled, Is.True);
            Assert.That(switches.MaintenanceMessageKey, Is.Empty);
        }

        // --- Lookups ------------------------------------------------------------------------

        [Test]
        public void UnknownBoardId_Throws_RatherThanSilentlyFallingBack()
        {
            Assert.That(() => _config.Board("does-not-exist"), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(_config.TryGetBoard("does-not-exist", out _), Is.False);
        }

        private int[] LengthsOf(string boardId)
        {
            IReadOnlyList<ShipSpec> fleet = _config.Board(boardId).Fleet;
            int[] lengths = new int[fleet.Count];
            for (int i = 0; i < fleet.Count; i++) lengths[i] = fleet[i].Length;
            return lengths;
        }
    }
}
