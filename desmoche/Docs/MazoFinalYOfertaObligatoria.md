# Mazo final y oferta obligatoria

Corrección posterior: [ManosFinalesEnEmpate.md](ManosFinalesEnEmpate.md) habilita Mostrar juego también en empate. Su autorización de manos en todo Finished y protocolo 7 sustituyen las restricciones/versiones anteriores de esta documentación.

Fecha: 14 de octubre de 2026. Godot 4.7.2 .NET/C#.

## 1. Objetivo de la fase

Añadir **Mostrar mazo** al final para consultar las cartas restantes en el orden real de salida. Añadir **Pasa** cuando nadie usa una oferta que podía ampliar un juego propio publicado. Conservar el diseño y todos los arreglos anteriores.

## 2. Decisiones arquitectónicas

El usuario confirmó: **solo se denuncia si nadie utiliza la oferta**. Si otro la reclama, se mantiene la prioridad actual y no se sanciona al que pasó. La obligación corresponde a ofertas públicas del mazo o de un pago, para jugadores habilitados para responder. El pagador sigue excluido de su propia oferta; el pozo antiguo del turno no abre esta denuncia.

La carta debe poder añadirse a un juego publicado completo: completar una cuarta o ampliar un extremo de escalera válido, con As bajo/alto sin vuelta K-A-2. Una posibilidad solo en mano no obliga. Se permite usarla en otro juego válido, incluyendo el trasmoche ya autorizado. Elegir y completar automáticamente un juego a los veinte segundos también cumple la obligación.

Después de todas las respuestas y reservas, si la oferta queda sin uso, la autoridad abre **tres segundos** de Pasa por infractor, en orden de asiento. La oferta permanece visible y la partida espera. Un rival habilitado que pulsa Pasa elimina únicamente al infractor. Sin denuncia, continúa. Después de todas las oportunidades la carta pasa al pozo, sin incorporarse automáticamente al juego del recién eliminado. Se conserva la segunda extracción de apertura cuando corresponde. Cada oportunidad tiene ID, ventana y plazo propios: una intención atrasada no puede denunciar al siguiente jugador.

El mazo se revela únicamente con `Phase = Finished`, incluyendo empate. Las manos ajenas conservan su política: solo se revelan tras victoria. La revelación es una copia de arriba hacia abajo, con la próxima extracción en el primer elemento; no cambia el dominio ni extrae cartas. Es nula durante preparación, partida activa, Pasa y revancha nueva.

El protocolo pasa a **6**, por `FinalDeck` y el motivo/juego de Pasa. Todos los equipos deben instalar la misma revisión. No se añaden RPC: se reutilizan canales, comandos y réplica existentes.

## 3. Dependencias

`GameManager`, `RulesValidator`, `ServerSession`, proyección/ensamblado, `IGameSession`, `Mesa` y `Carta`. Godot 4.7.2 .NET, escritorio `net8.0` y SDK .NET 9.0.308; sin paquetes ni imágenes nuevos. El desplazamiento usa `ScrollContainer` y `GridContainer`, comprobados con el SDK y la [documentación oficial](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html).

## 4. Implementación

Rutas desde la raíz:

| Archivo | Cambio |
| --- | --- |
| `desmoche/Scripts/Domain/GameState.cs` | Copia interna del mazo en orden de extracción. |
| `desmoche/Scripts/Domain/RulesValidator.cs` | Ampliación válida con As bajo/alto. |
| `desmoche/Scripts/Application/GameManager.cs` | Motivo, cola y resolución de Pasa; revelación únicamente final. |
| `desmoche/Scripts/Networking/Core/NetworkProtocol.cs` | Protocolo 6, motivo/juego cuestionado y mazo final. |
| `desmoche/Scripts/Networking/Core/ServerSession.cs`, `ServerSession.Timing.cs` | Plazos y ventanas distintos; la denuncia no renueva la elección. |
| `desmoche/Scripts/Networking/Core/StateReplication.cs` | Permiso, conteos, identidades, ensamblado y copias defensivas. |
| `desmoche/Scripts/Singleplayer/Core/LocalMatchController.cs` | Preservar apertura durante la pausa y copiar el mazo final. |
| `desmoche/Scripts/Presentation/Mesa.FinalHands.cs` | Botón, números y desplazamiento; una consulta final a la vez. |
| `desmoche/Scripts/Presentation/Mesa.cs`, `Mesa.Countdowns.cs` | Anclar Pasa al juego compatible; texto y plazo según motivo. |
| `desmoche/Scenes/MenuPrincipal.tscn` | Ayuda de obligación y duración correcta de Pasa. |
| `Desmoche.Pruebas/FinalDeckAndMandatoryOfferTests.cs`, `Program.cs`, `StateReplicationTests.cs`, `TimedMatchTests.cs` | Casos de reglas, tiempos, privacidad y regresiones del contrato. |
| `Desmoche.Interaccion.Pruebas/ActionInteractionTests.cs` | Disponibilidad de Pasa de oferta y pausa de reclamos. |
| `desmoche/Scripts/Diagnostics/MandatoryOfferSmoke.cs` y escena | GUI nativa conectada a la autoridad real. |
| `desmoche/Scripts/Diagnostics/OfferAndFinalHandsSmoke.cs`, `TimedRoomSmoke.cs` | Consulta y orden; revelación ENet y limpieza de revancha. |

`OrderChallengeDto` conserva su nombre y añade `Reason` (`SetOrder`/`UnusedOffer`) y `MeldId`. La denuncia de oferta requiere una oferta pública y un juego compatible del infractor. `FinalDeck` se añade al snapshot y réplica pública: debe ser nulo antes del final y contener exactamente `DeckCount` identidades distintas al terminar. Se rechazan identidades inválidas o solapamientos con juegos, oferta, pozo visible y manos finales. El ensamblador comprueba también la mano privada en empate. Todas las fronteras copian los arrays.

## 5. Configuración Godot

No requiere cambios manuales del Inspector ni conexiones de señales. Se conserva `/root/Main/NetworkManager`. Nodos bajo `Mesa/Scroll/Padding/Content`:

- `ShowDeckButton`: **Mostrar mazo**, junto al mazo, debajo de su contador, altura de interfaz 44; visible solo con revelación final autorizada.
- `FinalHandPanel/Padding/Content/DeckOrderLabel`: **1 = próxima carta en salir**.
- `FinalHandPanel/Padding/Content/DeckScroll/Cards`: grid de cuatro columnas. Cada `Posicion<N>` contiene `OrderLabel` y `Carta`; lectura de izquierda a derecha y de arriba abajo, con desplazamiento vertical.
- `FinalHandPanel` y `CloseFinalHandButton`: ventana translúcida y cierre existentes, en el espacio derecho libre, sin cubrir juegos publicados. Mano y mazo se consultan uno a la vez.
- `PasaButton`: mantiene su posición junto al juego señalado; el texto distingue la oferta sin jugar del orden de colores y el botón respeta el plazo mostrado. El host valida definitivamente.

Las cartas del mazo no se instancian antes de pulsar el botón. La consulta empieza por la próxima carta; un mazo agotado muestra un texto explícito. Cerrar, Escape, clic exterior, salida, cambio de sesión y nueva partida eliminan las copias consultadas. Una actualización del mismo final conserva la ventana si su contenido sigue vigente.

## 6. Integración

Combinar `desmoche` del paquete con el proyecto existente y reemplazar los archivos incluidos. Actualizar todos los participantes LAN y recompilar con Godot .NET. Las tres carpetas de pruebas permanecen hermanas de `desmoche`, fuera de `res://`.

Usar **Quiero esta carta** y confirmar un juego válido conserva los plazos 5/20. Si todos pasan o vence la elección sin uso y un juego publicado admite la carta, los demás ven Pasa durante tres segundos. Luego continúa el flujo normal. Al terminar, Mostrar mazo consulta el orden restante; Mostrar juego conserva la mano por rival.

Bots y humanos atraviesan la misma autoridad. Los bots mantienen la atención a Pasa de su dificultad; no emplean el mazo final en decisiones. LAN y Singleplayer comparten regla, reloj y presentación.

## 7. Prueba de aceptación

Ejecutar los cuatro comandos del README. La suite nueva cubre palos/extremos y trece rangos de cuarta; casos no obligatorios; rechazo explícito/automático; 4.999/5.000 y 2.999/3.000 ms; preparación automática; juego alternativo; reclamo de otro jugador; pagos, exclusión del pagador y pozo antiguo; varios infractores; intención atrasada y desconexión; orden final, copias, privacidad, empate, agotamiento y limpieza de revancha.

Con render real, ejecutar con F6:

- `res://Scenes/Diagnostics/MandatoryOfferSmoke.tscn`: `MANDATORY OFFER PASS`. Rechazar oferta y Pasa atraviesan intención, `ServerSession` real, vista y respuesta. También vence una oportunidad sin denuncia.
- `res://Scenes/Diagnostics/OfferAndFinalHandsSmoke.tscn`: `OFFER AND FINAL HANDS PASS`. Mazo y manos con 2–4 jugadores en siete resoluciones, desplazamiento hasta la última carta, cambio/cierre de consulta y juegos visibles.
- `TimedRoomSmoke`: procesos ENet separados comprueban huella idéntica del mazo final, nueva partida sin revelación, host espectador y exclusión por falta de confirmación.

Resultados ejecutados en `VERIFICACION.md` del ZIP. ENet entre procesos de una PC no sustituye dispositivos Wi-Fi físicos. La GUI nativa usa eventos en un SubViewport aislado. La prueba de Pasa usa la autoridad real sin transporte; las fronteras del DTO se prueban en consola. Pendiente aceptación táctil y exportación Android/iOS físicas.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| Versión incompatible o sala no encontrada | Todos deben usar protocolo 6. |
| No aparece Pasa al pasar | Debe haber un juego publicado compatible, oferta sin uso y denunciante habilitado. Solo mano no obliga. |
| Otro usa la carta y no hay denuncia | Es la prioridad confirmada por el usuario. |
| Vence el plazo y no pierde | Eliminar exige que un rival pulse Pasa dentro de tres segundos. |
| Hay oportunidades consecutivas | La oferta entraba en juegos de varios jugadores; cada infractor tiene su oportunidad. |
| No aparece Mostrar mazo | Requiere final y dato autorizado; una versión anterior no lo proporciona. |
| Se ven pocas cartas | Desplazar la ventana; los números conservan el orden completo. |
| Empate sin manos reveladas | Se conserva esa política; la nueva autorización es para el mazo final. |

## 9. Estado de la fase

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar; aceptación en dispositivos físicos pendiente.

SIGUIENTE FASE: la que indique el usuario.
