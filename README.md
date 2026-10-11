# Desmoche

Juego de cartas 2D en **Godot 4 .NET/C#**, con partidas de **2 a 4 jugadores**. Permite jugar por LAN/Wi-Fi o completamente en local contra bots. Ambas modalidades comparten las reglas y la Mesa.

El proyecto se construye por fases. El contrato para continuar el trabajo está en [CONTEXTO_PROYECTO.md](CONTEXTO_PROYECTO.md); [AGENTS.md](AGENTS.md) resume las instrucciones para asistentes de desarrollo.

## Estado actual

- Modelo de dominio, validación determinista y máquina de estados de partida.
- Multijugador ENet con host autoritativo, lobby por IP, límite de cuatro jugadores y gestión de desconexiones.
- Réplica pública mediante `MultiplayerSpawner`/`MultiplayerSynchronizer` y manos privadas por destinatario.
- Pantalla de carga, menú de inicio y formularios de crear/unirse a sala.
- Mesa compartida, cartas visuales, selección múltiple, orden por arrastre, preparación de combinaciones y pago con confirmación.
- Singleplayer con **1–3 bots**, dificultades **Fácil, Medio, Difícil y Experto**, nueva partida y regreso al menú.

Las fases 0–9 y la ampliación Singleplayer están implementadas. Los diseños visuales siguen siendo provisionales. Android/iOS forman parte del objetivo posterior; esta revisión no certifica esas exportaciones.

## Estructura

```text
.
├── README.md
├── CONTEXTO_PROYECTO.md
├── AGENTS.md
├── desmoche/                     # Proyecto Godot: abrir project.godot
│   ├── Assets/
│   ├── Docs/                    # Contratos, árboles, aceptación y PlantUML
│   ├── Scenes/
│   └── Scripts/
├── Desmoche.Pruebas/             # Dominio, reglas, partida, red y réplica
├── Desmoche.Singleplayer.Pruebas/ # Motor local, bots y simulaciones
└── Desmoche.Interaccion.Pruebas/  # Preparación visual sin Godot UI
```

Las carpetas de pruebas deben permanecer junto a `desmoche`: sus proyectos enlazan los archivos del juego mediante rutas relativas. No se copian dentro de `res://`.

## Preparar y ejecutar

Base técnica de esta revisión: **Godot 4.7.2 .NET**, `Godot.NET.Sdk/4.7.2` y SDK .NET **9.0.308** utilizado en la verificación. El proyecto de escritorio y las consolas apuntan a `net8.0`; el `.csproj` contiene una condición `net9.0` para Android. Necesitas un SDK compatible y, para ejecutar las consolas, el runtime de .NET 8. La edición estándar de Godot no ejecuta los scripts C#.

1. Clona o descarga el repositorio conservando su estructura.
2. En Godot **.NET**, importa `desmoche/project.godot` y espera la importación de recursos.
3. Compila y ejecuta con **F5**. La escena principal es `res://Scenes/Main.tscn`.

Desde PowerShell en la raíz del repositorio también puedes compilar:

```powershell
dotnet build .\desmoche\Desmoche.csproj
```

Singleplayer funciona sin conexión: elige nombre, dificultad y cantidad de bots. Para LAN, el host crea una sala; los demás introducen su **IP local** desde Buscar sala. Todos deben usar la misma revisión del proyecto y estar en la misma red. El puerto predeterminado es **UDP 7000**. Buscar sala utiliza conexión directa por IP; no descubre hosts automáticamente.

## Pruebas

Desde la raíz del repositorio:

```powershell
dotnet run --project .\Desmoche.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Interaccion.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Singleplayer.Pruebas\Pruebas.csproj -- --quick
```

Para el conjunto completo de Singleplayer, omite `-- --quick`. Las pruebas puras no necesitan abrir Godot. Para comprobar la integración, abre en Godot una escena de diagnóstico y pulsa **F6**:

- `res://Scenes/Diagnostics/SingleplayerSmoke.tscn`: espera `SINGLEPLAYER PASS`.
- `res://Scenes/Diagnostics/MenuNavigationSmoke.tscn`: espera `MENU NAVIGATION PASS`.
- Los diagnósticos de red y Mesa tienen instrucciones específicas en los documentos enlazados abajo.

En la verificación de la entrega Singleplayer del **11 de octubre de 2026** pasaron la compilación sin errores ni advertencias, **63 738 comprobaciones de bots con 168 partidas completas**, los diagnósticos de interfaz y la regresión LAN entre dos procesos. Es evidencia de esa revisión; vuelve a ejecutar las pruebas después de modificar código. La conexión entre procesos de una PC no sustituye una prueba entre dispositivos físicos.

Verificación de esta subida (11 de octubre de 2026): compilación **0 errores / 0 advertencias**, consola general aprobada, interacción **435 comprobaciones** y Singleplayer rápido **12 633 comprobaciones / 36 partidas completas**. Se actualizó una expectativa antigua de la prueba de interacción para incluir las declaraciones iniciales; el código del juego se conserva como en la instalación. No se repitieron pruebas visuales ni LAN entre dispositivos durante esta subida.

## Documentación técnica

| Tema | Documento |
| --- | --- |
| Contexto y forma de trabajar | [CONTEXTO_PROYECTO.md](CONTEXTO_PROYECTO.md) |
| Máquina de estados y reglas de flujo | [Fase4.md](desmoche/Docs/Fase4.md) |
| Autoridad, comandos y conexión | [Fase5.md](desmoche/Docs/Fase5.md) |
| Réplica pública, manos privadas y coherencia | [Fase6.md](desmoche/Docs/Fase6.md) |
| Lobby LAN | [Fase7.md](desmoche/Docs/Fase7.md) |
| Mesa y cartas | [Fase8.md](desmoche/Docs/Fase8.md), [Fase9.md](desmoche/Docs/Fase9.md) |
| Revisión visual y controles por arrastre | [MesaCasino.md](desmoche/Docs/MesaCasino.md), [MesaDinamica.md](desmoche/Docs/MesaDinamica.md) |
| Carga e inicio | [InicioLobby.md](desmoche/Docs/InicioLobby.md) |
| Partida local y bots | [Singleplayer.md](desmoche/Docs/Singleplayer.md), [Singleplayer.puml](desmoche/Docs/Singleplayer.puml) |

Los documentos de fases anteriores registran el estado de sus respectivas entregas. Por ejemplo, la referencia histórica a Singleplayer deshabilitado en `InicioLobby.md` queda reemplazada por `Singleplayer.md`. Algunas instrucciones antiguas contienen rutas del equipo original; utiliza las rutas relativas de este README para la instalación actual.
