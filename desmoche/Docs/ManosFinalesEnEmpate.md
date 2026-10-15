# Mostrar juego también al terminar en empate

## 1. Objetivo de la fase

Corregir la ausencia de **Mostrar juego** en partidas terminadas en empate, conservando **Mostrar mazo**, la ventana translúcida y todos los juegos publicados.

## 2. Decisiones arquitectónicas

La captura del usuario termina con `MatchResult.Draw` y mazo agotado. La versión anterior habilitaba las manos finales únicamente cuando existía ganador: `Mesa.FinalHandsAllowed`, `GameManager.GetViewFor` y `ServerSession.ViewFor` utilizaban esa condición, y `StateProjection` rechazaba manos reveladas en empate. Por eso el mazo era consultable y los botones de manos no aparecían.

La autorización corresponde ahora a **toda partida terminada** (`Finished`, victoria o empate). Solo la autoridad entrega `RevealedHands`; no se inventan cartas en la interfaz. Durante preparación, partida activa o una desconexión que no finaliza la partida, se mantienen ocultas las manos ajenas. La revancha elimina la revelación anterior. No cambia la condición de victoria, el empate, la regla de Pasa ni el flujo del mazo.

El protocolo pasa de **6 a 7**: aunque los campos se conservan, el validador anterior rechazaba la nueva revelación en empate. La versión evita mezclar esos clientes con la autoridad actual. No se añaden RPC.

## 3. Dependencias

Godot 4.7.2 .NET, clases y escenas existentes. `GameManager`, `ServerSession`, `StateProjection`, `ClientStateAssembler`, `IGameSession`, `Mesa` y `Carta`. Sin paquetes, recursos gráficos ni APIs nuevas.

## 4. Implementación

| Ruta desde la raíz | Cambio |
| --- | --- |
| `desmoche/Scripts/Presentation/Mesa.FinalHands.cs` | Habilitar consulta final en victoria y empate. |
| `desmoche/Scripts/Application/GameManager.cs` | Revelar manos desde la vista del motor solo en `Finished`. |
| `desmoche/Scripts/Networking/Core/ServerSession.cs` | Entregar esas manos a los destinatarios al terminar. |
| `desmoche/Scripts/Networking/Core/StateReplication.cs` | Exigir revelaciones completas en todos los finales; rechazarlas mientras se juega. |
| `desmoche/Scripts/Networking/Core/NetworkProtocol.cs` | Versión 7 por compatibilidad de validación. |
| `Desmoche.Pruebas/NetworkTests.cs`, `GameManagerTests.cs`, `FinalDeckAndMandatoryOfferTests.cs` | Empate, proyección/ensamblado, coherencia privada, copias y privacidad. |
| `Desmoche.Singleplayer.Pruebas/Program.cs` | Verificar revelaciones coherentes en finales reales de bots, incluidos empates. |
| `desmoche/Scripts/Diagnostics/OfferAndFinalHandsSmoke.cs` | Prueba de victoria y empate con 2–4 participantes, siete resoluciones y el caso visual de la captura. |
| `desmoche/Scripts/Diagnostics/MesaVisualSmoke.cs`, `TimedRoomSmoke.cs` | Expectativas de empate y transporte real de manos; limpieza en revancha. |

Se reutilizan los DTO y sus copias defensivas. El validador conserva conteos por jugador, identidades únicas, ausencia de solapamiento con mesa/pozo/mazo final y coincidencia de la mano revelada propia con el canal privado.

## 5. Configuración Godot

Sin cambios manuales de Inspector, señales o escenas de producción. Bajo `Mesa/Scroll/Padding/Content` siguen existiendo los botones dinámicos `ShowHandButton<ID>` con texto **Mostrar juego**, `ShowDeckButton` con **Mostrar mazo**, `FinalHandPanel` y `CloseFinalHandButton`.

Los botones de manos aparecen debajo de cada rival al finalizar. Una mano vacía también tiene botón y muestra **Sin cartas restantes en la mano**. Consultar una mano sustituye el contenido del mazo en la misma ventana y viceversa. Se conserva el espacio derecho libre, sin cubrir los juegos publicados; cerrar, Escape, clic exterior, salida y nueva partida limpian las cartas de consulta.

## 6. Integración

Combinar el contenido de `desmoche` del ZIP con el proyecto existente, reemplazar los archivos incluidos y recompilar en Godot .NET. Todos los equipos LAN deben instalar esta actualización de protocolo 7. Las pruebas permanecen en carpetas hermanas de `desmoche`, fuera de `res://`.

El paquete es acumulativo e incluye Peladilla, trasmoche, plazos/revancha, reparto/fluidez, adaptación, selección, oferta verde, mazo final y Pasa por oferta obligatoria.

## 7. Prueba de aceptación

Ejecutar los cuatro comandos del README. La suite comprueba revelaciones en empate por motor y sesión, destinatarios privados, rechazo de revelaciones incompletas/duplicadas/solapadas, copia defensiva y prohibición antes de finalizar. Las partidas de bots verifican las manos finales y sus conteos.

`res://Scenes/Diagnostics/OfferAndFinalHandsSmoke.tscn`, con render real, espera `OFFER AND FINAL HANDS PASS`. Recorre victorias y empates, 2–4 jugadores y siete resoluciones. El empate reproduce mazo agotado, cinco cartas propias y rivales con cero/seis/seis cartas: todos tienen Mostrar juego y el mazo conserva su botón. Se prueba cada mano, cambio de consulta, juegos visibles, cerrar y limpieza de nueva partida.

`TimedRoomSmoke` usa procesos ENet separados y compara las huellas de todas las manos y del mazo finales recibidos por host/clientes, junto con la ausencia de revelaciones en el siguiente reparto. Resultados ejecutados y alcance en `VERIFICACION.md` del ZIP.

Pendiente aceptación táctil y Wi-Fi entre dispositivos físicos; no se exportó APK/IPA. Las pruebas de GUI usan eventos nativos en SubViewport y render de software. La conexión entre procesos no sustituye una red Wi-Fi física.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| Solo aparece Mostrar mazo en empate | Instalar este parche y recompilar; versiones anteriores no entregaban las manos en empate. |
| Versión de sala incompatible | Actualizar todos los participantes al protocolo 7. |
| Un rival tiene cero cartas | Su botón permanece disponible y la ventana explica la mano vacía. |
| No hay botones durante la partida | Las consultas de manos y mazo se habilitan únicamente en `Finished`. |
| Revelación incompleta no se muestra | La réplica exige las manos de todos los participantes con cantidades e identidades coherentes. |

## 9. Estado de la fase

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar; aceptación en dispositivos físicos pendiente.

SIGUIENTE FASE: la que indique el usuario.
