# Singleplayer local contra bots

## 1. Objetivo de la fase

Habilitar una partida local para una persona y entre uno y tres bots, con cuatro dificultades. La Mesa y las reglas son las mismas que en LAN. Se incorporan configuración, decisiones de bots, finalización, nueva partida y retorno al menú.

## 2. Decisiones arquitectónicas

**Una Mesa, dos tipos de sesión.** `Mesa` recibe `IGameSession`; este contrato proporciona una vista permitida, estado de sesión, envío de intenciones y sus respuestas. `NetworkManager` sigue implementando la sesión LAN. `LocalGameSession` implementa el mismo contrato mediante llamadas locales, sin crear `ENetMultiplayerPeer`, abrir sockets ni enviar RPC.

**Una sola autoridad de reglas.** `LocalMatchController` reutiliza `ServerSession`, `GameManager` y `RulesValidator`. El nombre `ServerSession` identifica el servicio de autoridad ya existente; su núcleo es C# puro y no necesita un servidor de red. Las acciones humanas y de bots atraviesan los mismos controles de fase, propiedad de cartas, turno, ventana de acción y combinaciones. No se replica ni se reescribe el motor para Singleplayer.

**Información limitada por participante.** Cada `BotBrain` recibe exclusivamente un `SnapshotDto` preparado para su jugador. No recibe el motor, otras manos, el mazo ordenado ni la semilla del reparto. La Mesa recibe únicamente la vista humana. Las manos reveladas al finalizar siguen la política del motor existente. Los arreglos que salen del controlador y los comandos que se encolan se copian para evitar modificaciones externas accidentales.

**Decisiones locales diferidas.** `LocalGameSession.Submit` devuelve el identificador de la intención y procesa después su resultado en `_Process`. Esto conserva el comportamiento de la Mesa, que espera registrar primero su acción pendiente. Los bots actúan por pasos separados; la interfaz no duerme esperando su turno. Cerrar la sesión elimina la cola y cancela sus decisiones posteriores.

**Dificultad mediante estrategia, no información adicional.**

| Nivel | Diferencia principal |
| --- | --- |
| Fácil | Escoge combinaciones legales de forma sencilla y entrega/paga una carta de su mano con variación aleatoria. |
| Medio | Prioriza combinaciones y conserva cartas con potencial para formar juegos. |
| Difícil | Compara agrupaciones compatibles y considera si un pago ayuda a juegos rivales visibles. |
| Experto | Añade memoria de cartas públicas a esa evaluación y penaliza más los pagos que favorecen al rival. |

La búsqueda tiene un límite de 8192 estados por decisión. No se promete juego óptimo ni un porcentaje de victorias específico. Todos los niveles cumplen las mismas reglas, incluidas victorias iniciales, diez cartas combinadas sin cartas sueltas y la obligación de utilizar una carta reclamada. Los bots no transfieren cartas entre juegos ya publicados en esta versión; el humano conserva esa función del motor.

Las respuestas privadas de bots que no cambian la vista humana no provocan una reconstrucción de Mesa. Así, preparar una carta para el intercambio o arrastrarla no se cancela porque otro participante haya respondido en secreto.

## 3. Dependencias

- Proyecto anterior con carga, `MenuPrincipal`, lobby, Mesa y Drag & Drop instalados.
- `GameManager`, modelos de dominio y `RulesValidator` de las fases anteriores.
- Contratos de acción/vista y `ServerSession` del núcleo de autoridad existente.
- Godot .NET y el SDK que ya compilan tu proyecto; no se incorpora un paquete externo de IA.
- Los recursos visuales de Mesa siguen siendo los que ya utiliza el proyecto.

## 4. Implementación

Los archivos completos están incluidos en las rutas del paquete. No es necesario copiar fragmentos de esta guía.

| Ruta `res://` | Responsabilidad |
| --- | --- |
| `Scripts/Sessions/IGameSession.cs` | Contrato mínimo que consume la presentación. |
| `Scripts/Singleplayer/Core/LocalMatchOptions.cs` | Nombre, cantidad de bots y enumeración `BotDifficulty`; valida las opciones. |
| `Scripts/Singleplayer/Core/LocalMatchController.cs` | Crea los participantes, inicia el motor, procesa acciones humanas y coordina decisiones privadas de cada bot. |
| `Scripts/Singleplayer/Core/BotAction.cs` | Intención de un bot y contexto deducido de acciones públicas. |
| `Scripts/Singleplayer/Core/BotBrain.cs` | Decide únicamente con su mano y la información pública; genera acciones para que las valide la autoridad. |
| `Scripts/Singleplayer/LocalGameSession.cs` | Adaptador Godot, cola de acciones, intervalos, eventos y cierre. |
| `Scripts/Presentation/SingleplayerSetup.cs` | Formulario de nombre, bots y dificultad; comunica la configuración a Main. |
| `Scenes/Singleplayer.tscn` | Escena del formulario. |
| `Scripts/Presentation/Main.cs` y `Scenes/Main.tscn` | Carga la pantalla nueva y dirige Inicio → Singleplayer → Mesa. |
| `Scripts/Presentation/StartupMenu.cs` y `Scenes/MenuPrincipal.tscn` | Habilitan el botón Singleplayer. |
| `Scripts/Presentation/Mesa*.cs` | Consumen la sesión compartida y presentan salida local y nueva partida. |
| `Scripts/Presentation/Mesa.InitialDeclarations.cs` | Permite preparar y enviar las cinco declaraciones iniciales a la misma autoridad. |
| `Scripts/Networking/NetworkManager.cs` | Adapta la sesión LAN al contrato de Mesa, manteniendo su ciclo de conexión. |
| `Scripts/Networking/Core/ServerSession.cs` | Conserva la autoridad compartida; admite un proveedor de mazo para pruebas reproducibles. |
| `Scripts/Diagnostics/SingleplayerSmoke.cs` y `Scenes/Diagnostics/SingleplayerSmoke.tscn` | Diagnóstico de integración del recorrido local real. |
| `Scripts/Diagnostics/MenuNavigationSmoke.cs` | Regresión del menú con Singleplayer habilitado. |

`Scripts/Singleplayer/Core` no depende de Godot. La semilla opcional de diagnóstico se usa en el reparto; no se entrega a los bots. La configuración normal realiza un reparto nuevo en cada partida.

## 5. Configuración Godot

Las escenas ya llegan configuradas. No añadas autoloads, RPC ni nodos de réplica para Singleplayer. Conserva `res://Scenes/Main.tscn` como escena principal.

Árbol de Main en ejecución:

```text
Main : Control [Main.cs]
├── NetworkManager : Node [NetworkManager.cs]
├── LocalGameSession : Node [LocalGameSession.cs]
├── Background : TextureRect
└── Margin : MarginContainer
    └── Screens : Control
        ├── Carga : Control [LoadingScreen.cs]
        ├── MenuPrincipal : Control [StartupMenu.cs]
        ├── Lobby : Control [Lobby.cs]
        ├── Mesa : Control [Mesa.cs y sus parciales]
        └── Singleplayer : Control [SingleplayerSetup.cs]
```

Excepto Carga, estas pantallas se instancian durante la carga de Main. No las añadas otra vez manualmente al árbol de `Main.tscn`.

Árbol de `Singleplayer.tscn`:

```text
Singleplayer : Control
└── Scroll : ScrollContainer
    └── Center : CenterContainer
        └── Panel : PanelContainer
            └── Padding : MarginContainer
                └── Content : VBoxContainer
                    ├── TitleLabel : Label
                    ├── SubtitleLabel : Label
                    ├── LocalLabel : Label
                    ├── Separator : HSeparator
                    ├── NameLabel : Label
                    ├── NameInput : LineEdit
                    ├── BotCountLabel : Label
                    ├── BotCountInput : OptionButton
                    ├── DifficultyLabel : Label
                    ├── DifficultyInput : OptionButton
                    ├── DifficultyHint : Label
                    ├── FairPlayLabel : Label
                    ├── MessageLabel : Label
                    ├── StartButton : Button
                    └── BackButton : Button
```

Configuración incluida: raíz y Scroll ocupan el espacio disponible; no hay desplazamiento horizontal; el panel se adapta a la anchura; `NameInput` admite hasta 24 caracteres; botones de 44–52 píxeles de altura mínima; tema `Assets/UI/MenuTheme.tres`. Las opciones se rellenan una sola vez desde `_Ready`. Por defecto se seleccionan un bot y dificultad Medio.

No debes conectar señales adicionales en el Inspector. El script conecta `Pressed`, `TextChanged`, `TextSubmitted`, `ItemSelected` y `Resized`. Main escucha `StartRequested` y `BackRequested`; Mesa escucha `ViewChanged`, `ActionCompleted` y `SessionChanged`. `BotDelayMilliseconds` es una propiedad de código con valor normal de 650 ms; los diagnósticos pueden reducirla, pero no se expone como regla o dificultad al jugador.

`RematchButton` se crea en ejecución bajo `Mesa/Scroll/Padding/Content/TopBar`; solo se muestra al finalizar una partida local. Mesa conserva sus zonas de mazo, pozo, oferta, juegos y mano. Bajo `Mesa/Scroll/Padding/Content/ContextActions` se crean `InitialVictoryType : OptionButton`, `InitialVictoryHint : Label` y `DeclareInitialButton : Button`. Sus señales `ItemSelected` y `Pressed` preparan y envían la intención, respectivamente; el selector no modifica por sí mismo la partida.

Durante las declaraciones iniciales, elige A–2–3–4–5, 10–J–Q–K–A, Cuarta, Color o Peladilla. Para las tres primeras selecciona las cartas en el orden de tu mano; Color y Peladilla evalúan toda tu mano de nueve cartas. En la primera oferta solo pueden declararse las tres primeras y hay que preparar un juego que incluya esa carta. La autoridad confirma la victoria o aplica la penalización acordada si la declaración es incorrecta.

## 6. Integración

Flujo de una acción:

```text
Gestos y controles de Mesa
    → IGameSession.Submit
    → cola de LocalGameSession
    → LocalMatchController
    → ServerSession → GameManager → RulesValidator
    → vista permitida del humano y respuesta
    → Mesa representa el resultado
```

Cada bot recorre la misma autoridad, partiendo de su propia vista. El controlador nunca usa al bot para decidir en nombre del humano. Un jugador humano penalizado puede observar el resto de la partida; el motor conserva el procedimiento de pagos automáticos acordado.

Inicio no abre una sesión por anticipado. Entrar en Singleplayer abre primero el formulario. Empezar vincula Mesa a `LocalGameSession`; salir borra su estado y devuelve al formulario. Entrar en Multijugador vuelve a vincular Mesa a `NetworkManager`. No se puede iniciar simultáneamente una sesión local y otra LAN desde Main.

La nueva partida conserva configuración y participantes, usa la preparación del motor existente y respeta la rotación del jugador que inicia. No conserva juegos publicados, selecciones, manos reveladas ni memoria de cartas de la ronda anterior.

Diagrama: `res://Docs/Singleplayer.puml`.

## 7. Prueba de aceptación

Desde PowerShell, dentro de tu carpeta `desmoche`, compila:

```powershell
dotnet build .\Desmoche.csproj
```

Después ejecuta con F5 y verifica:

1. Carga → Inicio → Singleplayer. Aparecen nombre, cantidad de bots y cuatro dificultades.
2. Un nombre vacío impide empezar. Con nombre válido, prueba 1, 2 y 3 bots: deben aparecer 2, 3 y 4 jugadores en total, con nueve cartas cada uno.
3. Antes del intercambio, el mazo conserva 34, 25 o 16 cartas respectivamente y el pozo está vacío. Solo ves tu mano.
4. Arrastra tu carta de cambio y espera: las respuestas privadas de bots no deben borrar tu selección. Confirma; cada jugador sigue teniendo nueve cartas.
5. En declaraciones iniciales, comprueba el selector de cinco tipos; elegir una opción no envía la declaración. Si no declaras, continúa al turno. Roba, responde a ofertas, prepara juegos y paga con los mismos controles de la Mesa multijugador. Los bots deben responder sin que tengas que pulsar por ellos.
6. Si la carta no sirve a nadie, comprueba el avance correspondiente y la excepción de la primera oferta. Una carta reclamada debe utilizarse en un juego.
7. Al terminar, el resultado debe permanecer visible. Pulsa Otra partida: vuelve al intercambio con nueve cartas y sin juegos/revelaciones anteriores.
8. Sal de partida, cambia dificultad o bots y comienza otra. Después vuelve al Inicio y entra en Multijugador: no debe aparecer la partida local anterior.
9. Repite una partida con Wi-Fi desconectado. Singleplayer no solicita IP ni necesita conexión de red.

Para ejecutar los diagnósticos integrados, abre `res://Scenes/Diagnostics/SingleplayerSmoke.tscn` y pulsa **F6**. Espera `SINGLEPLAYER PASS` en la consola. La prueba usa una instancia real de Main en un SubViewport, realiza doce configuraciones, completa una partida con acciones públicas y comprueba nueva partida y limpieza. Termina por sí sola. Después vuelve a usar F5 para jugar normalmente.

La escena `res://Scenes/Diagnostics/MenuNavigationSmoke.tscn` debe terminar con `MENU NAVIGATION PASS`. Con render normal introduce eventos de ratón en la GUI de Godot; en modo headless usa las señales públicas. Los diagnósticos no mueven el cursor del sistema.

Resultados y comandos adicionales de las pruebas puras: consulta el archivo `VERIFICACION.md` del paquete. Estas pruebas verifican funcionamiento e invariantes, no garantizan que el balance subjetivo de cada dificultad sea definitivo.

## 8. Posibles errores

| Síntoma | Diagnóstico y solución |
| --- | --- |
| Singleplayer sigue deshabilitado | Se abrió otra copia del proyecto o falta reemplazar `MenuPrincipal.tscn`/`StartupMenu.cs`. Verifica el directorio de `project.godot` y recompila. |
| No se encuentra LocalGameSession o Singleplayer | Copia también las escenas y todos los scripts del paquete, conservando las carpetas. |
| No permite empezar | Revisa que el nombre no esté vacío y que no haya otra partida o sesión LAN activa. |
| Los bots parecen esperar | Lee la fase: el intercambio, las declaraciones y las ofertas también requieren tu respuesta. El controlador nunca contesta por ti. |
| Un pago o juego se rechaza | Lee el mensaje del motor; revisa propiedad de cartas, orden y fase. No se cambia el estado hasta aceptar la acción. |
| La compilación muestra DLL en uso | Detén la partida y cierra instancias de diagnóstico antes de volver a compilar. |
| Persisten errores al actualizar | Conserva el primer mensaje completo de compilación/Godot y la ruta del proyecto abierto; no borres ni reescribas las reglas para resolver un error de carga. |

## 9. Estado de la fase

FASE COMPLETADA: SÍ. Implementación, compilación, pruebas del motor, recorrido visual y regresión LAN aprobados el 11 de octubre de 2026. Resultados detallados en `VERIFICACION.md` del paquete.

BLOQUEADORES: ninguno para jugar en PC. El balance de dificultad puede ajustarse con pruebas de uso; no se ha certificado una exportación móvil en esta entrega.

SIGUIENTE FASE: pruebas de uso y ajuste de dificultad si lo solicitas. No se añade persistencia de partidas ni una fase de exportación móvil en esta entrega.
