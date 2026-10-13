> Actualización 12/10/2026: las reglas de victoria, intercambio, Pasa y acceso por código se detallan en [RevisionReglasCodigoSala.md](RevisionReglasCodigoSala.md). Esa revisión prevalece sobre las instrucciones históricas de este documento.

# Fase 7 — Lobby y conexión LAN

## 1. Objetivo de la fase

Crear las escenas Main y Lobby y permitir crear/unirse a una sala LAN, cancelar una conexión, salir, ver jugadores admitidos y solicitar el inicio. Al recibir el reparto confirmado, todos pasan a una Mesa básica.

Las pruebas de fase 6 fueron aprobadas por el usuario. Esta entrega usa ese transporte sin reemplazarlo. No incorpora Drag & Drop, representación gráfica de cartas ni botones para ejecutar los turnos.

## 2. Decisiones arquitectónicas

- Main es una raíz Control permanente. NetworkManager conserva la ruta /root/Main/NetworkManager durante toda la sesión.
- Lobby y Mesa son escenas instanciadas bajo Main. Se cambia Visible; NO se usa ChangeSceneToFile para pasar a Mesa.
- La UI consulta únicamente NetworkManager.View, State, IsHost y Status. Envía Host, Join, Close y Submit(StartMatch).
- StartMatch sigue siendo validado por ServerSession y GameManager. Un botón habilitado no autoriza una acción por sí mismo.
- El host puede solicitar inicio con 2–4 jugadores admitidos. El botón se bloquea mientras su solicitud está pendiente. Un cliente no lo tiene habilitado; el servidor también rechaza un inicio remoto.
- La transición depende de una vista coherente con Phase distinta de Preparing, nunca de la pulsación ni de un mensaje optimista.
- Nombre de 1–24 caracteres. La IP es una dirección literal IPv4/IPv6. El puerto permanece en UDP 7000, como en el protocolo actual.
- Se muestran las IPv4 candidatas del host. IP.GetLocalAddresses devuelve direcciones de varios adaptadores; no identifica cuál es Wi-Fi. No se agrega descubrimiento, multicast ni selección automática.
- No hay modificación del protocolo 2, autoridad, motor o reglas de juego. Las respuestas asíncronas y errores inmediatos permanecen diferenciados.

### Responsabilidades

| Clase | Responsabilidad | Datos que recibe |
|---|---|---|
| Main : Control | Enlazar pantallas y elegir cuál se ve | Estado de conexión y vista confirmada |
| Lobby : Control | Entradas, botones, lista y mensajes | Nombre/IP locales; vista autorizada |
| Mesa : Control | Mostrar estado y conteos; permitir salir | Vista autorizada |
| NetworkManager : Node | Ciclo ENet, RPC y composición de vistas | Solicitudes de UI y mensajes validados |
| LobbySmoke : Main | Prueba automática usando los botones reales | Argumentos de prueba; no forma parte de Main.tscn |

El motor sigue existiendo exclusivamente en el host dentro de ServerSession. Un cliente que modifique su UI o su copia de View no modifica la partida.

### Autoridad y privacidad

| Información | Autoridad / difusión |
|---|---|
| Admisión, nombres aceptados, IDs, puestos, límite y conexión | Servidor; resumen público |
| Inicio, fase, turnos, conteos, combinaciones visibles | Servidor; proyección pública |
| Identidad de cartas de la mano propia | Servidor; RPC privado al destinatario |
| Identidad de manos rivales, orden/contenido del mazo | Servidor; no se envían en preparación/juego |
| Revelación al finalizar con ganador | Únicamente según el contrato de fase 6 |
| Nombre/IP todavía no enviados, foco, mensajes de validación | Locales de la UI |

Lobby muestra nombre, puesto, Host/Tú y conexión. Mesa muestra conteos públicos y el número de cartas propias. Ninguna escena de esta fase imprime identidades privadas, serializa Player/GameState ni crea un motor cliente.

El host sigue teniendo todas las manos porque es el servidor; esta arquitectura no intenta ocultarlas al propietario del dispositivo host.

## 3. Dependencias

Proyecto existente con Godot.NET.Sdk/4.7.2 y TargetFramework net8.0 en escritorio. Utiliza:

- Scripts/Networking/NetworkManager.cs y PublicMatchReplica.cs de fase 6.
- Scripts/Networking/Core/NetworkProtocol.cs, ServerSession.cs, StateReplication.cs.
- Scripts/Application/GameManager.cs y dominio de las fases previas.
- Proyectos Desmoche.csproj y Desmoche.Pruebas/Pruebas.csproj existentes.

No agregar paquetes NuGet ni autoloads. No adjuntar GameManager a un nodo: sigue siendo C# de aplicación.

## 4. Implementación

Todos los archivos del paquete son completos.

| RUTA en Godot | ARCHIVO / clase / namespace |
|---|---|
| res://Scripts/Presentation/Main.cs | Main.cs — Main : Control — Desmoche.Presentation |
| res://Scripts/Presentation/Lobby.cs | Lobby.cs — Lobby : Control — Desmoche.Presentation |
| res://Scripts/Presentation/Mesa.cs | Mesa.cs — Mesa : Control — Desmoche.Presentation |
| res://Scenes/Main.tscn | Escena permanente con NetworkManager y pantallas |
| res://Scenes/Lobby.tscn | Escena de lobby con controles configurados |
| res://Scenes/Mesa.tscn | Escena mínima para transición |
| res://Scripts/Diagnostics/LobbySmoke.cs | LobbySmoke.cs — LobbySmoke : Main — Desmoche.Diagnostics |
| res://Scenes/Diagnostics/LobbySmoke.tscn | Instancia de Main con script de diagnóstico |
| res://project.godot | Main como escena principal y viewport 1100 × 800 |
| res://Docs/Fase7.md y Fase7.puml | Contrato, configuración y diagrama |

Fuera de res://: Desmoche.Pruebas/RunLobbySmoke.ps1. No reemplaza Program.cs ni Pruebas.csproj.

### Instalación exacta

1. Detener juegos y pruebas anteriores; cerrar Godot antes de copiar project.godot.
2. Extraer Fase7.zip.
3. Copiar el CONTENIDO de la carpeta desmoche del paquete dentro de C:\Users\ricar\Documents\desmoche. Combinar carpetas; no borrar las existentes ni crear desmoche/desmoche.
4. El único archivo existente sustituido es project.godot. La copia conserva la configuración inspeccionada de tu proyecto y establece Main como escena principal. Si cambiaste otros ajustes desde entonces, conservar tu archivo y aplicar manualmente los ajustes de la sección 5.
5. Copiar RunLobbySmoke.ps1 dentro de C:\Users\ricar\Documents\Desmoche.Pruebas.
6. Compilar:

```powershell
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
```

7. Abrir el proyecto en Godot y pulsar F6 sobre Main.tscn, o F5 para ejecutar la escena principal.

No copiar la carpeta de pruebas dentro de res://. Sus archivos de diagnóstico C# sí van en Scripts/Diagnostics; la escena normal no los ejecuta.

## 5. Configuración Godot

Las tres escenas incluidas ya contienen el árbol y sus ajustes. No necesitas recrearlas manualmente. Los nombres son parte del contrato de GetNode y deben conservarse.

### Main.tscn

```text
Main (Control) [Main.cs]
├── NetworkManager (Node) [NetworkManager.cs]
├── Background (ColorRect)
└── Margin (MarginContainer)
    └── Screens (Control)
        ├── Lobby (instancia de Lobby.tscn)
        └── Mesa (instancia de Mesa.tscn; Visible = false)
```

NetworkManager crea durante ejecución, sin agregarlos manualmente:

```text
NetworkManager
├── ReplicatedState (Node)
│   └── PublicState (PublicMatchReplica; cuando el servidor la crea)
│       └── StateSynchronizer (MultiplayerSynchronizer)
└── StateSpawner (MultiplayerSpawner)
```

### Lobby.tscn

```text
Lobby (Control) [Lobby.cs]
└── Scroll (ScrollContainer)
    └── Center (CenterContainer)
        └── Panel (PanelContainer)
            └── Padding (MarginContainer)
                └── Content (VBoxContainer)
                    ├── TitleLabel (Label)
                    ├── SubtitleLabel (Label)
                    ├── NameLabel (Label)
                    ├── NameInput (LineEdit)
                    ├── IPLabel (Label)
                    ├── IPInput (LineEdit)
                    ├── ConnectButtons (HBoxContainer)
                    │   ├── CreateButton (Button)
                    │   └── JoinButton (Button)
                    ├── StatusLabel (Label)
                    ├── AddressLabel (Label)
                    ├── PlayersTitle (Label)
                    ├── PlayersList (ItemList)
                    ├── RoomButtons (HBoxContainer)
                    │   ├── StartButton (Button)
                    │   └── LeaveButton (Button)
                    ├── MessageLabel (Label)
                    └── HintLabel (Label)
```

### Mesa.tscn

```text
Mesa (Control) [Mesa.cs]
└── Scroll (ScrollContainer)
    └── Center (CenterContainer)
        └── Panel (PanelContainer)
            └── Padding (MarginContainer)
                └── Content (VBoxContainer)
                    ├── TitleLabel (Label)
                    ├── StatusLabel (Label)
                    ├── PhaseLabel (Label)
                    ├── TurnLabel (Label)
                    ├── CountsLabel (Label)
                    ├── OwnHandLabel (Label)
                    ├── PlayersList (ItemList)
                    └── LeaveButton (Button)
```

### Inspector y proyecto

| Nodo / ajuste | Valor exacto |
|---|---|
| Raíces Main/Lobby/Mesa | Control, Layout Full Rect; script según árbol |
| Background | Full Rect; Mouse Filter = Ignore |
| Main/Margin | Full Rect; márgenes 16 en los cuatro lados |
| Screens | Tamaño administrado por Margin; ambas escenas ancladas Full Rect |
| Mesa instanciada en Main | Visible desactivado inicialmente |
| Scroll de ambas pantallas | Full Rect; Horizontal Scroll Mode = Disabled; Follow Focus activado |
| Center dentro de Scroll | Size Flags Horizontal/Vertical = Expand Fill |
| Panel | Custom Minimum Size = (560, 0) |
| Padding | Márgenes de tema 24 en los cuatro lados |
| Content | VBoxContainer; Separation = 12 |
| NameInput | Max Length = 24; Editable inicialmente true |
| IPInput | Max Length = 64; Editable inicialmente true; Text vacío |
| Botones | Custom Minimum Size Y = 44; botones en HBox con Horizontal Expand Fill |
| Lobby/PlayersList | Custom Minimum Size Y = 128; Focus Mode = None |
| Mesa/PlayersList | Custom Minimum Size Y = 168; Focus Mode = None |
| StartButton / LeaveButton en Lobby | Disabled inicialmente true |
| Etiquetas largas | Autowrap Mode = Word Smart |
| Project Settings → Application → Run → Main Scene | res://Scenes/Main.tscn |
| Display → Window → Size → Viewport Width/Height | 1100 / 800 |
| Display → Window → Stretch → Mode/Aspect | canvas_items / expand |

Main configura Window.MinSize = (640, 480) en ejecución; no se agregan ajustes ficticios min_width/min_height a ProjectSettings. El scroll permite alcanzar controles al reducir la altura.

El tema de Main usa fuente de 18 y panel oscuro; se hereda a ambas pantallas. Los archivos .tscn incluyen colores y tamaños, por lo que no hay recursos gráficos adicionales.

No existen propiedades Export pendientes de rellenar. NetworkManager establece autoridad 1 mediante su código. Los controles no reciben autoridad para modificar datos del servidor.

### Señales y eventos

Conectados desde C#, sin conexiones manuales adicionales en la pestaña Señales:

- CreateButton.Pressed → Lobby.CreateRoom.
- JoinButton.Pressed e IPInput.TextSubmitted → Lobby.JoinRoom.
- StartButton.Pressed → Lobby.StartMatch.
- LeaveButton.Pressed → Close desde Lobby o Mesa.
- NetworkManager.ViewChanged → render de ambas pantallas y selección de pantalla en Main.
- ConnectionChanged → estado/campos/mensajes y regreso a Lobby.
- ActionCompleted → liberar la solicitud StartMatch pendiente y mostrar rechazo si ocurre.

Cada pantalla desuscribe eventos al salir del árbol. No agregar un segundo NetworkManager ni un autoload con el mismo script.

## 6. Integración

### Crear sala

Botón → validar nombre → NetworkManager.Host → ENet servidor para tres invitados → ServerSession con host → vista Preparing → lista pública con host. El inicio queda bloqueado mientras haya un solo jugador.

### Unirse

Botón/Enter en IP → validar nombre y dirección → Join → Connecting → Handshaking → Hello/Admission existentes → proyección pública + mano privada coherentes → Connected → lista admitida.

La UI no incorpora al jugador de forma anticipada. Una conexión ENet todavía no admitida no cuenta como jugador.

### Iniciar y pasar a Mesa

Host → Submit(StartMatch) → validaciones del servidor → reparto de nueve → vista InitialExchange → Main muestra Mesa para cada destinatario → respuesta correlacionada.

La Mesa debe indicar Intercambio inicial, nueve cartas propias, pozo vacío y mazo de 34/25/16 para 2/3/4 jugadores. Todavía no contiene los controles para elegir la carta del intercambio; permanecer ahí es el resultado esperado de esta fase.

### Desconectar

- Cancelar Connecting/Handshaking: Close libera el peer y restaura campos.
- Invitado sale en Lobby: servidor lo elimina de la lista y libera el puesto.
- Invitado sale durante partida: el servidor conserva sus cartas y lo marca desconectado; los demás permanecen en Mesa.
- Host cierra desde cualquiera de las pantallas o se pierde: los clientes vuelven a Lobby, View queda null y se eliminan réplicas.
- No hay migración de host ni reconexión a una partida iniciada. Estas políticas no cambian respecto a fases previas.

### LAN real

En cada PC usar el mismo proyecto actualizado y Godot .NET compatible. No se comparte la carpeta .godot entre PCs: cada instalación compila localmente.

1. Conectar equipos al mismo Wi-Fi o LAN.
2. Host escribe su nombre y pulsa Crear sala.
3. Host identifica la IPv4 de Wi-Fi/Ethernet entre las candidatas mostradas. Si hay varias, abrir PowerShell y ejecutar ipconfig; buscar Dirección IPv4 del adaptador que está conectado a esa red.
4. Clientes introducen ESA IP y su nombre; pulsan Unirse a sala.
5. Esperar que los admitidos aparezcan en todas las listas. Host inicia con 2–4.

127.0.0.1 solo sirve entre instancias de la MISMA PC. No usar IP pública para esta prueba LAN.

Si Windows solicita acceso, permitir Godot en la red privada de confianza. Si bloquea sin preguntar, abrir Seguridad de Windows → Firewall y protección de red → Permitir una aplicación a través del firewall y permitir el ejecutable Godot que se está usando para redes privadas. No desactivar el firewall.

ENet usa UDP. El proyecto escucha 7000; no requiere abrir puertos del router para equipos de una misma LAN. Una red de invitados o un punto de acceso con aislamiento de clientes puede impedir la comunicación aunque todos vean el mismo Wi-Fi.

## 7. Prueba de aceptación

### A. Compilación y pruebas anteriores

```powershell
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
Set-Location "C:\Users\ricar\Documents\Desmoche.Pruebas"
dotnet run
```

Deben seguir aprobadas las 1908 comprobaciones y el catálogo de 1056 escaleras. Esta fase no cambia el núcleo.

### B. Prueba automática de los controles y transición

Cerrar otras salas que ocupen UDP 7000. Usar la ruta de tu ejecutable console de Godot:

```powershell
Set-Location "C:\Users\ricar\Documents\Desmoche.Pruebas"
$godotExe = "C:\Users\ricar\Downloads\Godot_v4.7.2-stable_mono_win64 (2)\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 2
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 3
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 4
```

Cada ejecución compila, abre instancias de diagnóstico y guarda registros en TestResults/Fase7-fecha. Debe finalizar con PRUEBA LOBBY COMPLETADA y OK por instancia.

Comprueba controles reales, no una copia del modelo UI: rechazo de IP inválida, cancelación y reintento, campos bloqueados, lista admitida, host con mínimo dos, solicitud pendiente, ausencia de transición optimista, Mesa con nueve cartas, ruta persistente y limpieza al cerrar el host.

La prueba emite señales de botones en modo headless. No verifica dibujo, clic de ratón, conectividad entre PCs ni rechazo del quinto; esas comprobaciones son manuales.

Si PowerShell impide ejecutar el archivo por la marca de descarga, desbloquear ESTE archivo desde sus Propiedades → Desbloquear, o usar los comandos manuales siguientes. No cambiar la política global.

Alternativa manual en DOS terminales, primero host y después cliente:

```powershell
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/LobbySmoke.tscn -- --mode host --players 2
```

```powershell
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/LobbySmoke.tscn -- --mode client --players 2 --ip 127.0.0.1 --name Cliente
```

### C. UI visible en una PC

En Godot, abrir Depurar → Customize Run Instances… (Personalizar instancias de ejecución; el texto puede variar con el idioma), activar Enable Multiple Instances y elegir 2, 3 o 4. Ejecutar F5. Todos deben ejecutar Main.

| Caso | Resultado exigido |
|---|---|
| Nombre vacío → Crear/Unirse | Mensaje; sin conexión |
| IP vacía o texto inválido → Unirse | Mensaje; sin conexión |
| Host solo crea sala | Lista 1/4; no puede iniciar |
| Clientes usan 127.0.0.1 y nombres | Todas las listas coinciden, máximo 4/4 |
| Cliente conectado | Nombre/IP bloqueados; inicio deshabilitado |
| Invitado sale en lobby y vuelve | Sale de todas las listas; puede ingresar otra vez |
| Cancelar conexión y reintentar | Campos libres, lista vacía, sin errores residuales |
| Host pulsa Iniciar | Todas las ventanas pasan a Mesa; fase InitialExchange |
| Reparto | Mano propia 9; mazo 34/25/16; pozo 0 |
| Invitado sale desde Mesa | Invitado vuelve a Lobby; los demás siguen en Mesa y lo ven desconectado |
| Host cierra desde Mesa | Todos vuelven a Lobby; se puede crear una sala nueva |
| Reducir alto a 480 y navegar Tab | Campos/botones alcanzables con scroll y foco |
| Cerrar ventana host durante conexión/juego | Clientes muestran pérdida del host y vuelven a Lobby |

Para probar IP válida de un host inexistente, usar una dirección libre conocida de tu red. No hay admisión y debe aparecer fallo/agotamiento de conexión; el límite de 10 segundos también cubre la sincronización. Cancelar debe ser posible antes.

### D. Límite y admisión

Con un host y tres clientes en Lobby (SIN iniciar), abrir una quinta instancia con nombre y 127.0.0.1. Debe fallar la conexión/admisión; ninguna lista puede superar cuatro. ENet puede rechazarla antes de Hello y mostrar un error genérico o timeout: no siempre habrá un mensaje específico de sala llena.

Salir con un invitado, comprobar 3/4 y volver a intentar con el quinto: ahora debe ingresar. Tras iniciar, un nuevo intento de conexión debe rechazarse y no afectar a los participantes.

### E. Dos PCs de la misma LAN

Repetir crear/unirse, listas, inicio, salida de invitado y cierre del host entre dos equipos con la IP local real. Si es posible, repetir con 3 y 4 equipos. La prueba localhost no sustituye esta aceptación.

## 8. Posibles errores

| Síntoma | Diagnóstico y corrección mínima |
|---|---|
| F5 muestra la escena vacía o el diagnóstico antiguo | Establecer Main.tscn como Main Scene |
| No encuentra un GetNode | Copia incompleta o nodo renombrado; usar árboles exactos |
| No reconoce Main/Lobby/Mesa | Copiar Scripts/Presentation, compilar y revisar namespace Desmoche.Presentation |
| Sala no se crea | Puerto UDP 7000 ocupado; cerrar pruebas/otra sala antes de reintentar |
| Unirse funciona solo con 127.0.0.1 | Revisar IPv4 real, firewall privado y aislamiento Wi-Fi |
| Varias IP del host | Elegir la del adaptador Wi-Fi/Ethernet de esa LAN; VPN/virtuales pueden aparecer |
| Lista se queda en uno | Cliente todavía conectando, no admitido, o falla de sincronización |
| Cuatro conectados y quinto falla genérico | Resultado válido del límite ENet; la quinta instancia no debe entrar |
| Inicio permanece deshabilitado | Es cliente, hay menos de dos admitidos, o hay solicitud pendiente |
| Mesa permanece en Intercambio inicial | Esperado: controles de juego pertenecen a la siguiente fase |
| Conexión desaparece al ir a Mesa | Se reemplazó Main; restaurar cambio de Visible y NetworkManager persistente |
| Protocol/checksum/path mismatch | Todos los equipos deben copiar el mismo código y escenas de fases 6 y 7 |
| Botones ejecutan acciones duplicadas | Quitar conexiones manuales de señales; C# ya las conecta |
| Script automático falla | Leer TestResults/Fase7-fecha/Host.log y JugadorN.log antes de cambiar el proyecto |

No solucionar un fallo de red otorgando autoridad al cliente, exponiendo el motor ni enviando todas las manos.

## 9. Estado de la fase

Implementación completa de pantallas, conexión y pruebas de aceptación. Todos los scripts, incluido el diagnóstico, compilan contra GodotSharp y el generador 4.7.2 con advertencias tratadas como errores. Las 1908 comprobaciones del núcleo y el catálogo de 1056 escaleras siguen aprobados.

FASE COMPLETADA: NO; pendiente de ejecutar la prueba nueva y la aceptación visual/LAN en tu instalación.

BLOQUEADORES: el entorno del asistente falla al inicializar Godot .NET con ".NET: Assemblies not found", antes de ejecutar lógica. Por eso no se afirma que LobbySmoke o la UI se hayan ejecutado aquí. No cambia el resultado de tus pruebas previas aprobadas.

SIGUIENTE FASE: la que indiques después de aprobar esta fase; no se ha implementado.

Referencias oficiales verificadas:

- [Multijugador de alto nivel y LAN](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html).
- [LineEdit](https://docs.godotengine.org/en/stable/classes/class_lineedit.html).
- [ItemList](https://docs.godotengine.org/en/stable/classes/class_itemlist.html).
- [IP.GetLocalAddresses](https://docs.godotengine.org/en/stable/classes/class_ip.html).
- [Contenedores de UI](https://docs.godotengine.org/en/stable/tutorials/ui/gui_containers.html).
- [ScrollContainer.follow_focus](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html#class-scrollcontainer-property-follow-focus).
- [Window.min_size](https://docs.godotengine.org/en/stable/classes/class_window.html#class-window-property-min-size).
- [Herramientas de depuración / varias instancias](https://docs.godotengine.org/en/stable/tutorials/scripting/debug/overview_of_debugging_tools.html).
- [Firewall y protección de red de Windows](https://support.microsoft.com/es-es/windows/security/windows-security/firewall-and-network-protection-in-the-windows-security-app).
