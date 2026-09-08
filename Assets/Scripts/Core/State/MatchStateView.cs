#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>
    /// Status of one of YOUR OWN ships. Safe to expose in full: it is your fleet.
    /// </summary>
    public sealed class ShipStatusView
    {
        public ShipClass Class { get; set; }
        public int Length { get; set; }
        public Coord Bow { get; set; }
        public Orientation Orientation { get; set; }
        public int HitCount { get; set; }
        public bool IsSunk { get; set; }
    }

    /// <summary>
    /// An opponent ship YOU sank. Both fields are gated by rule flags:
    /// <see cref="Class"/> only when <c>announceSunkShipType</c>, <see cref="Cells"/> only when
    /// <c>revealSunkCells</c>. When a flag is off the field stays null: it is not enough to hide
    /// it in the UI, it must not be in the payload.
    /// </summary>
    public sealed class SunkShipView
    {
        public ShipClass? Class { get; set; }
        public Coord[]? Cells { get; set; }
        public int ShotIndex { get; set; }
    }

    /// <summary>Your own half of the board. Your ships, so nothing here is secret from you.</summary>
    public sealed class OwnBoardView
    {
        public string OccupancyBase64 { get; set; } = string.Empty;
        public string HitsBase64 { get; set; } = string.Empty;
        public string IncomingShotsBase64 { get; set; } = string.Empty;
        public ShipStatusView[] Fleet { get; set; } = Array.Empty<ShipStatusView>();
    }

    /// <summary>
    /// Your tracking grid over the opponent's board: strictly what you learned by shooting.
    /// <para>
    /// Nothing derived from the opponent's <c>occupancy</c> belongs here. No per-row or per-column
    /// counts, no "cells remaining", no length of a partially hit ship, no total ships afloat.
    /// Anything that shrinks the search space is a leak, and this class is where such a leak would
    /// be introduced by accident. Adding a field here requires backend-security review.
    /// </para>
    /// </summary>
    public sealed class TrackingBoardView
    {
        /// <summary>Cells you have fired at.</summary>
        public string ShotsBase64 { get; set; } = string.Empty;

        /// <summary>Of those, the ones that hit something.</summary>
        public string HitsBase64 { get; set; } = string.Empty;

        /// <summary>Opponent ships you have sunk, redacted per rule flags.</summary>
        public SunkShipView[] SunkShips { get; set; } = Array.Empty<SunkShipView>();
    }

    /// <summary>
    /// Both fleets, revealed. Only ever attached when <see cref="MatchPhase.Finished"/>.
    /// </summary>
    public sealed class RevealedFleetsView
    {
        public ShipPlacement[] Yours { get; set; } = Array.Empty<ShipPlacement>();
        public ShipPlacement[] Opponent { get; set; } = Array.Empty<ShipPlacement>();
    }

    /// <summary>
    /// What one player is allowed to know about a match. Produced exclusively by
    /// <see cref="MatchProjection.Project"/>.
    /// <para>
    /// This is the security surface of the whole project. The tests that guard it live in
    /// <c>Armada.Core.Tests</c>; the decisive one asserts on the SERIALIZED JSON, not on this
    /// object, because that is what catches the day someone adds a debug field.
    /// </para>
    /// </summary>
    public sealed class MatchStateView
    {
        public string MatchId { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; } = GameProtocol.Version;

        public MatchMode Mode { get; set; }
        public string BoardId { get; set; } = string.Empty;
        public string RuleSetId { get; set; } = string.Empty;
        public MatchPhase Phase { get; set; }

        /// <summary>Echo it back as <c>expectedSequence</c> on the next mutating call.</summary>
        public long Sequence { get; set; }

        public bool IsYourTurn { get; set; }
        public bool YouAreFirstMover { get; set; }

        /// <summary>Deadline of the current phase, in server time.</summary>
        public long PhaseDeadlineUnixMs { get; set; }

        public long ServerTimeUnixMs { get; set; }

        public int YourConsecutiveTimeouts { get; set; }

        /// <summary>How many shots you must declare this turn. Above one only in Salvo.</summary>
        public int ShotsThisTurn { get; set; } = 1;

        public OwnBoardView You { get; set; } = new OwnBoardView();
        public TrackingBoardView Opponent { get; set; } = new TrackingBoardView();

        /// <summary>Public display name of the opponent, or null when there is none yet.</summary>
        public string? OpponentName { get; set; }

        /// <summary>
        /// The ONLY thing you learn about the opponent during placement. Not their progress,
        /// not a partial layout, not a timestamp of when they started.
        /// </summary>
        public bool OpponentReady { get; set; }

        public bool OpponentIsAi { get; set; }

        public MatchEndReason? EndReason { get; set; }
        public MatchOutcome? Outcome { get; set; }

        /// <summary>Non-null only when <see cref="Phase"/> is <see cref="MatchPhase.Finished"/>.</summary>
        public RevealedFleetsView? Reveal { get; set; }
    }
}
