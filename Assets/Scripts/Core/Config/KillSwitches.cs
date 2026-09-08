#nullable enable

namespace Armada.Core
{
    /// <summary>
    /// Feature flags that can be turned off from Remote Config without a client release.
    /// Design doc section 8.3. Every one of them has a defined degradation path in the client:
    /// turning a switch off must never break the game, only remove that feature behind a
    /// localized notice.
    /// </summary>
    public sealed class KillSwitches
    {
        public bool OnlineEnabled { get; set; } = true;
        public bool RankedEnabled { get; set; } = true;
        public bool AsyncEnabled { get; set; } = true;
        public bool IapEnabled { get; set; } = true;
        public bool AdsEnabled { get; set; } = true;

        /// <summary>Casual bot fill. Never applies to ranked, whatever this says.</summary>
        public bool BotFillEnabled { get; set; } = true;

        public bool PushEnabled { get; set; } = true;
        public bool RematchEnabled { get; set; } = true;
        public bool EmotesEnabled { get; set; } = true;

        /// <summary>Builds below this protocol version are refused at handshake.</summary>
        public int MinClientProtocolVersion { get; set; } = GameProtocol.MinSupportedVersion;

        /// <summary>Localization key of the maintenance banner, or empty when there is none.</summary>
        public string MaintenanceMessageKey { get; set; } = string.Empty;
    }
}
