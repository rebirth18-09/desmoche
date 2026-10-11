# Fase 6 — Sincronización de estado

## 1. Objetivo de la fase

Integrar MultiplayerSpawner y MultiplayerSynchronizer con el servidor de fase 5. El motor continúa siendo la única fuente de verdad. La presentación recibe una vista coherente que combina datos públicos y la mano del destinatario.

La fase 5 queda aprobada por las pruebas que el usuario confirmó. Esta entrega amplía el transporte; no cambia las reglas del juego ni implementa Drag & Drop.

## 2. Decisiones arquitectónicas

### Componentes y responsabilidades

| Componente | Responsabilidad |
|---|---|
| GameManager / GameState / RulesValidator | Conservan autoridad y validaciones de fases anteriores; no se modifican |
| ServerSession | Identifica jugadores, procesa solicitudes y produce vistas individuales; no se modifica |
| NetworkManager | Abre ENet, controla admisiones, crea la réplica pública, envía la parte privada y entrega vistas completas |
| PublicMatchReplica : Node | Almacena exclusivamente PublicJson y configura su MultiplayerSynchronizer |
| StateProjection | Separa SnapshotDto en listas explícitas de datos públicos y privados; realiza copias defensivas |
| ClientStateAssembler | Combina las dos partes únicamente cuando tienen igual sesión y revisión |
| NetworkSmoke : Node | Comprueba transporte, autoridad, réplica, privacidad y limpieza; no es UI del juego |

Host y cliente siguen siendo roles de NetworkManager. El host es servidor y también jugador. La UI del host recibe la misma proyección pública/privada que el resto.

### Qué viaja y por dónde

| Información | Transporte | Destinatario |
|---|---|---|
| Jugadores: ID, nombre, asiento, conectado, eliminado y cantidad de cartas | PublicJson mediante synchronizer | Todos los admitidos |
| Fase, resultado, ganador, turno y pago pendiente | PublicJson | Todos los admitidos |
| Cantidad del mazo | PublicJson | Todos los admitidos |
| Cantidad y cima del pozo; carta ofrecida | PublicJson | Todos los admitidos |
| Juegos ya bajados y sus cartas | PublicJson | Todos los admitidos |
| Cartas de la mano propia | RPC ReceiveView con PrivateSnapshotDto | Solo el dueño |
| Confirmación de su elección/respuesta pendiente | RPC privado | Solo ese jugador |
| Todas las manos al producirse una victoria válida | RevealedHands dentro de PublicJson | Todos; regla ya acordada |
| Orden del mazo, semilla de barajado, manos rivales antes de ganar | No se transmite | Solo el servidor |
| Elecciones de intercambio y contenido de reclamos pendientes | No se transmite a otros jugadores | Solo el servidor |

Una carta descartada u ofrecida ya es pública según las reglas. El pozo conserva su cantidad y cima en la vista; no se añade un historial completo de descartes.

### Uso de MultiplayerSpawner

NetworkManager crea un StateSpawner con autoridad 1, SpawnLimit=1 y SpawnPath=../ReplicatedState.

El servidor crea una sola réplica por sesión: PublicState. Esta representa conjuntamente lobby y mesa pública. Los jugadores son entradas de su lista pública, no nodos que posean manos replicadas. Esto evita crear estructuras visuales antes de su fase y mantiene cada actualización pública completa.

SpawnFunction devuelve un PublicMatchReplica configurado fuera del árbol. MultiplayerSpawner lo inserta y replica su aparición. El argumento de aparición es exclusivamente un PublicSnapshotDto serializado; nunca contiene SnapshotDto completo ni objetos del dominio.

La propiedad de aparición PublicJson contiene el estado actual al admitir un nuevo jugador. El dato inicial de SpawnFunction puede corresponder a una revisión anterior de la misma sesión: el synchronizer entrega la propiedad actual antes de _Ready. El ensamblador solo utiliza revisiones compatibles.

Al cerrar la conexión se retira la réplica del árbol, se vacían vistas y confirmaciones y se vuelve a Offline. La réplica se conserva al preparar otra partida dentro de la misma sesión.

### Uso de MultiplayerSynchronizer

PublicMatchReplica configura exactamente:

| Propiedad | Valor |
|---|---|
| Nombre | StateSynchronizer |
| RootPath | .. |
| Autoridad | 1 |
| PublicVisibility | false |
| Propiedad registrada | :PublicJson |
| Enviar al aparecer | true |
| Modo de replicación | OnChange |
| DeltaInterval | 0 |

No se registran otras propiedades. PublicJson es una cadena JSON con datos públicos. No se sincronizan instancias C# de GameState, Player, Card ni Resource.

OnChange entrega cambios de forma fiable; Always utiliza transporte no fiable, por lo que no se usa para esta vista discreta de cartas. Véase la [referencia oficial de SceneReplicationConfig](https://docs.godotengine.org/en/stable/classes/class_scenereplicationconfig.html).

La réplica nace invisible para peers remotos. Solo después de aceptar Hello se concede visibilidad al peer admitido. Se retira ese permiso al desconectarse, incluso si Godot ya eliminó la conexión. La [documentación de MultiplayerSynchronizer](https://docs.godotengine.org/en/stable/classes/class_multiplayersynchronizer.html) describe cómo esa visibilidad controla también nodos creados por un spawner.

### Autoridad y RPC

Todos los nodos de red, el spawner, la réplica y el synchronizer pertenecen al peer 1. Ningún cliente recibe autoridad sobre ellos.

Se conservan las cinco firmas RPC de fase 5, Reliable, canal 0 y CallLocal=false:

| RPC | Dirección | Contenido en fase 6 |
|---|---|---|
| Hello(int version, string name) | Cliente → host, AnyPeer | Versión y nombre; validar admisión |
| RequestAction(string json) | Cliente → host, AnyPeer | CommandDto; mismas validaciones autoritativas |
| Admission(bool accepted, string message) | Host → cliente, Authority | Si acepta, AdmissionDto con versión, sesión y destinatario; si rechaza, explicación |
| ReceiveView(string json) | Host → dueño, Authority | Ahora exclusivamente PrivateSnapshotDto |
| ActionReply(string json) | Host → solicitante, Authority | Resultado y revisión de la acción |

El protocolo pasa de versión 1 a **2**. Todos los equipos deben instalar esta fase antes de conectarse.

Cambiar una propiedad de la réplica en un cliente puede alterar su propia memoria, pero no cambia GameState ni autoriza una acción en el servidor. Los mensajes de sincronización se aceptan desde la autoridad configurada. Las acciones continúan verificando peer, sesión, repetición, oportunidad de actuar, propiedad de cartas, fase, turno y reglas.

### Coherencia entre el canal público y el privado

No se presupone que un RPC llegue antes o después del synchronizer.

Ejemplo:
1. El cliente tiene la vista de revisión 20.
2. Recibe su mano privada de revisión 21: conserva la vista 20.
3. Recibe PublicJson de revisión 21: publica la vista 21 completa.
4. Si el synchronizer agrupa cambios y salta a 23, espera la parte privada 23 y omite las revisiones intermedias.

El ensamblador conserva solo la última parte pública y la última privada. No acumula un historial ilimitado. Rechaza partes de otra sesión, otro destinatario o revisiones antiguas; comprueba cantidades y ausencia de colisiones entre cartas propias y públicas.

Window sigue identificando una oportunidad de juego; Revision ordena los estados. Una respuesta de otro jugador no invalida automáticamente todas las solicitudes simultáneas.

ActionReply se guarda hasta que la vista presentada tenga una revisión igual o posterior a la respuesta. Así, la UI no recibe una confirmación y envía inmediatamente otra acción utilizando una ventana anterior.

### Análisis de filtraciones

| Riesgo | Medida aplicada |
|---|---|
| Serializar GameState o Player completos | Proyecciones con campos permitidos explícitamente |
| Incluir la mano en propiedades de aparición | PublicMatchReplica solo exporta PublicJson |
| Compartir un nodo con sincronizadores públicos y privados | La parte privada no utiliza synchronizer ni spawn |
| Dejar visibilidad pública por defecto | PublicVisibility=false antes de entrar al árbol |
| Recibir datos antes de ser admitido | Permisos individuales tras Hello; prueba AdmissionGate |
| Nombres como Carta_17 o metadatos con IDs ocultos | Un único nombre fijo PublicState; no hay nodos de cartas privadas |
| Reutilizar datos después de desconectar | Limpieza de buffers, vista, réplica, permisos y sesión |
| Mezclar mano y mesa de revisiones diferentes | Ensamblado por sesión y revisión exactas |
| Mutar un DTO desde la presentación | Copias defensivas sin referencias al dominio |

No deben añadirse manos privadas a este nodo aunque se pretenda ocultarlas con otro synchronizer: el control de aparición y sus propiedades puede combinar los sincronizadores del mismo nodo. Mantener la separación física de los canales.

El host necesariamente conoce las manos porque ejecuta el servidor. El envío dirigido evita entregar manos a otros clientes de la aplicación; esta fase no configura cifrado del transporte ni pretende proteger contra un host modificado o captura de tráfico en una red comprometida.

## 3. Dependencias

Fases 2–5 instaladas y aprobadas. Godot 4.7.2 .NET y el proyecto de pruebas .NET 8 existentes.

No se modifican las reglas, el reparto, el motor de turnos ni la política de desconexión. Continúan las decisiones provisionales ya documentadas en fase 5.

## 4. Implementación

Los archivos del paquete son completos. Copiar sobre las carpetas existentes.

Nuevos:
- res://Scripts/Networking/PublicMatchReplica.cs — Desmoche.Networking, partial Node.
- res://Scripts/Networking/Core/StateReplication.cs — Desmoche.Networking.Core, DTO, StateProjection y ClientStateAssembler, sin Godot.
- Desmoche.Pruebas/StateReplicationTests.cs — pruebas de separación y ensamblado.
- Desmoche.Pruebas/RunSynchronizationSmoke.ps1 — prueba de procesos Godot.
- res://Docs/Fase6.md.
- res://Docs/Fase6.puml.

Reemplazar:
- res://Scripts/Networking/NetworkManager.cs.
- res://Scripts/Networking/Core/NetworkProtocol.cs.
- res://Scripts/Diagnostics/NetworkSmoke.cs.
- Desmoche.Pruebas/NetworkTests.cs — adapta los tests anteriores a la constante de versión.
- Desmoche.Pruebas/Program.cs — añade StateReplicationTests.Run().

No reemplazar Pruebas.csproj: el de fase 5 ya incluye Networking/Core/*.cs. La escena NetworkSmoke.tscn existente se reutiliza.

## 5. Configuración Godot

La escena existente conserva Main y NetworkManager. Al ejecutarse se genera:

```text
Main (Node, NetworkSmoke.cs solo en diagnóstico)
└── NetworkManager (Node, NetworkManager.cs, autoridad 1)
    ├── ReplicatedState (Node)
    │   └── PublicState (PublicMatchReplica, creado por spawner, autoridad 1)
    │       └── StateSynchronizer (MultiplayerSynchronizer, autoridad 1)
    └── StateSpawner (MultiplayerSpawner, autoridad 1)
```

No crear manualmente estos hijos dinámicos ni adjuntar el dominio a nodos visuales. No se requiere configuración nueva en el Inspector ni señales manuales.

PublicMatchReplica escucha DeltaSynchronized y Synchronized; también procesa la propiedad inicial en _Ready. NetworkManager mantiene ViewChanged, ActionCompleted y ConnectionChanged como eventos C# para la futura UI.

## 6. Integración

La API pública Host, Join, Submit, Close y ExpireClaimsAsHost se conserva. View sigue siendo SnapshotDto, por lo que la futura presentación no necesita conocer qué parte llegó por RPC o por synchronizer.

Los datos autoritativos permanecen exclusivamente dentro del ServerSession del host. El código de sincronización produce y transporta proyecciones; jamás utiliza la réplica como entrada del motor.

No se cambian reglas de victoria: las manos se revelan al haber ganador, no por un empate ni por una desconexión.

## 7. Prueba de aceptación

### Copiar y comprobar el núcleo

1. Extraer Fase6.zip.
2. Copiar el contenido de desmoche sobre C:\Users\ricar\Documents\desmoche.
3. Copiar el contenido de Desmoche.Pruebas sobre C:\Users\ricar\Documents\Desmoche.Pruebas.
4. Reemplazar los cinco archivos indicados y conservar los anteriores restantes.
5. Ejecutar:

```powershell
cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
dotnet run
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
```

Resultado esperado: todas las suites anteriores correctas y SINCRONIZACION DE ESTADO: 629 comprobaciones correctas. Total: 1908, además del catálogo previo de 1056 casos de escalera.

Los tests nuevos comprueban proyección sin secretos, límites y tipos, copias, partes públicas/privadas en ambos órdenes, saltos de revisión, mensajes de otra sesión/destinatario, limpieza y vistas reales de ServerSession con 2–4 jugadores.

### Probar spawner y synchronizer reales

En PowerShell:

```powershell
cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
$godotExe = "C:\Users\ricar\Downloads\Godot_v4.7.2-stable_mono_win64 (2)\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 2
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 3
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 4
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 2 -Scenario ClientDisconnect
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 2 -Scenario HostDisconnect
.\RunSynchronizationSmoke.ps1 -GodotPath $godotExe -Players 2 -Scenario AdmissionGate
```

Ajustar la ruta de Godot si cambió. El script compila, inicia los procesos y guarda registros en Desmoche.Pruebas/TestResults/Fase6-...

Resultados esperados:
- PRUEBA SINCRONIZACION COMPLETADA al terminar cada ejecución.
- SYNC OK con revisiones coherentes en host y clientes normales.
- SYNC LIMPIEZA OK al cerrar cada proceso.
- RED ENET OK en todos los procesos.
- En AdmissionGate: SYNC PRIVACIDAD OK: ninguna replica antes de la admision.

AdmissionGate conecta primero un proceso ENet que deliberadamente no envía Hello. Verifica durante 1,5 segundos que no aparezca PublicState ni una vista; después cierra y se ejecuta la partida normal. No añade una vía de omisión de admisión al código de producción.

El diagnóstico revisa la autoridad 1, una sola réplica, PublicVisibility=false y que la única propiedad configurada sea :PublicJson, con spawn y OnChange habilitados. También comprueba que una confirmación no llegue a la presentación antes de su vista y que desaparezcan todos los datos al cerrar.

### Entre equipos en LAN

Repetir la prueba LAN de fase 5 con los archivos de fase 6 en ambos equipos. Se mantiene la escena res://Scenes/Diagnostics/NetworkSmoke.tscn y el puerto UDP 7000.

Host:

```powershell
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/NetworkSmoke.tscn -- --mode host --players 2
```

Cliente (reemplazar la IP y las rutas con las reales de ese equipo):

```powershell
& $godotExe --headless --path "C:\Users\ricar\Documents\desmoche" --scene res://Scenes/Diagnostics/NetworkSmoke.tscn -- --mode client --ip 192.168.1.50 --name Cliente
```

Los dos equipos deben mostrar SYNC OK y RED ENET OK. La prueba del cliente sin admitir también puede ejecutarse manualmente con --mode probe, mientras el host espera en el lobby.

## 8. Posibles errores

| Síntoma | Diagnóstico mínimo |
|---|---|
| No existe PublicMatchReplica / StateProjection | Falta copiar uno de los archivos nuevos |
| Protocolo incompatible | Algún equipo sigue con fase 5; actualizar todos a versión 2 |
| No aparece PublicState en el cliente | Revisar Hello/Admission, visibilidad y ruta Main/NetworkManager |
| Espera de sincronización hasta agotar tiempo | Revisar configuración :PublicJson, OnChange y RPC ReceiveView |
| Error checksum o ruta RPC | Todos deben usar la misma escena raíz y los mismos scripts |
| Confirmación antes de vista | Revisar que esté instalado el NetworkManager de esta fase |
| No desaparece la réplica al salir | No eliminar la llamada Close ni cambiar los nodos dinámicos |
| Intento de recibir SnapshotDto completo por ReceiveView | Se mezclaron versiones; el RPC ahora transporta PrivateSnapshotDto |
| Puerto ocupado | Cerrar procesos previos o indicar --port igual en todos |
| PowerShell bloquea el script | Utilizar los comandos manuales de host y cliente; para una PC, IP 127.0.0.1 |

No corregir un problema de sincronización permitiendo al cliente escribir en GameManager ni enviando todas las manos para ocultarlas después en la UI.

## 9. Estado de la fase

Implementación y 1908 comprobaciones del núcleo completadas. Compilación de todos los scripts contra GodotSharp y el generador de Godot 4.7.2: correcta, con advertencias como errores.

FASE COMPLETADA: NO; pendiente de aceptación con spawner/synchronizer reales en la instalación habitual del usuario.

BLOQUEADORES: en el entorno del asistente, Godot falla al inicializar .NET con ".NET: Assemblies not found", antes de ejecutar los scripts. Las pruebas reales aprobadas por el usuario correspondían a fase 5; no sustituyen la aceptación de esta ampliación.

SIGUIENTE FASE: la que indique el usuario después de confirmar estas pruebas.

Ver VERIFICACION.md en la raíz del paquete para distinguir comprobaciones ejecutadas y pendientes.

Referencias oficiales:
- [MultiplayerSpawner](https://docs.godotengine.org/en/stable/classes/class_multiplayerspawner.html).
- [MultiplayerSynchronizer](https://docs.godotengine.org/en/stable/classes/class_multiplayersynchronizer.html).
- [SceneReplicationConfig](https://docs.godotengine.org/en/stable/classes/class_scenereplicationconfig.html).
