# Edición en mesa

La actualización de [BurbujasYAccionesEnMesa.md](BurbujasYAccionesEnMesa.md) sustituye la fila de acciones inferior por botones junto a sus cartas/juegos y traslada los tres plazos a círculos en la mesa. Conserva preparación y consultas de esta fase.

La corrección posterior de [SalidaDePartida.md](SalidaDePartida.md) hace que el fondo de Menú ignore los clics y coloca TopBar al frente para recibir input. Incluye comprobación del cierre real de sesión; sustituye la comprobación limitada a visibilidad del botón.

## 1. Objetivo de la fase

Integrar preparación y edición en Mi juego, eliminar la página vacía inferior y conservar las acciones en una pantalla. Mantener animaciones, pago preparado y consultas finales.

## 2. Decisiones arquitectónicas

Mesa reutiliza `HandInteractionState`, las intenciones y `IGameSession`. Las cartas preparadas se ocultan de la fila y aparecen en un borrador dorado, conservando identidad, orden y cantidad autoritativa hasta el snapshot confirmado. Quitar las devuelve. Una edición muestra la versión preparada en lugar de duplicar el dibujo del juego publicado; el estado real permanece intacto.

Editar elige el juego como destino. Seleccionar cartas y Ampliar juego, o arrastrarlas, usa la preparación existente. Nuevo vuelve a crear un grupo independiente. Se mantienen trasmoches atómicos y cancelación conjunta de sus dependencias. No cambian dominio, reglas, autoridad, prioridades, plazos, solicitudes ni protocolo 7.

La textura conserva escala uniforme. Botones y cabecera utilizan unidades de interfaz; las acciones tienen al menos 44 de altura. La fila permanece estable al comenzar un arrastre. La información detallada se abre en Info.

## 3. Dependencias

Godot 4.7.2 .NET, Mesa/Carta y estado local existentes. APIs verificadas en el SDK instalado y [ScrollContainer](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html): clipping, desplazamiento propio de los paneles y desactivación del scroll general.

## 4. Implementación

| Archivo bajo `desmoche` | Responsabilidad |
| --- | --- |
| `Scripts/Presentation/Mesa.TableEditing.cs` y `.uid` | Barra de estado, Info, Menú, destinos de edición, marcos pendientes y navegación de Mi juego. |
| `Scripts/Presentation/Mesa.Casino.cs` | Una pantalla, ajuste uniforme, perfil junto a mano y disposición integrada. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Borradores posicionables, botón con StageCards y limpieza por contexto. |
| `Scripts/Presentation/Mesa.Dynamic.cs` | Acciones contextuales y ocultación de cartas asignadas. |
| `Mesa.cs`, `Mesa.Selection.cs`, `Mesa.Countdowns.cs` | Inicialización, cierre de paneles, registro de juegos y reloj en cabecera. |
| `Scenes/Mesa.tscn` | Rows de borradores pasa a Control. |
| `Scripts/Diagnostics/HandDragSmoke.Table.cs` y `.uid` | Preparación, edición, trasmoche, menú, publicación y muchos borradores con input nativo. |
| Diagnósticos de Mesa, resolución y oferta obligatoria | Aceptación de la nueva distribución y regresiones. |

## 5. Configuración Godot

Sin ajustes ni conexiones manuales de Inspector. Se conservan las rutas de mano, juegos, borradores, pago y acciones. Nodos principales bajo `Mesa.ContentPath`:

| Nodo | Uso |
| --- | --- |
| `TableStatus/StatusLabel` | Turno, resultado o mensaje de acción. |
| `TableStatus/CardCountdownLabel` | Cronómetro trasladado desde ContextActions. |
| `TableStatus/InfoButton`, `MenuButton` | Información y salida. |
| `TableInfo` | Panel de texto completo con scroll propio y Cerrar. |
| `ContextActions/PrepareSelectionButton` | Prepara grupo nuevo o ampliación. |
| `OwnGamesPath/EditGame{id}` | Elige el juego publicado como destino. |
| `DraftZone/Content/Rows/Borrador{n}/PendingFrame`, `RemoveButton` | Marco dorado y devolución. |
| `PreviousGamesButton`, `NextGamesButton` | Navegación interna si no cabe Mi juego. |

La página no tiene scroll. Info y el mazo final conservan sus desplazamientos propios. ContextActions es Control con distribución de filas explícita, para evitar posiciones diferidas antiguas. Todas las conexiones nuevas son locales; no hay RPC nuevos.

## 6. Integración

Arrastrar o seleccionar y pulsar Preparar juego prepara mediante StageCards, incluso si el grupo aún está incompleto. Solo se habilita la confirmación correspondiente cuando la preparación contiene las cartas necesarias; la autoridad valida. Se conserva el verde de selección, incluida oferta/pozo elegida.

El borrador se representa en Mi juego y sustituye visualmente al publicado cuando se edita. Quitar cancela dependencias de trasmoche y devuelve cartas privadas. La mano reordena solo cartas visibles y conserva el lugar lógico de las preparadas. Confirmar envía los mismos IDs y orden.

Pago/cambio mantiene Aceptar/Cambiar carta y espera el snapshot. Pasa permanece junto al grupo cuestionado. Consultas de manos/mazo siguen solo en Finished, también en empate. Revancha, contador y plazo aparecen al finalizar. Perfil compacto junto a la mano; Info conserva nombre, conexión, estado y mensajes completos. Menú incluye salida. Escape/clic exterior cierran los paneles.

## 7. Prueba de aceptación

Ejecutar compilación y tres consolas del README. `HandDragSmoke.tscn`, con render real, comprueba crear/quitar, cartas sin duplicado, marco sin recorte, edición, trasmoche/cancelación conjunta, Info/Menú, solicitud de publicación, snapshot intacto y navegación de nueve borradores incompletos.

Resoluciones: 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440. Se repiten Mesa dinámica, selección, resolución, ofertas/manos/mazo finales, denuncia y reparto. Resultados ejecutados en VERIFICACION del paquete.

## 8. Posibles errores

Reemplazar juntos Mesa, su escena y los nuevos scripts con .uid. La información completa está en Info. Muchas preparaciones se recorren con las flechas de Mi juego sin agrandar la página. Los contadores incluyen las preparadas hasta confirmación. El dorado identifica una preparación pendiente; las reglas de orden y sus consecuencias siguen vigentes.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil física pendiente.
SIGUIENTE FASE: la que indique el usuario.
