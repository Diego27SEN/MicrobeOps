#nullable enable

using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class RetryPolicyTests
    {
        private RetryPolicy _policy = null!;

        [SetUp]
        public void SetUp()
        {
            _policy = new RetryPolicy();
        }

        [Test]
        public void TheDefaultSchedule_IsThreeRetriesAt250MsThen1sThen4s()
        {
            Assert.That(_policy.MaxAttempts, Is.EqualTo(4));
            Assert.That(_policy.DelayMsBefore(1), Is.EqualTo(250));
            Assert.That(_policy.DelayMsBefore(2), Is.EqualTo(1000));
            Assert.That(_policy.DelayMsBefore(3), Is.EqualTo(4000));
        }

        [Test]
        public void TheFirstAttempt_WaitsForNothing()
        {
            Assert.That(_policy.DelayMsBefore(0), Is.EqualTo(0));
        }

        [Test]
        public void TransportFailures_AreRetriedUpToTheLimit()
        {
            Assert.That(_policy.ShouldRetryTransport(1), Is.True);
            Assert.That(_policy.ShouldRetryTransport(3), Is.True);
            Assert.That(_policy.ShouldRetryTransport(4), Is.False);
        }

        [Test]
        public void ABusinessError_IsNeverRetried()
        {
            // The server decided. Asking again will not change its mind, and hammering it with a
            // call that cannot succeed is how a bad turn becomes an outage.
            string[] decided =
            {
                ErrorCodes.Format(ErrorCodes.NotYourTurn),
                ErrorCodes.Format(ErrorCodes.CellAlreadyTargeted, "C7"),
                ErrorCodes.Format(ErrorCodes.InvalidPlacement, "overlap"),
                ErrorCodes.Format(ErrorCodes.InsufficientFunds, "COIN", 500),
                ErrorCodes.Format(ErrorCodes.MatchFinished)
            };

            foreach (string error in decided)
            {
                Assert.That(_policy.ShouldRetryError(error, 1), Is.False, error);
            }
        }

        [Test]
        public void RateLimiting_IsRetried_BecauseItMeansLaterNotNo()
        {
            Assert.That(_policy.ShouldRetryError(ErrorCodes.Format(ErrorCodes.RateLimited, 2), 1), Is.True);
        }

        [Test]
        public void RateLimiting_StillRespectsTheAttemptLimit()
        {
            Assert.That(_policy.ShouldRetryError(ErrorCodes.Format(ErrorCodes.RateLimited, 2), 4), Is.False);
        }

        [Test]
        public void WhenTheServerSaysHowLongToWait_ThatWinsOverTheBackoff()
        {
            // The backoff is a guess; the server knows. Waiting 250 ms when it asked for 30 seconds
            // just earns another rate limit.
            string error = ErrorCodes.Format(ErrorCodes.RateLimited, 30);

            Assert.That(_policy.DelayMsBefore(1, error), Is.EqualTo(30_000));
            Assert.That(_policy.DelayMsBefore(3, error), Is.EqualTo(30_000));
        }

        [TestCase("ERR_RATE_LIMITED|1.5", 1500)]
        [TestCase("ERR_RATE_LIMITED|0", 0)]
        [TestCase("ERR_RATE_LIMITED", 0)]
        [TestCase("ERR_RATE_LIMITED|abc", 0)]
        [TestCase("ERR_NOT_YOUR_TURN|5", 0)]
        [TestCase("", 0)]
        public void RetryAfter_IsParsedFromTheErrorParameter(string error, int expectedMs)
        {
            Assert.That(RetryPolicy.RetryAfterMsFrom(error), Is.EqualTo(expectedMs));
        }

        [Test]
        public void RetryAfter_UsesInvariantCulture_SoADecimalNeverFlipsMeaning()
        {
            // On a Spanish locale a naive parse reads "1.5" as fifteen, and the client waits ten
            // times too long. The codes are machine-readable and are parsed as such.
            Assert.That(RetryPolicy.RetryAfterMsFrom("ERR_RATE_LIMITED|1.5"), Is.EqualTo(1500));
        }

        [Test]
        public void ProtocolAndKillSwitchErrors_EndTheSession_TheyDoNotDegrade()
        {
            Assert.That(RetryPolicy.IsFatalForSession(ErrorCodes.Format(ErrorCodes.ProtocolMismatch, 2)), Is.True);
            Assert.That(RetryPolicy.IsFatalForSession(ErrorCodes.Format(ErrorCodes.MinVersion, 3)), Is.True);
            Assert.That(RetryPolicy.IsFatalForSession(ErrorCodes.Format(ErrorCodes.FeatureDisabled, "ONLINE_ENABLED")), Is.True);

            Assert.That(RetryPolicy.IsFatalForSession(ErrorCodes.Format(ErrorCodes.NotYourTurn)), Is.False);
            Assert.That(RetryPolicy.IsFatalForSession(null), Is.False);
        }

        [Test]
        public void AStaleAction_AsksForASilentResync_NotAnErrorMessage()
        {
            // This is the double click and the flaky retry. The player did nothing wrong and should
            // never see it.
            Assert.That(RetryPolicy.RequiresSilentResync(ErrorCodes.Format(ErrorCodes.StaleAction, 42)), Is.True);
            Assert.That(RetryPolicy.RequiresSilentResync(ErrorCodes.Format(ErrorCodes.NotYourTurn)), Is.False);
        }

        [Test]
        public void ACustomSchedule_IsHonoured_AndAnEmptyOneIsRejected()
        {
            RetryPolicy fast = new RetryPolicy(new[] { 10, 20 });

            Assert.That(fast.MaxAttempts, Is.EqualTo(3));
            Assert.That(fast.DelayMsBefore(2), Is.EqualTo(20));
            Assert.That(fast.DelayMsBefore(9), Is.EqualTo(20), "past the schedule it holds at the last delay");

            Assert.That(() => new RetryPolicy(new int[0]), Throws.TypeOf<System.ArgumentException>());
        }
    }
}
