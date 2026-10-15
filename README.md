# Desmoche

Juego de cartas 2D en **Godot 4 .NET/C#**, con partidas de **2 a 4 jugadores**. Permite jugar por LAN/Wi-Fi o completamente en local contra bots. Ambas modalidades comparten las reglas y la Mesa.

El proyecto se construye por fases. El contrato para continuar el trabajo está en [CONTEXTO_PROYECTO.md](CONTEXTO_PROYECTO.md); [AGENTS.md](AGENTS.md) resume las instrucciones para asistentes de desarrollo.

## Estado actual

- Modelo de dominio, validación determinista y máquina de estados de partida.
- Multijugador ENet con host autoritativo, lobby por código LAN, límite de cuatro jugadores y gestión de desconexiones.
- Réplica pública mediante `MultiplayerSpawner`/`MultiplayerSynchronizer` y manos privadas por destinatario.
- Pantalla de carga, menú de inicio y formularios de crear/unirse a sala.
- Mesa compartida, cartas visuales, selección múltiple, orden por arrastre, preparación de combinaciones y pago con confirmación.
- Singleplayer con **1–3 bots**, dificultades **Fácil, Medio, Difícil y Experto**, nueva partida y regreso al menú.
- Ajustes de partida para cuatro tiempos y cinco victorias iniciales; el creador configura, los invitados consultan y la revancha conserva las opciones (protocolo **8**).

Las fases 0–9 y la ampliación Singleplayer están implementadas. Los diseños visuales siguen siendo provisionales. La interfaz se adapta a escritorio y móviles en horizontal; la exportación y aceptación en Android/iOS físicos siguen pendientes.

## Estructura

```text
.
├── README.md
├── CONTEXTO_PROYECTO.md
├── AGENTS.md
├── desmoche/                     # Proyecto Godot: abrir project.godot
│   ├── Assets/
│   ├── Docs/                    # Contratos, árboles, aceptación y PlantUML
│   ├── Scenes/
│   └── Scripts/
├── Desmoche.Pruebas/             # Dominio, reglas, partida, red y réplica
├── Desmoche.Singleplayer.Pruebas/ # Motor local, bots y simulaciones
└── Desmoche.Interaccion.Pruebas/  # Preparación visual sin Godot UI
```

Las carpetas de pruebas deben permanecer junto a `desmoche`: sus proyectos enlazan los archivos del juego mediante rutas relativas. No se copian dentro de `res://`.

## Preparar y ejecutar

Base técnica de esta revisión: **Godot 4.7.2 .NET**, `Godot.NET.Sdk/4.7.2` y SDK .NET **9.0.308** utilizado en la verificación. El proyecto de escritorio y las consolas apuntan a `net8.0`; el `.csproj` contiene una condición `net9.0` para Android. Necesitas un SDK compatible y, para ejecutar las consolas, el runtime de .NET 8. La edición estándar de Godot no ejecuta los scripts C#.

1. Clona o descarga el repositorio conservando su estructura.
2. En Godot **.NET**, importa `desmoche/project.godot` y espera la importación de recursos.
3. Compila y ejecuta con **F5**. La escena principal es `res://Scenes/Main.tscn`.

Desde PowerShell en la raíz del repositorio también puedes compilar:

```powershell
dotnet build .\desmoche\Desmoche.csproj
```

Singleplayer funciona sin conexión: elige nombre, dificultad y cantidad de bots. Para LAN, el host crea una sala y comparte su **código de seis caracteres**; los demás lo introducen desde Buscar sala. Todos deben usar la misma revisión (protocolo 8) y estar en la misma red. El descubrimiento utiliza **UDP 7001** y la partida ENet **UDP 7000**. No hay servidor externo.

## Pruebas

Desde la raíz del repositorio:

```powershell
dotnet run --project .\Desmoche.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Interaccion.Pruebas\Pruebas.csproj
dotnet run --project .\Desmoche.Singleplayer.Pruebas\Pruebas.csproj -- --quick
```

Para el conjunto completo de Singleplayer, omite `-- --quick`. Las pruebas puras no necesitan abrir Godot. Para comprobar la integración, abre en Godot una escena de diagnóstico y pulsa **F6**:

- `res://Scenes/Diagnostics/SingleplayerSmoke.tscn`: espera `SINGLEPLAYER PASS`.
- `res://Scenes/Diagnostics/MenuNavigationSmoke.tscn`: espera `MENU NAVIGATION PASS`.
- `res://Scenes/Diagnostics/OfferAndFinalHandsSmoke.tscn`: con render real, espera `OFFER AND FINAL HANDS PASS`; prueba oferta elegida, manos finales y orden del mazo restante.
- `res://Scenes/Diagnostics/MandatoryOfferSmoke.tscn`: con render real, espera `MANDATORY OFFER PASS`; prueba denuncia y vencimiento conectados a la autoridad real.
- `res://Scenes/Diagnostics/HandSelectionSmoke.tscn`: con render real, espera `HAND SELECTION PASS`; comprueba elevación, indicador y gestos.
- `res://Scenes/Diagnostics/HandDragSmoke.tscn`: con render real, espera `HAND DRAG PASS`; comprueba deslizamiento, destino marcado, cancelación y siete resoluciones.
- `res://Scenes/Diagnostics/ResponsiveLayoutSmoke.tscn`: espera `RESPONSIVE LAYOUT PASS`; verifica siete resoluciones con la ventana real.
- `res://Scenes/Diagnostics/DealAnimationSmoke.tscn`: espera `DEAL ANIMATION PASS`.
- `res://Scenes/Diagnostics/DealPerformanceSmoke.tscn`: mide el reparto; `--assert-smooth` comprueba el cierre sin reactivaciones ni reconstrucción de la mano.
- Los diagnósticos de red y Mesa tienen instrucciones específicas en los documentos enlazados abajo.

En la verificación de la entrega Singleplayer del **11 de octubre de 2026** pasaron la compilación sin errores ni advertencias, **63 738 comprobaciones de bots con 168 partidas completas**, los diagnósticos de interfaz y la regresión LAN entre dos procesos. Es evidencia de esa revisión; vuelve a ejecutar las pruebas después de modificar código. La conexión entre procesos de una PC no sustituye una prueba entre dispositivos físicos.

Verificación de esta subida (11 de octubre de 2026): compilación **0 errores / 0 advertencias**, consola general aprobada, interacción **435 comprobaciones** y Singleplayer rápido **12 633 comprobaciones / 36 partidas completas**. Se actualizó una expectativa antigua de la prueba de interacción para incluir las declaraciones iniciales; el código del juego se conserva como en la instalación. No se repitieron pruebas visuales ni LAN entre dispositivos durante esta subida.

La revisión del **12 de octubre de 2026** incorpora intercambio con ocho cartas pendientes, victoria automática con diez combinadas, declaraciones iniciales corregidas y Pasa durante dos segundos. Pasaron compilación (0 errores/0 advertencias), núcleo, 465 comprobaciones de interacción, 4.144 comprobaciones de bots y 170 partidas, diagnósticos de Godot con render/arrastre y conexión por código con cuatro procesos. Los detalles y el alcance de estas pruebas están en [RevisionReglasCodigoSala.md](desmoche/Docs/RevisionReglasCodigoSala.md).

La revisión posterior del **12 de octubre de 2026** muestra las cartas de victoria inicial en la mesa, coloca Pasa junto al grupo durante tres segundos y excluye al pagador de reclamar su pago. Conserva las victorias iniciales de los bots por decisión del usuario. Compilación, núcleo, 170 simulaciones, diagnóstico visual y cuatro procesos LAN verificados; consultar [VictoriasVisiblesYPagos.md](desmoche/Docs/VictoriasVisiblesYPagos.md).

Verificación de integración del **15 de octubre de 2026**: compilación con **0 errores / 0 advertencias**, **4077 comprobaciones generales** más **1056 casos de escalera**, **750 de interacción** y **11 984 de bots / 38 partidas completas**. La base completa conserva recursos y fuentes anteriores e incorpora la entrega acumulativa de Ajustes de partida. Los diagnósticos visuales y ENet no se repitieron en esta integración; véase [VERIFICACION.md](VERIFICACION.md).

## Documentación técnica

La actualización de **Ajustes de partida** añade un engranaje en bots, Crear partida y la sala. Permite cambiar los cuatro tiempos y activar cada victoria inicial; el host comparte las opciones, los bots las respetan y la revancha las conserva. Requiere **protocolo 8**. Véase [AjustesDePartida.md](desmoche/Docs/AjustesDePartida.md).

La actualización de **burbujas y acciones en mesa** muestra círculos de tiempo junto a Pasa, la elección de carta/pozo y la preparación. Tomar/Rechazar, pago y preparación se sitúan junto a sus elementos, eliminando la fila inferior. Véase [BurbujasYAccionesEnMesa.md](desmoche/Docs/BurbujasYAccionesEnMesa.md).

La corrección de **Salir de partida** evita que el fondo del nuevo Menú bloquee los clics y da prioridad de input a TopBar. Se comprueba el cierre real de la sesión local, además de la llamada de salida en siete resoluciones. Véase [SalidaDePartida.md](desmoche/Docs/SalidaDePartida.md).

La actualización de **Edición en mesa** integra los borradores en Mi juego con marca dorada de Sin confirmar, oculta de la fila las cartas preparadas y conserva devolución/validación. Estado, cronómetros, acciones, Info y Menú quedan dentro de la pantalla, sin la página vacía inferior. Véase [EdicionEnMesa.md](desmoche/Docs/EdicionEnMesa.md).

La corrección de **pago preparado** muestra la carta solo en la casilla desde que se suelta, cierra el espacio de la mano y la mantiene allí mientras espera confirmación. Cancelar, reemplazarla o recibir un rechazo la devuelve. La misma casilla aplica el comportamiento al cambio inicial; cantidades y reglas siguen siendo autoritativas. Véase [PagoPreparado.md](desmoche/Docs/PagoPreparado.md).

La actualización de **arrastre suave** oculta el origen mientras la carta sigue al cursor, desliza las vecinas y muestra un hueco dorado de destino. Confirma el orden al soltar sin reconstruir cartas; conserva selección, verde, borradores y la validación existente. Véase [ArrastreSuave.md](desmoche/Docs/ArrastreSuave.md).

La corrección de **manos finales en empate** habilita **Mostrar juego** para todos los rivales en cualquier partida terminada, junto con **Mostrar mazo**. La autoridad revela las manos únicamente en `Finished`; nueva partida y salida limpian la consulta. Requiere **protocolo 7** por compatibilidad de validación. Véase [ManosFinalesEnEmpate.md](desmoche/Docs/ManosFinalesEnEmpate.md).

La actualización del **14 de octubre de 2026** añade **Mostrar mazo** al finalizar, con el orden exacto de las cartas restantes y desplazamiento. Si nadie utiliza una oferta del mazo/pago que entra en un juego publicado, los demás tienen tres segundos para decir **Pasa** y eliminar al infractor; sin denuncia continúa y la carta pasa al pozo. Si otro la usa, se mantiene la prioridad sin denuncia. El protocolo pasa a **6**. Véase [MazoFinalYOfertaObligatoria.md](desmoche/Docs/MazoFinalYOfertaObligatoria.md).

La actualización de **oferta y manos finales** marca en verde la oferta elegida junto con las cartas propias que completan un juego, incluso con solo dos de mano. Al ganar, cada rival tiene **Mostrar juego** para consultar su mano restante en una ventana translúcida, sin tapar juegos publicados. Véase [OfertaYManosFinales.md](desmoche/Docs/OfertaYManosFinales.md).

La actualización de **selección animada** eleva las cartas de mano seleccionadas y las marca con contorno verde si toda la selección puede formar un juego, aunque esté desordenada. Pulsar un espacio vacío retira la selección. El indicador conserva orden, borradores y validación autoritativa. Véase [SeleccionAnimada.md](desmoche/Docs/SeleccionAnimada.md).

La actualización de **resolución adaptable** conserva la mesa y cartas a escala uniforme, usa una base 1280×720 con `canvas_items`/`expand`, coloca las acciones en filas fuera del tablero y respeta el área segura móvil. Móviles en horizontal; ayuda y ajustes desplazables. Véase [ResolucionAdaptable.md](desmoche/Docs/ResolucionAdaptable.md).

La revisión posterior de **fluidez del reparto** elimina las reactivaciones de cartas que ya llegaron y la reconstrucción de la mano al terminar. La medición aislada del cierre pasó de unos 52–55 ms a 4–7 ms. Se conservan el diseño y los tiempos. Véase [FluidezReparto.md](desmoche/Docs/FluidezReparto.md) para condiciones de medición, alcance y pruebas.

La actualización de **reparto clásico y trébol** del 13 de octubre de 2026 entrega nueve cartas por participante desde el mazo, una por asiento en cada ronda. Las propias giran al llegar y el cambio se habilita al terminar; los rivales muestran dorsos anónimos durante la animación. La revancha repite el reparto y los bots nativos esperan cuatro segundos antes de su primera decisión. El trébol presenta tres hojas redondas y tallo estrecho también en las esquinas. Véase [RepartoClasicoYTrebol.md](desmoche/Docs/RepartoClasicoYTrebol.md) para configuración y resultados de pruebas.

La corrección de **trasmoche** permite usar cartas de juegos propios publicados junto con la mano y una oferta/pozo, conservando un origen válido de al menos tres cartas. La actualización se confirma de forma atómica. Consulte [Trasmoche.md](desmoche/Docs/Trasmoche.md) para gestos, límites y pruebas.

La actualización del **13 de octubre de 2026** añade revancha multijugador con quince segundos y contador de confirmaciones, elección de carta en cinco segundos y preparación durante veinte desde su elección. El host resuelve los vencimientos y elige al azar un juego válido si el interesado no confirmó. El protocolo pasa a **5**: todos los equipos deben instalar la misma actualización. Véase [RevanchaYTemporizadores.md](desmoche/Docs/RevanchaYTemporizadores.md).

La corrección de **Peladilla** del 12 de octubre de 2026 invalida también las parejas y escaleras incompletas que una carta adicional podría completar. Se conserva el botón condicionado al orden visible y la penalización autoritativa de declaraciones falsas. Véase [Peladilla.md](desmoche/Docs/Peladilla.md), que sustituye la regla permisiva de las revisiones anteriores.

| Tema | Documento |
| --- | --- |
| Tiempos y victorias iniciales configurables | [AjustesDePartida.md](desmoche/Docs/AjustesDePartida.md) |
| Burbujas de tiempo y acciones junto a cartas/juegos | [BurbujasYAccionesEnMesa.md](desmoche/Docs/BurbujasYAccionesEnMesa.md) |
| Salida desde el Menú de Mesa | [SalidaDePartida.md](desmoche/Docs/SalidaDePartida.md) |
| Preparación y edición dentro de la mesa | [EdicionEnMesa.md](desmoche/Docs/EdicionEnMesa.md) |
| Carta preparada de pago sin duplicado | [PagoPreparado.md](desmoche/Docs/PagoPreparado.md) |
| Consulta de manos también en empate | [ManosFinalesEnEmpate.md](desmoche/Docs/ManosFinalesEnEmpate.md) |
| Arrastre suave y destino marcado | [ArrastreSuave.md](desmoche/Docs/ArrastreSuave.md) |
| Mazo final y obligación de jugar ofertas | [MazoFinalYOfertaObligatoria.md](desmoche/Docs/MazoFinalYOfertaObligatoria.md) |
| Oferta elegida y ventana de manos finales | [OfertaYManosFinales.md](desmoche/Docs/OfertaYManosFinales.md) |
| Selección elevada e indicador de juego | [SeleccionAnimada.md](desmoche/Docs/SeleccionAnimada.md) |
| Adaptación móvil y escritorio | [ResolucionAdaptable.md](desmoche/Docs/ResolucionAdaptable.md) |
| Fluidez y medición del cierre del reparto | [FluidezReparto.md](desmoche/Docs/FluidezReparto.md) |
| Reparto clásico y dibujo de trébol | [RepartoClasicoYTrebol.md](desmoche/Docs/RepartoClasicoYTrebol.md) |
| Trasmoche de juegos propios durante reclamos | [Trasmoche.md](desmoche/Docs/Trasmoche.md) |
| Revancha LAN y plazos de elección/preparación | [RevanchaYTemporizadores.md](desmoche/Docs/RevanchaYTemporizadores.md) |
| Corrección de Peladilla y proyectos con una carta | [Peladilla.md](desmoche/Docs/Peladilla.md) |
| Reglas actualizadas y código LAN | [RevisionReglasCodigoSala.md](desmoche/Docs/RevisionReglasCodigoSala.md) |
| Contexto y forma de trabajar | [CONTEXTO_PROYECTO.md](CONTEXTO_PROYECTO.md) |
| Máquina de estados y reglas de flujo | [Fase4.md](desmoche/Docs/Fase4.md) |
| Autoridad, comandos y conexión | [Fase5.md](desmoche/Docs/Fase5.md) |
| Réplica pública, manos privadas y coherencia | [Fase6.md](desmoche/Docs/Fase6.md) |
| Lobby LAN | [Fase7.md](desmoche/Docs/Fase7.md) |
| Mesa y cartas | [Fase8.md](desmoche/Docs/Fase8.md), [Fase9.md](desmoche/Docs/Fase9.md) |
| Revisión visual y controles por arrastre | [MesaCasino.md](desmoche/Docs/MesaCasino.md), [MesaDinamica.md](desmoche/Docs/MesaDinamica.md) |
| Carga e inicio | [InicioLobby.md](desmoche/Docs/InicioLobby.md) |
| Partida local y bots | [Singleplayer.md](desmoche/Docs/Singleplayer.md), [Singleplayer.puml](desmoche/Docs/Singleplayer.puml) |

Los documentos de fases anteriores registran el estado de sus respectivas entregas. Por ejemplo, la referencia histórica a Singleplayer deshabilitado en `InicioLobby.md` queda reemplazada por `Singleplayer.md`. Algunas instrucciones antiguas contienen rutas del equipo original; utiliza las rutas relativas de este README para la instalación actual.
