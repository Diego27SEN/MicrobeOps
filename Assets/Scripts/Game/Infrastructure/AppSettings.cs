#nullable enable

using System;
using UnityEngine;

namespace Armada.Game
{
    public enum ColorBlindMode
    {
        Off = 0,
        Protanopia = 1,
        Deuteranopia = 2,
        Tritanopia = 3
    }

    /// <summary>
    /// Local device preferences: volumes, accessibility, vibration.
    /// <para>
    /// These live in PlayerPrefs on purpose and never travel to the server. They are per-device
    /// comfort settings, not progression, so syncing them would only create conflicts between a
    /// phone and a tablet for no benefit.
    /// </para>
    /// <para>
    /// Accessibility is a launch requirement here, not an option screen nobody wires up: the
    /// colourblind palettes, the text scale and reduce-motion all drive real USS classes on the
    /// root, and the grid never distinguishes water from a hit by colour alone.
    /// </para>
    /// </summary>
    public static class AppSettings
    {
        private const string MusicKey = "settings.music";
        private const string SfxKey = "settings.sfx";
        private const string ColorBlindKey = "settings.colorblind";
        private const string TextScaleKey = "settings.text_scale";
        private const string ReduceMotionKey = "settings.reduce_motion";
        private const string HighContrastKey = "settings.high_contrast";
        private const string VibrationKey = "settings.vibration";

        private static bool _loaded;

        private static float _musicVolume = 0.7f;
        private static float _sfxVolume = 1.0f;
        private static ColorBlindMode _colorBlind = ColorBlindMode.Off;
        private static float _textScale = 1.0f;
        private static bool _reduceMotion;
        private static bool _highContrast;
        private static bool _vibration = true;

        /// <summary>Raised whenever anything changes, so the root can restyle itself.</summary>
        public static event Action? Changed;

        static AppSettings()
        {
            StaticStateRegistry.Register(ResetStatics);
        }

        private static void ResetStatics()
        {
            _loaded = false;
            Changed = null;
        }

        public static float MusicVolume
        {
            get { Load(); return _musicVolume; }
            set { Load(); _musicVolume = Mathf.Clamp01(value); Save(MusicKey, _musicVolume); }
        }

        public static float SfxVolume
        {
            get { Load(); return _sfxVolume; }
            set { Load(); _sfxVolume = Mathf.Clamp01(value); Save(SfxKey, _sfxVolume); }
        }

        public static ColorBlindMode ColorBlind
        {
            get { Load(); return _colorBlind; }
            set { Load(); _colorBlind = value; Save(ColorBlindKey, (int)value); }
        }

        /// <summary>100 %, 125 % or 150 %. Layouts must survive all three without clipping.</summary>
        public static float TextScale
        {
            get { Load(); return _textScale; }
            set { Load(); _textScale = Mathf.Clamp(value, 1.0f, 1.5f); Save(TextScaleKey, _textScale); }
        }

        public static bool ReduceMotion
        {
            get { Load(); return _reduceMotion; }
            set { Load(); _reduceMotion = value; Save(ReduceMotionKey, value ? 1 : 0); }
        }

        public static bool HighContrast
        {
            get { Load(); return _highContrast; }
            set { Load(); _highContrast = value; Save(HighContrastKey, value ? 1 : 0); }
        }

        public static bool Vibration
        {
            get { Load(); return _vibration; }
            set { Load(); _vibration = value; Save(VibrationKey, value ? 1 : 0); }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                _musicVolume = PlayerPrefs.GetFloat(MusicKey, _musicVolume);
                _sfxVolume = PlayerPrefs.GetFloat(SfxKey, _sfxVolume);
                _colorBlind = (ColorBlindMode)PlayerPrefs.GetInt(ColorBlindKey, (int)_colorBlind);
                _textScale = PlayerPrefs.GetFloat(TextScaleKey, _textScale);
                _reduceMotion = PlayerPrefs.GetInt(ReduceMotionKey, 0) == 1;
                _highContrast = PlayerPrefs.GetInt(HighContrastKey, 0) == 1;
                _vibration = PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            }
            catch (Exception exception)
            {
                // Corrupt or unavailable prefs fall back to the defaults rather than blocking boot.
                Debug.LogWarning("[AppSettings] Falling back to defaults: " + exception.Message);
            }
        }

        private static void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void Save(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>USS class applied to the root for the current colourblind palette, or null for none.</summary>
        public static string? ColorBlindUssClass
        {
            get
            {
                switch (ColorBlind)
                {
                    case ColorBlindMode.Protanopia: return "theme--protanopia";
                    case ColorBlindMode.Deuteranopia: return "theme--deuteranopia";
                    case ColorBlindMode.Tritanopia: return "theme--tritanopia";
                    default: return null;
                }
            }
        }

        public static readonly string[] AllColorBlindUssClasses =
        {
            "theme--protanopia",
            "theme--deuteranopia",
            "theme--tritanopia"
        };
    }
}
