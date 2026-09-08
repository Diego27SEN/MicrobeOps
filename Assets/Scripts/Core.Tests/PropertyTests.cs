#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// The eight invariants of design doc section 12.2, checked over hundreds of randomly played
    /// matches across every board and rule set.
    /// <para>
    /// Unlike the example-based fixtures, these do not care what the right answer is for a given
    /// position: they assert properties that must hold for every position the engine can reach.
    /// That is what catches the case nobody thought to write a test for.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class PropertyTests
    {
        private const int GamesPerCombination = 12;

        private static readonly string[] BoardIds = { BoardConfig.BlitzId, BoardConfig.ClassicId, BoardConfig.AdmiralId };
        private static readonly string[] RuleSetIds = { RuleFlags.ClassicId, RuleFlags.BlitzId, RuleFlags.SalvoId };

        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
        }

        /// <summary>Bookkeeping the driver keeps so the invariants have something to compare against.</summary>
        private sealed class Observations
        {
            public long PreviousSequence;
            public int ShotsAtA;
            public int ShotsAtB;
            public int MutationsApplied;
            public bool SawFinish;
        }

        [Test]
        public void RandomMatches_UpholdEveryInvariant()
        {
            int games = 0;

            foreach (string boardId in BoardIds)
            {
                foreach (string ruleSetId in RuleSetIds)
                {
                    for (int i = 0; i < GamesPerCombination; i++)
                    {
                        PlayRandomMatch(boardId, ruleSetId, (ulong)(9000 + (games * 31)));
                        games++;
                    }
                }
            }

            Assert.That(games, Is.EqualTo(BoardIds.Length * RuleSetIds.Length * GamesPerCombination));
        }

        private void PlayRandomMatch(string boardId, string ruleSetId, ulong seed)
        {
            BattleEngine engine = TestGame.Engine(boardId, ruleSetId, seed, _config);
            BoardConfig board = engine.Board;
            RuleFlags rules = engine.Flags;

            MatchState state = engine.CreateMatch("m" + seed, TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);
            Observations log = new Observations { PreviousSequence = state.Sequence };

            IRandom layoutRng = new SeededRandom(seed + 1);
            ApplyAndCheck(ref state, log, (ref MatchState s) => engine.ApplyPlacement(ref s, TestGame.PlayerA, PlacementRules.GenerateRandom(board, rules, layoutRng), TestGame.Epoch));
            ApplyAndCheck(ref state, log, (ref MatchState s) => engine.ApplyPlacement(ref s, TestGame.PlayerB, PlacementRules.GenerateRandom(board, rules, layoutRng), TestGame.Epoch));

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.InProgress));

            IRandom shotRng = new SeededRandom(seed + 2);
            long now = TestGame.Epoch;
            int guard = 0;

            while (state.Phase == MatchPhase.InProgress && guard++ < 1000)
            {
                now += 1000;

                PlayerSlot shooterSlot = state.CurrentTurn;
                string shooter = shooterSlot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
                PlayerSlot targetSlot = MatchState.Other(shooterSlot);

                PlayerBoard shooterBoard = shooterSlot == PlayerSlot.A ? state.BoardA : state.BoardB;
                PlayerBoard targetBoard = targetSlot == PlayerSlot.A ? state.BoardA : state.BoardB;

                int shots = ShotRules.ShotsForTurn(in shooterBoard, in targetBoard, board, rules);
                List<Coord> volley = PickUntriedCells(in targetBoard, board, shots, shotRng);

                Assert.That(volley.Count, Is.EqualTo(shots),
                    "the engine must never demand more shots than there is water left to aim at");

                // Invariant 6: A's knowledge of B must not change because B acted.
                MatchStateView opponentViewBefore = MatchProjection.Project(in state, TestGame.PlayerA, board, rules, now);

                if (targetSlot == PlayerSlot.A) log.ShotsAtA += shots;
                else log.ShotsAtB += shots;

                ApplyAndCheck(ref state, log, (ref MatchState s) => engine.ApplyShots(ref s, shooter, volley, s.Sequence, now));

                if (shooterSlot == PlayerSlot.B)
                {
                    MatchStateView after = MatchProjection.Project(in state, TestGame.PlayerA, board, rules, now);
                    AssertTrackingUnchanged(opponentViewBefore, after, "B acting must not change what A knows about B");
                }

                CheckInvariants(in state, board, rules, log);
            }

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished), "match did not conclude");
            Assert.That(log.SawFinish, Is.True);
        }

        // The delegate takes the state by ref on purpose: MatchState is a struct, so a by-value
        // parameter would quietly throw every mutation away and leave these tests asserting on a
        // match that never progressed.
        private delegate ApplyResult Mutation(ref MatchState state);

        private void ApplyAndCheck(ref MatchState state, Observations log, Mutation mutation)
        {
            long before = state.Sequence;

            ApplyResult result = mutation(ref state);

            Assert.That(result.Success, Is.True, result.Error);

            // Invariant 5: strictly increasing, exactly one step per accepted mutation.
            Assert.That(state.Sequence, Is.EqualTo(before + 1), "sequence must rise by exactly one per accepted mutation");
            Assert.That(state.Sequence, Is.GreaterThan(log.PreviousSequence));

            log.PreviousSequence = state.Sequence;
            log.MutationsApplied++;

            if (state.Phase == MatchPhase.Finished) log.SawFinish = true;
        }

        private void CheckInvariants(in MatchState state, BoardConfig board, RuleFlags rules, Observations log)
        {
            CheckBoardInvariants(in state.BoardA, board, log.ShotsAtA, "A");
            CheckBoardInvariants(in state.BoardB, board, log.ShotsAtB, "B");

            // Invariant 4: the match ends on exactly the shot that completes the last fleet cell.
            bool aWiped = state.BoardA.Occupancy.PopCount > 0 && state.BoardA.Hits.Contains(state.BoardA.Occupancy);
            bool bWiped = state.BoardB.Occupancy.PopCount > 0 && state.BoardB.Hits.Contains(state.BoardB.Occupancy);

            if (aWiped || bWiped)
            {
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished), "a fleet is gone but the match is still running");
                Assert.That(state.EndReason, Is.EqualTo(MatchEndReason.FleetDestroyed));
                Assert.That(state.Winner, Is.EqualTo(aWiped ? PlayerSlot.B : PlayerSlot.A));
            }
            else
            {
                Assert.That(state.Phase, Is.Not.EqualTo(MatchPhase.Finished), "the match ended while both fleets still had cells afloat");
            }

            // Invariant 8: saving and reloading must not change anything the engine reads.
            AssertSurvivesSerialization(in state, board, rules);
        }

        private static void CheckBoardInvariants(in PlayerBoard playerBoard, BoardConfig board, int shotsTaken, string label)
        {
            // Invariant 1: you cannot have more hits than there are ship cells.
            Assert.That(playerBoard.Hits.PopCount, Is.LessThanOrEqualTo(board.FleetCellCount), label + ": more hits than fleet cells");
            Assert.That(playerBoard.Occupancy.Contains(playerBoard.Hits), Is.True, label + ": a hit landed outside the fleet");

            // Invariant 3: no cell is ever shot twice, so the count of distinct cells equals the
            // number of shots taken.
            Assert.That(playerBoard.IncomingShots.PopCount, Is.EqualTo(shotsTaken), label + ": a cell was shot twice");

            // Disclosed knowledge is always a subset of the truth.
            Assert.That(playerBoard.Hits.Contains(playerBoard.DisclosedHits), Is.True, label + ": disclosed a hit that never happened");
            Assert.That(playerBoard.IncomingShots.Contains(playerBoard.Hits), Is.True, label + ": a hit on a cell nobody shot");

            // Invariant 2: sunk means every cell hit, and nothing else does.
            for (int i = 0; i < playerBoard.Fleet.Length; i++)
            {
                ShipRecord ship = playerBoard.Fleet[i];

                bool allCellsHit = true;
                for (int c = 0; c < ship.Length && allCellsHit; c++)
                {
                    if (!playerBoard.Hits.Get(ship.CellAt(c).ToIndex(board.Width))) allCellsHit = false;
                }

                Assert.That(ship.IsSunk, Is.EqualTo(allCellsHit),
                    label + ": ship " + i + " (" + ship.Class + ") reports sunk=" + ship.IsSunk + " but allCellsHit=" + allCellsHit);

                Assert.That(ship.HitCount, Is.LessThanOrEqualTo(ship.Length), label + ": ship " + i + " took more hits than it has cells");
                if (ship.IsSunk) Assert.That(ship.SunkOnShotIndex, Is.GreaterThanOrEqualTo(0), label + ": sunk ship without a shot index");
            }
        }

        /// <summary>
        /// Invariant 8: round-trips every bitboard through its wire encoding, exactly as a Cloud
        /// Save write and read would, and asserts the reloaded state is indistinguishable.
        /// </summary>
        private static void AssertSurvivesSerialization(in MatchState state, BoardConfig board, RuleFlags rules)
        {
            MatchState reloaded = RoundTrip(in state);

            Assert.That(reloaded.BoardA.Occupancy, Is.EqualTo(state.BoardA.Occupancy));
            Assert.That(reloaded.BoardA.Hits, Is.EqualTo(state.BoardA.Hits));
            Assert.That(reloaded.BoardA.DisclosedHits, Is.EqualTo(state.BoardA.DisclosedHits));
            Assert.That(reloaded.BoardA.IncomingShots, Is.EqualTo(state.BoardA.IncomingShots));
            Assert.That(reloaded.BoardB.Occupancy, Is.EqualTo(state.BoardB.Occupancy));
            Assert.That(reloaded.Sequence, Is.EqualTo(state.Sequence));

            // And the derived answers must match too, not just the bytes.
            Assert.That(ShotRules.IsFleetDestroyed(in reloaded.BoardA), Is.EqualTo(ShotRules.IsFleetDestroyed(in state.BoardA)));
            Assert.That(ShotRules.ShotsForTurn(in reloaded.BoardA, rules), Is.EqualTo(ShotRules.ShotsForTurn(in state.BoardA, rules)));

            MatchStateView original = MatchProjection.Project(in state, TestGame.PlayerA, board, rules, TestGame.Epoch);
            MatchStateView afterReload = MatchProjection.Project(in reloaded, TestGame.PlayerA, board, rules, TestGame.Epoch);

            Assert.That(afterReload.Opponent.ShotsBase64, Is.EqualTo(original.Opponent.ShotsBase64));
            Assert.That(afterReload.Opponent.HitsBase64, Is.EqualTo(original.Opponent.HitsBase64));
            Assert.That(afterReload.You.OccupancyBase64, Is.EqualTo(original.You.OccupancyBase64));
            Assert.That(board.CellCount, Is.GreaterThan(0));
        }

        private static MatchState RoundTrip(in MatchState state)
        {
            MatchState copy = state;
            copy.BoardA = RoundTrip(in state.BoardA);
            copy.BoardB = RoundTrip(in state.BoardB);
            return copy;
        }

        private static PlayerBoard RoundTrip(in PlayerBoard board)
        {
            PlayerBoard copy = board;
            copy.Occupancy = BitBoard.FromBase64(board.Occupancy.ToBase64());
            copy.Hits = BitBoard.FromBase64(board.Hits.ToBase64());
            copy.DisclosedHits = BitBoard.FromBase64(board.DisclosedHits.ToBase64());
            copy.IncomingShots = BitBoard.FromBase64(board.IncomingShots.ToBase64());

            ShipRecord[] fleet = new ShipRecord[board.Fleet.Length];
            Array.Copy(board.Fleet, fleet, board.Fleet.Length);
            copy.Fleet = fleet;

            return copy;
        }

        private static void AssertTrackingUnchanged(MatchStateView before, MatchStateView after, string because)
        {
            Assert.That(after.Opponent.ShotsBase64, Is.EqualTo(before.Opponent.ShotsBase64), because);
            Assert.That(after.Opponent.HitsBase64, Is.EqualTo(before.Opponent.HitsBase64), because);
            Assert.That(after.Opponent.SunkShips.Length, Is.EqualTo(before.Opponent.SunkShips.Length), because);
        }

        private static List<Coord> PickUntriedCells(in PlayerBoard target, BoardConfig board, int count, IRandom rng)
        {
            List<int> untried = new List<int>(board.CellCount);
            for (int i = 0; i < board.CellCount; i++)
            {
                if (!target.IncomingShots.Get(i)) untried.Add(i);
            }

            List<Coord> picked = new List<Coord>(count);
            for (int i = 0; i < count && untried.Count > 0; i++)
            {
                int at = rng.Next(untried.Count);
                picked.Add(board.FromIndex(untried[at]));
                untried.RemoveAt(at);
            }

            return picked;
        }

        // --- invariant 7, stated on its own because it is about the AI, not the engine -----------

        [Test]
        public void SameSeed_ProducesTheSameAiSequence_AcrossEveryBoardAndDifficulty()
        {
            foreach (string boardId in BoardIds)
            {
                BoardConfig board = _config.Board(boardId);
                RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

                foreach (AiDifficulty difficulty in new[] { AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard, AiDifficulty.Adaptive })
                {
                    List<Coord> first = RunAi(difficulty, board, rules, 20);
                    List<Coord> second = RunAi(difficulty, board, rules, 20);

                    Assert.That(second, Is.EqualTo(first), boardId + " / " + difficulty + " is not deterministic");
                }
            }
        }

        private List<Coord> RunAi(AiDifficulty difficulty, BoardConfig board, RuleFlags rules, int shots)
        {
            IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(board, rules, new SeededRandom(555));

            PlayerBoard target = PlayerBoard.CreateEmpty();
            ShipRecord[] fleet = new ShipRecord[layout.Count];
            for (int i = 0; i < layout.Count; i++)
            {
                fleet[i] = new ShipRecord
                {
                    Class = layout[i].Class,
                    Length = (byte)board.Fleet[i].Length,
                    Bow = layout[i].Bow,
                    Orientation = layout[i].Orientation,
                    HitCount = 0,
                    SunkOnShotIndex = -1
                };
            }
            target.Occupancy = PlacementRules.ToOccupancy(board, layout);
            target.Fleet = fleet;
            target.HasPlacement = true;

            IAiStrategy strategy = AiFactory.Create(difficulty, _config.Ai);
            IRandom rng = new SeededRandom(31337);
            List<Coord> trace = new List<Coord>(shots);

            for (int i = 0; i < shots && !ShotRules.IsFleetDestroyed(in target); i++)
            {
                AiMemory memory = AiMemory.FromDisclosedKnowledge(in target, board, rules);
                Coord cell = strategy.NextShot(in memory, board, rng);
                trace.Add(cell);

                ShotResolution resolution = ShotRules.Resolve(ref target, board, rules, cell, i);
                strategy.Observe(cell, resolution.Outcome, resolution.SunkClass);
            }

            return trace;
        }
    }
}
