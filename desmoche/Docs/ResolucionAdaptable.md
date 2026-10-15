# Resolución adaptable — móvil y escritorio

> [EdicionEnMesa.md](EdicionEnMesa.md) reemplaza el pie con scroll por controles integrados en una pantalla, manteniendo escala uniforme, orientación y área segura.

## 1. Objetivo de la fase

Adaptar el juego a pantallas de escritorio, tabletas y teléfonos apaisados conservando la mesa casino, sus posiciones relativas, las cartas y la animación. Esta entrega modifica presentación y configuración de ventana; conserva reglas, autoridad y protocolo 5.

## 2. Decisiones arquitectónicas

La resolución de diseño pasa de 1100×800 a **1280×720**. `canvas_items` dibuja el contenido 2D a la resolución de destino; `expand` aprovecha otras proporciones sin deformación. No se reduce el render a una imagen de 720p. La mesa conserva su espacio virtual 2016×780 y escala uniforme. Se mantienen las texturas originales con sus mipmaps y el dibujo de palos e índices.

La orientación móvil elegida es **horizontal automática**: permite girar el teléfono hacia ambos lados sin convertir la mesa en una composición vertical. La ventana de escritorio sigue siendo redimensionable, con mínimo 640×360. Los tamaños físicos menores reducen inevitablemente el tamaño aparente; 640×360 es un mínimo de funcionamiento, no una promesa de igual legibilidad que un monitor grande.

Las acciones contextuales se distribuyen debajo del tablero mediante `HFlowContainer`, con altura mínima de 44 unidades de interfaz. No se reducen con la escala interna del tablero. Jugador propio, información, salida/revancha, borradores y revelaciones ocupan filas sucesivas. En anchos reducidos la información pasa debajo del jugador. El scroll permite acceder a contenido adicional sin recortarlo. Las filas de hasta diez cartas rivales conservan sus espacios separados.

## 3. Dependencias

Godot 4.7.2 .NET, proyecto y recursos existentes. No se añaden paquetes ni recursos gráficos. Referencias oficiales consultadas para la configuración y APIs: [Multiple resolutions](https://docs.godotengine.org/en/stable/tutorials/rendering/multiple_resolutions.html), [DisplayServer](https://docs.godotengine.org/en/stable/classes/class_displayserver.html) y [ProjectSettings](https://docs.godotengine.org/en/stable/classes/class_projectsettings.html). La compilación verifica las firmas con el SDK instalado del proyecto.

## 4. Implementación

| Archivo | Cambio |
| --- | --- |
| `project.godot` | Base 1280×720, orientación móvil horizontal automática y emulación de ratón desde toque explícita. |
| `Scripts/Presentation/Main.cs` y `Main.Responsive.cs` | Mínimo de ventana, márgenes seguros y reserva para teclado virtual móvil. |
| `Scripts/Presentation/Mesa.Casino.cs` | Escala uniforme; pie colocado según alturas reales; título de mano separado de las cartas. |
| `Scripts/Presentation/Mesa.Dynamic.cs` | Acciones en filas adaptables y botones mayores. |
| `Scenes/Mesa.tscn` | Barra de salida/revancha con ajuste de filas. |
| `Scenes/MenuPrincipal.tscn` y `Scripts/Presentation/StartupMenu.cs` | Scroll en ayuda y ajustes; ayuda comienza en su primera línea. |
| `Scripts/Diagnostics/MenuNavigationSmoke.cs` | Rutas actualizadas de los paneles. |
| `Scripts/Diagnostics/ResponsiveLayoutSmoke.cs` y su escena | Comprobación con ventana real, stretch, geometría, ayuda y revancha en siete resoluciones. |

Los márgenes se recalculan al cambiar el viewport. Solo en Android/iOS se consulta además cada 0,25 segundos el área segura y el teclado: un giro de 180° puede cambiar el lado de la cámara sin cambiar las dimensiones. Se transforman los píxeles de pantalla al espacio lógico; no se añaden márgenes si su valor sigue igual.

## 5. Configuración Godot

- Display → Window → Size: viewport 1280×720 y ventana inicial 1280×720.
- Stretch: `canvas_items`, `expand`.
- Handheld → Orientation: `Sensor Landscape` (4).
- Input Devices → Pointing → Emulate Mouse From Touch: activado.
- Se conserva el renderer y la importación de los recursos existentes.

Cambios de árbol: `Mesa/Scroll/Padding/Content/TopBar` pasa a `HFlowContainer`. El nodo dinámico `ContextActions` también es `HFlowContainer`; mantiene los nombres y acciones de sus botones. Ayuda y ajustes quedan en `MenuPrincipal/<HelpOverlay o SettingsOverlay>/Scroll/Center/Panel/Padding/Content`. `Scroll` ocupa el panel completo, deshabilita desplazamiento horizontal y sigue el foco. Los demás nodos y rutas de sesión/red se conservan.

No requiere conexiones manuales en el Inspector. `Viewport.SizeChanged` actualiza márgenes; `Resized` y `MinimumSizeChanged` solicitan la redistribución diferida de Mesa. Se desconectan al salir. No se añaden RPC ni datos privados.

## 6. Integración

Main continúa cargando las mismas pantallas y conserva `/root/Main/NetworkManager`. Mesa sigue consumiendo `IGameSession`. Redimensionar modifica geometría; no reconstruye la mano ni reinicia el reparto. Las cartas en vuelo mantienen destinos en el tablero virtual compartido. Se conservan la optimización de cierre, los controles de cambio/pago, los plazos y las reglas anteriores.

El toque usa la emulación de ratón de Godot para los controles y gestos existentes. Menú, lobby y Singleplayer conservan sus formularios con scroll; el nuevo margen inferior deja espacio al teclado móvil.

## 7. Prueba de aceptación

La verificación automática y su alcance están en `VERIFICACION.md` del paquete. Incluye compilación, consolas de dominio/interacción/bots, diagnóstico de resolución con render real, arrastre nativo, navegación, reparto y comprobación de cierre sin reconstrucción de mano.

Resoluciones: **640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440**. El diagnóstico cambia la ventana real, por lo que incluye la transformación configurada en `project.godot`. Inspecciona 9/7/2 y 10/10/10 cartas rivales, mano de nueve, oferta y pozo, ausencia de solapamientos, ayuda desplazable y revancha con contador. Son vistas sintéticas para geometría; no certifican la legalidad de sus combinaciones.

Para repetir después de compilar, ejecuta con Godot .NET `res://Scenes/Diagnostics/ResponsiveLayoutSmoke.tscn`. Con render puede recibir `-- --capture=RUTA_ABSOLUTA`; termina con `RESPONSIVE LAYOUT PASS`. Los demás comandos están en el README.

Aceptación manual pendiente en dispositivo físico: girar el teléfono en ambas orientaciones horizontales, verificar cámara/barras, abrir teclado en el nombre, tocar y arrastrar cartas, y completar una partida. No se generó ni instaló un APK/IPA durante esta revisión.

## 8. Posibles errores

Si se conserva el encuadre antiguo, comprueba que se reemplazó también `project.godot`. En el editor, prueba una ventana independiente o el escalado Stretch to Fit del juego incrustado. Si el pie ocupa más alto por un texto o resultado extenso, desplázate hacia abajo: no se oculta ni se comprime sobre las cartas. En teléfonos pequeños, usa la orientación horizontal prevista. Las texturas tienen su resolución original; este cambio no inventa detalle adicional al ampliarlas.

## 9. Estado de la fase

FASE COMPLETADA: SÍ
BLOQUEADORES: ninguno para la adaptación de interfaz; queda pendiente la aceptación en dispositivos físicos y su exportación.
SIGUIENTE FASE: la que indique el usuario.
