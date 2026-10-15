# Reparto de casino clásico y trébol distinguible

> Revisión posterior del 13/10/2026: [FluidezReparto.md](FluidezReparto.md) elimina las reactivaciones de cartas terminadas y el redibujado completo al cierre. Conserva el diseño y las duraciones de esta entrega; incorpora medición y pruebas actualizadas.

Fecha: 13 de octubre de 2026. Godot 4.7.2 .NET/C#.

## 1. Objetivo de la fase

Implementar la propuesta Casino clásico: nueve rondas de una carta por jugador, salida desde el mazo, vuelo suave y giro de las cartas propias al llegar. Mantener los dorsos ajenos y habilitar el cambio al terminar. Redibujar el trébol para distinguirlo de la pica en la mano, los juegos y las esquinas de las cartas.

## 2. Decisiones arquitectónicas

El reparto es presentación del snapshot confirmado por la autoridad. No extrae cartas, cambia manos ni reconstruye el barajado. La secuencia sigue `SeatIndex`, en el mismo sentido de los asientos del motor. Solo las entregas del destinatario contienen una identidad de su mano; las ajenas son dorsos anónimos sin ID.

Mesa inicia una sola animación por sesión y ventana de reparto. Las confirmaciones ajenas durante el intercambio se guardan para dibujar la última vista al terminar. Un cambio de mano propia, fase, sesión, participantes o conexión cancela el reparto inmediatamente y dibuja la vista real. La revancha crea otra ventana y vuelve a animarse. El host espectador recibe únicamente entregas de dorso.

Las cartas reales de la mano conservan su espacio mientras están ocultas. La capa de vuelos ignora el ratón; selección, arrastre y envío de jugadas esperan el final. Las posiciones se recalculan a partir del mazo y las zonas actuales, por lo que siguen la escala de Mesa al cambiar la ventana.

En la sesión nativa contra bots, la primera decisión espera cuatro segundos desde el inicio o reinicio. Las siguientes conservan su intervalo habitual. El controlador puro admite esa demora como opción, con valor predeterminado cero, para conservar las simulaciones sin presentación. No se añaden plazos de reglas al intercambio.

El trébol usa tres hojas circulares, entrantes entre ellas y un tallo estrecho. El mismo dibujo sirve para el centro y ambas esquinas, también en figuras. Los palos conservan su identidad y color.

## 3. Dependencias

Godot 4.7.2 .NET, `Godot.NET.Sdk/4.7.2`, escritorio `net8.0`. Verificado con SDK .NET 9.0.308 y runtime 8.0.31. Sin paquetes ni recursos nuevos para ejecutar el proyecto.

Se conserva el protocolo **5** de la entrega de revancha y temporizadores. Este cambio no añade comandos ni campos de red.

## 4. Implementación

| Ruta relativa al repositorio | Cambio |
| --- | --- |
| `desmoche/Scripts/Presentation/Interaction/InitialDealPresentation.cs` | Plan de nueve rondas, elegibilidad, duraciones y entregas sin identidades ajenas. |
| `desmoche/Scripts/Presentation/Mesa.Deal.cs` | Vuelos, giro propio, última vista pendiente, cancelación y limpieza. |
| `desmoche/Scripts/Presentation/Mesa.cs` | Inicio desde snapshot, enlace de la capa y limpieza al salir. |
| `desmoche/Scripts/Presentation/Mesa.Interaction.cs` | Actualización por cuadro y bloqueo de edición/envío durante el reparto. |
| `desmoche/Scripts/Presentation/CartaArtwork.cs` | Trébol de tres hojas y tallo estrecho en todos los tamaños. |
| `desmoche/Scripts/Singleplayer/LocalGameSession.cs` | Demora inicial de cuatro segundos para los bots nativos. |
| `desmoche/Scripts/Singleplayer/Core/LocalMatchController.cs` | Opción de demora inicial y su aplicación también en la revancha. |
| `Desmoche.Interaccion.Pruebas/InitialDealTests.cs`, `Program.cs`, `Pruebas.csproj` | 74 comprobaciones de secuencia, cantidades, privacidad y elegibilidad. |
| `Desmoche.Singleplayer.Pruebas/Program.cs` | Comprobación del primer bot antes y después de la demora. |
| `desmoche/Scripts/Diagnostics/DealAnimationSmoke.cs`, `desmoche/Scenes/Diagnostics/DealAnimationSmoke.tscn` | Diagnóstico nativo con dos, tres y cuatro jugadores; capturas opcionales. |
| `desmoche/Scripts/Diagnostics/SingleplayerSmoke.cs` | Espera el reparto antes de preparar el cambio real. |

Los scripts nuevos incluyen sus `.uid`. README y `CONTEXTO_PROYECTO.md` enlazan esta revisión. El parche acumulado conserva peladilla, trasmoche, revancha y plazos de cartas.

## 5. Configuración Godot

No requiere cambios de Inspector ni de escenas de producción. Mesa crea `Scroll/Padding/Content/DealAnimation` bajo su lienzo, con `MouseFilter.Ignore` y `ZIndex = 20`. Sus cartas temporales se llaman `Entrega0`, `Entrega1`, etc.; los nombres no contienen identidades. Al terminar o cancelar se retiran todos esos nodos.

La actualización ocurre desde `_Process`. No hay señales o RPC nuevos de partida; el snapshot sigue llegando mediante `IGameSession.ViewChanged`. Se conserva `/root/Main/NetworkManager`.

Duraciones en `InitialDealPresentation`: intervalo de salida **0,09 s**, vuelo **0,34 s**, giro propio **0,16 s**. Antes de los vuelos se esperan dos cuadros para colocar la mesa. El primer bot nativo espera **4.000 ms**, configurados en `LocalGameSession`.

| Participantes | Cartas entregadas | Duración visual aproximada |
| --- | --- | --- |
| 2 | 18 | 2,03 s |
| 3 | 27 | 2,84 s |
| 4 | 36 | 3,65 s |

Los dorsos temporales de los rivales desaparecen al terminar y se recupera su zona de juegos publicados. Su mano sigue representada por el contador existente.

## 6. Integración

1. Detén el juego y combina la carpeta `desmoche` del ZIP con la del proyecto, reemplazando los archivos incluidos.
2. Abre Godot .NET, espera la importación y recompila.
3. Inicia Singleplayer o una sala de dos a cuatro personas. Espera el reparto y selecciona la carta del cambio.
4. Repite con Volver a jugar y comprueba que la nueva partida vuelve a repartir.

Las carpetas de pruebas se colocan junto a `desmoche`, fuera de `res://`. Para LAN todos los equipos deben conservar la misma actualización del protocolo 5.

## 7. Prueba de aceptación

Comandos de consola ejecutados desde la raíz:

```bash
dotnet build ./desmoche/Desmoche.csproj
dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick
```

| Prueba | Resultado |
| --- | --- |
| Compilación final | 0 errores, 0 advertencias. |
| Núcleo, reglas, partida, red y réplica | 3.049 comprobaciones y 1.056 casos de escalera correctos. |
| Interacción | 582 comprobaciones correctas, incluidas 74 del reparto. |
| Singleplayer rápido final | 11.870 comprobaciones; 38 partidas: 33 Desmoche y 5 empates. |
| DealAnimationSmoke con render OpenGL | 34 comprobaciones correctas con dos, tres y cuatro jugadores. |
| SingleplayerSmoke final | 571 comprobaciones; doce configuraciones, partida completa, revancha y cierre. |
| MesaInteractionSmoke headless | 159 comprobaciones correctas. |
| MesaDynamicSmoke headless | 563 comprobaciones correctas. |

El diagnóstico del reparto comprueba nueve entregas por participante, ocultación propia durante el vuelo, privacidad de dorsos, cambio de tamaño durante el reparto, confirmación ajena sin reinicio, aplicación de la vista más reciente, una sola animación por partida, revancha y cancelación al salir o cambiar la mano propia. El plan puro incluye el host espectador.

Se generaron e inspeccionaron capturas reales de Godot en una pantalla virtual de Linux usando OpenGL/Mesa: reparto en curso a 1600 × 900, final a 1100 × 820 y comparación de trébol/pica en Ases y figuras a 180 × 220, 90 × 112 y 50,3 × 72. No se simuló el ratón físico ni se repitió una prueba entre dispositivos Wi-Fi. El protocolo no cambió en esta fase.

Para repetir el diagnóstico, abre `res://Scenes/Diagnostics/DealAnimationSmoke.tscn` con F6 y espera `DEAL ANIMATION PASS`. Por línea de comandos:

```bash
godot --path ./desmoche res://Scenes/Diagnostics/DealAnimationSmoke.tscn -- --auto
```

Añade `--capture=/ruta/absoluta` después de `--auto` para capturas, con render de pantalla disponible. Sin capturas admite `--headless`.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| No puedes seleccionar durante los primeros segundos | El cambio se habilita después de repartir las nueve cartas. |
| No se reproduce al entrar a una partida ya avanzada | Solo se anima un reparto inicial confirmado; no una mano recibida a mitad de partida. |
| El reparto termina antes al desconectarse o acabar la partida | La vista autoritativa nueva cancela las entregas para no mostrar una mano antigua. |
| Se reparten dorsos al host sin mano | El host que no confirmó revancha observa la partida; no recibe cartas propias. |
| El trébol continúa con el dibujo anterior | Reemplazar `CartaArtwork.cs` y recompilar los scripts C#. |
| Error al capturar en modo headless | Las capturas necesitan render real; omitir `--capture` para controles headless. |
| Program duplicado al compilar | Mantener las consolas de pruebas fuera de `desmoche`. |

## 9. Estado de la fase

Reparto clásico y nuevo trébol implementados y verificados. Las pruebas conservan los arreglos anteriores. Queda la aceptación del movimiento y los controles con el ratón en los dispositivos del usuario.

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar el parche.

SIGUIENTE FASE: la que indique el usuario.
