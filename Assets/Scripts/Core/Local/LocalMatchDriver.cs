#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Runs an offline match: against the AI, or two people passing one device.
    /// <para>
    /// It exists so the client stays a painter even when there is no server. Every rule still lives
    /// in <see cref="BattleEngine"/>; this only decides whose turn it is to be asked and, when that
    /// is the AI, produces its move. The Cloud Code module will drive online matches through the
    /// same engine in the same order, so a bug found here is a bug found there.
    /// </para>
    /// <para>
    /// Pure like the rest of Core: no clock, no ambient randomness, no I/O. The caller passes
    /// <c>nowUnixMs</c> and owns the <see cref="IRandom"/>.
    /// </para>
    /// </summary>
    public sealed class LocalMatchDriver
    {
        private readonly GameConfig _config;
        private readonly BattleEngine _engine;
        private readonly IRandom _rng;
        private readonly IAiStrategy? _ai;

        private MatchState _state;

        public LocalMatchDriver(
            GameConfig config,
            string matchId,
            string boardId,
            string ruleSetId,
            MatchMode mode,
            AiDifficulty? aiDifficulty,
            string playerA,
            string? playerB,
            IRandom rng,
            long nowUnixMs)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));

            if (mode != MatchMode.VsAi && mode != MatchMode.LocalTwoPlayer)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), "LocalMatchDriver only drives offline modes.");
            }

            if (mode == MatchMode.VsAi && !aiDifficulty.HasValue)
            {
                throw new ArgumentException("A vs-AI match needs a difficulty.", nameof(aiDifficulty));
            }

            if (mode == MatchMode.LocalTwoPlayer && string.IsNullOrEmpty(playerB))
            {
                throw new ArgumentException("A pass-and-play match needs a second player.", nameof(playerB));
            }

            Board = config.Board(boardId);
            Rules = config.RuleSet(ruleSetId);

            _engine = new BattleEngine(Board, Rules, config.Timers.RealTime, config.Ai, rng);
            _ai = aiDifficulty.HasValue ? AiFactory.Create(aiDifficulty.Value, config.Ai) : null;

            _state = _engine.CreateMatch(matchId, playerA, playerB, mode, aiDifficulty, nowUnixMs);
        }

        public BoardConfig Board { get; }

        public RuleFlags Rules { get; }

        public MatchPhase Phase
        {
            get { return _state.Phase; }
        }

        public bool IsFinished
        {
            get { return _state.Phase == MatchPhase.Finished; }
        }

        /// <summary>Who the engine is waiting on right now.</summary>
        public string CurrentPlayerId
        {
            get { return _state.CurrentTurn == PlayerSlot.A ? _state.PlayerA : _state.PlayerB!; }
        }

        public bool IsAiTurn
        {
            get { return _ai != null && _state.Phase == MatchPhase.InProgress && CurrentPlayerId == MatchState.AiPlayerId; }
        }

        /// <summary>
        /// The state as one player is allowed to see it. Offline play goes through the projection
        /// too, deliberately: the local UI is built against exactly the DTO the server will send,
        /// so nothing has to be rewritten when the online modes arrive.
        /// </summary>
        public MatchStateView ViewFor(string playerId, long nowUnixMs)
        {
            return MatchProjection.Project(in _state, playerId, Board, Rules, nowUnixMs);
        }

        /// <summary>Read-only access for tests and for the summary screen. Not for the UI to reason with.</summary>
        public MatchState Snapshot()
        {
            return _state;
        }

        /// <summary>A valid random layout, for the player's "Random" button.</summary>
        public IReadOnlyList<ShipPlacement> SuggestPlacement()
        {
            return PlacementRules.GenerateRandom(Board, Rules, _rng);
        }

        public PlacementValidation ValidatePlacement(IReadOnlyList<ShipPlacement> placements)
        {
            return PlacementRules.Validate(Board, Rules, placements);
        }

        public ApplyResult SubmitPlacement(string playerId, IReadOnlyList<ShipPlacement> placements, long nowUnixMs)
        {
            return _engine.ApplyPlacement(ref _state, playerId, placements, nowUnixMs);
        }

        public ApplyResult Fire(string playerId, IReadOnlyList<Coord> cells, long nowUnixMs)
        {
            return _engine.ApplyShots(ref _state, playerId, cells, _state.Sequence, nowUnixMs);
        }

        public ApplyResult Forfeit(string playerId, long nowUnixMs)
        {
            return _engine.ApplyForfeit(ref _state, playerId, nowUnixMs);
        }

        /// <summary>
        /// How many cells the current player must declare. Offline there is no round trip to ask,
        /// so the UI reads it from here before enabling the fire button.
        /// </summary>
        public int ShotsRequiredThisTurn()
        {
            PlayerSlot shooter = _state.CurrentTurn;
            PlayerBoard shooterBoard = shooter == PlayerSlot.A ? _state.BoardA : _state.BoardB;
            PlayerBoard targetBoard = shooter == PlayerSlot.A ? _state.BoardB : _state.BoardA;

            return ShotRules.ShotsForTurn(in shooterBoard, in targetBoard, Board, Rules);
        }

        /// <summary>
        /// Plays the AI's whole turn and returns what it did, so the UI can animate the shots.
        /// Returns null when it is not the AI's turn - callers are expected to poll this rather
        /// than track turn ownership themselves.
        /// </summary>
        public ApplyResult? StepAi(long nowUnixMs)
        {
            if (!IsAiTurn) return null;

            PlayerSlot aiSlot = _state.CurrentTurn;
            PlayerSlot targetSlot = MatchState.Other(aiSlot);

            int shots = ShotsRequiredThisTurn();
            List<Coord> volley = new List<Coord>(shots);
            BitBoard planned = BitBoard.Empty;

            for (int i = 0; i < shots; i++)
            {
                PlayerBoard target = targetSlot == PlayerSlot.A ? _state.BoardA : _state.BoardB;
                AiMemory memory = AiMemory.FromDisclosedKnowledge(in target, Board, Rules);

                // Cells already picked earlier in this same volley are not in the board's incoming
                // shots yet, so they have to be masked out by hand or Salvo would fire twice at one
                // square and the engine would reject the whole turn.
                memory.Shots = memory.Shots.Or(planned);

                Coord cell = _ai!.NextShot(in memory, Board, _rng);
                volley.Add(cell);
                planned = planned.Set(cell.ToIndex(Board.Width));
            }

            ApplyResult result = _engine.ApplyShots(ref _state, MatchState.AiPlayerId, volley, _state.Sequence, nowUnixMs);

            if (result.Success)
            {
                for (int i = 0; i < result.Shots.Length; i++)
                {
                    _ai!.Observe(result.Shots[i].Cell, result.Shots[i].Outcome, result.Shots[i].SunkClass);
                }
            }

            return result;
        }

        /// <summary>
        /// Deploys the AI side if it has not been deployed yet. Normally the engine does this at
        /// creation; this covers a driver restored from a saved offline match.
        /// </summary>
        public void EnsureAiDeployed(long nowUnixMs)
        {
            if (_ai == null || _state.BoardB.HasPlacement) return;

            IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateHumanLike(Board, Rules, _config.Ai, _rng);
            _engine.ApplyPlacement(ref _state, MatchState.AiPlayerId, layout, nowUnixMs);
        }
    }
}
