> [PagoPreparado.md](PagoPreparado.md) conserva este arrastre cuando una carta ya está preparada en Pago/Cambio inicial y no ocupa un espacio visible en la mano.

# Arrastre suave con destino marcado

## 1. Objetivo de la fase

Al arrastrar una carta de la mano, mostrar únicamente la carta que sigue al cursor. Las vecinas se deslizan para abrir el espacio que ocuparía al soltar y un hueco dorado señala esa posición. Confirmar el nuevo orden al terminar el gesto.

## 2. Decisiones arquitectónicas

La previsualización pertenece a Mesa y Carta. Conserva el orden de `HandInteractionState` hasta recibir el drop; no modifica cartas autoritativas, snapshots, cantidades, reglas ni protocolo 7. Las posiciones de destino se calculan desde los espacios originales de la mano, evitando que el movimiento de las vecinas cambie continuamente el destino.

Los mismos nodos Carta permanecen en la mano durante la animación. El origen conserva su espacio y su geometría de input, pero se dibuja con alfa cero mientras existe el preview nativo de Godot. Al confirmar se reordenan los hijos existentes. Las vecinas y el aterrizaje usan un tween de **0,16 segundos**, cúbico de salida, sobre `offset_transform_position`. La elevación seleccionada sigue utilizando `offset_transform_position_ratio`; ambos desplazamientos son independientes.

## 3. Dependencias

Godot **4.7.2 .NET**, Mesa y Carta actuales, `HandInteractionState`, `CardDragData` y la configuración de resolución adaptable. APIs comprobadas en el SDK instalado y la documentación oficial de [Control](https://docs.godotengine.org/en/stable/classes/class_control.html), [Viewport](https://docs.godotengine.org/en/stable/classes/class_viewport.html) y [Tween](https://docs.godotengine.org/en/stable/classes/class_tween.html): preview nativo, notificación de fin, cancelación y transformación independiente del layout.

## 4. Implementación

| Archivo bajo `desmoche` | Cambio |
| --- | --- |
| `Scripts/Presentation/Mesa.HandDrag.cs` y `.uid` | Espacios estables, hueco dorado, deslizamiento, confirmación sin reconstruir y limpieza del gesto. |
| `Scripts/Presentation/Carta.cs` | Tween de reordenación y callbacks locales para el preview y el fin del arrastre. El preview conserva la escala y el indicador verde del original. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Conecta las cartas propias; el drop usa el destino marcado. Los botones de mover también reutilizan nodos y animan el desplazamiento. |
| `Scripts/Presentation/Mesa.Selection.cs` | Actualiza el destino antes del release y permite cancelar con Escape. |
| `Scripts/Presentation/Mesa.cs` | Inicialización y limpieza del nuevo componente visual. |
| `Scripts/Diagnostics/HandDragSmoke.cs`, `.uid` y `Scenes/Diagnostics/HandDragSmoke.tscn` | Prueba de integración con input nativo y render real. |

## 5. Configuración Godot

No se necesitan conexiones manuales, ajustes de Inspector ni cambios en `Mesa.tscn` o `Carta.tscn`. Los nodos nuevos se crean al iniciar Mesa:

| Nodo | Tipo y función |
| --- | --- |
| `Mesa.ContentPath + HandDragSlot` | Panel del tablero virtual, Z=4, fondo translúcido y contorno dorado con margen exterior. No intercepta input. |
| `HandDragSlot/DestinationLabel` | Label «Aquí», centrado en el espacio previsto. |
| Preview temporal del viewport | Control con Carta, Z=50; sigue al cursor por encima del marcador y Godot lo libera al acabar el gesto. |

`DragPreviewCreated` y `DragCompleted` son callbacks locales de Carta, sin señales ni RPC nuevos. `SelectionRequested` conserva su contrato. El arte del preview se escala uniformemente desde las métricas originales, evitando que los tamaños mínimos agranden la carta en resoluciones pequeñas.

## 6. Integración

Arrastrar selecciona la carta según el comportamiento existente y conserva las demás seleccionadas. Mientras el cursor cruza posiciones de la mano, las vecinas abren el hueco correspondiente; el marcador representa el orden que se confirmará. Al soltar, la carta aterriza suavemente y mantiene su elevación y verde, incluido un juego que use la oferta o el pozo seleccionado.

Salir de la mano oculta el marcador y devuelve las vecinas a sus sitios. Soltar en vacío o pulsar Escape devuelve también la carta arrastrada sin confirmar un nuevo orden. Llevarla a juegos propios o borradores conserva la preparación existente. Un snapshot nuevo, cambio de contexto, redimensionamiento que cambie la transformación o salida de Mesa cancela y limpia el preview antes de representar el estado recibido.

LAN conserva la autoridad del host; Singleplayer reutiliza la misma UI. El toque sigue la emulación de ratón del proyecto. La animación no adelanta solicitudes de juego, pago, cambio, Peladilla ni denuncias.

## 7. Prueba de aceptación

Ejecutar las cuatro órdenes de compilación y pruebas del README. Para probar el gesto, abrir `res://Scenes/Diagnostics/HandDragSmoke.tscn` con **F6** en Godot .NET y esperar `HAND DRAG PASS`. Requiere render real, no modo headless. Opcionalmente recibe `-- --capture=RUTA_ABSOLUTA`.

El diagnóstico usa input nativo en **640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440**. Comprueba origen oculto y preview renderizado, seguimiento del cursor, hueco correcto, desplazamiento de vecinas, conservación de nodos y snapshot, orden confirmado al soltar, extremos de la mano, verde, Escape, soltado exterior, cambio de fase, preparación de borrador, resize y salida. Cada caso inicia una sesión nueva: los snapshots ordinarios conservan deliberadamente el orden local elegido.

También se repiten los diagnósticos de selección, Mesa dinámica y reparto. Los resultados ejecutados están en `VERIFICACION.md` del paquete. Complementar con mouse y toque en la instalación del usuario; no se ha medido el rendimiento de su PC ni probado un teléfono físico.

## 8. Posibles errores

Si el origen sigue visible o no aparece el hueco, comprobar que se reemplazaron juntos Carta, las partes de Mesa y el nuevo `Mesa.HandDrag.cs` con su `.uid`. Importar con Godot .NET y esperar la compilación antes de ejecutar. El indicador dorado solo acompaña la reorganización de la mano; al llevar la carta a otra zona se aplican los destinos de preparación existentes. El contorno verde conserva su significado de posibilidad de combinación y la validación autoritativa sigue comprobando la jugada enviada.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil en dispositivos físicos pendiente.
SIGUIENTE FASE: la que indique el usuario.
