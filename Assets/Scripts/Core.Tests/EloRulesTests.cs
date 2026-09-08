#nullable enable

using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class EloRulesTests
    {
        private EloConfig _elo = null!;

        [SetUp]
        public void SetUp()
        {
            _elo = TestGame.Config().Elo;
        }

        [Test]
        public void ExpectedScore_BetweenEqualRatings_IsAHalf()
        {
            Assert.That(EloRules.ExpectedScore(1000, 1000), Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void ExpectedScore_WithA400PointLead_IsAboutNineTenths()
        {
            Assert.That(EloRules.ExpectedScore(1400, 1000), Is.EqualTo(10.0 / 11.0).Within(1e-9));
        }

        [Test]
        public void ExpectedScore_IsSymmetric()
        {
            Assert.That(EloRules.ExpectedScore(1200, 900) + EloRules.ExpectedScore(900, 1200), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Apply_OnAnEvenMatch_MovesBothByHalfTheKFactor()
        {
            // Both veterans: K = 24, expected 0.5, so the winner gains 12 and the loser drops 12.
            (int newA, int newB) = EloRules.Apply(1000, 1000, 1.0, 100, 100, _elo);

            Assert.That(newA, Is.EqualTo(1012));
            Assert.That(newB, Is.EqualTo(988));
        }

        [Test]
        public void Apply_UsesEachPlayersOwnKFactor()
        {
            // A newcomer (K = 40) against a veteran (K = 24): the same result moves them differently,
            // which is the point of a per-player K.
            (int newA, int newB) = EloRules.Apply(1000, 1000, 1.0, 0, 100, _elo);

            Assert.That(newA - 1000, Is.EqualTo(20));
            Assert.That(1000 - newB, Is.EqualTo(12));
        }

        [Test]
        public void Apply_WhenTheFavouriteWins_MovesLittle()
        {
            (int newA, int newB) = EloRules.Apply(1600, 1000, 1.0, 100, 100, _elo);

            Assert.That(newA - 1600, Is.LessThanOrEqualTo(2));
            Assert.That(newA, Is.GreaterThan(1600));
            Assert.That(newB, Is.LessThan(1000));
        }

        [Test]
        public void Apply_WhenTheUnderdogWins_MovesALot()
        {
            (int newA, int newB) = EloRules.Apply(1000, 1600, 1.0, 100, 100, _elo);

            Assert.That(newA - 1000, Is.GreaterThanOrEqualTo(20));
            Assert.That(1600 - newB, Is.GreaterThanOrEqualTo(20));
        }

        [Test]
        public void Apply_IsZeroSumInMagnitude_WhenBothShareAKFactor()
        {
            (int newA, int newB) = EloRules.Apply(1234, 1456, 1.0, 60, 60, _elo);

            Assert.That(newA - 1234, Is.EqualTo(1456 - newB));
        }

        [Test]
        public void Apply_NeverDropsAPlayerBelowTheRatingFloor()
        {
            (int newA, int newB) = EloRules.Apply(_elo.RatingFloor, 2000, 0.0, 100, 100, _elo);

            Assert.That(newA, Is.EqualTo(_elo.RatingFloor));
            Assert.That(newB, Is.GreaterThanOrEqualTo(_elo.RatingFloor));
        }

        [Test]
        public void Apply_OnALoss_MirrorsTheWin()
        {
            (int winA, int lossB) = EloRules.Apply(1100, 1050, 1.0, 100, 100, _elo);
            (int lossA, int winB) = EloRules.Apply(1100, 1050, 0.0, 100, 100, _elo);

            Assert.That(winA, Is.GreaterThan(1100));
            Assert.That(lossA, Is.LessThan(1100));
            Assert.That(lossB, Is.LessThan(1050));
            Assert.That(winB, Is.GreaterThan(1050));
        }

        [Test]
        public void ApplyAbandonPenalty_CostsMoreThanAPlainDefeat()
        {
            // Abandoning has to hurt more than losing, or it becomes the cheap exit from a bad game.
            (int _, int afterLoss) = EloRules.Apply(1200, 1000, 1.0, 100, 100, _elo);
            int afterAbandon = EloRules.ApplyAbandonPenalty(afterLoss, _elo);

            Assert.That(afterAbandon, Is.EqualTo(afterLoss - 15));
            Assert.That(afterAbandon, Is.LessThan(afterLoss));
        }

        [Test]
        public void ApplyAbandonPenalty_RespectsTheFloor()
        {
            Assert.That(EloRules.ApplyAbandonPenalty(_elo.RatingFloor + 5, _elo), Is.EqualTo(_elo.RatingFloor));
        }

        [Test]
        public void Apply_WithAnOutOfRangeScore_Throws()
        {
            Assert.That(() => EloRules.Apply(1000, 1000, 1.5, 0, 0, _elo), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
