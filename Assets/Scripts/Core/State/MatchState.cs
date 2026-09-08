#nullable enable

namespace Armada.Core
{
    /// <summary>
    /// The authoritative match. Lives only on the server, in a single Cloud Save document
    /// (<c>match:&lt;matchId&gt;</c>) written with conditional writes. Budget: under 1.5 KB
    /// serialized for a classic match (design doc section 6.3).
    /// <para>
    /// This type is NEVER sent to a client. Clients receive
    /// <see cref="MatchStateView"/> built by <see cref="MatchProjection.Project"/>.
    /// </para>
    /// </summary>
    public struct MatchState
    {
        /// <summary>
        /// Reserved player id for the AI side. Giving the AI an identity keeps every mutation on
        /// one code path: the engine never needs to know whether a move came from a person, and
        /// offline and online matches behave identically. Real player ids are UGS GUIDs, so this
        /// cannot collide with one.
        /// </summary>
        public const string AiPlayerId = "__ai__";

        public string MatchId;
        public int ProtocolVersion;

        public MatchMode Mode;
        public string BoardId;
        public string RuleSetId;
        public MatchPhase Phase;

        /// <summary>
        /// Strictly increasing, +1 per accepted mutation. Basis of idempotency: clients send
        /// <c>expectedSequence</c> and a mismatch yields <c>ERR_STALE_ACTION|serverSeq</c>.
        /// </summary>
        public long Sequence;

        public string PlayerA;

        /// <summary>Null while waiting for an opponent, and for AI matches.</summary>
        public string? PlayerB;

        /// <summary>Set only when slot B is an AI opponent.</summary>
        public AiDifficulty? AiDifficulty;

        /// <summary>True when slot B was bot-filled. Never true in ranked; tagged in analytics.</summary>
        public bool IsBotFilled;

        public string? PrivateCode;

        /// <summary>Decided by server RNG, recorded in state and in analytics.</summary>
        public PlayerSlot FirstMover;

        public PlayerSlot CurrentTurn;

        public PlayerBoard BoardA;
        public PlayerBoard BoardB;

        public long CreatedAtUnixMs;

        /// <summary>Deadline of whatever the current phase is waiting on.</summary>
        public long PhaseDeadlineUnixMs;

        public long LastActionUnixMs;

        public int ConsecutiveTimeoutsA;
        public int ConsecutiveTimeoutsB;

        /// <summary>Shots resolved so far in the whole match. Used to stamp sunk ships.</summary>
        public int ShotIndex;

        public MatchEndReason? EndReason;
        public PlayerSlot? Winner;
        public long? FinishedAtUnixMs;

        public readonly bool IsAiMatch
        {
            get { return AiDifficulty.HasValue; }
        }

        public readonly PlayerSlot? SlotOf(string playerId)
        {
            if (string.Equals(PlayerA, playerId, System.StringComparison.Ordinal)) return PlayerSlot.A;
            if (PlayerB != null && string.Equals(PlayerB, playerId, System.StringComparison.Ordinal)) return PlayerSlot.B;
            return null;
        }

        public static PlayerSlot Other(PlayerSlot slot)
        {
            return slot == PlayerSlot.A ? PlayerSlot.B : PlayerSlot.A;
        }
    }
}
