> Actualización 12/10/2026: las reglas de victoria, intercambio, Pasa y acceso por código se detallan en [RevisionReglasCodigoSala.md](RevisionReglasCodigoSala.md). Esa revisión prevalece sobre las instrucciones históricas de este documento.

# Fase 5 — Arquitectura multijugador autoritativa

## 1. Objetivo

Conectar el motor de la fase 4 mediante ENet, con un host que también juega y hasta tres clientes. Las decisiones de juego se ejecutan exclusivamente en el host. Se incluye una escena de simulación automática para probar la red; la interfaz del lobby y de la mesa queda para su fase correspondiente.

## 2. Decisiones arquitectónicas

### Responsabilidades

| Componente | Responsabilidad | Se instancia |
|---|---|---|
| NetworkManager : Godot.Node | Abrir/cerrar ENet, recibir RPC, capturar emisor, enviar vistas individuales y emitir eventos para presentación | Uno por proceso |
| ServerSession | Admitir jugadores, vincular peer con jugador, validar el protocolo, evitar repeticiones, despachar al motor y construir vistas | Solo en el host |
| NetworkProtocol y DTO | Contrato de mensajes, límites y validación estructural; independientes de Godot | Host y clientes |
| GameManager | Reglas de la partida, estados, turnos, prioridades, desconexiones y pagos automáticos | Dentro de ServerSession |
| GameState / RulesValidator | Estado privado e invariantes / validación determinista | Motor del host |
| NetworkSmoke | Prueba automática que usa la misma API pública que utilizará la UI | Solo escena de diagnóstico |

Host y Client son roles de NetworkManager, no clases duplicadas. El host envía sus propias solicitudes al mismo ServerSession que los clientes. Sus acciones de jugador también deben cumplir las reglas.

El árbol de red permanece en /root/Main/NetworkManager. No moverlo ni renombrarlo en un solo equipo. Todos usan los mismos scripts, escena y versión del protocolo. Esta exigencia de rutas y firmas RPC proviene de la [documentación de multiplayer de Godot](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html).

### Autoridad

| Dato o decisión | Autoridad | Qué recibe un cliente |
|---|---|---|
| Baraja, orden de robo, distribución de cartas | Host | Cantidad del mazo |
| Mano de cada jugador | Host | Cartas propias; cantidad de las ajenas |
| Juegos bajados, carta ofrecida, cima del pozo | Host | Información pública |
| Fase, turno, pago pendiente, resultado, ganador | Host | Copia informativa |
| Selección privada del intercambio | Host | Solo confirmación de su propia selección |
| Reclamos pendientes y cartas propuestas | Host | Solo confirmación de su propia respuesta |
| Jugadores, asientos, conexión, sanción | Host | Resumen público |
| Selección visual, arrastre, animaciones | Cliente | Datos de presentación futuros |

Godot mantiene la autoridad del nodo en el peer 1. El ID de jugador lo asigna ServerSession y es diferente del ID de conexión ENet. Un comando no incluye PlayerId: se obtiene a partir de GetRemoteSenderId dentro del RPC y del registro del servidor.

No se serializan GameState ni Player. No se usa MultiplayerSynchronizer para las manos, ni MultiplayerSpawner: esta fase intercambia mensajes y no crea nodos de juego por red. Las vistas contienen copias; modificar una copia en el cliente no modifica el motor.

El host necesariamente conoce todas las manos porque aloja el servidor. Este diseño evita que un cliente remoto decida o reciba estado privado ajeno; no intenta ocultar las manos al dispositivo que ejecuta el servidor.

### RPC exactos

Todos son Reliable, canal 0 y CallLocal=false. Los comandos locales del host entran en una cola y pasan por el mismo despachador.

| RPC | Dirección | Configuración | Datos |
|---|---|---|---|
| Hello(int version, string name) | Cliente → host | AnyPeer | Versión del protocolo y nombre |
| RequestAction(string json) | Cliente → host | AnyPeer | CommandDto |
| Admission(bool accepted, string message) | Host → cliente concreto | Authority | Admisión o rechazo |
| ReceiveView(string json) | Host → cliente concreto | Authority | SnapshotDto exclusivo del destinatario |
| ActionReply(string json) | Host → solicitante | Authority | ReplyDto: aceptación, mensaje, sanción, revisión |

AnyPeer únicamente permite enviar una solicitud: el método comprueba además que se está ejecutando en un host real y que el emisor está admitido. Los RPC de respuesta también comprueban emisor 1. El modo Offline no equivale a alojar una sala.

SceneMultiplayer.AllowObjectDecoding=false evita deserializar objetos Godot. ServerRelay=false desactiva el reenvío entre clientes. Se fija antes de abrir la sesión, siguiendo la [referencia oficial de SceneMultiplayer](https://docs.godotengine.org/en/stable/classes/class_scenemultiplayer.html). Solo se envían enteros, booleanos y JSON con tipos definidos.

### Contrato de solicitudes

CommandDto incluye Version, Session, RequestId, Window y Kind. Según la acción permite CardId, SourceMeldId, InitialKind, DeclareWin, Cards o Plays. PlayDto contiene los IDs de las cartas del juego completo resultante y, si se amplía uno existente, su MeldId.

| Acción | Datos adicionales | Validación del motor |
|---|---|---|
| StartMatch | Ninguno; solo host | Preparación y 2–4 jugadores |
| ExchangeCard | CardId | Intercambio, carta propia, una elección |
| FinishInitial | Ninguno | Declaración inicial pendiente |
| DeclareInitial | InitialKind; Cards cuando corresponda | Regla inicial y propiedad de cartas |
| DrawDeck | Ninguno | Turno habilitado y mazo disponible |
| DrawDiscard | Plays, DeclareWin opcional | Turno, pozo disponible y uso en juego válido |
| Claim | Plays; DeclareWin o InitialKind opcional | Ventana abierta, uso válido y una respuesta |
| Pass | Ninguno | Respuesta pendiente a la oferta actual |
| Publish | Plays | Cartas propias y combinaciones válidas |
| Transfer | SourceMeldId, CardId, un Play de destino | Juegos propios, donante válido y destino válido |
| DeclareDesmoche | Plays opcionales | Momento correcto, diez combinadas y ninguna suelta |
| Pay | CardId | Pago pendiente del jugador y carta propia |
| CompleteWithoutPayment | Ninguno | Pago pendiente y mano vacía |
| PrepareNextMatch | Ninguno; solo host | Partida finalizada |

La prioridad de los reclamos la resuelve GameManager después de recibir todas las respuestas elegibles. No se adjudica la carta al paquete que llega primero.

### Flujo de una acción y sincronización

1. La presentación llama NetworkManager.Submit con la intención del jugador.
2. El mensaje incluye la sesión, un número de solicitud creciente y la ventana observada.
3. En el host se captura el peer emisor y se comprueba su admisión.
4. ServerSession verifica tamaño, estructura, tipos, identidad de sesión, repetición, ventana y permisos del host.
5. GameManager comprueba fase, turno, propiedad, combinaciones y condición de victoria.
6. Se resuelven los reclamos si todos los jugadores habilitados respondieron.
7. El host prepara una vista distinta para cada destinatario y la envía con RpcId.
8. La vista actualizada se envía antes de confirmar la acción, de modo que la próxima solicitud use la ventana nueva.

Revision ordena las vistas; Window identifica la oportunidad de actuar. Responder a una oferta aumenta Revision, pero no cambia Window mientras siga siendo la misma oferta. Así se admiten respuestas simultáneas. Una respuesta de la oferta anterior se rechaza.

Se transmiten también las consecuencias de una declaración errónea: una respuesta Success=false puede incluir una sanción que sí modificó la partida.

### Límites del protocolo

- Nombre: 1–24 caracteres, sin caracteres de control.
- Comando: máximo 8192 bytes UTF-8; profundidad JSON máxima 8.
- Vista recibida: máximo 32768 bytes.
- Hasta 30 solicitudes por segundo por conexión admitida.
- Rechazo de propiedades JSON desconocidas o repetidas, enums desconocidos, IDs fuera de baraja y listas inválidas.
- Tres solicitudes mal formadas o que exceden la cuota cierran la conexión remota; las acciones de juego simplemente inválidas se rechazan sin expulsar.
- Una solicitud repetida no se vuelve a ejecutar. Una petición rechazada por ventana consume igualmente su identificador.
- Handshake y conexión: 10 segundos de espera técnica. No es un límite de tiempo del turno.

El handshake comprueba compatibilidad y plaza; no es un sistema de cuentas ni una contraseña de sala. La protección de turnos y propiedad se vuelve a comprobar en cada acción. Los límites son defensas de la aplicación, no una garantía contra cualquier saturación de la red.

La validación JSON utiliza características de [.NET 8 documentadas por Microsoft](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members).

### Ciclo de conexión y desconexión

Cliente: Offline → Connecting → Handshaking → Connected → Offline.
Host: Offline → Hosting → Offline.

El servidor acepta jugadores únicamente durante Preparing. El límite ENet es de tres conexiones remotas más el host. Tras empezar se rechazan ingresos; no hay reconexión a una mano anterior ni migración de host en esta fase.

- Salida durante lobby: se retira al jugador y se compactan los asientos.
- Salida durante intercambio: conserva su elección si ya la hizo. PROVISIONAL: si falta, se elige su carta de menor ID para completar el intercambio.
- Salida durante declaraciones iniciales: el ausente deja de bloquear la preparación.
- Salida durante reclamos: se retira su respuesta pendiente, se aplican las reglas automáticas y se resuelve cuando los demás respondieron.
- Salida durante su turno: se busca el siguiente jugador habilitado.
- Salida durante pago: paga automáticamente desde su mano.
- Durante la partida: la mano y los juegos del ausente permanecen en el estado; no tiene turnos ni puede ganar. Desconexión y penalización son estados diferentes.
- Al preparar otra partida: se retiran los participantes desconectados antes del siguiente reparto.
- Salida o pérdida del host: la sesión termina; cada cliente limpia su vista. Una pérdida abrupta se detecta cuando ENet informa la desconexión, no instantáneamente.

Se mantienen las decisiones provisionales de fase 4: pago automático con la carta de menor ID, primer juego público compatible por ID y tratamiento de prioridades documentado allí. No se impone un temporizador de juego. ExpireClaimsAsHost queda como punto de entrada local para un futuro temporizador autorizado; no existe un RPC para que un cliente fuerce ese vencimiento.

## 3. Dependencias

Se requieren todas las clases y pruebas aprobadas de fases 2–4. Este paquete es incremental y no contiene otra copia del dominio. No cambia Card, Deck, GameState ni RulesValidator.

Se verifican las firmas contra GodotSharp 4.7.2 instalado. El proyecto de pruebas sigue siendo .NET 8 sin dependencia de Godot.

## 4. Implementación y archivos

Copiar el contenido de las dos carpetas del ZIP sobre las carpetas del mismo nombre en Documentos.

Nuevos:
- res://Scripts/Networking/Core/NetworkProtocol.cs
- res://Scripts/Networking/Core/ServerSession.cs
- res://Scripts/Networking/NetworkManager.cs
- res://Scripts/Diagnostics/NetworkSmoke.cs
- res://Scenes/Diagnostics/NetworkSmoke.tscn
- res://Docs/Fase5.md
- res://Docs/Fase5.puml
- Desmoche.Pruebas/ConnectionLifecycleTests.cs
- Desmoche.Pruebas/NetworkTests.cs
- Desmoche.Pruebas/RunNetworkSmoke.ps1

Reemplazar:
- res://Scripts/Application/GameManager.cs
- Desmoche.Pruebas/Pruebas.csproj
- Desmoche.Pruebas/Program.cs

Los archivos C# entregados están completos. NetworkManager y NetworkSmoke heredan Node y son partial; ServerSession y el protocolo son C# puro. El proyecto de pruebas incluye únicamente Networking/Core, no los adaptadores Godot.

## 5. Configuración Godot

La escena suministrada crea exactamente:

```text
Main (Node) — NetworkSmoke.cs, solo diagnóstico
└── NetworkManager (Node) — NetworkManager.cs
```

No hay campos que completar en el Inspector, autoloads, señales que conectar a mano ni recursos adicionales. La autoridad se fija en 1 desde el script. No hace falta cambiar la escena principal del proyecto: las pruebas abren esta escena explícitamente.

Eventos C# disponibles para la futura presentación:
- ConnectionChanged(ConnectionState, string)
- ViewChanged(SnapshotDto)
- ActionCompleted(ReplyDto)

Estos son eventos C#, no señales Godot visibles en el Inspector.

API pública: Host(nombre, puerto), Join(ip, nombre, puerto), Submit(...), Close() y ExpireClaimsAsHost(). Consultar State, Status y View para representar la sesión. Los clientes no reciben acceso a ServerSession ni al GameManager del servidor.

## 6. Integración

GameManager añade manejo de conexión y consultas de respuestas pendientes. La sesión posee el motor de forma privada. Las llamadas de transporte se ejecutan secuencialmente en el hilo principal de Godot; no se usa Task.Run para modificar el estado.

Al construir la UI posteriormente, conservar Main/NetworkManager durante toda la sesión y cambiar los hijos visuales. No adjuntar los scripts de dominio a cartas visuales ni replicar el estado completo.

## 7. Prueba de aceptación

### A. Copiar y compilar

1. Cerrar las instancias de prueba del juego.
2. Extraer Fase5.zip.
3. Copiar el contenido de desmoche a C:\Users\ricar\Documents\desmoche.
4. Copiar el contenido de Desmoche.Pruebas a C:\Users\ricar\Documents\Desmoche.Pruebas.
5. Aceptar reemplazar únicamente los tres archivos indicados.
6. En PowerShell:

```powershell
cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
dotnet run
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
```

Deben pasar las pruebas anteriores y aparecer CONEXIONES: 207 y PRUEBAS DE RED SIN GODOT COMPLETADAS: 349, sin excepciones.

### B. ENet en el mismo equipo

El script siguiente compila el proyecto, abre procesos de Godot sin ventanas y guarda un registro por participante. No instala programas ni cambia el firewall.

```powershell
cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
$godotExe = "C:\Users\ricar\Downloads\Godot_v4.7.2-stable_mono_win64 (2)\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
.\RunNetworkSmoke.ps1 -GodotPath $godotExe -Players 2
.\RunNetworkSmoke.ps1 -GodotPath $godotExe -Players 3
.\RunNetworkSmoke.ps1 -GodotPath $godotExe -Players 4
.\RunNetworkSmoke.ps1 -GodotPath $godotExe -Players 2 -Scenario ClientDisconnect
.\RunNetworkSmoke.ps1 -GodotPath $godotExe -Players 2 -Scenario HostDisconnect
```

Ajustar $godotExe si moviste Godot. Resultado esperado: PRUEBA ENET COMPLETADA para cada ejecución. Los registros quedan en Desmoche.Pruebas/TestResults/Fase5-... y cada proceso debe escribir RED ENET OK.

La simulación:
- Admite jugadores y rechaza que un cliente inicie la partida.
- Reparte nueve cartas y entrega vistas individuales.
- Completa el intercambio y las declaraciones iniciales.
- Abre ofertas públicas, recoge respuestas y cambia turnos.
- En ClientDisconnect verifica que el ausente conserve nueve cartas mientras continúa el host.
- En HostDisconnect verifica que la vista del cliente se limpie.

Las combinaciones, pagos, victoria y sus casos límite se ejercitan en los tests del motor y del protocolo; esta escena prueba el transporte con una secuencia corta de pases.

### C. LAN / Wi-Fi entre dos equipos

Usar las mismas versiones y archivos del proyecto, compilados en ambos equipos. Ambos deben estar en la misma red local.

En el equipo host, abrir PowerShell y ejecutar (ajustando la ruta de Godot si corresponde):

```powershell
$godotExe = "C:\Users\ricar\Downloads\Godot_v4.7.2-stable_mono_win64 (2)\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/NetworkSmoke.tscn -- --mode host --players 2
```

Consultar la dirección IPv4 del adaptador de red del host con ipconfig. En el otro equipo usar su propia ruta del proyecto y la IP real del host:

```powershell
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/NetworkSmoke.tscn -- --mode client --ip 192.168.1.50 --name Cliente
```

192.168.1.50 es solo un ejemplo: reemplazarla. Iniciar el cliente dentro de un minuto desde que el host crea la sala. Ambos deben mostrar RED ENET OK. Para 3–4 jugadores cambiar --players en el host y abrir más clientes.

Si Windows consulta acceso de red para Godot, permitir el uso en la red privada donde se realizará la prueba. ENet usa el puerto UDP 7000 elegido por este proyecto. En una LAN normal no se necesita configurar reenvío de puertos del router.

## 8. Posibles errores

| Síntoma | Diagnóstico |
|---|---|
| No compila NetworkManager o faltan métodos | Comprobar que se reemplazó GameManager.cs y se usa Godot .NET |
| El proyecto de pruebas intenta compilar Node/Godot | Pruebas.csproj debe incluir Networking/Core, no todo Networking |
| Ejecutar escena muestra solo un mensaje | Sin --mode la escena espera instrucciones; usar los comandos indicados |
| Error de rutas o checksum RPC | Todos deben cargar Main/NetworkManager y los mismos scripts |
| Host no puede abrir el puerto | Cerrar pruebas previas o usar otro --port igual en todos |
| Cliente agota el tiempo | Verificar IP, host activo, puerto UDP, red privada y aislamiento Wi-Fi |
| La versión del protocolo no coincide | Copiar el mismo paquete a todos los equipos |
| Solicitud rechazada por ventana | La oportunidad cambió; usar la vista más reciente, no reenviar ciegamente la acción antigua |
| PowerShell impide ejecutar el script | Ejecutar los comandos manuales de la sección C usando 127.0.0.1 para este equipo |
| Error de creación de archivos durante compilación | Revisar el historial de protección de Windows, como ocurrió en la fase 2 |

No borrar manos ni reconstruir un estado nuevo en un cliente ante un error. El cliente cierra o espera otra vista del host.

## 9. Estado de la fase

La evidencia de compilación y las comprobaciones ejecutadas se registran en VERIFICACION.md, en la raíz del paquete. Pasaron 1279 comprobaciones y la compilación con GodotSharp 4.7.2.

FASE COMPLETADA: NO; implementación terminada, aceptación ENet/LAN pendiente.

BLOQUEADORES: el entorno del asistente no pudo iniciar .NET dentro de Godot para comprobar conexiones reales. Ejecutar las pruebas de transporte en la instalación habitual del usuario.

SIGUIENTE FASE: la que indique el usuario. No se implementa todavía la UI completa.
