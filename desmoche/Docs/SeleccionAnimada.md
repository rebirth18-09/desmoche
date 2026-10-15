> Ampliación posterior: [OfertaYManosFinales.md](OfertaYManosFinales.md) añade la oferta/pozo marcada al indicador y actualiza la consulta de manos finales.
> [ArrastreSuave.md](ArrastreSuave.md) conserva esta selección durante el deslizamiento de cartas y añade el hueco de destino.

# Selección animada de la mano

## 1. Objetivo de la fase

Elevar suavemente las cartas seleccionadas de la mano, retirar la selección al pulsar un espacio vacío y mostrar un contorno verde cuando toda la selección pueda formar un juego, aunque esté desordenada. Una selección que no forma un juego solo se eleva, con el borde normal.

## 2. Decisiones arquitectónicas

La selección continúa siendo local. El indicador considera exclusivamente las cartas seleccionadas de la mano propia: no consulta cartas ocultas, ni incorpora automáticamente pozo, oferta o juegos publicados. Una o dos cartas nunca se marcan verde. Para tres o más debe poder formarse **un único juego con toda la selección**; tener una tercia más una carta sobrante o dos tercias separadas no basta.

`HandSelectionHint` crea copias ordenadas con As bajo y alto y reutiliza `RulesValidator.ValidateMeld`. Se reconocen tercia, cuarta y escalera. El verde expresa que pueden formar una combinación; no certifica el orden declarado ni elimina la obligación de alternar colores. No cambia el orden visible, no prepara un borrador y no confirma una jugada. La autoridad mantiene la validación y las penalizaciones existentes.

Cada carta sube un **18 % de su altura** durante **0,16 segundos**, con curva cúbica de salida. Al deseleccionar vuelve suavemente a su sitio. Se utiliza el desplazamiento de transformación de Godot 4.7, independiente del layout del contenedor. El input acompaña a la carta elevada. No se reconstruyen los nodos ni se recalcula la validez en cada fotograma.

## 3. Dependencias

Godot 4.7.2 .NET y las clases actuales de cartas, Mesa, selección y reglas. APIs verificadas en el SDK instalado y la documentación oficial de [Control](https://docs.godotengine.org/en/stable/classes/class_control.html) (`offset_transform_enabled`, `offset_transform_position_ratio`, `offset_transform_visual_only`) y [Tween](https://docs.godotengine.org/en/stable/classes/class_tween.html).

## 4. Implementación

| Archivo | Responsabilidad |
| --- | --- |
| `Scripts/Presentation/Carta.cs` | Animación proporcional, sombra de elevación y contorno verde; cancela el tween al cambiar identidad, mostrar dorso/vacío o salir. |
| `Scripts/Presentation/Interaction/HandSelectionHint.cs` | Indicio puro de combinación completa en cualquier orden, sin mutar la selección. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Calcula el indicio al cambiar la selección y actualiza las cartas correspondientes. |
| `Scripts/Presentation/Mesa.Selection.cs` | Detecta clics en espacios vacíos, preservando clics sobre cartas, botones, campos y barras de scroll. |
| `Scripts/Presentation/Mesa.Casino.cs` | Rótulo de la mano en el espacio lateral de la bandeja, sin tapar cartas elevadas. |
| `Scripts/Diagnostics/HandSelectionSmoke.cs` y escena | GUI nativa: elevación, contorno, clic elevado, espacio vacío, arrastre y resize. |
| `Desmoche.Interaccion.Pruebas/HandSelectionHintTests.cs` | Permutaciones, As alto/bajo, duplicados, sobrantes y conservación del orden. |

La selección de cartas externas y los borradores conservan sus indicaciones previas. Las cartas de mano incluidas en un borrador no se confunden con una selección nueva: mantienen su indicación de preparación y no se elevan por esta animación. Pulsar el vacío limpia la selección; no borra los borradores ni la carta preparada para pago/cambio.

## 5. Configuración Godot

No se añaden nodos a `Carta.tscn` ni se cambian sus rutas. La raíz sigue siendo `PanelContainer` con `Padding/Content`, arte e índices. Mesa invoca `SetHandSelection` solo para la mano. Activa `OffsetTransformEnabled`, desactiva `OffsetTransformVisualOnly` y anima `offset_transform_position_ratio`. La posición y tamaño que controla `HFlowContainer` permanecen independientes de esta elevación.

No requiere cambios manuales en el Inspector ni conexiones nuevas. `SelectionRequested`, click/Enter y Drag & Drop mantienen su contrato. El toque móvil sigue la emulación de ratón configurada en la entrega de resolución adaptable.

## 6. Integración

Click/Enter alterna la selección existente; `RefreshSelection` calcula una vez el indicio y aplica elevación y color. Añadir una carta incompatible retira el verde de toda la selección; quitarla puede restaurarlo. El click sobre la zona sobresaliente y el arrastre usan la geometría desplazada.

`Mesa._Input` trata el clic izquierdo inicial en zonas vacías incluso cuando un contenedor de UI consumiría el evento. No acepta el evento, no interfiere con botones ni procesa ruedas, otros botones o un arrastre activo. La respuesta de la sesión sigue siendo la única fuente del estado real. Cambios de fase, mano, sesión y salida conservan la reconciliación existente.

## 7. Prueba de aceptación

Ejecutar las cuatro órdenes del README. La consola de interacción añade **146 comprobaciones** de la sugerencia verde, incluidas todas las permutaciones de tercias, cuartas y escaleras de ejemplo. Una escalera desordenada obtiene el indicio y sigue siendo inválida para confirmarla en ese orden: se comprueba expresamente.

El diagnóstico `res://Scenes/Diagnostics/HandSelectionSmoke.tscn` requiere render real y termina con `HAND SELECTION PASS`. Puede recibir `-- --capture=RUTA_ABSOLUTA`. Comprueba selección progresiva, verde para juego desordenado, retirada del verde al añadir un sobrante, retorno suave, clic en la parte elevada, botón sin deselección, tercia/cuarta, As alto, resize a 844×390, arrastre, conservación de snapshot y limpieza al cambiar de fase/salir.

Se repiten los diagnósticos de Mesa dinámica, reparto, resolución adaptable y Singleplayer. Los resultados reales y su alcance quedan en `VERIFICACION.md` del paquete. Complementar en la instalación con una partida y gestos en teléfono físico.

## 8. Posibles errores

Si solo cambia el borde y no sube la carta, comprobar que se sustituyeron juntos `Carta.cs`, `Mesa.Interaction.cs` y los nuevos archivos. Si la selección pierde el verde al añadir otra carta, verificar que toda la selección forme un único juego. El verde con cartas desordenadas indica posibilidad: ordenarlas antes de confirmar evita las consecuencias previstas por las reglas. El área de input se mueve con la carta; pulsar el espacio que queda libre debajo cuenta como espacio vacío.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil en dispositivos físicos pendiente.
SIGUIENTE FASE: la que indique el usuario.
