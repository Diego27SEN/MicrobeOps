#nullable enable

using System.Collections.Generic;
using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    [TestFixture]
    public sealed class TutorialFlowTests
    {
        [Test]
        public void ANewFlow_StartsAtTheWelcomeStep()
        {
            TutorialFlow flow = new TutorialFlow();

            Assert.That(flow.Current, Is.EqualTo(TutorialStep.Welcome));
            Assert.That(flow.IsFinished, Is.False);
            Assert.That(flow.WaitsForAcknowledgement, Is.True);
        }

        [Test]
        public void TheScriptedPath_ReachesTheEnd()
        {
            TutorialFlow flow = new TutorialFlow();

            Assert.That(flow.Advance(TutorialTrigger.Acknowledged), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceFirstShip));

            Assert.That(flow.Advance(TutorialTrigger.ShipPlaced), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceRemainingShips));

            Assert.That(flow.Advance(TutorialTrigger.FleetComplete), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.ConfirmFleet));

            Assert.That(flow.Advance(TutorialTrigger.PlacementSubmitted), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.FireFirstShot));

            Assert.That(flow.Advance(TutorialTrigger.ShotFired), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.ScoreFirstHit));

            Assert.That(flow.Advance(TutorialTrigger.ShotHit), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.SinkFirstShip));

            Assert.That(flow.Advance(TutorialTrigger.ShipSunk), Is.True);
            Assert.That(flow.IsFinished, Is.True);
        }

        [Test]
        public void AnUnrelatedAction_DoesNotAdvanceTheFlow()
        {
            // A player who fires before being asked to must not skip past the explanation.
            TutorialFlow flow = new TutorialFlow();
            flow.Advance(TutorialTrigger.Acknowledged);

            Assert.That(flow.Advance(TutorialTrigger.ShotFired), Is.False);
            Assert.That(flow.Advance(TutorialTrigger.ShipSunk), Is.False);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceFirstShip));
        }

        [Test]
        public void RepeatingTheSameAction_AdvancesOnlyOnce()
        {
            TutorialFlow flow = new TutorialFlow();
            flow.Advance(TutorialTrigger.Acknowledged);

            Assert.That(flow.Advance(TutorialTrigger.ShipPlaced), Is.True);
            Assert.That(flow.Advance(TutorialTrigger.ShipPlaced), Is.False, "placing a second ship must not skip a step");
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceRemainingShips));
        }

        [Test]
        public void ActingInsteadOfReading_ClearsTheWelcomeAndKeepsUp()
        {
            // The bug this exists for: the player hit Random and Ready without tapping through the
            // welcome, so every later trigger was ignored and the overlay went on explaining
            // deployment while they were already firing.
            TutorialFlow flow = new TutorialFlow();

            Assert.That(flow.Advance(TutorialTrigger.ShipPlaced), Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceRemainingShips));

            Assert.That(flow.Advance(TutorialTrigger.FleetComplete), Is.True);
            Assert.That(flow.Advance(TutorialTrigger.PlacementSubmitted), Is.True);

            Assert.That(flow.Current, Is.EqualTo(TutorialStep.FireFirstShot),
                "the overlay has to be talking about the screen the player is actually on");
        }

        [Test]
        public void ActingDoesNotSkipStepsThatWaitForAnAction()
        {
            // Only reading is optional. A stray shot must not jump past the deployment steps, or
            // the tutorial stops teaching the thing it exists to teach.
            TutorialFlow flow = new TutorialFlow();
            flow.Advance(TutorialTrigger.Acknowledged);

            Assert.That(flow.Advance(TutorialTrigger.ShotFired), Is.False);
            Assert.That(flow.Advance(TutorialTrigger.ShipSunk), Is.False);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.PlaceFirstShip));
        }

        [Test]
        public void TheClosingStep_IsNotClearedByPlayingOn()
        {
            // The last step has nothing after it, and the player keeps firing while it is up. It
            // has to stay until they dismiss it rather than vanish on the next shot.
            TutorialFlow flow = new TutorialFlow();
            foreach (TutorialTrigger trigger in new[]
            {
                TutorialTrigger.Acknowledged, TutorialTrigger.ShipPlaced, TutorialTrigger.FleetComplete,
                TutorialTrigger.PlacementSubmitted, TutorialTrigger.ShotFired, TutorialTrigger.ShotHit,
                TutorialTrigger.ShipSunk
            })
            {
                flow.Advance(trigger);
            }

            Assert.That(flow.Current, Is.EqualTo(TutorialStep.Finished));
            Assert.That(flow.Advance(TutorialTrigger.ShotFired), Is.False);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.Finished));
        }

        [Test]
        public void Skip_JumpsStraightToTheEnd()
        {
            TutorialFlow flow = new TutorialFlow();
            flow.Advance(TutorialTrigger.Acknowledged);

            flow.Skip();

            Assert.That(flow.IsFinished, Is.True);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.Finished));
        }

        [Test]
        public void AFinishedFlow_IgnoresEverything()
        {
            TutorialFlow flow = new TutorialFlow();
            flow.Skip();

            Assert.That(flow.Advance(TutorialTrigger.ShipPlaced), Is.False);
            Assert.That(flow.Advance(TutorialTrigger.Acknowledged), Is.False);
            Assert.That(flow.Current, Is.EqualTo(TutorialStep.Finished));
        }

        [Test]
        public void StepChanged_FiresOnceForEveryTransition()
        {
            TutorialFlow flow = new TutorialFlow();
            List<TutorialStep> seen = new List<TutorialStep>();
            flow.StepChanged += step => seen.Add(step);

            flow.Advance(TutorialTrigger.Acknowledged);
            flow.Advance(TutorialTrigger.ShotFired);
            flow.Advance(TutorialTrigger.ShipPlaced);

            Assert.That(seen, Is.EqualTo(new[] { TutorialStep.PlaceFirstShip, TutorialStep.PlaceRemainingShips }));
        }

        [Test]
        public void EveryStep_HasItsOwnLocalizationKey()
        {
            // A step with no text is a blank overlay the player cannot get past.
            HashSet<string> keys = new HashSet<string>();

            foreach (TutorialStep step in TutorialFlow.AllSteps())
            {
                string key = TutorialFlow.KeyFor(step);

                Assert.That(key, Does.StartWith("tutorial.step."));
                Assert.That(keys.Add(key), Is.True, "duplicate key for " + step);
            }

            Assert.That(keys.Count, Is.EqualTo(8));
        }

        [Test]
        public void OnlyTheFirstAndLastSteps_WaitForTheReader()
        {
            // Everything in between has to be driven by doing, not by tapping through text. That is
            // what keeps a ninety-second onboarding from turning into a wall of dialogs.
            TutorialFlow flow = new TutorialFlow();
            Assert.That(flow.WaitsForAcknowledgement, Is.True, "welcome");

            TutorialTrigger[] path =
            {
                TutorialTrigger.Acknowledged,
                TutorialTrigger.ShipPlaced,
                TutorialTrigger.FleetComplete,
                TutorialTrigger.PlacementSubmitted,
                TutorialTrigger.ShotFired,
                TutorialTrigger.ShotHit
            };

            foreach (TutorialTrigger trigger in path)
            {
                flow.Advance(trigger);
                if (!flow.IsFinished) Assert.That(flow.WaitsForAcknowledgement, Is.False, flow.Current.ToString());
            }

            flow.Advance(TutorialTrigger.ShipSunk);
            Assert.That(flow.IsFinished, Is.True);
            Assert.That(flow.WaitsForAcknowledgement, Is.True, "finished");
        }
    }
}
