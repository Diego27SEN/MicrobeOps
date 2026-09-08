#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class PendingActionQueueTests
    {
        private const long Epoch = 1_700_000_000_000L;

        private PendingActionQueue _queue = null!;

        [SetUp]
        public void SetUp()
        {
            _queue = new PendingActionQueue();
        }

        private static PendingAction Action(string id, PendingActionKind kind, string target = "", string payload = "{}", long at = Epoch)
        {
            return new PendingAction(id, kind, target, payload, at);
        }

        [Test]
        public void ANonCriticalAction_IsQueued()
        {
            Assert.That(_queue.TryEnqueue(Action("a1", PendingActionKind.Analytics), out string rejection), Is.True);
            Assert.That(rejection, Is.Empty);
            Assert.That(_queue.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryQueueableKind_IsOneThatCanSafelyLandLate()
        {
            // The list is the point of the class. Anything that decides a match, moves currency or
            // validates a purchase must fail in front of the player instead of replaying minutes
            // later against a world that moved on. If this ever grows, it was a decision.
            PendingActionKind[] allowed =
            {
                PendingActionKind.Analytics,
                PendingActionKind.EquipCosmetic,
                PendingActionKind.SetPlayerName,
                PendingActionKind.RegisterPushToken,
                PendingActionKind.SettingsChanged
            };

            foreach (PendingActionKind kind in System.Enum.GetValues(typeof(PendingActionKind)))
            {
                bool expected = System.Array.IndexOf(allowed, kind) >= 0;
                Assert.That(PendingActionQueue.IsQueueable(kind), Is.EqualTo(expected), kind.ToString());
            }
        }

        [Test]
        public void EquippingTwoCosmeticsOnTheSameSlot_KeepsOnlyTheLatest()
        {
            // Replaying four cosmetic changes to arrive at the fourth is four times the work for
            // the same result.
            _queue.TryEnqueue(Action("a1", PendingActionKind.EquipCosmetic, "board_theme", "{\"id\":\"arctic\"}"), out _);
            _queue.TryEnqueue(Action("a2", PendingActionKind.EquipCosmetic, "board_theme", "{\"id\":\"neon\"}"), out _);

            IReadOnlyList<PendingAction> drained = _queue.Drain();

            Assert.That(drained.Count, Is.EqualTo(1));
            Assert.That(drained[0].Id, Is.EqualTo("a2"));
            Assert.That(drained[0].PayloadJson, Does.Contain("neon"));
        }

        [Test]
        public void CosmeticsOnDifferentSlots_BothSurvive()
        {
            _queue.TryEnqueue(Action("a1", PendingActionKind.EquipCosmetic, "board_theme"), out _);
            _queue.TryEnqueue(Action("a2", PendingActionKind.EquipCosmetic, "fleet_skin"), out _);

            Assert.That(_queue.Count, Is.EqualTo(2));
        }

        [Test]
        public void AnalyticsEvents_Accumulate_BecauseEachOneIsItsOwnFact()
        {
            for (int i = 0; i < 5; i++)
            {
                _queue.TryEnqueue(Action("a" + i, PendingActionKind.Analytics, "shot_fired"), out _);
            }

            Assert.That(_queue.Count, Is.EqualTo(5));
        }

        [Test]
        public void WhenFull_AnalyticsDropsTheOldestRatherThanGrowingForever()
        {
            PendingActionQueue small = new PendingActionQueue(3);

            for (int i = 0; i < 5; i++)
            {
                small.TryEnqueue(Action("a" + i, PendingActionKind.Analytics), out _);
            }

            IReadOnlyList<PendingAction> drained = small.Drain();

            Assert.That(drained.Count, Is.EqualTo(3));
            Assert.That(drained[0].Id, Is.EqualTo("a2"), "the oldest two were dropped");
            Assert.That(small.DroppedCount, Is.EqualTo(2));
        }

        [Test]
        public void WhenFull_ANonTelemetryActionIsRefusedRatherThanSilentlyDroppingSomethingElse()
        {
            PendingActionQueue small = new PendingActionQueue(2);
            small.TryEnqueue(Action("a1", PendingActionKind.Analytics), out _);
            small.TryEnqueue(Action("a2", PendingActionKind.Analytics), out _);

            bool queued = small.TryEnqueue(Action("a3", PendingActionKind.SetPlayerName, "name"), out string rejection);

            Assert.That(queued, Is.False);
            Assert.That(rejection, Is.EqualTo(PendingActionRejection.QueueFull));
        }

        [Test]
        public void Drain_HandsOverInOrderAndEmpties()
        {
            _queue.TryEnqueue(Action("a1", PendingActionKind.SetPlayerName, "name"), out _);
            _queue.TryEnqueue(Action("a2", PendingActionKind.EquipCosmetic, "avatar"), out _);

            IReadOnlyList<PendingAction> drained = _queue.Drain();

            Assert.That(drained.Count, Is.EqualTo(2));
            Assert.That(drained[0].Id, Is.EqualTo("a1"));
            Assert.That(drained[1].Id, Is.EqualTo("a2"));
            Assert.That(_queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void Requeue_PutsAFailedReplayBackAtTheFrontInOrder()
        {
            _queue.TryEnqueue(Action("new", PendingActionKind.Analytics), out _);

            _queue.Requeue(new List<PendingAction>
            {
                Action("old1", PendingActionKind.Analytics),
                Action("old2", PendingActionKind.Analytics)
            });

            IReadOnlyList<PendingAction> drained = _queue.Drain();

            Assert.That(drained[0].Id, Is.EqualTo("old1"));
            Assert.That(drained[1].Id, Is.EqualTo("old2"));
            Assert.That(drained[2].Id, Is.EqualTo("new"));
        }

        [Test]
        public void Requeue_PastCapacity_CountsWhatItDropsInsteadOfOverflowing()
        {
            PendingActionQueue small = new PendingActionQueue(1);
            small.TryEnqueue(Action("kept", PendingActionKind.Analytics), out _);

            small.Requeue(new List<PendingAction> { Action("o1", PendingActionKind.Analytics), Action("o2", PendingActionKind.Analytics) });

            Assert.That(small.Count, Is.EqualTo(1));
            Assert.That(small.DroppedCount, Is.EqualTo(2));
        }

        [Test]
        public void EveryActionCarriesAClientRequestId_SoAReplayCannotDuplicate()
        {
            Assert.That(
                () => new PendingAction("", PendingActionKind.Analytics, "", "{}", Epoch),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void Clear_EmptiesTheQueue()
        {
            _queue.TryEnqueue(Action("a1", PendingActionKind.Analytics), out _);
            _queue.Clear();

            Assert.That(_queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void ANonPositiveCapacity_IsRejected()
        {
            Assert.That(() => new PendingActionQueue(0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
