#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// The paths the happy-path fixtures never reach: fallbacks, guards and misconfiguration.
    /// The design doc asks for 100 % coverage of rules and projection, and these are the lines
    /// that would otherwise be shipped untested precisely because they are hard to trigger.
    /// </summary>
    [TestFixture]
    public sealed class RulesEdgeCaseTests
    {
        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
        }

        /// <summary>A board so tight that random placement essentially cannot succeed.</summary>
        private static BoardConfig SaturatedBoard()
        {
            return new BoardConfig
            {
                Id = "saturated",
                Width = 5,
                Height = 5,
                Fleet = new List<ShipSpec>
                {
                    new ShipSpec(ShipClass.Carrier, 5),
                    new ShipSpec(ShipClass.Battleship, 5),
                    new ShipSpec(ShipClass.Cruiser, 5),
                    new ShipSpec(ShipClass.Submarine, 5),
                    new ShipSpec(ShipClass.Destroyer, 5)
                }
            };
        }

        [Test]
        public void GenerateRandom_WhenRandomPlacementCannotFit_FallsBackToADeterministicLayout()
        {
            // Twenty-five ship cells on twenty-five squares: the only solutions are the fully packed
            // ones, which random search will not stumble into. The fallback must still deliver a
            // legal fleet instead of looping or throwing.
            BoardConfig board = SaturatedBoard();
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

            IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(board, rules, new SeededRandom(1));

            Assert.That(PlacementRules.Validate(board, rules, layout).IsValid, Is.True);
            Assert.That(PlacementRules.ToOccupancy(board, layout).PopCount, Is.EqualTo(25));
        }

        [Test]
        public void GenerateRandom_IsStillDeterministicOnTheFallbackPath()
        {
            BoardConfig board = SaturatedBoard();
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

            Assert.That(
                PlacementRules.GenerateRandom(board, rules, new SeededRandom(9)),
                Is.EqualTo(PlacementRules.GenerateRandom(board, rules, new SeededRandom(9))));
        }

        [Test]
        public void GenerateRandom_OnABoardThatCannotHoldItsOwnFleet_ThrowsRatherThanLooping()
        {
            // A configuration error must surface as one, loudly, at the point of use. Silently
            // returning a partial fleet would corrupt every match created from that board.
            BoardConfig impossible = new BoardConfig
            {
                Id = "impossible",
                Width = 4,
                Height = 4,
                Fleet = new List<ShipSpec>
                {
                    new ShipSpec(ShipClass.Carrier, 4),
                    new ShipSpec(ShipClass.Battleship, 4),
                    new ShipSpec(ShipClass.Cruiser, 4),
                    new ShipSpec(ShipClass.Submarine, 4),
                    new ShipSpec(ShipClass.Destroyer, 4)
                }
            };

            Assert.That(
                () => PlacementRules.GenerateRandom(impossible, _config.RuleSet(RuleFlags.ClassicId), new SeededRandom(1)),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CellsOf_WhenTheShipRunsOffTheBoard_Throws()
        {
            Assert.That(
                () => PlacementRules.CellsOf(
                    _config.Board(BoardConfig.ClassicId),
                    new ShipPlacement(ShipClass.Carrier, new Coord(8, 0), Orientation.Horizontal),
                    5),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void PlacementRules_RejectNullArguments()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);
            List<ShipPlacement> empty = new List<ShipPlacement>();

            Assert.That(() => PlacementRules.Validate(null!, rules, empty), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => PlacementRules.Validate(board, null!, empty), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => PlacementRules.Validate(board, rules, null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => PlacementRules.GenerateRandom(board, rules, null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => PlacementRules.ToOccupancy(board, null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => PlacementRules.IsHumanLike(board, null!, empty), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void LengthOf_ReadsTheFleetSpec()
        {
            BoardConfig admiral = _config.Board(BoardConfig.AdmiralId);

            // The two battleships are exactly why length comes from the fleet spec and not the class.
            Assert.That(PlacementRules.LengthOf(admiral, 1), Is.EqualTo(5));
            Assert.That(PlacementRules.LengthOf(admiral, 2), Is.EqualTo(4));
            Assert.That(admiral.Fleet[1].Class, Is.EqualTo(admiral.Fleet[2].Class));
        }

        [Test]
        public void ShipSpec_WithANonPositiveLength_Throws()
        {
            Assert.That(() => new ShipSpec(ShipClass.Carrier, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // --- shot rules -------------------------------------------------------------------------

        [Test]
        public void UntriedCellCount_CountsTheWaterNobodyHasAimedAtYet()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            PlayerBoard target = PlayerBoard.CreateEmpty();

            Assert.That(ShotRules.UntriedCellCount(in target, board), Is.EqualTo(100));

            target.IncomingShots = BitBoard.FromIndices(new[] { 0, 1, 2 });
            Assert.That(ShotRules.UntriedCellCount(in target, board), Is.EqualTo(97));
        }

        [Test]
        public void ShotsForTurn_UnderSalvo_IsCappedByTheWaterLeft()
        {
            // This is the deadlock the property tests found: five ships afloat but only two cells
            // left to aim at. Demanding five would make every submission unsatisfiable.
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);

            PlayerBoard shooter = PlayerBoard.CreateEmpty();
            shooter.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Carrier, Length = 5, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Battleship, Length = 4, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Cruiser, Length = 3, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Submarine, Length = 3, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, SunkOnShotIndex = -1 }
            };

            PlayerBoard target = PlayerBoard.CreateEmpty();
            List<int> shotAt = new List<int>();
            for (int i = 0; i < 98; i++) shotAt.Add(i);
            target.IncomingShots = BitBoard.FromIndices(shotAt);

            Assert.That(ShotRules.ShotsForTurn(in shooter, salvo), Is.EqualTo(5));
            Assert.That(ShotRules.ShotsForTurn(in shooter, in target, board, salvo), Is.EqualTo(2));
        }

        [Test]
        public void ShotsForTurn_OnAFullyShotBoard_StillAsksForOne()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);

            PlayerBoard shooter = PlayerBoard.CreateEmpty();
            shooter.Fleet = new[] { new ShipRecord { Class = ShipClass.Cruiser, Length = 3, SunkOnShotIndex = -1 } };

            PlayerBoard target = PlayerBoard.CreateEmpty();
            List<int> all = new List<int>();
            for (int i = 0; i < board.CellCount; i++) all.Add(i);
            target.IncomingShots = BitBoard.FromIndices(all);

            Assert.That(ShotRules.ShotsForTurn(in shooter, in target, board, salvo), Is.EqualTo(1));
        }

        [Test]
        public void ShotsForTurn_WithAMisconfiguredFixedCount_FallsBackToOne()
        {
            RuleFlags broken = _config.RuleSet(RuleFlags.ClassicId);
            broken.FixedShotsPerTurn = 0;

            PlayerBoard board = PlayerBoard.CreateEmpty();
            Assert.That(ShotRules.ShotsForTurn(in board, broken), Is.EqualTo(1));
        }

        [Test]
        public void Resolve_WhenOccupancyAndFleetDisagree_ThrowsInsteadOfCorruptingTheMatch()
        {
            // Occupancy says there is a ship at A1 but no record claims that cell. That is a broken
            // board, and pretending otherwise would silently desynchronise client and server.
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

            PlayerBoard broken = PlayerBoard.CreateEmpty();
            broken.Occupancy = BitBoard.Empty.Set(0);
            broken.Fleet = Array.Empty<ShipRecord>();

            PlayerBoard local = broken;
            Assert.That(
                () => ShotRules.Resolve(ref local, board, rules, new Coord(0, 0), 0),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ShotRules_RejectNullArguments()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);
            PlayerBoard playerBoard = PlayerBoard.CreateEmpty();

            PlayerBoard local = playerBoard;
            Assert.That(() => ShotRules.Resolve(ref local, null!, rules, new Coord(0, 0), 0), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => ShotRules.ShotsForTurn(in playerBoard, null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => ShotRules.ToShooterResult(in playerBoard, board, null!, new Coord(0, 0), ShotResolution.Miss()), Throws.TypeOf<ArgumentNullException>());
        }

        // --- engine guards ----------------------------------------------------------------------

        [Test]
        public void BattleEngine_RejectsNullDependencies()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);
            TimerProfile timers = _config.Timers.RealTime;

            Assert.That(() => new BattleEngine(null!, rules, timers, _config.Ai, new SeededRandom(1)), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new BattleEngine(board, null!, timers, _config.Ai, new SeededRandom(1)), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new BattleEngine(board, rules, null!, _config.Ai, new SeededRandom(1)), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new BattleEngine(board, rules, timers, null!, new SeededRandom(1)), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new BattleEngine(board, rules, timers, _config.Ai, null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateMatch_WithoutAMatchIdOrPlayer_Throws()
        {
            BattleEngine engine = TestGame.Engine(config: _config);

            Assert.That(() => engine.CreateMatch("", "a", null, MatchMode.Casual, null, 0), Throws.TypeOf<ArgumentException>());
            Assert.That(() => engine.CreateMatch("m1", "", null, MatchMode.Casual, null, 0), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ApplyShots_WithANullVolley_Throws()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);

            MatchState local = state;
            Assert.That(() => engine.ApplyShots(ref local, TestGame.PlayerA, null!, 0, 0), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ApplyShots_RepeatingACellInsideTheSameVolley_IsRejected()
        {
            BattleEngine engine = TestGame.Engine(BoardConfig.ClassicId, RuleFlags.SalvoId, config: _config);
            MatchState state = TestGame.StartedMatch(engine);

            string shooter = state.CurrentTurn == PlayerSlot.A ? TestGame.PlayerA : TestGame.PlayerB;
            Coord repeated = new Coord(0, 0);
            Coord[] volley = { repeated, repeated, new Coord(1, 1), new Coord(2, 2), new Coord(3, 3) };

            ApplyResult result = engine.ApplyShots(ref state, shooter, volley, state.Sequence, TestGame.Epoch + 1000);

            Assert.That(result.Error, Is.EqualTo("ERR_CELL_ALREADY_TARGETED|" + repeated.ToA1()));
        }

        [Test]
        public void ApplyForfeit_ByAStranger_ReturnsMatchNotFound()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);

            Assert.That(engine.ApplyForfeit(ref state, "mallory", TestGame.Epoch).Error, Is.EqualTo(ErrorCodes.MatchNotFound));
        }

        [Test]
        public void ApplyForfeit_OnAFinishedMatch_ReturnsMatchFinished()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);
            engine.ApplyForfeit(ref state, TestGame.PlayerA, TestGame.Epoch);

            Assert.That(engine.ApplyForfeit(ref state, TestGame.PlayerB, TestGame.Epoch).Error, Is.EqualTo(ErrorCodes.MatchFinished));
        }

        [Test]
        public void ApplyJoin_OnAMatchThatAlreadyStarted_IsRejected()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);

            Assert.That(engine.ApplyJoin(ref state, "carol", TestGame.Epoch).Success, Is.False);
        }

        [Test]
        public void ResolveExpiredTurns_OnAMatchNobodyHasJoined_IsANoOp()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.Casual, null, TestGame.Epoch);

            engine.ResolveExpiredTurns(ref state, TestGame.Epoch + 10_000_000);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Created));
            Assert.That(state.Sequence, Is.EqualTo(0));
        }

        // --- projection guards --------------------------------------------------------------------

        [Test]
        public void Project_RejectsNullConfiguration()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);
            MatchState local = state;

            Assert.That(() => MatchProjection.Project(in local, TestGame.PlayerA, null!, engine.Flags, 0), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => MatchProjection.Project(in local, TestGame.PlayerA, engine.Board, null!, 0), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Project_OnACancelledMatch_ReportsCancelledRatherThanALoss()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = TestGame.StartedMatch(engine);

            state.Phase = MatchPhase.Finished;
            state.EndReason = MatchEndReason.Cancelled;
            state.Winner = null;

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, engine.Board, engine.Flags, TestGame.Epoch);

            Assert.That(view.Outcome, Is.EqualTo(MatchOutcome.Cancelled));
        }

        [Test]
        public void Project_AgainstTheAi_FlagsTheOpponentAsAi()
        {
            BattleEngine engine = TestGame.Engine(config: _config);
            MatchState state = engine.CreateMatch("m1", TestGame.PlayerA, null, MatchMode.VsAi, AiDifficulty.Hard, TestGame.Epoch);

            MatchStateView view = MatchProjection.Project(in state, TestGame.PlayerA, engine.Board, engine.Flags, TestGame.Epoch);

            Assert.That(view.OpponentIsAi, Is.True);
            Assert.That(view.OpponentReady, Is.True);
        }

        // --- config lookups -------------------------------------------------------------------------

        [Test]
        public void GameConfig_LookupsResolveKnownIdsAndRejectUnknownOnes()
        {
            Assert.That(_config.TryGetBoard(BoardConfig.ClassicId, out BoardConfig board), Is.True);
            Assert.That(board.Id, Is.EqualTo(BoardConfig.ClassicId));

            Assert.That(_config.TryGetRuleSet(RuleFlags.SalvoId, out RuleFlags rules), Is.True);
            Assert.That(rules.Id, Is.EqualTo(RuleFlags.SalvoId));

            Assert.That(_config.TryGetRuleSet("nope", out _), Is.False);
            Assert.That(() => _config.RuleSet("nope"), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void BoardConfig_KnowsWhichCellsItContains()
        {
            BoardConfig board = _config.Board(BoardConfig.ClassicId);

            Assert.That(board.Contains(new Coord(9, 9)), Is.True);
            Assert.That(board.Contains(new Coord(10, 0)), Is.False);
            Assert.That(board.ToIndex(new Coord(3, 2)), Is.EqualTo(23));
            Assert.That(board.FromIndex(23), Is.EqualTo(new Coord(3, 2)));
        }

        [Test]
        public void AiParams_ForAnUnconfiguredDifficulty_Throws()
        {
            AiParams empty = new AiParams();
            Assert.That(() => empty.For(AiDifficulty.Hard), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AiFactory_RejectsNullParameters()
        {
            Assert.That(() => AiFactory.Create(AiDifficulty.Easy, null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AiMemory_RejectsNullConfiguration()
        {
            PlayerBoard board = PlayerBoard.CreateEmpty();

            Assert.That(() => AiMemory.FromDisclosedKnowledge(in board, null!, _config.RuleSet(RuleFlags.ClassicId)), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => AiMemory.FromDisclosedKnowledge(in board, _config.Board(BoardConfig.ClassicId), null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void PlacementValidation_RequiresAnErrorCodeToBeInvalid()
        {
            Assert.That(() => PlacementValidation.Invalid(""), Throws.TypeOf<ArgumentException>());
            Assert.That(() => ApplyResult.Failed(""), Throws.TypeOf<ArgumentException>());
            Assert.That(() => ErrorCodes.Format(""), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ShipPlacement_CellAtRejectsNegativeOffsets()
        {
            ShipPlacement placement = new ShipPlacement(ShipClass.Carrier, new Coord(0, 0), Orientation.Horizontal);
            Assert.That(() => placement.CellAt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Coord_ToA1_OutsideTheNotationRange_Throws()
        {
            Assert.That(() => new Coord(200, 0).ToA1(), Throws.TypeOf<InvalidOperationException>());
            Assert.That(new Coord(200, 0).ToString(), Does.Contain("200"));
        }

        [Test]
        public void Coord_FromIndexRejectsANonPositiveWidth()
        {
            Assert.That(() => Coord.FromIndex(0, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
