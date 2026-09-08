#nullable enable

namespace Armada.Core
{
    /// <summary>
    /// Timer profile. Real-time and async matches use different profiles with the same shape.
    /// Values from design doc section 4.4, mirrored in Remote Config under <c>TIMERS</c>.
    /// </summary>
    public sealed class TimerProfile
    {
        /// <summary>Seconds to submit a placement. On expiry the server auto-deploys.</summary>
        public int PlacementSeconds { get; set; }

        /// <summary>Seconds to take a turn. On expiry: automatic valid random shot plus one strike.</summary>
        public int TurnSeconds { get; set; }

        /// <summary>Strikes before the player is treated as having abandoned.</summary>
        public int MaxConsecutiveTimeouts { get; set; }

        /// <summary>Grace window during which the turn clock does not run after a detected disconnect. Zero when not applicable.</summary>
        public int ReconnectGraceSeconds { get; set; }

        /// <summary>Idle seconds after which the opponent may claim victory.</summary>
        public int AbandonSeconds { get; set; }

        /// <summary>Window to accept a rematch offer. Zero when not applicable.</summary>
        public int RematchOfferSeconds { get; set; }
    }

    public sealed class TimersConfig
    {
        public TimerProfile RealTime { get; set; } = new TimerProfile();
        public TimerProfile Async { get; set; } = new TimerProfile();

        public TimerProfile For(MatchMode mode)
        {
            return mode == MatchMode.Async ? Async : RealTime;
        }
    }
}
