#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class AiStrategyTests
    {
        private GameConfig _config = null!;
        private BoardConfig _classic = null!;
        private RuleFlags _rules = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
            _classic = _config.Board(BoardConfig.ClassicId);
            _rules = _config.RuleSet(RuleFlags.ClassicId);
        }

        /// <summary>
        /// Plays one solitaire game: the AI shoots at a hidden fleet until it has sunk everything.
        /// Returns the shots it needed. This is the measurement behind the section 4.6 targets.
        /// </summary>
        private int ShotsToClearBoard(AiDifficulty difficulty, ulong layoutSeed, ulong aiSeed, List<Coord>? trace = null)
        {
            IReadOnlyList<ShipPlacement> layout = PlacementRules.GenerateRandom(_classic, _rules, new SeededRandom(layoutSeed));

            PlayerBoard board = PlayerBoard.CreateEmpty();
            ShipRecord[] fleet = new ShipRecord[layout.Count];
            for (int i = 0; i < layout.Count; i++)
            {
                fleet[i] = new ShipRecord
                {
                    Class = layout[i].Class,
                    Length = (byte)_classic.Fleet[i].Length,
                    Bow = layout[i].Bow,
                    Orientation = layout[i].Orientation,
                    HitCount = 0,
                    SunkOnShotIndex = -1
                };
            }

            board.Occupancy = PlacementRules.ToOccupancy(_classic, layout);
            board.Fleet = fleet;
            board.HasPlacement = true;

            IAiStrategy strategy = AiFactory.Create(difficulty, _config.Ai);
            IRandom rng = new SeededRandom(aiSeed);

            int shots = 0;
            while (!ShotRules.IsFleetDestroyed(in board) && shots < _classic.CellCount)
            {
                AiMemory memory = AiMemory.FromDisclosedKnowledge(in board, _classic, _rules);
                Coord cell = strategy.NextShot(in memory, _classic, rng);
                trace?.Add(cell);

                ShotResolution resolution = ShotRules.Resolve(ref board, _classic, _rules, cell, shots);
                strategy.Observe(cell, resolution.Outcome, resolution.SunkClass);
                shots++;
            }

            Assert.That(ShotRules.IsFleetDestroyed(in board), Is.True, difficulty + " failed to clear the board in " + _classic.CellCount + " shots");
            return shots;
        }

        private double AverageShotsToClear(AiDifficulty difficulty, int games)
        {
            long total = 0;
            for (int i = 0; i < games; i++)
            {
                total += ShotsToClearBoard(difficulty, (ulong)(1000 + i), (ulong)(50_000 + i));
            }
            return (double)total / games;
        }

        // --- determinism (design doc section 12.2, invariant 7) ----------------------------------

        [TestCase(AiDifficulty.Easy)]
        [TestCase(AiDifficulty.Medium)]
        [TestCase(AiDifficulty.Hard)]
        [TestCase(AiDifficulty.Adaptive)]
        public void NextShot_WithTheSameSeed_ProducesTheSameSequence(AiDifficulty difficulty)
        {
            List<Coord> first = new List<Coord>();
            List<Coord> second = new List<Coord>();

            ShotsToClearBoard(difficulty, 7, 4242, first);
            ShotsToClearBoard(difficulty, 7, 4242, second);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Count, Is.GreaterThan(10), "sanity: the trace must not be trivially short");
        }

        [TestCase(AiDifficulty.Easy)]
        [TestCase(AiDifficulty.Medium)]
        [TestCase(AiDifficulty.Hard)]
        [TestCase(AiDifficulty.Adaptive)]
        public void NextShot_NeverRepeatsACellAndStaysOnTheBoard(AiDifficulty difficulty)
        {
            for (ulong seed = 1; seed <= 25; seed++)
            {
                List<Coord> trace = new List<Coord>();
                ShotsToClearBoard(difficulty, seed, seed + 900, trace);

                BitBoard seen = BitBoard.Empty;
                for (int i = 0; i < trace.Count; i++)
                {
                    Coord cell = trace[i];
                    Assert.That(_classic.Contains(cell), Is.True, difficulty + " shot off-board at " + cell);

                    int index = cell.ToIndex(_classic.Width);
                    Assert.That(seen.Get(index), Is.False, difficulty + " shot " + cell.ToA1() + " twice");
                    seen = seen.Set(index);
                }
            }
        }

        // --- the AI does not cheat ----------------------------------------------------------------

        [Test]
        public void AiMemory_IsBuiltFromDisclosedKnowledgeOnly_SoTheAiCannotSeeTheBoard()
        {
            // Two boards that differ ONLY in where the surviving ships sit. If the AI's knowledge
            // depended on occupancy in any way, these would produce different memories - and "Hard
            // is hard because it cheats" would be true. It must not be.
            PlayerBoard left = BoardWithSurvivorsAt(new Coord(0, 0), new Coord(0, 2));
            PlayerBoard right = BoardWithSurvivorsAt(new Coord(5, 5), new Coord(7, 7));

            AiMemory a = AiMemory.FromDisclosedKnowledge(in left, _classic, _rules);
            AiMemory b = AiMemory.FromDisclosedKnowledge(in right, _classic, _rules);

            Assert.That(a.Shots, Is.EqualTo(b.Shots));
            Assert.That(a.Hits, Is.EqualTo(b.Hits));
            Assert.That(a.SunkCells, Is.EqualTo(b.SunkCells));
            Assert.That(a.RemainingShipLengths, Is.EqualTo(b.RemainingShipLengths));
        }

        [Test]
        public void AiMemory_UnderSalvoAggregateReport_SeesOnlyDisclosedHits()
        {
            RuleFlags salvo = _config.RuleSet(RuleFlags.SalvoId);

            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Cruiser, Length = 3, Bow = new Coord(0, 0), Orientation = Orientation.Horizontal, HitCount = 0, SunkOnShotIndex = -1 }
            };
            board.Occupancy = BitBoard.Empty.Set(0).Set(1).Set(2);
            board.HasPlacement = true;

            ShotRules.Resolve(ref board, _classic, salvo, new Coord(0, 0), 0);
            AiMemory memory = AiMemory.FromDisclosedKnowledge(in board, _classic, salvo);

            Assert.That(board.Hits.PopCount, Is.EqualTo(1));
            Assert.That(memory.Hits.IsEmpty, Is.True, "the AI must not learn a hit the rules did not disclose");
            Assert.That(memory.Shots.PopCount, Is.EqualTo(1));
        }

        [Test]
        public void AiMemory_WhenSunkCellsAreNotRevealed_DoesNotInventThem()
        {
            RuleFlags hidden = _config.RuleSet(RuleFlags.ClassicId);
            hidden.RevealSunkCells = false;

            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, Bow = new Coord(4, 4), Orientation = Orientation.Horizontal, HitCount = 2, SunkOnShotIndex = 3 }
            };
            board.HasPlacement = true;

            AiMemory memory = AiMemory.FromDisclosedKnowledge(in board, _classic, hidden);

            Assert.That(memory.SunkCells.IsEmpty, Is.True);
            Assert.That(memory.SunkShips.Count, Is.EqualTo(1));
        }

        // --- accuracy targets (design doc section 4.6, QA acceptance criteria) ---------------------

        [Test]
        public void Hard_ClearsTheClassicFleetInUnder48ShotsOnAverage()
        {
            // The design doc is explicit: if Hard cannot get under roughly 48 shots for 17 cells,
            // the algorithm is wrong. This is the test that says so.
            double average = AverageShotsToClear(AiDifficulty.Hard, 120);

            Assert.That(average, Is.LessThan(_config.Ai.For(AiDifficulty.Hard).MaxAverageShotsToClearClassic),
                "Hard averaged " + average.ToString("F1") + " shots");
        }

        [Test]
        public void Medium_LandsInsideItsExpectedAccuracyBand()
        {
            double average = AverageShotsToClear(AiDifficulty.Medium, 120);

            Assert.That(average, Is.LessThan(_config.Ai.For(AiDifficulty.Medium).MaxAverageShotsToClearClassic),
                "Medium averaged " + average.ToString("F1") + " shots");
        }

        [Test]
        public void Easy_LandsInsideItsExpectedAccuracyBand()
        {
            double average = AverageShotsToClear(AiDifficulty.Easy, 120);

            Assert.That(average, Is.LessThan(_config.Ai.For(AiDifficulty.Easy).MaxAverageShotsToClearClassic),
                "Easy averaged " + average.ToString("F1") + " shots");
        }

        [Test]
        public void Difficulties_AreOrderedEasyThenMediumThenHard()
        {
            // The four difficulties have to be genuinely different, not three labels on one
            // algorithm. Fewer shots is better play.
            double easy = AverageShotsToClear(AiDifficulty.Easy, 80);
            double medium = AverageShotsToClear(AiDifficulty.Medium, 80);
            double hard = AverageShotsToClear(AiDifficulty.Hard, 80);

            Assert.That(medium, Is.LessThan(easy), "Medium (" + medium.ToString("F1") + ") must beat Easy (" + easy.ToString("F1") + ")");
            Assert.That(hard, Is.LessThan(medium), "Hard (" + hard.ToString("F1") + ") must beat Medium (" + medium.ToString("F1") + ")");
        }

        [Test]
        public void Adaptive_PlaysBetweenMediumAndHard()
        {
            double medium = AverageShotsToClear(AiDifficulty.Medium, 80);
            double hard = AverageShotsToClear(AiDifficulty.Hard, 80);
            double adaptive = AverageShotsToClear(AiDifficulty.Adaptive, 80);

            Assert.That(adaptive, Is.LessThan(medium));
            Assert.That(adaptive, Is.GreaterThanOrEqualTo(hard - 0.5));
        }

        // --- hunting behaviour ---------------------------------------------------------------------

        [Test]
        public void Hard_HuntsOnTheEvenParityWhileTheSmallestShipIsTwoCellsLong()
        {
            // Parity hunting cannot miss a two-cell ship, and halves the search space doing it.
            AiMemory fresh = new AiMemory
            {
                Shots = BitBoard.Empty,
                Hits = BitBoard.Empty,
                SunkCells = BitBoard.Empty,
                RemainingShipLengths = new[] { 5, 4, 3, 3, 2 }
            };

            IAiStrategy hard = AiFactory.Create(AiDifficulty.Hard, _config.Ai);

            for (ulong seed = 1; seed <= 40; seed++)
            {
                Coord shot = hard.NextShot(in fresh, _classic, new SeededRandom(seed));
                Assert.That((shot.X + shot.Y) % 2, Is.EqualTo(0), "seed " + seed + " hunted off-parity at " + shot.ToA1());
            }
        }

        [Test]
        public void Medium_FollowsUpAdjacentToAnUnresolvedHit()
        {
            AiMemory memory = new AiMemory
            {
                Shots = BitBoard.Empty.Set(new Coord(4, 4).ToIndex(10)),
                Hits = BitBoard.Empty.Set(new Coord(4, 4).ToIndex(10)),
                SunkCells = BitBoard.Empty,
                RemainingShipLengths = new[] { 3 }
            };

            IAiStrategy medium = AiFactory.Create(AiDifficulty.Medium, _config.Ai);

            for (ulong seed = 1; seed <= 30; seed++)
            {
                Coord shot = medium.NextShot(in memory, _classic, new SeededRandom(seed));
                int distance = System.Math.Abs(shot.X - 4) + System.Math.Abs(shot.Y - 4);
                Assert.That(distance, Is.EqualTo(1), "seed " + seed + " wandered off to " + shot.ToA1());
            }
        }

        [Test]
        public void Easy_SometimesWalksAwayFromAnObviousFollowUp()
        {
            // The 25 % chance is what separates Easy from Medium. If this ever hits zero, Easy has
            // silently become Medium and the difficulty ladder has lost a rung.
            AiMemory memory = new AiMemory
            {
                Shots = BitBoard.Empty.Set(new Coord(4, 4).ToIndex(10)),
                Hits = BitBoard.Empty.Set(new Coord(4, 4).ToIndex(10)),
                SunkCells = BitBoard.Empty,
                RemainingShipLengths = new[] { 3 }
            };

            IAiStrategy easy = AiFactory.Create(AiDifficulty.Easy, _config.Ai);

            // Easy only chases the hit it just scored, so it has to be told about it.
            easy.Observe(new Coord(4, 4), ShotOutcome.Hit, null);

            int wandered = 0;

            for (ulong seed = 1; seed <= 200; seed++)
            {
                Coord shot = easy.NextShot(in memory, _classic, new SeededRandom(seed));
                if (System.Math.Abs(shot.X - 4) + System.Math.Abs(shot.Y - 4) != 1) wandered++;
            }

            Assert.That(wandered, Is.GreaterThan(20), "Easy never ignored a follow-up in 200 rolls");
            Assert.That(wandered, Is.LessThan(100), "Easy ignored follow-ups far more often than the configured 25 %");
        }

        [Test]
        public void Hard_ExtendsAlongTheAxisOfTwoCollinearHits()
        {
            // Line inference: two hits in a row make the placements that continue the line dominate
            // the density map, so the next shot must be at one of the two ends.
            AiMemory memory = new AiMemory
            {
                Shots = BitBoard.Empty.Set(new Coord(3, 5).ToIndex(10)).Set(new Coord(4, 5).ToIndex(10)),
                Hits = BitBoard.Empty.Set(new Coord(3, 5).ToIndex(10)).Set(new Coord(4, 5).ToIndex(10)),
                SunkCells = BitBoard.Empty,
                RemainingShipLengths = new[] { 5, 4, 3 }
            };

            IAiStrategy hard = AiFactory.Create(AiDifficulty.Hard, _config.Ai);

            for (ulong seed = 1; seed <= 30; seed++)
            {
                Coord shot = hard.NextShot(in memory, _classic, new SeededRandom(seed));
                bool onTheLine = shot.Y == 5 && (shot.X == 2 || shot.X == 5);
                Assert.That(onTheLine, Is.True, "seed " + seed + " ignored the line and shot " + shot.ToA1());
            }
        }

        [Test]
        public void Hard_NeverShootsIntoWaterAlreadyRuledOut()
        {
            // A cell surrounded by misses cannot hold the smallest surviving ship, so the density
            // map must give it zero and the AI must never pick it.
            AiMemory memory = new AiMemory
            {
                Shots = BitBoard.FromIndices(new[] { 1, 10, 12, 21 }),
                Hits = BitBoard.Empty,
                SunkCells = BitBoard.Empty,
                RemainingShipLengths = new[] { 3 }
            };

            IAiStrategy hard = AiFactory.Create(AiDifficulty.Hard, _config.Ai);

            for (ulong seed = 1; seed <= 30; seed++)
            {
                Coord shot = hard.NextShot(in memory, _classic, new SeededRandom(seed));
                Assert.That(shot, Is.Not.EqualTo(new Coord(1, 1)), "seed " + seed + " shot a cell no three-cell ship can reach");
            }
        }

        [Test]
        public void AiFactory_ReturnsAStrategyTaggedWithItsOwnDifficulty()
        {
            Assert.That(AiFactory.Create(AiDifficulty.Easy, _config.Ai).Difficulty, Is.EqualTo(AiDifficulty.Easy));
            Assert.That(AiFactory.Create(AiDifficulty.Medium, _config.Ai).Difficulty, Is.EqualTo(AiDifficulty.Medium));
            Assert.That(AiFactory.Create(AiDifficulty.Hard, _config.Ai).Difficulty, Is.EqualTo(AiDifficulty.Hard));
            Assert.That(AiFactory.Create(AiDifficulty.Adaptive, _config.Ai).Difficulty, Is.EqualTo(AiDifficulty.Adaptive));
        }

        private PlayerBoard BoardWithSurvivorsAt(Coord firstBow, Coord secondBow)
        {
            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, Bow = new Coord(8, 8), Orientation = Orientation.Horizontal, HitCount = 2, SunkOnShotIndex = 4 },
                new ShipRecord { Class = ShipClass.Cruiser, Length = 3, Bow = firstBow, Orientation = Orientation.Horizontal, HitCount = 0, SunkOnShotIndex = -1 },
                new ShipRecord { Class = ShipClass.Submarine, Length = 3, Bow = secondBow, Orientation = Orientation.Vertical, HitCount = 0, SunkOnShotIndex = -1 }
            };

            board.IncomingShots = BitBoard.FromIndices(new[] { 88, 89, 30, 31 });
            board.DisclosedHits = BitBoard.FromIndices(new[] { 88, 89 });
            board.Hits = board.DisclosedHits;
            board.HasPlacement = true;
            return board;
        }
    }
}
