> Revisión posterior 12/10/2026: [VictoriasVisiblesYPagos.md](VictoriasVisiblesYPagos.md) actualiza el plazo y ubicación de Pasa, muestra la evidencia de victoria y excluye al pagador de su oferta. Protocolo actual: 4.

> Actualización 12/10/2026: las reglas de victoria, intercambio, Pasa y acceso por código se detallan en [RevisionReglasCodigoSala.md](RevisionReglasCodigoSala.md). Esa revisión prevalece sobre las instrucciones históricas de este documento.

# Mesa dinámica — contrato de presentación de las fases 8 y 9

## 1. Objetivo de la fase

Corregir la distribución de la mesa y reducir los controles permanentes. La oferta se muestra a la derecha del pozo; las acciones correspondientes aparecen junto a ella. El panel propio conserva un tamaño legible y deja de superponerse al encabezado.

La mano se ordena mediante arrastre. La zona vacía de **Mi juego** permite preparar un juego nuevo y los juegos propios publicados sirven como destinos para ampliaciones. El pago y el cambio inicial utilizan una casilla con confirmación. Se conservan los materiales y las imágenes anteriores.

## 2. Decisiones arquitectónicas

La interfaz permanece basada en `Control`, `Carta.tscn` y Drag & Drop nativo. `Carta` representa una identidad recibida en la vista; `Card` sigue siendo un objeto del dominio. Un gesto modifica orden, selección o borradores locales, nunca el `GameManager`.

La composición panorámica se escala uniformemente y reserva espacio para el pie en ventanas anchas. El centro usa una fila horizontal sin salto: mazo, pozo y oferta o casilla contextual. Los controles a su derecha compensan la escala para conservar texto legible. El panel propio y los mensajes ocupan el pie, antes de los borradores. `Scroll.follow_focus` queda desactivado para que recibir foco no desplace automáticamente la vista.

`HandInteractionState.StageCards` permite borradores locales de **1–13 cartas**. Este tamaño es una capacidad de preparación, no una nueva regla. Al enviar se rechazan grupos incompletos y el servidor valida las reglas definitivas. No se ordenan escaleras automáticamente: se conserva el orden ascendente o descendente declarado; una selección desordenada sigue siendo rechazable.

Un drop sobre un juego propio conserva sus cartas publicadas y prepara una ampliación. Una carta nueva del borrador puede volver a la mano para deshacer su preparación. No se permite retirar así las cartas ya publicadas. Cada carta solo puede estar asignada a un borrador local.

**Pago/Cambio inicial** prepara una carta propia; el drop no envía una solicitud. **Aceptar** envía la acción existente. La carta permanece en la mano autorizada hasta el nuevo snapshot. La preparación se invalida si cambia su contexto o la disponibilidad de la carta. La declaración de victoria sigue siendo explícita.

Los nodos anteriores de botones se conservan ocultos para compatibilidad de rutas. La interfaz visible utiliza destinos y controles contextuales; no requiere el desplegable de crear/ampliar ni botones para mover a derecha o izquierda.

## 3. Dependencias

Proyecto con fases 0–9 y networking aprobados, Godot **4.7.2 .NET**, SDK .NET instalado, DTO y solicitudes existentes. Esta entrega actualiza archivos completos; conserva `Main`, `Lobby`, `.csproj` y `project.godot`.

Se mantienen los recursos `CasinoTable.png`, `Portraits.png`, `Courts.png`, sus importaciones y la máscara de retrato. Los contadores usan `OwnHand`, `CardCount`, `DeckCount` y `DiscardCount`, sin deducir manos por resta de juegos.

APIs consultadas: [Control y Drag & Drop](https://docs.godotengine.org/en/stable/classes/class_control.html) y [BoxContainer](https://docs.godotengine.org/en/stable/classes/class_boxcontainer.html). La compilación y ejecución concretas se documentan en `VERIFICACION.md` del paquete.

## 4. Implementación

Los archivos completos del paquete sustituyen sus equivalentes. Estas rutas son internas al proyecto:

| Ruta | Clase y responsabilidad |
| --- | --- |
| `res://Scenes/Mesa.tscn` | Árbol y composición; centro sin salto de fila |
| `res://Scripts/Presentation/Mesa.cs` | `Desmoche.Presentation.Mesa : Control`; representar snapshots autorizados |
| `res://Scripts/Presentation/Mesa.Casino.cs` | Parte de `Mesa`; escala, tamaños, paneles y distribución |
| `res://Scripts/Presentation/Mesa.Interaction.cs` | Parte de `Mesa`; selección, drag, borradores, envío y reconciliación |
| `res://Scripts/Presentation/Mesa.Dynamic.cs` | Parte de `Mesa`; controles contextuales y casilla de pago/cambio |
| `res://Scripts/Presentation/Interaction/HandInteractionState.cs` | Estado puro de preparación y `VisualGroupDraft`, sin Godot |
| `res://Scripts/Presentation/Interaction/InteractionActions.cs` | Comprobar disponibilidad de datos de una solicitud; validación definitiva en servidor |
| `res://Scenes/Diagnostics/MesaDynamicSmoke.tscn` | Diagnóstico aislado |
| `res://Scripts/Diagnostics/MesaDynamicSmoke.cs` | Comprobaciones de distribución, gestos y capturas |
| `res://Scripts/Diagnostics/MesaNetworkSmoke.cs` y su escena | Dos procesos ENet; controles de Mesa y confirmaciones reales |
| Fuera de `res://`: `Desmoche.Interaccion.Pruebas` | Consola de pruebas puras, 434 comprobaciones |
| Fuera de `res://`: `Desmoche.Pruebas/RunMesaDynamic.ps1` | Compilación, diagnóstico y captura opcional |
| Fuera de `res://`: `Desmoche.Pruebas/RunMesaNetwork.ps1` | Recorrido de intercambio, oferta y pago por localhost |

También se incluyen recursos y soportes visuales anteriores para evitar mezclar versiones. No se introducen RPC, cambios de autoridad o reglas nuevas. Instalación exacta: `LEEME.md`.

## 5. Configuración Godot

La escena conserva el árbol anterior. Este esquema destaca los elementos de la revisión; los dinámicos los crea `Mesa` al entrar en la escena. Los segmentos separados por `/` representan nodos sucesivos de una ruta, no un nombre literal:

```text
Mesa (Control; Mesa.cs, clase parcial)
└─ Scroll (ScrollContainer; follow_focus = false)
   └─ Padding (Control)
      └─ Content (Control; composición virtual 2016×780)
         ├─ Material (TextureRect; fondo casino)
         ├─ PlayerZone/Padding/Players (HFlowContainer; rivales dinámicos)
         ├─ Table/Padding/Surface (Control)
         │  ├─ OpponentGames (HFlowContainer; juegos públicos rivales)
         │  ├─ Center (HBoxContainer)
         │  │  ├─ Mazo/Content (VBoxContainer)
         │  │  │  ├─ TitleLabel (Label)
         │  │  │  ├─ Carta (Carta.tscn; dorso, click contextual)
         │  │  │  └─ CountLabel (Label)
         │  │  ├─ Pozo/Content (VBoxContainer)
         │  │  │  ├─ TitleLabel (Label)
         │  │  │  ├─ Carta (Carta.tscn; superior pública o vacío)
         │  │  │  └─ CountLabel (Label)
         │  │  ├─ Oferta/Content (VBoxContainer; cuando hay oferta)
         │  │  │  ├─ TitleLabel (Label)
         │  │  │  ├─ Carta (Carta.tscn; carta pública)
         │  │  │  └─ CountLabel (Label)
         │  │  └─ Pago (CardDropZone; dinámico, pago o cambio)
         │  │     └─ Content (VBoxContainer)
         │  │        ├─ TitleLabel (Label)
         │  │        ├─ Carta (Carta.tscn; carta preparada)
         │  │        └─ CountLabel (Label; indicación)
         │  └─ MyGame (Control; destino de juego nuevo)
         │     ├─ TitleLabel (Label)
         │     ├─ EmptyLabel (Label)
         │     └─ Rows (Control; juegos propios con destino de ampliación)
         ├─ HandZone/Padding/Content (Control)
         │  ├─ HandHeader/SelfPlayers (HFlowContainer; panel propio)
         │  ├─ HandHeader/TitleLabel (Label)
         │  ├─ Cards (HFlowContainer; mano propia dinámica)
         │  └─ Interaction (VBoxContainer)
         │     ├─ HintLabel (Label)
         │     ├─ SelectionLabel (Label)
         │     ├─ LocalActions (HFlowContainer; oculto)
         │     ├─ DraftZone (CardDropZone)
         │     │  └─ Content/Rows (VBoxContainer; borradores dinámicos)
         │     ├─ NetworkActions (HFlowContainer; oculto)
         │     └─ MessageLabel (Label)
         ├─ ContextActions (VBoxContainer; dinámico)
         │  ├─ ClaimButton / PassButton (Button; oferta)
         │  ├─ DrawDeckButton / DrawDiscardButton (Button; turno)
         │  ├─ ContinueButton (Button; etapa inicial)
         │  ├─ PublishButton / DesmocheButton (Button)
         │  ├─ NoPaymentButton (Button; cuando corresponde)
         │  ├─ DeclareWin (CheckButton; explícito)
         │  ├─ ConfirmCardButton (Button; Aceptar, tras preparar carta)
         │  └─ CancelCardButton (Button; Cambiar carta)
         ├─ TopBar (HBoxContainer; salir/cerrar sala)
         ├─ Header (VBoxContainer; estado y mensajes)
         └─ RevealedZone (VBoxContainer; según vista autorizada)
```

**Inspector:** copia la escena completa. Mantén los padres de composición como `Control` y el centro como `HBoxContainer`; `HFlowContainer` permite el salto que superponía la oferta. Las cartas y destinos se habilitan por estado y los filtros de ratón se configuran desde los scripts. No conectes manualmente los botones generados. No añadas sincronizadores ni authority de cliente a las cartas privadas visuales.

`Carta.tscn` conserva `PanelContainer`, padding, artwork y etiquetas. Sus eventos y callbacks reciben identidades, sin un objeto mutable del dominio. `CardDropZone` revalida los datos antes del drop.

## 6. Integración

`NetworkManager.ViewChanged` entrega una vista vigente y `Mesa.DisplaySnapshot` representa mano propia, juegos públicos, mazo oculto, pozo, oferta, fase y turno. La sincronización reconcilia orden y borradores, cancela gestos sobre nodos que van a reemplazarse y limpia preparaciones que perdieron su contexto.

`Carta.SelectionRequested` permite selección múltiple. `_GetDragData`, `_CanDropData`, `_DropData` y el reenvío de drag de destinos permiten ordenar, crear y ampliar borradores. Enviar utiliza `InteractionActions`, `NetworkManager.Submit` y los RPC aprobados. `ActionCompleted` informa aceptación o rechazo; solo el snapshot confirmado modifica cartas reales y contadores.

Los controles visibles llaman las mismas solicitudes. **Aceptar** añade una confirmación de presentación a la acción existente. Declarar victoria conserva su gesto explícito; ninguna validación visual reemplaza a `RulesValidator` del servidor.

Privacidad: se dibuja `OwnHand` del destinatario; los rivales muestran nombres, conexión, cantidades y juegos públicos. La oferta es pública según las reglas existentes. El mazo muestra dorsos y cantidad, sin identidades ni orden. Los borradores usan exclusivamente cartas propias, juegos públicos propios y la fuente externa permitida.

## 7. Prueba de aceptación

**A. Compilación y consola.** Ejecuta las órdenes de `LEEME.md`. Espera compilación correcta y **434 comprobaciones correctas**. No copies las pruebas de consola dentro de `res://`.

**B. Diagnóstico gráfico.** Ejecuta `RunMesaDynamic.ps1` con `-Capture`. Revisa `MesaDynamic.log`, el marcador `MESA DYNAMIC PASS:` y las capturas generadas. Comprueba el drag nativo de Godot en un `SubViewport` renderizado, sin mover el cursor del sistema. Sin `-Capture`, se omiten esos gestos expresamente. Complementa con una prueba manual en la ventana normal mediante F5.

**B2. ENet real.** Ejecuta `RunMesaNetwork.ps1` según `LEEME.md`. Ambos procesos deben terminar con `MESA NETWORK PASS:`; las cifras dependen del reparto. Verifica intercambio, oferta y pago desde controles de la mesa usando las vistas reales del servidor, sin instalar fixtures.

**C. Posición.** En una partida con dos y luego cuatro jugadores, revisa oferta, pago y cambio inicial a 2016×780, 1200×900 y 976×874. La oferta queda a la derecha del pozo y no tapa **Mi juego**. El nombre propio y sus estados no se superponen a mensajes. Entrar en Mesa no provoca scroll por foco. Redimensiona y desplázate para acceder a los borradores.

**D. Orden y preparación.** Arrastra una carta entre otras de la mano: no cambia su cantidad. Suelta una sola carta en **Mi juego**, después añade una segunda y una tercera al borrador. Con una o dos no debe enviarse. Prueba una escalera válida ascendente y luego descendente; ambos sentidos deben aceptarse. Un orden no consecutivo debe rechazarse. Devuelve una carta nueva del borrador a la mano: deshace su preparación, sin solicitar descarte. Quita el borrador y comprueba que la mano real permaneció intacta.

**E. Ampliación y privacidad.** Suelta una carta sobre un juego propio publicado: prepara su ampliación sin desplegable. Las cartas publicadas no se devuelven a la mano. No puede ampliarse un juego rival mediante este destino. Otro cliente conserva oculta tu mano, selección, orden y borradores.

**F. Oferta y pozo.** Prepara una combinación que incorpore la oferta y pulsa **Tomar oferta**. **Rechazar oferta** conserva la acción de pasar. Si dos rivales reclaman, el servidor conserva la prioridad acordada a la derecha; la UI no resuelve la competencia. Cuando corresponda, prepara una combinación con la carta superior del pozo y solicita **Tomar pozo**. Ganar al tomar requiere marcar su control explícito.

**G. Cambio inicial y pago.** Arrastra una carta a **Cambio inicial**: aparece **Aceptar** y la carta sigue contada en la mano. Usa **Cambiar carta**, prepara otra y acepta. El servidor conserva entrega a la derecha y recepción desde la izquierda. En **Pago**, repite: el drop no envía; aceptar limpia la preparación al solicitar y el snapshot retira la carta si el servidor acepta. Comprueba que no se duplican solicitudes mientras hay una pendiente.

**H. Contexto y finalización.** Cambia de fase, desconecta o recibe una actualización que invalide una carta preparada: la casilla no debe conservar una carta ajena a la mano vigente. Comprueba continuar, publicar, desmoche y terminar sin pago en los contextos permitidos. Conserva las pruebas LAN aprobadas de finalización y desconexión.

Las escenas de demostración aíslan presentación. La aceptación de acciones de red se ejecuta desde `Main` con F5. Los resultados realmente ejecutados están en `VERIFICACION.md`.

## 8. Posibles errores

| Síntoma | Diagnóstico y corrección mínima |
| --- | --- |
| Oferta debajo del pozo o botones anteriores visibles | Copia escena y scripts completos y recompila; no mezcles versiones |
| Falta nodo o no compila `Mesa.Dynamic.cs` | Copia todo el contenido de `desmoche`, combinando subcarpetas |
| Soltar en pago no descarta inmediatamente | Es deliberado: prepara la carta y pulsa **Aceptar** |
| No se confirma un borrador de una o dos cartas | Complétalo; preparar no convierte el borrador en juego válido |
| No puedo devolver una carta publicada | El retorno deshace únicamente cartas nuevas preparadas |
| Control deshabilitado en diagnóstico | No hay conexión vigente; juega desde `Main` para enviar acciones reales |
| Escalera rechazada | Revisa palo y orden declarado; no se ordena automáticamente |
| Se limpia la carta preparada al actualizar | Revisa sesión, fase, ventana y disponibilidad; se evitan acciones obsoletas |
| No se verifica drag sin ventana | Ejecuta con `-Capture` para utilizar el backend gráfico |
| Godot no carga C# o no encuentra proyecto | Usa Godot .NET y las rutas de `LEEME.md` |

Para informar un error conserva estado, resolución, registro y acción concreta. Se corrige el componente mínimo responsable sin reescribir dominio o networking.

## 9. Estado de la fase

FASE COMPLETADA: SI; actualización visual y de interacción implementada, compilada y verificada. Consultar `VERIFICACION.md`.

BLOQUEADORES: ninguno. La aceptación manual en los dispositivos LAN del usuario continúa según la sección 7; no se afirma que esas pruebas físicas se hayan ejecutado aquí.

SIGUIENTE FASE: la que indique el usuario después de revisar esta actualización. No se implementan fases posteriores.
