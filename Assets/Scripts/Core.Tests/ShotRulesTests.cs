#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class ShotRulesTests
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

        /// <summary>A board holding a single destroyer of length two at A1-B1.</summary>
        private PlayerBoard SingleDestroyerBoard()
        {
            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord
                {
                    Class = ShipClass.Destroyer,
                    Length = 2,
                    Bow = new Coord(0, 0),
                    Orientation = Orientation.Horizontal,
                    HitCount = 0,
                    SunkOnShotIndex = -1
                }
            };
            board.Occupancy = BitBoard.Empty.Set(0).Set(1);
            board.HasPlacement = true;
            return board;
        }

        [Test]
        public void Resolve_OnEmptyWater_ReturnsMissAndRecordsTheShot()
        {
            PlayerBoard board = SingleDestroyerBoard();

            ShotResolution result = ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(5, 5), 0);

            Assert.That(result.Outcome, Is.EqualTo(ShotOutcome.Miss));
            Assert.That(board.IncomingShots.Get(new Coord(5, 5).ToIndex(10)), Is.True);
            Assert.That(board.Hits.IsEmpty, Is.True);
        }

        [Test]
        public void Resolve_OnShipCell_ReturnsHit()
        {
            PlayerBoard board = SingleDestroyerBoard();

            ShotResolution result = ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(0, 0), 0);

            Assert.That(result.Outcome, Is.EqualTo(ShotOutcome.Hit));
            Assert.That(board.Hits.Get(0), Is.True);
            Assert.That(board.Fleet[0].HitCount, Is.EqualTo(1));
            Assert.That(board.Fleet[0].IsSunk, Is.False);
        }

        [Test]
        public void Resolve_OnLastCellOfTheLastShip_ReturnsWin()
        {
            PlayerBoard board = SingleDestroyerBoard();

            ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(0, 0), 0);
            ShotResolution result = ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(1, 0), 1);

            Assert.That(result.Outcome, Is.EqualTo(ShotOutcome.Win));
            Assert.That(result.SunkClass, Is.EqualTo(ShipClass.Destroyer));
            Assert.That(board.Fleet[0].IsSunk, Is.True);
            Assert.That(board.Fleet[0].SunkOnShotIndex, Is.EqualTo(1));
            Assert.That(ShotRules.IsFleetDestroyed(in board), Is.True);
        }

        [Test]
        public void Resolve_SinkingWithShipsLeft_ReturnsSunkNotWin()
        {
            PlayerBoard board = SingleDestroyerBoard();
            List<ShipRecord> fleet = new List<ShipRecord>(board.Fleet)
            {
                new ShipRecord
                {
                    Class = ShipClass.Cruiser,
                    Length = 3,
                    Bow = new Coord(0, 5),
                    Orientation = Orientation.Horizontal,
                    HitCount = 0,
                    SunkOnShotIndex = -1
                }
            };
            board.Fleet = fleet.ToArray();
            board.Occupancy = board.Occupancy.Set(50).Set(51).Set(52);

            ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(0, 0), 0);
            ShotResolution result = ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(1, 0), 1);

            Assert.That(result.Outcome, Is.EqualTo(ShotOutcome.Sunk));
            Assert.That(ShotRules.IsFleetDestroyed(in board), Is.False);
        }

        [Test]
        public void Resolve_UnderNormalRules_DisclosesEveryHitImmediately()
        {
            PlayerBoard board = SingleDestroyerBoard();

            ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(0, 0), 0);

            Assert.That(board.DisclosedHits, Is.EqualTo(board.Hits));
        }

        [Test]
        public void Resolve_UnderSalvoAggregateReport_KeepsAHitUndisclosedUntilTheShipSinks()
        {
            // This is the whole point of the Salvo variant: you are told "one hit", not which cell.
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);
            PlayerBoard board = SingleDestroyerBoard();

            ShotRules.Resolve(ref board, _classic, salvo, new Coord(0, 0), 0);

            Assert.That(board.Hits.PopCount, Is.EqualTo(1));
            Assert.That(board.DisclosedHits.IsEmpty, Is.True, "the hit cell must not be disclosed yet");

            ShotRules.Resolve(ref board, _classic, salvo, new Coord(1, 0), 1);

            // Sinking is announced, and revealSunkCells is on for salvo, so both cells surface now.
            Assert.That(board.DisclosedHits.PopCount, Is.EqualTo(2));
        }

        [Test]
        public void ToShooterResult_WhenAnnounceSunkShipTypeIsOff_OmitsTheClass()
        {
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);
            PlayerBoard board = SingleDestroyerBoard();

            ShotRules.Resolve(ref board, _classic, salvo, new Coord(0, 0), 0);
            ShotResolution resolution = ShotRules.Resolve(ref board, _classic, salvo, new Coord(1, 0), 1);
            ShotResult result = ShotRules.ToShooterResult(in board, _classic, salvo, new Coord(1, 0), resolution);

            Assert.That(result.SunkClass, Is.Null);
            Assert.That(result.SunkCells, Is.Not.Null);
        }

        [Test]
        public void ToShooterResult_WhenRevealSunkCellsIsOff_OmitsTheCells()
        {
            RuleFlags hidden = _config.RuleSet(RuleFlags.ClassicId);
            hidden.RevealSunkCells = false;

            PlayerBoard board = SingleDestroyerBoard();
            ShotRules.Resolve(ref board, _classic, hidden, new Coord(0, 0), 0);
            ShotResolution resolution = ShotRules.Resolve(ref board, _classic, hidden, new Coord(1, 0), 1);
            ShotResult result = ShotRules.ToShooterResult(in board, _classic, hidden, new Coord(1, 0), resolution);

            Assert.That(result.SunkCells, Is.Null);
            Assert.That(result.SunkClass, Is.EqualTo(ShipClass.Destroyer));
        }

        [Test]
        public void ToShooterResult_OnAMiss_CarriesNoSinkingData()
        {
            PlayerBoard board = SingleDestroyerBoard();
            ShotResolution resolution = ShotRules.Resolve(ref board, _classic, _classicRules, new Coord(5, 5), 0);
            ShotResult result = ShotRules.ToShooterResult(in board, _classic, _classicRules, new Coord(5, 5), resolution);

            Assert.That(result.Outcome, Is.EqualTo(ShotOutcome.Miss));
            Assert.That(result.SunkClass, Is.Null);
            Assert.That(result.SunkCells, Is.Null);
        }

        [Test]
        public void ShotsForTurn_UnderFixedRules_IsOne()
        {
            PlayerBoard board = SingleDestroyerBoard();
            Assert.That(ShotRules.ShotsForTurn(in board, _classicRules), Is.EqualTo(1));
        }

        [Test]
        public void ShotsForTurn_UnderSalvo_EqualsTheShootersShipsAfloat()
        {
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);
            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Cruiser, Length = 3, HitCount = 0, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, HitCount = 2, SunkOnShotIndex = 4 },
                new ShipRecord { Class = ShipClass.Submarine, Length = 3, HitCount = 1, SunkOnShotIndex = -1 }
            };

            Assert.That(ShotRules.ShotsForTurn(in board, salvo), Is.EqualTo(2));
        }

        [Test]
        public void ShotsForTurn_UnderSalvo_NeverDropsBelowOne()
        {
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);
            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, HitCount = 2, SunkOnShotIndex = 1 }
            };

            Assert.That(ShotRules.ShotsForTurn(in board, salvo), Is.EqualTo(1));
        }

        [Test]
        public void IsFleetDestroyed_OnAnEmptyFleet_IsFalse()
        {
            // An undeployed board must never read as "already destroyed": that would end matches
            // before they start.
            PlayerBoard board = PlayerBoard.CreateEmpty();
            Assert.That(ShotRules.IsFleetDestroyed(in board), Is.False);
        }
    }
}
