#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Armada.Game
{
    /// <summary>
    /// The one deliberate exception to "every player-facing string comes from Localization".
    /// <para>
    /// These are the strings the loading screen needs before Localization itself has initialised,
    /// so they cannot come from it. Two languages only, Spanish and English, and the list stays
    /// short: anything that can wait for Localization must wait for it.
    /// </para>
    /// <para>
    /// Adding a string here is a decision, not a shortcut. If it is not on screen before the
    /// Localization system is ready, it does not belong.
    /// </para>
    /// </summary>
    public static class BootTexts
    {
        private static readonly Dictionary<string, string> Spanish = new Dictionary<string, string>
        {
            { "boot.loading", "Cargando…" },
            { "boot.connecting", "Conectando…" },
            { "boot.offline", "Sin conexión. Podés jugar contra la máquina." },
            { "boot.update_required", "Hay una versión nueva disponible." },
            { "boot.maintenance", "Estamos en mantenimiento. Volvé en un rato." },
            { "boot.error", "Algo salió mal al iniciar." },
            { "boot.retry", "Reintentar" }
        };

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "boot.loading", "Loading…" },
            { "boot.connecting", "Connecting…" },
            { "boot.offline", "No connection. You can still play offline." },
            { "boot.update_required", "A new version is available." },
            { "boot.maintenance", "We are under maintenance. Please come back later." },
            { "boot.error", "Something went wrong while starting up." },
            { "boot.retry", "Retry" }
        };

        /// <summary>
        /// Resolves a boot key against the device language, falling back to English and then to the
        /// key itself. It never throws and never returns null: a broken loading screen must still
        /// be a loading screen.
        /// </summary>
        public static string Get(string key)
        {
            Dictionary<string, string> table = Application.systemLanguage == SystemLanguage.Spanish ? Spanish : English;

            if (table.TryGetValue(key, out string? value)) return value;
            if (English.TryGetValue(key, out string? fallback)) return fallback;

            return key;
        }
    }
}
