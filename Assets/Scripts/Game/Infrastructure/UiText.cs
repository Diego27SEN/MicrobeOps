#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Armada.Game
{
    /// <summary>Where localized strings come from. The seam that lets screens be written before the backing store exists.</summary>
    public interface ILocalizedStringProvider
    {
        bool TryGet(string key, out string value);
    }

    /// <summary>
    /// Every player-facing string in the game resolves through here, by key. Screens never hold
    /// text; they hold keys.
    /// <para>
    /// The provider is swapped for Unity Localization in M3. Until then a small embedded table
    /// stands in, so the screens can be built and reviewed now against their final shape. That
    /// table is scaffolding with an expiry date, not a second <see cref="BootTexts"/>: it is
    /// registered as technical debt and the screens do not change when it goes away.
    /// </para>
    /// <para>
    /// Fallback is hard and silent-safe: requested language, then English, then the key itself. A
    /// missing translation shows an ugly key, never an exception and never a blank screen.
    /// </para>
    /// </summary>
    public static class UiText
    {
        private static ILocalizedStringProvider? _provider;

        static UiText()
        {
            StaticStateRegistry.Register(() => _provider = null);
        }

        /// <summary>Installs the real provider. Called once at boot, and again by tests.</summary>
        public static void SetProvider(ILocalizedStringProvider? provider)
        {
            _provider = provider;
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            try
            {
                if (_provider != null && _provider.TryGet(key, out string value)) return value;
            }
            catch (Exception exception)
            {
                // A localization backend that throws must not take a screen down with it.
                Debug.LogWarning("[UiText] Provider threw for '" + key + "': " + exception.Message);
            }

            return EmbeddedStrings.Get(key);
        }

        /// <summary>Resolves an <c>ERR_CODE|p1|p2</c> payload against <c>error.&lt;CODE&gt;</c>.</summary>
        public static string Error(string formattedErrorCode)
        {
            if (string.IsNullOrEmpty(formattedErrorCode)) return Get("error.ERR_INTERNAL");

            string[] parts = formattedErrorCode.Split('|');
            string template = Get("error." + parts[0]);

            for (int i = 1; i < parts.Length; i++)
            {
                template = template.Replace("{" + (i - 1) + "}", parts[i]);
            }

            return template;
        }
    }

    /// <summary>
    /// Temporary stand-in for the StringTables that arrive with Unity Localization in M3.
    /// Spanish is neutral Latin American, per the project directives.
    /// </summary>
    internal static class EmbeddedStrings
    {
        private static readonly Dictionary<string, string> Spanish = new Dictionary<string, string>
        {
            { "app.title", "ARMADA" },
            { "app.tagline", "Estrategia naval por cuadrícula" },
            { "common.back", "Volver" },
            { "home.play", "Jugar" },
            { "home.settings", "Ajustes" },
            { "home.quit", "Salir" },
            { "mode.title", "Elegí un modo" },
            { "mode.vs_ai", "Contra la máquina" },
            { "mode.local", "Dos jugadores" },
            { "mode.online", "En línea" },
            { "mode.online_unavailable", "Disponible próximamente." },
            { "mode.back", "Volver" },
            { "difficulty.easy", "Fácil" },
            { "difficulty.medium", "Normal" },
            { "difficulty.hard", "Difícil" },
            { "difficulty.adaptive", "Adaptativa" },
            { "board.blitz", "Rápido 8×8" },
            { "board.classic", "Clásico 10×10" },
            { "board.admiral", "Almirante 12×12" },
            { "placement.title", "Desplegá tu flota" },
            { "placement.random", "Aleatorio" },
            { "placement.rotate", "Rotar" },
            { "placement.orientation.horizontal", "Horizontal" },
            { "placement.orientation.vertical", "Vertical" },
            { "placement.confirm", "Listo" },
            { "placement.hint", "Elegí una casilla para ver dónde encaja el barco y tocá de nuevo para confirmarlo. Para rotar: clic derecho, rueda del mouse, tecla R o el botón." },
            { "placement.remaining", "Faltan {0} barcos" },
            { "match.your_turn", "Tu turno" },
            { "match.opponent_turn", "Turno del rival" },
            { "match.your_fleet", "Tu flota" },
            { "match.enemy_waters", "Aguas enemigas" },
            { "match.fire", "Disparar" },
            { "match.forfeit", "Rendirse" },
            { "match.pass_device", "Pasale el dispositivo a {0}" },
            { "match.shots_required", "Elegí {0} casillas" },
            { "match.double_tap_to_fire", "Tocá dos veces una casilla para disparar" },
            { "match.fleet_sunk", "{0}/{1} hundidos" },
            { "outcome.won", "¡Victoria!" },
            { "outcome.lost", "Derrota" },
            { "outcome.cancelled", "Partida cancelada" },
            { "summary.shots", "Disparos" },
            { "summary.accuracy", "Precisión" },
            { "summary.rematch", "Revancha" },
            { "summary.home", "Al inicio" },
            { "settings.title", "Ajustes" },
            { "settings.music", "Música" },
            { "settings.sfx", "Efectos" },
            { "settings.colorblind", "Modo daltónico" },
            { "settings.colorblind.off", "Desactivado" },
            { "settings.colorblind.protanopia", "Protanopía" },
            { "settings.colorblind.deuteranopia", "Deuteranopía" },
            { "settings.colorblind.tritanopia", "Tritanopía" },
            { "settings.text_scale", "Tamaño de texto" },
            { "settings.reduce_motion", "Reducir movimiento" },
            { "settings.high_contrast", "Alto contraste" },
            { "settings.vibration", "Vibración" },
            { "settings.back", "Volver" },
            { "ship.Carrier", "Portaaviones" },
            { "ship.Battleship", "Acorazado" },
            { "ship.Cruiser", "Crucero" },
            { "ship.Submarine", "Submarino" },
            { "ship.Destroyer", "Destructor" },
            { "ship.PatrolBoat", "Patrullera" },
            { "shot.miss", "Agua" },
            { "shot.hit", "Tocado" },
            { "shot.sunk", "Hundido" },
            { "opponent.ai", "Máquina" },
            { "mode.tutorial", "Cómo se juega" },
            { "tutorial.next", "Seguir" },
            { "tutorial.skip", "Saltar" },
            { "tutorial.done", "Listo" },
            { "tutorial.step.Welcome", "Tu flota va en la cuadrícula de abajo. El rival despliega la suya en secreto y ninguno de los dos ve el tablero del otro." },
            { "tutorial.step.PlaceFirstShip", "Elegí una casilla para ver dónde entra el primer barco y tocá de nuevo para confirmarlo. Con Rotar cambiás la dirección." },
            { "tutorial.step.PlaceRemainingShips", "Colocá el resto de la flota. Si preferís, Aleatorio la acomoda entera por vos." },
            { "tutorial.step.ConfirmFleet", "Tu flota está completa. Tocá Listo para empezar la partida." },
            { "tutorial.step.FireFirstShot", "Arriba están las aguas enemigas. Elegí una casilla y dispará: tocá dos veces o usá el botón." },
            { "tutorial.step.ScoreFirstHit", "Agua. Seguí buscando: cada disparo descarta una casilla y te acerca a la flota." },
            { "tutorial.step.SinkFirstShip", "¡Tocado! Ahora probá las casillas de al lado: los barcos ocupan casillas seguidas." },
            { "tutorial.step.Finished", "Hundiste tu primer barco. Eso es todo: hundí la flota entera antes de que el rival hunda la tuya." },
            { "error.ERR_CELL_ALREADY_TARGETED", "Ya disparaste a esa casilla." },
            { "error.ERR_NOT_YOUR_TURN", "No es tu turno." },
            { "error.ERR_OUT_OF_BOUNDS", "Esa casilla está fuera del tablero." },
            { "error.ERR_INVALID_PLACEMENT", "Ese despliegue no es válido." },
            { "error.ERR_FLEET_MISMATCH", "Falta desplegar barcos." },
            { "error.ERR_SHOT_COUNT_INVALID", "Tenés que elegir {0} casillas." },
            { "error.ERR_WRONG_PHASE", "Esa acción no corresponde ahora." },
            { "error.ERR_MATCH_FINISHED", "La partida ya terminó." },
            { "error.ERR_STALE_ACTION", "Sincronizando…" },
            { "error.ERR_INTERNAL", "Algo salió mal." }
        };

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "app.title", "ARMADA" },
            { "app.tagline", "Grid naval strategy" },
            { "common.back", "Back" },
            { "home.play", "Play" },
            { "home.settings", "Settings" },
            { "home.quit", "Quit" },
            { "mode.title", "Pick a mode" },
            { "mode.vs_ai", "Against the computer" },
            { "mode.local", "Two players" },
            { "mode.online", "Online" },
            { "mode.online_unavailable", "Coming soon." },
            { "mode.back", "Back" },
            { "difficulty.easy", "Easy" },
            { "difficulty.medium", "Normal" },
            { "difficulty.hard", "Hard" },
            { "difficulty.adaptive", "Adaptive" },
            { "board.blitz", "Blitz 8×8" },
            { "board.classic", "Classic 10×10" },
            { "board.admiral", "Admiral 12×12" },
            { "placement.title", "Deploy your fleet" },
            { "placement.random", "Random" },
            { "placement.rotate", "Rotate" },
            { "placement.orientation.horizontal", "Horizontal" },
            { "placement.orientation.vertical", "Vertical" },
            { "placement.confirm", "Ready" },
            { "placement.hint", "Pick a cell to see where the ship fits, then tap again to confirm it. To rotate: right click, mouse wheel, the R key or the button." },
            { "placement.remaining", "{0} ships left" },
            { "match.your_turn", "Your turn" },
            { "match.opponent_turn", "Opponent's turn" },
            { "match.your_fleet", "Your fleet" },
            { "match.enemy_waters", "Enemy waters" },
            { "match.fire", "Fire" },
            { "match.forfeit", "Forfeit" },
            { "match.pass_device", "Pass the device to {0}" },
            { "match.shots_required", "Pick {0} cells" },
            { "match.double_tap_to_fire", "Double tap a cell to fire" },
            { "match.fleet_sunk", "{0}/{1} sunk" },
            { "outcome.won", "Victory!" },
            { "outcome.lost", "Defeat" },
            { "outcome.cancelled", "Match cancelled" },
            { "summary.shots", "Shots" },
            { "summary.accuracy", "Accuracy" },
            { "summary.rematch", "Rematch" },
            { "summary.home", "Home" },
            { "settings.title", "Settings" },
            { "settings.music", "Music" },
            { "settings.sfx", "Effects" },
            { "settings.colorblind", "Colourblind mode" },
            { "settings.colorblind.off", "Off" },
            { "settings.colorblind.protanopia", "Protanopia" },
            { "settings.colorblind.deuteranopia", "Deuteranopia" },
            { "settings.colorblind.tritanopia", "Tritanopia" },
            { "settings.text_scale", "Text size" },
            { "settings.reduce_motion", "Reduce motion" },
            { "settings.high_contrast", "High contrast" },
            { "settings.vibration", "Vibration" },
            { "settings.back", "Back" },
            { "ship.Carrier", "Carrier" },
            { "ship.Battleship", "Battleship" },
            { "ship.Cruiser", "Cruiser" },
            { "ship.Submarine", "Submarine" },
            { "ship.Destroyer", "Destroyer" },
            { "ship.PatrolBoat", "Patrol boat" },
            { "shot.miss", "Miss" },
            { "shot.hit", "Hit" },
            { "shot.sunk", "Sunk" },
            { "opponent.ai", "Computer" },
            { "mode.tutorial", "How to play" },
            { "tutorial.next", "Next" },
            { "tutorial.skip", "Skip" },
            { "tutorial.done", "Done" },
            { "tutorial.step.Welcome", "Your fleet goes on the grid below. Your opponent deploys theirs in secret, and neither of you can see the other's board." },
            { "tutorial.step.PlaceFirstShip", "Pick a cell to see where the first ship fits, then tap again to confirm it. Rotate changes the direction." },
            { "tutorial.step.PlaceRemainingShips", "Place the rest of the fleet. If you prefer, Random lays out the whole thing for you." },
            { "tutorial.step.ConfirmFleet", "Your fleet is complete. Tap Ready to start the match." },
            { "tutorial.step.FireFirstShot", "Enemy waters are up top. Pick a cell and fire: double tap it, or use the button." },
            { "tutorial.step.ScoreFirstHit", "A miss. Keep looking: every shot rules out a cell and narrows the fleet down." },
            { "tutorial.step.SinkFirstShip", "A hit! Now try the cells next to it: ships occupy consecutive cells." },
            { "tutorial.step.Finished", "You sank your first ship. That is all of it: sink the whole enemy fleet before they sink yours." },
            { "error.ERR_CELL_ALREADY_TARGETED", "You already fired at that cell." },
            { "error.ERR_NOT_YOUR_TURN", "It is not your turn." },
            { "error.ERR_OUT_OF_BOUNDS", "That cell is off the board." },
            { "error.ERR_INVALID_PLACEMENT", "That deployment is not valid." },
            { "error.ERR_FLEET_MISMATCH", "Some ships are still undeployed." },
            { "error.ERR_SHOT_COUNT_INVALID", "You need to pick {0} cells." },
            { "error.ERR_WRONG_PHASE", "That action does not apply right now." },
            { "error.ERR_MATCH_FINISHED", "The match is already over." },
            { "error.ERR_STALE_ACTION", "Syncing…" },
            { "error.ERR_INTERNAL", "Something went wrong." }
        };

        public static string Get(string key)
        {
            Dictionary<string, string> table = Application.systemLanguage == SystemLanguage.Spanish ? Spanish : English;

            if (table.TryGetValue(key, out string? value)) return value;
            if (English.TryGetValue(key, out string? fallback)) return fallback;

            return key;
        }
    }
}
