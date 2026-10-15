# Corrección de Salir de partida

## 1. Objetivo de la fase

Restaurar el clic en la salida de Mesa desde Menú, manteniendo Edición en mesa y el cierre de sesión existente.

## 2. Decisiones arquitectónicas

El fallo estaba en presentación: `MenuPanel`, añadido después de `TopBar`, recibía el input con MouseFilter Stop. El botón se dibujaba por delante gracias a ZIndex, pero eso no determina la prioridad del input. La reproducción nativa identificó MenuPanel bajo el cursor y la sesión local permaneció activa después del clic.

El fondo decorativo pasa a MouseFilter Ignore y TopBar se mueve al frente de sus hermanos al inicializar la interfaz. No se reemplaza el botón, su señal ni el contrato de sesión. Su ruta, tamaño y aspecto se conservan.

## 3. Dependencias

Godot 4.7.2 .NET y los componentes existentes. La documentación oficial de [Control](https://docs.godotengine.org/en/stable/classes/class_control.html) describe MouseFilter Ignore; [CanvasItem](https://docs.godotengine.org/en/stable/classes/class_canvasitem.html) aclara que ZIndex afecta al dibujo y no al input, y documenta MoveToFront. La compilación verifica los métodos C# del SDK instalado.

## 4. Implementación

| Ruta bajo `desmoche` | Cambio |
| --- | --- |
| `Scripts/Presentation/Mesa.TableEditing.cs` | Fondo de Menú ignora input; TopBar al frente para recibir clics sobre los controles de la mesa. |
| `Scripts/Diagnostics/SingleplayerSmoke.cs` | Abre Menú y pulsa Salir mediante input nativo, comprueba cierre real, limpieza y regreso a Singleplayer. |
| `Scripts/Diagnostics/HandDragSmoke.Table.cs` | Comprueba que Salir llama una vez a Close en las siete resoluciones, incluso con una solicitud pendiente. |
| `Docs/EdicionEnMesa.md`, README y CONTEXTO | Enlaces y descripción de esta corrección. |

## 5. Configuración Godot

No hay cambios manuales de Inspector ni nuevos nodos, señales o RPC. `Mesa.ContentPath + TopBar/LeaveButton` sigue conectado por Pressed a LeaveRoom. MenuPanel permanece como fondo; TopBar conserva el mismo padre y ruta. El orden se configura desde código.

## 6. Integración

LeaveRoom usa el IGameSession vinculado: cierra la partida local, la sala del host o la sesión del invitado según el modo existente. LocalGameSession borra la cola y su vista; Main vuelve a Singleplayer. Las consultas, borradores y reparto se limpian mediante los eventos existentes. La revancha comparte TopBar y conserva su funcionamiento.

No cambian las reglas, la autoridad, NetworkManager, Main ni protocolo 7. La prueba de llamada de cierre para una sesión de diagnóstico verifica la interfaz compartida; no sustituye una conexión LAN real.

## 7. Prueba de aceptación

La reproducción previa con render e input nativos falló: el cursor encontraba MenuPanel y no se cerraba la sesión. Con la corrección, SingleplayerSmoke recorre las doce configuraciones de dificultad/cantidad de bots y comprueba salida real. HandDragSmoke añade la llamada de cierre en 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440.

Ejecutar con F6 `Scenes/Diagnostics/SingleplayerSmoke.tscn` y `HandDragSmoke.tscn` usando render real; esperar sus mensajes PASS. Ejecutar también compilación y las tres consolas del README. Los resultados de esta entrega están en VERIFICACION del ZIP.

## 8. Posibles errores

Copiar el archivo completo Mesa.TableEditing.cs sobre la instalación que ya incluye Edición en mesa y recompilar. El ZIP acumulativo conserva todas las fuentes anteriores. Emisión directa de Pressed o comprobar solo la visibilidad no detecta este fallo; los diagnósticos nativos pulsan el botón y verifican su efecto. La aceptación en teléfonos y LAN entre dispositivos físicos sigue pendiente.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para esta entrega; aceptación táctil y LAN física pendientes.
SIGUIENTE FASE: la que indique el usuario.
