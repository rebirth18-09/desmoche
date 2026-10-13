> Actualización 12/10/2026: las reglas de victoria, intercambio, Pasa y acceso por código se detallan en [RevisionReglasCodigoSala.md](RevisionReglasCodigoSala.md). Esa revisión prevalece sobre las instrucciones históricas de este documento.

# Menús de salón — diseños elegidos

## 1. Objetivo de la fase

Sustituir la presentación de Inicio, opciones Multijugador, Crear partida, Unirse a partida y configuración contra bots por los diseños elegidos en el chat. Inicio usa el salón de casino; Multijugador usa dos tarjetas; Crear y Unirse usan formularios centrales; contra bots usa tres tarjetas y cuatro dificultades visibles. El lobby conectado adopta el mismo tema para que no exista un salto de estilo.

La referencia visual se interpreta como una interfaz real: los textos, campos, selección, errores y botones son controles de Godot. No se utiliza una captura completa del menú como fondo ni se colocan zonas invisibles encima de texto incrustado.

## 2. Decisiones arquitectónicas

Se conservan `Main`, su ciclo de carga y la ruta `/root/Main/NetworkManager`. Los formularios siguen siendo estados de `Lobby.tscn`, no copias independientes que creen otra instancia de red. Sus botones continúan llamando a `Host`, `Join`, `Close` y `Submit(StartMatch)`.

`MenuChoiceSelector : HBoxContainer` sustituye exclusivamente los dos `OptionButton` de configuración local. Presenta selección exclusiva mediante botones, conserva IDs 1–3 para bots y 0–3 para `BotDifficulty`, y emite `ItemSelected(long)`. `SingleplayerSetup` sigue creando el mismo `LocalMatchOptions`; no interpreta reglas. El selector de victorias iniciales de Mesa sigue siendo un `OptionButton`.

El salón se añade como un fondo de navegación. Cuando aparece Mesa, se ocultan `SalonBackground` y `MenuFrame`: permanecen el fondo anterior y la presentación de partida existente. No se modifican el dominio, GameManager, bots, sesiones, ENet, RPC ni privacidad.

Se hacen funcionales los dos controles presentes en el diseño de Inicio: Cómo jugar abre una guía breve derivada del contexto vigente; el engranaje abre el ajuste de pantalla completa. Este ajuste solo dura la sesión. No se añade persistencia, una selección de puerto, descubrimiento de salas ni audio nuevo.

## 3. Dependencias

Base: repositorio `rebirth18-09/desmoche`, commit `8f5fd1c7c5a97fe9847f7ea470feafc701273d15`. Godot 4.7.2 .NET y proyecto `net8.0`, sin cambiar `Desmoche.csproj` ni `project.godot`. La validación utilizó SDK .NET 9.0.308 y runtime .NET 8.

Las tipografías Libre Baskerville y Lato están incluidas en `Assets/UI/Salon/Fonts`, con sus licencias SIL Open Font License. El diseño no depende de que Windows tenga instalada Georgia u otra fuente. Godot importa las fuentes y los SVG como recursos nativos.

## 4. Implementación

| Ruta dentro de `desmoche` | Responsabilidad |
| --- | --- |
| `Assets/UI/Salon/SalonBackground.png` | Salón y cartas ornamentales sin textos ni botones. Fondo generado usando la referencia seleccionada. |
| `Assets/UI/Salon/*.svg` | Panel, botones dorados y verdes en estados normal/hover/pulsado, separador, personas, mesa y engranaje. Vectores editables. |
| `Assets/UI/Salon/Fonts/*` | Fuentes y licencias incluidas. |
| `Assets/UI/SalonTheme.tres` | Estilos comunes, variantes PrimaryButton/MenuLink/BotChoice/DifficultyChoice/GoldTitle/SerifHeading. |
| `Scenes/Main.tscn` | Añade el salón y el marco sin cambiar la raíz de red. |
| `Scenes/MenuPrincipal.tscn` | Inicio, reglas y ajustes. |
| `Scenes/Lobby.tscn` | Dos tarjetas, crear, unirse, conexión y sala activa. |
| `Scenes/Singleplayer.tscn` | Nombre, tres cantidades y cuatro dificultades visibles. |
| `Scripts/Presentation/SalonFrame.cs` | Marco ornamental con márgenes constantes al redimensionar. |
| `Scripts/Presentation/MultiplayerChoices.cs` | Dos tarjetas en columnas; las apila en ventanas estrechas. |
| `Scripts/Presentation/MenuChoiceSelector.cs` | Botones de selección exclusiva. |
| `Scripts/Presentation/StartupMenu.cs` | Navegación existente, paneles de reglas/ajustes y adaptación de Inicio. |
| `Scripts/Presentation/Lobby.cs` | Cambia ancho y marco según estado; conserva conexión y validación. |
| `Scripts/Presentation/SingleplayerSetup.cs` | Lee los selectores nuevos, actualiza ayuda y conserva la solicitud local. |
| `Scripts/Presentation/Main.cs` | Oculta el salón y el marco al entrar en Mesa. |
| `Scripts/Diagnostics/MenuNavigationSmoke.cs` | Regresión de botones, formularios, reglas, ajustes y tamaños. |
| `Scripts/Diagnostics/SingleplayerSmoke.cs` | Pulsa las opciones visibles y verifica sus IDs y exclusividad. |

Los recursos `.import` y los `.cs.uid` de los componentes nuevos conservan su identidad. No se incluyen cachés `.godot`, DLL, binarios de validación, `bin` ni `obj`.

## 5. Configuración Godot

Abrir el `project.godot` del proyecto existente, permitir la importación y compilar C# antes de F5. No es necesario añadir autoloads ni conectar señales en el Inspector. Se conserva el tamaño base de ventana del proyecto y su renderizador.

Árbol añadido a Main:

```text
Main [Main.cs]
├── NetworkManager [misma instancia y ruta]
├── LocalGameSession [misma instancia]
├── Background [fondo anterior]
├── SalonBackground [TextureRect; IgnoreSize / KeepAspectCovered; ignora ratón]
├── MenuFrame [Control; SalonFrame.cs; ignora ratón]
└── Margin/Screens [carga e instancias de pantallas existentes]
```

Las rutas del lobby que consumen los scripts y diagnósticos se conservan bajo `Scroll/Center/Panel/Padding/Content/`:

```text
OptionsPanel [Control; MultiplayerChoices.cs]
├── CreateCard / Padding / Content [icono, título y ayuda]
├── JoinCard / Padding / Content [icono, título y ayuda]
├── CreateOption [Button; ruta conservada]
├── JoinOption [Button; ruta conservada]
└── BackButton [Button]
CreatePanel [VBoxContainer; NameInput y Buttons conservados]
JoinPanel [VBoxContainer; NameInput, IPInput y Buttons conservados]
SessionPanel [VBoxContainer; estado, dirección, jugadores y botones conservados]
```

Los botones se mantienen directamente bajo OptionsPanel para conservar sus contratos; `MultiplayerChoices` los coloca sobre cada tarjeta. Los elementos ornamentales no capturan ratón.

Singleplayer conserva el árbol central, con `BotCountInput` y `DifficultyInput` de tipo `HBoxContainer` y script `MenuChoiceSelector`. `BotCount = true` en el Inspector del primero y `false` en el segundo. Cada selector crea `Choice0`, `Choice1`, etc. durante `_Ready`; no deben añadirse botones duplicados manualmente.

Inicio conserva `Scroll/Center/Content/Buttons/MultiplayerButton`, `SingleplayerButton` y `QuitButton`. Añade RulesButton, SettingsButton y dos paneles ocultos HelpOverlay/SettingsOverlay. Los nombres internos Singleplayer y CurrentScreen se conservan; el texto visible se traduce a Jugar contra bots.

| Señal o evento | Conexión |
| --- | --- |
| `StartupMenu.MultiplayerRequested / SingleplayerRequested / QuitRequested` | Main, como antes. |
| `RulesButton.Pressed / SettingsButton.Pressed` | Abren sus paneles y entregan foco. |
| `FullscreenButton.Toggled` | Cambia únicamente `Window.Mode`. |
| `MenuChoiceSelector.ItemSelected(long)` de dificultad | Actualiza el texto de estrategia en SingleplayerSetup. |
| `StartRequested(LocalMatchOptions)` | Main inicia la sesión local existente. |
| `BackRequested` | Main vuelve al Inicio, como antes. |
| `Resized` | Ajusta paneles y tarjetas; una ventana corta conserva desplazamiento vertical. |

Tab permite recorrer controles; Enter activa botones; Esc vuelve de formularios o cierra reglas/ajustes. Los selectores no pueden quedar sin selección ni con dos opciones activas. Nombre vacío impide empezar. El origen de la IP sigue siendo quien crea la sala.

## 6. Integración

El paquete es una actualización por superposición. Copiar el contenido de su carpeta `desmoche` sobre la carpeta del proyecto donde se encuentra `project.godot`, conservando subdirectorios y aceptando reemplazos. No importar la carpeta de actualización como si fuera un juego completo: depende del proyecto existente.

La guía y el instalador opcional del paquete explican las dos formas de aplicación. Tras copiar, Godot reimporta recursos; compilar C# y ejecutar F5. La navegación es Inicio → Multijugador → Crear/Unirse → sala → Mesa, o Inicio → Jugar contra bots → Mesa. Los mensajes de error, cancelación y desconexión permanecen operativos.

## 7. Prueba de aceptación

Los resultados ejecutados están en `VERIFICACION.md` del paquete. Se verificaron compilación, importación, navegación con entrada GUI, doce configuraciones locales y regresión host/cliente entre procesos. Las capturas de `Evidence` proceden de Godot y muestran la implementación, no nuevas imágenes conceptuales.

En el equipo de destino: comprobar Inicio; abrir/cerrar Cómo jugar y Ajustes; entrar a las dos tarjetas; probar nombre/IP inválidos y volver; crear y unirse desde dos dispositivos; comenzar contra 1, 2 y 3 bots en los cuatro niveles; salir y volver; redimensionar la ventana. La prueba entre procesos no certifica una LAN física, firewall ni una exportación móvil.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| Falta fondo, fuente o estilo | Copiar también Assets/UI/Salon y SalonTheme.tres, esperar la importación y comprobar que se abrió la copia correcta. |
| El script no encuentra MenuChoiceSelector | Copiar todos los C# y UID incluidos; detener el juego y recompilar. |
| Aparecen los menús anteriores | Reemplazar las escenas y scripts juntos, conservando sus rutas. |
| Un menú corto necesita desplazamiento | Es deliberado para mantener campos y botones legibles; Tab o el ratón permiten recorrerlo. |
| Error de IP o sala | Se conserva la conexión manual y sus errores; todos los dispositivos necesitan la misma versión y la misma red local. |
| Una jugada se rechaza | Leer la validación del motor; esta entrega no modifica reglas ni autoridad. |

## 9. Estado de la fase

FASE COMPLETADA: SÍ. Diseños seleccionados implementados como controles reales, compilación e integración verificadas y paquete de actualización preparado.

BLOQUEADORES: ninguno para entregar e instalar esta revisión de menús. La aceptación en la PC del usuario y una conexión entre dispositivos físicos siguen siendo comprobaciones del entorno de destino.

SIGUIENTE FASE: la que indique el usuario; no se añade otra fase de desarrollo automáticamente.
