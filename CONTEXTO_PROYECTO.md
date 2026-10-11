# Contexto de trabajo — Desmoche

Este archivo permite continuar el proyecto sin reconstruir todo el historial del chat. Resume el contrato de trabajo, la arquitectura y las decisiones que debe preservar cada cambio. Para las firmas exactas y los casos límite, consulta el código y los documentos de `desmoche/Docs`.

## Objetivo y estado

Desmoche es un juego de cartas 2D para **2–4 jugadores**, construido en **Godot 4 .NET/C#**. Su modalidad original es LAN/Wi-Fi con un dispositivo **HOST = servidor + jugador** y clientes conectados por IP local. Singleplayer permite a una persona jugar en local contra **1–3 bots**, con la misma Mesa y reglas.

La base de esta revisión usa Godot **4.7.2 .NET**; el proyecto de escritorio apunta a `net8.0`. Se verificó con SDK .NET **9.0.308**. Mantén la versión acordada y verifica compatibilidad antes de actualizarla.

| Fase / ampliación | Estado de la base actual |
| --- | --- |
| 0 — Especificación y reglas | Decisiones incorporadas al motor; algunas políticas siguen marcadas PROVISIONALES. |
| 1 — Arquitectura | Capas y contratos implementados. |
| 2 — Modelo de dominio | Cartas, mazo, jugadores, manos, pozo y estado. |
| 3 — Motor de reglas | Grupos, mano completa, desmoche y victorias iniciales. |
| 4 — Partida y estados | Intercambio, apertura, turnos, ofertas, pagos y finalización. |
| 5 — Autoridad y ENet | Conexión, comandos, admisión y desconexiones. |
| 6 — Sincronización | Réplica pública y datos privados por destinatario. |
| 7 — Lobby LAN | Crear/unirse, errores, lista de jugadores e inicio. |
| 8 — Mesa | Representación de las vistas autorizadas. |
| 9 — Cartas e interacción | Selección, arrastre, borradores y acciones visuales. |
| Revisiones de presentación | Mesa casino, controles contextuales, carga y menú. |
| Singleplayer | Configuración, cuatro dificultades, bots, nueva partida y salida. |

Los recursos y diseños visuales son provisionales. No se ha certificado la exportación a Android/iOS. La siguiente fase la define el usuario; no asumir que toca exportar, persistir partidas o añadir descubrimiento automático de salas.

## Cómo trabajamos

1. Trabajar exclusivamente en la **FASE ACTUAL** y su objetivo, o en la revisión que el usuario solicite expresamente. No avanzar automáticamente a otra fase ni generar sistemas «por si acaso».
2. Antes de implementar, analizar componentes existentes, dependencias y conflictos. Explicar brevemente las decisiones relevantes. Para cada clase, conocer quién la instancia/referencia, los datos de entrada/salida, su autoridad y su comportamiento en host, cliente y desconexión.
3. Conservar nombres, namespaces, tipos, referencias y contratos coherentes entre archivos. Cada entrega debe dejar un proyecto compilable y verificable. No cambiar silenciosamente una decisión arquitectónica: explicar el defecto y proponer la corrección antes de modificarla.
4. Entregar cambios completos y ubicaciones exactas. Cuando corresponda, detallar árboles de nodos, tipos, scripts, Inspector, señales, RPC y relaciones. Los archivos incluidos deben poder usarse juntos.
5. No inventar reglas ni APIs. Si una ambigüedad afecta a la implementación, consultarla antes de fijar una regla permanente. Una decisión no bloqueante puede ser **PROVISIONAL**, identificada como tal. Verificar documentación oficial actual cuando una afirmación dependa de Godot/.NET/Android/iOS o de una versión concreta.
6. Añadir pruebas que comprueben reglas, invariantes y comportamientos reales. Diferenciar pruebas ejecutadas, pruebas propuestas y aceptación manual. No afirmar que una escena visible, una compilación o una conexión localhost demuestran todas las condiciones de juego o LAN física.
7. Ante errores, diagnosticar causa probable, archivo/sistema y línea afectada, solución mínima, riesgos y corrección. No reescribir todo el proyecto como primera respuesta.

Cada entrega de fase utiliza estas nueve secciones: **1. Objetivo de la fase; 2. Decisiones arquitectónicas; 3. Dependencias; 4. Implementación; 5. Configuración Godot; 6. Integración; 7. Prueba de aceptación; 8. Posibles errores; 9. Estado de la fase.** La última termina con `FASE COMPLETADA: SÍ/NO`, `BLOQUEADORES: ...` y `SIGUIENTE FASE: ...`. Los documentos del proyecto contienen los detalles; el resumen del chat puede ser breve.

## Capas y responsabilidades

| Capa / componente | Responsabilidad y límite |
| --- | --- |
| `Scripts/Domain` | C# sin UI: `Card`, `Suit`, `Rank`, `Deck`, `Player`, `PlayerHand`, `DiscardPile`, `GameState`, `Meld`, `MeldPlay`, `RulesValidator`. Estado/invariantes y validación de cartas; no transporte ni animaciones. |
| `Scripts/Application/GameManager.cs` | Flujo autoritativo secuencial: fases, reparto, intercambio, turnos, ofertas, prioridades, pagos, habilitación y resultados. |
| `Scripts/Networking/Core` | `ServerSession`, protocolo, comandos y proyección/ensamblado de vistas. Núcleo C# puro; conserva el motor privado y valida solicitantes/ventanas. |
| `Scripts/Networking` | `NetworkManager` y réplica Godot: ENet, RPC, ciclo de conexión, spawner y synchronizer. |
| `Scripts/Sessions/IGameSession.cs` | Contrato consumido por Mesa: vista permitida, estado, intenciones y resultados. |
| `Scripts/Singleplayer/Core` | Opciones, controlador local y decisiones de bots. Reutiliza `ServerSession`, `GameManager` y `RulesValidator`; no contiene una segunda versión de las reglas. |
| `Scripts/Singleplayer/LocalGameSession.cs` | Adaptador Godot local: procesa intenciones y bots de forma diferida; no abre ENet ni envía RPC. |
| `Scripts/Presentation` | Carga, menú, lobby, configuración Singleplayer, Mesa y Carta. Presenta vistas y prepara solicitudes; no decide su validez definitiva. |
| `Scripts/Presentation/Interaction` | Orden, selección y borradores locales testeables. No modifica manos ni juegos autoritativos. |
| `Scripts/Diagnostics` y consolas hermanas | Diagnósticos de integración y pruebas puras. |

`Main` conserva `/root/Main/NetworkManager` como ruta estable para red y posee también `LocalGameSession`. Carga → Inicio → Multijugador/Lobby o Singleplayer → Mesa. Solo hay una sesión activa; Mesa se vincula mediante `IGameSession`. `Carta.tscn` es presentación; `Domain.Card` es identidad del dominio. No confundir ambos tipos.

## Autoridad, privacidad y acciones

- **LAN:** el host es el único escritor de mazo, manos, pozo, juegos publicados, ofertas, fases, turnos y victoria. La autoridad de los nodos de red permanece en el peer 1; el ID de jugador no es el ID de conexión ENet.
- Un cliente envía una intención. El host obtiene su identidad del emisor admitido y comprueba estructura/límites, sesión, ID de solicitud, ventana vigente, fase, turno, propiedad y reglas antes de aplicar un cambio.
- La UI y el Drag & Drop solo cambian orden, selección o borradores. La confirmación envía una solicitud; la vista confirmada determina las cartas reales y contadores. Las animaciones tampoco son fuente de verdad.
- La réplica pública incluye jugadores/cantidades, juegos publicados, mazo como cantidad, parte pública del pozo, oferta, turno y estado. **No incluye manos ajenas ni identidades/orden de cartas ocultas del mazo.** La mano propia y confirmaciones privadas viajan solo al destinatario.
- `MultiplayerSpawner`/`MultiplayerSynchronizer` replican exclusivamente el estado público. No añadir secretos al nodo público, sus propiedades, nombres, metadatos, datos de aparición o registros.
- El ensamblador une partes públicas/privadas de la misma sesión y revisión. No asumir orden entre RPC y sincronización. Tras victoria válida se revelan las manos según la política vigente; empate y desconexión no autorizan esa revelación.
- El dispositivo host aloja el estado completo y necesariamente puede conocerlo. La protección de privacidad descrita corresponde a clientes remotos.
- **Singleplayer:** humano y bots atraviesan la misma autoridad local. Cada bot recibe únicamente su propia vista y datos públicos; no recibe manos rivales, mazo ordenado ni la semilla del reparto. La dificultad cambia la estrategia, nunca el acceso a información.

## Reglas vigentes que debe preservar el motor

**Cartas e inicio.** Baraja inglesa de 52 cartas, sin comodines; 2–4 jugadores, nueve cartas por jugador. Todas las cartas tienen identidad única. Siempre se conserva `mazo + pozo + manos + juegos en mesa + oferta = 52`, sin duplicaciones. Los asientos avanzan a la derecha. Cada jugador selecciona una carta de sus nueve para entregar a la derecha y recibe la seleccionada a su izquierda; el intercambio se completa de forma conjunta. Después se resuelven las declaraciones iniciales.

**Combinaciones.** Tercia: exactamente tres cartas del mismo rango. Cuarta: exactamente cuatro. Escalera: 3–13 cartas consecutivas del mismo palo, en orden ascendente o descendente declarado. El As puede ser bajo (`A-2-3`) o alto (`Q-K-A`); no hay vuelta `K-A-2`. No reutilizar una carta entre grupos ni tratar los valores de `Rank` como puntuación. El orden de la selección importa; no corregirlo silenciosamente en la UI.

**Ofertas, reclamos y pagos.** Toda carta sacada del mazo se muestra públicamente. Si nadie la utiliza, pasa al pozo: no se conserva ni se cambia por otra de la mano. Una carta reclamada, incluida la superior del pozo, debe utilizarse en una combinación válida; no puede descartarse inmediatamente. Reclamar y jugar exige pagar una carta propia salvo victoria o el caso del motor de mano vacía sin victoria. El pago genera una oferta pública. La siguiente extracción corresponde al jugador habilitado a la derecha de quien jugó/pagó la carta.

**Excepción de apertura.** Si nadie usa la primera carta mostrada, queda en el pozo y el creador debe extraer otra del mazo; ocurre una sola vez. Si alguien usa esa primera carta, paga salvo victoria y termina la excepción: sigue el jugador a su derecha. La segunda extracción sigue el flujo de oferta normal.

**Victoria normal.** Exactamente **diez cartas** combinadas entre los juegos propios publicados y los nuevos grupos declarados, sin cartas sueltas ni reutilizadas. Solo la autoridad adjudica victoria. La declaración es explícita; acertar al tomar evita el pago. Tener once cartas combinadas no gana. Mano vacía por sí sola tampoco adjudica victoria.

**Victorias iniciales.** Se evalúan con la mano de nueve cartas: `A-2-3-4-5` del mismo palo, `10-J-Q-K-A` del mismo palo, una cuarta, Color (todas rojas o todas negras, sin exigir mismo palo), o Peladilla. Peladilla exige que no exista un juego formado **ni pueda formarse añadiendo una sola carta ausente de la mano**: dos cartas del mismo rango o dos compatibles para completar una escalera ya la excluyen. Color/Peladilla se declaran antes de la primera oferta. Las otras tres pueden completarse con esa primera oferta, y la declaración debe incluirla.

**Juegos y penalizaciones.** Se pueden publicar juegos propios y ampliar grupos conservando sus cartas anteriores. El traslado de una carta entre juegos propios debe dejar válidos tanto origen como destino; no equivale a desarmar libremente cualquier grupo. Declarar victoria incorrectamente elimina al declarante; el motor también aplica la penalización acordada a una declaración de grupo desordenado que puede componerse. Datos ajenos, duplicados, juegos imposibles o fases incorrectas se rechazan. No convertir cualquier rechazo técnico en penalización.

**Ausencias.** El penalizado o desconectado conserva mano y juegos para operaciones automáticas; queda fuera de los turnos y no puede ganar. Desconexión y penalización son estados distintos. Si pierde el host se termina la sesión; no hay migración de host ni reconexión a una mano anterior. En lobby se retira al invitado ausente; la siguiente preparación retira participantes desconectados.

**Estados.** `Preparing → InitialExchange → InitialDeclarations → AwaitingDraw → AwaitingClaims → AwaitingPayment`, con vueltas entre robo/reclamos/pago y final `Finished`. Resultados: `None`, `Desmoche`, `InitialVictory`, `Draw`. El agotamiento del mazo se resuelve junto con la posibilidad de usar el pozo y los participantes habilitados; no asumir que sacar la última carta termina inmediatamente toda partida.

## Políticas todavía PROVISIONALES y límites

No convertir estas decisiones de implementación en reglas permanentes sin acuerdo:

- Pago automático con la carta de menor ID de la mano; juego automático compatible de menor ID.
- Desconexión durante intercambio: conservar elección previa o usar la carta de menor ID si falta.
- Prioridad de oferta de mazo: quien extrae, después hacia la derecha; oferta de descarte: comenzar a la derecha del pagador y dejarlo al final. Preservar la implementación/documentación hasta que el usuario confirme o cambie esta política.
- Declaraciones iniciales sin primera oferta: se acepta la primera válida procesada; falta acordar arbitraje si se exige simultaneidad.

No hay temporizador de turno establecido. El motor conserva puntos de entrada de expiración; los clientes no pueden forzarla. No hay descubrimiento automático de salas ni persistencia de partidas. Los bots actuales no transfieren cartas entre juegos publicados; la función humana del motor se conserva. Experto usa memoria pública y búsqueda acotada, sin garantía de juego óptimo o porcentaje de victorias.

## Verificación y continuación

[README.md](README.md) contiene los comandos relativos de compilación y pruebas. La entrega Singleplayer del 11 de octubre de 2026 registró compilación 0 errores/0 advertencias, 168 partidas completas y 63 738 comprobaciones de bots, además de diagnósticos visuales y regresión ENet entre dos procesos. Estos resultados pertenecen a esa entrega; repetir los controles relevantes después de cada cambio y anotar sus resultados reales.

Consultar primero [Singleplayer.md](desmoche/Docs/Singleplayer.md), [MesaDinamica.md](desmoche/Docs/MesaDinamica.md) y los documentos de la capa afectada. Los documentos históricos pueden contener rutas del equipo original o funcionalidades entonces pendientes: el código actual y la documentación de su revisión posterior permiten resolver esas diferencias. Si aparecen contradicciones de reglas, pedir aclaración antes de cambiar el contrato.

Para exportación futura, verificar documentación oficial de Godot .NET, Android e iOS antes de dar instrucciones específicas. Incluir SDK/NDK/JDK, firma y pruebas reales cuando llegue su fase; no afirmar que permisos como multicast son obligatorios para esta conexión por IP sin una necesidad demostrada.
