# Corrección de Peladilla — 12/10/2026

Esta revisión se basa en `main`, commit `a8841afdbacb6a6813ad8ae23615b7b42f29462e`. Sustituye únicamente la regla permisiva de Peladilla de las revisiones anteriores, por indicación expresa del usuario. El diseño de la mesa se conserva.

## 1. Objetivo de la fase

Después del cambio inicial y antes de la primera oferta, declarar Peladilla solo gana si las nueve cartas no contienen ningún juego y tampoco pueden formar uno añadiendo una sola carta. Separar o desordenar las cartas no cambia el resultado. Declarar con un juego o proyecto elimina al jugador conforme a la penalización existente.

El caso de la captura de Bot Bruno contiene dos nueves, dos reinas y Q-K de picas. Puede completar tercias o escaleras; ya no se acepta como victoria por Peladilla.

## 2. Decisiones arquitectónicas

La corrección se concentra en `RulesValidator.IsPeladilla`, compartido por `GameManager`, LAN y `BotBrain`. El motor autoritativo ya elimina una declaración inicial falsa; no se agrega otra regla en la interfaz ni otro motor para bots.

- Dos cartas del mismo rango bastan para excluir Peladilla: una tercera permite una tercia.
- Dos cartas del mismo palo dentro de una secuencia válida de tres bastan para excluirla: incluye consecutivas y parejas con un hueco.
- Se preserva el As bajo en A-2-3 y alto en Q-K-A; no se admite K-A-2.
- Se comprueba cualquier selección y orden de la mano. La alternancia de colores de una tercia puede acomodarse; un mal orden no autoriza Peladilla.
- La posibilidad es teórica respecto de las 52 cartas: se consideran identidades ausentes de la mano, sin consultar cartas ocultas del mazo ni de rivales. No depende de cuál carta concreta vaya a salir primero.

El botón conserva su criterio visual: aparece después del cambio cuando no hay un juego completo contiguo visible. Puede aparecer con parejas, proyectos o juegos ocultos; pulsarlo en esos casos es una declaración falsa y se penaliza. No se oculta ni deshabilita para proteger al jugador de esa decisión.

## 3. Dependencias

Godot 4.7.2 .NET y los proyectos C# existentes. Las pruebas enlazan el mismo código de dominio y autoridad mediante rutas relativas. No hay cambios de SDK, paquetes, protocolo, RPC ni acceso a información privada para bots.

## 4. Implementación

| Archivo | Cambio |
| --- | --- |
| `desmoche/Scripts/Domain/RulesValidator.cs` | Rechaza rangos repetidos desde dos cartas y ventanas de escalera con al menos dos presentes. |
| `desmoche/Scripts/Presentation/Mesa.InitialDeclarations.cs` | Explica juegos y proyectos en la ayuda del botón; conserva visibilidad y señal. |
| `desmoche/Scenes/MenuPrincipal.tscn` | Actualiza únicamente el texto de ayuda de Peladilla. |
| `Desmoche.Pruebas/RulesTests.cs` | Expectativas de parejas/proyectos, oráculo independiente en 133 manos y las 1.326 parejas posibles. |
| `Desmoche.Pruebas/RuleRevisionTests.cs` | Declaraciones falsas de parejas, consecutivas, huecos y mano de la captura con 2–4 jugadores; conservación, sanción y privacidad. |
| `Desmoche.Interaccion.Pruebas/ActionInteractionTests.cs` | Comprueba que una pareja sin juego visible permite mostrar el botón aunque la declaración sea falsa. |
| `Desmoche.Singleplayer.Pruebas/Program.cs` | Bots de las cuatro dificultades continúan con parejas/proyectos; verifica juegos actuales y una carta futura en la evidencia de victorias. |
| `README.md`, `CONTEXTO_PROYECTO.md`, documentos históricos | Regla vigente y aviso de prioridad de esta revisión. |

No se modifican `GameManager`, contratos de sesiones, snapshots ni permisos de información. Se reutilizan su validación y penalización existentes.

## 5. Configuración Godot

No crear nodos ni cambiar el Inspector. `PeladillaButton.Pressed` sigue enviando `DeclareInitial` con `InitialVictoryKind.Peladilla`. La autoridad comprueba las nueve cartas reales, no el orden de presentación enviado por la UI. No se necesita importar nuevos recursos ni configurar señales o RPC.

## 6. Integración

Aplicar la carpeta `desmoche` del ZIP como actualización sobre el proyecto existente, conservando sus subcarpetas. No abrir el paquete como un proyecto completo. Las carpetas de pruebas van junto a `desmoche`, fuera de `res://`. Copiar también README y contexto a la raíz del repositorio cuando exista esa estructura.

Compilar C# y comenzar una partida nueva. Utilizar la misma revisión en las instalaciones LAN. El formato de protocolo se conserva en versión 4; el host sigue siendo el único que adjudica victoria o eliminación.

## 7. Prueba de aceptación

Ejecución con SDK .NET 9.0.308 y runtime .NET 8.0.31:

| Validación | Resultado ejecutado |
| --- | --- |
| Regresión antes del arreglo | Falló: una mano con pareja ganaba en vez de eliminarse. |
| `dotnet build ./desmoche/Desmoche.csproj` | Aprobada, 0 errores y 0 advertencias. |
| `dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj` | 2.383 comprobaciones aprobadas, más 1.056 casos de escalera; incluye comparación de las 1.326 parejas y 133 manos. |
| `dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj` | 466 comprobaciones aprobadas. |
| `dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick` | 11.868 comprobaciones, 38 partidas completas: 33 Desmoche y 5 empates; 0 acciones rechazadas de bots. |

La corrida rápida no produjo Peladillas aleatorias; el caso positivo se ejecutó expresamente en el dominio, autoridad y decisiones de las cuatro dificultades. No se cambiaron reparto ni semillas para forzar resultados. Los conteos generales corresponden a esta revisión y no reemplazan los resultados históricos de otras entregas.

Aceptación manual pendiente en la PC del usuario:

1. Tras el cambio, tener dos cartas del mismo rango y declarar Peladilla: elimina al declarante y no publica victoria inicial.
2. Repetir con dos consecutivas del mismo palo, con una pareja separada por un hueco y con un juego completo oculto por el orden: pierde.
3. Con C2-C6-C10-D3-D7-D11-H4-H8-H12 después del cambio, declarar: gana. C=tréboles, D=diamantes, H=corazones.
4. Reordenar la misma mano sin alterar sus identidades: no cambia su validez real. Ordenar un juego completo contiguo oculta el botón.
5. Probar LAN y Singleplayer. La comprobación autoritativa por comandos fue ejecutada; no se ejecutó Godot ni una conexión física entre dispositivos en esta revisión.

## 8. Posibles errores

- Un bot o humano sigue ganando con parejas: comprobar que se reemplazó `Scripts/Domain/RulesValidator.cs` en el proyecto que se ejecuta y se recompiló C#.
- El botón aparece con una pareja o proyecto: comportamiento intencional. La disponibilidad visual no garantiza una victoria válida.
- Los tests no encuentran código enlazado: colocar sus carpetas junto a `desmoche`, no dentro del proyecto Godot.
- Los documentos antiguos dicen que una pareja gana: esta revisión y el contexto actualizado tienen prioridad.

## 9. Estado de la fase

FASE COMPLETADA: SÍ — corrección implementada, compilación y pruebas automáticas aprobadas, paquete de actualización preparado.

BLOQUEADORES: ninguno para entregar esta corrección. Pendiente aceptación manual en la PC del usuario y LAN física; no se afirma verificación visual o entre dispositivos.

SIGUIENTE FASE: la que indique el usuario.
