# Corrección de las pausas al final del reparto

Fecha: 13 de octubre de 2026. Godot 4.7.2 .NET/C#.

## 1. Objetivo de la fase

Analizar las dos grabaciones del usuario, localizar trabajo que pueda causar pequeñas pausas al terminar el reparto clásico y corregirlo conservando movimiento, duración, cartas y reglas.

## 2. Decisiones arquitectónicas

Se encontraron dos costes de presentación: cada carta propia ya entregada volvía a hacerse visible y ocultarse en todos los cuadros siguientes; al finalizar se reconstruía la mesa completa, incluidas las nueve cartas propias, sus fuentes y controles. También se buscaban repetidamente los mismos nodos durante cada vuelo.

Cada entrega ahora conserva sus referencias y termina una sola vez. Las cartas que llegaron permanecen quietas bajo el mismo lienzo escalado; solo las que están en vuelo o giro necesitan actualización. El cierre conserva las cartas y paneles existentes y aplica la revisión, el mensaje y los contadores confirmados.

La vista pendiente solo se conserva si sigue siendo un reparto compatible, con la misma mano y participantes. Cambiar conexión, eliminación, asiento u otro contexto invalida esas referencias y representa la vista nueva mediante el recorrido normal. La autoridad sigue decidiendo el estado real.

Las grabaciones son de 30 cuadros por segundo y no contienen medidas de CPU/GPU ni del perfilador del juego. Sirven para examinar el síntoma, pero no adjudican por sí solas una causa al hardware. La causa de código se reprodujo y midió en un diagnóstico aislado.

## 3. Dependencias

Se conservan Godot 4.7.2 .NET, `Godot.NET.Sdk/4.7.2`, escritorio `net8.0` y protocolo **5**. Verificación con SDK .NET 9.0.308 y runtime 8.0.31. Sin paquetes nuevos para el juego.

## 4. Implementación

| Ruta relativa al repositorio | Cambio |
| --- | --- |
| `desmoche/Scripts/Presentation/Mesa.Deal.cs` | Referencias estables por entrega, estados de inicio/final, cierre sin reconstrucción y compatibilidad de vistas pendientes. |
| `desmoche/Scripts/Presentation/Mesa.cs` | Registro de las etiquetas públicas de cantidad para actualizar sus textos al terminar. |
| `desmoche/Scripts/Diagnostics/DealAnimationSmoke.cs` | Comprueba que los mismos nueve nodos propios sobreviven al cierre. |
| `desmoche/Scripts/Diagnostics/DealPerformanceSmoke.cs` y su `.uid` | Medición aislada de CPU/asignaciones de Mesa y detección de reactivaciones/reconstrucción. |
| `desmoche/Scenes/Diagnostics/DealPerformanceSmoke.tscn` | Mesa de diagnóstico con dos, tres y cuatro jugadores, con y sin confirmación pendiente. |
| `README.md`, `CONTEXTO_PROYECTO.md`, `RepartoClasicoYTrebol.md` | Enlaces y estado actualizado. |

El ZIP acumula las correcciones anteriores. Los vídeos y capturas de análisis no forman parte del parche.

## 5. Configuración Godot

Sin cambios de Inspector, RPC, señales de partida ni escenas de producción. Se conserva `/root/Main/NetworkManager` y la capa `Mesa/Scroll/Padding/Content/DealAnimation`.

Las entregas temporales siguen llamándose `EntregaN`, con dorsos ajenos anónimos. Las referencias almacenadas se usan durante ese reparto y se descartan al cancelar. Las etiquetas de cantidad se registran al dibujar los paneles públicos; el registro se renueva en cada reconstrucción normal y se limpia al salir.

El diagnóstico de rendimiento hereda `Mesa.tscn`; su script deriva de Mesa y mide su llamada a `_Process`. No se carga en la partida normal. Sus señales de visibilidad se observan únicamente para contar reactivaciones. Los registros incluyen cantidades y tiempos, sin manos rivales ni orden del mazo.

## 6. Integración

1. Detén el juego y combina `desmoche` del ZIP con tu carpeta existente, reemplazando los archivos incluidos.
2. Abre Godot .NET, espera la importación y recompila.
3. Repite una partida de tres o cuatro participantes y una revancha. El diseño, vuelo, giro y tiempos del reparto se conservan.

Las pruebas de consola se mantienen junto a `desmoche`, fuera de `res://`. Se conserva la actualización de protocolo 5 en todos los equipos LAN.

## 7. Prueba de aceptación

La comparación usa el mismo diagnóstico aislado en Linux, con Godot headless limitado a 60 FPS. Mide el tiempo de CPU de `Mesa._Process`, incluido el cierre, y asignaciones administradas del hilo. No mide FPS finales, GPU ni el rendimiento de la PC del usuario. Los resultados son una ejecución antes y otra después del ajuste.

| Participantes / vista pendiente | Cierre antes (ms) | Cierre después (ms) | Reactivaciones después |
| --- | --- | --- | --- |
| 2 / confirmación ajena | 52,26 | 7,22 | 0 |
| 3 / confirmación ajena | 54,32 | 5,21 | 0 |
| 4 / confirmación ajena | 54,69 | 4,90 | 0 |
| 4 / sin actualización | 51,85 | 4,32 | 0 |

Con cuatro participantes y confirmación pendiente, los últimos 0,65 segundos pasan de una media de **1,73 a 0,15 ms por actualización**, y de **20.705 a 912 bytes asignados por actualización**. El cierre pasa de **261.560 a 63.888 bytes**. Antes se observaron 778 reactivaciones redundantes y nueve cartas reconstruidas; después, cero reactivaciones y los mismos nueve nodos. Estos valores describen el trabajo del código medido, no un compromiso de FPS para otros equipos.

| Control ejecutado | Resultado |
| --- | --- |
| Compilación final | 0 errores, 0 advertencias. |
| Núcleo | 3.049 comprobaciones y 1.056 casos de escalera. |
| Interacción | 582 comprobaciones. |
| Singleplayer rápido | 11.870 comprobaciones y 38 partidas completas. |
| DealPerformanceSmoke con `--assert-smooth` | Cuatro casos correctos; cero reactivaciones y mano conservada. |
| DealAnimationSmoke con render OpenGL | 37 comprobaciones; reparto, última vista, redimensionado, privacidad, revancha y salida. |
| MesaInteractionSmoke headless | 159 comprobaciones. |
| SingleplayerSmoke headless | 571 comprobaciones; doce configuraciones, partida completa y revancha. |
| Importación y `git diff --check` | Correctos. |

Se inspeccionó la captura final de Godot con render OpenGL/Mesa en pantalla virtual, después de cambiar la ventana de 1600 × 900 a 1100 × 820. Se conservan posiciones y el contador pendiente de ocho cartas del rival. No se repitió una prueba entre dispositivos Wi-Fi ni se midió el ratón físico.

Comandos desde la raíz del repositorio:

```bash
dotnet build ./desmoche/Desmoche.csproj
dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick
godot --headless --max-fps 60 --path ./desmoche res://Scenes/Diagnostics/DealPerformanceSmoke.tscn -- --auto --assert-smooth
```

También puedes abrir `DealPerformanceSmoke.tscn` con F6. Para exigir las condiciones de cierre por línea de comandos, utiliza `--assert-smooth`. Las cifras variarán con el equipo y el modo de ejecución.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| Continúan las cifras antiguas o `PreservedHand=false` | Confirmar que se reemplazaron ambos archivos de Mesa y se recompiló. |
| El diagnóstico informa reactivaciones | Debe usarse el script actualizado de `Mesa.Deal.cs`; las cartas terminadas no vuelven a cambiar de visibilidad. |
| Se cancela al desconectarse o ser eliminado | Se representa inmediatamente el contexto autoritativo nuevo; no se conservan referencias antiguas. |
| Sigue una pausa pese a este parche | Comparar con y sin grabación y consultar el perfilador de Godot para separar CPU de render, GPU y carga de otras aplicaciones. No cambiar el intervalo del reparto para ocultar el problema. |
| Program duplicado | Mantener las consolas fuera de `desmoche`. |

## 9. Estado de la fase

Causa de presentación reproducida, ajuste aplicado y mejora medida. Se conserva el reparto clásico y los controles pasan sus regresiones. La evaluación del resultado en la PC del usuario requiere ejecutar este parche allí.

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar la corrección.

SIGUIENTE FASE: la que indique el usuario.
