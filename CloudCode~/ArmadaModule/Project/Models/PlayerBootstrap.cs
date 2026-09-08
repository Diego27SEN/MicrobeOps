#nullable enable

using System;

namespace Armada.CloudCode
{
    /// <summary>
    /// Everything the client needs at startup, in one call.
    /// <para>
    /// Five round trips at boot was inherited debt: it made cold start slow and every one of them
    /// was another chance to fail on a bad connection. One call either works or does not, and the
    /// offline path is the same either way.
    /// </para>
    /// </summary>
    public sealed class PlayerBootstrap
    {
        public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
        public EconomyDto Economy { get; set; } = new EconomyDto();
        public string[] Inventory { get; set; } = Array.Empty<string>();
        public ActiveMatchDto[] ActiveMatches { get; set; } = Array.Empty<ActiveMatchDto>();
        public DailyStateDto DailyState { get; set; } = new DailyStateDto();
        public SeasonStateDto SeasonState { get; set; } = new SeasonStateDto();

        /// <summary>True the first time a player is seen, so the client can offer the onboarding.</summary>
        public bool IsNewPlayer { get; set; }
    }

    public sealed class PlayerProfileDto
    {
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Public display name, in the <c>Nombre#1234</c> shape. Empty until the player picks one.</summary>
        public string DisplayName { get; set; } = string.Empty;

        public int Level { get; set; } = 1;
        public long TotalXp { get; set; }
        public string AvatarId { get; set; } = string.Empty;
        public string FrameId { get; set; } = string.Empty;
        public string BoardThemeId { get; set; } = string.Empty;
        public string FleetSkinId { get; set; } = string.Empty;

        /// <summary>True once a Google or Apple identity is attached. Anonymous progress is one lost phone away from gone.</summary>
        public bool IsLinked { get; set; }
    }

    public sealed class EconomyDto
    {
        public int Coin { get; set; }
        public int Gem { get; set; }
    }

    /// <summary>
    /// A match the player can return to. Deliberately a pointer, not a state: the board arrives
    /// through <c>GetMatchState</c>, which is the only thing allowed to project it.
    /// </summary>
    public sealed class ActiveMatchDto
    {
        public string MatchId { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public bool IsYourTurn { get; set; }
        public long DeadlineUnixMs { get; set; }
        public long Sequence { get; set; }
    }

    public sealed class DailyStateDto
    {
        public string DateUtc { get; set; } = string.Empty;
        public bool DailyRewardAvailable { get; set; }
        public int CoinEarnedToday { get; set; }
        public int CoinFromAiToday { get; set; }
        public int OnlineWinStreak { get; set; }
        public bool FirstWinClaimedToday { get; set; }
    }

    public sealed class SeasonStateDto
    {
        public int SeasonNumber { get; set; }
        public long EndsAtUnixMs { get; set; }
        public int Rating { get; set; }

        /// <summary>Localization key suffix, never display text.</summary>
        public string TierId { get; set; } = string.Empty;

        public int PlacementMatchesRemaining { get; set; }

        /// <summary>Rating stays hidden until the placement matches are done.</summary>
        public bool RatingVisible { get; set; }
    }
}
