#nullable enable

namespace Armada.Core
{
    /// <summary>Matchmaking window behaviour for one queue. Design doc section 4.9.</summary>
    public sealed class MatchmakingQueueConfig
    {
        public int InitialEloWindow { get; set; }

        /// <summary>Window growth per second of waiting.</summary>
        public int WindowExpansionPerSecond { get; set; } = 25;

        public int MaxEloWindow { get; set; }

        /// <summary>Ticket time-to-live in seconds.</summary>
        public int TicketTtlSeconds { get; set; }

        /// <summary>
        /// Seconds before an AI opponent fills the slot. Zero or less means never.
        /// Bot fill is NEVER allowed in ranked.
        /// </summary>
        public int BotFillAfterSeconds { get; set; }

        public bool AllowBotFill
        {
            get { return BotFillAfterSeconds > 0; }
        }
    }

    public sealed class MatchmakingConfig
    {
        public MatchmakingQueueConfig Casual { get; set; } = new MatchmakingQueueConfig();
        public MatchmakingQueueConfig Ranked { get; set; } = new MatchmakingQueueConfig();

        public MatchmakingQueueConfig For(MatchMode mode)
        {
            return mode == MatchMode.Ranked ? Ranked : Casual;
        }
    }
}
