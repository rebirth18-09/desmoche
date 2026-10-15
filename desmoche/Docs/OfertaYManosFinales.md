# Oferta seleccionada y consulta de manos finales

Corrección posterior: [ManosFinalesEnEmpate.md](ManosFinalesEnEmpate.md) habilita Mostrar juego también en empate. Su autorización de manos en todo Finished y protocolo 7 sustituyen las restricciones/versiones anteriores de esta documentación.

Ampliación posterior: [MazoFinalYOfertaObligatoria.md](MazoFinalYOfertaObligatoria.md) incorpora el mazo final y la denuncia por oferta sin jugar. Su protocolo 6 y obligación sobre juegos publicados sustituyen las referencias anteriores correspondientes.

## 1. Objetivo de la fase

Extender el contorno verde a la combinación de una oferta elegida con cartas seleccionadas de la mano, y sustituir las manos reveladas al pie por un botón **Mostrar juego** de cada rival. La ventana de consulta es translúcida y deja visibles sus juegos publicados.

## 2. Decisiones arquitectónicas

El indicio combina copias de las cartas seleccionadas con la fuente externa marcada. Dos cartas propias y una oferta pueden formar tercia o escalera; tres propias y la oferta pueden completar cuarta o continuar escalera. Se admiten los mismos juegos al ordenar copias con As bajo o alto. El verde aparece en las propias seleccionadas y en la fuente externa; la oferta conserva su posición. No se cambia el orden ni se confirma automáticamente una jugada. La selección del pozo comparte el mismo comportamiento.

La respuesta aceptada de **Quiero esta carta** ya marca la fuente mediante la selección existente. Se reutiliza esa ruta, su reserva de veinte segundos y la validación autoritativa. Al desmarcar la fuente, añadir un sobrante o cambiar el contexto, se recalcula el indicador. El plazo y las reglas no cambian.

Las manos finales se leen exclusivamente de `SnapshotDto.RevealedHands`, con fase `Finished`, ganador y resultado `Desmoche` o `InitialVictory`. Empate, desconexión o una partida activa no autorizan la consulta. No se añaden datos, RPC ni acceso a manos ocultas. Se abre una ventana a la vez, con las cartas restantes de un rival. La evidencia de victoria inicial permanece en su zona pública, conforme a la política anterior.

## 3. Dependencias

Godot 4.7.2 .NET y las clases existentes `Carta`, `Mesa`, `IGameSession`, `HandInteractionState`, `RulesValidator` y DTO. La ventana usa `PanelContainer`, `StyleBoxFlat`, `HFlowContainer` y las transformaciones de `Control` empleadas por la mesa; firmas verificadas con el SDK del proyecto. No se añaden recursos gráficos ni paquetes.

## 4. Implementación

| Archivo | Responsabilidad |
| --- | --- |
| `Scripts/Presentation/Interaction/HandSelectionHint.cs` | Evaluar toda la selección junto con la fuente externa marcada. |
| `Scripts/Presentation/Carta.cs` | Aplicar el verde a una carta seleccionada sin elevarla, para oferta/pozo. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Actualizar las cartas propias y la fuente pública al cambiar la selección. |
| `Scripts/Presentation/Mesa.FinalHands.cs` | Botones por rival, ventana translúcida, contenido autorizado, distribución y cierre. |
| `Scripts/Presentation/Mesa.cs` | Ciclo de consulta, creación de botones y retirada de revelaciones al pie. |
| `Scripts/Presentation/Mesa.Casino.cs` | Distribución del final y ubicación de la ventana. |
| `Scripts/Presentation/Mesa.Selection.cs` | Cerrar con Escape o clic fuera de la ventana. |
| `Scripts/Diagnostics/OfferAndFinalHandsSmoke.cs` y escena | Prueba nativa de oferta elegida y consulta final con dos, tres y cuatro jugadores. |
| `Scripts/Diagnostics/MesaVisualSmoke.cs` | Expectativas de la nueva revelación por botón. |
| `Desmoche.Interaccion.Pruebas/HandSelectionHintTests.cs` | Once casos adicionales con fuente externa y fronteras. |

No se instancian cartas finales hasta que se pulsa el botón. Cerrar, iniciar otra partida, perder la sesión o retirar la autorización borra las cartas de la ventana. Una mano vacía muestra una indicación explícita. Una actualización de la misma finalización conserva la ventana si su contenido sigue vigente.

## 5. Configuración Godot

Nodos dinámicos bajo `Mesa/Scroll/Padding/Content`:

- `ShowHandButton<ID>`: **Mostrar juego**, asociado a la zona pública del rival. Altura mínima de interfaz 44.
- `FinalHandPanel`: `PanelContainer` translúcido, oculto inicialmente, z-index 10.
- `FinalHandPanel/Padding/Content/TitleLabel`, `EmptyLabel` y `Cards`: nombre/cantidad, mano vacía y fila de cartas con ajuste.
- `CloseFinalHandButton`: **Cerrar**, altura de interfaz 44 y z-index 11.

Los botones se sitúan bajo las cartas públicas de su rival sin superponerse. La ventana utiliza la franja derecha del tablero, que queda fuera de los juegos propios y debajo de los rivales. Su contenido ajusta escala para caber en esa zona. Al finalizar, el mazo y el pozo se acomodan en el espacio izquierdo libre, debajo de los botones; sus contadores no tapan el juego propio. Las cartas de juegos publicados conservan posición y visibilidad.

La ventana no oscurece el resto de la pantalla ni bloquea consultar otro rival. Se cierra con Cerrar, Escape o un clic exterior. `RevealedZone` permanece en la escena para compatibilidad y sigue oculto/vacío. No requiere conexiones manuales en el Inspector ni cambios del protocolo 5.

## 6. Integración

Se conserva el flujo UI → intención → sesión → vista autorizada. La elección aceptada marca la fuente; al seleccionar las cartas propias, el indicio usa la fuente y la selección completa. El verde es una sugerencia de combinación posible y sigue requiriendo ordenar y confirmar según las reglas.

Al recibir una victoria, Mesa dibuja los mismos juegos y añade los botones de las manos que la autoridad reveló. Pulsar uno consulta esa mano; cambiar de rival sustituye el contenido de la ventana. El snapshot y los juegos publicados permanecen intactos. LAN y Singleplayer usan la misma presentación.

## 7. Prueba de aceptación

Ejecutar los comandos del README. La consola de interacción añade once comprobaciones: dos propias con oferta, tercia, cuarta, As alto, ausencia de elección, selección insuficiente, sobrante, duplicado e identidad inválida.

El diagnóstico `res://Scenes/Diagnostics/OfferAndFinalHandsSmoke.tscn` requiere render real. Finaliza con `OFFER AND FINAL HANDS PASS` y puede recibir `-- --capture=RUTA_ABSOLUTA`. El botón Quiero esta carta se prueba mediante un adaptador de sesión sintético que emite vista y Reply diferido; comprueba el puente de presentación, no sustituye las pruebas del motor y sus plazos.

La prueba final recorre siete tamaños de ventana y dos, tres y cuatro jugadores. Comprueba botones, contenido correcto por rival, cartas dentro de la ventana, contadores separados, juegos visibles sin superposición, cambio de rival, manos de cero y nueve cartas, cierre, Escape, siguiente partida, empate, salida y conservación del snapshot. Las consolas de reglas, red, réplica y bots y los diagnósticos de selección, Mesa y reparto mantienen sus regresiones. Los resultados ejecutados están en `VERIFICACION.md` del paquete.

Queda pendiente la aceptación táctil y de área segura en teléfonos físicos; no se generó un APK/IPA en esta entrega.

## 8. Posibles errores

Si dos cartas no se marcan verde, comprobar que la fuente externa esté elegida/marcada y que toda la selección forme un único juego junto con ella. Verde no corrige el orden visible. Si no aparece Mostrar juego, la vista debe haber terminado con victoria y contener una revelación de ese rival: empate y desconexión no la habilitan. Al cerrar no se borra ningún juego; solo se retira la copia visual de la mano consultada.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación en dispositivos físicos pendiente.
SIGUIENTE FASE: la que indique el usuario.
