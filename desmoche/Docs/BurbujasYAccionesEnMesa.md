# Burbujas de tiempo y acciones en mesa

## 1. Objetivo de la fase

Mostrar los plazos de Pasa, elección y preparación en pequeños círculos dentro de la mesa. Situar las acciones junto a su carta, casilla o juego y retirar la fila de botones bajo la mano.

## 2. Decisiones arquitectónicas

La presentación reutiliza los botones, rutas, señales y solicitudes existentes. ContextActions pasa a ocupar la pantalla como capa sin captura de input en los espacios vacíos; solo sus botones reciben los clics. Sus posiciones se calculan en unidades de interfaz, alrededor del elemento correspondiente y evitando cartas, textos y otros controles. La escala del tablero permanece estable al seleccionar.

MesaActionClock dibuja fondo oscuro, aro dorado y segundos. El aro se vacía y cambia a ámbar en los últimos tres segundos. La burbuja ignora el input, conserva 44 unidades de diámetro y se oculta al terminar su contexto. No reconstruye cartas ni prepara solicitudes.

Los milisegundos proceden de la vista autoritativa. Se descuenta el tiempo local transcurrido desde su recepción y se resincroniza con cada revisión. Llegar a cero deshabilita los controles ya sujetos al plazo; el host conserva los vencimientos, rechazos y juego automático existentes. No cambian dominio, red ni protocolo 7.

## 3. Dependencias

Godot 4.7.2 .NET y las vistas OfferTiming, DiscardTiming y OrderChallenge existentes. Dibujo verificado con [CanvasItem](https://docs.godotengine.org/en/stable/classes/class_canvasitem.html), mediante DrawCircle, DrawArc y QueueRedraw. Las transformaciones e input usan [Control](https://docs.godotengine.org/en/stable/classes/class_control.html). No se añaden texturas ni paquetes externos.

## 4. Implementación

| Ruta bajo `desmoche` | Responsabilidad |
| --- | --- |
| `Scripts/Presentation/MesaActionClock.cs` y `.uid` | Círculo, número y progreso del tiempo restante. |
| `Scripts/Presentation/Mesa.TableActions.cs` y `.uid` | Agrupación, colocación, protección de cartas/textos y sincronización visual de los tres círculos. |
| `Mesa.Countdowns.cs` | Conserva cálculo y deshabilitación existentes; traslada los plazos visibles a las burbujas. |
| `Mesa.Casino.cs`, `Mesa.TableEditing.cs`, `Mesa.Dynamic.cs`, `Mesa.cs` | Retiran el pie, inicializan componentes y mantienen información completa en Info y acciones contextuales. Los textos compactos de los jugadores evitan alturas transitorias por wrapping. |
| `Scripts/Diagnostics/TableActionsSmoke.cs`, `.uid` y escena | Input nativo, cambio de fases, precisión contextual, geometría, vencimiento visual y ausencia de escrituras al motor. |
| Diagnósticos de arrastre y resolución | Aceptación de los círculos y controles dentro de la mesa. |

## 5. Configuración Godot

Sin cambios manuales de Inspector, conexiones nuevas de red ni RPC. Las rutas de ContextActions y PasaButton se conservan. Bajo Mesa.ContentPath se añaden:

| Nodo | Contexto |
| --- | --- |
| `ChoiceClock/Seconds` | Cinco segundos junto a Tomar carta o Tomar pozo. |
| `PreparationClock/Seconds` | Veinte segundos junto a Confirmar juego, o en Mi juego mientras todavía no hay grupo preparado. |
| `PasaClock/Seconds` | Tres segundos junto a Pasa y al juego cuestionado. El infractor puede ver el plazo sin disponer del botón. |

CardCountdownLabel permanece oculto para conservar su información en Info. El contador de revancha mantiene su ubicación existente. MenuPanel sigue ignorando input y TopBar continúa al frente, preservando la corrección de salida.

## 6. Integración

Tomar y Rechazar aparecen junto a la carta ofrecida mientras la fase los permite. Elegir el pozo aparece junto a su carta; Del mazo acompaña al mazo. Aceptar y Cambiar carta quedan junto a Pago/Cambio inicial después de preparar una carta. Finalizar pago utiliza esa misma zona cuando corresponde.

Preparar, Ampliar, Confirmar juegos y Confirmar juego con oferta/pozo están en la zona de Mi juego. Color, Peladilla, Desmoche y Continuar usan esa zona en su contexto inicial. Se preservan las solicitudes, selección verde, orden local, trasmoche atómico, pago preparado, Pasa, consultas finales, reparto y revancha.

La colocación se adapta al espacio disponible: junto al elemento, encima/debajo o en su zona libre más cercana. Las burbujas permanecen al lado de su botón. El texto y las cartas seleccionadas se consideran al colocar controles. Ya no se reserva una fila bajo la mano.

## 7. Prueba de aceptación

Ejecutar TableActionsSmoke.tscn con F6 y render real; esperar TABLE ACTIONS PASS. Verifica círculos, cercanía a la carta, clics de elección/reclamo/Pasa/pozo/pago, ausencia de solapes, tamaño mínimo, cambio de contexto y cero sin solicitudes automáticas.

Resoluciones: 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440. ResponsiveLayoutSmoke incluye también el stretch real de la ventana. Repetir los diagnósticos de ofertas, arrastre, reparto, Mesa y Singleplayer, y los comandos de compilación/consola del README. VERIFICACION del paquete registra los resultados ejecutados.

## 8. Posibles errores

Copiar juntos los scripts completos y sus .uid. Los textos de tiempo completos están en Info; las burbujas muestran el tiempo restante aproximado de la vista recibida. El círculo puede llegar a cero antes del siguiente snapshot, pero no resuelve la oportunidad por su cuenta. Las pruebas con adaptador verifican la interfaz; las consolas y la denuncia real comprueban la autoridad. La aceptación táctil física sigue pendiente.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil física pendiente.
SIGUIENTE FASE: la que indique el usuario.
