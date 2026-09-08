#nullable enable

using System;
using Armada.Core;
using Unity.Services.CloudCode.Core;

namespace Armada.CloudCode
{
    /// <summary>
    /// Session and configuration endpoints: the handshake and the single config read point.
    /// <para>
    /// These run before anything else in a session, so they are the ones that must never throw.
    /// Everything here degrades rather than failing.
    /// </para>
    /// </summary>
    public class SessionFunctions
    {
        private readonly ConfigProvider _config;

        public SessionFunctions(ConfigProvider config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Boot handshake. The client compares protocol versions before doing anything else, so a
        /// build below the minimum is turned away at the door instead of failing halfway through a
        /// match with a confusing error.
        /// </summary>
        [CloudCodeFunction("GetServerInfo")]
        public Result<ServerInfo> GetServerInfo(IExecutionContext context)
        {
            long now = NowUnixMs();
            GameConfig config = _config.Get(now);

            return Result<ServerInfo>.Ok(new ServerInfo
            {
                ProtocolVersion = GameProtocol.Version,
                MinClientProtocolVersion = config.Switches.MinClientProtocolVersion,
                ServerTimeUnixMs = now,
                Environment = context?.EnvironmentName ?? string.Empty,
                KillSwitches = ToDto(config.Switches)
            });
        }

        /// <summary>
        /// The only place configuration crosses to the client. Rejects a client whose protocol the
        /// server no longer speaks, so an outdated build cannot start a match on stale rules.
        /// </summary>
        [CloudCodeFunction("GetGameConfig")]
        public Result<GameConfig> GetGameConfig(IExecutionContext context, int clientProtocolVersion)
        {
            GameConfig config = _config.Get(NowUnixMs());

            if (clientProtocolVersion < config.Switches.MinClientProtocolVersion)
            {
                return Result<GameConfig>.Fail(
                    ErrorCodes.Format(ErrorCodes.MinVersion, config.Switches.MinClientProtocolVersion));
            }

            if (clientProtocolVersion > GameProtocol.Version)
            {
                return Result<GameConfig>.Fail(
                    ErrorCodes.Format(ErrorCodes.ProtocolMismatch, GameProtocol.Version));
            }

            return Result<GameConfig>.Ok(config);
        }

        /// <summary>
        /// Everything the client needs at startup, in one call.
        /// <para>
        /// Player data still comes from defaults: Cloud Save reads land once the service account
        /// exists. The shape is final, so the client and its tests are written against what they
        /// will actually receive rather than being rewritten later.
        /// </para>
        /// </summary>
        [CloudCodeFunction("GetPlayerBootstrap")]
        public Result<PlayerBootstrap> GetPlayerBootstrap(IExecutionContext context)
        {
            long now = NowUnixMs();
            GameConfig config = _config.Get(now);

            string playerId = context?.PlayerId ?? string.Empty;
            if (string.IsNullOrEmpty(playerId))
            {
                // No identity means no data to fetch. It is not an internal error, so it does not
                // get reported as one.
                return Result<PlayerBootstrap>.Fail(ErrorCodes.MatchNotFound);
            }

            return Result<PlayerBootstrap>.Ok(PlayerBootstrapBuilder.ForNewPlayer(playerId, config, now));
        }

        internal static KillSwitchesDto ToDto(KillSwitches switches)
        {
            return new KillSwitchesDto
            {
                OnlineEnabled = switches.OnlineEnabled,
                RankedEnabled = switches.RankedEnabled,
                AsyncEnabled = switches.AsyncEnabled,
                IapEnabled = switches.IapEnabled,
                AdsEnabled = switches.AdsEnabled,
                BotFillEnabled = switches.BotFillEnabled,
                PushEnabled = switches.PushEnabled,
                RematchEnabled = switches.RematchEnabled,
                EmotesEnabled = switches.EmotesEnabled,
                MaintenanceMessageKey = switches.MaintenanceMessageKey
            };
        }

        /// <summary>
        /// The server clock. Core is not allowed to read it, so the module is where it enters, and
        /// keeping that in one named place means there is one thing to change if it ever has to
        /// come from somewhere else.
        /// </summary>
        private static long NowUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
