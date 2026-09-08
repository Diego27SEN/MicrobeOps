#nullable enable

using System;
using Armada.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// Paints the onboarding on top of whichever screen is showing.
    /// <para>
    /// It owns no logic: <see cref="TutorialFlow"/> in Core decides what step the player is on and
    /// what it takes to leave it. This class shows that step's text, offers a way out, and reports
    /// what the player did. Keeping the decisions on the other side of that line is what lets the
    /// whole onboarding be tested without opening Unity.
    /// </para>
    /// </summary>
    public sealed class TutorialOverlay
    {
        private readonly Action _onFinished;

        private VisualElement? _root;
        private TutorialFlow? _flow;

        public TutorialOverlay(Action onFinished)
        {
            _onFinished = onFinished ?? throw new ArgumentNullException(nameof(onFinished));
        }

        public bool IsRunning
        {
            get { return _flow != null && !_flow.IsFinished; }
        }

        /// <summary>The step the player is on, for analytics once it exists in M3.</summary>
        public TutorialStep? CurrentStep
        {
            get { return _flow?.Current; }
        }

        /// <summary>Rebinds to a fresh tree. Safe to call repeatedly, like every other screen.</summary>
        public void Bind(VisualElement appRoot)
        {
            _root = appRoot.Q<VisualElement>("tutorial");
            if (_root == null)
            {
                Debug.LogWarning("[UI] Tutorial overlay element is missing from the document.");
                return;
            }

            Button? next = _root.Q<Button>("tutorial-next");
            if (next != null)
            {
                next.text = UiText.Get("tutorial.next");
                next.clickable = new Clickable(() => Report(TutorialTrigger.Acknowledged));
            }

            Button? skip = _root.Q<Button>("tutorial-skip");
            if (skip != null)
            {
                skip.text = UiText.Get("tutorial.skip");
                skip.clickable = new Clickable(Skip);
            }

            Redraw();
        }

        public void Start()
        {
            _flow = new TutorialFlow();
            Redraw();
        }

        public void Stop()
        {
            _flow = null;
            Redraw();
        }

        /// <summary>
        /// Tells the flow what the player did. Called from the screens for every action the
        /// onboarding might care about; the flow ignores the ones it does not.
        /// </summary>
        public void Report(TutorialTrigger trigger)
        {
            if (_flow == null || _flow.IsFinished) return;

            // Reaching the last step does not close the overlay: the player gets told they are done
            // and taps out of it, rather than having it vanish in the middle of a match.
            _flow.Advance(trigger);
            Redraw();
        }

        private void Skip()
        {
            _flow?.Skip();
            Finish();
        }

        private void Finish()
        {
            _flow = null;
            Redraw();
            _onFinished();
        }

        private void Redraw()
        {
            if (_root == null) return;

            bool visible = _flow != null;
            _root.EnableInClassList("tutorial--visible", visible);

            if (!visible) return;

            Label? text = _root.Q<Label>("tutorial-text");
            if (text != null) text.text = UiText.Get(_flow!.LocalizationKey);

            // Only the opening and closing steps have something to tap. Everything in between is
            // driven by playing, which is what keeps this to a minute and a half instead of a
            // sequence of dialogs.
            Button? next = _root.Q<Button>("tutorial-next");
            if (next != null)
            {
                next.style.display = _flow.WaitsForAcknowledgement ? DisplayStyle.Flex : DisplayStyle.None;
                next.text = UiText.Get(_flow.IsFinished ? "tutorial.done" : "tutorial.next");

                if (_flow.IsFinished) next.clickable = new Clickable(Finish);
                else next.clickable = new Clickable(() => Report(TutorialTrigger.Acknowledged));
            }

            Button? skip = _root.Q<Button>("tutorial-skip");
            if (skip != null) skip.style.display = _flow.IsFinished ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
