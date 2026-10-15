# Victorias visibles, Pasa y pagos — 12/10/2026

> Corrección posterior: [Peladilla.md](Peladilla.md) reemplaza la validación de Peladilla descrita aquí. Parejas y proyectos con una carta futura ya no permiten ganar. Los resultados de simulación de esta entrega son históricos; se mantienen la evidencia visual, Pasa y la exclusión del pagador.

Esta revisión sustituye el plazo de dos segundos y la prioridad provisional que permitía al pagador reclamar su propio pago. Conserva todas las condiciones de victoria inicial, según la confirmación del usuario.

## 1. Objetivo de la fase

Mostrar las cartas que justifican una victoria inicial en la zona del ganador; colocar Pasa junto al juego incorrecto durante tres segundos; excluir al pagador de la oferta que acaba de producir; comprobar las victorias tempranas de los bots.

## 2. Decisiones arquitectónicas

El motor guarda el tipo y el orden de las cartas de la declaración válida. La sesión solo publica esa evidencia cuando el resultado es `InitialVictory`. Mesa la dibuja en el lugar de los juegos del ganador: cuatro cartas para A234/JQKA/cuarta, nueve para Color/Peladilla. Color y Peladilla no se convierten en combinaciones de dominio ni se mueven artificialmente de la mano: la evidencia visual referencia cartas ya autorizadas al finalizar, sin duplicarlas en el estado ni en el conteo de 52.

El plazo de Pasa es **3.000 ms**, medido por la autoridad con reloj monotónico. En el límite exacto la ventana ya venció. La UI coloca un único botón junto al primer grupo incorrecto del jugador cuestionado; si otro juego ocupa el lado derecho, usa el izquierdo, y si ninguno cabe lo coloca inmediatamente debajo para no tapar cartas. La posición sigue los límites de las cartas visibles, incluso al redimensionar. La acción sanciona al jugador y ordena sus juegos como antes.

Una oferta de pago excluye al pagador tanto de respuestas humanas como de reclamos automáticos. Solo los demás pueden reclamar o pasar; no se espera una respuesta del pagador. Si nadie reclama, sigue el jugador habilitado a su derecha. Las ofertas del mazo conservan sus reglas anteriores.

## 3. Dependencias

Godot 4.7.2 .NET, motor y sesiones existentes, Mesa basada en `IGameSession`. Las pruebas de consola están en carpetas hermanas del juego. El protocolo pasa de 3 a **4**, porque añade evidencia de victoria; todos los dispositivos de la sala deben actualizarse.

APIs visuales verificadas: [Control.GetGlobalRect](https://docs.godotengine.org/en/stable/classes/class_control.html#class-control-method-get-global-rect) y [CanvasItem.GetGlobalTransform](https://docs.godotengine.org/en/stable/classes/class_canvasitem.html#class-canvasitem-method-get-global-transform).

## 4. Implementación

- `res://Scripts/Application/GameManager.cs`: evidencia inicial, exclusión del pagador y prioridades de oferta.
- `res://Scripts/Networking/Core/NetworkProtocol.cs`: `InitialVictoryDto(Kind, Cards)`, campo opcional de transporte y versión 4.
- `res://Scripts/Networking/Core/StateReplication.cs`: proyección explícita, validación de dueño/resultado/cantidad y copias defensivas. El campo solo es admisible con victoria inicial y es obligatorio en ese resultado.
- `res://Scripts/Networking/Core/ServerSession.cs`: evidencia por destinatario y plazo de Pasa.
- `res://Scripts/Presentation/Mesa.cs`: cartas y nombre de victoria en la zona correspondiente y en el resultado.
- `res://Scripts/Presentation/Mesa.Dynamic.cs`, `Mesa.Casino.cs`, `Mesa.Interaction.cs`: botón junto a cartas, seguimiento de su posición y envío de la intención existente.
- `res://Scripts/Diagnostics/MesaDynamicSmoke.cs`: validaciones de cartas, posiciones, controles y privacidad en tres resoluciones.
- `Desmoche.Pruebas`: casos de victoria, copia defensiva, pagos de 2–4 jugadores, límites del plazo y regresiones.
- `Desmoche.Singleplayer.Pruebas/Program.cs`: desglose de resultados e inspección independiente de todas las ternas de cada Peladilla ganadora.

## 5. Configuración Godot

No hay que crear nodos ni cambiar el Inspector. Los nuevos elementos se crean al representar la vista:

```text
Mesa/Scroll/Padding/Content
├── PasaButton                 Button, fuera de ContextActions
├── ContextActions            Acciones habituales; ya no contiene PasaButton
└── Table/Padding/Surface
    ├── OpponentGames/JuegosJugador<ID>/Rows/VictoriaInicial
    │   └── Carta0…CartaN     Evidencia del rival ganador
    └── MyGame/Rows/VictoriaInicial
        └── Carta0…CartaN     Evidencia del ganador local
```

`PasaButton.Pressed` conserva el envío de `ActionKind.SayPasa`. La autoridad sigue resolviendo tiempo, identidad, fase y penalización. Se conserva el ciclo `_Process` existente de Mesa para seguir la posición y comprobar respuestas pendientes; no se introduce otro motor de reglas en UI ni un RPC nuevo.

## 6. Integración y revisión de bots

LAN y Singleplayer comparten el cambio. El pagador recibe `OwnResponseSubmitted=true` mientras se ofrece su pago; los controles Tomar/Rechazar y las decisiones del bot ya usan ese estado para no responder. El servidor rechaza igualmente una solicitud manual de retomar o rechazar el propio pago.

El usuario eligió **mantener todas las victorias iniciales**, sin opción de desactivarlas y sin reducir artificialmente la capacidad de los bots para reconocerlas. La regla vigente de Peladilla permite ganar cuando no hay ningún juego formado entre las nueve cartas, aunque existan parejas o proyectos. Esto explica la alta frecuencia de finales tempranos y afecta a todas las dificultades.

En las 170 ejecuciones verificadas hubo 160 Peladillas, cuatro A234, tres cuartas, un Desmoche normal y dos empates. El conjunto incluye repeticiones, revanchas y escenarios controlados; no es una estimación estadística de probabilidades de reparto. Las Peladillas ganadoras se comprobaron con una búsqueda independiente de todas las ternas: ninguna ocultaba una tercia o escalera. Una partida completa adicional mediante la interfaz terminó por Desmoche normal. No se cambiaron reparto, dificultad ni reglas para producir resultados distintos.

## 7. Prueba de aceptación

Verificación realizada con SDK 9.0.308:

- Compilación: 0 errores y 0 advertencias.
- Núcleo: 357 comprobaciones de revisión, 85 de reglas más 1.056 escaleras exhaustivas, 574 de partida, 207 de conexiones, 349 de red, 629 de réplica y 61 de dominio.
- Interacción: 465 comprobaciones.
- Bots: 4.440 comprobaciones, 170 ejecuciones completas.
- Godot Singleplayer: 570 comprobaciones, doce configuraciones y partida completa.
- Godot Mesa con render y arrastre: 586 comprobaciones, resoluciones 2016×780, 1200×900 y 976×874.
- Lobby: cuatro procesos Godot por código LAN. No sustituye una prueba en dos dispositivos físicos.

Desde `C:\Users\ricar\Documents`:

```powershell
dotnet build .\desmoche\Desmoche.csproj
dotnet run --project .\Desmoche.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Interaccion.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Singleplayer.Pruebas\Pruebas.csproj
```

Prueba manual:

1. Ganar antes del primer robo: comprobar nombre del ganador, tipo de victoria y cuatro o nueve cartas en su zona de mesa. Color conserva el orden declarado; Peladilla muestra toda la mano que la justifica.
2. Publicar una tercia rojo-rojo-negro: otro jugador debe ver Pasa pegado a ese grupo. Pulsarlo antes de tres segundos sanciona; dejar vencer acomoda sin sanción. El infractor no ve su propio botón.
3. Reclamar y pagar: después de confirmar el pago desaparecen las opciones de tomar/rechazar para quien pagó; los demás conservan su respuesta. La ronda no espera al pagador.
4. Repetir con bots y comprobar la razón de cada victoria inicial. Siguen siendo frecuentes las Peladillas válidas conforme a la regla elegida.
5. Iniciar otra partida: no deben quedar cartas ni rótulos de la victoria anterior.

## 8. Posibles errores

- **Cliente de versión anterior:** actualizar todos los dispositivos a protocolo 4 y volver a crear la sala.
- **Bot gana antes del primer robo:** revisar la evidencia y el nombre de la victoria; este resultado puede ser válido. La regla de Peladilla no exige descartar parejas ni proyectos incompletos.
- **Pasa al otro lado o debajo del grupo:** es la disposición prevista cuando no cabe en el lado derecho sin tapar otro juego.
- **Cambios no visibles:** detener la partida que estuviera abierta, compilar y ejecutar de nuevo con F5.

## 9. Estado de la fase

FASE COMPLETADA: SÍ, implementación y verificación local.

BLOQUEADORES: ninguno local. Prueba entre dispositivos físicos pendiente de aceptación del usuario.

SIGUIENTE FASE: la que indique el usuario.
