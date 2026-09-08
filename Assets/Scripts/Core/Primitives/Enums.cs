#nullable enable

namespace Armada.Core
{
    public enum Orientation : byte
    {
        Horizontal = 0,
        Vertical = 1
    }

    /// <summary>
    /// Generic naval class names on purpose: they are descriptive and not protectable.
    /// See design doc section 15.1 on trademark constraints.
    /// </summary>
    public enum ShipClass : byte
    {
        Carrier = 0,
        Battleship = 1,
        Cruiser = 2,
        Submarine = 3,
        Destroyer = 4,
        PatrolBoat = 5
    }

    public enum ShotOutcome : byte
    {
        Miss = 0,
        Hit = 1,
        Sunk = 2,

        /// <summary>A sinking shot that also ended the match.</summary>
        Win = 3
    }

    public enum MatchPhase : byte
    {
        Created = 0,
        Placement = 1,
        InProgress = 2,
        Finished = 3
    }

    public enum MatchEndReason : byte
    {
        FleetDestroyed = 0,
        Forfeit = 1,
        Abandoned = 2,
        TimeoutStrikes = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Match flavour. Added to the contract at M0: sections 7.2 and 7.3 use it but 7.1 did not
    /// declare it. Changing this enum is a contract change.
    /// </summary>
    public enum MatchMode : byte
    {
        /// <summary>Offline, against the local AI. Never grants online economy.</summary>
        VsAi = 0,

        /// <summary>Pass-and-play on a single device. Never grants economy at all.</summary>
        LocalTwoPlayer = 1,

        /// <summary>Online real-time quickmatch.</summary>
        Casual = 2,

        /// <summary>Online real-time ranked. Elo, leaderboard, seasons. Never bot-filled.</summary>
        Ranked = 3,

        /// <summary>Online turn-based with push notifications.</summary>
        Async = 4,

        /// <summary>Online private match joined by code.</summary>
        Private = 5
    }

    /// <summary>
    /// AI difficulty. Added to the contract at M0 for the same reason as <see cref="MatchMode"/>.
    /// Behaviour and accuracy targets are specified in design doc section 4.6.
    /// </summary>
    public enum AiDifficulty : byte
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        Adaptive = 3
    }

    /// <summary>Result of a finished match from one player's point of view.</summary>
    public enum MatchOutcome : byte
    {
        Won = 0,
        Lost = 1,
        Cancelled = 2
    }

    /// <summary>Which side of the match a player sits on.</summary>
    public enum PlayerSlot : byte
    {
        A = 0,
        B = 1
    }
}
