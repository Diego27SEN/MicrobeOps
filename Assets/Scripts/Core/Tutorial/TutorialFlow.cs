#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>Where the onboarding currently is. Values travel in analytics, so they are frozen.</summary>
    public enum TutorialStep : byte
    {
        Welcome = 0,
        PlaceFirstShip = 1,
        PlaceRemainingShips = 2,
        ConfirmFleet = 3,
        FireFirstShot = 4,
        ScoreFirstHit = 5,
        SinkFirstShip = 6,
        Finished = 7
    }

    /// <summary>Things the player can do that the onboarding cares about.</summary>
    public enum TutorialTrigger : byte
    {
        Acknowledged = 0,
        ShipPlaced = 1,
        FleetComplete = 2,
        PlacementSubmitted = 3,
        ShotFired = 4,
        ShotHit = 5,
        ShipSunk = 6
    }

    /// <summary>
    /// The onboarding, as a state machine over things the player does.
    /// <para>
    /// It lives in Core, with no reference to any screen, for the same reason the rules do: it is
    /// the part that can be wrong, and here it can be tested exhaustively without a Unity licence.
    /// The client's whole job is to show the current step's text and report what the player did.
    /// </para>
    /// <para>
    /// Every step waits for one specific action, and unrelated actions are ignored rather than
    /// skipped past. A player who fires before being asked to does not race ahead of the
    /// explanation, and a player who does the right thing twice does not advance twice.
    /// </para>
    /// <para>
    /// Target length is around 90 seconds: deploy a fleet with help, then land a hit and sink
    /// something. Anything the player can discover by tapping is not in here.
    /// </para>
    /// </summary>
    public sealed class TutorialFlow
    {
        private readonly struct Stage
        {
            public readonly TutorialStep Step;
            public readonly TutorialTrigger Awaits;

            public Stage(TutorialStep step, TutorialTrigger awaits)
            {
                Step = step;
                Awaits = awaits;
            }
        }

        private static readonly Stage[] Script =
        {
            new Stage(TutorialStep.Welcome, TutorialTrigger.Acknowledged),
            new Stage(TutorialStep.PlaceFirstShip, TutorialTrigger.ShipPlaced),
            new Stage(TutorialStep.PlaceRemainingShips, TutorialTrigger.FleetComplete),
            new Stage(TutorialStep.ConfirmFleet, TutorialTrigger.PlacementSubmitted),
            new Stage(TutorialStep.FireFirstShot, TutorialTrigger.ShotFired),
            new Stage(TutorialStep.ScoreFirstHit, TutorialTrigger.ShotHit),
            new Stage(TutorialStep.SinkFirstShip, TutorialTrigger.ShipSunk),
            new Stage(TutorialStep.Finished, TutorialTrigger.Acknowledged)
        };

        private int _index;

        /// <summary>Raised whenever the step changes, so the client can repaint without polling.</summary>
        public event Action<TutorialStep>? StepChanged;

        public TutorialStep Current
        {
            get { return Script[_index].Step; }
        }

        public bool IsFinished
        {
            get { return Current == TutorialStep.Finished; }
        }

        /// <summary>True when the step is waiting for the player to read and confirm, not to act.</summary>
        public bool WaitsForAcknowledgement
        {
            get { return Script[_index].Awaits == TutorialTrigger.Acknowledged; }
        }

        /// <summary>Localization key for the current step. The flow never holds player-facing text.</summary>
        public string LocalizationKey
        {
            get { return KeyFor(Current); }
        }

        public static string KeyFor(TutorialStep step)
        {
            return "tutorial.step." + step;
        }

        /// <summary>
        /// Feeds an action into the flow. Returns true only when it actually advanced, so the
        /// caller can tell "the player did the thing" from "the player did something else".
        /// <para>
        /// A step that is only waiting to be read is cleared by acting instead. Someone who reaches
        /// for Random and Ready without tapping through the welcome has read as much as they mean
        /// to, and holding the flow there leaves the overlay explaining deployment to a player who
        /// is already firing. The tutorial following reality beats the tutorial being obeyed.
        /// </para>
        /// <para>
        /// Steps that wait for an action still ignore everything else, so a stray shot cannot skip
        /// past the deployment steps. Only reading is optional; doing is not.
        /// </para>
        /// </summary>
        public bool Advance(TutorialTrigger trigger)
        {
            if (IsFinished) return false;

            bool advanced = false;

            if (WaitsForAcknowledgement && trigger != TutorialTrigger.Acknowledged && _index < Script.Length - 1)
            {
                _index++;
                advanced = true;
            }

            if (!IsFinished && Script[_index].Awaits == trigger)
            {
                _index++;
                advanced = true;
            }

            if (advanced) StepChanged?.Invoke(Current);
            return advanced;
        }

        /// <summary>Jumps to the end. Onboarding a player who does not want it is worse than none.</summary>
        public void Skip()
        {
            if (IsFinished) return;

            _index = Script.Length - 1;
            StepChanged?.Invoke(Current);
        }

        /// <summary>Every step in script order. Used by tests and by the analytics funnel.</summary>
        public static IReadOnlyList<TutorialStep> AllSteps()
        {
            TutorialStep[] steps = new TutorialStep[Script.Length];
            for (int i = 0; i < Script.Length; i++) steps[i] = Script[i].Step;
            return steps;
        }
    }
}
