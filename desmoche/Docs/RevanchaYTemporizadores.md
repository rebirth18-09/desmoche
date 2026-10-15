# Revancha multijugador y plazos de cartas

Corrección posterior: [ManosFinalesEnEmpate.md](ManosFinalesEnEmpate.md) habilita Mostrar juego también en empate. Su autorización de manos en todo Finished y protocolo 7 sustituyen las restricciones/versiones anteriores de esta documentación.

Ampliación posterior: [MazoFinalYOfertaObligatoria.md](MazoFinalYOfertaObligatoria.md) incorpora el mazo final y la denuncia por oferta sin jugar. Su protocolo 6 y obligación sobre juegos publicados sustituyen las referencias anteriores correspondientes.

Fecha: 13 de octubre de 2026. Godot 4.7.2 .NET/C#.

## 1. Objetivo de la fase

Añadir Volver a jugar en LAN con quince segundos para confirmar y contador compartido; limitar la elección de cartas a cinco segundos y dar veinte desde la elección para preparar el juego. Al vencer, bajar aleatoriamente un uso válido. Conservar peladilla, trasmoche, prioridad, pagos, Pasa y victorias.

## 2. Decisiones arquitectónicas

Los relojes pertenecen a `ServerSession`. `NetworkManager` aporta tiempo monotónico del host; Singleplayer usa la misma autoridad. Los clientes muestran tiempo restante y no pueden enviar un reloj ni vencer ventanas por RPC. Se conservan las fases existentes.

Elegir registra interés, sin añadir cartas a la mano ni modificar juegos. Cada reserva vence veinte segundos después de aceptarla el host. Los interesados preparan simultáneamente; confirmar primero no cambia la prioridad de asiento. No elegir durante cinco segundos implica pasar, aunque se pudiera formar juego.

La autoridad comprueba los usos al elegir y los recalcula al vencer. Si no existe ninguno, registra rechazo sin sanción: la carta queda para otros interesados o para el pozo. La búsqueda utiliza mano y juegos propios, incluyendo tercias, cuartas, escaleras, ampliaciones y trasmoche con origen válido de al menos tres cartas anteriores. No consulta mazo oculto ni manos rivales. El host elige al azar entre resultados completos validados. Tras el juego automático, el humano sigue pagando normalmente salvo victoria o mano vacía según las reglas anteriores.

El pozo antiguo sigue siendo del turno actual: cinco segundos para elegir y veinte para preparar. Vencer su elección muestra la siguiente carta del mazo. Con mazo vacío, los siguientes habilitados tienen oportunidad; rechazar esa misma carta termina en empate sin ciclo infinito. La segunda extracción obligatoria de apertura no habilita el pozo.

Al finalizar en LAN, pueden apuntarse también los eliminados. Al terminar quince segundos se inicia otra partida con los confirmados si son al menos dos. Los invitados que no confirmaron salen. Desconectarse retira su confirmación y actualiza el denominador. El host que no confirma conserva el servidor como espectador, sin asiento ni mano; no se migra autoridad. Con menos de dos confirmados la partida permanece finalizada. El botón local contra bots conserva su reinicio inmediato.

## 3. Dependencias

Godot 4.7.2 .NET, `Godot.NET.Sdk/4.7.2` y escritorio `net8.0`, sin paquetes nuevos. Verificado con SDK .NET 9.0.308 y runtime 8.0.31.

El protocolo cambia de 4 a **5** por los comandos y snapshots nuevos. Todos los equipos de la sala deben instalar esta actualización; el descubrimiento por código usa la misma versión.

## 4. Implementación

| Ruta relativa al repositorio | Cambio |
| --- | --- |
| `desmoche/Scripts/Application/GameManager.cs` | Elegibilidad, pozo del turno, selección de participantes y rotación de inicio. |
| `desmoche/Scripts/Application/GameManager.TimedCards.cs` | Elección/rechazo del pozo y búsqueda de usos válidos con trasmoche. |
| `desmoche/Scripts/Networking/Core/ServerSession.cs`, `ServerSession.Timing.cs` | Plazos 5/20/15, reservas, confirmaciones, vencimientos y vista del espectador. |
| `desmoche/Scripts/Networking/Core/NetworkProtocol.cs`, `StateReplication.cs` | Protocolo 5, comandos `ChooseOffer`, `ChooseDiscard`, `JoinRematch`, DTO, validación y copias. |
| `desmoche/Scripts/Networking/NetworkManager.cs` | Revancha LAN y notificación al invitado excluido antes de cerrar su peer. |
| `desmoche/Scripts/Presentation/Mesa.cs`, `Mesa.Interaction.cs`, `Mesa.Dynamic.cs`, `Mesa.Countdowns.cs` | Botones, relojes, confirmaciones y conservación de borradores al elegir. |
| `desmoche/Scripts/Presentation/Interaction/InteractionActions.cs`, `HandInteractionState.cs` | Controles según reserva/turno y exclusión del pozo en extracción obligatoria. |
| `desmoche/Scripts/Singleplayer/Core/LocalMatchController.cs` | Vencimientos sin reconstruir Mesa solo porque cambió el tiempo restante. |
| `Desmoche.Pruebas/TimedMatchTests.cs`, `Program.cs`, `RuleRevisionTests.cs`, `StateReplicationTests.cs` | Nueva suite y regresiones de plazos, prioridad, azar, privacidad y snapshots. |
| `desmoche/Scripts/Diagnostics/MesaInteractionSmoke.cs` | Prueba botones, Enter, borradores, relojes y contador. |
| `desmoche/Scripts/Diagnostics/TimedRoomSmoke.cs`, `desmoche/Scenes/Diagnostics/TimedRoomSmoke.tscn` | Revancha/exclusión con procesos ENet reales. |
| `Desmoche.Pruebas/RunTimedRoom.ps1` | Lanzador del diagnóstico para Windows. |

Los scripts nuevos incluyen sus `.uid`. README, `CONTEXTO_PROYECTO.md` y este documento registran los cambios. El ZIP conserva también los arreglos anteriores de peladilla y trasmoche.

## 5. Configuración Godot

No requiere cambios manuales del Inspector ni de escenas de producción. Se conserva `/root/Main/NetworkManager`.

Mesa crea `RematchButton` y `RematchCountdownLabel` en `TopBar`; añade `ChooseOfferButton`, `ChooseDiscardButton` y `CardCountdownLabel` en `ContextActions`. Click/Enter sobre la carta pública solicita su elección. Su arrastre se habilita después de elegir. Confirmar envía el resultado completo.

El RPC nuevo `RematchExcluded` solo acepta autoridad, es fiable por canal 0 y notifica la exclusión. Las jugadas siguen usando `RequestAction`, con solicitante autenticado. No hay RPC de expiración.

Duraciones centralizadas en `ServerSession.Timing.cs`: `CardChoiceMilliseconds = 5000`, `CardPreparationMilliseconds = 20000`, `RematchMilliseconds = 15000`. No hay ajustes de Inspector.

## 6. Integración

1. Detén el juego, conserva una copia y combina la carpeta `desmoche` del ZIP con tu proyecto, reemplazando los archivos incluidos.
2. Instala los mismos archivos en todos los equipos.
3. Abre Godot .NET, espera la importación y recompila.
4. Las carpetas de pruebas van junto a `desmoche`, fuera de `res://`.

Para una oferta, pulsa **Quiero esta carta**, o click/Enter en ella, durante cinco segundos. Si tiene uso válido aparece **Armar juego: 20 s**. Prepara cartas/trasmoche y pulsa **Confirmar juego con oferta**. Para el pozo del turno, usa **Quiero el pozo** y **Confirmar juego con pozo**. Los veinte segundos empiezan desde tu elección, no desde que termina la ventana de cinco. No elegir implica rechazar; elegir sin uso válido no incorpora la carta a mano.

Al finalizar en LAN, pulsa **Volver a jugar** antes de quince segundos. Todos ven las confirmaciones, por ejemplo `1/4`, `2/4`, `3/4`, `4/4`. Al terminar el plazo la partida nueva comienza automáticamente. Singleplayer conserva **Otra partida** sin votación.

## 7. Prueba de aceptación

Comandos ejecutados desde la raíz:

```bash
dotnet build ./desmoche/Desmoche.csproj
dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick
```

| Prueba | Resultado |
| --- | --- |
| Compilación | 0 errores, 0 advertencias. |
| Núcleo, reglas, partida, red y réplica | 3.049 comprobaciones y 1.056 casos de escalera correctos; incluye 513 de plazos/revancha. |
| Interacción | 508 comprobaciones correctas. |
| Singleplayer rápido | 11.868 comprobaciones; 38 partidas: 33 Desmoche y 5 empates. |
| Importación Godot 4.7.2 .NET | Correcta. |
| MesaInteractionSmoke headless | 159 comprobaciones correctas. |
| MesaDynamicSmoke headless | 563 comprobaciones correctas en tres resoluciones. |
| ENet, tres procesos | Host no confirma y aloja como espectador; dos invitados reciben otra partida. |
| ENet, tres procesos | Invitado no confirma, recibe exclusión y los otros dos reinician. |
| ENet, dos procesos | Ambos confirman y reciben otra partida al terminar quince segundos. |

El reloj simulado comprueba 4.999/5.000 y 19.999/20.000 ms. Repetir elección no renueva el plazo. Incluye rechazo sin juego, prioridad frente a confirmación temprana, confirmación después de cinco segundos con reserva, publicación después de pasar sin bloqueo, trasmoche automático de Ases conservando 2–3–4, mazo vacío, desconexiones y host espectador. Varias semillas producen juegos distintos y válidos.

Las pruebas ENet usan procesos Godot separados por localhost y el plazo real de quince segundos. Verifican transporte y réplica; no equivalen a dispositivos Wi-Fi físicos. Los diagnósticos UI headless tampoco verifican el ratón del sistema. Ambos quedan para aceptación manual del usuario.

Para repetir la revancha en Windows:

```powershell
cd .\Desmoche.Pruebas
.\RunTimedRoom.ps1 -GodotPath "C:\ruta\Godot_v4.7.2-stable_mono_win64_console.exe" -Players 3 -Scenario GuestTimeout
.\RunTimedRoom.ps1 -GodotPath "C:\ruta\Godot_v4.7.2-stable_mono_win64_console.exe" -Players 3 -Scenario HostSpectator
.\RunTimedRoom.ps1 -GodotPath "C:\ruta\Godot_v4.7.2-stable_mono_win64_console.exe" -Players 2 -Scenario AllReturn
```

El script PowerShell se entrega para Windows; aquí se verificó el mismo diagnóstico con lanzador Python en Linux. Provoca declaraciones inválidas deliberadas para terminar la primera partida y comprobar que también los eliminados pueden volver a jugar.

Aceptación manual: comprobar los relojes en dos dispositivos, apuntarse solo algunos jugadores, confirmar un juego antes del límite, dejar vencer otro con varios usos, elegir una carta inútil y repetir con pozo/trasmoche.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| Versión incompatible o código de sala no encontrado | Actualizar todos los equipos a protocolo 5. |
| Confirmar no aparece al ver una carta | Elegirla durante cinco segundos con Quiero esta carta/Quiero el pozo. |
| La carta se libera al elegir | No existe un uso válido con mano y juegos propios. No hay sanción por esa elección. |
| Se baja otro juego al vencer | La elección es aleatoria; confirmar antes permite elegir tu combinación. |
| No comienza otra partida | Deben confirmar al menos dos conectados antes del plazo. |
| Host observa sin cartas | No confirmó y conserva el servidor sin participar en el reparto. |
| Sala termina al salir el host | Se conserva la política sin migración de autoridad. |
| Program duplicado al compilar | Mantener pruebas fuera de `desmoche`. |

## 9. Estado de la fase

Implementado y verificado mediante compilación, consolas, controles Godot y ENet entre procesos. Peladilla y trasmoche pasan sus regresiones. Pendiente aceptación manual con ratón y dispositivos Wi-Fi.

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar el parche; aceptación manual en dispositivos del usuario pendiente.

SIGUIENTE FASE: la que indique el usuario.
