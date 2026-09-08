---
tags: [proyecto/armada, gdd, arquitectura, unity, ugs, produccion]
proyecto: ARMADA (nombre en clave)
genero: Estrategia por turnos / adivinanza 1v1
version_doc: 1.0
fecha: 2026-07-23
estado: Documento fundacional — pre-M0
---

# ARMADA — Documento Técnico de Producción

> Juego de combate naval por cuadrícula (género "Battleship") para **Android / iOS / PC**, sobre **Unity 6.3 LTS + Unity Gaming Services**, **servidor autoritativo desde el minuto cero**, alcance preparado para **lanzamiento comercial** (no PoC, no alfa).
>
> Este documento es la **fuente de verdad** del proyecto: arquitectura, contrato de API, cifras de balance y economía, alcance de lanzamiento y plan de milestones. Destila los aprendizajes y **resuelve explícitamente la deuda técnica** de los dos proyectos previos (TTTXO y TCGMaster) — ver [§10](#10-deuda-heredada--resolución-explícita).

---

## Índice

1. [Cómo usar este documento](#1-cómo-usar-este-documento)
2. [Producto: qué construimos y por qué](#2-producto-qué-construimos-y-por-qué)
3. [Alcance de lanzamiento (in / out)](#3-alcance-de-lanzamiento-in--out)
4. [Diseño de juego — fuente de verdad numérica](#4-diseño-de-juego--fuente-de-verdad-numérica)
5. [Economía, progresión y monetización](#5-economía-progresión-y-monetización)
6. [Arquitectura técnica](#6-arquitectura-técnica)
7. [Contrato de API público](#7-contrato-de-api-público)
8. [Backend UGS](#8-backend-ugs)
9. [Cliente Unity](#9-cliente-unity)
10. [Deuda heredada — resolución explícita](#10-deuda-heredada--resolución-explícita)
11. [Anti-cheat y abuso](#11-anti-cheat-y-abuso)
12. [Calidad: tests, QA y presupuestos de rendimiento](#12-calidad-tests-qa-y-presupuestos-de-rendimiento)
13. [CI/CD, entornos y deploy](#13-cicd-entornos-y-deploy)
14. [Observabilidad: analytics, KPIs y LiveOps](#14-observabilidad-analytics-kpis-y-liveops)
15. [Legal, privacidad y tiendas](#15-legal-privacidad-y-tiendas)
16. [Roadmap y milestones](#16-roadmap-y-milestones)
17. [Riesgos](#17-riesgos)
18. [Estructura de repo y directivas (`CLAUDE.md`)](#18-estructura-de-repo-y-directivas-claudemd)
19. [Checklist de lanzamiento](#19-checklist-de-lanzamiento)
20. [Apéndices](#20-apéndices)

---

## 1. Cómo usar este documento

| Si sos… | Leé primero |
|---|---|
| Product / decisión de alcance | §2, §3, §5, §16 |
| Programador de gameplay (Core) | §4, §7, §12 |
| Programador de sistemas / backend | §6, §7, §8, §11, §13 |
| Cliente / UI | §7, §9 |
| QA | §4 (cifras), §12, §19 |
| Claude Code al arrancar | §18 (directivas) + §7 (contrato) + §16 (milestone actual) |

**Reglas de uso del documento**

1. **Ninguna cifra se inventa en el código.** Todo valor de balance, economía o timing sale de §4 y §5 y se espeja en Remote Config. Si falta un número, se pide — no se improvisa.
2. **El contrato de §7 se define antes de implementar.** UI y lógica se construyen en paralelo contra las mismas firmas.
3. **Toda deuda que se acepte se documenta en el momento**, con su condición de cierre, en `CHANGELOG-dev.md`. Cero deuda silenciosa.
4. Este doc se versiona en `Docs/` y sube de versión en el mismo commit que el cambio que lo motiva.

> ✅ **Sobre las versiones de paquetes**: las tablas de §6.5 provenían de los handoffs de TTTXO y TCGMaster (2026-07-23) y quedaron **verificadas contra el registro real** en M0 (ver §6.5). Las de NuGet del módulo de Cloud Code siguen pendientes y se verifican en M3. Nunca inventar ni asumir versiones — es la regla que más tiempo ahorró en los proyectos previos.

---

## 2. Producto: qué construimos y por qué

### 2.1 El juego en un párrafo

Dos jugadores despliegan en secreto una flota sobre una cuadrícula y se turnan para disparar a coordenadas del tablero rival. El servidor responde *agua*, *tocado* o *hundido*. Gana quien hunde toda la flota enemiga primero. Es el clásico de papel y lápiz de la Primera Guerra Mundial, comercializado como juego de tablero desde 1931 y en versión de tablero plástico desde 1967, con múltiples variantes documentadas (Salvo, no anunciar hundimientos, mover barcos).

### 2.2 Por qué este juego encaja con nuestra arquitectura

Battleship es, técnicamente, **un juego de información oculta**. Eso lo vuelve el caso ideal para la arquitectura que ya dominamos:

- **Si el cliente conoce el tablero rival, el juego está roto.** Un cliente modificado o un simple dump de memoria revela la flota completa. No hay ofuscación que resista. La única defensa real es que **el tablero enemigo nunca salga del servidor**.
- Nuestro patrón heredado — *servidor autoritativo + el cliente solo pinta un `MatchStateView` proyectado* — no es una decisión de gusto acá: es **la condición de existencia** del modo online.
- El estado de partida es minúsculo (dos cuadrículas de 100 casillas ⇒ bitboards de ~200 bytes serializados), lo que hace barato el modelo "todo el estado en Cloud Save, todas las reglas en Cloud Code".
- El ritmo por turnos permite **partidas asíncronas** (jugar una jugada, cerrar la app, recibir push cuando toca) — el formato con mejor retención en móvil, y prácticamente gratis con el mismo modelo de estado.

### 2.3 Plataformas

| Plataforma | Prioridad | Notas |
|---|---|---|
| Android (Google Play) | **P0** | Mercado primario. AAB, Play Games Services opcional para vinculación. |
| iOS (App Store) | **P0** | Sign in with Apple obligatorio si hay otro login social. |
| Windows Standalone | P1 | Mismo backend, mismos assets. Distribución inicial directa/itch; Steam post-lanzamiento (requiere SDK + logros + auth propia). |

**Regla de input:** una capa de abstracción única; toda la UI debe funcionar con *tap* y con *mouse + teclado* sin código condicional por plataforma. Tamaño mínimo de casilla táctil: 44 pt.

### 2.4 Principios de producto (no negociables)

1. **Anti-anuncio abusivo.** Frecuencia limitada, siempre cerrable, los rewarded son opcionales y nunca dan ventaja competitiva.
2. **IA con dificultad real y diferenciada** (§4.6). Nada de "difícil = hace trampa".
3. **Privacidad estricta.** Borrado de cuenta accesible en ≤ 3 taps desde Ajustes, resuelto server-side.
4. **Velocidad por encima del adorno.** ≤ 2 taps desde el arranque hasta estar disparando.
5. **Monetización 100% cosmética.** *Pay-to-look, nunca pay-to-win.* Ningún ítem comprable altera reglas, probabilidades, timers ni información disponible.
6. **El juego nunca se rompe por falta de red.** Modo local y vs IA plenamente jugables offline.

---

## 3. Alcance de lanzamiento (in / out)

### 3.1 Dentro del alcance v1.0

| Área | Contenido |
|---|---|
| **Modos** | Vs IA (4 dificultades, offline) · 2 jugadores local (pass-and-play) · **Quickmatch online** (tiempo real) · **Ranked** (Elo + leaderboard + temporadas) · **Asíncrono** (turnos con push) · **Partida privada por código** |
| **Tableros** | `classic` 10×10 · `blitz` 8×8 · `admiral` 12×12 |
| **Reglas** | Clásicas + variante **Salvo** + flags configurables (§4.3) |
| **Meta** | Perfil con nombre `Nombre#1234`, nivel/XP, historial de partidas, estadísticas, logros locales |
| **Economía** | Moneda blanda (COIN) + dura (GEM) sobre **UGS Economy** · tienda de cosméticos · recompensa diaria |
| **Monetización** | IAP (packs de GEM, quitar anuncios, pase de temporada) con **validación server-side** · anuncios rewarded + intersticiales con caps |
| **Cosméticos** | Temas de tablero, skins de flota, VFX de impacto, avatares, marcos, emotes |
| **Social** | Emotes/frases predefinidas y localizadas durante la partida (**sin chat de texto libre**) · rematch · compartir resultado |
| **Localización** | 10 idiomas de alfabeto latino: es, en, pt-BR, fr, de, it, tr, pl, id, vi |
| **Cuenta** | Anónimo + **vinculación (Google / Apple) disponible desde el día 1** |
| **Accesibilidad** | Modo daltónico, tamaño de texto, reducir movimiento, subtítulos de audio, alternativa no-cromática para agua/tocado |
| **Audio** | Sistema completo: música, SFX, mezclador, ducking, persistencia de volúmenes |
| **Tutorial** | Onboarding interactivo de ~90 s con despliegue guiado y primeros disparos |
| **Notificaciones** | Push nativo "es tu turno" (asíncrono) + recordatorio de recompensa diaria, con opt-out |

### 3.2 Fuera del alcance v1.0 (backlog explícito)

| Ítem | Por qué queda fuera | Ventana tentativa |
|---|---|---|
| Clanes / gremios | Coste alto, retención no probada aún | v1.3+ |
| Chat de texto libre | Obliga a moderación, reportes y compliance de UGC | Probablemente nunca |
| Torneos programados | Requiere LiveOps maduro | v1.2 |
| Modos 3-4 jugadores | Cambia el modelo de partida completo | v1.4 |
| Barcos con habilidades especiales (sonar, misil en X) | Cambia el balance base; validar primero el juego clásico | v1.2 (evaluar con datos) |
| Variante "mover barcos cada N turnos" | Variante conocida, pero complica la proyección de estado | Evaluar |
| Espectador / replays compartibles | Riesgo de fuga de información si se implementa mal | v1.3 |
| Steam (logros, cloud, auth) | Segunda cadena de identidad | v1.1–v1.2 |
| Cross-progression con Steam/consola | Depende de lo anterior | v1.3+ |

### 3.3 Criterio de "listo para lanzar"

El juego se lanza cuando se cumplen **todos** los ítems del checklist de §19, con soft launch previo de **4 semanas mínimo** en 2 mercados pequeños (propuesta: Perú + Filipinas, por coste de UA e idiomas es/en) y KPIs de §14.3 dentro de umbral.

---

## 4. Diseño de juego — fuente de verdad numérica

> Todo lo de esta sección se espeja en Remote Config (`GameConfig.rc`). El cliente **no** lee Remote Config directo: pide `GetGameConfig()` a Cloud Code (§7.3) y tiene fallback local embebido con **estos mismos valores** para funcionar offline.

### 4.1 Tableros y flotas (`BOARD_CONFIGS`)

| id | Cuadrícula | Flota (longitudes) | Casillas ocupadas | Densidad | Duración objetivo |
|---|---|---|---|---|---|
| `blitz` | 8×8 (64) | 4, 3, 3, 2 | 12 | 18.8 % | 3–5 min |
| `classic` | 10×10 (100) | 5, 4, 3, 3, 2 | 17 | 17.0 % | 6–10 min |
| `admiral` | 12×12 (144) | 5, 5, 4, 4, 3, 3, 2 | 26 | 18.1 % | 12–18 min |

Clases de barco (`ShipClass`) y longitud por tablero:

| ShipClass | `blitz` | `classic` | `admiral` |
|---|---|---|---|
| `Carrier` | — | 5 | 5 |
| `Battleship` | 4 | 4 | 5 (×1) + 4 (×1) |
| `Cruiser` | 3 | 3 | 4 |
| `Submarine` | 3 | 3 | 3 |
| `Destroyer` | 2 | 2 | 3 |
| `PatrolBoat` | — | — | 2 |

> **Nota de nomenclatura**: usamos nombres genéricos de clase naval (no protegibles). Ver §15.1 sobre el nombre del producto y la marca.

Coordenadas visibles al jugador: **columnas A–L, filas 1–12** (localizable; en idiomas con alfabetos idénticos se mantiene A–L). Internamente siempre `Coord(x, y)` con origen `(0,0)` arriba-izquierda.

### 4.2 Fases de la partida

```
Created ──► Placement ──► InProgress ──► Finished
   │            │              │            │
   │            │              │            └─ Resultado revelado: se envían AMBAS flotas
   │            │              └─ Turnos alternados; timers activos
   │            └─ Ambos despliegan en paralelo; barrera "los dos listos"
   └─ Existe el matchId; falta el rival (o el rival es la IA)
```

- **Barrera de despliegue**: la partida pasa a `InProgress` cuando ambos jugadores enviaron un despliegue válido, o cuando vence `placementSeconds` (el servidor auto-despliega al que faltó, §4.7).
- Durante `Placement`, un jugador **nunca** recibe información del otro más allá de un booleano `opponentReady`.
- El primer tirador se decide con RNG del servidor (`firstMover`), registrado en el estado y en analytics.

### 4.3 Reglas y flags (`RULE_FLAGS`)

| Flag | `classic` | `blitz` | `salvo` | Descripción |
|---|---|---|---|---|
| `allowAdjacentShips` | `true` | `true` | `true` | Si `false`, los barcos no pueden tocarse ni en diagonal (variante rusa) |
| `announceSunkShipType` | `true` | `true` | `false` | Al hundir, se anuncia qué clase se hundió |
| `revealSunkCells` | `true` | `true` | `true` | Las casillas del barco hundido se marcan en la grilla de seguimiento |
| `extraTurnOnHit` | `false` | `true` | `false` | Un impacto concede otro disparo (acelera muchísimo `blitz`) |
| `shotsPerTurn` | `1` | `1` | `= barcos propios a flote` | Modo Salvo: se declaran N coordenadas y se resuelven a la vez |
| `salvoAggregateReport` | — | — | `true` | En Salvo se reporta "2 impactos, 3 aguas" sin decir cuáles |
| `diagonalPlacement` | `false` | `false` | `false` | Nunca. Solo horizontal/vertical |

Reglas invariantes (no configurables):
- Los barcos ocupan casillas **consecutivas**, en horizontal o vertical, **sin solapamiento** y **sin salirse** del tablero.
- Ambos jugadores tienen exactamente la misma composición de flota.
- No se puede disparar dos veces a la misma casilla (`ERR_CELL_ALREADY_TARGETED`).

### 4.4 Timers (`TIMERS`)

| Clave | Tiempo real | Asíncrono | Notas |
|---|---|---|---|
| `placementSeconds` | 90 | 86 400 (24 h) | Al vencer: auto-despliegue server-side |
| `turnSeconds` | 30 | 86 400 (24 h) | Al vencer: disparo aleatorio válido automático + 1 strike |
| `maxConsecutiveTimeouts` | 3 | 2 | Al alcanzarlo: abandono ⇒ derrota |
| `reconnectGraceSeconds` | 45 | n/a | El turno no corre durante la gracia si hay desconexión detectada |
| `abandonTimeout` | 3 min | 48 h | Sin acción tras este tiempo, el rival puede reclamar victoria |
| `rematchOfferSeconds` | 30 | n/a | Ventana para aceptar revancha |

**Resolución de vencimientos**: híbrida. (a) *Perezosa*: toda llamada a Cloud Code pasa primero por `ResolveExpiredTurnsAsync` (patrón heredado de TCGMaster). (b) *Proactiva*: un **Scheduler cada 5 min** barre partidas vencidas (§8.6) — esto **cierra la deuda #6 de TTTXO**, obligatoria porque Ranked depende de que las partidas muertas no queden abiertas.

### 4.5 Resultado de disparo

`ShotOutcome`: `Miss` · `Hit` · `Sunk` · `Win` (hundimiento que termina la partida).

Respuesta del servidor por disparo:
```
ShotResult { Coord cell; ShotOutcome outcome; ShipClass? sunkClass; Coord[]? sunkCells; }
```
`sunkClass` solo si `announceSunkShipType`; `sunkCells` solo si `revealSunkCells`.

### 4.6 IA — cuatro dificultades reales (`AI_PARAMS`)

La IA vive en `Armada.Core` (C# puro) ⇒ **el mismo código corre offline en el cliente y server-side en Cloud Code**. Es determinista: recibe un `IRandom` inyectado (semilla fija en tests, CSPRNG en producción).

| Dificultad | Búsqueda | Objetivo | Precisión esperada* | Winrate objetivo vs jugador medio |
|---|---|---|---|---|
| `Easy` | Aleatorio uniforme sobre casillas no probadas | 25 % de las veces **ignora** un seguimiento obvio | ~1 impacto / 5.5 disparos | 20 % |
| `Medium` | *Hunt & Target*: caza aleatoria, al impactar ataca las 4 adyacentes | Sin paridad, sin inferencia de línea | ~1 / 4.2 | 40 % |
| `Hard` | *Parity hunt* (solo casillas con `(x+y) % 2 == 0` mientras el barco vivo más chico mida 2) + **mapa de densidad de probabilidad** (conteo exacto de emplazamientos posibles por casilla, dadas las restricciones conocidas) + inferencia de línea al segundo impacto | Óptima práctica | ~1 / 2.6 | 65 % |
| `Adaptive` | `Hard` con ruido variable: elige la k-ésima mejor casilla, con `k` derivado del rendimiento del jugador en las últimas 20 jugadas | Mantener la partida cerrada | Variable | 50 % (por diseño) |

\* Medido en `classic` sobre 10 000 partidas simuladas contra despliegues aleatorios. **Estos números son criterio de aceptación de QA**, no aspiraciones: si `Hard` no baja de ~48 disparos promedio para hundir 17 casillas, el algoritmo está mal.

Parámetros de `Adaptive`:
```
windowShots        = 20      // ventana de evaluación
targetWinRate      = 0.50
noiseFloor         = 0       // k=0 ⇒ juega óptimo
noiseCeiling       = 6       // k=6 ⇒ juega casi como Medium
adjustStep         = 1       // ±1 por ventana evaluada
```

**Despliegue de la IA**: se generan flotas aleatorias válidas, pero se **rechaza** cualquier disposición con más del 60 % de casillas de barco sobre el borde, o con dos barcos grandes paralelos adyacentes (patrones que los jugadores humanos aprenden a explotar). Máximo 10 reintentos, luego se acepta la última.

### 4.7 Auto-despliegue y disparo automático

- `GenerateRandomPlacement(config, rng)` vive en `Armada.Core` y es la **misma función** para: el botón "Aleatorio" del jugador, el despliegue de la IA y el auto-despliegue por timeout. Una sola implementación, un solo set de tests.
- Disparo automático por timeout: usa la estrategia `Medium` (no `Easy`, para no regalar la partida; no `Hard`, para no premiar el AFK).

### 4.8 Ranked, Elo y temporadas (`SEASON_CONFIG`)

| Parámetro | Valor |
|---|---|
| Rating inicial | 1000 |
| K-factor | 40 (< 10 partidas) · 32 (< 50) · 24 (≥ 50) |
| Suelo de rating | 100 |
| Partidas de colocación | 5 (rating oculto hasta completarlas) |
| Duración de temporada | 60 días |
| Reset blando | `nuevo = 1000 + (viejo − 1000) × 0.5` |
| Modo | `classic` únicamente (tablero fijo, reglas fijas) |

Rangos visibles: Recluta (< 900) · Marinero (900–1099) · Contramaestre (1100–1299) · Teniente (1300–1499) · Capitán (1500–1699) · Comodoro (1700–1899) · **Almirante** (≥ 1900).

Abandono en Ranked: derrota + **penalización adicional de −15 puntos** y cooldown de matchmaking de 5 min al tercer abandono del día.

**Ventaja del primer disparo**: teóricamente existe pero es pequeña. Decisión: se acepta en v1.0 (el reparto es aleatorio y simétrico en esperanza) y se **mide** con el KPI `first_mover_win_rate`. Si supera **53 %** de forma sostenida (n ≥ 20 000 partidas ranked), se activa una compensación ya prevista: el segundo tirador arranca con un "sonar" gratuito (revela si hay barco en una fila o columna, sin decir dónde). Está diseñado, no implementado.

### 4.9 Matchmaking (`MATCHMAKING_CONFIG`)

| Parámetro | Casual | Ranked |
|---|---|---|
| Ventana inicial de Elo | ±250 | ±100 |
| Expansión | +25 / s | +25 / s |
| Ventana máxima | ±1000 | ±600 |
| TTL del ticket | 120 s | 180 s |
| Relleno con bot tras | 45 s | **nunca** |
| Reintento tras expirar | Sugerir vs IA | Reencolar 1 vez |

> **Decisión con nota ética**: el relleno con bot en Casual sirve para que el juego no se sienta vacío en el lanzamiento (población baja). Reglas duras: **nunca en Ranked**, nunca cuenta para leaderboard, marcado en analytics como `opponent_type=bot`, la recompensa es la de IA (no la de online), y existe un kill switch en Remote Config (`BOT_FILL_ENABLED`). Si el equipo decide que no es aceptable, se apaga sin tocar código.

---

## 5. Economía, progresión y monetización

### 5.1 Monedas

| Código | Nombre visible (es) | Tipo | Obtención |
|---|---|---|---|
| `COIN` | Doblones | Blanda | Jugar, misiones diarias, nivel, rewarded ads |
| `GEM` | Cristales | Dura | **IAP** + goteo pequeño por nivel y pase |

Ambas se implementan en **UGS Economy** (no a mano en Cloud Save). Esto cierra la deuda de TCGMaster §5.2: Economy da atomicidad de balance, inventario y transacciones sin reimplementarlo.

### 5.2 Recompensas (`ECONOMY_REWARDS`)

| Evento | COIN | XP | Condición |
|---|---|---|---|
| Victoria online (Casual/Ranked/Async) | 100 | 100 | Partida válida (§5.3) |
| Derrota online | 40 | 40 | Partida válida |
| Victoria por abandono rival | 100 | 100 | — |
| Derrota por abandono propio | 0 | 0 | — |
| Victoria vs IA `Easy` / `Medium` / `Hard` / `Adaptive` | 10 / 20 / 35 / 35 | igual | Sujeto a tope diario |
| Derrota vs IA | 5 | 10 | Sujeto a tope diario |
| 2 jugadores local | 0 | 0 | Nunca otorga economía |
| Primera victoria del día | +150 | +100 | 1 / día (UTC) |
| Racha de victorias online | +10 % por victoria consecutiva, tope **+50 %** | — | Se rompe con derrota o abandono |
| Misión diaria (3 activas) | 50–120 c/u | 50 | Rotación diaria |
| Subir de nivel | 200 + 25 × nivel | — | — |

Tope diario de COIN provenientes de IA + bot fill: **150**. Sin tope para online real.

### 5.3 Anti-farming (server-side, obligatorio)

Una partida solo otorga economía si **todas** se cumplen (validado en Cloud Code, nunca en cliente):

1. Duración ≥ **60 s** de reloj de servidor.
2. El jugador recompensado hizo ≥ **12 disparos** (≥ 8 en `blitz`).
3. Ambos jugadores realizaron al menos **1 acción** después del despliegue.
4. El par `(playerA, playerB)` no jugó más de **3 partidas online entre sí en 24 h** → a partir de la 4ª, las recompensas se ponen a 0 y se emite `reward_suppressed_pair` (detección de colusión, §11.4).
5. Tope duro por cuenta: **2 500 COIN / día**.

### 5.4 Progresión de nivel

`XP requerido para nivel n = 500 + 250 × (n − 1)`, hasta nivel 60. Total ≈ 472 500 XP.
Recompensa por nivel: COIN (§5.2) + un cosmético cada 5 niveles.

### 5.5 Catálogo de cosméticos (`STORE_CATALOG`)

| Slot | Ejemplos | Precio COIN | Precio GEM |
|---|---|---|---|
| `board_theme` | Radar clásico, Carta náutica, Ártico, Neón | 800 | 250 |
| `fleet_skin` | Flota moderna, Vela, Steampunk, Papel | 1 200 | 350 |
| `hit_vfx` | Explosión, Tinta, Chispa, Sonar | 500 | 150 |
| `avatar` | 24 avatares | 300 | 100 |
| `frame` | Marcos de perfil | 400 | 120 |
| `emote` | 12 emotes/frases | 200 | 80 |

**Regla dura de diseño**: ningún cosmético puede reducir la legibilidad de la cuadrícula ni alterar el contraste agua/tocado/hundido por debajo del umbral de accesibilidad (§9.6). El cosmético se prueba con el modo daltónico activo antes de aprobarse.

### 5.6 IAP

| SKU | Contenido | Precio ref. (USD) |
|---|---|---|
| `starter_bundle` | 400 GEM + tema exclusivo (1 sola vez) | 2.99 |
| `gems_small` | 500 GEM | 1.99 |
| `gems_medium` | 1 400 GEM | 4.99 |
| `gems_large` | 3 000 GEM | 9.99 |
| `gems_xl` | 6 500 GEM | 19.99 |
| `remove_ads` | Quita intersticiales para siempre (rewarded siguen disponibles) | 3.99 |
| `season_pass` | Pase "Almirantazgo": pista cosmética de 30 niveles, 60 días | 9.99 |

**Toda compra pasa por `ValidatePurchase` en Cloud Code**: se verifica el recibo contra Google Play / App Store **antes** de conceder nada, y la concesión se hace vía UGS Economy. El cliente nunca acredita moneda. Idempotencia por `transactionId` (una compra reprocesada no duplica).

### 5.7 Publicidad (`AD_CONFIG`)

| Tipo | Regla |
|---|---|
| Intersticial | Solo al cerrar el resumen de partida · máx. **1 cada 4 partidas completadas** · mín. **240 s** entre anuncios · máx. **6/día** · **nunca** en las primeras 24 h desde la instalación · **nunca** inmediatamente después de una derrota · siempre cerrable |
| Rewarded: duplicar recompensa | Máx. 3/día, ofrecido en el resumen |
| Rewarded: cofre diario extra | 1/día |
| Rewarded: pista de sonar | **Solo en modo vs IA.** Jamás en online (sería pay-to-win) |
| Banner | No se usa |

Kill switch global `ADS_ENABLED` y por tipo en Remote Config.

---

## 6. Arquitectura técnica

### 6.1 Principios rectores

1. **El servidor decide, el cliente pinta.** Ninguna regla, recompensa, resultado o información oculta se calcula en el cliente en modo online.
2. **Lógica pura aislada del motor.** `Armada.Core` sin `UnityEngine`, reutilizada *verbatim* por Cloud Code.
3. **Una sola fuente de verdad por dato**: cifras en el design-doc → Remote Config; contenido en `ContentDatabase.json` → generador; versión visible en `bundleVersion`.
4. **Los servicios degradan, nunca rompen.** Todo servicio externo va detrás de un wrapper defensivo con fallback local.
5. **Idempotencia y versionado de protocolo desde el diseño**, no como parche.
6. **Nada de estado serializado a mano.** Escenas, PanelSettings, Locales y StringTables se generan con editor scripts idempotentes.

### 6.2 Assemblies

| Assembly | Rol | Restricciones |
|---|---|---|
| `Armada.Core` | Reglas, despliegue, resolución de disparos, IA, Elo, cálculo de recompensa | **`noEngineReferences: true`**, `netstandard2.1`, **`<Nullable>enable</Nullable>` desde el primer commit**, sin `DateTime.Now`, sin `System.Random` ambiental, sin I/O |
| `Armada.Core.Tests` | EditMode NUnit sobre `Armada.Core` | Sin Unity API, sin mocks |
| `Armada.Game` | Bootstrap, managers, UI Toolkit, servicios UGS, audio, input | Depende de `Armada.Core` |
| `Armada.Game.Tests` | PlayMode: flujo de UI, resiliencia de red | — |
| `Armada.Game.Editor` | Editor scripts idempotentes (setup de escena/UI/locales, Content Designer) | Editor-only |

El módulo de Cloud Code incluye el código fuente de Core:

```xml
<ItemGroup>
  <Compile Include="../../Assets/Scripts/Core/**/*.cs" />
</ItemGroup>
```

⇒ Las reglas y la IA son **byte-for-byte idénticas** en cliente y servidor. Es la decisión de mayor retorno de los proyectos previos y se replica sin cambios.

**Tests de arquitectura** (corren en CI, fallan el build):
- `Core_HasNoEngineReferences` — parsea el `.asmdef` y falla si `noEngineReferences != true`.
- `Core_DoesNotUseAmbientTimeOrRandom` — escanea IL/fuente buscando `DateTime.Now`, `DateTime.UtcNow`, `new Random(` sin semilla, `Guid.NewGuid`.
- `Core_IsNullableClean` — el `.csproj` de test trata `CS86xx` como error (cierra la deuda #3 de TTTXO **antes de que nazca**).
- `Game_HasNoUnregisteredMutableStatics` — reflexión sobre `Armada.Game`: todo campo `static` mutable debe estar declarado en `StaticStateRegistry` (§9.3).

### 6.3 Modelo de datos: bitboards y proyección

**Representación interna** — una cuadrícula de hasta 144 casillas cabe en 3 `ulong`:

```csharp
public readonly struct BitBoard {          // 144 bits máx
    readonly ulong _w0, _w1, _w2;
    public bool Get(int index);
    public BitBoard Set(int index);
    public int PopCount { get; }
}
```

Estado por jugador en el servidor:

```
PlayerBoard {
    BitBoard occupancy;      // dónde hay barco (SECRETO)
    BitBoard hits;           // casillas propias impactadas
    BitBoard incomingShots;  // todas las casillas que el rival disparó
    ShipRecord[] fleet;      // clase, casillas, hits, sunk
}
```

**La proyección es la pieza crítica de seguridad del proyecto.** El estado que se envía a cada jugador se construye con:

```csharp
MatchStateView Project(MatchState state, string forPlayerId);
```

y cumple estas invariantes, **verificadas por tests**:

| Invariante | Test |
|---|---|
| La vista de A nunca contiene casillas de barcos de B que no hayan sido impactadas | `Projection_ForOpponent_NeverLeaksUnhitShipCells` |
| La vista de A nunca contiene `occupancy` de B en ninguna forma (ni conteos por fila/columna) | `Projection_ForOpponent_HasNoOccupancyDerivedData` |
| Solo con `phase == Finished` se revelan ambas flotas | `Projection_RevealsBothFleets_OnlyWhenFinished` |
| Durante `Placement`, A solo ve `opponentReady: bool` | `Projection_DuringPlacement_ExposesOnlyReadyFlag` |
| El JSON serializado de la vista de A no contiene ninguna coordenada secreta de B (test de serialización, no de objeto) | `SerializedView_ContainsNoSecretCoordinates` |

> El último test es el importante: valida el **JSON final**, no el DTO. Es el que atrapa la fuga cuando alguien agrega un campo "de debug".

**Presupuesto de tamaño**: un `MatchState` completo de `classic` serializado ronda **< 1.5 KB** (bitboards en base64 + metadatos). Es lo que permite guardarlo en un único documento de Cloud Save por partida y hacer escritura condicional sin transacciones.

### 6.4 Diagrama de componentes

```
                         ┌──────────────────────────────────────────┐
   Cliente Unity 6.3 LTS │            Unity Gaming Services         │
   ─────────────────     │                                          │
   Armada.Game           │   Authentication (anónimo + link)        │
     ├ UI Toolkit        │            │                             │
     ├ Armada.Core ──────┼── (mismo código fuente) ──┐              │
     │   (offline/IA)    │                           ▼              │
     ├ UgsGateway ───RPC─┼──────────────►  ArmadaModule (Cloud Code, .NET 9)
     │                   │                     │  │  │  │           │
     │◄──── Wire push ───┼─────────────────────┘  │  │  │           │
     │   (tick + seq)    │                        │  │  │           │
     │                   │   Remote Config ◄──────┘  │  │           │
     │                   │   (GameConfig.rc)         │  │           │
     │                   │                           │  │           │
     │                   │   Cloud Save  ◄───────────┘  │           │
     │                   │   (MatchState / PlayerData)  │           │
     │                   │                              │           │
     │                   │   Economy · Leaderboards ◄────┘           │
     │                   │   Analytics · Diagnostics · Scheduler     │
     │                   │                                          │
   Push nativo (FCM/APNs) ◄── HTTP desde Cloud Code ─────────────────┘
                         └──────────────────────────────────────────┘
   Matchmaker (com.unity.services.multiplayer) ── ticket ──► MatchId compartido
```

### 6.5 Stack y versiones

> ✅ **Verificado contra el registro real** (`packages.unity.com`) el **2026-07-23**, en M0. Las versiones de esta sección ya no son "propuestas": son las que se fijan en `manifest.json` en M3. Cualquier cambio futuro se vuelve a verificar contra el registro antes de escribirlo acá.

**Motor / cliente**

| Item | Versión fijada | Nota de verificación |
|---|---|---|
| Unity Editor | **`6000.3.20f1`** — Unity 6.3 LTS (ver §6.6) | Instalado y fijado en `ProjectVersion.txt` |
| Render pipeline | URP `17.3.0` (2D) | Ya en el `manifest.json` del proyecto |
| UI | **UI Toolkit con `UIDocument`** (UXML + USS + controladores C#). `PanelRenderer` **no existe en 6.3 LTS** (§6.6). Nunca uGUI/TextMeshPro | — |
| Input | Input System `1.19.0` | Ya en el `manifest.json` del proyecto |
| Localización | `com.unity.localization` `1.5.12` | Existe. **Corrección**: arrastra Addressables **`1.25.0`**, no `2.9.1` |
| Addressables | `com.unity.addressables` **`2.9.1`** (fijado explícitamente) | `unity >= 2023.1`. Se fija a mano porque Localization solo exige `1.25.0`. **No** se sube a `3.1.0`: es un salto mayor que además cambia `scriptablebuildpipeline` de `2.6.1` a `3.1.1`, y Localization `1.5.12` está validado contra la línea 2.x |
| Notificaciones | `com.unity.mobile.notifications` **`2.4.3`** | Resuelto (estaba como "verificar"). `unity >= 2021.3` |
| IAP | `com.unity.purchasing` **`5.4.1`** | Resuelto (estaba como "verificar"). `unity >= 2022.3`. **Arrastra `com.unity.ugui`**: es una dependencia inevitable del paquete y no contradice la regla de UI — la prohibición es *usar* uGUI para nuestras pantallas, no que el paquete exista en el proyecto |
| Ads | Unity LevelPlay / Unity Ads | Sin decidir: se elige y se verifica en **M6** |

**UGS**

| Paquete | Versión fijada | Rol | Nota de verificación |
|---|---|---|---|
| `com.unity.services.core` | `1.18.0` | Bootstrap de servicios | Coincide con `latest` |
| `com.unity.services.authentication` | `3.7.3` | Anónimo + vinculación | Coincide con `latest` |
| `com.unity.services.cloudcode` | `2.10.4` | RPC + suscripción Wire | Coincide con `latest` |
| `com.unity.services.cloudsave` | `3.4.1` | Estado de partida y datos de jugador | Coincide con `latest` |
| `com.unity.remote-config` | `4.2.5` | Config-as-code | Coincide con `latest` |
| `com.unity.services.economy` | **`3.5.4`** | Moneda, inventario, transacciones | Subida de parche sobre el `3.5.3` original |
| `com.unity.services.leaderboards` | **`2.3.4`** | Ladder de Ranked | Resuelto (estaba como "verificar") |
| `com.unity.services.analytics` | `6.3.0` | Telemetría | Coincide con `latest` |
| `com.unity.services.multiplayer` | **`2.3.0`** | **Matchmaking** (incluye Wire transitivo) | Subida de minor sobre el `2.2.3` original. Motivo: `2.3.0` pide exactamente `services.core 1.18.0` y `authentication 3.7.0`, que son las versiones que ya fijamos; `2.2.3` pide `1.16.0` / `3.6.0` y dejaría el grafo desalineado |
| `com.unity.services.deployment` | `1.7.2` | Deploy desde el Editor | Coincide con `latest`. También llega transitivamente por `multiplayer` |

> ⚠️ **`com.unity.services.matchmaker` está DEPRECADO en Unity 6.** El paquete sigue publicado (`1.2.0` en el registro), lo cual lo vuelve fácil de instalar por error. **No se usa.** El matchmaking va dentro de `com.unity.services.multiplayer`. La suscripción Wire real se consume con `CloudCodeService.Instance.SubscribeToPlayerMessagesAsync`.

**Cloud Code (proyecto .NET aparte, `/CloudCode~/ArmadaModule`)**

| Item | Valor |
|---|---|
| TargetFramework | `net9.0`, `<Nullable>enable</Nullable>` |
| Publish | `PublishReadyToRunComposite=true`, runtime `linux-x64` |
| `Com.Unity.Services.CloudCode.Core` | 0.0.4 *(verificar)* |
| `Com.Unity.Services.CloudCode.Apis` | 0.0.26 *(verificar)* |
| `Microsoft.Extensions.Logging.Abstractions` | **9.0.0** — alineado con el runtime (cierra la deuda de desalineación 7.x↔net9 de TCGMaster) |

Requisitos de deploy que ya nos costaron tiempo antes:
- **`Properties/PublishProfiles/FolderProfile.pubxml` es obligatorio** o el Deployment window falla con *"Could not find a Publish Profile"*.
- Registrar `IGameApiClient` con `config.AddGameApiClient()` (el viejo `GameApiClient.Create()` está obsoleto).
- Patrón `ICloudCodeSetup` / `ModuleSetup` + funciones `[CloudCodeFunction]`.
- **Un solo módulo.** Nada de scaffolds `GameApi` deployados "por si acaso" (deuda de TCGMaster).

### 6.6 Decisión: Unity 6.3 LTS, no 6.5

**Se fija Unity 6.3 LTS (`6000.3.x`).** Es la decisión correcta para este proyecto y conviene dejar el porqué escrito, porque el Hub empuja hacia la versión más nueva.

En el esquema de releases de Unity 6 conviven dos líneas:

| Línea | Ejemplo | Soporte | Recomendada por Unity para |
|---|---|---|---|
| **LTS** | **6.3 LTS (`6000.3.x`)** | 2 años (hasta **diciembre de 2027**; +1 año para Enterprise/Industry) | Juegos **live service** y proyectos que van a fijar versión para producción |
| **Update** | 6.5 (`6000.5.x`) | Solo hasta que sale la siguiente release | Producciones nuevas o a mitad de ciclo que quieran features y plataformas nuevas antes |

Por qué LTS acá:

1. **ARMADA es un live service.** Va a estar en tiendas recibiendo parches durante años; el ciclo de soporte tiene que cubrir el LiveOps, no solo el desarrollo. 6.3 LTS cubre hasta fines de 2027 con parches y actualizaciones críticas de plataforma.
2. **Una Update release obliga a saltar de versión para seguir recibiendo fixes.** En un juego con backend acoplado, cada salto de Editor es una regresión potencial en build, IAP, notificaciones y firmas de plataforma — justo lo que no se quiere en medio de un soft launch.
3. **Ecosistema verificado.** Los paquetes y assets de terceros se validan primero contra la LTS vigente.
4. El proyecto de referencia TTTXO ya corría sobre `6000.3.20f1`, así que la línea está probada con este mismo stack de UGS.

**Consecuencia concreta a tener en cuenta**: `PanelRenderer` (el sucesor nativo de `UIDocument`) es de **6.5+**. En 6.3 LTS se usa **`UIDocument`**, que sigue plenamente soportado (queda catalogado como *legacy* en las versiones nuevas, pero funciona y recibe fixes). Práctica: encapsular el acceso al panel en una fachada propia (`UiPanelHost`) para que la eventual migración a `PanelRenderer` en un futuro salto de LTS toque un solo archivo y no las ~16 pantallas.

> ⚠️ Si se reutiliza código de UI de **TCGMaster** (que corre sobre `6000.5.3f1` con `PanelRenderer`), hay **coste de migración inverso**: los `RegisterUIReloadCallback` y el manejo de ciclo de vida de `PanelRenderer` deben reescribirse contra `UIDocument.rootVisualElement`. Presupuestarlo en M2.

**Regla operativa**: se fija una versión de parche exacta en M0 (ej. `6000.3.x` concreta), se anota en `ProjectVersion.txt` y en la imagen de CI, y **no se toca hasta después del lanzamiento** salvo fix de seguridad o requisito de tienda. Los saltos de parche dentro de la misma LTS son de bajo riesgo pero no gratis: se validan con la suite completa antes de adoptarlos.

---

## 7. Contrato de API público

> Se define **antes** de implementar. UI, Core y módulo se construyen en paralelo contra estas firmas. Cambiar una firma = subir `GameProtocol.Version`.

### 7.1 Tipos compartidos (`Armada.Core`)

```csharp
namespace Armada.Core;

public readonly struct Coord {
    public readonly byte X, Y;
    public Coord(byte x, byte y);
    public static bool TryParse(string a1Notation, out Coord coord);   // "C7"
    public string ToA1();
}

public enum Orientation : byte { Horizontal = 0, Vertical = 1 }

public enum ShipClass : byte {
    Carrier = 0, Battleship = 1, Cruiser = 2,
    Submarine = 3, Destroyer = 4, PatrolBoat = 5
}

public readonly struct ShipPlacement {
    public readonly ShipClass Class;
    public readonly Coord Bow;
    public readonly Orientation Orientation;
}

public enum ShotOutcome : byte { Miss = 0, Hit = 1, Sunk = 2, Win = 3 }

public enum MatchPhase : byte { Created = 0, Placement = 1, InProgress = 2, Finished = 3 }

public enum MatchEndReason : byte {
    FleetDestroyed = 0, Forfeit = 1, Abandoned = 2, TimeoutStrikes = 3, Cancelled = 4
}
```

### 7.2 Reglas puras (`Armada.Core`) — API estable

```csharp
public static class PlacementRules {
    public static PlacementValidation Validate(BoardConfig cfg, IReadOnlyList<ShipPlacement> placements);
    public static IReadOnlyList<ShipPlacement> GenerateRandom(BoardConfig cfg, IRandom rng);
    public static bool IsHumanLike(BoardConfig cfg, IReadOnlyList<ShipPlacement> placements); // heurística §4.6
}

public static class ShotRules {
    public static ShotResolution Resolve(ref PlayerBoard target, Coord cell, RuleFlags flags);
    public static bool IsFleetDestroyed(in PlayerBoard board);
}

public sealed class BattleEngine {
    public BattleEngine(BoardConfig cfg, RuleFlags flags, IRandom rng);
    public MatchState CreateMatch(string playerA, string? playerB, MatchMode mode);
    public ApplyResult ApplyPlacement(ref MatchState s, string playerId, IReadOnlyList<ShipPlacement> p);
    public ApplyResult ApplyShots(ref MatchState s, string playerId, IReadOnlyList<Coord> cells, long expectedSequence);
    public ApplyResult ApplyForfeit(ref MatchState s, string playerId);
    public ApplyResult ResolveExpiredTurns(ref MatchState s, long nowUnixMs);
}

public interface IAiStrategy {
    Coord NextShot(in AiMemory memory, BoardConfig cfg, IRandom rng);
    void Observe(Coord cell, ShotOutcome outcome, ShipClass? sunk);
}
public static class AiFactory { public static IAiStrategy Create(AiDifficulty d, AiParams p); }

public static class MatchProjection {
    public static MatchStateView Project(in MatchState s, string forPlayerId);
}

public static class RewardRules {
    public static RewardBreakdown Compute(in MatchState s, string playerId, EconomyConfig cfg, PlayerDailyStats stats);
}

public static class EloRules {
    public static (int newA, int newB) Apply(int a, int b, double scoreA, int gamesA, int gamesB, EloConfig cfg);
}

public interface IRandom { int Next(int maxExclusive); }
```

`IRandom` es el único seam de aleatoriedad. En tests: `SeededRandom(1234)`. En Cloud Code: `CryptoRandom`. En cliente offline: `UnityRandomAdapter`.

### 7.3 Endpoints de Cloud Code (`ArmadaModule`)

Todos devuelven `Result<T>` con `error: "ERR_CODE|p1|p2"` en caso de fallo. Todos los que mutan aceptan `expectedSequence`.

**Sesión y configuración**

| Función | Firma | Notas |
|---|---|---|
| `GetServerInfo` | `() → ServerInfo{ protocolVersion, serverTimeUtc, minClientVersion, killSwitches }` | Handshake en el boot. Bloquea sesiones con protocolo incompatible |
| `GetGameConfig` | `() → GameConfig` | **Único** punto de lectura de config. El cliente nunca lee Remote Config directo |
| `GetPlayerBootstrap` | `() → { profile, economy, inventory, activeMatches[], dailyState, seasonState }` | Una sola llamada al arrancar (patrón heredado; ahorra 5 RPC) |

**Partida**

| Función | Firma |
|---|---|
| `CreateMatch` | `(mode, boardId, ruleSetId, aiDifficulty?, privateCode?) → MatchStateView` — **idempotente por `clientRequestId`** |
| `JoinMatch` | `(matchId | privateCode) → MatchStateView` |
| `SubmitPlacement` | `(matchId, ShipPlacement[], expectedSequence) → MatchStateView` |
| `RequestAutoPlacement` | `(matchId) → MatchStateView` |
| `FireShot` | `(matchId, Coord[], expectedSequence) → { view, ShotResult[] }` — 1 coord salvo Salvo |
| `GetMatchState` | `(matchId) → MatchStateView` — resync canónico y resolución perezosa de vencimientos |
| `ForfeitMatch` | `(matchId) → MatchStateView` |
| `SendEmote` | `(matchId, emoteId) → void` — rate-limited a 1 / 5 s |
| `OfferRematch` / `AcceptRematch` | `(matchId) → MatchStateView` |

**Matchmaking**

| Función | Firma |
|---|---|
| `FindMatch` | `(mode, boardId) → { ticketId }` |
| `CancelMatchmaking` | `() → void` |
| `ClaimMatchFromTicket` | `(ticketId, matchId) → MatchStateView` — handoff en 2 pasos sobre el `MatchId` compartido (el SDK de Matchmaker **no expone el roster**) |

**Meta / economía**

| Función | Firma |
|---|---|
| `GetPlayerProfile` / `SetPlayerName` | `(…) → PlayerProfile` |
| `GetMatchHistory` | `(page, pageSize) → MatchSummary[]` |
| `ClaimDailyReward` | `() → RewardBreakdown` |
| `PurchaseStoreItem` | `(sku, currency) → { inventory, balances }` — compra con moneda del juego, vía Economy |
| `ValidatePurchase` | `(store, receipt, transactionId) → { granted, balances }` — **idempotente por `transactionId`** |
| `EquipCosmetic` | `(slot, itemId) → PlayerProfile` |
| `GetLeaderboardPage` | `(leaderboardId, page) → LeaderboardPage` |
| `RegisterPushToken` | `(token, platform) → void` |
| `DeleteAccountData` | `(confirmationToken) → void` — borrado real, §15.2 |

**Solo Scheduler (no invocables por jugador)**

| Función | Frecuencia |
|---|---|
| `SweepAbandonedMatches` | cada 5 min |
| `RolloverDailies` | 00:05 UTC |
| `CloseSeason` | fin de temporada |

### 7.4 Catálogo de errores `ERR_*`

El servidor **nunca** manda prosa: manda códigos con parámetros que el cliente resuelve contra Localization (`error.<CODIGO>`). Greppables en logs, estables para asserts de tests.

| Código | Parámetros | Significado |
|---|---|---|
| `ERR_PROTOCOL_MISMATCH` | serverVersion | Cliente desactualizado → pantalla de update forzado |
| `ERR_MIN_VERSION` | minVersion | Build por debajo del mínimo soportado |
| `ERR_FEATURE_DISABLED` | feature | Kill switch activo |
| `ERR_MATCH_NOT_FOUND` | — | — |
| `ERR_MATCH_FINISHED` | — | — |
| `ERR_WRONG_PHASE` | expected | Acción fuera de fase |
| `ERR_NOT_YOUR_TURN` | — | — |
| `ERR_STALE_ACTION` | serverSequence | Doble click / reintento → resync silencioso |
| `ERR_OUT_OF_BOUNDS` | coord | — |
| `ERR_CELL_ALREADY_TARGETED` | coord | — |
| `ERR_INVALID_PLACEMENT` | reason | overlap / oob / diagonal / adjacency |
| `ERR_FLEET_MISMATCH` | expected, got | Composición de flota inválida |
| `ERR_SHOT_COUNT_INVALID` | expected, got | Salvo con N incorrecto |
| `ERR_ALREADY_IN_QUEUE` | — | — |
| `ERR_RATE_LIMITED` | retryAfterSeconds | — |
| `ERR_INSUFFICIENT_FUNDS` | currency, needed | — |
| `ERR_ITEM_ALREADY_OWNED` | sku | — |
| `ERR_PURCHASE_INVALID` | reason | Recibo rechazado |
| `ERR_INTERNAL` | traceId | Log correlacionable en Diagnostics |

### 7.5 Idempotencia y versionado

- **`sequence`**: contador que sube con cada mutación del `MatchState`. El cliente envía `expectedSequence`; si no coincide, el servidor rechaza con `ERR_STALE_ACTION|<serverSeq>` y el cliente hace resync silencioso. Mata dobles clicks y reintentos de red.
- **`clientRequestId`**: GUID por operación de creación (`CreateMatch`, `ClaimDailyReward`). Reintentos devuelven el mismo resultado, no crean duplicados.
- **`GameProtocol.Version`** (`int`, espejado en módulo y cliente): sube **solo** con cambios incompatibles de contrato, **siempre en el mismo commit** en ambos lados. No confundir con `bundleVersion` (versión visible al jugador, sube en todo cambio que llega a jugadores).


---

## 8. Backend UGS

### 8.1 Servicios y rol

| Servicio | Rol | Decisión de producción |
|---|---|---|
| **Authentication** | Identidad | Anónimo al primer arranque + **vinculación Google/Apple disponible desde el día 1** (no en beta): con IAP activo, "teléfono nuevo = progreso perdido = reembolso" |
| **Cloud Code** (`ArmadaModule`) | Dueño de toda la lógica | Módulo **único**, .NET 9, DI vía `ICloudCodeSetup` |
| **Remote Config** | Config y kill switches | Config-as-code (`GameConfig.rc`), leído **solo** por Cloud Code |
| **Cloud Save** | Estado autoritativo | 1 documento por partida (`match:<matchId>`) + datos de jugador |
| **Economy** | Moneda, inventario, transacciones | **Adoptado desde el inicio** (cierra deuda de TCGMaster) |
| **Matchmaker** (vía `com.unity.services.multiplayer`) | Emparejamiento | **Adoptado desde el inicio** en vez de cola casera (cierra deuda de TCGMaster) |
| **Wire** | Push in-app "algo cambió" | Nunca es fuente de verdad: es un *tick* |
| **Leaderboards** | Ladder de Ranked | `ranked_elo_s{N}` por temporada |
| **Analytics** | Telemetría | Todos los eventos registrados en el **Event Manager antes** de emitirse |
| **Cloud Diagnostics** | Crash/exception reporting | `CrashReportHandler.SetUserMetadata` con PlayerId y matchId activo |
| **Scheduler** | Cron (barrido, dailies, temporadas) | **Adoptado** (cierra deuda #6 de TTTXO) |

### 8.2 Esquema de Cloud Save

| Clave | Ámbito | Contenido | Escritura |
|---|---|---|---|
| `match:<matchId>` | Custom Data | `MatchState` completo (bitboards en base64, sequence, timers, playerIds) | Solo Cloud Code, **escritura condicional por `writeLock`/ETag** |
| `mmhandoff:<ticketId>` | Custom Data | Puntero de handoff Matchmaker → matchId | Cloud Code, TTL corto |
| `player.profile` | Player Data (protegido) | nombre, avatar, marco, nivel, XP, país | Cloud Code |
| `player.stats` | Player Data (protegido) | partidas, victorias, precisión, rachas, elo, temporada | Cloud Code |
| `player.daily` | Player Data (protegido) | contadores anti-farm del día (UTC), rewarded vistos | Cloud Code |
| `player.history` | Player Data (protegido) | últimas 50 `MatchSummary` | Cloud Code |
| `player.push` | Player Data (protegido) | token FCM/APNs, plataforma, opt-in | Cloud Code |
| `settings.local` | **PlayerPrefs local** | volúmenes, idioma, accesibilidad, vibración | Cliente |

> **Cloud Save no tiene transacciones.** Estrategia: (a) la moneda y el inventario viven en **Economy**, que sí es atómico; (b) el `MatchState` usa **escritura condicional** — si el ETag cambió, se relee y se reintenta hasta 3 veces, y si falla se devuelve `ERR_STALE_ACTION`; (c) el emparejamiento lo hace **Matchmaker**, no una cola en documento único. Con eso desaparecen las tres carreras conocidas de los proyectos previos.

### 8.3 Remote Config (`GameConfig.rc`)

Claves: `BOARD_CONFIGS`, `RULE_SETS`, `TIMERS`, `AI_PARAMS`, `ECONOMY_REWARDS`, `ANTIFARM_CAPS`, `MATCHMAKING_CONFIG`, `SEASON_CONFIG`, `STORE_CATALOG`, `AD_CONFIG`, `PUSH_CONFIG`, `KILL_SWITCHES`.

`KILL_SWITCHES` (apagables sin release):
```
ONLINE_ENABLED · RANKED_ENABLED · ASYNC_ENABLED · IAP_ENABLED · ADS_ENABLED
BOT_FILL_ENABLED · PUSH_ENABLED · REMATCH_ENABLED · EMOTES_ENABLED
MIN_CLIENT_VERSION · MAINTENANCE_MESSAGE_KEY
```

Cada uno tiene su ruta de degradación definida en el cliente: `ONLINE_ENABLED=false` ⇒ el botón online muestra el aviso de mantenimiento localizado y el resto del juego funciona.

### 8.4 Wire: modelo de notificación

Mensaje mínimo, nunca portador de verdad:
```json
{ "type": "match_updated", "matchId": "…", "sequence": 42 }
```
El cliente compara `sequence` con su copia local: si hay hueco, llama `GetMatchState`. Si el mensaje se pierde, un poll de respaldo cada 15 s (solo con la app en foreground y partida en tiempo real activa) garantiza convergencia.

**Optimización de coste diferida (documentada, no implementada en v1.0)**: el push puede llevar el *delta proyectado* firmado con `sequence`, y el cliente solo llama `GetMatchState` si detecta hueco. Reduce las invocaciones de Cloud Code por partida casi a la mitad (§8.7). Se implementa **si y solo si** el coste o la latencia medidos lo justifican; la métrica que dispara la decisión es `p95_shot_roundtrip_ms > 400` o coste/partida por encima del presupuesto.

### 8.5 Push nativo (cierra la deuda #5 de TTTXO)

Wire solo vive con la app abierta; el modo asíncrono es inviable sin push real.

- **Registro**: el cliente obtiene el token (FCM en Android, APNs en iOS) vía `com.unity.mobile.notifications` y lo envía con `RegisterPushToken`. Se guarda en `player.push` y se **borra** con la cuenta.
- **Envío**: `ArmadaModule` llama a la **API HTTP v1 de FCM** (que cubre también iOS vía APNs) con credenciales de service account guardadas como secreto del módulo.
- **Eventos que disparan push**: es tu turno (async, con debounce de 1/hora por partida) · tu partida vence en 2 h · recompensa diaria disponible (opt-in, máx. 1/día) · fin de temporada.
- **Fallback**: notificación **local programada** (`com.unity.mobile.notifications`) para el vencimiento de turno conocido, por si el push remoto falla.
- **Opt-out** granular en Ajustes; sin push, el juego sigue funcionando.

> ⚠️ **Spike obligatorio en M3**: confirmar que un módulo Cloud Code C# puede hacer salida HTTP a `fcm.googleapis.com` y cómo se almacenan los secretos del módulo. Si hubiera restricción de red saliente, el plan B es una Cloud Function propia (GCP/Cloudflare Worker) invocada desde el módulo o disparada por Scheduler. **Esto se valida antes de comprometer el modo asíncrono en el alcance.**

### 8.6 Scheduler (cron)

| Job | Frecuencia | Qué hace |
|---|---|---|
| `SweepAbandonedMatches` | 5 min | Cierra partidas vencidas, aplica Elo/recompensas, notifica por Wire y push |
| `RolloverDailies` | 00:05 UTC | Resetea contadores anti-farm, rota misiones diarias |
| `CloseSeason` | Fin de temporada | Congela leaderboard, reparte recompensas, aplica reset blando |
| `PurgeStaleMatches` | Diario | Borra `match:*` finalizadas hace > 30 días (mantiene `player.history`) |

### 8.7 Economía de llamadas por partida (presupuesto de coste)

Partida `classic` típica: ~55 disparos por jugador, ~110 mutaciones totales.

| Recurso | Sin optimizar | Con `FireShot` devolviendo la vista completa | Con delta en Wire (diferido) |
|---|---|---|---|
| Invocaciones Cloud Code | ~230 | **~120** | ~65 |
| Escrituras Cloud Save | ~115 | ~115 | ~115 |
| Mensajes Wire | ~110 | ~110 | ~110 |

**Decisión v1.0**: `FireShot` devuelve la vista proyectada completa del tirador (el tirador nunca necesita `GetMatchState`); el rival hace un `GetMatchState` por tick. ~120 invocaciones/partida es el presupuesto contra el que se mide.

> El coste real en dólares depende del pricing vigente de UGS y del plan contratado; **no se estima acá**. Lo que sí queda fijado es el **consumo en unidades por partida**, que es lo que se multiplica por la tarifa y lo que hay que vigilar cuando escale el DAU. Alerta si `cloud_code_invocations_per_match > 150`.

### 8.8 Cuentas de servicio y permisos (planear temprano)

Las APIs admin/cross-player desde Cloud Code (estado de ticket de Matchmaker, escrituras en datos de otro jugador, Leaderboards administrativos) requieren **service token + roles en la service account**, no el token del jugador. Sin eso: `401 Unauthorized`.

Roles a crear **en M3, no en M7**:

| Service account | Roles | Usado por |
|---|---|---|
| `armada-module-runtime` | Cloud Save Editor, Economy Admin, Leaderboards Admin, Matchmaker Admin, Remote Config Viewer | Funciones del módulo |
| `armada-ci-deploy` | Deployment | GitHub Actions |
| `armada-liveops` | Remote Config Editor | Operación (humanos) |

La verificación del ticket de Matchmaker es **hard, no best-effort** (cierra la deuda #1 de TTTXO): si no se puede verificar, la partida ranked **no se crea**.

---

## 9. Cliente Unity

### 9.1 Estructura de escena y UI

- **Una sola escena persistente** (`Main`) con paneles UI Toolkit, salvo `Splash`. Nada de cargar escenas por pantalla.
- **UI Toolkit con `UIDocument`** (UXML + USS + controladores C#), nunca uGUI/TextMeshPro. `PanelRenderer` es 6.5+ y no está disponible en la LTS elegida (§6.6). El rewireo de UI debe ser **idempotente**: los callbacks de recarga pueden dispararse más de una vez.
- Colores y tipografías **solo** desde variables USS en `:root`. Ningún hex hardcodeado fuera de ahí — es lo que hace posible el sistema de temas cosméticos y el modo daltónico.
- Layout desde wireframes; ≤ 2 taps hasta estar jugando.

Pantallas v1.0: Splash · Home · Selección de modo · Despliegue · Partida · Resumen · Perfil · Historial · Tienda · Colección/Cosméticos · Ranked/Leaderboard · Misiones · Ajustes · Ajustes ▸ Privacidad · Tutorial · Update forzado · Mantenimiento.

### 9.2 Capa de servicios (`UgsGateway`)

Toda llamada a UGS pasa por una fachada única con:
- **Reintento** con backoff exponencial (3 intentos, 250 ms → 1 s → 4 s) solo para errores transitorios.
- **Deduplicación** por `clientRequestId`.
- **Traducción** de `ERR_*` a claves de Localization.
- **Wrapper defensivo**: cualquier excepción no esperada degrada a warning + estado offline; nunca burbujea a un crash.
- **Cola offline** para acciones no críticas (analytics, equipar cosmético) que se reintentan al recuperar red.

### 9.3 Blindaje contra "Enter Play Mode sin domain reload"

Familia de NRE recurrente en los dos proyectos previos. Se blinda **desde la plantilla**, no como parche:

1. Todo estado estático mutable se registra en `StaticStateRegistry` y se resetea en
   `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`.
2. Tras **cada** `await` en código de UI: `if (rootVisualElement == null || !document) return;` — con `UIDocument`, el árbol puede haberse recargado durante la espera.
3. Test de arquitectura `Game_HasNoUnregisteredMutableStatics` (§6.2) que falla el build si alguien agrega un `static` suelto.
4. `Enter Play Mode Options` activado en el proyecto **desde el día 1**, para que el bug aparezca en desarrollo y no en producción.

### 9.4 Offline-first y degradación

| Escenario | Comportamiento |
|---|---|
| Sin red al arrancar | Home funcional, modos local y vs IA disponibles, online con aviso |
| `GetGameConfig` falla | Fallback a los valores embebidos de `Armada.Core` (los de §4/§5) |
| Localization no inicializó | Mini-tabla embebida es/en (`BootTexts`) para la pantalla de carga |
| Analytics falla | try/catch → warning. Nunca bloquea |
| Wire cae | Poll de respaldo cada 15 s |
| Se pierde red en partida online | Banner de reconexión + gracia de 45 s + resync por `GetMatchState` |
| Economy no responde | La tienda muestra estado "no disponible"; el juego sigue |

### 9.5 Localización

- 10 idiomas de alfabeto latino: **es, en, pt-BR, fr, de, it, tr, pl, id, vi**.
- Español **neutro latinoamericano** (nada de voseo rioplatense).
- `UiText` resuelve vía Unity Localization con **fallback duro al inglés** embebido.
- **Font asset con cobertura latina extendida (tr / vi / pl) configurado y verificado en M8** — cierra la deuda #4 de TTTXO. QA lo valida con una pantalla de prueba que renderiza el set completo de glifos por idioma.
- Ningún texto de jugador hardcodeado. Excepción única y deliberada: `BootTexts` (§9.4).
- Unity Localization arrastra **Addressables**; el content build **no corre en Play mode** — se ejecuta en el paso de build de CI.

### 9.6 Accesibilidad (requisito de lanzamiento, no "nice to have")

| Función | Detalle |
|---|---|
| Modo daltónico | 3 paletas (protanopía, deuteranopía, tritanopía). Agua/tocado/hundido **nunca** se distinguen solo por color: llevan forma distinta (punto / cruz / casco) |
| Escala de texto | 100 % / 125 % / 150 % sin romper layout |
| Reducir movimiento | Desactiva parallax, sacudidas y transiciones largas |
| Alto contraste | Variante USS de la cuadrícula |
| Feedback no visual | Vibración distinta para agua/tocado/hundido (opt-out) + audio diferenciado |
| Objetivos táctiles | ≥ 44 pt |
| Sin dependencia de audio | Todo evento sonoro tiene contraparte visual |

### 9.7 Audio (cierra la deuda #10 de TTTXO)

Sistema completo desde M8, no sliders huérfanos: `AudioMixer` con buses Master/Music/SFX/UI, ducking de música durante explosiones, pool de fuentes, persistencia de volúmenes, respeto del silencio del sistema, y pausa correcta al perder foco. Presupuesto: ~35 SFX, 4 pistas de música (menú, despliegue, partida, victoria/derrota).

### 9.8 Contenido: fuente única + generador

Replicamos el acierto del **Card Designer** de TCGMaster desde el día 1:

- **`Content/ContentDatabase.json`** es la única fuente de: catálogo de cosméticos, misiones diarias, pista del pase de temporada, textos de logros y metadatos de tableros.
- Una ventana de Editor **"Content Designer"** con botón **GENERATE ALL** produce: `GameConfig.rc` (fragmentos), `DefaultCatalogSeed.cs` (fallback embebido), CSVs de Localization y los `ScriptableObject` de arte.
- **Prohibido editar a mano** cualquiera de los archivos generados. Un test de CI compara el hash del generado contra el committeado y falla si divergen.

Se define **antes** de crear el primer cosmético. Mantener el mismo dato en tres archivos a mano fue una de las fricciones más caras del proyecto anterior.

### 9.9 La "última milla" manual — automatizada

En TCGMaster el PoC no podía terminarse en remoto: wireo de escena, PanelSettings y deploys exigían el Editor abierto. Mitigación en ARMADA:

- Todo asset serializado se genera con **editor scripts idempotentes** expuestos como MenuItems y agrupados en un único
  `Armada.Editor.ProjectBootstrap.SetupAll()`.
- CI ejecuta ese método en batchmode:
  `Unity -batchmode -quit -executeMethod Armada.Editor.ProjectBootstrap.SetupAll`
- Nunca se editan a mano `.unity`, `.asset`, PanelSettings, Locales ni StringTables en YAML.
- Los deploys de Cloud Code / Remote Config se hacen por CLI en CI (§13), no solo desde el Deployment window.

---

## 10. Deuda heredada — resolución explícita

### 10.1 Deuda de TTTXO

| # | Deuda | Resolución en ARMADA | Cuándo |
|---|---|---|---|
| 1 | Verificación de ticket de Matchmaker best-effort (degrada en 401) | **Hard.** Service account `armada-module-runtime` con rol Matchmaker Admin creada en M3; sin verificación válida no se crea partida ranked | M3/M4 |
| 2 | Lectura de Remote Config desde Cloud Code con firma dudosa | Spike de 1 día en M3 validando `AssignSettingsGetAsync` con IntelliSense contra el SDK real; se encapsula en `ConfigProvider` con cache TTL 2 min + **seed embebido** | M3 |
| 3 | 9 warnings nullable en Core | **`<Nullable>enable</Nullable>` desde el primer commit** + `TreatWarningsAsErrors` para CS86xx en Core | M1 |
| 4 | Font asset sin cobertura latina extendida | Tarea explícita en M8 con pantalla de verificación de glifos por idioma en QA | M8 |
| 5 | Sin push nativo | **FCM/APNs implementado** (§8.5), con spike de viabilidad en M3 | M3 (spike) / M7 |
| 6 | Sin barrido proactivo de partidas abandonadas | **Scheduler cada 5 min** (§8.6) | M4 |
| 7 | Carrera en el puntero de handoff (Cloud Save sin transacciones) | Matchmaker + handoff con **escritura condicional (ETag)** y `clientRequestId` idempotente; test de concurrencia en M4 | M4 |
| 8 | Tienda / IAP no implementada | **En alcance de lanzamiento** (M6) con `ValidatePurchase` server-side e idempotencia por `transactionId` | M6 |
| 9 | Ranked / Leaderboards / MMR no implementado | **En alcance** (M7) con Elo, temporadas y reset blando (§4.8) | M7 |
| 10 | Audio no implementado | **En alcance** (M8), sistema completo (§9.7) | M8 |
| 11 | Vinculación de cuenta sin UI | **Desde el día 1**, requisito por tener IAP en v1.0 | M3 |
| 12 | Commits solo locales (git sin `gh` autenticado) | `gh auth login` + remoto configurado en **M0**; CI exige push | M0 |

### 10.2 Deuda de TCGMaster

| Deuda | Resolución en ARMADA |
|---|---|
| Cola de matchmaking no atómica en documento único | **UGS Matchmaker desde el inicio.** La cola casera no se implementa nunca |
| Partidas abandonadas sin cron | **Scheduler** proactivo + resolución perezosa (defensa en profundidad) |
| Constantes de recompensa espejadas en el cliente | El cliente **no** espeja recompensas: el servidor devuelve el `RewardBreakdown` ya calculado y la UI lo pinta. El único espejo permitido es el fallback offline de config, que solo se usa en modos sin economía online |
| Textos de UI hardcodeados | Localization desde M1; el único hardcode permitido es `BootTexts` |
| Mismatch `net9.0` ↔ `netstandard2.1` / `Microsoft.Extensions` 7.x | `Microsoft.Extensions.*` **9.0.x** alineado con el runtime; Core en `netstandard2.1` documentado como intencional (es lo que permite compartirlo con Unity) |
| Módulo scaffold `GameApi` deployado sin uso | **Un solo módulo.** Los ejemplos quedan fuera del deploy |
| NRE por "Enter Play Mode sin domain reload" | Blindaje desde la plantilla + test de estáticos (§9.3) |
| Economía a mano en Cloud Save | **UGS Economy** desde M5 |
| Drift de contenido multi-archivo | **Content Designer** con fuente única, definido antes del primer cosmético (§9.8) |
| Doble `.csproj` con `ModuleSetup` duplicado | Un módulo, un `.csproj`, un `ModuleSetup` |
| "Última milla" manual no automatizable | `ProjectBootstrap.SetupAll()` en batchmode desde CI (§9.9) |

### 10.3 Deuda que ARMADA acepta conscientemente

| Deuda | Por qué se acepta | Condición de cierre |
|---|---|---|
| Wire como *tick* sin payload (más invocaciones de Cloud Code) | Simplicidad y seguridad; el coste está presupuestado | Si `p95_shot_roundtrip_ms > 400` o el coste/partida supera presupuesto → delta en push (§8.4) |
| Ventaja del primer disparo sin compensar | Es pequeña y simétrica en esperanza | Si `first_mover_win_rate > 53 %` con n ≥ 20 000 → activar "sonar" compensatorio (§4.8) |
| Sin espectador ni replays | Riesgo de fuga de información; sin valor probado | v1.3, con proyección propia y tests de fuga |
| Sin cross-progression con Steam | Segunda cadena de identidad | Cuando se lance en Steam |
| Bot fill en Casual | Población baja al lanzar | Se apaga cuando el tiempo de espera p50 en Casual < 15 s sin bots |

Toda deuda nueva se registra **en el momento de aceptarla** en `CHANGELOG-dev.md`, con severidad y condición de cierre.

---

## 11. Anti-cheat y abuso

### 11.1 El vector principal: fuga de información

En este género, **la única trampa que importa es ver el tablero rival**. Defensa:

1. El `occupancy` del rival **nunca sale del servidor** hasta `Finished` (§6.3).
2. Cinco tests de proyección, incluido uno sobre el **JSON serializado**.
3. Revisión obligatoria de cualquier PR que toque `MatchProjection` o los DTO de vista (regla en `CODEOWNERS`).
4. Ningún endpoint de debug que devuelva estado crudo existe en el módulo de producción; si existe para desarrollo, va detrás de `#if DEBUG_ENV` y del entorno `development`.

### 11.2 Validaciones server-side por acción

| Acción | Validaciones |
|---|---|
| `SubmitPlacement` | Composición exacta de flota · dentro de límites · sin solapes · orientación válida · adyacencia según flag · fase correcta · el jugador no había desplegado ya |
| `FireShot` | Turno del `context.PlayerId` · fase `InProgress` · coordenada dentro de límites · casilla no disparada antes · cantidad de disparos = `shotsPerTurn` · `expectedSequence` coincide |
| `PurchaseStoreItem` | Precio del catálogo **del servidor**, nunca del cliente · fondos suficientes vía Economy · ítem no poseído |
| `ValidatePurchase` | Recibo verificado contra la tienda · `transactionId` no usado · SKU en catálogo |
| `SetPlayerName` | Longitud, charset, lista de bloqueo, rate limit 1/24 h |
| `SendEmote` | Emote en catálogo y poseído · rate limit 1/5 s |

### 11.3 Rate limiting

Por jugador y por función, en el módulo: `FireShot` 4/s · `GetMatchState` 4/s · `FindMatch` 1/3 s · resto 10/min. Exceso ⇒ `ERR_RATE_LIMITED|<segundos>`. Los contadores viven en memoria del módulo con respaldo en `player.daily` para los límites diarios.

### 11.4 Bots y colusión

Un bot que juegue óptimamente es indistinguible de un humano fuerte en una sola partida. No se persigue con detección de cliente (inútil), sino con **analítica de comportamiento agregada**:

| Señal | Umbral de sospecha |
|---|---|
| Eficiencia de disparo sostenida | Por encima del percentil 97 del óptimo teórico durante ≥ 30 partidas |
| Varianza del tiempo entre disparos | Desviación estándar < 300 ms sostenida |
| Ritmo de sesión | > 6 h continuas sin pausa |
| Repetición de pareja | Mismo par de `playerId` > 3 partidas online / 24 h ⇒ recompensas a 0 (§5.3) + evento `reward_suppressed_pair` |
| Ratio victoria/abandono anómalo | Revisión manual |

Consecuencia escalonada: *shadow flag* → exclusión del leaderboard → suspensión de Ranked. Nunca se banea de forma automática por una sola señal.

### 11.5 Lo que explícitamente NO hacemos

- No confiamos en ofuscación del cliente ni en detección de root/jailbreak como defensa.
- No enviamos "checksums" de estado desde el cliente como validación (es teatro: el cliente puede mentir).
- No hacemos anti-cheat de memoria. No hace falta: el dato secreto no está en el dispositivo.


---

## 12. Calidad: tests, QA y presupuestos de rendimiento

### 12.1 Pirámide de tests

| Nivel | Dónde | Qué cubre | Objetivo |
|---|---|---|---|
| **Unit (Core)** | `Armada.Core.Tests` (NUnit, EditMode, sin Unity) | Despliegue, disparos, hundimiento, fin de partida, IA, Elo, recompensas, proyección | **≥ 90 % de líneas en `Core`, 100 % en reglas y proyección** |
| **Property-based** | Idem | Miles de partidas aleatorias verificando invariantes | Sin contraejemplos |
| **Módulo** | `/CloudCode~/ArmadaModule/TestProject` (net9.0) | Endpoints con `context`/`gameApiClient` en null y catálogo primado por seam | ~60 tests, corren en CI sin Unity |
| **Arquitectura** | CI | `noEngineReferences`, sin tiempo/RNG ambiental, estáticos registrados, contenido generado sin drift | Verde obligatorio |
| **PlayMode** | `Armada.Game.Tests` | Flujo de pantallas, reconexión, degradación sin red | Smoke suite |
| **Integración** | CI nocturno contra entorno `development` | Partida completa 2 clientes headless, matchmaking, IAP sandbox | Verde antes de cada deploy a `production` |
| **Carga** | Manual, pre-lanzamiento | 500 partidas concurrentes simuladas | Objetivos de §12.3 |
| **Dispositivo** | Manual | Matriz de §12.4 | Sin bloqueantes |

### 12.2 Invariantes de property-based testing

Se generan despliegues y secuencias de disparo aleatorios y se verifica que **siempre**:

1. `hits.PopCount ≤ suma de longitudes de flota`.
2. Un barco marcado `Sunk` tiene **todas** sus casillas en `hits`, y ninguno no-hundido las tiene.
3. `incomingShots` nunca repite casilla.
4. La partida termina en **exactamente** el disparo que completa la última casilla de flota.
5. `sequence` es estrictamente creciente y aumenta en 1 por mutación aceptada.
6. La proyección para A es **idéntica** antes y después de cualquier acción de B que no afecte a A.
7. Con la misma semilla, la IA produce la **misma secuencia de disparos** (determinismo).
8. `Resolve` es **conmutativo respecto del orden de serialización**: guardar y recargar el estado no cambia el resultado del siguiente disparo.

### 12.3 Presupuestos de rendimiento (criterios de aceptación)

| Métrica | Objetivo |
|---|---|
| FPS en dispositivo de referencia gama media | ≥ 60 estable en partida; ≥ 30 mínimo absoluto |
| Arranque en frío → Home jugable | < 3.5 s |
| Tamaño de descarga (AAB / IPA) | < 150 MB |
| Memoria residente en partida | < 512 MB |
| `FireShot` round-trip p95 | < 400 ms |
| `FireShot` round-trip p99 | < 900 ms |
| Matchmaking Casual p50 / p95 | < 10 s / < 45 s |
| Sesiones sin crash | ≥ 99.5 % |
| ANR (Android) | < 0.35 % (umbral de Play: 0.47 %) |
| Invocaciones Cloud Code por partida | ≤ 150 (§8.7) |
| Consumo de batería | < 6 %/hora en dispositivo de referencia |

### 12.4 Matriz de dispositivos (mínimo)

| Nivel | Android | iOS |
|---|---|---|
| Bajo | Gama de entrada con 2–3 GB RAM, Android 9 | iPhone SE (2ª gen), iOS 16 |
| Medio | Gama media reciente, 4–6 GB, Android 13 | iPhone 12/13 |
| Alto | Flagship reciente | iPhone reciente + iPad |
| Otros | Tablet Android, pantalla con notch, pantalla ultra-ancha | — |

Resoluciones/aspect ratios a validar: 16:9, 18:9, 19.5:9, 20:9, 4:3 (tablet), ventana redimensionable en PC.

### 12.5 Proceso de QA

- El agente/rol de **QA reporta por severidad, no repara** (Bloqueante / Alta / Media / Baja).
- Cada milestone cierra con el trío **QA → changelog → commit atómico**.
- Bloqueantes y Altas se cierran antes de avanzar de milestone; Medias y Bajas van al backlog con dueño.
- Se corren los tests del módulo **antes de cada deploy**, sin excepción.

---

## 13. CI/CD, entornos y deploy

### 13.1 Entornos UGS

| Entorno | Uso | Quién deploya |
|---|---|---|
| `development` | Desarrollo diario, datos desechables | CI en cada merge a `develop` |
| `staging` | Pre-producción, datos realistas, IAP sandbox | CI con aprobación manual |
| `production` | Jugadores reales | CI con aprobación de 2 personas |

Cada entorno tiene su propio Remote Config, sus leaderboards y sus catálogos de Economy. **Nunca** se apunta un build de desarrollo a `production`.

### 13.2 Pipelines (GitHub Actions)

| Workflow | Disparo | Pasos |
|---|---|---|
| `core-tests` | Todo PR | `dotnet test` de Core (sin Unity) — **< 2 min** |
| `module-tests` | Todo PR que toque `/CloudCode~` | `dotnet test` del módulo |
| `architecture-tests` | Todo PR | Tests de §6.2 + verificación de contenido generado |
| `unity-tests` | PR a `develop`/`main` | EditMode + PlayMode en runner con licencia |
| `build-mobile` | Merge a `develop` | AAB + IPA firmados, subidos a canal interno |
| `deploy-backend` | Merge a `develop` (dev) / tag (staging, prod) | Deploy de módulo + Remote Config vía CLI, tras tests verdes |
| `nightly-integration` | Cron | Partida completa headless 2 clientes contra `development` |

**Regla dura**: `deploy-backend` no corre si `module-tests` no está verde. El deploy de backend y el release de cliente que dependen entre sí **se coordinan por `GameProtocol.Version`**: primero backend (compatible hacia atrás), después cliente.

### 13.3 Versionado y releases

- **`bundleVersion`** (Player Settings) es la única fuente de la versión visible. Sube en el **mismo commit** que el cambio que llega a jugadores: `patch` para fixes, `minor` para features.
- **`GameProtocol.Version`** sube solo con cambios incompatibles, en módulo + cliente a la vez.
- Ramas: `main` (producción) · `develop` (integración) · `feature/*` · `hotfix/*`.
- Commits: **Conventional Commits**. Tags `v{bundleVersion}`.
- **Nunca push a `main` ni PR sin confirmación explícita.**
- Dos changelogs: `CHANGELOG-dev.md` (Keep a Changelog, técnico) y `changelog-jugadores.md` (lenguaje de jugador, localizable).

### 13.4 Rollback

- **Backend**: Remote Config es el rollback rápido (kill switches). El módulo se re-deploya con la versión anterior desde el tag. Compatibilidad hacia atrás obligatoria durante al menos 2 versiones de protocolo.
- **Cliente**: staged rollout en Play (5 % → 20 % → 50 % → 100 %) con halt automático si `crash_free_sessions < 99 %`; en App Store, phased release.
- Un incidente que requiera cambio de cliente debe poder mitigarse **primero** con un kill switch.

---

## 14. Observabilidad: analytics, KPIs y LiveOps

### 14.1 Eventos (snake_case, registrados en el Event Manager **antes** de emitirse)

| Evento | Parámetros clave |
|---|---|
| `app_start` | cold_start_ms, is_first_session |
| `tutorial_step` | step_id, completed, elapsed_ms |
| `match_started` | mode, board_id, rule_set, opponent_type (human/ai/bot), ai_difficulty, is_ranked |
| `placement_completed` | mode, duration_ms, used_autoplace, layout_signature |
| `shot_fired` | mode, shot_index, outcome, think_time_ms *(muestreo 10 % en online)* |
| `ship_sunk` | ship_class, shot_index |
| `match_finished` | mode, result, end_reason, duration_s, shots_taken, accuracy, first_mover, elo_delta |
| `matchmaking_wait_time` | mode, wait_ms, matched, filled_with_bot |
| `reward_granted` | source, coin, xp, streak_multiplier |
| `reward_suppressed_pair` | reason |
| `store_viewed` / `store_item_purchased` | sku, currency, price |
| `iap_purchase_validated` | sku, store, revenue_usd, is_first_purchase |
| `ad_shown` / `ad_rewarded` | placement, ad_type |
| `cosmetic_equipped` | slot, item_id |
| `account_linked` | provider |
| `push_opt_in` / `push_received` / `push_opened` | reason |
| `error_shown` | err_code, endpoint |
| `protocol_mismatch` | client_version, server_version |
| `settings_changed` | key, value |

Wrapper de Analytics **siempre defensivo**: try/catch → warning. Nunca bloquea. Sin PII en ningún parámetro.

### 14.2 Dashboards mínimos

1. **Salud técnica**: crash-free, ANR, p95/p99 por endpoint, tasa de `ERR_*` por código, invocaciones/partida.
2. **Embudo de onboarding**: install → tutorial completado → primera partida → primera victoria → D1.
3. **Salud del online**: tiempo de matchmaking, % bot fill, % abandonos, duración media de partida.
4. **Balance**: winrate por dificultad de IA, `first_mover_win_rate`, precisión media por rango, distribución de Elo.
5. **Monetización**: ARPDAU, conversión, ARPPU, ingresos por SKU, eCPM y fill rate de anuncios.

### 14.3 KPIs objetivo para pasar de soft launch a global

| KPI | Umbral |
|---|---|
| Retención D1 | ≥ 35 % |
| Retención D7 | ≥ 15 % |
| Retención D30 | ≥ 6 % |
| Sesiones/día | ≥ 2.5 |
| Duración de sesión | ≥ 8 min |
| Partidas por sesión | ≥ 2.5 |
| Tutorial completado | ≥ 85 % |
| Sesiones sin crash | ≥ 99.5 % |
| Conversión a IAP (D30) | ≥ 1.5 % |
| ARPDAU | ≥ 0.03 USD |
| Abandono en partidas online | ≤ 8 % |

Si un KPI queda por debajo, no se lanza globalmente: se itera en soft launch.

### 14.4 LiveOps del primer trimestre

| Semana | Acción |
|---|---|
| 0 | Lanzamiento; vigilancia 24/7 los primeros 3 días; kill switches a mano |
| 1–2 | Ajuste fino de matchmaking y de caps de anuncios según datos reales |
| 3–4 | Primer set cosmético estacional (solo contenido, sin release de cliente) |
| 5–8 | Fin de la temporada 1 de Ranked + reset blando + recompensas |
| 9–12 | Evaluación de v1.1: async mejorado, Steam, barcos con habilidades (según datos) |

Todo lo anterior debe ser posible **sin release de cliente**, salvo el contenido que requiera arte nuevo empaquetado.

---

## 15. Legal, privacidad y tiendas

### 15.1 ⚠️ Marca: el nombre "Battleship" NO se puede usar

Este es un riesgo de lanzamiento, no un detalle:

- **Las mecánicas de un juego no son protegibles por copyright**, y el juego de combate naval por cuadrícula es de dominio público (se juega desde antes de la Primera Guerra Mundial y la primera edición comercial, *Salvo*, es de 1931).
- Pero **"BATTLESHIP" es una marca registrada de Hasbro**, igual que el *trade dress* de la versión de tablero plástico (la maleta gris/roja, las clavijas rojas y blancas, el arte asociado).
- Publicar en App Store o Google Play con ese nombre, con "Batalla Naval de Hasbro", con esa identidad visual, o con keywords de ASO que exploten la marca, es una vía rápida a **takedown** — y a perder toda la inversión de UA.

**Reglas duras del proyecto**:
1. El nombre comercial final **no** contiene "Battleship" ni traducciones registradas equivalentes. Nombre en clave interno: **ARMADA**.
2. Antes de fijar el nombre definitivo: **búsqueda de antecedentes marcarios** en USPTO/EUIPO/INDECOPI y en las tiendas, y consulta con un abogado de marcas. Es un gasto de tres cifras que evita uno de seis.
3. Identidad visual **original**: nada de clavijas rojas/blancas sobre maleta gris.
4. Nombres genéricos de clase naval (portaaviones, destructor, submarino) son descriptivos y de uso libre.
5. En la ficha de tienda se puede describir el género ("juego de estrategia naval por cuadrícula") sin nombrar la marca ajena.

### 15.2 Privacidad y datos

| Requisito | Implementación |
|---|---|
| Política de privacidad y ToS | URLs públicas, enlazadas en Ajustes y en la ficha de tienda |
| Consentimiento de analytics | Framework de consentimiento del motor (Unity 6.2+), con pantalla propia en el primer arranque |
| Borrado de cuenta | `DeleteAccountData` en ≤ 3 taps desde Ajustes: borra Cloud Save, Economy, entradas de leaderboard, token de push y desvincula identidades. Confirmación de dos pasos e irreversible |
| Exportación de datos | Solicitud desde Ajustes con entrega por el canal de soporte |
| GDPR / CCPA / LGPD | Base legal declarada, minimización de datos, sin PII en analytics |
| **COPPA / edad** | Age gate neutral al primer arranque. Bajo la edad de consentimiento: **sin anuncios personalizados, sin IAP, sin push de marketing** |
| ATT (iOS) | Prompt solo si se usa tracking para atribución; el juego funciona igual si se rechaza |
| Data Safety (Play) / Nutrition Labels (Apple) | Formularios completos y coherentes con lo que el cliente realmente envía — **auditado, no declarado de memoria** |
| Retención | `match:*` finalizadas se purgan a 30 días; `player.history` guarda 50 resúmenes sin PII del rival más allá del nombre público |

### 15.3 Requisitos de ficha de tienda

Clasificación por edad (IARC) esperada **E / 3+ / PEGI 3** (sin chat libre, sin violencia gráfica). Screenshots por dispositivo y por idioma, icono, video opcional, descripciones localizadas en los 10 idiomas, declaración de anuncios y compras.

---

## 16. Roadmap y milestones

Estimación para un equipo pequeño (1–2 devs + apoyo de arte y QA). Cada milestone cierra con **QA → changelog → commit atómico** y no se avanza con bloqueantes abiertos.

| M | Nombre | Duración | Contenido | Criterio de salida |
|---|---|---|---|---|
| **M0** | Fundación | 1 sem | Repo, `CLAUDE.md`, sub-agentes, este doc, contrato §7 congelado, CI de Core, `gh` autenticado, proyecto UGS y 3 entornos creados, **versión de Editor 6.3 LTS fijada** | CI verde en un repo con un test trivial; contrato revisado y firmado por todos los roles; `ProjectVersion.txt` e imagen de CI apuntando al mismo parche exacto |
| **M1** | Core puro | 2 sem | Reglas, bitboards, despliegue, disparos, hundimiento, fin de partida, Elo, recompensas, proyección, 4 IAs, property tests | ≥ 90 % cobertura en Core; los 8 invariantes de §12.2 verdes; IA cumple las precisiones de §4.6 |
| **M2** | Cliente local | 2 sem | Escena única, UI Toolkit, despliegue con drag&drop, partida vs IA y 2P local, Home, Ajustes, tutorial básico | Partida completa vs IA en dispositivo real, 60 FPS, sin red |
| **M3** | Fundaciones UGS | 2 sem | Módulo Cloud Code, Auth anónimo + **vinculación**, `GetServerInfo`/`GetGameConfig`/`GetPlayerBootstrap`, Cloud Save, Remote Config, Analytics, Diagnostics, service accounts, **spike de push** | Handshake de protocolo funcionando; config viniendo del servidor con fallback local; spike de FCM resuelto |
| **M4** | Online tiempo real | 3 sem | Matchmaker, handoff por MatchId, `SubmitPlacement`/`FireShot`/`GetMatchState`, Wire, reconexión, timers, Scheduler de barrido, partida privada por código | Dos dispositivos juegan una partida completa; test de concurrencia sin doble-reclamo; abandono resuelto por cron |
| **M5** | Meta y economía | 2 sem | Economy (COIN/GEM), perfil, historial, estadísticas, nivel/XP, misiones diarias, cosméticos + Content Designer, anti-farming | Recompensas otorgadas 100 % server-side; anti-farm verificado con cuentas de prueba |
| **M6** | Monetización | 2 sem | Tienda, IAP, `ValidatePurchase` con recibos reales sandbox, `remove_ads`, pase de temporada, anuncios con caps | Compra en sandbox de ambas tiendas acreditada e idempotente; caps de anuncios verificados |
| **M7** | Ranked y asíncrono | 3 sem | Elo, Leaderboards, temporadas, modo asíncrono, **push nativo**, emotes, revancha | Partida asíncrona completa con la app cerrada; leaderboard consistente; cierre de temporada simulado |
| **M8** | Pulido y contenido | 2 sem | Audio completo, 10 idiomas + font asset verificado, accesibilidad, VFX, tutorial final, arte de cosméticos | QA de glifos verde en los 10 idiomas; checklist de accesibilidad completo |
| **M9** | Hardening y soft launch | 3 sem | Test de carga, pase de anti-cheat, matriz de dispositivos, compliance de tiendas, staged rollout en 2 mercados | Todos los presupuestos de §12.3 cumplidos; §19 completo |
| **—** | Soft launch | 4 sem | Iteración con datos reales | KPIs de §14.3 en umbral |
| **—** | Lanzamiento global | — | — | — |

**Total hasta soft launch: ~20 semanas.** Hasta global: ~24–26 semanas.

**Camino crítico**: M1 → M3 (spike de push) → M4 (matchmaking + concurrencia) → M7 (asíncrono). El spike de push en M3 es el que puede obligar a replanificar el alcance: si no hay salida HTTP desde Cloud Code, el modo asíncrono se mueve a v1.1 y M7 se acorta.

---

## 17. Riesgos

| # | Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|---|
| 1 | **Conflicto de marca por el nombre** | Media | **Crítico** (takedown) | §15.1: nombre original + búsqueda de antecedentes antes de M8 |
| 2 | Fuga del tablero rival por un campo agregado sin querer | Baja | **Crítico** | Tests de proyección sobre JSON + `CODEOWNERS` en `MatchProjection` |
| 3 | Cloud Code sin salida HTTP ⇒ push imposible | Media | Alto | Spike en M3; plan B con función externa; async se mueve a v1.1 |
| 4 | Población insuficiente ⇒ matchmaking vacío | Alta | Alto | Bot fill en Casual (con kill switch), modo asíncrono, IA fuerte, soft launch concentrado geográficamente |
| 5 | Coste de backend mayor al presupuestado al escalar | Media | Medio | Presupuesto en unidades §8.7 + alerta a 150 invocaciones/partida + optimización de delta en Wire ya diseñada |
| 6 | Rechazo de tienda por privacidad/age gate | Media | Alto | §15.2 auditado en M9, no declarado de memoria |
| 7 | Versiones de paquetes inventadas, deprecadas o fuera de soporte | Media | Medio | Verificación contra el registro real en M0; **Editor fijado en 6.3 LTS** (§6.6), no en una Update release; revisar deprecaciones (matchmaker → multiplayer, `UIDocument` → `PanelRenderer` en 6.5+) |
| 8 | NRE por domain reload en producción | Media | Medio | Blindaje §9.3 desde plantilla + test de estáticos |
| 9 | Balance de IA percibido como tramposo | Media | Medio | IA determinista y auditable, precisiones medidas en §4.6, nunca ve el tablero del jugador |
| 10 | Farming/colusión erosiona la economía | Media | Medio | §5.3 + §11.4, con caps ajustables por Remote Config |
| 11 | Última milla manual bloquea releases | Media | Medio | `ProjectBootstrap.SetupAll()` en batchmode desde M0 |
| 12 | Alcance creciente (habilidades, clanes, torneos) | **Alta** | Alto | §3.2 es un contrato: nada entra a v1.0 sin sacar otra cosa |

---

## 18. Estructura de repo y directivas (`CLAUDE.md`)

### 18.1 Estructura

```
/CLAUDE.md                        ← directivas (§18.2)
/.claude/agents/*.md              ← sub-agentes (§18.3)
/Docs/
   ARMADA-Documento-Tecnico-Produccion.md   ← este doc (fuente de verdad)
   GDD.md · Arquitectura.md · Wireframes/ · Estetica.md
   RUNBOOK_Puesta_En_Marcha.md
   changelog/                     ← notas Obsidian + MOC
/CHANGELOG-dev.md                 ← Keep a Changelog (técnico)
/changelog-jugadores.md           ← patch notes (jugador)
/Assets/Scripts/Core              ← lógica pura, sin UnityEngine
/Assets/Scripts/Core.Tests        ← EditMode tests
/Assets/Scripts/Game              ← capa Unity + servicios UGS
/Assets/Scripts/Game/Editor       ← editor scripts idempotentes + Content Designer
/Assets/UI                        ← UXML + USS (UI Toolkit)
/Assets/Localization              ← StringTables generadas
/Content/ContentDatabase.json     ← fuente única de contenido
/CloudCode~/ArmadaModule          ← módulo Cloud Code (~ para que Unity lo ignore)
   Project/Functions · Logic · Data · Models · GameProtocol.cs
   Properties/PublishProfiles/FolderProfile.pubxml   ← OBLIGATORIO
   TestProject/
/Assets/RemoteConfig              ← config-as-code (GameConfig.rc)
/Assets/Matchmaker                ← queue/pool config-as-code
/.github/workflows/               ← CI (§13.2)
```

### 18.2 Directivas (`CLAUDE.md`)

**Idioma**
- Documentación (GDD, arquitectura, notas, changelog de jugadores): **español** (neutro latinoamericano).
- Código — clases, métodos, variables, comentarios, commits, ramas, logs, nombres de tests y de eventos: **inglés, sin excepción**. Nada de español en `.cs`/`.uxml`/`.uss`.
- Textos del juego: **siempre** por Localization, nunca hardcodeados.
- Regla práctica: *¿el string termina frente al jugador? → clave de Localization. ¿Termina en consola/log/Editor/archivo generado? → inglés. ¿Es un `ERR_*`/key/id? → no se toca nunca.*

**Convenciones C#**
- PascalCase para clases/métodos públicos; camelCase para locales/parámetros.
- Nombres de test: `Method_Scenario_ExpectedResult` (ej. `FireShot_OnAlreadyTargetedCell_ReturnsError`).
- Cloud Code: **un solo módulo**, funciones `[CloudCodeFunction]`, `context`/`gameApiClient`/`pushClient` inyectados vía `ICloudCodeSetup`.
- `<Nullable>enable</Nullable>` en Core y en el módulo, desde el primer commit.
- Eventos de Analytics en snake_case inglés, registrados en el Event Manager **antes** de emitirse.

**Seguridad / server-authoritative**
- Toda regla de juego (validación de despliegue y disparos, detección de victoria, IA online, **cálculo de moneda en modos online**) se resuelve en Cloud Code. El cliente nunca decide.
- **El tablero rival nunca sale del servidor** hasta el fin de la partida.
- Toda compra se valida server-side antes de otorgar nada.
- El cliente no lee Remote Config directo (pasa por `GetGameConfig`).
- Handshake de versión (`GameProtocol.Version`) en el boot.

**UX / UI**
- UI simple y rápida por encima de la estética; ≤ 2 taps para empezar a jugar.
- Una sola escena persistente con paneles (salvo Splash).
- UI Toolkit con `UIDocument` (no `PanelRenderer`: es 6.5+); rewireo idempotente; colores desde variables USS en `:root`.

**Contenido y assets**
- El contenido se edita **solo** con el Content Designer (fuente única `ContentDatabase.json`).
- Los assets serializados se generan con editor scripts idempotentes; nunca se edita YAML a mano.

**Proceso**
- Config-as-code para todo lo deployable; versionado en git.
- Tests del módulo antes de cada deploy, sin excepción.
- Cada milestone cierra con QA → changelog → commit atómico.
- Deuda documentada en el momento de aceptarla, con condición de cierre.
- **Nunca push a `main` ni PR sin confirmación explícita.**

### 18.3 Sub-agentes (`.claude/agents/*.md`)

Flujo: `game-designer` → `gameplay-programmer` / `systems-programmer` → `qa-tester` → `docs-changelog` → `git-ops`.

| Agente | Model | Rol | Límite clave |
|---|---|---|---|
| `game-designer` | opus | Mecánicas, progresión, balance; mantiene este doc | Especifica con cifras concretas; no implementa |
| `gameplay-programmer` | sonnet | Reglas de `Core`, IA, UI de partida | No decide balance; pregunta en vez de inventar valores |
| `systems-programmer` | sonnet | Arquitectura, Cloud Code, UGS, rendimiento, build | No decide diseño de juego; prefiere lo simple al refactor grande |
| `backend-security` | opus | Revisión de proyección, anti-cheat, permisos, privacidad | **Poder de veto** sobre cualquier cambio en `MatchProjection` o en DTOs de vista |
| `ui-programmer` | sonnet | UI Toolkit, USS, accesibilidad, temas | No inventa copy; usa claves de Localization |
| `narrative-writer` | sonnet | Copy in-game, tutorial, notas de parche de jugador | No introduce canon nuevo sin registrarlo |
| `qa-tester` | haiku | Ejecuta tests, busca bugs, reporta por severidad | **Reporta, no repara** |
| `docs-changelog` | haiku | `CHANGELOG-dev` + changelog de jugadores + notas Obsidian | Nunca inventa cambios no confirmados |
| `git-ops` | haiku | Commits (Conventional Commits), ramas, tags, PRs | **Nunca push a `main` ni PR sin confirmación explícita** |

**Orquestación**: definir el contrato de API (§7) por adelantado y pasarlo idéntico a los agentes que trabajen en paralelo; este documento es la única fuente de verdad numérica; cerrar cada milestone con el trío QA → changelog → commit.

---

## 19. Checklist de lanzamiento

**Técnico**
- [ ] Todos los presupuestos de §12.3 cumplidos en la matriz de dispositivos de §12.4
- [ ] Cobertura de Core ≥ 90 %; 100 % en reglas y proyección
- [ ] Los 5 tests de proyección verdes, incluido el de JSON serializado
- [ ] Test de carga: 500 partidas concurrentes dentro de presupuesto
- [ ] Todos los kill switches probados en `staging` (apagar y encender cada uno)
- [ ] Rollback de módulo ensayado al menos una vez
- [ ] Crash-free ≥ 99.5 % y ANR < 0.35 % durante 2 semanas de soft launch
- [ ] Sin `TODO`/`FIXME` bloqueantes en `Core` ni en el módulo
- [ ] Deuda técnica abierta documentada con dueño y condición de cierre

**Backend**
- [ ] Entornos `development` / `staging` / `production` separados y verificados
- [ ] Service accounts con roles mínimos necesarios (§8.8)
- [ ] Verificación de ticket de Matchmaker en modo **hard**
- [ ] Scheduler corriendo y verificado (barrido, dailies, temporadas)
- [ ] Todos los eventos de Analytics registrados en el Event Manager
- [ ] Alertas configuradas: error rate, latencia p95, invocaciones/partida, crash-free

**Producto**
- [ ] Tutorial con ≥ 85 % de finalización en soft launch
- [ ] KPIs de §14.3 en umbral
- [ ] 10 idiomas revisados por hablante nativo (no solo traducción automática)
- [ ] Font asset verificado con el set completo de glifos por idioma
- [ ] Checklist de accesibilidad completo (§9.6)
- [ ] Caps de anuncios verificados en dispositivo real

**Monetización**
- [ ] Compras sandbox acreditadas e idempotentes en ambas tiendas
- [ ] `remove_ads` respetado en todos los puntos de inserción
- [ ] Restauración de compras funcionando (iOS obligatorio)
- [ ] Precios revisados por región

**Legal**
- [ ] **Búsqueda de antecedentes marcarios del nombre final completada** (§15.1)
- [ ] Identidad visual original, sin trade dress ajeno
- [ ] Política de privacidad y ToS publicadas y enlazadas
- [ ] Data Safety / Nutrition Labels auditados contra el tráfico real
- [ ] Borrado de cuenta funcionando end-to-end
- [ ] Age gate y modo bajo-edad verificados
- [ ] Clasificación IARC obtenida

**Operación**
- [ ] Runbook de incidentes escrito y ensayado
- [ ] Guardia definida para los primeros 3 días
- [ ] Canal de soporte y FAQ publicados
- [ ] Ficha de tienda completa en los 10 idiomas
- [ ] Staged rollout configurado con criterio de halt automático

---

## 20. Apéndices

### 20.1 Tabla maestra de cifras (referencia rápida)

| Concepto | Valor |
|---|---|
| Editor / UI | Unity 6.3 LTS (`6000.3.x`) · UI Toolkit con `UIDocument` |
| Tableros | 8×8 (12 casillas de flota) · 10×10 (17) · 12×12 (26) |
| Flota `classic` | 5, 4, 3, 3, 2 |
| Timer de despliegue / turno (tiempo real) | 90 s / 30 s |
| Timer de turno (asíncrono) | 24 h |
| Timeouts consecutivos antes de abandono | 3 (tiempo real) / 2 (asíncrono) |
| Abandono | 3 min (tiempo real) / 48 h (asíncrono) |
| Elo inicial / K / suelo | 1000 / 40-32-24 / 100 |
| Temporada / reset blando | 60 días / `1000 + (r−1000) × 0.5` |
| Recompensa victoria / derrota online | 100 / 40 COIN |
| Primera victoria del día | +150 COIN |
| Tope diario de COIN por IA | 150 |
| Tope diario de COIN total | 2 500 |
| Racha máxima | +50 % |
| XP por nivel | `500 + 250 × (n−1)`, hasta 60 |
| Intersticiales | 1 cada 4 partidas, ≥ 240 s, máx. 6/día |
| Rewarded duplicar recompensa | máx. 3/día |
| Ventana de matchmaking Casual / Ranked | ±250 / ±100, +25/s, tope ±1000 / ±600 |
| Bot fill Casual | tras 45 s (nunca en Ranked) |
| Invocaciones Cloud Code por partida | ≤ 150 (objetivo ~120) |
| `FireShot` p95 / p99 | < 400 ms / < 900 ms |

### 20.2 Glosario

| Término | Definición |
|---|---|
| **Proyección** | Construcción del `MatchStateView` específico de un jugador, con la información del rival eliminada |
| **Bitboard** | Representación de la cuadrícula como bits en 1–3 `ulong` |
| **Tick (Wire)** | Mensaje push que solo dice "algo cambió"; nunca es fuente de verdad |
| **Sequence** | Contador monótono de mutaciones del estado; base de la idempotencia |
| **Seam** | Punto de inyección que permite testear lógica pura sin mocks (ej. `IRandom`, catálogo primable) |
| **Kill switch** | Flag de Remote Config que apaga una feature sin release de cliente |
| **Bot fill** | Rival controlado por IA cuando el matchmaking humano no encuentra pareja |
| **Salvo** | Variante en la que se disparan tantas casillas como barcos propios queden a flote |

### 20.3 Primeros pasos concretos (M0)

1. Crear repo, `CLAUDE.md` (§18.2) y los 9 sub-agentes (§18.3). `gh auth login` y remoto configurado.
2. Crear el proyecto UGS y los **tres entornos**; anotar `cloudProjectId` y environment ids.
3. **Fijar el Editor en un parche exacto de Unity 6.3 LTS** (§6.6) y **verificar cada versión de paquete** de §6.5 contra el registro real antes de fijar `manifest.json` / `.csproj`.
4. Congelar el contrato de §7 (firmas exactas) y repartirlo a todos los roles.
5. Levantar el esqueleto: `Armada.Core` (`noEngineReferences: true`, nullable enable) + `Armada.Core.Tests` con un test trivial verde en CI.
6. Crear `Properties/PublishProfiles/FolderProfile.pubxml` en el módulo **antes** del primer intento de deploy.
7. Escribir `ProjectBootstrap.SetupAll()` vacío pero invocable en batchmode, y engancharlo a CI.
8. Abrir `CHANGELOG-dev.md` y `changelog-jugadores.md`.

---

## Conexiones

- [[TTTXO-Handoff-Tecnico]] — origen de: separación Core/Game, patrones defensivos, gotchas de UGS, sub-agentes
- [[TCGMaster-Contexto-Tecnico]] — origen de: servidor autoritativo total, errores `ERR_*`, idempotencia por sequence, generador de contenido, seam de reglas puras
- [[ARMADA-GDD]] — diseño de juego extendido (a crear en M0)
- [[ARMADA-Arquitectura]] — detalle de implementación por subsistema (a crear en M1)
- [[ARMADA-Runbook]] — puesta en marcha y deploy paso a paso (a crear en M3)

## Fuente

- `TTTXO-Technical-Handoff.md` (2026-07-23) — Unity 6000.3.20f1 (línea 6.3 LTS, la que adopta este proyecto), stack UGS, deuda técnica, directivas y recomendaciones.
- `TCGMaster_Contexto_Tecnico.md` (2026-07-23) — Unity 6000.5.3f1 (Update release, con `PanelRenderer`), arquitectura server-authoritative, patrones reutilizables, deuda técnica.
- Reglas y variantes del juego de combate naval por cuadrícula: artículo "Battleship (game)", Wikipedia (consultado 2026-07-23) — composición de flota de la edición de 1990, variante Salvo de 1931 y variantes documentadas.
