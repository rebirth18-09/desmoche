# Fase 8 — Mesa y cartas, distribución basada en la referencia

## 1. Objetivo de la fase

Representar la partida como una mesa de cartas: rivales arriba, sus juegos públicos frente a ellos, mazo/pozo/oferta en el centro, juegos propios en la parte inferior del tapete y mano privada en una bandeja inferior.

Esta entrega sustituye la presentación inicial de fase 8. No modifica reglas, dominio, GameManager, NetworkManager, RPC ni datos sincronizados.

## 2. Decisiones arquitectónicas

- Mesa sigue consumiendo exclusivamente NetworkManager.View, la vista autorizada por destinatario.
- Los rivales se ordenan por SeatIndex; sus columnas de juegos mantienen ese mismo orden. La colocación visual no calcula prioridades ni turnos.
- Los grupos públicos se colocan según OwnerId. Cada MeldDto conserva su identidad y orden de cartas, sin ordenar ni validar de nuevo.
- OwnHand aparece solo en la bandeja propia. Los encabezados contienen datos públicos y nunca nodos de cartas.
- La carta ofrecida se muestra aparte del pozo. La UI espera que el servidor confirme su destino.
- El propio jugador aparece en el encabezado de la bandeja y conserva el borde de turno/pago. RenderedPlayerCount cuenta los paneles realmente creados, incluidos propio y rivales.
- Los avatares son iniciales locales y colores por Id. El protocolo no contiene fotografías ni campos nuevos de perfil.
- Las superficies son SVG originales: madera oscura, paño verde, marco oscuro/dorado y bandeja oscura. Las cartas utilizan arte vectorial original dibujado con CanvasItem.
- Las figuras J/Q/K son ilustraciones estilizadas originales. La referencia guía distribución y aspecto; no se introduce ninguna regla o combinación de la imagen.
- En pantallas estrechas los HFlow envuelven cartas y paneles; Scroll permite recorrer contenido largo. No se reduce una carta a un tamaño ilegible.
- No se implementan selección, acciones de cartas, animaciones ni Drag & Drop en esta revisión visual.

## 3. Dependencias

Godot 4.7.2 .NET y las fases 0–7 instaladas. Se conserva Main.tscn con NetworkManager persistente en /root/Main/NetworkManager.

No cambiar Desmoche.csproj, Program.cs ni Pruebas.csproj. Las clases de presentación usan Godot y no se añaden al proyecto de pruebas del dominio.

## 4. Implementación

Todos los archivos del paquete son completos.

| Ruta | Responsabilidad |
|---|---|
| res://Scripts/Presentation/Mesa.cs | Mesa : Control; representa SnapshotDto en las zonas por propietario |
| res://Scenes/Mesa.tscn | Contenedores, fondos, etiquetas, cartas del centro y bandeja |
| res://Scripts/Presentation/Carta.cs | Carta : PanelContainer; identidad autorizada, cara/dorso/vacío y etiquetas |
| res://Scripts/Presentation/CartaArtwork.cs | CartaArtwork : Control; palos vectoriales, pips, figuras y dorso |
| res://Scenes/Carta.tscn | Carta de 76×108 con índices superior e inferior invertido |
| res://Assets/Table/Wood.svg | Fondo de madera |
| res://Assets/Table/Felt.svg | Tapete y marco |
| res://Assets/Table/Tray.svg | Bandeja inferior |
| res://Scripts/Diagnostics/MesaVisualSmoke.cs | Comprobaciones de datos, privacidad y distribución; vista previa |
| res://Scenes/Diagnostics/MesaVisualSmoke.tscn | Instancia de Main para diagnóstico aislado |
| res://Scripts/Diagnostics/LobbySmoke.cs | Diagnóstico de conexión compatible con la ruta del botón de Mesa |
| res://Docs/Fase8.md / Fase8.puml | Este contrato y su diagrama |

Fuera de res://: Desmoche.Pruebas/RunMesaVisual.ps1.

Todos los scripts nuevos o sustituidos utilizan namespace Desmoche.Presentation o Desmoche.Diagnostics según su carpeta. El networking existente no se sustituye.

Carta conserva ShowFace(int), ShowBack(), ShowEmpty(string), FaceCardId, IsFaceUp e IsBack. El dorso y el vacío eliminan la identidad en Carta y el rango/palo en CartaArtwork.

## 5. Configuración Godot

No crear estos nodos a mano si copias las escenas completas. No renombrarlos: Mesa utiliza sus rutas exactas.

```text
Mesa (Control) [Mesa.cs]
├── Background (TextureRect) [Wood.svg]
└── Scroll (ScrollContainer)
    └── Padding (MarginContainer)
        └── Content (VBoxContainer)
            ├── TopBar (HBoxContainer)
            │   ├── TitleLabel (Label)
            │   └── LeaveButton (Button)
            ├── Header (VBoxContainer)
            │   ├── ConnectionLabel (Label)
            │   ├── PhaseLabel (Label)
            │   ├── TurnLabel (Label)
            │   ├── ResultLabel (Label)
            │   └── ResponseLabel (Label)
            ├── PlayerZone (MarginContainer)
            │   └── Padding (MarginContainer)
            │       └── Players (HFlowContainer) [solo rivales]
            ├── Table (PanelContainer)
            │   ├── Felt (TextureRect) [Felt.svg]
            │   └── Padding (MarginContainer)
            │       └── Surface (VBoxContainer)
            │           ├── OpponentGames (HFlowContainer)
            │           ├── Center (HFlowContainer)
            │           │   ├── Mazo (VBoxContainer)
            │           │   │   └── Content (VBoxContainer)
            │           │   │       ├── TitleLabel (Label)
            │           │   │       ├── Carta (Carta.tscn)
            │           │   │       └── CountLabel (Label)
            │           │   ├── Pozo (VBoxContainer)
            │           │   │   └── Content (VBoxContainer)
            │           │   │       ├── TitleLabel (Label)
            │           │   │       ├── Carta (Carta.tscn)
            │           │   │       └── CountLabel (Label)
            │           │   └── Oferta (VBoxContainer) [oculta sin OfferedCard]
            │           │       └── Content (VBoxContainer)
            │           │           ├── TitleLabel (Label)
            │           │           ├── Carta (Carta.tscn)
            │           │           └── CountLabel (Label)
            │           └── MyGame (VBoxContainer)
            │               ├── TitleLabel (Label)
            │               ├── EmptyLabel (Label)
            │               └── Rows (VBoxContainer)
            ├── HandZone (PanelContainer)
            │   ├── Tray (TextureRect) [Tray.svg]
            │   └── Padding (MarginContainer)
            │       └── Content (VBoxContainer)
            │           ├── HandHeader (HBoxContainer)
            │           │   ├── SelfPlayers (HFlowContainer) [solo jugador propio]
            │           │   └── TitleLabel (Label)
            │           ├── Cards (HFlowContainer) [OwnHand]
            │           └── EmptyLabel (Label)
            └── RevealedZone (VBoxContainer) [oculta sin victoria/revelación]
                ├── TitleLabel (Label)
                └── Rows (VBoxContainer)
```

Nodos dinámicos:
- Players: Jugador<ID> de cada rival, PanelContainer con avatar circular de iniciales y etiquetas de nombre/estado/cantidad.
- SelfPlayers: Jugador<ID> del destinatario, panel compacto con el mismo indicador de turno.
- OpponentGames: JuegosJugador<ID>, VBox con título y Rows; cada combinación es HFlow Juego<MeldId>.
- MyGame/Rows: cada combinación propia es HFlow Juego<MeldId>.
- Cards: Carta0, Carta1… en el orden de OwnHand.
- RevealedZone/Rows: manos rivales autorizadas al finalizar con ganador, separadas de los juegos publicados.

```text
Carta (PanelContainer) [Carta.cs]
└── Padding (MarginContainer)
    └── Content (Control)
        ├── Artwork (Control) [CartaArtwork.cs]
        ├── Corner (Control)
        │   ├── Rank (Label)
        │   └── Suit (Label)
        ├── ReverseCorner (Control) [rotación PI]
        │   ├── Rank (Label)
        │   └── Suit (Label)
        ├── Symbol (Label) [oculta, compatibilidad]
        └── Caption (Label) [texto del espacio vacío]
```

| Inspector | Valor |
|---|---|
| Mesa, Background, Scroll | Full Rect |
| Background | Expand Mode Ignore Size; Keep Aspect Covered; Mouse Filter Ignore |
| Scroll | Horizontal Disabled; Follow Focus true |
| Padding de Scroll | Márgenes 12 horizontal y 8/12 vertical |
| Content | Expand Fill horizontal; Separation 10 |
| Players y OpponentGames | HFlow; separación horizontal 14; orden de SeatIndex |
| Table | Panel StyleBoxEmpty; mínimo alto 350 |
| Felt y Tray | Expand Mode Ignore Size; Stretch Scale; Mouse Filter Ignore |
| Table/Padding | Márgenes 40 horizontal y 28 vertical |
| Surface | VBox; separación 14 |
| Center | HFlow; Alignment Center; separación 38/12 |
| Mazo y Pozo | Mínimo ancho 105 |
| Oferta | Mínimo ancho 125; Visible false al inicio |
| MyGame | Título y cartas centrados; combinaciones en filas separadas |
| HandZone/Padding | Márgenes 26 horizontal y 14/16 vertical |
| HandHeader | HBox con panel propio y título de mano |
| Cards | HFlow centrado; separación 8/8 |
| Carta | Mínimo 76×108; Mouse Filter Pass |
| Carta/Padding | Márgenes 4 |
| Artwork y etiquetas | Mouse Filter Ignore |
| ReverseCorner | Rotación PI y pivot (12,20) |
| RevealedZone | Visible false al inicio |
| Scripts | Adjuntos a Mesa, Carta y Artwork; no hay Export pendientes |

Señales: LeaveButton.Pressed → NetworkManager.Close; ViewChanged y ConnectionChanged → representación actualizada. Las suscripciones se eliminan al desvincular o salir. CartaArtwork repinta al cambiar tamaño o modo.

No hay RPC ni nuevos nodos de autoridad en estas escenas. El servidor conserva toda autoridad.

## 6. Integración e instalación

Esta entrega contiene la fase 8 completa revisada, por lo que sirve también si aún no copiaste el primer ZIP de fase 8.

1. Detener juegos/pruebas y cerrar Godot.
2. Extraer Fase8-Mesa-Visual.zip.
3. Copiar el CONTENIDO de desmoche del paquete dentro de C:\Users\ricar\Documents\desmoche, combinando carpetas y sustituyendo los archivos del paquete que ya existan.
4. Copiar RunMesaVisual.ps1 desde Desmoche.Pruebas a C:\Users\ricar\Documents\Desmoche.Pruebas.
5. Compilar:

```powershell
dotnet build "C:\Users\ricar\Documents\desmoche\Desmoche.csproj"
```

6. Abrir Godot y dejar que importe los tres SVG. No añadir recursos externos manualmente.
7. Ejecutar F5, crear/unirse/iniciar como antes.

Main, Lobby, NetworkManager, project.godot y las reglas no se reemplazan. La escena principal continúa siendo res://Scenes/Main.tscn.

Una partida real mostrará nueve cartas propias, 1–3 rivales y mazo de 34/25/16 según 2/3/4 jugadores. Al inicio no hay juegos publicados y el pozo está vacío. La sesión queda en intercambio inicial porque todavía no existen controles de cartas.

## 7. Prueba de aceptación

```powershell
Set-Location "C:\Users\ricar\Documents\Desmoche.Pruebas"
$godotExe = "C:\Users\ricar\Downloads\Godot_v4.7.2-stable_mono_win64 (2)\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
.\RunMesaVisual.ps1 -GodotPath $godotExe
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 2
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 3
.\RunLobbySmoke.ps1 -GodotPath $godotExe -Players 4
```

Exigir PRUEBA MESA COMPLETADA y PRUEBA LOBBY COMPLETADA. Los comandos pueden ejecutarse uno por uno, con las ejecuciones anteriores detenidas.

MesaVisualSmoke comprueba las 52 identidades y colores, limpieza del dorso/vacío, no modificación de SnapshotDto, todas las identidades visibles autorizadas, cantidad de rivales/propio, orden de asientos, ubicación y orden de cada juego, turno/pago y revelación solo al ganar.

También incluye 3 y 4 jugadores; el caso de cuatro usa RecipientId 3 y pago del jugador propio, para asegurar que el host no se confunde con el destinatario. El fixture de escalera larga verifica la presentación de 13 cartas y no afirma que esa situación se alcance mediante acciones del motor. Los fixtures nunca se envían a una sesión real.

Para inspección visual, abrir Scenes/Diagnostics/MesaVisualSmoke.tscn y pulsar F6. Recorre doce casos cada seis segundos:
1. Reparto de nueve cartas.
2. Juegos públicos y turno.
3. Carta ofrecida.
4. Pago pendiente.
5. Jugador desconectado/eliminado.
6. Victoria y revelación.
7. Empate sin revelación.
8. Limpieza de estado.
9. Tres jugadores y sus juegos.
10. Cuatro jugadores desde un cliente.
11. Escalera pública larga.
12. Sin estado sincronizado.

Revisar una ventana maximizada, el tamaño inicial 1100×800 y una ventana estrecha de 640×480. En esta última se permite desplazamiento vertical y salto de fila. Las cartas deben ser legibles y ninguna zona debe quedar inaccesible.

Después detener F6 y usar F5 para la sesión normal. No cambiar la escena principal al diagnóstico.

En LAN real comprobar 2–4 jugadores, cartas propias distintas por destinatario, solo cantidades rivales, estados de desconexión, cierre del host y limpieza al crear una sala nueva.

## 8. Posibles errores

| Síntoma | Diagnóstico |
|---|---|
| No aparece textura o error de SVG | Copiar también Assets/Table y permitir su importación |
| No se encuentra CartaArtwork | Falta el nuevo script o volver a compilar |
| GetNode no encuentra una zona | Script/escena de distinta revisión; sustituir ambos |
| Juegos rivales vacíos | No hay Melds publicados; no son manos privadas |
| Mazo con un solo dorso | Representa el mazo completo; cantidad real debajo |
| Pozo vacío junto a oferta visible | Correcto: el servidor todavía no ha descartado la oferta |
| Continúa intercambio inicial | Esperado: aún no hay controles para jugar cartas |
| Mucho contenido requiere desplazamiento | Usar Scroll; maximizar permite una distribución más amplia |
| No aparecen retratos | Se usan iniciales; los perfiles con fotografía no forman parte de esta fase |
| Cartas viejas al salir | Revisar View null y limpieza; nunca leer directamente manos del dominio en el cliente |

## 9. Estado de la fase

La presentación revisada y los diagnósticos compilan contra GodotSharp/SourceGenerators 4.7.2 con nullable y advertencias como errores. Las rutas y tipos de los nodos han sido revisados. La aceptación de la escena .NET y su legibilidad queda pendiente de ejecución en tu instalación.

FASE COMPLETADA: NO; implementación terminada, aceptación visual y prueba actualizada pendientes.
BLOQUEADORES: el entorno del asistente no confirma ejecución de la escena .NET completa.
SIGUIENTE FASE: la que indiques después de aceptar esta entrega; no se implementó.

Referencias oficiales consultadas:
- [Dibujo 2D en C#](https://docs.godotengine.org/en/stable/tutorials/2d/custom_drawing_in_2d.html).
- [CanvasItem](https://docs.godotengine.org/en/stable/classes/class_canvasitem.html).
- [TextureRect](https://docs.godotengine.org/en/stable/classes/class_texturerect.html).
- [FlowContainer](https://docs.godotengine.org/en/stable/classes/class_flowcontainer.html).
