#nullable enable

using System.Collections.Generic;
using Armada.Core;

namespace Armada.Core.Tests
{
    /// <summary>Shared fixtures. Everything here is deterministic: seeds in, same match out.</summary>
    internal static class TestGame
    {
        public const string PlayerA = "alice";
        public const string PlayerB = "bob";
        public const long Epoch = 1_700_000_000_000L;

        public static GameConfig Config()
        {
            return DefaultGameConfig.Create();
        }

        public static BattleEngine Engine(
            string boardId = BoardConfig.ClassicId,
            string ruleSetId = RuleFlags.ClassicId,
            ulong seed = 1234,
            GameConfig? config = null)
        {
            GameConfig cfg = config ?? Config();
            return new BattleEngine(
                cfg.Board(boardId),
                cfg.RuleSet(ruleSetId),
                cfg.Timers.RealTime,
                cfg.Ai,
                new SeededRandom(seed));
        }

        /// <summary>A match with both fleets deployed, sitting in <see cref="MatchPhase.InProgress"/>.</summary>
        public static MatchState StartedMatch(BattleEngine engine, ulong layoutSeed = 99, MatchMode mode = MatchMode.Casual)
        {
            MatchState state = engine.CreateMatch("m1", PlayerA, PlayerB, mode, null, Epoch);

            IRandom rng = new SeededRandom(layoutSeed);
            IReadOnlyList<ShipPlacement> layoutA = PlacementRules.GenerateRandom(engine.Board, engine.Flags, rng);
            IReadOnlyList<ShipPlacement> layoutB = PlacementRules.GenerateRandom(engine.Board, engine.Flags, rng);

            engine.ApplyPlacement(ref state, PlayerA, layoutA, Epoch);
            engine.ApplyPlacement(ref state, PlayerB, layoutB, Epoch);

            return state;
        }

        /// <summary>Every cell of a board's fleet, in row-major order. Test-only white-box access.</summary>
        public static List<Coord> OccupiedCells(in PlayerBoard board, BoardConfig cfg)
        {
            List<Coord> cells = new List<Coord>();
            IReadOnlyList<int> indices = board.Occupancy.ToIndices();
            for (int i = 0; i < indices.Count; i++) cells.Add(cfg.FromIndex(indices[i]));
            return cells;
        }

        /// <summary>A cell that holds no ship, for tests that need a guaranteed miss.</summary>
        public static Coord EmptyCell(in PlayerBoard board, BoardConfig cfg)
        {
            for (int i = 0; i < cfg.CellCount; i++)
            {
                if (!board.Occupancy.Get(i) && !board.IncomingShots.Get(i)) return cfg.FromIndex(i);
            }

            throw new System.InvalidOperationException("The board has no free water left.");
        }

        /// <summary>Drives the match to completion by having <paramref name="winner"/> shoot every enemy cell.</summary>
        public static void PlayToVictory(BattleEngine engine, ref MatchState state, string winner)
        {
            string loser = winner == PlayerA ? PlayerB : PlayerA;
            PlayerSlot loserSlot = state.SlotOf(loser)!.Value;

            PlayerBoard loserBoard = loserSlot == PlayerSlot.A ? state.BoardA : state.BoardB;
            List<Coord> targets = OccupiedCells(in loserBoard, engine.Board);
            int next = 0;

            long now = Epoch;
            int guard = 0;

            while (state.Phase != MatchPhase.Finished && guard++ < 500)
            {
                now += 1000;
                string shooter = state.CurrentTurn == state.SlotOf(winner)!.Value ? winner : loser;

                if (shooter == winner)
                {
                    engine.ApplyShots(ref state, winner, new[] { targets[next++] }, state.Sequence, now);
                }
                else
                {
                    PlayerBoard winnerBoard = state.SlotOf(winner)!.Value == PlayerSlot.A ? state.BoardA : state.BoardB;
                    engine.ApplyShots(ref state, loser, new[] { EmptyCell(in winnerBoard, engine.Board) }, state.Sequence, now);
                }
            }
        }
    }
}
