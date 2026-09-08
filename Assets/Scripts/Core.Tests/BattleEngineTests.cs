#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class BattleEngineTests
    {
        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
        }

        private static IReadOnlyList<ShipPlacement> Layout(BattleEngine engine, ulong seed)
        {
            return PlacementRules.GenerateRandom(engine.Board, engine.Flags, new SeededRandom(seed));
        }

        // --- creation ------------------------------------------------------------------------

        [Test]
        public void CreateMatch_WithoutAnOpponent_WaitsInCreated()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.Casual, null, TestGame.Epoch);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Created));
            Assert.That(state.Sequence, Is.EqualTo(0));
            Assert.That(state.ProtocolVersion, Is.EqualTo(GameProtocol.Version));
            Assert.That(state.PlayerB, Is.Null);
        }

        [Test]
        public void CreateMatch_WithBothPlayers_OpensPlacementWithADeadline()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(state.PhaseDeadlineUnixMs, Is.EqualTo(TestGame.Epoch + 90_000));
        }

        [Test]
        public void CreateMatch_AgainstAi_DeploysTheAiFleetImmediately()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.VsAi, AiDifficulty.Hard, TestGame.Epoch);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(state.BoardB.HasPlacement, Is.True);
            Assert.That(state.BoardA.HasPlacement, Is.False);
            Assert.That(state.BoardB.Occupancy.PopCount, Is.EqualTo(engine.Board.FleetCellCount));
            Assert.That(state.IsAiMatch, Is.True);
        }

        [Test]
        public void CreateMatch_WithTheSameSeed_PicksTheSameFirstMover()
        {
            MatchState a = TestGame.Engine(seed: 77).CreateMatch("m1", "a", "b", MatchMode.Casual, null, TestGame.Epoch);
            MatchState b = TestGame.Engine(seed: 77).CreateMatch("m1", "a", "b", MatchMode.Casual, null, TestGame.Epoch);

            Assert.That(a.FirstMover, Is.EqualTo(b.FirstMover));
            Assert.That(a.CurrentTurn, Is.EqualTo(a.FirstMover));
        }

        [Test]
        public void ApplyJoin_AttachesTheOpponentAndOpensPlacement()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.Casual, null, TestGame.Epoch);

            ApplyResult result = engine.ApplyJoin(ref state, TestGame.PlayerB, TestGame.Epoch + 5000);

            Assert.That(result.Success, Is.True);
            Assert.That(state.PlayerB, Is.EqualTo(TestGame.PlayerB));
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(state.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void ApplyJoin_ByTheCreator_IsRejected()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.Casual, null, TestGame.Epoch);

            Assert.That(engine.ApplyJoin(ref state, TestGame.PlayerA, TestGame.Epoch).Success, Is.False);
        }

        // --- placement -----------------------------------------------------------------------

        [Test]
        public void ApplyPlacement_WithOnlyOneSideReady_StaysInPlacement()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            ApplyResult result = engine.ApplyPlacement(ref state, TestGame.PlayerA, Layout(engine, 1), TestGame.Epoch);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(state.BoardA.HasPlacement, Is.True);
        }

        [Test]
        public void ApplyPlacement_WhenBothSidesAreReady_StartsTheMatch()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            engine.ApplyPlacement(ref state, TestGame.PlayerA, Layout(engine, 1), TestGame.Epoch);
            engine.ApplyPlacement(ref state, TestGame.PlayerB, Layout(engine, 2), TestGame.Epoch + 1000);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.InProgress));
            Assert.That(state.CurrentTurn, Is.EqualTo(state.FirstMover));
            Assert.That(state.PhaseDeadlineUnixMs, Is.EqualTo(TestGame.Epoch + 1000 + 30_000));
        }

        [Test]
        public void ApplyPlacement_Twice_IsRejected()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            engine.ApplyPlacement(ref state, TestGame.PlayerA, Layout(engine, 1), TestGame.Epoch);
            ApplyResult second = engine.ApplyPlacement(ref state, TestGame.PlayerA, Layout(engine, 3), TestGame.Epoch);

            Assert.That(second.Success, Is.False);
            Assert.That(ErrorCodes.CodeOf(second.Error!), Is.EqualTo(ErrorCodes.WrongPhase));
        }

        [Test]
        public void ApplyPlacement_WithAnInvalidFleet_LeavesTheStateUntouched()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);
            long sequenceBefore = state.Sequence;

            ApplyResult result = engine.ApplyPlacement(ref state, TestGame.PlayerA, new List<ShipPlacement>(), TestGame.Epoch);

            Assert.That(result.Success, Is.False);
            Assert.That(state.Sequence, Is.EqualTo(sequenceBefore));
            Assert.That(state.BoardA.HasPlacement, Is.False);
        }

        [Test]
        public void ApplyPlacement_ByAStranger_ReturnsMatchNotFound()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);

            ApplyResult result = engine.ApplyPlacement(ref state, "mallory", Layout(engine, 1), TestGame.Epoch);

            Assert.That(result.Error, Is.EqualTo(ErrorCodes.MatchNotFound));
        }

        // --- shooting ------------------------------------------------------------------------

        [Test]
        public void ApplyShots_OnAMiss_PassesTheTurn()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string shooter = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            PlayerBoard target = state.CurrentTurn == PlayerSlot.A ? state.BoardB : state.BoardA;

            ApplyResult result = engine.ApplyShots(
                ref state, shooter, new[] { TestGame.EmptyCell(in target, engine.Board) }, state.Sequence, TestGame.Epoch + 2000);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Shots.Length, Is.EqualTo(1));
            Assert.That(result.Shots[0].Outcome, Is.EqualTo(ShotOutcome.Miss));
            Assert.That(state.CurrentTurn, Is.Not.EqualTo(state.FirstMover));
        }

        [Test]
        public void ApplyShots_WithAStaleSequence_IsRejectedAndChangesNothing()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string shooter = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            PlayerBoard target = state.CurrentTurn == PlayerSlot.A ? state.BoardB : state.BoardA;
            Coord cell = TestGame.EmptyCell(in target, engine.Board);

            long sequenceBefore = state.Sequence;
            ApplyResult result = engine.ApplyShots(ref state, shooter, new[] { cell }, sequenceBefore - 1, TestGame.Epoch + 2000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("ERR_STALE_ACTION|" + sequenceBefore));
            Assert.That(state.Sequence, Is.EqualTo(sequenceBefore));
        }

        [Test]
        public void ApplyShots_OutOfTurn_ReturnsNotYourTurn()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string waiting = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerB : TestGame.PlayerA;
            PlayerBoard target = state.CurrentTurn == PlayerSlot.A ? state.BoardA : state.BoardB;

            ApplyResult result = engine.ApplyShots(
                ref state, waiting, new[] { TestGame.EmptyCell(in target, engine.Board) }, state.Sequence, TestGame.Epoch + 2000);

            Assert.That(result.Error, Is.EqualTo(ErrorCodes.NotYourTurn));
        }

        [Test]
        public void FireShot_OnAlreadyTargetedCell_ReturnsError()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string first = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            string second = first == TestGame.PlayerA ? TestGame.PlayerB : TestGame.PlayerA;

            PlayerBoard target = state.CurrentTurn == PlayerSlot.A ? state.BoardB : state.BoardA;
            Coord cell = TestGame.EmptyCell(in target, engine.Board);

            engine.ApplyShots(ref state, first, new[] { cell }, state.Sequence, TestGame.Epoch + 1000);

            PlayerBoard otherBoard = state.CurrentTurn == PlayerSlot.A ? state.BoardB : state.BoardA;
            engine.ApplyShots(ref state, second, new[] { TestGame.EmptyCell(in otherBoard, engine.Board) }, state.Sequence, TestGame.Epoch + 2000);

            ApplyResult result = engine.ApplyShots(ref state, first, new[] { cell }, state.Sequence, TestGame.Epoch + 3000);

            Assert.That(result.Error, Is.EqualTo("ERR_CELL_ALREADY_TARGETED|" + cell.ToA1()));
        }

        [Test]
        public void ApplyShots_OutsideTheBoard_ReturnsOutOfBounds()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string shooter = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;

            ApplyResult result = engine.ApplyShots(ref state, shooter, new[] { new Coord(10, 0) }, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(ErrorCodes.CodeOf(result.Error!), Is.EqualTo(ErrorCodes.OutOfBounds));
        }

        [Test]
        public void ApplyShots_WithTheWrongCount_ReturnsShotCountInvalid()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string shooter = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;

            ApplyResult result = engine.ApplyShots(
                ref state, shooter, new[] { new Coord(0, 0), new Coord(1, 1) }, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(result.Error, Is.EqualTo("ERR_SHOT_COUNT_INVALID|1|2"));
        }

        [Test]
        public void ApplyShots_UnderBlitzRules_KeepsTheTurnAfterAHit()
        {
            BattleEngine engine = TestGame.Engine(BoardConfig.BlitzId, RuleFlags.BlitzId);
            MatchState state = TestGame.StartedMatch(engine);

            PlayerSlot shooterSlot = state.CurrentTurn;
            string shooter = shooterSlot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            PlayerBoard target = shooterSlot == PlayerSlot.A ? state.BoardB : state.BoardA;
            Coord shipCell = TestGame.OccupiedCells(in target, engine.Board)[0];

            engine.ApplyShots(ref state, shooter, new[] { shipCell }, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(state.CurrentTurn, Is.EqualTo(shooterSlot), "a hit under blitz grants another shot");
        }

        [Test]
        public void ApplyShots_UnderSalvo_ReturnsAnAggregateReportInsteadOfPerCellResults()
        {
            BattleEngine engine = TestGame.Engine(BoardConfig.ClassicId, RuleFlags.SalvoId);
            MatchState state = TestGame.StartedMatch(engine);

            PlayerSlot shooterSlot = state.CurrentTurn;
            string shooter = shooterSlot == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            PlayerBoard target = shooterSlot == PlayerSlot.A ? state.BoardB : state.BoardA;

            List<Coord> shipCells = TestGame.OccupiedCells(in target, engine.Board);
            Coord water = TestGame.EmptyCell(in target, engine.Board);

            // Five shots: the classic fleet is five ships, all afloat.
            Coord[] volley = { shipCells[0], shipCells[1], water, new Coord(9, 9), new Coord(9, 8) };
            ApplyResult result = engine.ApplyShots(ref state, shooter, volley, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Shots, Is.Empty, "per-cell results would defeat the aggregate report");
            Assert.That(result.Volley, Is.Not.Null);
            Assert.That(result.Volley!.ShotsFired, Is.EqualTo(5));
            Assert.That(result.Volley.Hits + result.Volley.Misses, Is.EqualTo(5));
            Assert.That(result.Volley.Hits, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ApplyShots_SinkingTheLastShip_FinishesTheMatch()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string winner = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;

            TestGame.PlayToVictory(engine, ref state, winner);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(state.EndReason, Is.EqualTo(MatchEndReason.FleetDestroyed));
            Assert.That(state.Winner, Is.EqualTo(state.SlotOf(winner)));
            Assert.That(state.PhaseDeadlineUnixMs, Is.EqualTo(0));
        }

        [Test]
        public void ApplyShots_OnAFinishedMatch_ReturnsMatchFinished()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            string winner = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            TestGame.PlayToVictory(engine, ref state, winner);

            ApplyResult result = engine.ApplyShots(ref state, winner, new[] { new Coord(0, 0) }, state.Sequence, TestGame.Epoch + 99_000);

            Assert.That(result.Error, Is.EqualTo(ErrorCodes.MatchFinished));
        }

        // --- forfeit and expiry ---------------------------------------------------------------

        [Test]
        public void ApplyForfeit_HandsTheWinToTheOtherPlayer()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);

            ApplyResult result = engine.ApplyForfeit(ref state, TestGame.PlayerA, TestGame.Epoch + 5000);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(state.EndReason, Is.EqualTo(MatchEndReason.Forfeit));
            Assert.That(state.Winner, Is.EqualTo(PlayerSlot.B));
        }

        [Test]
        public void ResolveExpiredTurns_BeforeTheDeadline_ChangesNothing()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            long sequenceBefore = state.Sequence;

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 1000);

            Assert.That(state.Sequence, Is.EqualTo(sequenceBefore));
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.InProgress));
        }

        [Test]
        public void ResolveExpiredTurns_OnPlacementTimeout_AutoDeploysWhoeverIsMissing()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, TestGame.PlayerB, MatchMode.Casual, null, TestGame.Epoch);
            engine.ApplyPlacement(ref state, TestGame.PlayerA, Layout(engine, 1), TestGame.Epoch);

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 91_000);

            Assert.That(state.BoardB.HasPlacement, Is.True);
            Assert.That(state.BoardB.Occupancy.PopCount, Is.EqualTo(engine.Board.FleetCellCount));
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.InProgress));
        }

        [Test]
        public void ResolveExpiredTurns_OnATurnTimeout_FiresAnAutomaticShotAndAddsAStrike()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);

            PlayerSlot idle = state.CurrentTurn;
            PlayerBoard targetBefore = idle == PlayerSlot.A ? state.BoardB : state.BoardA;
            int shotsBefore = targetBefore.IncomingShots.PopCount;

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 31_000);

            PlayerBoard targetAfter = idle == PlayerSlot.A ? state.BoardB : state.BoardA;
            int strikes = idle == PlayerSlot.A ? state.ConsecutiveTimeoutsA : state.ConsecutiveTimeoutsB;

            Assert.That(targetAfter.IncomingShots.PopCount, Is.EqualTo(shotsBefore + 1));
            Assert.That(strikes, Is.EqualTo(1));
            Assert.That(state.CurrentTurn, Is.Not.EqualTo(idle));
        }

        [Test]
        public void ResolveExpiredTurns_AfterThreeStrikes_EndsTheMatchAgainstTheIdlePlayer()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            PlayerSlot idle = state.CurrentTurn;

            // Strikes alternate, because each timeout also passes the turn: the first mover is
            // struck at 30 s, 90 s and 150 s. So five 30 s turns are needed to reach three strikes,
            // and 155 s still sits inside the 180 s abandon window, which isolates the strike path.
            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 155_000);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(state.EndReason, Is.EqualTo(MatchEndReason.TimeoutStrikes));
            Assert.That(state.Winner, Is.EqualTo(MatchState.Other(idle)));
        }

        [Test]
        public void ResolveExpiredTurns_AfterTheAbandonWindow_ClosesTheMatch()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            PlayerSlot idle = state.CurrentTurn;

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 10_000_000);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(state.Winner, Is.EqualTo(MatchState.Other(idle)));
        }

        [Test]
        public void ResolveExpiredTurns_OnAFinishedMatch_IsANoOp()
        {
            BattleEngine engine = TestGame.Engine();
            MatchState state = TestGame.StartedMatch(engine);
            engine.ApplyForfeit(ref state, TestGame.PlayerA, TestGame.Epoch + 1000);
            long sequenceBefore = state.Sequence;

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 10_000_000);

            Assert.That(state.Sequence, Is.EqualTo(sequenceBefore));
        }

        [Test]
        public void ResolveExpiredTurns_DoesNotLetTimeoutsKeepAnIdleMatchAliveForever()
        {
            // The idle clock must not be refreshed by the engine's own auto-shots: a player who
            // walks away has to lose eventually, one way or another.
            BattleEngine engine = TestGame.Engine(BoardConfig.ClassicId, RuleFlags.ClassicId, 5);
            MatchState state = TestGame.StartedMatch(engine);

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 3_600_000);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Finished));
        }
    }
}
