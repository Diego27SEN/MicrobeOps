# CLAUDE.md — Directivas del proyecto ARMADA

> Juego de combate naval por cuadrícula (Android / iOS / PC) sobre **Unity 6.3 LTS + Unity Gaming Services**, con **servidor autoritativo desde el minuto cero**.
>
> **Fuente de verdad única**: [`Docs/ARMADA-Documento-Tecnico-Produccion.md`](Docs/ARMADA-Documento-Tecnico-Produccion.md).
> Toda cifra de balance, economía o timing sale de ese documento (§4 y §5). **Si falta un número, se pide — no se improvisa.**

---

## 0. Antes de tocar código

1. Leé §18 (directivas), §7 (contrato de API) y el milestone actual de §16 del documento técnico.
2. El contrato de §7 está **congelado**. Cambiar una firma pública de `Armada.Core` o de un endpoint de Cloud Code exige subir `GameProtocol.Version` en el **mismo commit**, en cliente y módulo a la vez.
3. Milestone actual: **M0 — Fundación**. Ver `CHANGELOG-dev.md` para el estado real.

---

## 1. Idioma

- **Documentación** (GDD, arquitectura, notas, changelog de jugadores): **español neutro latinoamericano** (nada de voseo rioplatense).
- **Código** — clases, métodos, variables, comentarios, commits, ramas, logs, nombres de tests y de eventos de analytics: **inglés, sin excepción**. Nada de español en `.cs` / `.uxml` / `.uss`.
- **Textos del juego**: **siempre** por Localization, nunca hardcodeados.

> Regla práctica: *¿el string termina frente al jugador? → clave de Localization. ¿Termina en consola / log / Editor / archivo generado? → inglés. ¿Es un `ERR_*` / key / id? → no se toca nunca.*

Única excepción de hardcode permitida: `BootTexts` (mini-tabla es/en para la pantalla de carga, §9.4).

---

## 2. Arquitectura — reglas no negociables

1. **El servidor decide, el cliente pinta.** Ninguna regla, recompensa, resultado o información oculta se calcula en el cliente en modo online.
2. **`Armada.Core` es lógica pura**: `noEngineReferences: true`, `netstandard2.1`, `<Nullable>enable</Nullable>`. Sin `UnityEngine`, sin `DateTime.Now`/`DateTime.UtcNow`, sin `System.Random` ambiental, sin `Guid.NewGuid`, sin I/O. El tiempo entra por parámetro (`nowUnixMs`); el azar entra por `IRandom`.
3. **El módulo de Cloud Code compila el mismo código fuente de Core** (`<Compile Include="../../Assets/Scripts/Core/**/*.cs" />`). Reglas e IA son byte-for-byte idénticas en cliente y servidor. **No se duplica lógica de Core en el módulo.**
4. **Una sola fuente de verdad por dato**: cifras → documento técnico → Remote Config; contenido → `Content/ContentDatabase.json` → generador; versión visible → `bundleVersion`.
5. **Los servicios degradan, nunca rompen.** Todo servicio externo va detrás de un wrapper defensivo con fallback local.
6. **Idempotencia y versionado de protocolo desde el diseño**, no como parche (`expectedSequence`, `clientRequestId`, `GameProtocol.Version`).
7. **Nada de estado serializado a mano.** Escenas, PanelSettings, Locales y StringTables se generan con editor scripts idempotentes (`ProjectBootstrap.SetupAll()`). Nunca se edita YAML de Unity a mano.

### Assemblies

| Assembly | Rol | Restricción |
|---|---|---|
| `Armada.Core` | Reglas, despliegue, disparos, IA, Elo, recompensas, proyección | `noEngineReferences: true`, nullable enable, sin tiempo/azar ambiental |
| `Armada.Core.Tests` | EditMode NUnit sobre Core | Sin Unity API, sin mocks |
| `Armada.Game` | Bootstrap, managers, UI Toolkit, servicios UGS, audio, input | Depende de `Armada.Core` |
| `Armada.Game.Tests` | PlayMode: flujo de UI, resiliencia de red | — |
| `Armada.Game.Editor` | Editor scripts idempotentes, Content Designer | Editor-only |

---

## 3. Seguridad — el vector principal es la fuga de información

En este género la única trampa que importa es **ver el tablero rival**.

- El `occupancy` del rival **nunca sale del servidor** hasta `MatchPhase.Finished`.
- `MatchProjection.Project` es la pieza crítica del proyecto. Cualquier cambio en `MatchProjection` o en los DTO de vista (`MatchStateView` y anidados) requiere revisión del rol `backend-security`, que tiene **poder de veto**.
- Cinco tests de proyección obligatorios (§6.3), incluido uno que valida el **JSON serializado**, no el DTO.
- Ningún endpoint de debug que devuelva estado crudo existe en el módulo de producción. Si existe para desarrollo, va detrás de `#if DEBUG_ENV` **y** del entorno `development`.
- Toda compra se valida server-side antes de otorgar nada. El cliente nunca acredita moneda.
- El cliente no lee Remote Config directo: pasa por `GetGameConfig`.
- Handshake de versión (`GameProtocol.Version`) en el boot.

**Lo que explícitamente NO hacemos**: ofuscación de cliente, detección de root/jailbreak, checksums de estado enviados por el cliente, anti-cheat de memoria. Son teatro; el dato secreto no está en el dispositivo.

---

## 4. Convenciones C#

- PascalCase para clases y miembros públicos; camelCase para locales y parámetros; `_camelCase` para campos privados.
- Namespaces con llaves (`namespace Armada.Core { … }`), **no** file-scoped: Unity 6.3 compila C# 9.
- Nombres de test: `Method_Scenario_ExpectedResult` (ej. `FireShot_OnAlreadyTargetedCell_ReturnsError`).
- `#nullable enable` en todos los archivos de Core y del módulo. Los warnings `CS86xx` son **errores** en Core.
- Cloud Code: **un solo módulo**, funciones `[CloudCodeFunction]`, `context` / `gameApiClient` / `pushClient` inyectados vía `ICloudCodeSetup`. Nunca `GameApiClient.Create()` (obsoleto): `config.AddGameApiClient()`.
- Eventos de Analytics en snake_case inglés, registrados en el Event Manager **antes** de emitirse.
- El servidor **nunca** manda prosa: manda `ERR_CODE|p1|p2` y el cliente resuelve contra Localization (`error.<CODIGO>`).

---

## 5. UI / UX

- **UI Toolkit con `UIDocument`** (UXML + USS + controladores C#). **Nunca uGUI ni TextMeshPro.**
- `PanelRenderer` **no existe en 6.3 LTS** (es 6.5+). El acceso al panel se encapsula en `UiPanelHost` para que una futura migración toque un solo archivo.
- **Una sola escena persistente** (`Main`) con paneles, salvo `Splash`. Nada de cargar escenas por pantalla.
- El rewireo de UI debe ser **idempotente**: los callbacks de recarga pueden dispararse más de una vez.
- Colores y tipografías **solo** desde variables USS en `:root`. Ningún hex hardcodeado fuera de ahí — es lo que hace posibles los temas cosméticos y el modo daltónico.
- Velocidad por encima del adorno: **≤ 2 taps** desde el arranque hasta estar disparando.
- Objetivos táctiles ≥ 44 pt. Toda la UI funciona con *tap* y con *mouse + teclado* sin código condicional por plataforma.
- Accesibilidad es **requisito de lanzamiento**, no "nice to have": agua / tocado / hundido nunca se distinguen solo por color.

### Blindaje contra "Enter Play Mode sin domain reload"

1. Todo estado estático mutable se registra en `StaticStateRegistry` y se resetea en `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`.
2. Tras **cada** `await` en código de UI: `if (rootVisualElement == null || !document) return;`
3. El test `Game_HasNoUnregisteredMutableStatics` falla el build si alguien agrega un `static` mutable suelto.
4. `Enter Play Mode Options` activado desde el día 1.

---

## 6. Contenido y assets

- El contenido se edita **solo** con el Content Designer; fuente única `Content/ContentDatabase.json`.
- **Prohibido editar a mano** cualquier archivo generado. Un test de CI compara hashes y falla si divergen.
- Los assets serializados se generan con editor scripts idempotentes agrupados en `Armada.Editor.ProjectBootstrap.SetupAll()`, invocable en batchmode.

---

## 7. Proceso

- Config-as-code para todo lo deployable; versionado en git.
- **Tests del módulo antes de cada deploy, sin excepción.**
- Cada milestone cierra con el trío **QA → changelog → commit atómico**. No se avanza con bloqueantes abiertos.
- **Toda deuda que se acepte se documenta en el momento** en `CHANGELOG-dev.md`, con severidad y condición de cierre. Cero deuda silenciosa.
- Nunca se inventan versiones de paquetes: se verifican contra el registro real (Package Manager / NuGet) antes de fijarlas.
- Ramas: `main` (producción) · `develop` (integración) · `feature/*` · `hotfix/*`.
- Commits: **Conventional Commits**. Tags `v{bundleVersion}`.
- **Nunca push a `main` ni PR sin confirmación explícita del usuario.**
- Dos changelogs: `CHANGELOG-dev.md` (Keep a Changelog, técnico) y `changelog-jugadores.md` (lenguaje de jugador, localizable).
- `bundleVersion` sube en el mismo commit que el cambio que llega a jugadores. `GameProtocol.Version` sube **solo** con cambios incompatibles de contrato.

---

## 8. Estructura del repo

```
/CLAUDE.md                                 ← este archivo
/.claude/agents/*.md                       ← sub-agentes
/.claude/skills/*                          ← enlaces a claude-kit, NO versionados (§11)
/Docs/
   ARMADA-Documento-Tecnico-Produccion.md  ← fuente de verdad
/CHANGELOG-dev.md                          ← Keep a Changelog (técnico)
/changelog-jugadores.md                    ← patch notes (jugador)
/Assets/Scripts/Core                       ← lógica pura, sin UnityEngine
/Assets/Scripts/Core.Tests                 ← EditMode tests
/Assets/Scripts/Game                       ← capa Unity + servicios UGS
/Assets/Scripts/Game/Editor                ← editor scripts idempotentes + Content Designer
/Assets/UI                                 ← UXML + USS (UI Toolkit)
/Assets/Localization                       ← StringTables generadas
/Assets/RemoteConfig                       ← config-as-code (GameConfig.rc)
/Assets/Matchmaker                         ← queue/pool config-as-code
/Content/ContentDatabase.json              ← fuente única de contenido
/CloudCode~/ArmadaModule                   ← módulo Cloud Code (~ para que Unity lo ignore)
/Tests/Armada.Core.Tests                   ← csproj headless: dotnet test sin Unity
/Tests/Armada.Architecture.Tests           ← tests de arquitectura, sin Unity
/.github/workflows/                        ← CI
```

> `/Tests/*` no está en la estructura original del documento técnico: son los `.csproj` que permiten correr los tests de Core y de arquitectura **sin licencia de Unity** en CI (< 2 min). Compilan las mismas fuentes que los asmdef de `Assets/Scripts`; no duplican código.

---

## 9. Entorno local verificado

| Item | Valor |
|---|---|
| Unity Editor | `6000.3.20f1` (línea 6.3 LTS) — fijado, no se toca hasta después del lanzamiento |
| URP | `17.3.0` |
| Input System | `1.19.0` |
| .NET SDK | `10.0.302` (el módulo de Cloud Code targetea `net9.0`) |
| Remoto git | `origin` → `github.com/Hellscythe25/Armada` |

---

## 10. Sub-agentes

Flujo: `game-designer` → `gameplay-programmer` / `systems-programmer` → `qa-tester` → `docs-changelog` → `git-ops`.

Definidos en `.claude/agents/`. Reglas de orquestación: el contrato de §7 se pasa **idéntico** a los agentes que trabajen en paralelo; el documento técnico es la única fuente de verdad numérica; cada milestone cierra con QA → changelog → commit.

---

## 11. Skills

`.claude/skills/` **no se versiona**: son enlaces (junctions en Windows) al repo `claude-kit`, fuente única de las convenciones compartidas con TCGMaster, TTTXO y C4. Se espera como carpeta hermana de esta — `D:/UnityProjects/claude-kit` en el entorno local verificado de §9.

Git atraviesa el enlace y ve archivos normales, así que la carpeta está en `.gitignore`. Sin esa regla, un `git add .` commitea una copia de la skill que después diverge del original en silencio.

Hoy se enlaza el pack `ugs`: orden de arranque de servicios, clases de acceso de Cloud Save, política de retry y anti-patrones, extraídos de los cuatro proyectos. Parte del material salió de este repo — el `StaticStateRegistry` de §5 quedó registrado en la skill como la forma verificable de resolver el reset de estáticos, frente a la versión ad-hoc de TCGMaster.

Para recrear los enlaces:

```powershell
powershell -ExecutionPolicy Bypass -File ..\claude-kit\scripts\link.ps1 Armada
```

En Linux/macOS: `../claude-kit/scripts/link.sh Armada`.

Editar un `SKILL.md` desde este proyecto edita el original, y el cambio llega a los demás proyectos enlazados. Es el objetivo del enlace, pero conviene tenerlo presente antes de tocar uno.
