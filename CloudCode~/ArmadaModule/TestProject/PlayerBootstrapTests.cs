#nullable enable

using Armada.CloudCode;
using Armada.Core;
using NUnit.Framework;

namespace Armada.CloudCode.Tests
{
    [TestFixture]
    public sealed class PlayerBootstrapTests
    {
        private const long Epoch = 1_700_000_000_000L;

        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = DefaultGameConfig.Create();
        }

        [Test]
        public void ANewPlayer_StartsAtLevelOneWithNothing()
        {
            PlayerBootstrap boot = PlayerBootstrapBuilder.ForNewPlayer("p1", _config, Epoch);

            Assert.That(boot.IsNewPlayer, Is.True);
            Assert.That(boot.Profile.PlayerId, Is.EqualTo("p1"));
            Assert.That(boot.Profile.Level, Is.EqualTo(1));
            Assert.That(boot.Profile.IsLinked, Is.False);
            Assert.That(boot.Economy.Coin, Is.EqualTo(0));
            Assert.That(boot.Economy.Gem, Is.EqualTo(0));
            Assert.That(boot.Inventory, Is.Empty);
            Assert.That(boot.ActiveMatches, Is.Empty);
        }

        [Test]
        public void ANewPlayer_HasTheirFirstDailyRewardWaiting()
        {
            Assert.That(PlayerBootstrapBuilder.ForNewPlayer("p1", _config, Epoch).DailyState.DailyRewardAvailable, Is.True);
        }

        [Test]
        public void ANewPlayer_StartsAtTheConfiguredRating_WithItHidden()
        {
            SeasonStateDto season = PlayerBootstrapBuilder.ForNewPlayer("p1", _config, Epoch).SeasonState;

            Assert.That(season.Rating, Is.EqualTo(1000));
            Assert.That(season.PlacementMatchesRemaining, Is.EqualTo(5));
            Assert.That(season.RatingVisible, Is.False);
            Assert.That(season.TierId, Is.EqualTo("sailor"));
        }

        [Test]
        public void DailyState_OnTheSameDay_KeepsTheStoredCounters()
        {
            PlayerDailyStats stored = new PlayerDailyStats
            {
                DateUtc = PlayerBootstrapBuilder.UtcDay(Epoch),
                CoinEarnedToday = 340,
                CoinFromAiToday = 60,
                OnlineWinStreak = 3,
                FirstWinClaimedToday = true
            };

            DailyStateDto daily = PlayerBootstrapBuilder.BuildDailyState(stored, Epoch);

            Assert.That(daily.CoinEarnedToday, Is.EqualTo(340));
            Assert.That(daily.FirstWinClaimedToday, Is.True);
            Assert.That(daily.DailyRewardAvailable, Is.False);
        }

        [Test]
        public void DailyState_OnANewDay_ResetsTheCountersButKeepsTheStreak()
        {
            // Opening the app at 00:04 UTC must not show yesterday's counters, which is why the
            // rollover happens here as well as in the scheduler. The win streak is not a daily
            // counter: it breaks on a loss, not on a clock.
            PlayerDailyStats yesterday = new PlayerDailyStats
            {
                DateUtc = PlayerBootstrapBuilder.UtcDay(Epoch),
                CoinEarnedToday = 2400,
                CoinFromAiToday = 150,
                OnlineWinStreak = 4,
                FirstWinClaimedToday = true
            };

            DailyStateDto daily = PlayerBootstrapBuilder.BuildDailyState(yesterday, Epoch + 86_400_000L);

            Assert.That(daily.CoinEarnedToday, Is.EqualTo(0));
            Assert.That(daily.CoinFromAiToday, Is.EqualTo(0));
            Assert.That(daily.FirstWinClaimedToday, Is.False);
            Assert.That(daily.DailyRewardAvailable, Is.True);
            Assert.That(daily.OnlineWinStreak, Is.EqualTo(4), "a new day does not break a streak");
        }

        [TestCase(0, 5, false)]
        [TestCase(3, 2, false)]
        [TestCase(5, 0, true)]
        [TestCase(40, 0, true)]
        public void SeasonState_HidesTheRatingUntilPlacementsAreDone(int played, int expectedRemaining, bool visible)
        {
            SeasonStateDto season = PlayerBootstrapBuilder.BuildSeasonState(1180, played, 1, Epoch, _config);

            Assert.That(season.PlacementMatchesRemaining, Is.EqualTo(expectedRemaining));
            Assert.That(season.RatingVisible, Is.EqualTo(visible));
            Assert.That(season.TierId, Is.EqualTo("boatswain"));
        }

        [Test]
        public void GetPlayerBootstrap_WithoutAnIdentity_IsRefusedWithoutClaimingAnInternalError()
        {
            SessionFunctions functions = new SessionFunctions(new ConfigProvider());

            Result<PlayerBootstrap> result = functions.GetPlayerBootstrap(null!);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.StartWith("ERR_"));
            Assert.That(ErrorCodes.CodeOf(result.Error!), Is.Not.EqualTo(ErrorCodes.Internal));
        }

        [Test]
        public void UtcDay_FormatsTheBoundaryEveryDailyCounterUses()
        {
            Assert.That(PlayerBootstrapBuilder.UtcDay(0), Is.EqualTo("1970-01-01"));
            Assert.That(PlayerBootstrapBuilder.UtcDay(86_400_000L - 1), Is.EqualTo("1970-01-01"));
            Assert.That(PlayerBootstrapBuilder.UtcDay(86_400_000L), Is.EqualTo("1970-01-02"));
        }
    }
}
