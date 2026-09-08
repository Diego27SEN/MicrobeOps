#nullable enable

namespace Armada.Core
{
    /// <summary>
    /// Advertising caps from design doc section 5.7. The product principle behind these numbers is
    /// non-negotiable: limited frequency, always closable, rewarded ads are optional and never give
    /// a competitive advantage.
    /// </summary>
    public sealed class AdConfig
    {
        /// <summary>Interstitials only appear when closing the match summary.</summary>
        public int InterstitialEveryNMatches { get; set; } = 4;

        public int InterstitialMinSecondsBetween { get; set; } = 240;

        public int InterstitialMaxPerDay { get; set; } = 6;

        /// <summary>No interstitials at all during the first hours after install.</summary>
        public int NoInterstitialsForHoursAfterInstall { get; set; } = 24;

        /// <summary>Never show an interstitial right after a defeat.</summary>
        public bool NeverInterstitialAfterLoss { get; set; } = true;

        public int RewardedDoubleRewardMaxPerDay { get; set; } = 3;

        public int RewardedExtraDailyChestMaxPerDay { get; set; } = 1;

        /// <summary>
        /// Sonar hint rewarded ad: offline AI matches only. Offering it online would be
        /// pay-to-win, which the product principles forbid outright.
        /// </summary>
        public bool RewardedSonarHintAiOnly { get; set; } = true;

        /// <summary>Banners are not used.</summary>
        public bool BannersEnabled { get; set; }
    }
}
