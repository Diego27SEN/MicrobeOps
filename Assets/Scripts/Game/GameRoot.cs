#nullable enable

using System;
using Armada.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// The single entry point of the game, living in the single persistent scene.
    /// <para>
    /// It owns the router, the screens and the current offline match, and it re-wires everything
    /// whenever the UI document rebuilds. There is no scene loading anywhere in this project: one
    /// scene, one document, panels toggled by the router.
    /// </para>
    /// <para>
    /// Nothing here talks to the network. M2 is the offline game, and it has to stay fully playable
    /// with the radio off - that is a product principle, not a phase.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UiPanelHost))]
    public sealed class GameRoot : MonoBehaviour
    {
        private const string LocalPlayerId = "local-player-1";
        private const string LocalGuestId = "local-player-2";

        private UiPanelHost _host = null!;
        private ScreenRouter _router = null!;
        private TutorialOverlay _tutorial = null!;
        private GameConfig _config = null!;

        private LocalMatchDriver? _driver;
        private MatchMode _mode = MatchMode.VsAi;
        private string _boardId = BoardConfig.ClassicId;
        private AiDifficulty? _difficulty = AiDifficulty.Medium;

        /// <summary>Whose turn it is to look at the screen. Only ever differs in pass-and-play.</summary>
        private string _activeViewer = LocalPlayerId;

        private void Awake()
        {
            _host = GetComponent<UiPanelHost>();

            // The embedded fallback, straight from the design doc's numbers. In M3 this is replaced
            // by whatever GetGameConfig returns, with this as the offline fallback.
            _config = DefaultGameConfig.Create();

            BuildRouter();
            _host.RegisterRebuildCallback(Rewire);
        }

        private void OnDestroy()
        {
            _host.UnregisterRebuildCallback(Rewire);
        }

        private void BuildRouter()
        {
            _router = new ScreenRouter();
            _tutorial = new TutorialOverlay(OnTutorialFinished);

            _router.Register(ScreenId.Home, new HomeScreen(StartTutorial));
            _router.Register(ScreenId.ModeSelect, new ModeSelectScreen(StartLocalMatch));
            _router.Register(ScreenId.Placement, new PlacementScreen(() => _driver, () => _activeViewer, OnPlacementSubmitted, ShowToast, ReportToTutorial));
            _router.Register(ScreenId.Match, new MatchScreen(() => _driver, () => _activeViewer, OnMatchFinished, ShowToast, ReportToTutorial));
            _router.Register(ScreenId.Summary, new SummaryScreen(() => _driver, () => _activeViewer, OnRematch));
            _router.Register(ScreenId.Settings, new SettingsScreen(ApplyTheme));
        }

        /// <summary>
        /// Re-binds every screen to a freshly built tree. Called on enable and on every document
        /// rebuild, so it has to be safe to run repeatedly - which is why the screens query their
        /// elements fresh each time instead of caching them across calls.
        /// </summary>
        private void Rewire()
        {
            // IsReady, not IsAlive: the root exists before the UXML is cloned into it, and binding
            // against an empty tree finds no panels at all.
            if (!_host.IsReady) return;

            VisualElement root = _host.Root!;
            _router.Bind(root);
            _tutorial.Bind(root);
            ApplyTheme();
        }

        /// <summary>
        /// Starts the onboarding from the menu. It runs as a normal Easy match against the AI
        /// rather than a scripted sandbox: what the player learns is the real game, and there is no
        /// second code path to keep working.
        /// </summary>
        private void StartTutorial()
        {
            _mode = MatchMode.VsAi;
            _boardId = BoardConfig.ClassicId;
            _difficulty = AiDifficulty.Easy;

            CreateDriver();
            _activeViewer = LocalPlayerId;

            _tutorial.Start();
            _router.Show(ScreenId.Placement);
        }

        private void ReportToTutorial(TutorialTrigger trigger)
        {
            _tutorial.Report(trigger);
        }

        private void OnTutorialFinished()
        {
            // Nothing to remember. The onboarding is a menu option the player picks, so there is no
            // "already seen" state to track - and a preference nobody reads is worse than none.
        }

        /// <summary>
        /// Pushes the accessibility settings onto the root as USS classes. Every colour in the game
        /// comes from a variable in <c>:root</c>, so swapping a class here restyles the whole game
        /// without any screen knowing it happened - which is what makes cosmetic themes and the
        /// colourblind palettes the same mechanism.
        /// </summary>
        private void ApplyTheme()
        {
            if (!_host.IsAlive) return;

            VisualElement root = _host.Root!;

            for (int i = 0; i < AppSettings.AllColorBlindUssClasses.Length; i++)
            {
                root.RemoveFromClassList(AppSettings.AllColorBlindUssClasses[i]);
            }

            string? palette = AppSettings.ColorBlindUssClass;
            if (palette != null) root.AddToClassList(palette);

            root.EnableInClassList("theme--high-contrast", AppSettings.HighContrast);
            root.EnableInClassList("theme--reduce-motion", AppSettings.ReduceMotion);

            root.RemoveFromClassList("text-scale--125");
            root.RemoveFromClassList("text-scale--150");

            if (Mathf.Approximately(AppSettings.TextScale, 1.25f)) root.AddToClassList("text-scale--125");
            else if (Mathf.Approximately(AppSettings.TextScale, 1.5f)) root.AddToClassList("text-scale--150");
        }

        private void StartLocalMatch(MatchMode mode, string boardId, AiDifficulty? difficulty)
        {
            _mode = mode;
            _boardId = boardId;
            _difficulty = difficulty;

            CreateDriver();
            _activeViewer = LocalPlayerId;
            _router.Show(ScreenId.Placement);
        }

        private void CreateDriver()
        {
            // A fresh seed per match, taken once, here. Core never reaches for ambient randomness;
            // this is the one place the client decides what the seed is.
            ulong seed = (ulong)Environment.TickCount ^ ((ulong)DateTime.UtcNow.Ticks << 16);

            _driver = new LocalMatchDriver(
                _config,
                Guid.NewGuid().ToString("N"),
                _boardId,
                RuleFlags.ClassicId,
                _mode,
                _difficulty,
                LocalPlayerId,
                _mode == MatchMode.LocalTwoPlayer ? LocalGuestId : null,
                new SeededRandom(seed),
                LocalClock.NowUnixMs());
        }

        private void OnPlacementSubmitted()
        {
            if (_driver == null) return;

            // Pass-and-play: the second player deploys on the same device before anyone shoots.
            if (_mode == MatchMode.LocalTwoPlayer && _driver.Phase == MatchPhase.Placement)
            {
                _activeViewer = LocalGuestId;
                ShowToast(UiText.Get("match.pass_device").Replace("{0}", "2"));
                _router.Get<PlacementScreen>(ScreenId.Placement).OnShow();
                return;
            }

            _activeViewer = LocalPlayerId;
            _router.Show(ScreenId.Match);

            // The AI may hold the first move.
            MatchScreen match = _router.Get<MatchScreen>(ScreenId.Match);
            match.Refresh();
        }

        private void OnMatchFinished()
        {
            _router.Show(ScreenId.Summary);
        }

        private void OnRematch()
        {
            CreateDriver();
            _activeViewer = LocalPlayerId;
            _router.Show(ScreenId.Placement);
        }

        /// <summary>
        /// Transient message strip. Errors reach the player as resolved Localization text, never as
        /// an <c>ERR_*</c> code: the codes are for logs and tests.
        /// </summary>
        private void ShowToast(string message)
        {
            if (!_host.IsAlive) return;

            Label? toast = _host.Root!.Q<Label>("toast");
            if (toast == null)
            {
                Debug.Log("[Toast] " + message);
                return;
            }

            toast.text = message;
            toast.AddToClassList("toast--visible");

            toast.schedule.Execute(() =>
            {
                // The panel can be gone by the time this fires, so re-check rather than assume.
                if (!_host.IsAlive) return;
                toast.RemoveFromClassList("toast--visible");
            }).StartingIn(2500);
        }
    }
}
