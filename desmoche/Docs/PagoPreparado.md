# Carta preparada sin duplicado en la mano

## 1. Objetivo de la fase

Al soltar una carta en Pago, mostrarla inmediatamente solo en esa casilla. La fila de la mano cierra el espacio libre. Aceptar confirma el pago existente; preparar la carta sigue siendo una acción local reversible.

## 2. Decisiones arquitectónicas

La carta permanece en `OwnHand` y en el orden local hasta recibir el estado confirmado de la sesión. Se oculta su nodo de la mano, conservando identidad y posición relativa, y se dibuja en la casilla. Cancelar, reemplazar la elección, un rechazo o vencer la espera de respuesta la restauran. Los contadores mantienen las cantidades autoritativas: incluyen la carta preparada mientras todavía no se ha pagado.

La misma casilla se usa para el cambio inicial y comparte este comportamiento visual. No cambian las reglas de pago/intercambio, las solicitudes ni protocolo 7. La autoridad sigue siendo el host en LAN y la sesión compartida en Singleplayer.

## 3. Dependencias

Godot 4.7.2 .NET, `Mesa.Dynamic`, el arrastre suave y `IGameSession` existentes. Se reutilizan `Control.Visible` y el layout de `HFlowContainer`: ocultar libera espacio sin eliminar el nodo ni reconstruir las cartas restantes.

## 4. Implementación

| Archivo bajo `desmoche` | Responsabilidad |
| --- | --- |
| `Scripts/Presentation/Mesa.Dynamic.cs` | Oculta la carta preparada de la fila, bloquea su input y mantiene la casilla al enviar Aceptar. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Restaura tras rechazo o espera vencida; traduce índices del arrastre visible al orden local completo. |
| `Scripts/Presentation/Mesa.HandDrag.cs` | Excluye la carta preparada de los destinos visibles, conserva su posición en el modelo y evita animar su retorno mientras está oculta. |
| `Scripts/Presentation/Mesa.Casino.cs` | Dimensiona la fila según las cartas visibles. |
| `Scripts/Diagnostics/HandDragSmoke.cs`, `HandDragSmoke.Payment.cs` y `.uid` | Input nativo, preparación y reordenación con una carta oculta; respuestas y snapshots en ambos órdenes de llegada. |
| `Scripts/Diagnostics/MesaDynamicSmoke.cs` | Actualiza la aceptación del pago/cambio preparado. |

## 5. Configuración Godot

No se añaden escenas, nodos, señales ni RPC, y no requiere cambios de Inspector. Se conserva `Mesa.CenterPath + Pago/Content/Carta`, con Aceptar y Cambiar carta en `Mesa.ContextActionsPath`. El nodo original continúa en `HandZone/Padding/Content/Cards`, oculto y sin interacción mientras está preparado.

## 6. Integración

Soltar prepara; Aceptar envía una sola solicitud y bloquea cambios mientras está pendiente. La carta permanece en la casilla incluso si Reply llega antes del snapshot. El snapshot confirmado decide si ya salió de la mano. Cambiar carta o Escape deshace una preparación que todavía no se envió; preparar otra devuelve la anterior.

Una revisión del mismo contexto conserva la elección pertinente. Cambio de fase/ventana/sesión, carta ausente o ya asignada, eliminación y salida invalidan la preparación mediante las comprobaciones existentes. Un envío que no llega a iniciarse conserva la carta en la casilla y permite cambiarla o volver a intentar.

Las otras cartas se pueden ordenar con el deslizamiento y hueco dorado existentes. Sus índices se calculan sobre la fila visible y se traducen al modelo completo; al devolver la preparada se respeta ese orden. Borradores y verde conservan su funcionamiento.

## 7. Prueba de aceptación

Ejecutar compilación y las tres consolas del README. Abrir `Scenes/Diagnostics/HandDragSmoke.tscn` con F6 y esperar `HAND DRAG PASS`; requiere render real. Incluye pago en siete resoluciones, una sola copia visible, cierre del hueco, reemplazo, ordenar otras cartas, Cancelar/Escape, revisión intermedia, cambio de fase, envío único, rechazo, confirmación con Reply/snapshot en ambos órdenes, envío no iniciado, espera vencida y salida.

`MesaDynamicSmoke.tscn` comprueba también cambio inicial y pago preparado; `HandSelectionSmoke.tscn` cubre la selección elevada y el verde. Los resultados ejecutados y el alcance quedan en `VERIFICACION.md` del paquete. La aceptación táctil en un teléfono físico permanece pendiente.

## 8. Posibles errores

Si se ve la carta en ambos sitios, reemplazar juntos las partes de Mesa incluidas y esperar la recompilación .NET. Que el contador todavía incluya la preparada es correcto: el gesto no ha pagado esa carta. Mientras hay una solicitud pendiente no se puede cambiar la elección; un rechazo o falta de respuesta recupera el estado recibido de la sesión.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil física pendiente.
SIGUIENTE FASE: la que indique el usuario.
