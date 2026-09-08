#nullable enable

using System;
using Armada.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>Home. Two taps from here to shooting, which is the whole product principle.</summary>
    public sealed class HomeScreen : ScreenController
    {
        private readonly Action _startTutorial;

        public HomeScreen(Action startTutorial)
        {
            _startTutorial = startTutorial ?? throw new ArgumentNullException(nameof(startTutorial));
        }

        public override string RootName
        {
            get { return "screen-home"; }
        }

        protected override void OnBind(VisualElement root)
        {
            SetText(root, "home-title", "app.title");
            SetText(root, "home-tagline", "app.tagline");

            WireButton(root, "home-play", "home.play", () => Router.Show(ScreenId.ModeSelect));

            // Offered, never imposed. It sits in the menu as something the player chooses, which
            // also means it stays available to someone coming back after a month rather than being
            // a one-shot on first launch.
            WireButton(root, "home-tutorial", "mode.tutorial", _startTutorial);

            WireButton(root, "home-settings", "home.settings", () => Router.Show(ScreenId.Settings));

            Button? quit = root.Q<Button>("home-quit");
            if (quit != null)
            {
                // Mobile stores dislike an explicit quit button; desktop players expect one.
                bool showQuit = Application.platform == RuntimePlatform.WindowsPlayer
                    || Application.platform == RuntimePlatform.OSXPlayer
                    || Application.platform == RuntimePlatform.LinuxPlayer
                    || Application.isEditor;

                quit.style.display = showQuit ? DisplayStyle.Flex : DisplayStyle.None;
                quit.text = UiText.Get("home.quit");
                quit.clickable = new Clickable(Application.Quit);
            }
        }
    }

    /// <summary>Mode, board and difficulty in one screen, so starting a match stays a single decision.</summary>
    public sealed class ModeSelectScreen : ScreenController
    {
        private readonly Action<MatchMode, string, AiDifficulty?> _startMatch;

        private string _boardId = BoardConfig.ClassicId;
        private AiDifficulty _difficulty = AiDifficulty.Medium;

        public ModeSelectScreen(Action<MatchMode, string, AiDifficulty?> startMatch)
        {
            _startMatch = startMatch ?? throw new ArgumentNullException(nameof(startMatch));
        }

        public override string RootName
        {
            get { return "screen-mode"; }
        }

        protected override void OnBind(VisualElement root)
        {
            SetText(root, "mode-title", "mode.title");

            WireRadioGroup(root, "mode-boards", new[]
            {
                (BoardConfig.BlitzId, "board.blitz"),
                (BoardConfig.ClassicId, "board.classic"),
                (BoardConfig.AdmiralId, "board.admiral")
            }, _boardId, id => _boardId = id);

            WireRadioGroup(root, "mode-difficulties", new[]
            {
                (AiDifficulty.Easy.ToString(), "difficulty.easy"),
                (AiDifficulty.Medium.ToString(), "difficulty.medium"),
                (AiDifficulty.Hard.ToString(), "difficulty.hard"),
                (AiDifficulty.Adaptive.ToString(), "difficulty.adaptive")
            }, _difficulty.ToString(), id => _difficulty = (AiDifficulty)Enum.Parse(typeof(AiDifficulty), id));

            WireButton(root, "mode-vs-ai", "mode.vs_ai", () => _startMatch(MatchMode.VsAi, _boardId, _difficulty));
            WireButton(root, "mode-local", "mode.local", () => _startMatch(MatchMode.LocalTwoPlayer, _boardId, null));
            WireButton(root, "mode-back", "mode.back", () => Router.Show(ScreenId.Home));

            // Online exists as a visible, disabled affordance rather than a missing button: players
            // should know the mode is coming, and the same element becomes live in M4.
            Button? online = root.Q<Button>("mode-online");
            if (online != null)
            {
                online.text = UiText.Get("mode.online");
                online.SetEnabled(false);
                online.tooltip = UiText.Get("mode.online_unavailable");
            }
        }

        /// <summary>
        /// Builds a set of mutually exclusive choice buttons. Rebuilt from scratch on every bind so
        /// rebinding cannot accumulate duplicates.
        /// </summary>
        private static void WireRadioGroup(
            VisualElement root,
            string containerName,
            (string Id, string LabelKey)[] options,
            string selectedId,
            Action<string> onSelected)
        {
            VisualElement? container = root.Q<VisualElement>(containerName);
            if (container == null)
            {
                Debug.LogWarning("[UI] Radio group '" + containerName + "' not found.");
                return;
            }

            container.Clear();

            foreach ((string id, string labelKey) in options)
            {
                Button button = new Button { text = UiText.Get(labelKey), name = containerName + "-" + id };
                button.AddToClassList("chip");
                if (id == selectedId) button.AddToClassList("chip--selected");

                string captured = id;
                button.clickable = new Clickable(() =>
                {
                    foreach (VisualElement sibling in container.Children()) sibling.RemoveFromClassList("chip--selected");
                    button.AddToClassList("chip--selected");
                    onSelected(captured);
                });

                container.Add(button);
            }
        }
    }

    /// <summary>
    /// Settings. Accessibility lives here and actually drives the root's USS classes: the toggles
    /// are wired to real behaviour, not to preferences nobody reads.
    /// </summary>
    public sealed class SettingsScreen : ScreenController
    {
        private readonly Action _applyTheme;

        public SettingsScreen(Action applyTheme)
        {
            _applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));
        }

        public override string RootName
        {
            get { return "screen-settings"; }
        }

        protected override void OnBind(VisualElement root)
        {
            SetText(root, "settings-title", "settings.title");

            WireSlider(root, "settings-music", "settings.music", AppSettings.MusicVolume, value => AppSettings.MusicVolume = value);
            WireSlider(root, "settings-sfx", "settings.sfx", AppSettings.SfxVolume, value => AppSettings.SfxVolume = value);

            WireToggle(root, "settings-reduce-motion", "settings.reduce_motion", AppSettings.ReduceMotion, value =>
            {
                AppSettings.ReduceMotion = value;
                _applyTheme();
            });

            WireToggle(root, "settings-high-contrast", "settings.high_contrast", AppSettings.HighContrast, value =>
            {
                AppSettings.HighContrast = value;
                _applyTheme();
            });

            WireToggle(root, "settings-vibration", "settings.vibration", AppSettings.Vibration, value => AppSettings.Vibration = value);

            WireColorBlind(root);
            WireTextScale(root);

            WireButton(root, "settings-back", "settings.back", () => Router.Show(ScreenId.Home));
        }

        private void WireColorBlind(VisualElement root)
        {
            VisualElement? container = root.Q<VisualElement>("settings-colorblind");
            if (container == null) return;

            container.Clear();

            Label caption = new Label(UiText.Get("settings.colorblind"));
            caption.AddToClassList("field__label");
            container.Add(caption);

            VisualElement choices = new VisualElement();
            choices.AddToClassList("field__choices");

            (ColorBlindMode Mode, string Key)[] modes =
            {
                (ColorBlindMode.Off, "settings.colorblind.off"),
                (ColorBlindMode.Protanopia, "settings.colorblind.protanopia"),
                (ColorBlindMode.Deuteranopia, "settings.colorblind.deuteranopia"),
                (ColorBlindMode.Tritanopia, "settings.colorblind.tritanopia")
            };

            foreach ((ColorBlindMode mode, string key) in modes)
            {
                Button button = new Button { text = UiText.Get(key) };
                button.AddToClassList("chip");
                if (AppSettings.ColorBlind == mode) button.AddToClassList("chip--selected");

                ColorBlindMode captured = mode;
                button.clickable = new Clickable(() =>
                {
                    AppSettings.ColorBlind = captured;
                    foreach (VisualElement sibling in choices.Children()) sibling.RemoveFromClassList("chip--selected");
                    button.AddToClassList("chip--selected");
                    _applyTheme();
                });

                choices.Add(button);
            }

            container.Add(choices);
        }

        private void WireTextScale(VisualElement root)
        {
            VisualElement? container = root.Q<VisualElement>("settings-text-scale");
            if (container == null) return;

            container.Clear();

            Label caption = new Label(UiText.Get("settings.text_scale"));
            caption.AddToClassList("field__label");
            container.Add(caption);

            VisualElement choices = new VisualElement();
            choices.AddToClassList("field__choices");

            float[] scales = { 1.0f, 1.25f, 1.5f };
            foreach (float scale in scales)
            {
                Button button = new Button { text = Mathf.RoundToInt(scale * 100f) + " %" };
                button.AddToClassList("chip");
                if (Mathf.Approximately(AppSettings.TextScale, scale)) button.AddToClassList("chip--selected");

                float captured = scale;
                button.clickable = new Clickable(() =>
                {
                    AppSettings.TextScale = captured;
                    foreach (VisualElement sibling in choices.Children()) sibling.RemoveFromClassList("chip--selected");
                    button.AddToClassList("chip--selected");
                    _applyTheme();
                });

                choices.Add(button);
            }

            container.Add(choices);
        }

        // Both wiring helpers stash the handler they registered in the element's userData and
        // unregister it before adding a new one. Buttons get away with reassigning `clickable`;
        // fields do not, and binding the same tree twice would otherwise fire every change handler
        // twice. Rebinding happening more than once is the normal case here, not a bug to avoid.
        private static void WireSlider(VisualElement root, string name, string labelKey, float value, Action<float> onChanged)
        {
            Slider? slider = root.Q<Slider>(name);
            if (slider == null) return;

            slider.label = UiText.Get(labelKey);
            slider.lowValue = 0f;
            slider.highValue = 1f;
            slider.SetValueWithoutNotify(value);

            if (slider.userData is EventCallback<ChangeEvent<float>> previous)
            {
                slider.UnregisterValueChangedCallback(previous);
            }

            EventCallback<ChangeEvent<float>> handler = evt => onChanged(evt.newValue);
            slider.userData = handler;
            slider.RegisterValueChangedCallback(handler);
        }

        private static void WireToggle(VisualElement root, string name, string labelKey, bool value, Action<bool> onChanged)
        {
            Toggle? toggle = root.Q<Toggle>(name);
            if (toggle == null) return;

            toggle.label = UiText.Get(labelKey);
            toggle.SetValueWithoutNotify(value);

            if (toggle.userData is EventCallback<ChangeEvent<bool>> previous)
            {
                toggle.UnregisterValueChangedCallback(previous);
            }

            EventCallback<ChangeEvent<bool>> handler = evt => onChanged(evt.newValue);
            toggle.userData = handler;
            toggle.RegisterValueChangedCallback(handler);
        }
    }
}
