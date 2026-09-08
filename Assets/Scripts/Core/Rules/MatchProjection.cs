#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Builds the player-specific <see cref="MatchStateView"/> with the opponent's information
    /// removed. This is the single most security-critical function in the project.
    /// <para>
    /// The threat model is simple: if the client ever holds the opponent's board, the game is
    /// broken, and no obfuscation resists a memory dump. The only real defence is that the
    /// opponent's <c>occupancy</c> never leaves the server before the match is over.
    /// </para>
    /// <para>
    /// Invariants, all covered by tests in <c>Armada.Core.Tests</c>:
    /// <list type="number">
    /// <item>A's view never contains cells of B's ships that have not been hit.</item>
    /// <item>A's view never contains B's occupancy in any form, including per-row or per-column counts.</item>
    /// <item>Both fleets are revealed only when the phase is <see cref="MatchPhase.Finished"/>.</item>
    /// <item>During placement, A sees nothing about B beyond <c>opponentReady</c>.</item>
    /// <item>The serialized JSON of A's view contains no secret coordinate of B.</item>
    /// </list>
    /// The last one is the one that matters: it asserts on the wire format, not on this object,
    /// so it catches the day someone adds a convenient debug field.
    /// </para>
    /// <para>
    /// Any change to this file or to the view DTOs requires backend-security review. That is a
    /// hard rule, not a preference.
    /// </para>
    /// </summary>
    public static class MatchProjection
    {
        public static MatchStateView Project(in MatchState state, string forPlayerId, BoardConfig board, RuleFlags flags, long nowUnixMs)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            PlayerSlot? slot = state.SlotOf(forPlayerId);
            if (slot == null)
            {
                throw new ArgumentException("Player is not part of this match.", nameof(forPlayerId));
            }

            PlayerSlot mine = slot.Value;
            PlayerSlot theirs = MatchState.Other(mine);

            PlayerBoard ownBoard = BoardOf(in state, mine);
            PlayerBoard opponentBoard = BoardOf(in state, theirs);

            bool finished = state.Phase == MatchPhase.Finished;

            MatchStateView view = new MatchStateView
            {
                MatchId = state.MatchId,
                ProtocolVersion = state.ProtocolVersion,
                Mode = state.Mode,
                BoardId = state.BoardId,
                RuleSetId = state.RuleSetId,
                Phase = state.Phase,
                Sequence = state.Sequence,
                IsYourTurn = state.Phase == MatchPhase.InProgress && state.CurrentTurn == mine,
                YouAreFirstMover = state.FirstMover == mine,
                PhaseDeadlineUnixMs = state.PhaseDeadlineUnixMs,
                ServerTimeUnixMs = nowUnixMs,
                YourConsecutiveTimeouts = mine == PlayerSlot.A ? state.ConsecutiveTimeoutsA : state.ConsecutiveTimeoutsB,
                // Capped by the water actually left, so the client is never told to submit a volley
                // the server would then reject.
                ShotsThisTurn = ShotRules.ShotsForTurn(in ownBoard, in opponentBoard, board, flags),
                You = ProjectOwnBoard(in ownBoard),

                // The ONLY fact about the opponent's board that crosses the wire before the end.
                OpponentReady = opponentBoard.HasPlacement,
                OpponentIsAi = theirs == PlayerSlot.B && state.AiDifficulty.HasValue,
                EndReason = state.EndReason
            };

            // Tracking data is what YOU learned by shooting, so it is derived from the opponent
            // board's incoming shots and disclosed hits - never from its occupancy.
            view.Opponent = ProjectTrackingBoard(in opponentBoard, flags);

            if (finished)
            {
                view.Outcome = state.Winner == null
                    ? MatchOutcome.Cancelled
                    : (state.Winner.Value == mine ? MatchOutcome.Won : MatchOutcome.Lost);

                view.Reveal = new RevealedFleetsView
                {
                    Yours = ToPlacements(in ownBoard),
                    Opponent = ToPlacements(in opponentBoard)
                };
            }

            return view;
        }

        private static OwnBoardView ProjectOwnBoard(in PlayerBoard board)
        {
            ShipStatusView[] fleet = new ShipStatusView[board.Fleet.Length];

            for (int i = 0; i < board.Fleet.Length; i++)
            {
                ShipRecord ship = board.Fleet[i];
                fleet[i] = new ShipStatusView
                {
                    Class = ship.Class,
                    Length = ship.Length,
                    Bow = ship.Bow,
                    Orientation = ship.Orientation,
                    HitCount = ship.HitCount,
                    IsSunk = ship.IsSunk
                };
            }

            return new OwnBoardView
            {
                OccupancyBase64 = board.Occupancy.ToBase64(),
                HitsBase64 = board.Hits.ToBase64(),
                IncomingShotsBase64 = board.IncomingShots.ToBase64(),
                Fleet = fleet
            };
        }

        private static TrackingBoardView ProjectTrackingBoard(in PlayerBoard opponentBoard, RuleFlags flags)
        {
            List<SunkShipView> sunk = new List<SunkShipView>();

            for (int i = 0; i < opponentBoard.Fleet.Length; i++)
            {
                ShipRecord ship = opponentBoard.Fleet[i];
                if (!ship.IsSunk) continue;

                SunkShipView entry = new SunkShipView { ShotIndex = ship.SunkOnShotIndex };

                // Both fields are rule-gated. Omitting them from the payload, rather than hiding
                // them in the UI, is the whole point.
                if (flags.AnnounceSunkShipType) entry.Class = ship.Class;
                if (flags.RevealSunkCells) entry.Cells = ToCoordArray(ShotRules.CellsOf(in ship));

                sunk.Add(entry);
            }

            return new TrackingBoardView
            {
                ShotsBase64 = opponentBoard.IncomingShots.ToBase64(),

                // DisclosedHits, not Hits. Under Salvo's aggregate report the two differ, and using
                // Hits here would quietly hand the shooter the information the variant withholds.
                HitsBase64 = opponentBoard.DisclosedHits.ToBase64(),
                SunkShips = sunk.ToArray()
            };
        }

        private static ShipPlacement[] ToPlacements(in PlayerBoard board)
        {
            ShipPlacement[] placements = new ShipPlacement[board.Fleet.Length];
            for (int i = 0; i < board.Fleet.Length; i++)
            {
                placements[i] = board.Fleet[i].ToPlacement();
            }
            return placements;
        }

        private static Coord[] ToCoordArray(IReadOnlyList<Coord> cells)
        {
            Coord[] copy = new Coord[cells.Count];
            for (int i = 0; i < cells.Count; i++) copy[i] = cells[i];
            return copy;
        }

        private static PlayerBoard BoardOf(in MatchState state, PlayerSlot slot)
        {
            return slot == PlayerSlot.A ? state.BoardA : state.BoardB;
        }
    }
}
