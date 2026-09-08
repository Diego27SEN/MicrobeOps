# ArmadaModule — módulo de Cloud Code

Módulo **único** de Cloud Code del proyecto. Dueño de toda la lógica autoritativa: reglas de partida, proyección de estado, economía, matchmaking, Elo y validación de compras.

> El sufijo `~` en `CloudCode~/` hace que Unity ignore esta carpeta. Es un proyecto .NET aparte, no parte del proyecto de Unity.

## Estado: esqueleto (M0)

Lo que existe hoy:

- `Properties/PublishProfiles/FolderProfile.pubxml` — **obligatorio**, creado antes del primer intento de deploy (§6.5 del documento técnico). Sin este archivo el Deployment window falla con *"Could not find a Publish Profile"*.

Lo que **falta**, y se hace en **M3**:

- `ArmadaModule.csproj`
- `ModuleSetup.cs` con el patrón `ICloudCodeSetup`
- `Project/Functions`, `Project/Logic`, `Project/Data`, `Project/Models`
- `TestProject/` con los ~60 tests del módulo

## Por qué el `.csproj` no está todavía

El documento técnico marca las versiones de `Com.Unity.Services.CloudCode.Core` (0.0.4) y `Com.Unity.Services.CloudCode.Apis` (0.0.26) como *"verificar"*. La regla del proyecto es que **nunca se inventan ni se asumen versiones de paquetes**: se verifican contra NuGet antes de fijarlas. Se crea el `.csproj` en M3, cuando esa verificación esté hecha.

Queda registrado como deuda **M0-4** en `CHANGELOG-dev.md`.

## Cuando se cree el `.csproj`

Requisitos que ya están decididos y no se discuten de nuevo:

| Item | Valor |
|---|---|
| TargetFramework | `net9.0` |
| Nullable | `enable` |
| Publish | `PublishReadyToRunComposite=true`, runtime `linux-x64` |
| `Microsoft.Extensions.*` | **9.0.x**, alineado con el runtime |
| Registro del API client | `config.AddGameApiClient()` — nunca `GameApiClient.Create()` (obsoleto) |
| Patrón | `ICloudCodeSetup` / `ModuleSetup` + funciones `[CloudCodeFunction]` |

Y, sobre todo, el `.csproj` debe compilar las fuentes de Core, no copiarlas:

```xml
<ItemGroup>
  <Compile Include="../../Assets/Scripts/Core/**/*.cs" />
</ItemGroup>
```

Eso es lo que hace que las reglas y la IA sean **byte-for-byte idénticas** en cliente y servidor. Es la decisión de mayor retorno heredada de los proyectos previos y no se toca.
