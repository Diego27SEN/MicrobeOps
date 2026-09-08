#nullable enable

using System;
using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class LocalMatchDriverTests
    {
        private GameConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestGame.Config();
        }

        private LocalMatchDriver VsAi(AiDifficulty difficulty = AiDifficulty.Medium, ulong seed = 7, string boardId = BoardConfig.ClassicId, string ruleSetId = RuleFlags.ClassicId)
        {
            return new LocalMatchDriver(
                _config, "local-1", boardId, ruleSetId, MatchMode.VsAi, difficulty,
                TestGame.PlayerA, null, new SeededRandom(seed), TestGame.Epoch);
        }

        private LocalMatchDriver PassAndPlay(ulong seed = 7)
        {
            return new LocalMatchDriver(
                _config, "local-2", BoardConfig.ClassicId, RuleFlags.ClassicId, MatchMode.LocalTwoPlayer, null,
                TestGame.PlayerA, TestGame.PlayerB, new SeededRandom(seed), TestGame.Epoch);
        }

        [Test]
        public void VsAi_DeploysTheAiAndWaitsForThePlayer()
        {
            LocalMatchDriver driver = VsAi();

            Assert.That(driver.Phase, Is.EqualTo(MatchPhase.Placement));
            Assert.That(driver.Snapshot().BoardB.HasPlacement, Is.True);
            Assert.That(driver.Snapshot().BoardA.HasPlacement, Is.False);
        }

        [Test]
        public void VsAi_TheAiGetsAReservedIdentity_SoItMovesThroughTheSameEnginePath()
        {
            Assert.That(VsAi().Snapshot().PlayerB, Is.EqualTo(MatchState.AiPlayerId));
        }

        [Test]
        public void SubmitPlacement_StartsTheMatchOnceThePlayerDeploys()
        {
            LocalMatchDriver driver = VsAi();

            ApplyResult result = driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(driver.Phase, Is.EqualTo(MatchPhase.InProgress));
        }

        [Test]
        public void SuggestPlacement_AlwaysProducesSomethingValidatePlacementAccepts()
        {
            LocalMatchDriver driver = VsAi();

            for (int i = 0; i < 25; i++)
            {
                IReadOnlyList<ShipPlacement> layout = driver.SuggestPlacement();
                Assert.That(driver.ValidatePlacement(layout).IsValid, Is.True);
            }
        }

        [Test]
        public void StepAi_DoesNothingWhenItIsNotTheAisTurn()
        {
            LocalMatchDriver driver = VsAi();
            Assert.That(driver.StepAi(TestGame.Epoch), Is.Null, "still in placement");

            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            if (!driver.IsAiTurn)
            {
                Assert.That(driver.StepAi(TestGame.Epoch), Is.Null, "it is the human's turn");
            }
        }

        [Test]
        public void AMatchCanOpenOnTheAisTurn_SoTheCallerMustStepItBeforeWaitingForInput()
        {
            // The first mover is a coin flip, so roughly half of vs-AI matches begin with the AI on
            // the clock. A caller that only steps the AI after the human has fired deadlocks those
            // matches outright - which is exactly what the match screen did. This pins the
            // condition the UI has to handle.
            int aiOpened = 0;

            for (ulong seed = 1; seed <= 40; seed++)
            {
                LocalMatchDriver driver = VsAi(AiDifficulty.Medium, seed);
                driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

                if (driver.IsAiTurn) aiOpened++;
            }

            Assert.That(aiOpened, Is.GreaterThan(0), "no seed ever gave the AI the first move; the fixture is not exercising the case");
            Assert.That(aiOpened, Is.LessThan(40), "the AI always moved first; the coin flip is not a coin flip");
        }

        [Test]
        public void StepAi_FromTheOpeningTurn_UnblocksTheMatch()
        {
            LocalMatchDriver driver = FirstDriverWhereAiOpens();

            Assert.That(driver.IsAiTurn, Is.True);
            ApplyResult? result = driver.StepAi(TestGame.Epoch + 1000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True, result.Error);
            Assert.That(driver.IsAiTurn, Is.False, "after the AI moves it must be the human's turn");
            Assert.That(driver.ViewFor(TestGame.PlayerA, TestGame.Epoch).IsYourTurn, Is.True);
        }

        private LocalMatchDriver FirstDriverWhereAiOpens()
        {
            for (ulong seed = 1; seed <= 100; seed++)
            {
                LocalMatchDriver driver = VsAi(AiDifficulty.Medium, seed);
                driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

                if (driver.IsAiTurn) return driver;
            }

            throw new InvalidOperationException("No seed in range gave the AI the opening move.");
        }

        [Test]
        public void StepAi_FiresAndPassesTheTurnBack()
        {
            LocalMatchDriver driver = VsAi(seed: 3);
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            long now = TestGame.Epoch;
            if (!driver.IsAiTurn)
            {
                driver.Fire(TestGame.PlayerA, new[] { FirstUntriedAgainstAi(driver) }, now += 1000);
            }

            Assert.That(driver.IsAiTurn, Is.True);
            ApplyResult? result = driver.StepAi(now + 1000);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True, result.Error);
            Assert.That(driver.Snapshot().BoardA.IncomingShots.PopCount, Is.GreaterThan(0));
        }

        [TestCase(AiDifficulty.Easy)]
        [TestCase(AiDifficulty.Medium)]
        [TestCase(AiDifficulty.Hard)]
        [TestCase(AiDifficulty.Adaptive)]
        public void AFullOfflineMatchAgainstEveryDifficulty_Concludes(AiDifficulty difficulty)
        {
            LocalMatchDriver driver = VsAi(difficulty, seed: 21);
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            long now = TestGame.Epoch;
            int guard = 0;

            while (!driver.IsFinished && guard++ < 500)
            {
                now += 1000;

                if (driver.IsAiTurn)
                {
                    ApplyResult? aiResult = driver.StepAi(now);
                    Assert.That(aiResult!.Success, Is.True, aiResult.Error);
                    continue;
                }

                ApplyResult result = driver.Fire(TestGame.PlayerA, new[] { FirstUntriedAgainstAi(driver) }, now);
                Assert.That(result.Success, Is.True, result.Error);
            }

            MatchState state = driver.Snapshot();
            Assert.That(driver.IsFinished, Is.True, difficulty + " match never concluded");
            Assert.That(state.EndReason, Is.EqualTo(MatchEndReason.FleetDestroyed));
            Assert.That(state.Winner, Is.Not.Null);
        }

        [Test]
        public void AFullSalvoMatchAgainstTheAi_Concludes()
        {
            // Salvo is the variant where the AI fires several cells at once, so it exercises the
            // in-volley duplicate masking that a one-shot-per-turn match never touches.
            LocalMatchDriver driver = VsAi(AiDifficulty.Hard, seed: 33, ruleSetId: RuleFlags.SalvoId);
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            long now = TestGame.Epoch;
            int guard = 0;

            while (!driver.IsFinished && guard++ < 500)
            {
                now += 1000;

                if (driver.IsAiTurn)
                {
                    ApplyResult? aiResult = driver.StepAi(now);
                    Assert.That(aiResult!.Success, Is.True, aiResult.Error);
                    continue;
                }

                ApplyResult result = driver.Fire(TestGame.PlayerA, UntriedAgainstAi(driver, driver.ShotsRequiredThisTurn()), now);
                Assert.That(result.Success, Is.True, result.Error);
            }

            Assert.That(driver.IsFinished, Is.True);
        }

        [Test]
        public void ViewFor_NeverShowsThePlayerTheAiFleetBeforeTheEnd()
        {
            // Offline the whole board sits in the same process, so nothing stops a careless UI from
            // reading it. Going through the projection anyway is what keeps that from happening.
            LocalMatchDriver driver = VsAi();
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            MatchStateView view = driver.ViewFor(TestGame.PlayerA, TestGame.Epoch);

            Assert.That(view.Reveal, Is.Null);
            Assert.That(BitBoard.FromBase64(view.Opponent.HitsBase64).IsEmpty, Is.True);
            Assert.That(view.OpponentIsAi, Is.True);
        }

        [Test]
        public void ViewFor_RevealsBothFleetsOnceTheMatchIsOver()
        {
            LocalMatchDriver driver = VsAi(AiDifficulty.Easy, seed: 12);
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            long now = TestGame.Epoch;
            int guard = 0;
            while (!driver.IsFinished && guard++ < 500)
            {
                now += 1000;
                if (driver.IsAiTurn) driver.StepAi(now);
                else driver.Fire(TestGame.PlayerA, new[] { FirstUntriedAgainstAi(driver) }, now);
            }

            MatchStateView view = driver.ViewFor(TestGame.PlayerA, now);

            Assert.That(view.Reveal, Is.Not.Null);
            Assert.That(view.Reveal!.Opponent.Length, Is.EqualTo(5));
            Assert.That(view.Outcome, Is.Not.Null);
        }

        [Test]
        public void PassAndPlay_WaitsForBothPlayersBeforeStarting()
        {
            LocalMatchDriver driver = PassAndPlay();

            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);
            Assert.That(driver.Phase, Is.EqualTo(MatchPhase.Placement));

            driver.SubmitPlacement(TestGame.PlayerB, driver.SuggestPlacement(), TestGame.Epoch);
            Assert.That(driver.Phase, Is.EqualTo(MatchPhase.InProgress));
            Assert.That(driver.IsAiTurn, Is.False);
        }

        [Test]
        public void PassAndPlay_ShowsEachPlayerOnlyTheirOwnBoard()
        {
            LocalMatchDriver driver = PassAndPlay();
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);
            driver.SubmitPlacement(TestGame.PlayerB, driver.SuggestPlacement(), TestGame.Epoch);

            MatchStateView forA = driver.ViewFor(TestGame.PlayerA, TestGame.Epoch);
            MatchStateView forB = driver.ViewFor(TestGame.PlayerB, TestGame.Epoch);

            MatchState state = driver.Snapshot();
            Assert.That(BitBoard.FromBase64(forA.You.OccupancyBase64), Is.EqualTo(state.BoardA.Occupancy));
            Assert.That(BitBoard.FromBase64(forB.You.OccupancyBase64), Is.EqualTo(state.BoardB.Occupancy));
            Assert.That(forA.IsYourTurn, Is.Not.EqualTo(forB.IsYourTurn));
        }

        [Test]
        public void Forfeit_EndsTheMatchInFavourOfTheOtherSide()
        {
            LocalMatchDriver driver = VsAi();
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            ApplyResult result = driver.Forfeit(TestGame.PlayerA, TestGame.Epoch + 5000);

            Assert.That(result.Success, Is.True);
            Assert.That(driver.IsFinished, Is.True);
            Assert.That(driver.Snapshot().Winner, Is.EqualTo(PlayerSlot.B));
        }

        [Test]
        public void SameSeed_ReplaysTheIdenticalOfflineMatch()
        {
            string first = PlayScripted(AiDifficulty.Hard, 4242);
            string replay = PlayScripted(AiDifficulty.Hard, 4242);

            Assert.That(replay, Is.EqualTo(first));
            Assert.That(first, Is.Not.Empty);
        }

        [Test]
        public void Constructor_RejectsOnlineModesAndMissingArguments()
        {
            Assert.That(
                () => new LocalMatchDriver(_config, "m", BoardConfig.ClassicId, RuleFlags.ClassicId, MatchMode.Ranked, null, "a", "b", new SeededRandom(1), 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());

            Assert.That(
                () => new LocalMatchDriver(_config, "m", BoardConfig.ClassicId, RuleFlags.ClassicId, MatchMode.VsAi, null, "a", null, new SeededRandom(1), 0),
                Throws.TypeOf<ArgumentException>());

            Assert.That(
                () => new LocalMatchDriver(_config, "m", BoardConfig.ClassicId, RuleFlags.ClassicId, MatchMode.LocalTwoPlayer, null, "a", null, new SeededRandom(1), 0),
                Throws.TypeOf<ArgumentException>());

            Assert.That(
                () => new LocalMatchDriver(null!, "m", BoardConfig.ClassicId, RuleFlags.ClassicId, MatchMode.LocalTwoPlayer, null, "a", "b", new SeededRandom(1), 0),
                Throws.TypeOf<ArgumentNullException>());
        }

        // --- helpers ------------------------------------------------------------------------------

        private string PlayScripted(AiDifficulty difficulty, ulong seed)
        {
            LocalMatchDriver driver = VsAi(difficulty, seed);
            driver.SubmitPlacement(TestGame.PlayerA, driver.SuggestPlacement(), TestGame.Epoch);

            System.Text.StringBuilder log = new System.Text.StringBuilder();
            long now = TestGame.Epoch;
            int guard = 0;

            while (!driver.IsFinished && guard++ < 500)
            {
                now += 1000;

                if (driver.IsAiTurn)
                {
                    ApplyResult? aiResult = driver.StepAi(now);
                    if (aiResult != null)
                    {
                        for (int i = 0; i < aiResult.Shots.Length; i++) log.Append(aiResult.Shots[i].Cell.ToA1()).Append(';');
                    }
                    continue;
                }

                Coord cell = FirstUntriedAgainstAi(driver);
                log.Append('>').Append(cell.ToA1()).Append(';');
                driver.Fire(TestGame.PlayerA, new[] { cell }, now);
            }

            return log.ToString();
        }

        private static Coord FirstUntriedAgainstAi(LocalMatchDriver driver)
        {
            return UntriedAgainstAi(driver, 1)[0];
        }

        private static Coord[] UntriedAgainstAi(LocalMatchDriver driver, int count)
        {
            MatchState state = driver.Snapshot();
            PlayerSlot targetSlot = MatchState.Other(state.CurrentTurn);
            PlayerBoard target = targetSlot == PlayerSlot.A ? state.BoardA : state.BoardB;

            List<Coord> picked = new List<Coord>(count);
            for (int i = 0; i < driver.Board.CellCount && picked.Count < count; i++)
            {
                if (!target.IncomingShots.Get(i)) picked.Add(driver.Board.FromIndex(i));
            }

            return picked.ToArray();
        }
    }
}
