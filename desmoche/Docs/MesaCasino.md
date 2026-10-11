# Revisión visual — Mesa casino

## 1. Objetivo de la fase

Actualizar la presentación de la fase 8 y conservar las interacciones de la fase 9. La referencia define el encuadre: tres paneles superiores, tres zonas para juegos públicos, mazo/pozo centrales, juego propio y mano privada frontal.

La revisión aporta madera oscura, cuero acolchado, detalles dorados, fieltro verde, retratos circulares y cartas marfil con palos claros, índices correctos y figuras coherentes. Cada zona rival reserva espacio para diez cartas en una fila.

## 2. Decisiones arquitectónicas

La interfaz continúa usando `Control`. Una composición virtual **2016×780** se escala uniformemente dentro de la ventana. El fondo limpio es un `TextureRect`; cartas, retratos, etiquetas, contadores y destinos son elementos independientes.

Cada rival dispone de 548 unidades de ancho. Para diez cartas el ancho máximo es `(548 - 9 × 5) / 10 = 50,3`, con alto máximo de 72. Los grupos conservan identidad y orden del servidor y se distribuyen horizontalmente, centrados en su zona. Una cantidad autorizada mayor se representa reduciendo tamaño: la UI no trunca cartas ni establece reglas nuevas.

El juego propio usa cartas de hasta 90×96; la mano, hasta **124×128**. Los tamaños y separaciones se adaptan al ancho disponible. Al reducir la ventana, la composición cambia de escala sin deformarse. El pie de acciones y borradores compensa esa escala para mantener texto y botones legibles; se accede mediante el scroll.

`Mesa.Casino.cs` contiene escala, distribución y retratos; `Mesa.cs` representa las vistas; `Mesa.Interaction.cs` conserva selección, arrastre, borradores y solicitudes. La decoración de la pila muestra dorsos sin identidades y no representa el contenido del mazo.

Los contadores usan `PublicPlayerDto.CardCount`, `SnapshotDto.OwnHand.Length`, `DeckCount` y `DiscardCount`. No se aplica `9 - juegos`: una mano de seis puede pasar a siete mientras espera pagar. Esa carta continúa visible y contada hasta que el servidor confirma el pago.

Los retratos son decorativos de la vista; no representan fotos reales ni añaden perfiles. Las texturas se importan con mipmaps y el dibujo usa filtrado compatible para conservar limpieza al reducirlas.

## 3. Dependencias

Proyecto existente con fases 0–9 aprobadas, Godot **4.7.2 .NET**, SDK .NET instalado, `Main`, `NetworkManager`, DTO y clases de interacción existentes. El paquete actualiza archivos y conserva el `.csproj`.

`Portraits.png` tiene cuatro regiones de 627×627 y una máscara circular. `Courts.png` tiene regiones de 512×1024 para J/Q/K. Valor y palo se dibujan aparte desde la identidad real. Las cartas numéricas usan un palo central grande; los índices muestran valor y símbolo sin letras P/T/C/D.

Los títulos buscan Georgia, Times New Roman, Liberation Serif o Noto Serif; los índices usan Georgia, Times New Roman o Noto Serif. No se redistribuyen fuentes. Si faltan, la sustitución del sistema puede cambiar el aspecto.

Documentación oficial utilizada: [Control](https://docs.godotengine.org/en/stable/classes/class_control.html), [AtlasTexture](https://docs.godotengine.org/en/stable/classes/class_atlastexture.html), [TextureRect](https://docs.godotengine.org/en/stable/classes/class_texturerect.html), [Viewport](https://docs.godotengine.org/en/stable/classes/class_viewport.html) y [shader CanvasItem](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/canvas_item_shader.html). La máscara modifica únicamente el alfa de `COLOR`, que ya contiene la textura en `fragment()`. Compilación y ejecución se verifican concretamente con 4.7.2 .NET.

## 4. Implementación

El paquete contiene archivos completos compatibles entre sí.

| Ruta | Responsabilidad |
| --- | --- |
| `res://Scenes/Mesa.tscn` | Composición panorámica, mano, destinos, contadores y pie de acciones |
| `res://Scripts/Presentation/Mesa.cs` | Representar vistas autorizadas |
| `res://Scripts/Presentation/Mesa.Casino.cs` | Escala, filas, retratos y decoración |
| `res://Scripts/Presentation/Mesa.Interaction.cs` | Interacción existente y redistribución visual |
| `res://Scenes/Carta.tscn` | Árbol visual de carta |
| `res://Scripts/Presentation/Carta.cs` | Cara/dorso/vacío, tamaño, selección e input nativo |
| `res://Scripts/Presentation/CartaArtwork.cs` | Palos y atlas de figuras |
| `res://Assets/Casino/CasinoTable.png` | Fondo limpio, 2017×780 |
| `res://Assets/Casino/Portraits.png` | Cuatro retratos decorativos, 1254×1254 |
| `res://Assets/Casino/Courts.png` | Figuras J/Q/K, 1536×1024 |
| `res://Assets/Casino/PortraitCircle.gdshader` | Máscara circular |
| `res://Assets/Casino/*.png.import` | Configuración de importación con mipmaps |
| `res://Assets/Casino/AssetsManifest.json` | Dimensiones y regiones reales |
| `res://Assets/Casino/AssetPrompts.md` | Prompts y método de generación |
| `res://Scenes/Diagnostics/CasinoVisualSmoke.tscn` | Escena aislada de comparación |
| `res://Scripts/Diagnostics/CasinoVisualSmoke.cs` | Casos, geometría, privacidad, gesto y captura |
| `res://Scripts/Diagnostics/MesaVisualSmoke.cs` | Regresión compatible con filas horizontales |
| Fuera de `res://`: `Desmoche.Pruebas/RunCasinoVisual.ps1` | Compilación, prueba y captura |

Se incluyen los archivos de soporte de interacción y diagnósticos de la fase 9. No se añaden RPC ni modificaciones de dominio, Application o networking.

Copia el contenido de `desmoche` del paquete sobre `C:\Users\ricar\Documents\desmoche`, combinando carpetas y sustituyendo coincidencias. Los proyectos y scripts de pruebas se copian junto al proyecto, fuera de `res://`. Sigue `LEEME.md` para las órdenes exactas.

## 5. Configuración Godot

La escena ya incluye este árbol; los elementos dinámicos se crean desde los DTO y no se añaden manualmente:

```text
Mesa (Control; Mesa.cs, clase parcial)
├─ Background (ColorRect)
└─ Scroll (ScrollContainer)
   └─ Padding (Control)
      └─ Content (Control; 2016×780)
         ├─ Material (TextureRect; CasinoTable.png)
         ├─ TopBar (HBoxContainer)
         │  ├─ TitleLabel (Label)
         │  └─ LeaveButton (Button)
         ├─ Header (VBoxContainer)
         │  ├─ ConnectionLabel (Label)
         │  ├─ PhaseLabel (Label)
         │  ├─ TurnLabel (Label)
         │  ├─ ResultLabel (Label)
         │  └─ ResponseLabel (Label)
         ├─ PlayerZone (Control)
         │  └─ Padding (Control)
         │     └─ Players (HFlowContainer; paneles rivales dinámicos)
         ├─ Table (Control)
         │  └─ Padding (Control)
         │     └─ Surface (Control)
         │        ├─ OpponentGames (HFlowContainer)
         │        │  └─ JuegosJugador<ID> (VBoxContainer; dinámico)
         │        │     ├─ encabezado (HBoxContainer; dinámico)
         │        │     └─ Rows (Control; dinámico)
         │        │        └─ Juego<ID> (HFlowContainer; dinámico)
         │        │           └─ Carta<n> (Carta.tscn; dinámico)
         │        ├─ Center (HFlowContainer)
         │        │  ├─ Mazo (VBoxContainer)
         │        │  │  └─ Content (VBoxContainer)
         │        │  │     ├─ TitleLabel (Label)
         │        │  │     ├─ Carta (Carta.tscn; dorso)
         │        │  │     └─ CountLabel (Label)
         │        │  ├─ Pozo (PanelContainer; CardDropZone.cs)
         │        │  │  └─ Content (VBoxContainer)
         │        │  │     ├─ TitleLabel (Label)
         │        │  │     ├─ Carta (Carta.tscn; cara/vacío)
         │        │  │     └─ CountLabel (Label)
         │        │  └─ Oferta (VBoxContainer; visible cuando hay oferta)
         │        │     └─ Content (VBoxContainer)
         │        │        ├─ TitleLabel (Label)
         │        │        ├─ Carta (Carta.tscn)
         │        │        └─ CountLabel (Label)
         │        └─ MyGame (Control)
         │           ├─ TitleLabel (Label)
         │           ├─ LineLeft (ColorRect)
         │           ├─ LineRight (ColorRect)
         │           ├─ EmptyLabel (Label)
         │           └─ Rows (Control; juegos propios dinámicos)
         ├─ HandZone (Control)
         │  └─ Padding (Control)
         │     └─ Content (Control)
         │        ├─ HandHeader (Control)
         │        │  ├─ SelfPlayers (HFlowContainer; panel propio dinámico)
         │        │  └─ TitleLabel (Label)
         │        ├─ Cards (HFlowContainer; mano propia dinámica)
         │        ├─ EmptyLabel (Label)
         │        └─ Interaction (VBoxContainer)
         │           ├─ HintLabel (Label)
         │           ├─ SelectionLabel (Label)
         │           ├─ LocalActions (HFlowContainer)
         │           │  ├─ TargetGame (OptionButton)
         │           │  ├─ PrepareButton (Button)
         │           │  ├─ ClearSelectionButton (Button)
         │           │  ├─ ClearDraftsButton (Button)
         │           │  ├─ LeftButton (Button)
         │           │  ├─ RightButton (Button)
         │           │  └─ ExternalSelect (CheckButton)
         │           ├─ DraftZone (PanelContainer; CardDropZone.cs)
         │           │  └─ Content (VBoxContainer)
         │           │     ├─ TitleLabel (Label)
         │           │     ├─ EmptyLabel (Label)
         │           │     └─ Rows (VBoxContainer; borradores dinámicos)
         │           ├─ NetworkActions (HFlowContainer)
         │           │  ├─ ExchangeButton (Button)
         │           │  ├─ ContinueButton (Button)
         │           │  ├─ DrawDeckButton (Button)
         │           │  ├─ PassButton (Button)
         │           │  ├─ PublishButton (Button)
         │           │  ├─ ClaimButton (Button)
         │           │  ├─ DrawDiscardButton (Button)
         │           │  ├─ DesmocheButton (Button)
         │           │  ├─ PayButton (Button)
         │           │  ├─ NoPaymentButton (Button)
         │           │  └─ DeclareWin (CheckButton)
         │           └─ MessageLabel (Label)
         └─ RevealedZone (VBoxContainer; según vista autorizada)
            ├─ TitleLabel (Label)
            └─ Rows (VBoxContainer; revelaciones dinámicas)
```

Los paneles dinámicos `Jugador<ID>` son `PanelContainer` con retrato, nombre, estado y cantidad. El borde dorado indica turno o pago activo recibido en la vista.

```text
Carta (PanelContainer; Carta.cs)
└─ Padding (MarginContainer)
   └─ Content (Control)
      ├─ Artwork (Control; CartaArtwork.cs)
      ├─ Corner (Control)
      │  ├─ Rank (Label)
      │  └─ Suit (Label; oculto, sin letras de palo)
      ├─ ReverseCorner (Control; girado 180°)
      │  ├─ Rank (Label)
      │  └─ Suit (Label; oculto)
      ├─ Symbol (Label; oculto)
      └─ Caption (Label; modo vacío)
```

**Inspector:** `Mesa` y `Scroll` ocupan su padre. `Padding` y `Content` son `Control`: convertirlos a contenedores que reajusten escala rompe la composición. `Material`, dibujos y etiquetas ignoran ratón para que el input llegue al nodo adecuado. `Carta` comienza sin input; el controlador configura interacción y foco cuando corresponde. `Pozo` y `DraftZone` usan `CardDropZone.cs`. No asignes autoridad de red a cartas visuales ni añadas sincronizadores a la mano.

`Main` conserva su instancia de `Mesa.tscn` y enlace al `NetworkManager`. No cambies la escena principal o el Inspector de networking. La escena de diagnóstico hereda `Main` y sustituye únicamente su script por `CasinoVisualSmoke.cs`.

## 6. Integración

`NetworkManager` recibe una vista autorizada; `Mesa.DisplaySnapshot` actualiza paneles, juegos, mano propia, mazo, pozo, oferta, fase y turno y solicita la redistribución. La UI usa identidades presentes en la vista; no reconstruye manos privadas rivales ni orden del mazo.

`Carta.SelectionRequested(int cardId)` informa selección. `_GuiInput` procesa click/teclado y `_GetDragData`, `_CanDropData`, `_DropData` utilizan Drag & Drop nativo. La vista previa adopta la escala visual real. Reordenar cambia el orden local; preparar crea un borrador pendiente de validación.

Botones y drops producen solicitudes con las clases de interacción existentes. El servidor valida y replica el resultado. Una respuesta no resta por sí sola cartas del contador: se espera el nuevo estado. Los enlaces `ViewChanged`, `ConnectionChanged` y `ActionCompleted` conservan su ciclo de vida y limpieza.

No se sincronizan imágenes o layouts ni se cambia la autoridad. La separación entre `Carta` visual y `Card` de dominio permanece intacta. Diagrama: `MesaCasino.puml`.

## 7. Prueba de aceptación

**A. Compilación y consola.** Ejecuta las dos órdenes de `LEEME.md`: compilación correcta y 296 comprobaciones de interacción.

**B. Comparación manual.** Abre `CasinoVisualSmoke.tscn`, pulsa F6 y compara a 2016×780. Revisa silueta, materiales, paneles, retratos, índices/palos, figuras, tamaños, márgenes y pozo vacío. Espacio cambia de caso. El pago sigue contando siete cartas; el caso de espacio tiene tres filas de diez. Redimensiona a 1600×900 y 1000×760; la mesa conserva su forma. Desplázate para acceder al pie de controles.

**C. Automática y captura.** Ejecuta `RunCasinoVisual.ps1` con `-GodotPath`, `-ProjectPath` y `-Capture`. Verifica nueve casos, el arrastre nativo bajo escalado y la privacidad; captura el primer ejemplo antes del gesto. Debe terminar con `PRUEBA MESA CASINO COMPLETADA` y `CASINO VISUAL OK` en el registro. Sin `-Capture` ejecuta nueve casos sin ventana y omite explícitamente el arrastre nativo; no lo presentes como prueba del gesto.

**D. Regresión.** Ejecuta `RunCardInteraction.ps1` con tu Godot .NET console. Deben aprobarse `CardInputSmoke`, `MesaInteractionSmoke` y `MesaVisualSmoke`. Conservan selección, orden, borradores, drops, reconciliación y privacidad.

**E. Partida real.** Con F5 usa el lobby aprobado. Comprueba intercambio inicial, turnos, oferta pública, publicación y pago. La carta por pagar sigue contada hasta la confirmación del servidor. Prueba click/arrastre en la mano escalada, controles inferiores, rechazos y limpieza al desconectar. La repetición en 2–4 dispositivos LAN se realiza en la instalación del usuario; esta revisión no afirma haberla ejecutado aquí.

### Ejemplo coherente y diferencias

| Zona | Juegos públicos | Mano | Motivo |
| --- | --- | --- | --- |
| Ana | 9 cartas en dos escaleras válidas | 0 | Nueve cartas asignadas |
| Luis | 7 en una escalera | 2 | Estado desconectado/eliminado de demostración |
| Tercer jugador | J♠, Q♠, K♠ | 6 | Dos cartas no forman un juego válido |
| Mi juego | 5♦, 5♣, 5♥ | — | Tercia válida |
| Mi mano | A♥, 2♦, 3♠, 6♣, 8♦, K♥ | 6 | Mano solicitada |
| Mazo | Dorso sin identidad | 16 en el mazo | 36 asignadas + 16 = 52 |
| Pozo | Vacío | 0 | Sin carta superior |

El pago incorpora una carta propia adicional: mano siete y mazo quince. El caso de tres rivales con diez cartas es sintético y no redefine la victoria.

Estos snapshots son demostraciones de presentación, no reconstrucciones de una trayectoria de `GameManager`. La referencia incluye inconsistencias de cartas/cantidades; los ejemplos usan identidades válidas. El acabado es una aproximación generada: cambian detalles de cuero, luz, retratos y figuras. Las cartas superiores son más pequeñas para reservar diez por rival. Los controles quedan debajo del encuadre. El fondo real es 2017×780, no 4K; pantallas mayores pueden mostrar menos detalle. La tipografía depende de las fuentes disponibles. Los prompts y límites de los recursos están documentados en `Assets/Casino`.

La captura entregada en `Evidence/MesaCasino.png` es del viewport real de Godot a 2016×780 y se comparó con la referencia. La versión final aprobó 378 comprobaciones gráficas, 360 sin ventana, 317 de vistas, 124 de interacción, 26 de input y 296 de consola. Los registros y los límites de verificación están en `VERIFICACION.md`.

## 8. Posibles errores

| Síntoma | Solución mínima |
| --- | --- |
| Falta script, shader o atlas | Copia toda la carpeta `desmoche`, espera importación y recompila |
| Persisten letras de palo o filas partidas | Reemplaza escenas y scripts completos; no mezcles versiones |
| Error de tipo en `Rows` | La fila pública nueva es `Control`; copia también el diagnóstico actualizado |
| Mesa deformada o estrecha | Conserva `Scroll/Padding/Content` y padres como `Control` |
| No veo acciones inferiores | Desplázate dentro de Mesa |
| Consola no encuentra fuentes | Las pruebas van junto al proyecto existente; el paquete no incluye las fases anteriores |
| Godot no carga C# | Usa la edición .NET y compila correctamente |
| No se genera captura | Usa `-Capture`; `--headless` no produce la comparación gráfica |
| Acceso denegado en registros/compilación | Revisa la ruta y el historial de protección de Windows; usa una carpeta permitida |
| Nombre largo ocupa demasiado | Revisa la resolución objetivo; no reduzcas nombres desde el protocolo |

Conserva registro, resolución y estado al diagnosticar. Corrige el archivo mínimo responsable antes de modificar arquitectura o motor.

## 9. Estado de la fase

FASE COMPLETADA: SI, revisión visual implementada, compilada y verificada localmente.

BLOQUEADORES: ninguno para esta revisión. La instalación, valoración visual y repetición opcional en dispositivos LAN se realizan en el proyecto del usuario; no se confunden con comprobaciones ya ejecutadas.

SIGUIENTE FASE: la que indique el usuario después de revisar esta entrega. No se implementan fases posteriores.
