# Fase 9 — Cartas, mano y Drag & Drop

## 1. Objetivo de la fase

Mostrar cartas, seleccionar varias, cambiar el orden local de la mano, arrastrar y soltar, preparar combinaciones y solicitar publicación, reclamo o pago al servidor. Se mantienen los diseños de fase 8 como PROVISIONALES.

El paquete contiene archivos completos para copiar sobre las fases anteriores aprobadas. No es un proyecto independiente. No modifica el dominio, GameManager, reglas, protocolo, RPC, Main ni Lobby. No incluye animaciones ni exportación móvil.

## 2. Decisiones arquitectónicas

Se utiliza `Carta : PanelContainer`, que hereda `Control`. Los controles pertenecen a contenedores de UI y usan el arrastre nativo de Godot. `Area2D` introduciría física y coordenadas adicionales sin necesidad. `TextureRect` sigue reservado a las superficies; el arte vectorial existente permanece en `CartaArtwork : Control`.

`Desmoche.Domain.Card` es el dato inmutable de una carta. `Desmoche.Presentation.Carta` representa una identidad autorizada, cara/dorso/vacío y gestos. No posee una mano, partida ni autoridad para mover cartas del juego. La referencia de dominio que usa para dibujar rango/palo no permite cambiar GameState.

`HandInteractionState` mantiene únicamente orden, selección y borradores locales. No ejecuta RulesValidator, GameManager ni RPC. Copia los datos que necesita de OwnHand y los juegos propios públicos. Los borradores pueden ser inválidos: solamente el servidor determina su validez. No se ordenan automáticamente las escaleras; se conserva el orden declarado, ascendente o descendente según las reglas acordadas.

`InteractionActions` habilita acciones y verifica forma y pertenencia visibles. Estas comprobaciones ayudan a preparar solicitudes; no reemplazan las comprobaciones del servidor. Algunas restricciones internas, como el segundo robo obligatorio de apertura, no aparecen en SnapshotDto y pueden provocar un rechazo del servidor.

El arrastre transporta un Dictionary local con tipo, sesión, revisión, ventana, destinatario, origen, índice de borrador e ID de carta. Nunca se envía este Dictionary por RPC. Se rechazan tokens antiguos, de otra vista, con tipos incorrectos o cartas no disponibles. Este filtro evita gestos obsoletos; la seguridad continúa en el servidor.

El original nunca se mueve de nodo ni se retira durante el gesto. Godot administra y libera una previsualización independiente. Soltar fuera de un destino válido deja todo igual. Antes de reconstruir una vista se cancela el arrastre activo.

Cada jugador puede tener una solicitud de UI pendiente. Hasta recibir confirmación se bloquean acciones y edición local. La respuesta se correlaciona por RequestId y sesión; no actualiza manos ni pozo. La vista autoritativa actualiza la mesa. Tras diez segundos sin confirmación se muestra un aviso y se desbloquean los controles, sin reintento automático. Una actualización posterior sigue siendo válida.

## 3. Dependencias

Godot 4.7.2 .NET, SDK .NET instalado, fases 0–8 y su NetworkManager con `View`, `Submit`, `ViewChanged`, `ConnectionChanged` y `ActionCompleted`. El núcleo y los DTO existentes se conservan.

Las nuevas pruebas de consola usan net8.0 y enlazan los fuentes del dominio, Application, Networking/Core e Interaction. No necesitan paquetes de pruebas ni Godot. Deben instalarse fuera de `res://`, como carpeta hermana de `desmoche`, para evitar que sus fuentes y Program.cs se compilen dentro del juego.

## 4. Implementación

Todos los archivos están completos en el paquete. Rutas relativas a `res://`:

| RUTA | ARCHIVO / clase | Responsabilidad |
|---|---|---|
| Scripts/Presentation/ | Carta.cs — Carta : PanelContainer | Señal de selección, foco, input, cara/dorso/vacío y preview |
| Scripts/Presentation/ | CartaArtwork.cs — CartaArtwork : Control | Arte provisional conservado |
| Scripts/Presentation/ | CardDropZone.cs — CardDropZone : PanelContainer | Destino de drop con permiso y callback locales |
| Scripts/Presentation/ | CardDragData.cs — CardDragData, LocalCardDrag, CardDragOrigin | Token local y lectura estricta |
| Scripts/Presentation/Interaction/ | HandInteractionState.cs — HandInteractionState, VisualGroupDraft | Selección múltiple, orden, borradores y reconciliación |
| Scripts/Presentation/Interaction/ | InteractionActions.cs — InteractionActions, VisualActionRequest | Preparación de solicitudes a partir de datos visibles |
| Scripts/Presentation/ | Mesa.cs — Mesa : Control | Representación del snapshot y vínculo con networking |
| Scripts/Presentation/ | Mesa.Interaction.cs — otra parte de Mesa | Controles, drag, borradores, envío y respuesta |
| Scenes/ | Carta.tscn | Carta reutilizable |
| Scenes/ | Mesa.tscn | Mesa provisional con controles y destinos |
| Scripts/Diagnostics/ y Scenes/Diagnostics/ | CardInputSmoke.cs / CardInputSmoke.tscn | Input y callbacks aislados |
| Scripts/Diagnostics/ y Scenes/Diagnostics/ | MesaInteractionSmoke.cs / MesaInteractionSmoke.tscn | Integración de gestos y borradores con vistas aisladas |
| Scripts/Diagnostics/ y Scenes/Diagnostics/ | MesaVisualSmoke.cs / MesaVisualSmoke.tscn | Regresión de visualización y privacidad de fase 8 |
| Scripts/Diagnostics/ | LobbySmoke.cs | Diagnóstico compatible del lobby |
| Assets/Table/ | Wood.svg, Felt.svg, Tray.svg | Diseños provisionales conservados |
| Docs/ | Fase9.md, Fase9.puml | Este contrato y diagrama |

Namespaces: Presentation y Interaction como indica la carpeta; diagnósticos en `Desmoche.Diagnostics`. Los archivos Mesa.cs y Mesa.Interaction.cs declaran la misma clase parcial: solamente Mesa.cs se adjunta a la escena.

Fuera de `res://` se entregan:

- `Desmoche.Interaccion.Pruebas/Pruebas.csproj`, `Program.cs` y `ActionInteractionTests.cs`: 296 comprobaciones.
- `Desmoche.Pruebas/RunCardInteraction.ps1`: compilación, consola y tres diagnósticos Godot.
- `Desmoche.Pruebas/RunMesaVisual.ps1`: se conserva el diagnóstico anterior.
- `LEEME.md` y `VERIFICACION.md`: instalación y evidencia.

## 5. Configuración Godot

No crear nodos a mano si copias las escenas entregadas. No renombrar rutas. No adjuntar estos scripts del dominio o Interaction a nodos. No añadir autoloads.

```text
Carta (PanelContainer) [Carta.cs] mínimo 76×108
└── Padding (MarginContainer)
    └── Content (Control)
        ├── Artwork (Control) [CartaArtwork.cs]
        ├── Corner (Control)
        │   ├── Rank (Label)
        │   └── Suit (Label)
        ├── ReverseCorner (Control)
        │   ├── Rank (Label)
        │   └── Suit (Label)
        ├── Symbol (Label)
        └── Caption (Label)

Main (existente, sin cambios)
├── NetworkManager (Node, persistente)
└── Margin/Screens
    ├── Lobby (existente)
    └── Mesa (Control) [Mesa.cs]
        ├── Background (TextureRect)
        └── Scroll/Padding/Content (VBoxContainer)
            ├── TopBar/LeaveButton (Button)
            ├── Header (VBoxContainer)
            ├── PlayerZone/Padding/Players (HFlowContainer)
            ├── Table/Padding/Surface (VBoxContainer)
            │   ├── OpponentGames (HFlowContainer)
            │   ├── Center (HFlowContainer)
            │   │   ├── Mazo/Content (VBoxContainer)
            │   │   │   ├── TitleLabel (Label)
            │   │   │   ├── Carta (instancia Carta.tscn)
            │   │   │   └── CountLabel (Label)
            │   │   ├── Pozo (PanelContainer) [CardDropZone.cs]
            │   │   │   └── Content (VBoxContainer)
            │   │   │       ├── TitleLabel (Label)
            │   │   │       ├── Carta (instancia Carta.tscn)
            │   │   │       └── CountLabel (Label)
            │   │   └── Oferta/Content (VBoxContainer)
            │   │       ├── TitleLabel (Label)
            │   │       ├── Carta (instancia Carta.tscn)
            │   │       └── CountLabel (Label)
            │   └── MyGame (VBoxContainer)
            │       ├── TitleLabel (Label)
            │       ├── EmptyLabel (Label)
            │       └── Rows (VBoxContainer, juegos propios publicados)
            ├── HandZone/Padding/Content (VBoxContainer)
            │   ├── HandHeader (HBoxContainer)
            │   │   ├── SelfPlayers (HFlowContainer)
            │   │   └── TitleLabel (Label)
            │   ├── Cards (HFlowContainer, cartas propias dinámicas)
            │   ├── EmptyLabel (Label)
            │   └── Interaction (VBoxContainer)
            │       ├── HintLabel (Label)
            │       ├── SelectionLabel (Label)
            │       ├── LocalActions (HFlowContainer)
            │       │   ├── TargetGame (OptionButton)
            │       │   ├── PrepareButton (Button)
            │       │   ├── ClearSelectionButton (Button)
            │       │   ├── ClearDraftsButton (Button)
            │       │   ├── LeftButton (Button)
            │       │   ├── RightButton (Button)
            │       │   └── ExternalSelect (CheckButton)
            │       ├── DraftZone (PanelContainer) [CardDropZone.cs]
            │       │   └── Content (VBoxContainer)
            │       │       ├── TitleLabel (Label)
            │       │       ├── EmptyLabel (Label)
            │       │       └── Rows (VBoxContainer, borradores dinámicos)
            │       ├── NetworkActions (HFlowContainer)
            │       │   ├── ExchangeButton (Button)
            │       │   ├── ContinueButton (Button)
            │       │   ├── DrawDeckButton (Button)
            │       │   ├── PassButton (Button)
            │       │   ├── PublishButton (Button)
            │       │   ├── ClaimButton (Button)
            │       │   ├── DrawDiscardButton (Button)
            │       │   ├── DesmocheButton (Button)
            │       │   ├── PayButton (Button)
            │       │   ├── NoPaymentButton (Button)
            │       │   └── DeclareWin (CheckButton)
            │       └── MessageLabel (Label)
            └── RevealedZone/Rows (VBoxContainer)
```

Las ramas de fondo Felt/Tray y los contenedores Padding de fase 8 se conservan en Mesa.tscn; el árbol anterior detalla las rutas usadas por los scripts. Cada borrador genera `BorradorN/Header` con título y Quitar, y `BorradorN/Cards` con instancias Carta.

Inspector: Carta raíz tiene Mouse Filter Ignore y Focus None por defecto; Mesa cambia a Stop y All solamente para cartas propias/borradores o carta pública seleccionable. Los descendientes visuales ignoran el ratón. Pozo y DraftZone reciben drops; sus contenedores de contenido usan Pass y las etiquetas Ignore. Cards tiene mínimo vertical 112 y HFlow con separación 8. Los callbacks se asignan desde Mesa en Ready; no se conectan manualmente en el editor.

Señales/input:

- `Carta.SelectionRequested(int cardId)` se emite al soltar un click izquierdo dentro de la carta o al pulsar Espacio/Enter sin repetición. Un release de arrastre no se trata como click.
- Cada click alterna la selección de esa carta; no requiere Ctrl. Tab permite enfocar cartas propias. Los botones izquierda/derecha mueven una carta seleccionada.
- `_GetDragData`, `_CanDropData`, `_DropData` y `NotificationDragEnd` gestionan el gesto nativo. `SetDragForwarding` permite soltar en los huecos finales de los contenedores.
- Botones usan Pressed; los CheckButton usan Toggled. `SetPressedNoSignal` evita respuestas recursivas al pintar una vista.
- `ViewChanged`, `ConnectionChanged` y `ActionCompleted` se suscriben en Bind y se retiran en Unbind/ExitTree.

Los nodos visuales no añaden RPC ni MultiplayerSynchronizer. Se deja su autoridad heredada por defecto; no se concede autoridad de partida al cliente. El servidor/peer 1 conserva el estado verdadero y el networking aprobado sigue transmitiendo vistas por destinatario.

## 6. Integración

Instalación exacta:

1. Detén F5 y cierra Godot. Guarda una copia de tu proyecto.
2. Descomprime Fase9.zip. Copia el CONTENIDO de su carpeta `desmoche` en `C:\Users\ricar\Documents\desmoche`, combinando carpetas y sustituyendo coincidencias. No copies una segunda carpeta desmoche dentro de la primera.
3. Copia la carpeta `Desmoche.Interaccion.Pruebas` completa en `C:\Users\ricar\Documents\Desmoche.Interaccion.Pruebas`. Debe quedar junto a desmoche. No sustituye tus pruebas anteriores.
4. Copia los dos .ps1 del paquete en `C:\Users\ricar\Documents\Desmoche.Pruebas`, conservando todos los .cs/.csproj existentes.
5. Abre Godot, deja finalizar la importación y compila. Main sigue como escena principal; no cambies project.godot.

```powershell
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
dotnet run --project "C:\Users\ricar\Documents\Desmoche.Interaccion.Pruebas\Pruebas.csproj"
```

Flujo de acción: gesto → selección/borrador local → InteractionActions → `NetworkManager.Submit` → CommandDto existente → RPC/cola local de host → identidad real del remitente y validación de ServerSession/GameManager/RulesValidator → nuevas vistas pública/privada → NetworkManager.View → Mesa. La respuesta correlacionada muestra el resultado y libera los controles. Rechazar no retira cartas ni adelanta el turno localmente.

Publicar envía únicamente `PlayDto[]` con IDs ordenados y MeldId opcional. Para ampliar un juego propio se incluyen todas sus cartas anteriores y las nuevas, tal como exige el motor. Claim y DrawDiscard deben incluir exactamente una vez la carta pública en un borrador. Pay y ExchangeCard envían un ID propio. Declarar victoria utiliza el indicador existente; no garantiza victoria y una declaración incorrecta puede eliminar al jugador.

Las manos rivales y el orden/identidades del mazo nunca se incorporan a selección, borradores o payloads locales. Solamente se muestran juegos publicados, cartas públicas y revelaciones que el servidor autoriza al finalizar. Al cerrar o cambiar sesión se borran mano visual, selección y borradores. Las revisiones ajenas conservan el orden local; los cambios de ventana, fase, mano, juegos propios, oferta, respuesta o elegibilidad invalidan los borradores correspondientes.

Uso:

1. Intercambio: selecciona una carta y pulsa Entregar seleccionada a la derecha. Espera al servidor. Cada jugador pulsa Continuar sin victoria inicial para comenzar una partida normal.
2. Ordenar: arrastra una carta propia sobre la mitad izquierda/derecha de otra, o al hueco final de la mano. No se envía comando.
3. Preparar: selecciona varias cartas; elige Juego nuevo o Ampliar mi juego N y pulsa Preparar combinación. También puedes soltar una carta de la selección sobre el área de borradores. El orden inicial sigue el orden visual de la mano; la carta pública se agrega al final.
4. Reordena el borrador arrastrando sus cartas si la escalera requiere otro orden. Quitar elimina solamente el borrador. No se intercambian directamente cartas entre dos borradores: quita y prepara nuevamente los grupos.
5. Publica los borradores propios. Para una oferta o pozo selecciona también esa carta pública (click o Incluir oferta/pozo), prepara el juego y usa Reclamar oferta/Tomar pozo. Si no quieres la oferta pulsa No reclamar.
6. En tu pago arrastra una carta propia al Pozo o selecciona una y pulsa Pagar seleccionada. La carta permanece hasta confirmación. El motor puede ofrecer ese pago inmediatamente a otros jugadores; no necesariamente quedará arriba del pozo.
7. DeclareWin y Declarar desmoche solicitan evaluación de victoria con exactamente diez cartas combinadas y ninguna suelta según el contrato anterior. Úsalos únicamente cuando corresponda. Completar sin pago aparece cuando el servidor tiene mano propia vacía en fase de pago y decide su efecto.

Esta fase incorpora los controles mínimos para llegar a las jugadas de cartas. El motor conserva DeclareInitial y Transfer de fases anteriores; no se añade aquí una UI completa para victorias iniciales especiales o transferencia entre juegos de distintos jugadores. El botón Continuar inicia una partida normal. No se redefine ninguna de esas reglas.

## 7. Prueba de aceptación

Prueba A — sin Godot: ejecuta el segundo comando de instalación. Esperado: `PRUEBAS INTERACCION COMPLETADAS: 296 comprobaciones correctas.` Incluye orden/selección, datos privados, copias defensivas, reconciliación, forma de solicitudes y validación real del motor/ServerSession: pagos, prioridad, ventanas antiguas y penalizaciones.

Prueba B — diagnósticos automáticos Godot. Ajusta GodotPath a TU ejecutable .NET console:

```powershell
cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
.\RunCardInteraction.ps1 -GodotPath "C:\ruta\Godot_v4.7.2-stable_mono_win64_console.exe"
```

Esperado: compilación correcta, 296 comprobaciones, `OK: Carta`, `OK: InteraccionMesa`, `OK: PrivacidadMesa`, y PRUEBAS FASE 9 COMPLETADAS. Los registros quedan en TestResults/Fase9-fecha. Estos diagnósticos comprueban callbacks y eventos sintéticos; el gesto nativo real se verifica en C.

Prueba C — F6 manual:

1. Abre Scenes/Diagnostics/CardInputSmoke.tscn y pulsa F6. Click, Enter y Espacio alternan selección; varias cartas pueden estar seleccionadas. Arrastra una carta a otra o a la zona. Preview sigue al cursor; el original queda intacto; desactivar permiso o soltar fuera no produce un drop aceptado. Cartas públicas, dorso y vacío permanecen pasivos.
2. Abre Scenes/Diagnostics/MesaInteractionSmoke.tscn y pulsa F6. Ordena y selecciona la mano, prepara varios grupos, reordena dentro de ellos, incluye la oferta y quita un grupo. La mano conserva todos sus IDs. Los botones de red y el pago están deshabilitados porque este diagnóstico está desconectado.
3. Abre Scenes/Diagnostics/MesaVisualSmoke.tscn y pulsa F6. Revisa los doce estados, privacidad, salto de filas y scroll en ventanas amplias y estrechas. No se deben revelar manos rivales durante la partida.

Prueba D — F5 con 2, después 3 y 4 jugadores en la LAN:

- Instala la fase en TODOS los dispositivos. Crea sala y conecta mediante la IP local; inicia la partida. Todos entregan una carta y continúan.
- Ordena y prepara cartas en un cliente: ningún rival observa esas cartas ni cambios en el mazo/turno. Publica un juego válido y comprueba que aparece en todos solamente al confirmarse.
- Muestra una carta del mazo. Reclámala con un juego válido que la incluya, o pulsa No reclamar en todos. Comprueba oferta, prioridad, turno y pozo en todos los dispositivos.
- Durante el pago del reclamante, suelta una carta propia en Pozo: se envía un pago y la carta desaparece solamente al recibirse el estado. Repite con el botón Pagar seleccionada.
- Intenta soltar en Pozo fuera de tu pago: ningún comando ni cambio. Suelta en una zona ajena: mano intacta. Mientras hay una solicitud pendiente no se puede enviar otra desde la UI.
- Recibe una oferta/nueva ventana mientras arrastras: se cancela el gesto y no se envía el token anterior. En caso de rechazo permanece el estado autoritativo y se muestra el mensaje.
- Desconecta un cliente: el motor conserva su mano para pagos automáticos y su UI no puede jugar. Cierra el host: todos vuelven al lobby y desaparecen identidades privadas. Una nueva sala no conserva borradores de la anterior.
- Comprueba victoria con diez combinadas y sin cartas sueltas mediante una preparación legal, siguiendo las pruebas del motor. El resultado aparece a todos después de la decisión del servidor.

No hay que repetir declaraciones incorrectas en una partida que quieras conservar: las penalizaciones previstas se verifican en las pruebas de consola y sesiones de prueba.

## 8. Posibles errores

| Síntoma | Diagnóstico y solución mínima |
|---|---|
| Node not found o cast Pozo | Copiar también Mesa.tscn nueva; Pozo ahora es PanelContainer con CardDropZone. No mezclar script nuevo y escena anterior. |
| No encuentra InteractionActions/Carta señal | Copiar todos los .cs, incluida la carpeta Interaction y Mesa.Interaction.cs; recompilar. |
| Program duplicado o clases de pruebas dentro de Godot | Sacar Desmoche.Interaccion.Pruebas fuera de res://. Debe ser carpeta hermana. |
| Pruebas no encuentran Domain/Application | Revisar carpetas hermanas; para otra instalación pasar `-p:GameSourceRoot=C:\ruta\desmoche`. |
| No prepara grupo | Se requieren al menos tres cartas para juego nuevo; no repetir cartas asignadas. Para ampliar, seleccionar cartas nuevas y un juego propio. |
| No acepta un drop | Verificar fase, conexión, origen, selección y vista actual. Un token de una revisión antigua se rechaza deliberadamente. |
| Escalera rechazada o eliminada | Revisar el ORDEN del borrador antes de enviarlo; la UI no autocorrige reglas ni asegura validez. |
| Pago aún no aparece en pozo | Puede ser una oferta pendiente. Revisar estado/turno y respuesta, no asumir fallo por posición visual. |
| Acción oculta/deshabilitada | Verificar fase, destinatario, respuesta pendiente, eliminación o solicitud en curso. En fixtures offline siempre se deshabilita networking. |
| Diez segundos sin confirmación | Leer MessageLabel/registro; comprobar host y red. No se reintenta automáticamente. Una respuesta tardía puede haber aplicado la acción: revisar primero la vista. |
| Engine assemblies not found | Usar Godot .NET y compilar el proyecto antes de ejecutar los diagnósticos. |
| Error de archivos temporales/acceso denegado | Revisar el registro de Windows Security como en fases anteriores. No desactivar protección global ni cambiar el código del juego por este error. |

Referencias oficiales usadas: [Control y Drag & Drop](https://docs.godotengine.org/en/stable/classes/class_control.html), [Viewport: cancelación y estado del drag](https://docs.godotengine.org/en/stable/classes/class_viewport.html). Los scripts se compilaron contra GodotSharp y sus generadores 4.7.2.

## 9. Estado de la fase

Implementación y 296 comprobaciones de consola completadas. Compilación de todos los scripts contra GodotSharp/SourceGenerators 4.7.2 correcta, nullable habilitado y advertencias como errores. Auditoría independiente del circuito de input, tokens, privacidad, pagos y respuestas completada.

No se afirma que los diagnósticos Godot ni el arrastre real/LAN hayan pasado en el entorno del asistente: la ejecución .NET completa no pudo verificarse aquí. Los archivos de diagnóstico y procedimientos están entregados para ejecutarlos en tu instalación.

FASE COMPLETADA: NO, pendiente de aceptación visual y LAN.

BLOQUEADORES: confirmar las pruebas B, C y D en Godot/dispositivos reales.

SIGUIENTE FASE: la que indiques después de aprobar esta; no implementada.
