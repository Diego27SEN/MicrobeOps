#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class PlacementRulesTests
    {
        private GameConfig _config = null!;
        private BoardConfig _classic = null!;
        private RuleFlags _classicRules = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
            _classic = _config.Board(BoardConfig.ClassicId);
            _classicRules = _config.RuleSet(RuleFlags.ClassicId);
        }

        /// <summary>A hand-built valid classic fleet: five ships along the top rows, no touching.</summary>
        private static List<ShipPlacement> ValidClassicFleet()
        {
            return new List<ShipPlacement>
            {
                new ShipPlacement(ShipClass.Carrier, new Coord(0, 0), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Battleship, new Coord(0, 2), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Cruiser, new Coord(0, 4), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Submarine, new Coord(0, 6), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Destroyer, new Coord(0, 8), Orientation.Horizontal)
            };
        }

        [Test]
        public void Validate_OnWellFormedFleet_Succeeds()
        {
            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, ValidClassicFleet());

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public void Validate_OnWrongShipCount_ReturnsFleetMismatch()
        {
            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet.RemoveAt(4);

            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, fleet);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo("ERR_FLEET_MISMATCH|5|4"));
        }

        [Test]
        public void Validate_OnWrongShipOrder_ReturnsFleetMismatch()
        {
            // Fleet order is part of the contract: placement i is cfg.Fleet[i]. On admiral two
            // battleships have different lengths, so order is what disambiguates them.
            List<ShipPlacement> fleet = ValidClassicFleet();
            ShipPlacement first = fleet[0];
            fleet[0] = fleet[1];
            fleet[1] = first;

            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, fleet);

            Assert.That(result.IsValid, Is.False);
            Assert.That(ErrorCodes.CodeOf(result.Error!), Is.EqualTo(ErrorCodes.FleetMismatch));
        }

        [Test]
        public void Validate_OnShipRunningOffTheBoard_ReturnsOutOfBounds()
        {
            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[0] = new ShipPlacement(ShipClass.Carrier, new Coord(6, 0), Orientation.Horizontal);

            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, fleet);

            Assert.That(result.Error, Is.EqualTo("ERR_INVALID_PLACEMENT|oob"));
        }

        [Test]
        public void Validate_OnBowOutsideTheBoard_ReturnsOutOfBounds()
        {
            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[4] = new ShipPlacement(ShipClass.Destroyer, new Coord(0, 200), Orientation.Horizontal);

            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, fleet);

            Assert.That(result.Error, Is.EqualTo("ERR_INVALID_PLACEMENT|oob"));
        }

        [Test]
        public void Validate_OnOverlappingShips_ReturnsOverlap()
        {
            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[1] = new ShipPlacement(ShipClass.Battleship, new Coord(0, 0), Orientation.Vertical);

            PlacementValidation result = PlacementRules.Validate(_classic, _classicRules, fleet);

            Assert.That(result.Error, Is.EqualTo("ERR_INVALID_PLACEMENT|overlap"));
        }

        [Test]
        public void Validate_OnTouchingShips_IsAllowedByDefault()
        {
            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[1] = new ShipPlacement(ShipClass.Battleship, new Coord(0, 1), Orientation.Horizontal);

            Assert.That(PlacementRules.Validate(_classic, _classicRules, fleet).IsValid, Is.True);
        }

        [Test]
        public void Validate_OnTouchingShips_WhenAdjacencyIsForbidden_ReturnsAdjacency()
        {
            RuleFlags noTouching = _config.RuleSet(RuleFlags.ClassicId);
            noTouching.AllowAdjacentShips = false;

            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[1] = new ShipPlacement(ShipClass.Battleship, new Coord(0, 1), Orientation.Horizontal);

            PlacementValidation result = PlacementRules.Validate(_classic, noTouching, fleet);

            Assert.That(result.Error, Is.EqualTo("ERR_INVALID_PLACEMENT|adjacency"));
        }

        [Test]
        public void Validate_OnDiagonallyTouchingShips_WhenAdjacencyIsForbidden_ReturnsAdjacency()
        {
            // The no-touching variant forbids corners too, which is what makes the halo an
            // eight-neighbourhood rather than four.
            RuleFlags noTouching = _config.RuleSet(RuleFlags.ClassicId);
            noTouching.AllowAdjacentShips = false;

            List<ShipPlacement> fleet = ValidClassicFleet();
            fleet[1] = new ShipPlacement(ShipClass.Battleship, new Coord(5, 1), Orientation.Horizontal);

            PlacementValidation result = PlacementRules.Validate(_classic, noTouching, fleet);

            Assert.That(result.Error, Is.EqualTo("ERR_INVALID_PLACEMENT|adjacency"));
        }

        [Test]
        public void ToOccupancy_CoversExactlyTheFleetCellCount()
        {
            BitBoard occupancy = PlacementRules.ToOccupancy(_classic, ValidClassicFleet());

            Assert.That(occupancy.PopCount, Is.EqualTo(_classic.FleetCellCount));
            Assert.That(occupancy.PopCount, Is.EqualTo(17));
        }

        [Test]
        public void CellsOf_WalksBowToStern()
        {
            IReadOnlyList<Coord> cells = PlacementRules.CellsOf(
                _classic, new ShipPlacement(ShipClass.Carrier, new Coord(2, 3), Orientation.Vertical), 5);

            Assert.That(cells.Count, Is.EqualTo(5));
            Assert.That(cells[0], Is.EqualTo(new Coord(2, 3)));
            Assert.That(cells[4], Is.EqualTo(new Coord(2, 7)));
        }

        [TestCase(BoardConfig.BlitzId)]
        [TestCase(BoardConfig.ClassicId)]
        [TestCase(BoardConfig.AdmiralId)]
        public void GenerateRandom_AlwaysProducesAValidFleet(string boardId)
        {
            BoardConfig board = _config.Board(boardId);
            RuleFlags rules = _config.RuleSet(RuleFlags.ClassicId);

            for (ulong seed = 1; seed <= 200; seed++)
            {
                IRandom rng = new SeededRandom(seed);
                IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(board, rules, rng);

                PlacementValidation validation = PlacementRules.Validate(board, rules, layout);
                Assert.That(validation.IsValid, Is.True, "seed " + seed + ": " + validation.Error);
            }
        }

        [Test]
        public void GenerateRandom_UnderTheNoTouchingVariant_StillProducesAValidFleet()
        {
            RuleFlags noTouching = _config.RuleSet(RuleFlags.ClassicId);
            noTouching.AllowAdjacentShips = false;

            for (ulong seed = 1; seed <= 100; seed++)
            {
                IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(_classic, noTouching, new SeededRandom(seed));
                Assert.That(PlacementRules.Validate(_classic, noTouching, layout).IsValid, Is.True, "seed " + seed);
            }
        }

        [Test]
        public void GenerateRandom_WithTheSameSeed_ProducesTheSameFleet()
        {
            IReadOnlyList<ShipPlacement> a = PlacementRules.GenerateRandom(_classic, _classicRules, new SeededRandom(4242));
            IReadOnlyList<ShipPlacement> b = PlacementRules.GenerateRandom(_classic, _classicRules, new SeededRandom(4242));

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void GenerateRandom_WithDifferentSeeds_DoesNotAlwaysProduceTheSameFleet()
        {
            IReadOnlyList<ShipPlacement> a = PlacementRules.GenerateRandom(_classic, _classicRules, new SeededRandom(1));
            IReadOnlyList<ShipPlacement> b = PlacementRules.GenerateRandom(_classic, _classicRules, new SeededRandom(2));

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void GenerateRandom_ReturnsShipsInFleetOrder()
        {
            IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(_classic, _classicRules, new SeededRandom(7));

            for (int i = 0; i < layout.Count; i++)
            {
                Assert.That(layout[i].Class, Is.EqualTo(_classic.Fleet[i].Class));
            }
        }

        [Test]
        public void IsHumanLike_RejectsALayoutHuggingTheBorder()
        {
            // Every ship on row 0 or column 0: all 17 cells on the border, far past the 60 % cap.
            List<ShipPlacement> hugging = new List<ShipPlacement>
            {
                new ShipPlacement(ShipClass.Carrier, new Coord(0, 0), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Battleship, new Coord(5, 0), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Cruiser, new Coord(0, 9), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Submarine, new Coord(4, 9), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Destroyer, new Coord(0, 1), Orientation.Vertical)
            };

            Assert.That(PlacementRules.Validate(_classic, _classicRules, hugging).IsValid, Is.True, "fixture must be legal");
            Assert.That(PlacementRules.IsHumanLike(_classic, _config.Ai, hugging), Is.False);
        }

        [Test]
        public void IsHumanLike_RejectsTwoLargeShipsLyingParallelAndAdjacent()
        {
            List<ShipPlacement> parallel = new List<ShipPlacement>
            {
                new ShipPlacement(ShipClass.Carrier, new Coord(2, 3), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Battleship, new Coord(2, 4), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Cruiser, new Coord(1, 7), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Submarine, new Coord(6, 7), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Destroyer, new Coord(3, 1), Orientation.Horizontal)
            };

            Assert.That(PlacementRules.Validate(_classic, _classicRules, parallel).IsValid, Is.True, "fixture must be legal");
            Assert.That(PlacementRules.IsHumanLike(_classic, _config.Ai, parallel), Is.False);
        }

        [Test]
        public void IsHumanLike_AcceptsASpreadLayout()
        {
            List<ShipPlacement> spread = new List<ShipPlacement>
            {
                new ShipPlacement(ShipClass.Carrier, new Coord(2, 2), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Battleship, new Coord(4, 5), Orientation.Vertical),
                new ShipPlacement(ShipClass.Cruiser, new Coord(1, 6), Orientation.Horizontal),
                new ShipPlacement(ShipClass.Submarine, new Coord(7, 1), Orientation.Vertical),
                new ShipPlacement(ShipClass.Destroyer, new Coord(6, 8), Orientation.Horizontal)
            };

            Assert.That(PlacementRules.Validate(_classic, _classicRules, spread).IsValid, Is.True, "fixture must be legal");
            Assert.That(PlacementRules.IsHumanLike(_classic, _config.Ai, spread), Is.True);
        }

        [Test]
        public void GenerateHumanLike_ProducesValidLayouts_AndUsuallySatisfiesTheHeuristic()
        {
            int humanLike = 0;

            for (ulong seed = 1; seed <= 100; seed++)
            {
                IReadOnlyList<ShipPlacement> layout =
                    PlacementRules.GenerateHumanLike(_classic, _classicRules, _config.Ai, new SeededRandom(seed));

                Assert.That(PlacementRules.Validate(_classic, _classicRules, layout).IsValid, Is.True, "seed " + seed);
                if (PlacementRules.IsHumanLike(_classic, _config.Ai, layout)) humanLike++;
            }

            // The design doc allows accepting the last attempt after the retry budget runs out, so
            // this is a rate, not an absolute. Anything near 100 % means the filter is working.
            Assert.That(humanLike, Is.GreaterThanOrEqualTo(95));
        }
    }
}
