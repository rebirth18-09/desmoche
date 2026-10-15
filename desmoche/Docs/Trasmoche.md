# Corrección: trasmoche de cartas publicadas

> Actualización 13/10/2026: [RevanchaYTemporizadores.md](RevanchaYTemporizadores.md) añade elección/preparación de cartas, cambia los controles de confirmación y eleva el protocolo a 5. La validez de origen y destino de este documento se conserva.

Fecha: 12 de octubre de 2026. Proyecto: Godot 4.7.2 .NET/C#.

## 1. Objetivo de la fase

Permitir trasladar cartas de juegos propios publicados a un juego nuevo o a otro juego propio, usando también cartas de la mano y la carta ofrecida o superior del pozo cuando corresponde. El origen debe conservar al menos tres cartas anteriores que sigan formando un juego válido por sí mismas.

Caso solicitado: origen A♦–2♦–3♦–4♦, A♣ en la mano y A♥ ofrecido. La misma solicitud conserva 2♦–3♦–4♦ y crea A♦–A♣–A♥. Retirar el 2♦ del origen se rechaza: A♦–3♦–4♦ no forma un juego válido. También se permite completar una cuarta o continuar una escalera, respetando la validez de ambos juegos.

La corrección anterior de peladilla se conserva.

## 2. Decisiones arquitectónicas

Se reutilizan los comandos existentes `Publish`, `Claim` y `DrawDiscard`, con su lista de `PlayDto`. Para un trasmoche, la lista incluye el resultado completo del origen y el del destino. Los juegos publicados conservan sus identificadores. No se crean DTO, RPC ni estados de partida intermedios; la versión de protocolo sigue siendo 4.

`GameManager` resuelve las cartas de todos los juegos propios incluidos en la intención. `GameState.ApplyMeldPlays` valida la operación completa antes de devolver un estado nuevo:

- Cada juego modificado debe pertenecer al solicitante autenticado y aparecer una sola vez.
- Si se retiran cartas, las cartas anteriores que permanecen deben formar un juego válido de al menos tres cartas. Añadir reemplazos no habilita retirar cartas que, por sí solas, dejarían un origen inválido.
- Todas las cartas anteriores deben aparecer exactamente una vez entre los resultados. No pueden desaparecer, duplicarse ni volver a la mano mediante este comando.
- Cada destino debe formar un juego válido. Las cartas adicionales deben proceder de la mano o del origen externo autorizado por el comando.

La UI solo prepara borradores. Anticipa si una carta publicada puede retirarse, pero el motor vuelve a comprobar origen, destino, propiedad y reglas. Durante la prioridad de una oferta, la partida conserva sus juegos y mano originales hasta que se resuelve el reclamo. Se mantienen pagos, Pasa, turnos, penalizaciones y victoria automática existentes.

No se habilitan cartas de juegos rivales. No se accede a manos ajenas ni al orden del mazo. LAN y Singleplayer usan la misma corrección. La estrategia de los bots no se amplía para proponer trasmoches en esta entrega.

## 3. Dependencias

Se conservan Godot 4.7.2 .NET y `Godot.NET.Sdk/4.7.2`. La verificación utilizó SDK .NET 9.0.308 y runtime .NET 8.0.31. El juego de escritorio y las pruebas continúan apuntando a `net8.0`. No se añaden paquetes.

Las consolas de pruebas permanecen como carpetas hermanas de `desmoche`, fuera de `res://`.

## 4. Implementación

| Archivo | Cambio |
| --- | --- |
| `Scripts/Domain/GameState.cs` | Aplica origen y destino de forma atómica; valida cartas originales retenidas, conservación, propiedad y ausencia de duplicados. |
| `Scripts/Domain/Meld.cs` | Documenta que `MeldPlay` describe el resultado completo de un juego, incluyendo una reducción válida. |
| `Scripts/Application/GameManager.cs` | Resuelve las cartas propias de todos los juegos modificados en una misma intención. |
| `Scripts/Presentation/Interaction/HandInteractionState.cs` | Prepara origen reducido y destino; comprueba la extracción y cancela juntos los borradores dependientes. |
| `Scripts/Presentation/Interaction/InteractionActions.cs` | Construye intenciones con cartas publicadas cuyo origen esté incluido en la solicitud. |
| `Scripts/Presentation/CardDragData.cs` | Añade origen local `Published`, con identificador real de juego y validación de sesión, revisión, ventana, propiedad y carta. |
| `Scripts/Presentation/Mesa.Dynamic.cs` | Habilita click/Enter y arrastre en las cartas propias extraíbles, y actualiza indicaciones y controles. |
| `Scripts/Presentation/Mesa.Interaction.cs` | Acepta cartas publicadas en destinos locales y actualiza su disponibilidad al editar borradores. |
| `Scripts/Diagnostics/MesaInteractionSmoke.cs` | Prueba tokens y controles reales para el caso A–2–3–4, incluyendo click/Enter. |
| `Desmoche.Pruebas/RuleRevisionTests.cs` | Cubre reclamos serializados, prioridad, conservación, pagos, cuartas, escaleras, victoria con diez cartas y solicitudes inválidas. |
| `Desmoche.Interaccion.Pruebas/PublishedTransferTests.cs` | Añade 18 comprobaciones de preparación, destinos existentes, cancelación y protección del estado recibido. |
| `Desmoche.Interaccion.Pruebas/Program.cs`, `Pruebas.csproj` | Ejecutan e incluyen las nuevas pruebas de interacción. |
| `CONTEXTO_PROYECTO.md`, `README.md`, este documento | Registran la regla, el uso y la verificación. |

Las rutas `Scripts/...` de la tabla son relativas a `desmoche`. Las carpetas de pruebas y los documentos de la raíz son relativos al repositorio.

## 5. Configuración Godot

No se requieren cambios manuales de nodos, Inspector, escenas, señales ni RPC. La Mesa continúa usando `IGameSession` y el snapshot confirmado. La ruta estable de `NetworkManager` se conserva.

La conexión local de `SelectionRequested` de una carta publicada prepara un juego nuevo con click/Enter. Sus callbacks de arrastre y las zonas existentes permiten elegir un borrador o juego propio como destino. La carta original permanece en su nodo hasta recibir un snapshot confirmado.

## 6. Integración

1. Detén la ejecución del juego y conserva una copia del proyecto.
2. Extrae el ZIP y copia su carpeta `desmoche` sobre tu carpeta `desmoche`, combinando carpetas y reemplazando los archivos incluidos. Es una actualización de fuentes, no un proyecto completo.
3. Para ejecutar las pruebas, copia las carpetas `Desmoche.Pruebas`, `Desmoche.Interaccion.Pruebas` y `Desmoche.Singleplayer.Pruebas` junto a `desmoche`. No las copies dentro del proyecto Godot.
4. Abre el proyecto en Godot .NET, deja terminar la importación y recompila.

Uso para la captura del usuario:

1. Selecciona el A♣ de la mano y la carta ofrecida A♥.
2. Haz click o pulsa Enter en el A♦ del juego publicado A♦–2♦–3♦–4♦. Aparecen dos borradores: la actualización 2♦–3♦–4♦ y la nueva tercia A♦–A♣–A♥.
3. Confirma con **Tomar oferta**. El motor aplica la operación cuando se resuelve la prioridad y continúa con el pago habitual, si corresponde.

También puedes pulsar la carta publicada primero y después arrastrar cartas de mano/oferta sobre el borrador nuevo. Para completar una cuarta o continuar una escalera existente, arrastra la carta publicada sobre el juego propio de destino o sobre su borrador. Al quitar un borrador implicado, se cancelan los grupos relacionados; los independientes se conservan.

## 7. Prueba de aceptación

Comandos ejecutados desde la raíz del repositorio:

```bash
dotnet build ./desmoche/Desmoche.csproj
dotnet run --project ./Desmoche.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Interaccion.Pruebas/Pruebas.csproj
dotnet run --project ./Desmoche.Singleplayer.Pruebas/Pruebas.csproj -- --quick
```

Resultados automáticos obtenidos:

| Verificación | Resultado |
| --- | --- |
| Compilación del juego | Correcta, 0 errores y 0 advertencias. |
| Consola de dominio, reglas, partida, conexiones, red y réplica | 2.536 comprobaciones y 1.056 casos de escalera correctos. |
| Consola de interacción | 484 comprobaciones correctas, incluyendo 18 nuevas de trasmoche. |
| Singleplayer `--quick` | 11.868 comprobaciones; 38 partidas completas: 33 Desmoche y 5 empates. |
| Importación Godot 4.7.2 .NET | Correcta. |
| `MesaInteractionSmoke` en Godot headless | 146 comprobaciones correctas. |
| `MesaDynamicSmoke` en Godot headless | 563 comprobaciones correctas, en tres resoluciones. |

Los diagnósticos se ejecutaron con:

```bash
godot --headless --path ./desmoche res://Scenes/Diagnostics/MesaInteractionSmoke.tscn -- --auto
godot --headless --path ./desmoche res://Scenes/Diagnostics/MesaDynamicSmoke.tscn -- --auto
```

`godot` representa el ejecutable de Godot 4.7.2 .NET instalado. Las pruebas de servidor usan el circuito serializado de `ServerSession` y solicitantes autenticados. Incluyen el caso de la captura con 2, 3 y 4 jugadores; el estado sigue intacto mientras otros participantes responden; el As pasa a la tercia tras resolver la prioridad y el pago posterior funciona.

También se comprobaron una cuarta y una escalera con carta ofrecida, ampliaciones de juegos existentes, cierre automático con diez cartas, rechazo de un origen con dos cartas aunque se intente repararlo con un reemplazo, origen omitido, carta perdida, carta duplicada y juego rival. Los rechazos técnicos conservan la partida y no eliminan al jugador. La revisión de la vista puede avanzar según el comportamiento previo de la sesión.

En Godot se comprobaron callbacks reales de las cartas, tokens publicados, zonas de destino, selección de mano/oferta, Enter sobre el As y cancelación. La ejecución headless no verifica el arrastre con el ratón del sistema ni partidas en dispositivos LAN separados. Esas pruebas manuales quedan para la instalación del usuario.

Aceptación manual recomendada: reproducir el caso indicado, confirmar la tercia y comprobar que el origen queda 2–3–4; intentar retirar el 2 y comprobar que no se habilita; repetir con una cuarta y una escalera propias. Verificar el mismo resultado desde un cliente LAN.

## 8. Posibles errores

| Síntoma | Comprobación |
| --- | --- |
| El As publicado no responde | Recompilar los scripts y confirmar que es un juego propio, la fase permite preparar juegos y el origen conservaría un juego válido. |
| El destino queda incompleto | Añadir las cartas necesarias al mismo borrador antes de confirmar. Un destino debe formar un juego válido. |
| Se cancela el borrador de origen al quitar el destino | Es el comportamiento esperado para impedir una extracción sin destino. |
| Cambia la oferta o se cierra la ventana | La preparación local se invalida; preparar de nuevo con el snapshot vigente. |
| No se toman cartas de un rival | El trasmoche de esta entrega utiliza exclusivamente juegos propios. |
| Errores de `Program` duplicado al compilar Godot | Mantener las carpetas de pruebas fuera de `desmoche`. |

## 9. Estado de la fase

Corrección implementada y validada mediante las consolas y los diagnósticos Godot indicados. Se conserva la corrección de peladilla. Queda pendiente la aceptación manual con ratón y dispositivos LAN del usuario; no bloquea la entrega del parche de fuentes.

FASE COMPLETADA: SÍ

BLOQUEADORES: ninguno para entregar el parche; aceptación manual en la instalación del usuario pendiente.

SIGUIENTE FASE: la que indique el usuario.
