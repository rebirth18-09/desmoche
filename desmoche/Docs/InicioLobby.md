# Carga, menú principal y lobby simplificado

Actualización posterior: Singleplayer ya está habilitado. Su configuración, sesión local y bots están documentados en [Singleplayer.md](Singleplayer.md). Las referencias siguientes a «Próximamente» describen el estado de la entrega original del menú.

Esta revisión amplía la presentación construida en las fases 7–9. Mantiene el núcleo, las reglas aprobadas, los RPC y la autoridad del servidor. Singleplayer y descubrimiento automático de salas quedan pendientes para sus fases correspondientes.

## 1. Objetivo de la fase

Iniciar la aplicación con una pantalla de carga, mostrar un menú DESMOCHE con «Jugar» y las opciones Multijugador, Singleplayer y Salir, y reducir el lobby a dos entradas claras: crear una partida o entrar en una sala por IP. Mantener el recorrido hacia la Mesa existente y los mensajes de conexión/desconexión.

## 2. Decisiones arquitectónicas

- `Main` conserva su nodo raíz y una única instancia de `NetworkManager`. La ruta `/root/Main/NetworkManager` no cambia: sigue siendo el punto estable de networking y RPC.
- Carga, menú, lobby y mesa son pantallas de presentación. Solo una pantalla participa en la navegación visible a la vez.
- La pantalla de carga prepara los recursos necesarios antes de mostrar el menú. Un porcentaje de carga no debe representar una conexión de red ni una preparación de partida.
- `Main` solicita las escenas y recursos con `ResourceLoader` en segundo plano y crea los nodos en el hilo principal, una pantalla por fotograma. La permanencia mínima de 0,8 segundos permite ver la carga; no reemplaza la comprobación de que los recursos terminaron.
- Singleplayer se muestra deshabilitado; no se crea un motor paralelo ni una simulación contra bots.
- Buscar sala despliega nombre e IP. No implica descubrimiento de hosts, difusión por UDP ni permisos multicast.
- El lobby reutiliza `Host`, `Join`, `Close` y `Submit(StartMatch)` de `NetworkManager`; no modifica el estado autoritativo directamente.
- Los formularios y la navegación utilizan `Control` y contenedores de Godot. El tema verde oscuro y oro se comparte entre estas pantallas sin modificar los materiales de la Mesa.

## 3. Dependencias

Requiere el proyecto existente de Godot 4.7.2 .NET, `NetworkManager`, `NetworkProtocol`, `ConnectionState`, `SnapshotDto`, el núcleo de `GameManager`, las escenas Lobby/Mesa anteriores y la revisión Mesa dinámica. Conserva las referencias C# de `Desmoche.csproj`.

El nuevo menú no recibe cartas privadas ni datos del mazo. El lobby recibe estado de conexión, jugadores públicos y la fase necesaria para iniciar/transicionar. Mesa sigue recibiendo únicamente la vista autorizada para cada destinatario.

## 4. Implementación

Los archivos completos se entregan en el paquete; los cambios no requieren copiar fragmentos de esta guía.

| Ruta `res://` | Clase / tipo | Responsabilidad |
| --- | --- | --- |
| `Scripts/Presentation/Main.cs` | `Desmoche.Presentation.Main : Control` | Mantener el ciclo de pantallas y la instancia persistente de red. |
| `Scripts/Presentation/LoadingScreen.cs` | `Desmoche.Presentation.LoadingScreen : Control` | Presentar avance y errores de preparación de recursos. |
| `Scripts/Presentation/StartupMenu.cs` | `Desmoche.Presentation.StartupMenu : Control` | Presentar DESMOCHE/Jugar y comunicar las elecciones del menú. |
| `Scripts/Presentation/Lobby.cs` | `Desmoche.Presentation.Lobby : Control` | Desplegar formularios Crear/Buscar y representar el estado de sala. |
| `Scenes/Main.tscn` | Escena raíz | Contener red, fondo y pantallas. |
| `Scenes/Carga.tscn` | Escena `Control` | Pantalla de carga. |
| `Scenes/MenuPrincipal.tscn` | Escena `Control` | Menú principal. |
| `Scenes/Lobby.tscn` | Escena `Control` | Selector de opción, formularios y sala. |
| `Assets/UI/MenuTheme.tres` | Recurso `Theme` | Estilos comunes del menú, la carga y los formularios. |
| `Assets/UI/MenuBackdrop.svg` | Recurso `Texture2D` | Fondo verde y oro de la navegación inicial. |
| `Docs/InicioLobby.puml` | PlantUML | Responsabilidades y recorrido de navegación. |

Los nombres exactos de los nodos y las conexiones se detallan en la sección 5. `NetworkManager`, dominio y `GameManager` conservan sus contratos; la actualización no necesita nuevos RPC.

## 5. Configuración Godot

La escena principal continúa siendo `res://Scenes/Main.tscn`. Abre `project.godot`, espera la importación y compila los scripts C# antes de F5. La escena del paquete incluye los nodos, referencias a scripts y configuración de layout; no conectes manualmente señales adicionales.

El árbol de `Main` se conserva durante toda la ejecución. Los nodos MenuPrincipal, Lobby y Mesa se instancian después de la carga; no deben añadirse otra vez manualmente en el Inspector:

```text
Main [Control; Main.cs]
├── NetworkManager [Node; NetworkManager.cs]
├── Background [TextureRect; MenuBackdrop.svg]
└── Margin [MarginContainer]
    └── Screens [Control]
        ├── Carga [Control; instancia de Carga.tscn]
        ├── MenuPrincipal [Control; instancia creada al cargar]
        ├── Lobby [Control; instancia creada al cargar]
        └── Mesa [Control; instancia creada al cargar]
```

`Main.MinimumLoadingSeconds` es un valor exportado entre 0 y 3 segundos, con 0,8 por defecto. Es un mínimo visual; la salida hacia el menú espera también a que termine la preparación real. La ventana establece un tamaño mínimo de 640×480. El networking continúa en su ruta original.

Las señales se conectan en C#, sin conexiones adicionales del Inspector:

| Emisor | Señal / evento | Receptor y efecto |
| --- | --- | --- |
| `LoadingScreen` | `RetryRequested` | `Main`: vuelve a solicitar los recursos pendientes después de un error. |
| `LoadingScreen` | `QuitRequested` | `Main`: cierra el juego. |
| `StartupMenu` | `MultiplayerRequested` | `Main.OpenMultiplayer()`: muestra las opciones del lobby. |
| `StartupMenu` | `QuitRequested` | `Main`: cierra el juego. |
| `Lobby` | `BackRequested` (evento C#) | `Main.ReturnToStart()`: vuelve al menú solo estando desconectado. |
| `NetworkManager` | `ConnectionChanged`, `ViewChanged` | `Main`: decide Lobby/Mesa según conexión y fase autorizada. |
| `NetworkManager` | `ConnectionChanged`, `ViewChanged`, `ActionCompleted` | `Lobby`: representa conexión, jugadores, resultado de inicio y errores. |
| `Main` | `ScreensInitialized` | Evento C# local utilizado por diagnóstico; no es una RPC. |

El árbol del lobby es:

```text
Lobby [Control; Lobby.cs; MenuTheme.tres]
└── Scroll [ScrollContainer]
    └── Center [CenterContainer]
        └── Panel [PanelContainer]
            └── Padding [MarginContainer]
                └── Content [VBoxContainer]
                    ├── TitleLabel [Label]
                    ├── SubtitleLabel [Label]
                    ├── OptionsPanel [VBoxContainer]
                    │   ├── CreateOption [Button]
                    │   ├── JoinOption [Button]
                    │   └── BackButton [Button]
                    ├── CreatePanel [VBoxContainer]
                    │   ├── TitleLabel [Label]
                    │   ├── HintLabel [Label]
                    │   ├── NameLabel [Label]
                    │   ├── NameInput [LineEdit]
                    │   ├── RoomInfoLabel [Label]
                    │   └── Buttons [VBoxContainer]
                    │       ├── CreateButton [Button]
                    │       └── CancelButton [Button]
                    ├── JoinPanel [VBoxContainer]
                    │   ├── TitleLabel [Label]
                    │   ├── HintLabel [Label]
                    │   ├── NameLabel [Label]
                    │   ├── NameInput [LineEdit]
                    │   ├── IPLabel [Label]
                    │   ├── IPInput [LineEdit]
                    │   └── Buttons [VBoxContainer]
                    │       ├── JoinButton [Button]
                    │       └── CancelButton [Button]
                    ├── SessionPanel [VBoxContainer]
                    │   ├── StatusLabel [Label]
                    │   ├── AddressLabel [Label]
                    │   ├── PlayersTitle [Label]
                    │   ├── PlayersList [ItemList]
                    │   ├── WaitingLabel [Label]
                    │   └── RoomButtons [VBoxContainer]
                    │       ├── StartButton [Button]
                    │       └── LeaveButton [Button]
                    ├── MessageLabel [Label]
                    └── HintLabel [Label]
```

CreatePanel, JoinPanel y SessionPanel arrancan ocultos; `Lobby.Render()` muestra solamente el panel correspondiente al estado actual. Los dos NameInput usan `MaxLength = 24` y conservan el mismo nombre local al cambiar de formulario. IPInput limita la longitud a 64. `Scroll` deshabilita scroll horizontal y sigue el foco. `Panel` tiene ancho mínimo de 540 píxeles y Padding aplica márgenes de 28/24 píxeles. PlayersList no admite selección; representa jugadores públicos ordenados por asiento.

Los botones de opción despliegan su formulario. CancelButton vuelve a las opciones. Enter en el nombre de Crear confirma ese formulario; Enter en IP confirma Buscar. `Esc` vuelve del formulario a las opciones y de las opciones al menú; mientras conecta cancela la conexión. No cierra automáticamente una sala activa y no intercepta el input de Mesa.

Tras un error asíncrono de conexión se restaura Buscar con el mensaje. Cancelar deliberadamente, salir o cerrar una sala vuelve a las opciones. El botón de inicio solo se muestra al host y se habilita para 2–4 conectados, fase Preparing y sin solicitud pendiente. El cliente ve la indicación de espera.

La carga y el menú contienen:

```text
Carga [Control; LoadingScreen.cs; MenuTheme.tres]
└── Center [CenterContainer]
    └── Content [VBoxContainer]
        ├── TitleLabel [Label; DESMOCHE]
        ├── SuitsLabel [Label]
        ├── ProgressBar [ProgressBar]
        ├── StatusLabel [Label]
        └── ErrorButtons [HBoxContainer; oculto inicialmente]
            ├── RetryButton [Button]
            └── QuitButton [Button]

MenuPrincipal [Control; StartupMenu.cs; MenuTheme.tres]
└── Scroll [ScrollContainer]
    └── Center [CenterContainer]
        └── Content [VBoxContainer]
            ├── SuitsLabel [Label]
            ├── TitleLabel [Label; DESMOCHE]
            ├── PlayLabel [Label; Jugar]
            ├── Buttons [VBoxContainer]
            │   ├── MultiplayerButton [Button]
            │   ├── SingleplayerButton [Button; Disabled = true]
            │   ├── ComingSoonLabel [Label]
            │   └── QuitButton [Button]
            └── FooterLabel [Label]
```

Content tiene ancho mínimo de 420 píxeles en carga/menú. La barra no muestra un porcentaje numérico; su valor representa los recursos y nodos realmente preparados. Los botones de error solo aparecen ante fallo y llevan el foco a Reintentar. MenuPrincipal centra sus textos; TitleLabel utiliza una fuente serif de sistema con alternativas. El menú lleva inicialmente el foco a Multijugador.

Background llena la ventana con `ExpandMode = IgnoreSize`, `StretchMode = KeepAspectCovered` y `MouseFilter = Ignore`. Los cuatro márgenes de Main son 16 píxeles. MenuTheme se referencia desde las tres escenas nuevas/revisadas; el Theme interno anterior de Main permanece para mantener la representación de Mesa.

Las pantallas usan anclajes completos y contenedores para mantener márgenes. El fondo no debe capturar input. Los botones y LineEdit conservan foco de teclado. Singleplayer permanece deshabilitado en el Inspector y en el comportamiento del menú.

La autoridad multijugador continúa en el servidor, con peer ID 1. Los nuevos controles son presentación local y no requieren `MultiplayerSynchronizer`, `MultiplayerSpawner` ni autoridad transferida al cliente.

## 6. Integración

1. La aplicación entra en carga y prepara las pantallas.
2. Cuando los recursos están disponibles muestra el menú DESMOCHE.
3. Multijugador muestra el lobby desconectado.
4. Crear/Buscar despliega el formulario correspondiente. Crear usa el nombre; Buscar usa nombre e IP.
5. `NetworkManager` comunica conexión, handshake, errores y snapshots; el lobby representa esos estados.
6. Solo el host envía la solicitud de inicio, con 2–4 jugadores conectados. El servidor la valida.
7. Un snapshot de partida iniciada muestra Mesa. Las cartas e interacciones mantienen su implementación anterior.
8. Al salir o perder conexión se vuelve al lobby con el mensaje de red. El usuario desconectado puede volver al menú.

Se conserva la validación del nombre de 1–24 caracteres y la IP literal. No se aceptan direcciones comodín, multicast ni broadcast como destino. El puerto utilizado sigue siendo UDP 7000; no se introduce una selección de puerto de UI en esta revisión.

La preparación inicial solicita `MenuPrincipal.tscn`, `Lobby.tscn`, `Mesa.tscn`, las texturas Casino `Courts.png`/`Portraits.png` y `PortraitCircle.gdshader`; las escenas cargan sus propias dependencias. La barra dedica el 75 % a recursos y el 25 % a instanciar las tres pantallas. Ante un recurso fallido, inválido o un tiempo de espera superior a 60 segundos, la carga ofrece Reintentar/Salir y registra el detalle en la consola. Reintentar conserva los recursos ya disponibles, reutiliza solicitudes en progreso y consume las fallidas antes de volver a solicitarlas. Si falla la instanciación se limpia el nodo parcial. La ruta síncrona se reserva para diagnósticos de escenas anteriores; F5 utiliza la carga normal.

## 7. Prueba de aceptación

Antes de probar, aplica el paquete sobre una copia del proyecto existente y compila:

```powershell
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
```

### Carga y menú

1. Ejecuta F5 desde `Main`.
2. Comprueba que se muestra carga antes del menú, y que termina sin archivos faltantes ni errores de consola.
3. Comprueba «DESMOCHE», «Jugar», Multijugador, Singleplayer y Salir. Singleplayer debe verse deshabilitado y no abrir otra pantalla.
4. Entra en Multijugador y vuelve al menú estando desconectado. Repite: debe mantenerse un solo `NetworkManager`.
5. Cierra la aplicación mediante Salir.

### Formularios y errores

1. Abre Crear partida. Debe mostrar el campo nombre y confirmación; el formulario de IP debe permanecer oculto.
2. Intenta crear con nombre vacío: debe mostrarse el error sin abrir un servidor.
3. Cambia a Buscar sala: debe mostrar nombre e IP. Un destino inválido debe rechazarse sin comenzar una conexión.
4. Usa una IP válida de un equipo sin sala abierta: debe mostrarse conexión pendiente, ofrecer cancelación y mostrar el resultado de error/desconexión cuando corresponda.
5. Comprueba teclado, Enter en IP y visibilidad de campos/botones al reducir la ventana a 640×480 y ampliarla.

### Dos instancias en una PC

1. Primera instancia: Multijugador → Crear partida → nombre Host → confirma. Debe mostrar Host y `1/4`; iniciar debe estar deshabilitado.
2. Segunda instancia: Multijugador → Buscar sala → nombre Cliente → IP `127.0.0.1` → conecta.
3. Ambas deben mostrar dos jugadores; solo el host puede iniciar.
4. Inicia desde Host. Ambas pasan a Mesa mediante el estado autorizado; cada una ve su mano y cantidades públicas rivales.
5. Sal de sala desde Cliente: debe regresar al lobby con su mensaje. Cierra desde Host: debe volver al lobby y poder regresar al menú.
6. Repite y cierra Host mientras Cliente está conectado. Cliente debe regresar al lobby con el error de desconexión.

### LAN física y límite

1. Usa dos equipos conectados a la misma red. Obtén la IP IPv4 local del host y úsala en Buscar sala. `127.0.0.1` solo sirve en la misma PC.
2. Si Windows pide acceso de red a Godot, permite el acceso adecuado a tu red privada. El host utiliza UDP 7000.
3. Repite inicio de partida y cierre del host. El cliente debe mostrar el estado confirmado y luego regresar al lobby.
4. Conecta hasta cuatro jugadores y comprueba el indicador. Un quinto debe recibir rechazo; no debe aparecer en la partida.

### Diagnósticos ejecutables

Después de compilar, ejecuta el diagnóstico del menú con tu Godot .NET console:

```powershell
& "C:\RUTA\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "C:\Users\ricar\Documents\desmoche" "res://Scenes/Diagnostics/MenuNavigationSmoke.tscn"
```

Debe terminar con `MENU NAVIGATION PASS` y 123 comprobaciones. Con render normal y `--capture-dir` comprueba la GUI nativa y genera once capturas:

```powershell
& "C:\RUTA\Godot_v4.7.2-stable_mono_win64_console.exe" --path "C:\Users\ricar\Documents\desmoche" --rendering-method gl_compatibility --rendering-driver opengl3 "res://Scenes/Diagnostics/MenuNavigationSmoke.tscn" -- --capture-dir="C:\Users\ricar\Documents\Desmoche.Pruebas\TestResults\InicioLobby"
```

Debe terminar con `MENU NAVIGATION PASS` y 151 comprobaciones. La GUI nativa se verifica en un SubViewport mediante eventos de Godot, sin mover el cursor del sistema. La prueba headless utiliza señales públicas y no verifica ese recorrido de input.

El diagnóstico `LobbySmoke` puede ejecutarse en dos PowerShell: primero host, y cuando aparezca `LOBBY HOST LISTO`, cliente. Asegúrate de que no haya otra sala en UDP 7000.

```powershell
# Primera consola
& "C:\RUTA\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "C:\Users\ricar\Documents\desmoche" "res://Scenes/Diagnostics/LobbySmoke.tscn" -- --mode host --players 2 --name Host
```

```powershell
# Segunda consola
& "C:\RUTA\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "C:\Users\ricar\Documents\desmoche" "res://Scenes/Diagnostics/LobbySmoke.tscn" -- --mode client --players 2 --name Cliente --ip 127.0.0.1
```

Ambos deben terminar con `LOBBY OK`: 19 comprobaciones en host y 21 en cliente. El diagnóstico comprueba cancelación y reintento, inicio confirmado, cantidades privadas/públicas y limpieza al perder el host. Este recorrido usa dos procesos reales; no sustituye la prueba física LAN ni la prueba manual de cuatro jugadores.

Los resultados automáticos ejecutados se documentan en `VERIFICACION.md`, con registros y capturas en `Evidence`. Una captura no sustituye una prueba de conexión física LAN.

## 8. Posibles errores

| Síntoma | Diagnóstico / solución mínima |
| --- | --- |
| Sigue apareciendo directamente el lobby | Comprueba que reemplazaste `Main.cs` y `Main.tscn` y que F5 usa `res://Scenes/Main.tscn`. |
| Error de script C# o pantalla vacía | Abre con la edición .NET de Godot, espera la importación y revisa el primer error de compilación. |
| Carga muestra recurso faltante | Confirma que copiaste las escenas, scripts y recursos de tema del paquete preservando las rutas. |
| Buscar sala no lista hosts automáticamente | Es conexión directa por IP en esta revisión; introduce la IP local del host. |
| Sala no se puede crear | Otra instancia puede estar alojando en UDP 7000. Cierra su sala antes de crear otra. |
| IP correcta pero no conecta | Comprueba sala abierta, misma LAN, dirección del adaptador activo, firewall y aislamiento de clientes del router. |
| Iniciar no está disponible | Debes ser host con 2–4 jugadores conectados; espera a que termine el handshake del cliente. |
| Error de ruta RPC al conectar | No renombres Main ni NetworkManager. Conserva `/root/Main/NetworkManager` en ambas instancias. |
| Singleplayer no responde | Está deshabilitado intencionalmente; se implementará en una fase futura. |

## 9. Estado de la fase

FASE COMPLETADA: SI — revisión de presentación implementada, compilada y verificada.

BLOQUEADORES: ninguno para esta entrega. Los árboles se cotejaron con las escenas del paquete y pasaron los diagnósticos de menú, regresión de Mesa y conexión ENet entre dos procesos. La prueba LAN en dispositivos físicos debe repetirse en la instalación del usuario; no se ejecutó aquí.

SIGUIENTE FASE: la que indique el usuario. Singleplayer y descubrimiento de salas no se implementan automáticamente.
