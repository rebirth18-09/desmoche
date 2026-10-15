> Revisión posterior 12/10/2026: [VictoriasVisiblesYPagos.md](VictoriasVisiblesYPagos.md) actualiza el plazo y ubicación de Pasa, muestra la evidencia de victoria y excluye al pagador de su oferta. Protocolo actual: 4.

> Corrección posterior de Peladilla: [Peladilla.md](Peladilla.md) sustituye únicamente las referencias de este documento a ganar con parejas o proyectos. La regla actual excluye también los juegos que se completarían con una carta más; el botón sigue dependiendo de los juegos visibles.

# Revisión de reglas, intercambio y código de sala — 12 de octubre de 2026

Este documento reemplaza las reglas anteriores que exigían cinco cartas en las escaleras iniciales, declaración manual de victoria normal o una posible carta futura para excluir Peladilla. Conserva los diseños de menú aportados por el usuario. No introduce una fase posterior.

## 1. Objetivo de la fase

Aplicar las reglas confirmadas en LAN y Singleplayer: cambio con ocho cartas pendientes, victoria normal automática, declaraciones iniciales contextuales, colores alternados con oportunidad de «Pasa» y acceso a salas LAN mediante código.

## 2. Decisiones arquitectónicas

- `GameState` reserva en una zona privada las cartas confirmadas para el cambio. Cada donante queda con ocho. Al confirmar todos, cada jugador recibe la carta de su izquierda; se conserva el intercambio simultáneo acordado. La reserva no es el pozo. Una victoria anterior a completar el cambio devuelve las cartas reservadas a sus donantes antes de terminar.
- Conservación: **mazo + pozo + manos + juegos + oferta + reserva de cambio = 52**, sin identidades repetidas. Al cliente solo viaja la cantidad total reservada, nunca sus cartas.
- `RulesValidator.ValidateMeld` comprueba la estructura del juego. `HasAlternatingSetOrder` comprueba por separado el orden de colores de tercias y cuartas. Esto permite mostrar el juego desordenado durante «Pasa» sin rechazar una réplica válida.
- `GameManager` abre `AwaitingOrderChallenge`, guarda la fase pendiente y solo resuelve el resultado lógico. `ServerSession.Tick` controla los **2.000 ms** con tiempo monotónico del host. Las operaciones de juego quedan suspendidas durante esa oportunidad; la red y las desconexiones siguen activas.
- Una pulsación válida de otro jugador habilitado elimina al infractor y acomoda sus juegos. Sin pulsación, los acomoda sin sanción. Se conservan mano, juegos y pagos automáticos; el eliminado no puede ganar. Una llegada al servidor a los 2.000 ms o después ya es tardía. El cliente no puede extender ni expirar el plazo.
- Si hay diez cartas combinadas con orden incorrecto, primero se resuelve «Pasa». Si nadie sanciona, la victoria se adjudica automáticamente al acomodar. Una escalera fuera de orden mantiene la penalización inmediata acordada anteriormente: este plazo se aplica al color de tercias/cuartas.
- Desconexiones durante «Pasa» se resuelven al reanudar: pago automático, salto de turno o declaración inicial pendiente, incluso cuando el ausente no es el infractor.

### Reglas iniciales confirmadas

| Declaración | Disponibilidad visual | Validación autoritativa |
| --- | --- | --- |
| Desmoche inicial | Cuatro cartas contiguas y ordenadas A-2-3-4 o J-Q-K-A del mismo palo. Antes de confirmar el cambio propio y después de recibirlo. | Exactamente las cuatro cartas, propias, del mismo palo, en orden ascendente o descendente; As=1 en A234 y As=14 en JQKA. |
| Cuarta inicial | Se conserva la regla anterior: cuatro cartas iguales contiguas y con colores alternados, después del cambio; usa el botón Desmoche. | Cuatro rangos iguales y tratamiento de «Pasa» si un cliente declara un orden incorrecto. |
| Color | Después del cambio, nueve cartas rojas o nueve negras. | Las nueve cartas completas en orden no decreciente; se permite As bajo **o** alto. Rangos repetidos permitidos. Una declaración incorrecta elimina conforme a la regla de declaraciones falsas. |
| Peladilla | Después del cambio, cuando no hay ninguna tercia o escalera contigua y ordenada a simple vista. | No se puede formar ninguna tercia, cuarta ni escalera usando cualquier selección u orden de la mano actual. Una pareja o proyecto incompleto sí permite Peladilla. Un juego oculto por el orden elimina al declarar. |

Las declaraciones iniciales siguen siendo voluntarias. No se comprueba una posible carta futura en Peladilla. Se conserva la posibilidad anterior de completar una combinación inicial con la primera oferta, incluyendo esa carta. Color/Peladilla no usan la oferta. La victoria normal siempre requiere **exactamente diez combinadas y ninguna suelta**; no basta con tener diez cartas en la mano.

## 3. Dependencias

Se reutilizan dominio, `GameManager`, `ServerSession`, vistas pública/privada, `IGameSession`, Mesa, borradores, `LocalMatchController` y `BotBrain`. No hay otro motor de reglas para bots ni otra fuente de verdad en la UI.

Godot 4.7.2 .NET y el proyecto existente. Los recursos del menú Salón se conservan. No se necesita instalar complementos.

## 4. Implementación

| Rutas bajo `res://` | Cambio |
| --- | --- |
| `Scripts/Domain/GameState.cs`, `RulesValidator.cs` | Reserva de intercambio, cuatro cartas iniciales, Color ordenado, Peladilla actual y alternancia. |
| `Scripts/Application/GameManager.cs` | Victoria automática y máquina de estados de «Pasa». |
| `Scripts/Networking/Core/NetworkProtocol.cs`, `ServerSession.cs`, `StateReplication.cs` | Protocolo **3**, intención `SayPasa`, reloj y campos públicos explícitos. |
| `Scripts/Networking/Core/RoomCodeProtocol.cs` | Código de seis caracteres, formato y límites del descubrimiento. |
| `Scripts/Networking/LanRoomDiscovery.cs`, `NetworkManager.cs` | Descubrimiento UDP y posterior conexión ENet. |
| `Scripts/Presentation/Interaction/InitialHandPresentation.cs` | Detección visual de secuencias y juegos contiguos, sin modificar cartas. |
| `Scripts/Presentation/Mesa.InitialDeclarations.cs`, `Mesa.Dynamic.cs`, `Mesa.Interaction.cs`, `Mesa.cs` | Botones contextuales, mensajes y conservación de la carta preparada ante confirmaciones ajenas. |
| `Scripts/Presentation/Lobby.cs`, `Scenes/Lobby.tscn` | Campo y presentación del código. |
| `Scripts/Singleplayer/Core/BotBrain.cs`, `LocalMatchController.cs`, `Scripts/Singleplayer/LocalGameSession.cs` | Reglas compartidas, orden alternado, reacción a Pasa y reloj relativo al inicio local. |
| `Scenes/MenuPrincipal.tscn` | Ayuda actualizada. |

Los archivos completos están incluidos en el proyecto. Las pruebas están fuera de `res://`, en las tres carpetas `Desmoche.*Pruebas` hermanas.

### Transporte y privacidad

1. El host abre ENet en **UDP 7000** y anuncia un código aleatorio en **UDP 7001**. Se admite un host por dispositivo en este puerto de descubrimiento.
2. Buscar sala emite una consulta broadcast IPv4 acotada con código y nonce. Reintenta cada 500 ms hasta un máximo de cinco segundos. El host solo responde mientras esté en preparación. El cliente verifica código, versión y nonce, y usa la IP de origen del paquete para conectarse por ENet.
3. El saludo `Hello(version, name, roomCode)` comprueba la sala. El método directo `Join(IP)` permanece para diagnósticos; el formulario del jugador usa `JoinByCode`.
4. `RequestAction` transporta la nueva intención `SayPasa` sin ID de víctima ni tiempo del cliente. La autoridad ya conoce al infractor y al emisor. Conserva validación de sesión, solicitud, ventana, fase y jugador.
5. La réplica pública añade `ExchangeCount`, `OrderChallenge` (jugador, ID y milisegundos restantes) y `OpeningOffer`. La privada conserva mano propia y respuesta propia. No se publica el contenido de la reserva, el orden del mazo ni manos rivales durante el juego.

El código local identifica la sala; no es una contraseña ni un servicio de Internet. No requiere router con puertos públicos ni cuenta externa. El descubrimiento encuentra la dirección; la partida continúa usando el ENet nativo. Referencias oficiales: [PacketPeerUDP](https://docs.godotengine.org/en/stable/classes/class_packetpeerudp.html) y [multijugador de alto nivel](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html).

## 5. Configuración Godot

No crear nodos manualmente ni modificar el Inspector. Usar los scripts y escenas incluidos y mantener `Main.tscn` como escena principal.

```text
Main
├── NetworkManager                 # autoridad peer 1; ruta estable
├── LocalGameSession                # modo local, sin sockets
└── Margin/Screens
    ├── Lobby/Scroll/Center/Panel/Padding/Content
    │   ├── JoinPanel/IPInput       # LineEdit: nombre interno histórico, ahora código
    │   └── SessionPanel            # código, jugadores, inicio y salida
    └── Mesa/Scroll/Padding/Content
        ├── HandZone                # solo mano propia confirmada
        └── ContextActions          # creado por script, como antes
            ├── DeclareInitialButton  # «Desmoche», solo victoria inicial
            ├── ColorButton
            ├── PeladillaButton
            └── PasaButton
```

Las señales `Pressed` se conectan en C#. Ordenar por arrastre solo actualiza la presentación; declarar envía las identidades en su orden visible. Se ocultan el botón y la casilla de victoria normal. Los nodos antiguos bajo `Interaction/NetworkActions` permanecen ocultos para compatibilidad; no son los controles visibles. No hay nuevos RPC que permitan modificar manos directamente.

## 6. Integración

Abrir o actualizar el proyecto y compilar. Todos los jugadores LAN deben usar esta misma revisión de protocolo 3. En Crear sala se comparte el código; en Buscar sala se introducen nombre y código. Minúsculas, espacios y guiones se normalizan.

Los bots publican tercias/cuartas alternadas y conocen las declaraciones nuevas sin recibir información adicional. Su probabilidad de advertir una oportunidad de Pasa es una **política de dificultad PROVISIONAL**: Fácil 20 %, Medio 50 %, Difícil 80 % y Experto 100 %, una decisión por oportunidad. No cambia las reglas humanas. La nueva Peladilla puede terminar muchas partidas tras el cambio; no se ha ocultado ni alterado esa regla para alargarlas.

## 7. Prueba de aceptación

Desde la raíz que contiene las cuatro carpetas:

```powershell
dotnet build .\desmoche\Desmoche.csproj
dotnet run --project .\Desmoche.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Interaccion.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Singleplayer.Pruebas\Pruebas.csproj
```

Validación realizada el 12/10/2026: compilación **0 errores / 0 advertencias**; núcleo **2.178 comprobaciones**, más **1.056 escaleras exhaustivas**; interacción **465 comprobaciones**; bots **4.144 comprobaciones / 170 partidas**, incluidos escenarios controlados de victoria normal y agotamiento. Godot: menú **180**, Singleplayer **570**, Mesa headless **306** y Mesa con render/arrastre nativo **329** comprobaciones. Se verificó el lobby con cuatro procesos reales conectados mediante código, incluyendo cancelar/reintentar, reparto y cierre del host. Estas pruebas no sustituyen dos dispositivos físicos en la misma Wi-Fi.

Para repetir el lobby, indicar la ruta real al ejecutable .NET de Godot:

```powershell
.\Desmoche.Pruebas\RunLobbySmoke.ps1 -GodotPath "C:\ruta\Godot_mono_win64_console.exe" -Players 4
```

Aceptación manual en dos dispositivos:

1. Crear sala en uno y copiar su código en el otro; comprobar lista de 2–4 participantes e iniciar.
2. Confirmar una carta mientras otro jugador aún no confirma: la mano queda con ocho y el rival ve ocho. Al completarse el intercambio vuelven a nueve; la carta recibida viene de la izquierda.
3. Ordenar A234/JQKA del mismo palo antes y después del cambio: aparece Desmoche. Fuera de la oportunidad inicial no aparece ese botón.
4. En Color, ordenar de menor a mayor con todos los ases al principio o al final y declarar. Probar también un orden incorrecto en una partida de prueba: elimina al declarante.
5. Declarar Peladilla con pareja pero sin juego: gana. Separar/desordenar un juego real y declararla: pierde. Si el juego es visible, el botón se oculta.
6. Publicar una tercia rojo-rojo-negro: los demás ven Pasa dos segundos. Pulsarlo elimina al infractor; dejarlo vencer acomoda sin eliminar. Comprobar que sus cartas publicadas siguen admitiendo ampliaciones y pagos automáticos.
7. Completar diez cartas combinadas sin ninguna suelta: gana automáticamente, sin marcar ninguna casilla. Con nueve o con una carta suelta no gana.

## 8. Posibles errores

- **No encuentra sala:** comprobar código, misma LAN, sala aún abierta y misma revisión. Algunas redes de invitados/aulas aíslan dispositivos o bloquean broadcast. Permitir Godot en el firewall para la red privada utilizada; ENet usa UDP 7000 y descubrimiento UDP 7001. No desactivar globalmente la seguridad.
- **No puede crear sala:** otro host o servicio puede tener ocupado 7000/7001. Cerrar la sala anterior libera ambos.
- **Botón inicial ausente:** comprobar fase, nueve cartas, orden contiguo y que no se haya elegido Continuar/confirmado el cambio. Color/Peladilla solo se ofrecen después del cambio.
- **Recursos no importados:** esperar a que Godot termine la importación antes de ejecutar diagnósticos. No copiar cachés `.godot` de otra instalación.
- **Menos turnos contra bots:** Peladilla ahora permite ganar sin considerar cartas futuras. Los bots también pueden declarar esta victoria válida.

## 9. Estado de la fase

FASE COMPLETADA: SÍ — implementación y pruebas locales realizadas.

BLOQUEADORES: ninguno para compilar o probar localmente. Pendiente la aceptación en dos dispositivos físicos de la red del usuario.

SIGUIENTE FASE: la que indique el usuario.
