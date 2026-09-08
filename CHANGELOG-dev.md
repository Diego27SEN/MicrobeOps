# CHANGELOG-dev — ARMADA

Changelog técnico del proyecto. Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).
El changelog de cara al jugador vive en [`changelog-jugadores.md`](changelog-jugadores.md).

Fuente de verdad del proyecto: [`Docs/ARMADA-Documento-Tecnico-Produccion.md`](Docs/ARMADA-Documento-Tecnico-Produccion.md).

Secciones por entrada: `Añadido` · `Cambiado` · `Obsoleto` · `Eliminado` · `Corregido` · `Seguridad` · `Deuda aceptada`.

---

## [Sin publicar] — M3 Fundaciones UGS (en curso)

### Añadido

- **Proyecto UGS vinculado.** `cloudProjectId` `688b3fe5-d84e-4766-af47-1e4b95159922`, organización `wild-cat-games`, proyecto `Armada`, con los tres entornos creados. Entorno activo **`development`**, id `b59e0336-99b9-40cf-a763-ce892730a39a`, en `ProjectSettings/Packages/com.unity.services.core/Settings.json`. Cierra **M0-2**, que bloqueaba todo M3.
- Los siete paquetes resolvieron limpio. Confirmado que **Wire entra transitivamente por Cloud Code** (`services.wire 1.4.4`), como anticipaba §6.5, junto con `remote-config-runtime 4.0.4` y `deployment.api 1.1.3`.
- **`CloudCode~/ArmadaModule` completo y compilando**: `.csproj` en `net9.0` con nullable enable y los `CS86xx` como error, `ModuleSetup` con `ICloudCodeSetup`, publish a `linux-x64` con ReadyToRun compuesto. Cierra **M0-4**.
  Lo importante del `.csproj` es el `<Compile Include="../../Assets/Scripts/Core/**/*.cs" />`: el módulo **compila las fuentes de Core**, no una copia. Reglas, proyección e IA son byte-for-byte idénticas en cliente y servidor.
- **`GetServerInfo` y `GetGameConfig`**, los dos primeros endpoints de §7.3. El handshake devuelve versión de protocolo, hora de servidor y kill switches; `GetGameConfig` rechaza clientes por debajo del mínimo y también los que van **por delante** del servidor, que es lo que pasa a mitad de un staged rollout.
- **`GetPlayerBootstrap`**, el tercer endpoint de §7.3: perfil, economía, inventario, partidas activas, estado diario y de temporada en **una sola llamada**. Cinco round trips en el arranque era deuda heredada: hacía lento el arranque en frío y cada uno era otra oportunidad de fallar con mala conexión.
  La lógica interesante vive en `PlayerBootstrapBuilder`, puro y sin almacenamiento, así que se testea antes de que exista Cloud Save. Dos decisiones que salieron de ahí: el **rollover del día también ocurre en el boot** y no solo en el scheduler, porque quien abre la app a las 00:04 UTC no debe ver los contadores de ayer; y **la racha de victorias sobrevive el cambio de día**, porque se rompe con una derrota, no con un reloj.
- **`RetryPolicy` en Core** (§9.2). Vive en Core y no en el gateway porque es la parte que puede estar mal: reintentar un error de negocio martilla al servidor con una llamada que nunca va a funcionar, y rendirse ante un paquete perdido le cuesta al jugador un turno que ya jugó.
  La línea es simple: un fallo de transporte se reintenta, un `ERR_*` es el servidor habiendo decidido. La única excepción es `ERR_RATE_LIMITED`, que significa "más tarde" y no "no" — y ahí **el tiempo que pide el servidor gana sobre el backoff**, porque el backoff es una conjetura y el servidor sabe. Se parsea con cultura invariante: en una configuración regional española, un parse ingenuo lee `1.5` como quince y el cliente espera diez veces de más.
- **`UgsGateway`** — la única puerta a Cloud Code; nada más en el cliente lo llama directo. Deliberadamente delgado: las decisiones que importan viven en Core (`RetryPolicy` qué reintentar, `PendingActionQueue` qué puede esperar), y acá solo queda el transporte que las ejecuta. Que haya poco código es el punto: hay poco que pueda estar mal y no ser testeable a la vez.
  Nunca lanza. Una excepción inesperada degrada a `Offline` con un warning, porque un servicio que se porta mal no puede llevarse el juego puesto. El transporte es inyectable, así que el gateway se puede ejercitar sin UGS.
  `ReplayQueuedAsync` **para en la primera llamada que no llega** y devuelve el resto a la cola en orden: seguir adelante reordenaría la reproducción.
- **`Armada.Game.asmdef` referencia los assemblies de UGS.** Los `asmdef` son explícitos: instalar el paquete no alcanza. Los nombres se tomaron de los propios paquetes, no de memoria — `Unity.Services.CloudDiagnostics`, por ejemplo, no sigue el patrón del id del paquete (`com.unity.services.cloud-diagnostics`).
- **`PendingActionQueue` en Core** — la cola offline de §9.2. Dos reglas cargan el peso:
  **Nada que decida una partida o mueva moneda puede encolarse.** La lista de tipos encolables es corta y ampliarla es una decisión, no un trámite: perder la conexión durante un disparo tiene que ser un error que el jugador ve, no una jugada que aterriza tres minutos tarde contra un mundo que siguió adelante. Hay un test que recorre el enum entero y falla si alguien agrega un tipo sin pensarlo.
  **Para el estado donde solo importa el último valor** —un cosmético equipado, un token de push— una acción nueva reemplaza a la anterior del mismo destino en vez de apilarse. Reproducir cuatro cambios de cosmético para llegar al cuarto es cuatro veces el trabajo por el mismo resultado. Analytics es la excepción: cada evento es un hecho propio y se acumulan; al llenarse se descarta el más viejo, porque un evento de telemetría perdiendo su lugar es menos grave que la cola creciendo sin límite en un teléfono que estuvo un día sin señal.
- **`ConfigProvider`** como único punto de lectura de configuración, con cache de 2 min y seed embebido. Un servicio de config caído degrada a las cifras del documento, no a una excepción.
- **12 tests del módulo** que corren con `dotnet test`, sin Unity y sin módulo desplegado — que es lo que permite sostener la regla de "tests del módulo antes de cada deploy, sin excepción". Workflow `module-tests` agregado, con las fuentes de Core entre sus disparadores: el módulo las compila, así que tocar las reglas es tocar el servidor.
- **Paquetes de M3 en `manifest.json`**, con las versiones verificadas: `services.core 1.18.0`, `authentication 3.7.3`, `cloudcode 2.10.4`, `cloudsave 3.4.1`, `remote-config 4.2.5`, `analytics 6.3.0`, `deployment 1.7.2`, `cloud-diagnostics 1.0.12`, `localization 1.5.12` y `addressables 2.9.1`.
  §6.5 dejaba el paquete de Cloud Diagnostics sin nombrar; el real es **`com.unity.services.cloud-diagnostics`**, última `1.0.12`, y solo depende de `services.core` y del módulo de analytics del motor.
  Addressables se fija **explícitamente en 2.9.1** y no se deja resolver por Localization, que solo exige `1.25.0`. No se sube a la línea 3.x: es un salto mayor que además cambia `scriptablebuildpipeline`, y Localization 1.5.12 está validado contra 2.x.
  **Economy, Leaderboards, Multiplayer, notificaciones e IAP no se instalan todavía**: entran con M5, M7, M4, M7 y M6. Meter el grafo entero de una vez hace que, si algo falla al resolver, no se sepa qué lo rompió — y ya pasó una vez en este proyecto con las hojas de estilo.

### Verificación de versiones NuGet

Consultadas contra `nuget.org`. `Com.Unity.Services.CloudCode.Core 0.0.4` y `.Apis 0.0.26` son correctas y son las últimas publicadas.

Dos hallazgos:

- **`CloudCode.Core 0.0.4` apunta a `netstandard2.1` y arrastra `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2`.** Es exactamente la desalineación que §6.5 manda corregir; la referencia explícita a **9.0.18** —último parche de la línea alineada con el runtime `net9.0`— gana sobre la transitiva. El documento decía "9.0.0"; se usa el último parche de esa misma línea.
- **`AddGameApiClient` vive en `Unity.Services.CloudCode.Apis.Extensions`, no en `.Apis`.** El propio paquete confirma que `GameApiClient.Create()` está obsoleto y que esta es la vía soportada.

### Corregido

- El `.csproj` del módulo excluye `TestProject/**`. El proyecto de tests vive dentro de la carpeta del módulo, como manda §18.1, y el glob por defecto del SDK compilaba sus fuentes dentro del módulo. El primer síntoma es NUnit sin resolver **dentro del módulo**, que no apunta ni cerca de la causa.

---

## [Sin publicar] — M2 Cliente local (cerrado, salvo medición en dispositivo)

**Estado**: verificado de punta a punta en el Editor.

- Partida completa contra la IA: Home → modo → despliegue → partida → resumen, con victoria y estadísticas.
- Tutorial completo: los 8 pasos encadenados hasta el cierre.
- Accesibilidad: alto contraste aplicándose sobre toda la interfaz.

Todo el contenido de M2 según §16 está construido. Lo único que falta del criterio de salida es la medición en **dispositivo real** (60 FPS, arranque < 3.5 s), que no se puede hacer desde acá.

Las tres rondas de bugs de UI que salieron de probarlo —overlay tapando encabezados, tutorial desincronizado, botones sin centrar— están todas corregidas y con guardas de arquitectura donde correspondía.

### Añadido

- **Tutorial interactivo**, con la lógica en `Armada.Core`. `TutorialFlow` es una máquina de estados sobre acciones del jugador —desplegar un barco, completar la flota, confirmar, disparar, acertar, hundir— y no conoce ninguna pantalla. Es la parte que puede estar mal, y así se testea exhaustivamente sin licencia de Unity (9 tests).
  Cada paso espera **una** acción concreta y las demás se ignoran en vez de saltearse: quien dispara antes de que se lo pidan no se adelanta a la explicación, y quien hace lo correcto dos veces no avanza dos pasos. Solo el primero y el último esperan que el jugador lea y toque; el resto avanza jugando, que es lo que mantiene el onboarding en ~90 s en vez de una fila de diálogos.
  Corre como una partida normal contra la IA en `Easy`, no como un sandbox guionado: lo que el jugador aprende es el juego real y no hay un segundo camino de código que mantener. **Es una opción del menú principal, no un arranque automático**: se ofrece, no se impone, y queda disponible siempre — para quien vuelve después de un mes tanto como para quien empieza. Sin auto-arranque no hace falta recordar si ya se vio, así que no se guarda ninguna preferencia; un flag que nadie lee es peor que no tenerlo.

- **`LocalMatchDriver` en `Armada.Core`** — el bucle offline (vs IA y 2 jugadores local) vive en lógica pura y testeable, no en la capa Unity. El cliente sigue siendo un pintor incluso sin servidor, y el módulo de Cloud Code manejará las partidas online con el mismo motor en el mismo orden.
- **Assembly `Armada.Game`** con el blindaje de §9.3: `StaticStateRegistry` con reset en `SubsystemRegistration`, `UiPanelHost` como única fachada sobre `UIDocument`, `BootTexts` y `AppSettings` en PlayerPrefs.
- **Router de pantallas y 6 pantallas**: Home, selección de modo, despliegue, partida, resumen y ajustes. Todo en un solo documento con paneles; ninguna pantalla carga escenas.
- **`Assets/UI/Main.uxml` + `Theme.uss` + `Screens.uss`.** Todos los colores y tamaños son variables en `:root`; ningún hex fuera de `Theme.uss`. Incluye las 3 paletas daltónicas, alto contraste, 3 escalas de texto y `reduce motion`.
- **Accesibilidad no cromática desde el primer día**: agua, tocado y hundido llevan forma propia (punto / cruz / casco) además de color. En escala de grises el tablero se sigue leyendo.
- **`ProjectBootstrap` con 4 pasos idempotentes**: activa *Enter Play Mode Options*, genera `ArmadaPanelSettings`, reconstruye la escena `Main` desde cero y fija las Build Settings. Ningún `.unity` ni `.asset` se edita a mano.
- **9 tests de arquitectura nuevos** sobre `Armada.Game`, entre ellos `Game_HasNoUnregisteredMutableStatics`, `UiDocument_IsTouchedOnlyByTheFacade`, `Colours_AreDeclaredOnlyInTheThemeStyleSheet` y `Game_DoesNotUsePanelRenderer`.
- 18 tests de `LocalMatchDriver`, incluida una partida completa contra las 4 dificultades y una partida completa de Salvo.

### Corregido

- **La partida se trababa cuando la IA ganaba el sorteo del primer turno.** `FirstMover` es aleatorio, así que alrededor de la mitad de las partidas vs IA abren con la máquina en el reloj. La pantalla de partida solo hacía mover a la IA *después* de que el jugador disparara, con lo que esas partidas quedaban muertas: el jugador no podía disparar porque no era su turno y nadie iba a mover a la IA. `MatchScreen.OnShow` ahora la hace jugar si le toca. Dos tests nuevos en `LocalMatchDriverTests` fijan la condición: que existan semillas donde la IA abre, y que un `StepAi` desde el turno de apertura devuelva el turno al humano.
- **La vista de juego en modo edición mostraba el marcado crudo.** Consecuencia directa de adjuntar los USS desde código: `UIDocument` sí dibuja en modo edición, pero ahí no corre ningún `Awake`, así que no se aplicaba ninguna hoja y se veían los seis paneles apilados en gris. `UiPanelHost` lleva ahora `[ExecuteAlways]`: el trabajo es idempotente y solo toca el panel, y devuelve una previsualización que sirve — se ve solo Home, que es el panel que el marcado marca activo, y con su estilo real. En runtime el router toma el control como siempre.
  Se relajó de paso `StyleSheets_AreWiredByTheBootstrap_AndAnyUxmlReferenceCarriesItsGuid`: prohibir todo `<Style src>` habría peleado contra UI Builder, que escribe la forma correcta con GUID. Ahora se permite esa forma y se rechaza cualquier otra.
- **Disparar exigía seleccionar y después ir hasta el botón.** Un paso de más que se paga decenas de veces por partida. Ahora un segundo clic sobre la casilla dispara directo; el botón sigue estando, y el clic simple y la activación por teclado no cambian. En Salvo el atajo solo dispara cuando entra la última casilla del volley. La pista aparece en el mismo lugar donde ya se muestra la cantidad de disparos, y solo mientras el jugador está en el reloj.
- **Las letras de columna no coincidían con sus casillas.** Los encabezados se dimensionaban como cuadrados de 24 px mientras las casillas miden `--cell-size` más margen, así que cada letra se corría un poco más de su columna a medida que avanzaba el tablero. Los rótulos tienen ahora modificadores propios: la columna toma el ancho de casilla con sus márgenes, la fila toma el alto, y la esquina toma ambos canales.
- **No había forma de salir del despliegue.** La partida todavía no empezó, así que no hay nada que rendir, y no existía ninguna otra salida: el jugador quedaba encerrado. Se agregó `Volver`.
- Se agregó `EveryElementTheScreensLookUp_ExistsInTheMarkup`: extrae los nombres que el código busca con `Q`, `WireButton` y `SetText` y verifica que existan en `Main.uxml`. Nada validaba ese contrato, y un nombre que deja de coincidir degrada a un warning en runtime y un control que no hace nada — que es exactamente cómo se llega a un botón faltante.
- El contenido centrado ya no se pega arriba dejando una pantalla de espacio vacío: un `ScrollView` maqueta en un contenedor interno, así que el centrado tenía que aplicarse ahí.
- **Despliegue sin previsualización.** No había forma de ver dónde iba a caer el barco antes de soltarlo, y `Rotar` no mostraba estado, así que parecía no hacer nada. Ahora la cuadrícula previsualiza la huella del barco en válido o inválido, el botón dice la orientación actual (`Rotar · Horizontal`), la tecla `R` rota, y el barco pendiente muestra su nombre y su longitud. El mismo gesto sirve para los dos modelos de entrada: con puntero el hover ya dejó la celda previsualizada y el clic la coloca de una; con dedo, que no genera hover, el primer toque previsualiza y el segundo confirma — sin ramas por plataforma.
- **`Main.uxml` no parseaba: XML prohíbe el doble guion dentro de un comentario.** Un comentario del encabezado mencionaba literalmente el nombre de la clase modificadora de panel, con sus dos guiones, y eso invalida el documento entero. Unity **no reporta nada**: simplemente no genera el `VisualTreeAsset`, el árbol queda vacío y el único síntoma es que todas las pantallas se declaran faltantes en runtime. Se agregó `Uxml_IsWellFormedXml`, que parsea cada `.uxml` — una línea de test que atrapa esto y cualquier otro error de forma antes de abrir el Editor — y `Uxml_DeclaresEveryPanelTheRouterExpects`, que verifica el contrato de nombres entre el marcado y los controladores.
- **La UI se cableaba contra un árbol vacío.** `UIDocument` entrega un `rootVisualElement` no nulo **bastante antes** de clonar el UXML dentro de él. `UiPanelHost` disparaba el rewireo en su `OnEnable` confiando en que el árbol ya existía, así que las seis pantallas se buscaban con `Q(...)` y ninguna aparecía: seis errores `[UI] Screen element ... is missing`, ningún panel ocultado y las seis pantallas dibujadas una encima de otra. **Esta era la causa real de la captura ilegible.** `UiPanelHost` ahora distingue `IsAlive` (el root existe) de `IsReady` (el árbol se clonó) y reintenta por el scheduler hasta 30 ticks, con un error explícito si nunca llega. Se eliminó así toda dependencia del orden de componentes en la escena, que es un dato del `.unity` serializado y no algo sobre lo que el código deba apostar.
- **Registro tardío del callback de rewireo.** `GameRoot` registra desde `Awake`; si `UiPanelHost.OnEnable` ya había pasado, no quedaba nada agendado y el rewireo no ocurría nunca. `RegisterRebuildCallback` arranca ahora la espera por su cuenta.
- Los seis errores idénticos se colapsaron en **uno solo que nombra los paneles faltantes**. Seis mensajes iguales dicen "la UI está rota"; uno dice qué parte.
- **Las hojas de estilo no se cargaban, y `<Style src>` resultó no ser una vía confiable.** La forma `project://database/Assets/UI/Theme.uss` solo resuelve con el query string de `fileID`/`guid` que genera el Editor; escrita a mano sin él no aplica nada. La forma relativa (`src="Theme.uss"`) **tampoco aplicó nada** al probarla en el Editor. Ambos modos de falla son silenciosos y se ven idénticos en pantalla: controles grises por defecto.
  Los USS se adjuntan ahora **desde código**, en `UiPanelHost`, a partir de referencias de asset serializadas que `ProjectBootstrap` asigna. Una referencia de asset está asignada o está visiblemente en null; no puede fallar en silencio. El UXML queda con estructura y el código con estilo. Cubierto por `StyleSheets_AreAttachedFromCode_NotFromUxml`, que además verifica que el bootstrap asigne **todos** los `.uss` que existan en `Assets/UI`.
- **Los paneles se ocultaban solo desde C#**, con estilo inline. En la vista de juego en modo edición no corre ningún `Awake`, así que las seis pantallas se dibujaban apiladas. Ahora `.screen` es `display: none` en USS y el router agrega `screen--active`; el estilo inline desapareció porque además ganaba sobre cualquier regla de hoja. Cubierto por `Screens_AreHiddenByDefaultInStyle`.
- **La marca de "tocado" no dibujaba una cruz.** Un solo elemento con `border-width` y `rotate: 45deg` da un rombo, no una X, y USS no tiene `::before`/`::after`. Las tres formas no cromáticas son ahora anillo hueco (agua), rombo sólido (tocado) y barra de casco (hundido) — distinguibles en escala de grises con un elemento por casilla.
- **El tamaño de casilla escalaba con el texto.** Un tablero `admiral` al 150 % se pasaba del ancho de referencia y se recortaba. `--cell-size` es ahora independiente de la escala tipográfica; las casillas ya superan el piso de 44 pt en todas las escalas.

### Cambiado

- **La IA recibe una identidad reservada** (`MatchState.AiPlayerId`). Antes `PlayerB` quedaba en `null` en partidas vs IA y la IA no podía mover por el camino normal del motor. Ahora toda mutación pasa por el mismo código, venga de una persona o no.
- **Rediseño de legibilidad**: contenido en una columna centrada con ancho máximo (720 px) en vez de estirado de borde a borde, tarjetas con superficie propia, más contraste entre texto y fondo, botones más altos con foco de teclado visible, y agrupación de los ajustes en tarjetas.
- 3 tests de arquitectura nuevos sobre el UXML: referencias de estilo resolubles, paneles ocultos por defecto y **cero texto de jugador en el marcado** (`Uxml_CarriesNoPlayerFacingText`).

### Deuda aceptada

| # | Deuda | Severidad | Condición de cierre |
|---|---|---|---|
| ~~M2-1~~ | ~~`ProjectBootstrap.SetupAll()` no se ejecutó~~ | ~~Alta~~ | **Cerrada.** Ejecutado; `Assets/Scenes/Main.unity` y `Assets/UI/ArmadaPanelSettings.asset` generados y cableados correctamente. |
| ~~M2-2~~ | ~~La partida completa vs IA no está verificada en Play mode~~ | ~~Alta~~ | **Cerrada.** Partida entera jugada hasta el resumen; la IA responde y el modo de alto contraste se aplica correctamente. |
| M2-3 | **Despliegue por *tap-to-place*, no *drag & drop***, como pedía §16. Se eligió tap deliberadamente: funciona idéntico con dedo, mouse y teclado sin código condicional por plataforma, que es lo que exige la regla de input de §2.3; el drag es mucho más difícil de hacer accesible por teclado | Media | Decisión de producto: aceptar tap-to-place como definitivo, o agregar drag como atajo **encima** del tap (nunca en lugar de). |
| M2-4 | **`UiText` usa una tabla embebida temporal** (`EmbeddedStrings`) en vez de Unity Localization, que llega en M3 con el paquete. Las pantallas ya usan claves, así que no cambian cuando se reemplace el backend | Media | Instalar `com.unity.localization 1.5.12`, generar las StringTables y cambiar el provider. M3. |
| ~~M2-5~~ | ~~Sin tutorial~~ | ~~Media~~ | **Cerrada.** `TutorialFlow` en Core + overlay, 8 pasos, repetible desde la selección de modo. |
| M2-8 | El tutorial **no emite el evento `tutorial_step`** de §14.1 (`step_id`, `completed`, `elapsed_ms`), porque Analytics llega en M3. Sin él no hay embudo de onboarding y el KPI de 85 % de finalización de §14.3 no se puede medir | Media | Conectar Analytics en M3. `TutorialFlow` ya expone el paso actual y `AllSteps()` para el embudo. |
| M2-6 | **Los turnos de la IA se resuelven de forma síncrona**, sin animación ni ritmo | Baja | Pulido de M8, cuando haya VFX y audio que acompañen el disparo. |
| M2-7 | **Presupuestos de §12.3 sin verificar en dispositivo.** Medido en el Editor sobre escritorio: Home **1100 FPS (0.9 ms), 3 batches, 385 tris**; partida en `admiral` —el peor caso, dos grillas de 12×12— **1007 FPS (1.0 ms), 8 batches, 14.4k tris, 33.6k verts**. Pasar de 0 a ~288 casillas cuesta **0.1 ms y 5 batches**: los triángulos se multiplican por 37 y el tiempo de frame casi no se mueve, así que la UI no está limitada ni por layout ni por geometría. La pregunta de si esta interfaz sostiene 60 FPS queda respondida con 16× de margen | Baja | Queda lo que solo responde un teléfono: build en vez de Editor, GPU y CPU de gama media, **arranque en frío < 3.5 s**, memoria < 512 MB, descarga < 150 MB y batería < 6 %/h, sobre la matriz de §12.4. Vigilar aparte el **pico de construcción de la grilla** al entrar a la partida (288 botones creados de una), que se manifiesta como tirón puntual y no como FPS bajos. |

---

## [Sin publicar] — M1 Core puro

### Añadido

- **`PlacementRules` completo**: validación (composición, orden de flota, límites, solapamiento, adyacencia), `GenerateRandom` con fallback determinista garantizado, `IsHumanLike` y `GenerateHumanLike`.
- **`ShotRules` completo**: resolución de disparo, hundimiento, detección de fin de partida, disparos por turno y redacción del resultado según flags.
- **`BattleEngine` completo**: creación, join, despliegue con barrera de "ambos listos", volleys, abandono y resolución de vencimientos (auto-despliegue, disparo automático, strikes y abandono).
- **`MatchProjection` completo**, con los 5 tests de §6.3 verdes, incluido `SerializedView_ContainsNoSecretCoordinates` sobre el JSON real.
- **`EloRules` y `RewardRules` completos**, con anti-farming, racha, primera victoria del día, topes diarios y curva de nivel.
- **Las 4 IAs**: `Easy`, `Medium`, `Hard` (paridad + mapa de densidad de probabilidad + inferencia de línea) y `Adaptive`.
- **297 tests de Core + 9 de arquitectura**, incluidos los 8 invariantes de property-based testing de §12.2 sobre 108 partidas aleatorias (3 tableros × 3 rule sets × 12 partidas).
- `Tests/coverlet.runsettings` — necesario porque Core se compila **dentro** del assembly de tests y coverlet lo excluiría por defecto, reportando 0 %.

### Corregido

- **Deadlock en Salvo, encontrado por los property tests.** Cuando al tablero rival le quedaban menos casillas sin disparar que barcos a flote tenía el tirador, el volley exigido era imposible de satisfacer: toda submission fallaba con `ERR_SHOT_COUNT_INVALID` y la partida quedaba trabada hasta el timer de abandono. `ShotRules.ShotsForTurn` tiene ahora una sobrecarga que acota la cantidad por el agua restante, y la usan el motor y la proyección.

### Cambiado

- **`Easy` reescrita.** La primera versión perseguía el conjunto acumulado de impactos sin resolver, ignorándolo un 25 % de las veces: medía **64.1 disparos**, prácticamente idéntico a `Medium` (64.3), con lo que el escalón de dificultad no existía. Ignorar un seguimiento cuesta casi nada si el cabo suelto sigue esperando en el turno siguiente. Ahora `Easy` **olvida**: solo persigue el impacto que acaba de anotar. Medida: **92.2 disparos**, contra los ~93.5 que implica §4.6.
- `PlayerBoard` gana `DisclosedHits`: el subconjunto de impactos que las reglas efectivamente comunicaron al tirador. Bajo `salvoAggregateReport` difiere de `Hits`, y la proyección lee **este** campo. Leer `Hits` sería entregar al jugador de Salvo justo la información que la variante le niega.
- `ApplyResult` gana `VolleySummary` para el reporte agregado de Salvo ("2 impactos, 3 aguas"). En modo agregado `Shots` va vacío: devolver los resultados por casilla anularía la variante entera.
- `MatchProjection.Project` recibe además `BoardConfig`, para poder acotar `ShotsThisTurn` por el agua restante.
- `PlacementRules.CellsOf` recibe la longitud del barco: `ShipPlacement` lleva clase pero no longitud, y en `admiral` hay dos Battleship de longitudes distintas.

### Medición de las IAs (400 partidas por dificultad, tablero `classic`)

| Dificultad | Disparos promedio | Objetivo §4.6 | Estado |
|---|---|---|---|
| `Easy` | 92.2 | ~93.5 | ✅ en objetivo |
| `Medium` | 63.5 | ~71.4 | ⚠️ **juega mejor de lo especificado** (~1/3.7 en vez de ~1/4.2) |
| `Hard` | 45.2 | < 48 (criterio duro) | ✅ cumple |
| `Adaptive` | 59.1 | variable | ✅ entre Medium y Hard |

### Cobertura

96.34 % de líneas en `Armada.Core` (mínimo exigido: 90 %). `MatchProjection`, `ShotRules`, `RewardRules` y `EloRules` al **100 %**; `PlacementRules` 98.6 %, `BattleEngine` 95.8 %.

### Deuda aceptada

| # | Deuda | Severidad | Condición de cierre |
|---|---|---|---|
| M1-1 | **`Medium` juega mejor de lo que especifica §4.6** (63.5 disparos contra los ~71.4 que implica "~1 impacto / 4.2 disparos"). *Hunt & target* puro sobre `classic` rinde ~64 disparos; la cifra del documento es conservadora. El winrate objetivo de 40 % vs jugador medio queda en riesgo de quedar alto | Media | Decisión de `game-designer`: corregir la cifra de §4.6 o degradar `Medium`. Antes de M2. |
| M1-2 | `BattleEngine` al 95.8 % y `PlacementRules` al 98.6 %, no al 100 % que §12.1 pide para "reglas" | Baja | Cubrir las ramas restantes al cerrar M2. |
| M1-3 | El modo `Adaptive` ajusta el ruido comparando su precisión con la del jugador, pero nadie alimenta todavía `OwnAccuracyLastWindow` / `OpponentAccuracyLastWindow`: hasta M4 juega con el ruido inicial fijo | Media | Conectar las métricas de ventana en el módulo de Cloud Code (M4). |

---

## [Sin publicar] — M0 Fundación

### Añadido

- `CLAUDE.md` con las directivas del proyecto (§18.2 del documento técnico).
- Los 9 sub-agentes de §18.3 en `.claude/agents/`: `game-designer`, `gameplay-programmer`, `systems-programmer`, `backend-security`, `ui-programmer`, `narrative-writer`, `qa-tester`, `docs-changelog`, `git-ops`.
- `Docs/ARMADA-Documento-Tecnico-Produccion.md` v1.0 — documento fundacional, fuente de verdad.
- `CHANGELOG-dev.md` y `changelog-jugadores.md`.
- **Contrato de §7 congelado** en `Armada.Core` (`Assets/Scripts/Core`): tipos compartidos (§7.1), superficie pública de reglas (§7.2), catálogo de errores `ERR_*` (§7.4) y `GameProtocol.Version = 1`. Los cuerpos de las reglas quedan sin implementar hasta M1; las firmas son definitivas.
- `BitBoard` (§6.3) implementado: 3 `ulong`, hasta 144 casillas.
- `DefaultGameConfig` — fallback embebido offline con las cifras exactas de §4 y §5. Es el espejo local de Remote Config y la única copia de esas cifras en el cliente.
- `Armada.Core.asmdef` con `noEngineReferences: true` y `csc.rsp` que activa nullable y trata los `CS86xx` como error.
- `Armada.Core.Tests` (EditMode) + `Tests/Armada.Core.Tests/*.csproj` para correr los mismos tests con `dotnet test`, sin licencia de Unity, en CI.
- Tests de arquitectura (§6.2) en `Tests/Armada.Architecture.Tests`: `Core_HasNoEngineReferences`, `Core_DoesNotUseAmbientTimeOrRandom`, `Core_IsNullableClean`.
- `Armada.Editor.ProjectBootstrap.SetupAll()` — vacío pero invocable en batchmode con `-executeMethod`.
- `CloudCode~/ArmadaModule/Properties/PublishProfiles/FolderProfile.pubxml` — creado antes del primer intento de deploy, como exige §6.5.
- Workflows de CI: `core-tests`, `architecture-tests`.

### Cambiado

- `.gitignore`: se des-ignoran los `.csproj` escritos a mano de `Tests/` y `CloudCode~/`, y se ignoran sus `bin/` y `obj/`. El `.gitignore` de Unity ignora `*.csproj` global porque Unity los autogenera; los nuestros son fuente.

### Decisiones registradas

- **Editor fijado en `6000.3.20f1`** (línea Unity 6.3 LTS), coincidente con §6.6. No se toca hasta después del lanzamiento salvo fix de seguridad o requisito de tienda.
- **`GameProtocol.cs` vive en `Armada.Core`**, no en el módulo. El documento (§18.1) lo ubicaba en `CloudCode~/ArmadaModule/Project`; al vivir en Core queda espejado automáticamente en cliente y módulo por el `<Compile Include>` compartido, que es exactamente lo que §7.5 exige ("espejado en módulo y cliente, siempre en el mismo commit"). Elimina la posibilidad de desincronización.
- **`MatchMode` y `AiDifficulty` agregados al contrato.** §7.3 (`CreateMatch`) y §7.2 (`AiFactory.Create`) los usan pero §7.1 no los declaraba. Valores: `MatchMode { VsAi, LocalTwoPlayer, Casual, Ranked, Async, Private }`, `AiDifficulty { Easy, Medium, Hard, Adaptive }`. Cualquier cambio en ellos es cambio de contrato.
- **Namespaces con llaves, no file-scoped.** Los ejemplos de §7.1 usan `namespace Armada.Core;` (C# 10); Unity 6.3 compila **C# 9**. Se usa la forma con llaves.
- **`/Tests/*.csproj` es una adición a la estructura de §18.1.** Permite correr Core y los tests de arquitectura sin licencia de Unity, que es lo que el workflow `core-tests` de §13.2 pide ("`dotnet test` de Core, sin Unity, < 2 min"). Compilan las mismas fuentes que los asmdef; no duplican código.
- **PRNG propio (`SeededRandom`, xorshift64\* con semilla SplitMix64) en vez de `System.Random`.** `System.Random` cambió de algoritmo en .NET 6: la misma semilla produce secuencias distintas en el Mono de Unity y en el runtime `net9.0` del módulo. El invariante 7 de §12.2 ("con la misma semilla, la IA produce la misma secuencia de disparos") sería imposible de cumplir con el generador de la BCL. Cubierto por `SeededRandom_SameSeed_ProducesTheSameSequence`.
- **`BitBoard` serializa con orden de bytes little-endian escrito a mano**, no con `BitConverter`, para que el formato de cable sea idéntico en todo runtime por el que pase el estado.
- **`TrackingBoardView` no expone ningún conteo agregado del rival** (ni "barcos restantes"). Se consideró incluirlo para la UI y se descartó: en Salvo, con `announceSunkShipType = false`, es información que reduce el espacio de búsqueda. La UI se arma con `SunkShips`, que ya está redactado por flags. Decisión sujeta a revisión de `backend-security` si alguna pantalla lo necesita.

### Verificación de versiones de paquetes (cierra M0-1)

Las 15 versiones de §6.5 se consultaron contra `packages.unity.com` el 2026-07-23. **Ninguna era inventada: las 12 que el documento fijaba existen en el registro.** Resultados que cambiaron algo:

- **Corrección al documento**: `com.unity.localization 1.5.12` arrastra Addressables **`1.25.0`**, no `2.9.1` como decía §6.5. Addressables `2.9.1` se fija explícitamente; no se sube a `3.1.0` (salto mayor, cambia `scriptablebuildpipeline` de 2.6.1 a 3.1.1, y Localization 1.5.12 está validado contra la línea 2.x).
- **`com.unity.services.multiplayer` sube de `2.2.3` a `2.3.0`**: `2.3.0` pide exactamente `services.core 1.18.0` y `authentication 3.7.0`, que son las versiones ya fijadas; `2.2.3` pide `1.16.0` / `3.6.0` y dejaría el grafo desalineado.
- **`com.unity.services.economy` sube de `3.5.3` a `3.5.4`** (parche).
- Las tres marcadas *(verificar)* quedan resueltas: `leaderboards 2.3.4`, `mobile.notifications 2.4.3`, `purchasing 5.4.1`.
- **`com.unity.purchasing 5.4.1` depende de `com.unity.ugui`.** Es inevitable y no contradice la regla de UI: lo prohibido es *usar* uGUI para nuestras pantallas, no que el paquete exista en el grafo.
- `com.unity.services.matchmaker` sigue publicado (`1.2.0`), lo que lo vuelve fácil de instalar por error. Queda anotado en §6.5 como "existe pero no se usa".

### Desviaciones del contrato de §7 (registradas al congelarlo)

Las firmas de §7.2 se implementaron con estos ajustes. Son consecuencia directa de las restricciones de §6.2 sobre `Armada.Core`, no cambios de diseño. Quedan congeladas junto con el resto del contrato.

| Firma del documento | Firma real | Motivo |
|---|---|---|
| `BattleEngine.CreateMatch(playerA, playerB, mode)` | `+ AiDifficulty? aiDifficulty, long nowUnixMs` | Core no puede leer el reloj ambiental (§6.2). El tiempo entra por parámetro. La dificultad hacía falta para crear partidas vs IA. |
| `ApplyPlacement` / `ApplyShots` / `ApplyForfeit` | `+ long nowUnixMs` | Ídem: fijan deadlines de fase y `LastActionUnixMs`. |
| `ResolveExpiredTurns(ref s, nowUnixMs)` | Sin cambios | Ya recibía el tiempo. |
| `BattleEngine(cfg, flags, rng)` | `+ TimerProfile timers, AiParams aiParams` | El motor calcula deadlines y hace auto-despliegue y disparo automático por timeout; necesita ambos. |
| `PlacementRules.Validate(cfg, placements)` | `+ RuleFlags flags` | La validación de adyacencia depende de `allowAdjacentShips`. |
| `PlacementRules.GenerateRandom(cfg, rng)` | `+ RuleFlags flags` | Ídem. |
| `PlacementRules.IsHumanLike(cfg, placements)` | `+ AiParams aiParams` | Los umbrales (60 % de borde, reintentos) son cifras de config, no constantes de código. |
| `ShotRules.Resolve(ref target, cell, flags)` | `+ BoardConfig cfg, int shotIndex` | Necesita la geometría para indexar el bitboard y el índice de disparo para marcar el hundimiento. |
| `MatchProjection.Project(in s, forPlayerId)` | `+ RuleFlags flags, long nowUnixMs` | La redacción de `sunkClass` / `sunkCells` depende de los flags, que no viven en el estado serializado. |
| `RewardRules.Compute(in s, playerId, cfg, stats)` | `+ BoardConfig board` | El mínimo de disparos anti-farm es distinto en `blitz` (§5.3). |
| `IAiStrategy.NextShot(in AiMemory, cfg, rng)` | Sin cambios | `AiMemory` se implementó como clase; `in` sobre un tipo por referencia es legal y mantiene la firma literal del documento. |

### Deuda aceptada

| # | Deuda | Severidad | Condición de cierre |
|---|---|---|---|
| ~~M0-1~~ | ~~Versiones de paquetes UGS de §6.5 sin verificar contra el registro real~~ | ~~Media~~ | **Cerrada 2026-07-23.** Las 15 verificadas contra `packages.unity.com`; §6.5 actualizada. Escribir `manifest.json` sigue siendo tarea de M3, ya sin riesgo de versión inventada. |
| M0-2 | Proyecto UGS y los tres entornos (`development` / `staging` / `production`) **no creados**; falta `cloudProjectId` y environment ids | Alta | Crear en el dashboard de UGS y anotar los ids. Bloquea M3. |
| M3-1 | **`ProjectBootstrap` reconstruye la escena `Main` desde cero en cada ejecución**, así que cada `Setup All` renumera los fileID y produce un diff de decenas de líneas sin cambio real. La escena sigue siendo una función pura del código —que es lo que se buscaba— pero no es estable byte a byte, y eso ensucia las revisiones y provoca conflictos de merge evitables | Baja | Reutilizar los GameObject existentes cuando la escena ya está bien formada, en vez de recrearla. Antes de que haya más de una persona tocando la escena. |
| M0-3 | `gh` **no está instalado** en la máquina de desarrollo. El remoto `origin` sí está configurado (`github.com/Hellscythe25/Armada`) | Baja | `gh auth login` cuando haga falta operar PRs/releases desde CLI. Es la deuda #12 de TTTXO, cerrada solo a medias. |
| M0-4 | `CloudCode~/ArmadaModule` tiene el `pubxml` pero **no el `.csproj`** | Media | Se crea en M3, después de verificar las versiones de `Com.Unity.Services.CloudCode.*` contra NuGet. No se inventan versiones. |
| ~~M0-5~~ | ~~Los cuerpos de `PlacementRules`, `ShotRules`, `BattleEngine`, `MatchProjection`, `RewardRules`, `EloRules` y `AiFactory` lanzan `NotImplementedException`~~ | ~~Baja (planificada)~~ | **Cerrada en M1.** Todos implementados, con 96.3 % de cobertura de líneas en Core. |
| M0-6 | Workflows `unity-tests`, `build-mobile`, `deploy-backend` y `nightly-integration` (§13.2) **no creados** | Media | `unity-tests` requiere runner con licencia de Unity; el resto requiere el proyecto UGS de M0-2. |
