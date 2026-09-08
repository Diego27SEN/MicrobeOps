#nullable enable

using System;
using Armada.Core;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// End of match: result, the numbers, and the two things a player wants next.
    /// <para>
    /// Offline it shows no currency at all, which is not an omission: local and vs-AI matches award
    /// nothing until the server says so, and the client never credits anything on its own.
    /// </para>
    /// </summary>
    public sealed class SummaryScreen : ScreenController
    {
        private readonly Func<LocalMatchDriver?> _driver;
        private readonly Func<string> _viewerId;
        private readonly Action _onRematch;

        private VisualElement? _root;

        public SummaryScreen(Func<LocalMatchDriver?> driver, Func<string> viewerId, Action onRematch)
        {
            _driver = driver;
            _viewerId = viewerId;
            _onRematch = onRematch;
        }

        public override string RootName
        {
            get { return "screen-summary"; }
        }

        protected override void OnBind(VisualElement root)
        {
            _root = root;

            WireButton(root, "summary-rematch", "summary.rematch", _onRematch);
            WireButton(root, "summary-home", "summary.home", () => Router.Show(ScreenId.Home));

            Refresh();
        }

        public override void OnShow()
        {
            Refresh();
        }

        private void Refresh()
        {
            LocalMatchDriver? driver = _driver();
            if (driver == null || _root == null || !driver.IsFinished) return;

            MatchStateView view = driver.ViewFor(_viewerId(), LocalClock.NowUnixMs());

            Label? outcome = _root.Q<Label>("summary-outcome");
            if (outcome != null)
            {
                string key = view.Outcome == MatchOutcome.Won ? "outcome.won"
                    : view.Outcome == MatchOutcome.Lost ? "outcome.lost"
                    : "outcome.cancelled";

                outcome.text = UiText.Get(key);

                outcome.RemoveFromClassList("summary__outcome--won");
                outcome.RemoveFromClassList("summary__outcome--lost");
                outcome.AddToClassList(view.Outcome == MatchOutcome.Won ? "summary__outcome--won" : "summary__outcome--lost");
            }

            // Shots and accuracy come from the tracking board, which is exactly what the player was
            // allowed to see during the match. No extra numbers appear at the end.
            BitBoard shots = BitBoard.FromBase64(view.Opponent.ShotsBase64);
            BitBoard hits = BitBoard.FromBase64(view.Opponent.HitsBase64);

            int shotCount = shots.PopCount;
            int hitCount = hits.PopCount;

            SetStat(_root, "summary-shots", "summary.shots", shotCount.ToString());
            SetStat(_root, "summary-accuracy", "summary.accuracy",
                shotCount > 0 ? Math.Round(hitCount * 100.0 / shotCount) + " %" : "-");
        }

        private static void SetStat(VisualElement root, string elementName, string labelKey, string value)
        {
            VisualElement? container = root.Q<VisualElement>(elementName);
            if (container == null) return;

            container.Clear();

            Label caption = new Label(UiText.Get(labelKey));
            caption.AddToClassList("stat__label");

            Label number = new Label(value);
            number.AddToClassList("stat__value");

            container.Add(caption);
            container.Add(number);
        }
    }
}
