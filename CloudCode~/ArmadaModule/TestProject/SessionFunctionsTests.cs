#nullable enable

using Armada.CloudCode;
using Armada.Core;
using NUnit.Framework;

namespace Armada.CloudCode.Tests
{
    /// <summary>
    /// Endpoint tests with a null execution context.
    /// <para>
    /// That is not a shortcut: it is the shape the design doc asks for. A function that needs a
    /// live context to be testable is a function nobody tests before a deploy, so the endpoints
    /// take their dependencies through the constructor and treat the context as optional data.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class SessionFunctionsTests
    {
        private SessionFunctions _functions = null!;

        [SetUp]
        public void SetUp()
        {
            _functions = new SessionFunctions(new ConfigProvider());
        }

        [Test]
        public void GetServerInfo_WithNoContext_StillAnswers()
        {
            Result<ServerInfo> result = _functions.GetServerInfo(null!);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.ProtocolVersion, Is.EqualTo(GameProtocol.Version));
            Assert.That(result.Data.ServerTimeUnixMs, Is.GreaterThan(0));
        }

        [Test]
        public void GetServerInfo_CarriesTheKillSwitches_SoTheClientCanDegradeBeforeDoingAnything()
        {
            Result<ServerInfo> result = _functions.GetServerInfo(null!);

            Assert.That(result.Data!.KillSwitches, Is.Not.Null);
            Assert.That(result.Data.KillSwitches.OnlineEnabled, Is.True);
            Assert.That(result.Data.KillSwitches.MaintenanceMessageKey, Is.Empty);
        }

        [Test]
        public void GetGameConfig_ForACurrentClient_ReturnsTheWholeConfiguration()
        {
            Result<GameConfig> result = _functions.GetGameConfig(null!, GameProtocol.Version);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Data!.Boards.Count, Is.EqualTo(3));
            Assert.That(result.Data.RuleSets.Count, Is.EqualTo(3));
            Assert.That(result.Data.Board(BoardConfig.ClassicId).FleetCellCount, Is.EqualTo(17));
        }

        [Test]
        public void GetGameConfig_ForAClientBelowTheMinimum_IsRefused()
        {
            Result<GameConfig> result = _functions.GetGameConfig(null!, GameProtocol.MinSupportedVersion - 1);

            Assert.That(result.Success, Is.False);
            Assert.That(ErrorCodes.CodeOf(result.Error!), Is.EqualTo(ErrorCodes.MinVersion));
        }

        [Test]
        public void GetGameConfig_ForAClientAheadOfTheServer_IsRefused()
        {
            // A client newer than the server happens during a staged rollout, when the backend has
            // not caught up yet. Refusing is what stops it playing on rules the server cannot judge.
            Result<GameConfig> result = _functions.GetGameConfig(null!, GameProtocol.Version + 1);

            Assert.That(result.Success, Is.False);
            Assert.That(ErrorCodes.CodeOf(result.Error!), Is.EqualTo(ErrorCodes.ProtocolMismatch));
        }

        [Test]
        public void Endpoints_NeverReturnProse_OnlyErrorCodes()
        {
            Result<GameConfig> result = _functions.GetGameConfig(null!, 0);

            Assert.That(result.Error, Does.StartWith("ERR_"));
            Assert.That(result.Error, Does.Not.Contain(" "));
        }

        [Test]
        public void Constructor_RejectsAMissingConfigProvider()
        {
            Assert.That(() => new SessionFunctions(null!), Throws.TypeOf<System.ArgumentNullException>());
        }
    }

    [TestFixture]
    public sealed class ConfigProviderTests
    {
        [Test]
        public void Get_ReturnsTheDesignDocNumbers()
        {
            GameConfig config = new ConfigProvider().Get(0);

            Assert.That(config.Economy.OnlineWin.Coin, Is.EqualTo(100));
            Assert.That(config.Elo.StartingRating, Is.EqualTo(1000));
            Assert.That(config.Timers.RealTime.TurnSeconds, Is.EqualTo(30));
        }

        [Test]
        public void Get_WithinTheTtl_ReturnsTheSameInstance()
        {
            // A burst of matches must not turn into a burst of Remote Config reads.
            ConfigProvider provider = new ConfigProvider();

            GameConfig first = provider.Get(0);
            GameConfig second = provider.Get((long)ConfigProvider.CacheTtl.TotalMilliseconds - 1);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Get_PastTheTtl_ReloadsSoLiveOpsChangesLand()
        {
            ConfigProvider provider = new ConfigProvider();

            GameConfig first = provider.Get(0);
            GameConfig second = provider.Get((long)ConfigProvider.CacheTtl.TotalMilliseconds + 1);

            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void Invalidate_DropsTheCache()
        {
            ConfigProvider provider = new ConfigProvider();

            GameConfig first = provider.Get(0);
            provider.Invalidate();

            Assert.That(provider.Get(0), Is.Not.SameAs(first));
        }

        [Test]
        public void UntilRemoteConfigIsWired_ReadsComeFromTheEmbeddedSeed()
        {
            // Recorded as a fact rather than left implicit: today every read is the fallback, and
            // this test is what will fail loudly when the real read is wired in and forgets to
            // clear the flag.
            ConfigProvider provider = new ConfigProvider();
            provider.Get(0);

            Assert.That(provider.UsingEmbeddedFallback, Is.True);
        }
    }
}
