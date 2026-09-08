#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Owns every transition of a <see cref="MatchState"/>. The same instance runs inside Cloud
    /// Code and, for offline modes, inside the client: both compile this very file.
    /// <para>
    /// Time is never read from the ambient clock: every mutation takes <c>nowUnixMs</c> from the
    /// caller. Randomness only ever comes from the injected <see cref="IRandom"/>. Those two rules
    /// are what make the engine testable and deterministic, and they are enforced by an
    /// architecture test.
    /// </para>
    /// <para>
    /// Every method either applies its whole mutation and bumps <c>Sequence</c> by exactly one, or
    /// fails and leaves the state byte-identical. There is no partial application: that is what
    /// makes conditional writes to Cloud Save safe to retry.
    /// </para>
    /// </summary>
    public sealed class BattleEngine
    {
        /// <summary>
        /// Ceiling on how many expiries one call may resolve. A match left untouched for a month
        /// must not turn into an unbounded loop inside a Cloud Code invocation.
        /// </summary>
        private const int MaxExpiryIterations = 64;

        private readonly BoardConfig _board;
        private readonly RuleFlags _flags;
        private readonly TimerProfile _timers;
        private readonly AiParams _aiParams;
        private readonly IRandom _rng;

        public BattleEngine(BoardConfig board, RuleFlags flags, TimerProfile timers, AiParams aiParams, IRandom rng)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _timers = timers ?? throw new ArgumentNullException(nameof(timers));
            _aiParams = aiParams ?? throw new ArgumentNullException(nameof(aiParams));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public BoardConfig Board
        {
            get { return _board; }
        }

        public RuleFlags Flags
        {
            get { return _flags; }
        }

        public TimerProfile Timers
        {
            get { return _timers; }
        }

        /// <summary>
        /// Creates a match. When the opponent is already known - a human passed in
        /// <paramref name="playerB"/>, or an AI difficulty - it opens straight into
        /// <see cref="MatchPhase.Placement"/>; otherwise it waits in <see cref="MatchPhase.Created"/>
        /// for someone to join. The first mover is drawn from the injected RNG and recorded.
        /// <para>
        /// <paramref name="matchId"/> is a parameter because Core may not call
        /// <c>Guid.NewGuid</c>: identifiers come from the caller, like time does.
        /// </para>
        /// </summary>
        public MatchState CreateMatch(string matchId, string playerA, string? playerB, MatchMode mode, AiDifficulty? aiDifficulty, long nowUnixMs)
        {
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("Match id is required.", nameof(matchId));
            if (string.IsNullOrEmpty(playerA)) throw new ArgumentException("Player A is required.", nameof(playerA));

            MatchState state = new MatchState
            {
                MatchId = matchId,
                ProtocolVersion = GameProtocol.Version,
                Mode = mode,
                BoardId = _board.Id,
                RuleSetId = _flags.Id,
                Phase = MatchPhase.Created,
                Sequence = 0,
                PlayerA = playerA,

                // The AI gets a reserved identity so it moves through exactly the same engine path
                // a person does.
                PlayerB = aiDifficulty.HasValue ? MatchState.AiPlayerId : playerB,
                AiDifficulty = aiDifficulty,
                IsBotFilled = false,
                PrivateCode = null,
                FirstMover = _rng.Next(2) == 0 ? PlayerSlot.A : PlayerSlot.B,
                BoardA = PlayerBoard.CreateEmpty(),
                BoardB = PlayerBoard.CreateEmpty(),
                CreatedAtUnixMs = nowUnixMs,
                LastActionUnixMs = nowUnixMs,
                ShotIndex = 0
            };

            state.CurrentTurn = state.FirstMover;

            bool opponentKnown = playerB != null || aiDifficulty.HasValue;
            if (opponentKnown)
            {
                OpenPlacement(ref state, nowUnixMs);

                // The AI deploys immediately, using the human-like filter so it does not fall into
                // the border-hugging patterns players learn to sweep.
                if (aiDifficulty.HasValue)
                {
                    IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateHumanLike(_board, _flags, _aiParams, _rng);
                    ApplyPlacementToBoard(ref state.BoardB, layout);
                }
            }

            return state;
        }

        /// <summary>Attaches the second player and opens the placement phase.</summary>
        public ApplyResult ApplyJoin(ref MatchState state, string playerId, long nowUnixMs)
        {
            if (state.Phase != MatchPhase.Created)
            {
                return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.WrongPhase, MatchPhase.Created));
            }

            if (state.PlayerB != null) return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.WrongPhase, MatchPhase.Created));
            if (string.Equals(state.PlayerA, playerId, StringComparison.Ordinal)) return ApplyResult.Failed(ErrorCodes.MatchNotFound);

            state.PlayerB = playerId;
            OpenPlacement(ref state, nowUnixMs);
            Commit(ref state, nowUnixMs);
            return ApplyResult.Ok();
        }

        /// <summary>
        /// Records one player's fleet. The match moves to <see cref="MatchPhase.InProgress"/> only
        /// once both placements are in: the "both ready" barrier of design doc section 4.2.
        /// </summary>
        public ApplyResult ApplyPlacement(ref MatchState state, string playerId, IReadOnlyList<ShipPlacement> placements, long nowUnixMs)
        {
            if (state.Phase != MatchPhase.Placement)
            {
                return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.WrongPhase, MatchPhase.Placement));
            }

            PlayerSlot? slot = state.SlotOf(playerId);
            if (slot == null) return ApplyResult.Failed(ErrorCodes.MatchNotFound);

            ref PlayerBoard board = ref BoardOf(ref state, slot.Value);
            if (board.HasPlacement) return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.WrongPhase, MatchPhase.Placement));

            PlacementValidation validation = PlacementRules.Validate(_board, _flags, placements);
            if (!validation.IsValid) return ApplyResult.Failed(validation.Error!);

            ApplyPlacementToBoard(ref board, placements);
            MaybeStartMatch(ref state, nowUnixMs);
            Commit(ref state, nowUnixMs);
            return ApplyResult.Ok();
        }

        /// <summary>
        /// Resolves a turn's shots. One coordinate except in Salvo, where the count must equal the
        /// shooter's ships afloat. A stale <paramref name="expectedSequence"/> is rejected with
        /// <c>ERR_STALE_ACTION</c> and leaves the state untouched.
        /// </summary>
        public ApplyResult ApplyShots(ref MatchState state, string playerId, IReadOnlyList<Coord> cells, long expectedSequence, long nowUnixMs)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            if (state.Phase == MatchPhase.Finished) return ApplyResult.Failed(ErrorCodes.MatchFinished);
            if (state.Phase != MatchPhase.InProgress)
            {
                return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.WrongPhase, MatchPhase.InProgress));
            }

            PlayerSlot? slot = state.SlotOf(playerId);
            if (slot == null) return ApplyResult.Failed(ErrorCodes.MatchNotFound);

            // Sequence is checked before turn ownership so a duplicate submit reports the stale
            // action it actually is, instead of the confusing "not your turn" it would become
            // once the first submit already flipped the turn.
            if (state.Sequence != expectedSequence)
            {
                return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.StaleAction, state.Sequence));
            }

            if (state.CurrentTurn != slot.Value) return ApplyResult.Failed(ErrorCodes.NotYourTurn);

            PlayerSlot targetSlot = MatchState.Other(slot.Value);
            int expectedShots = ShotRules.ShotsForTurn(
                in BoardOf(ref state, slot.Value), in BoardOf(ref state, targetSlot), _board, _flags);

            if (cells.Count != expectedShots)
            {
                return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.ShotCountInvalid, expectedShots, cells.Count));
            }

            // Validate the whole volley before mutating anything, so a rejected shot cannot leave
            // half a turn applied.
            BitBoard alreadyTargeted = BoardOf(ref state, targetSlot).IncomingShots;
            BitBoard volley = BitBoard.Empty;

            for (int i = 0; i < cells.Count; i++)
            {
                Coord cell = cells[i];
                if (!_board.Contains(cell))
                {
                    return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.OutOfBounds, cell.ToA1()));
                }

                int index = cell.ToIndex(_board.Width);
                if (alreadyTargeted.Get(index) || volley.Get(index))
                {
                    return ApplyResult.Failed(ErrorCodes.Format(ErrorCodes.CellAlreadyTargeted, cell.ToA1()));
                }

                volley = volley.Set(index);
            }

            return ResolveVolley(ref state, slot.Value, targetSlot, cells, nowUnixMs);
        }

        public ApplyResult ApplyForfeit(ref MatchState state, string playerId, long nowUnixMs)
        {
            if (state.Phase == MatchPhase.Finished) return ApplyResult.Failed(ErrorCodes.MatchFinished);

            PlayerSlot? slot = state.SlotOf(playerId);
            if (slot == null) return ApplyResult.Failed(ErrorCodes.MatchNotFound);

            Finish(ref state, MatchState.Other(slot.Value), MatchEndReason.Forfeit, nowUnixMs);
            Commit(ref state, nowUnixMs);
            return ApplyResult.Ok();
        }

        /// <summary>
        /// Lazy expiry resolution: auto-deploys a missing placement, fires an automatic shot for a
        /// missed turn, applies strikes and closes abandoned matches. Called at the head of every
        /// Cloud Code entry point AND by the five-minute sweeper, on purpose: defence in depth.
        /// Returns a successful no-op when nothing had expired.
        /// </summary>
        public ApplyResult ResolveExpiredTurns(ref MatchState state, long nowUnixMs)
        {
            if (state.Phase == MatchPhase.Finished || state.Phase == MatchPhase.Created) return ApplyResult.Ok();

            for (int iteration = 0; iteration < MaxExpiryIterations; iteration++)
            {
                if (state.Phase == MatchPhase.Finished) break;

                if (_timers.AbandonSeconds > 0 && nowUnixMs - state.LastActionUnixMs >= _timers.AbandonSeconds * 1000L)
                {
                    ResolveAbandon(ref state, nowUnixMs);
                    break;
                }

                if (state.PhaseDeadlineUnixMs <= 0 || nowUnixMs < state.PhaseDeadlineUnixMs) break;

                if (state.Phase == MatchPhase.Placement) ResolvePlacementTimeout(ref state);
                else if (state.Phase == MatchPhase.InProgress) ResolveTurnTimeout(ref state);
                else break;

                // CommitSystem, not Commit: an engine-driven auto-shot is not a player action, so
                // it must not refresh the idle clock. Otherwise a player who walks away would keep
                // the match alive forever through their own timeouts.
                CommitSystem(ref state);
            }

            return ApplyResult.Ok();
        }

        // --- internals -----------------------------------------------------------------------

        private ApplyResult ResolveVolley(ref MatchState state, PlayerSlot shooter, PlayerSlot targetSlot, IReadOnlyList<Coord> cells, long nowUnixMs)
        {
            List<ShotResult> results = new List<ShotResult>(cells.Count);
            List<SunkShipReport> sunk = new List<SunkShipReport>();

            int hits = 0;
            bool anyHit = false;
            bool fleetDestroyed = false;

            for (int i = 0; i < cells.Count; i++)
            {
                ShotResolution resolution = ShotRules.Resolve(
                    ref BoardOf(ref state, targetSlot), _board, _flags, cells[i], state.ShotIndex);

                state.ShotIndex++;

                if (resolution.Outcome != ShotOutcome.Miss)
                {
                    hits++;
                    anyHit = true;
                }

                ShotResult shooterResult = ShotRules.ToShooterResult(
                    in BoardOf(ref state, targetSlot), _board, _flags, cells[i], resolution);

                results.Add(shooterResult);

                if (resolution.Outcome == ShotOutcome.Sunk || resolution.Outcome == ShotOutcome.Win)
                {
                    sunk.Add(new SunkShipReport { Class = shooterResult.SunkClass, Cells = shooterResult.SunkCells });
                }

                if (resolution.Outcome == ShotOutcome.Win) fleetDestroyed = true;
            }

            BoardOf(ref state, shooter).ShotsFired += cells.Count;
            ResetStrikes(ref state, shooter);

            if (fleetDestroyed)
            {
                Finish(ref state, shooter, MatchEndReason.FleetDestroyed, nowUnixMs);
            }
            else
            {
                bool keepTurn = _flags.ExtraTurnOnHit && anyHit;
                if (!keepTurn) state.CurrentTurn = targetSlot;
                state.PhaseDeadlineUnixMs = nowUnixMs + (_timers.TurnSeconds * 1000L);
            }

            Commit(ref state, nowUnixMs);

            if (!_flags.SalvoAggregateReport)
            {
                return ApplyResult.Ok(results.ToArray(), fleetDestroyed);
            }

            // Aggregate mode: the shooter learns counts and sinkings, never which cell did what.
            // Returning the per-cell array here would silently undo the whole Salvo variant.
            return ApplyResult.OkAggregated(
                new VolleySummary
                {
                    ShotsFired = cells.Count,
                    Hits = hits,
                    Misses = cells.Count - hits,
                    Sunk = sunk.ToArray()
                },
                fleetDestroyed);
        }

        private void ResolvePlacementTimeout(ref MatchState state)
        {
            // Whoever failed to deploy gets a plain random layout, not the human-like one: the
            // human-like filter exists to make the AI harder to read, not to reward going idle.
            if (!state.BoardA.HasPlacement)
            {
                ApplyPlacementToBoard(ref state.BoardA, PlacementRules.GenerateRandom(_board, _flags, _rng));
            }

            if (!state.BoardB.HasPlacement)
            {
                ApplyPlacementToBoard(ref state.BoardB, PlacementRules.GenerateRandom(_board, _flags, _rng));
            }

            state.Phase = MatchPhase.InProgress;
            state.CurrentTurn = state.FirstMover;
            state.PhaseDeadlineUnixMs = state.PhaseDeadlineUnixMs + (_timers.TurnSeconds * 1000L);
        }

        private void ResolveTurnTimeout(ref MatchState state)
        {
            PlayerSlot shooter = state.CurrentTurn;
            PlayerSlot targetSlot = MatchState.Other(shooter);

            int strikes = AddStrike(ref state, shooter);

            // The automatic shot uses Medium: Easy would hand the match over, Hard would reward
            // going away from the keyboard (design doc section 4.7).
            IAiStrategy strategy = AiFactory.Create(_aiParams.TimeoutShotDifficulty, _aiParams);
            int shots = ShotRules.ShotsForTurn(
                in BoardOf(ref state, shooter), in BoardOf(ref state, targetSlot), _board, _flags);

            bool fleetDestroyed = false;

            for (int i = 0; i < shots; i++)
            {
                AiMemory memory = AiMemory.FromDisclosedKnowledge(in BoardOf(ref state, targetSlot), _board, _flags);
                if (memory.UntriedCellCount(_board) == 0) break;

                Coord cell = strategy.NextShot(in memory, _board, _rng);
                ShotResolution resolution = ShotRules.Resolve(
                    ref BoardOf(ref state, targetSlot), _board, _flags, cell, state.ShotIndex);

                state.ShotIndex++;
                strategy.Observe(cell, resolution.Outcome, resolution.SunkClass);

                if (resolution.Outcome == ShotOutcome.Win) fleetDestroyed = true;
            }

            BoardOf(ref state, shooter).ShotsFired += shots;

            if (fleetDestroyed)
            {
                Finish(ref state, shooter, MatchEndReason.FleetDestroyed, state.PhaseDeadlineUnixMs);
                return;
            }

            if (strikes >= _timers.MaxConsecutiveTimeouts)
            {
                Finish(ref state, targetSlot, MatchEndReason.TimeoutStrikes, state.PhaseDeadlineUnixMs);
                return;
            }

            state.CurrentTurn = targetSlot;
            state.PhaseDeadlineUnixMs = state.PhaseDeadlineUnixMs + (_timers.TurnSeconds * 1000L);
        }

        private void ResolveAbandon(ref MatchState state, long nowUnixMs)
        {
            // Whoever is on the clock is the one who walked away.
            PlayerSlot idle = state.Phase == MatchPhase.InProgress
                ? state.CurrentTurn
                : (!state.BoardA.HasPlacement ? PlayerSlot.A : PlayerSlot.B);

            Finish(ref state, MatchState.Other(idle), MatchEndReason.Abandoned, nowUnixMs);
            CommitSystem(ref state);
        }

        private void OpenPlacement(ref MatchState state, long nowUnixMs)
        {
            state.Phase = MatchPhase.Placement;
            state.PhaseDeadlineUnixMs = nowUnixMs + (_timers.PlacementSeconds * 1000L);
        }

        private void MaybeStartMatch(ref MatchState state, long nowUnixMs)
        {
            if (!state.BoardA.HasPlacement || !state.BoardB.HasPlacement) return;

            state.Phase = MatchPhase.InProgress;
            state.CurrentTurn = state.FirstMover;
            state.PhaseDeadlineUnixMs = nowUnixMs + (_timers.TurnSeconds * 1000L);
        }

        private void ApplyPlacementToBoard(ref PlayerBoard board, IReadOnlyList<ShipPlacement> placements)
        {
            ShipRecord[] fleet = new ShipRecord[placements.Count];

            for (int i = 0; i < placements.Count; i++)
            {
                fleet[i] = new ShipRecord
                {
                    Class = placements[i].Class,
                    Length = (byte)_board.Fleet[i].Length,
                    Bow = placements[i].Bow,
                    Orientation = placements[i].Orientation,
                    HitCount = 0,
                    SunkOnShotIndex = -1
                };
            }

            board.Occupancy = PlacementRules.ToOccupancy(_board, placements);
            board.Fleet = fleet;
            board.HasPlacement = true;
        }

        private void Finish(ref MatchState state, PlayerSlot winner, MatchEndReason reason, long nowUnixMs)
        {
            state.Phase = MatchPhase.Finished;
            state.Winner = winner;
            state.EndReason = reason;
            state.FinishedAtUnixMs = nowUnixMs;
            state.PhaseDeadlineUnixMs = 0;
        }

        /// <summary>Records a player-driven mutation: bumps the sequence and the idle clock.</summary>
        private static void Commit(ref MatchState state, long nowUnixMs)
        {
            state.Sequence++;
            state.LastActionUnixMs = nowUnixMs;
        }

        /// <summary>Records an engine-driven mutation: bumps the sequence only.</summary>
        private static void CommitSystem(ref MatchState state)
        {
            state.Sequence++;
        }

        private static void ResetStrikes(ref MatchState state, PlayerSlot slot)
        {
            if (slot == PlayerSlot.A) state.ConsecutiveTimeoutsA = 0;
            else state.ConsecutiveTimeoutsB = 0;
        }

        private static int AddStrike(ref MatchState state, PlayerSlot slot)
        {
            if (slot == PlayerSlot.A) return ++state.ConsecutiveTimeoutsA;
            return ++state.ConsecutiveTimeoutsB;
        }

        private static ref PlayerBoard BoardOf(ref MatchState state, PlayerSlot slot)
        {
            if (slot == PlayerSlot.A) return ref state.BoardA;
            return ref state.BoardB;
        }
    }
}
