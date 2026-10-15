# Contexto actual de trabajo — Desmoche

Actualizado el **15 de octubre de 2026** tras revisar e integrar la entrega acumulativa **Actualizacion-Ajustes-De-Partida** sobre el proyecto completo. Este es el punto de entrada para continuar el desarrollo; README y AGENTS.md contienen las instrucciones de ejecución y trabajo.

El paquete original es un parche acumulativo. Este repositorio conserva la base completa, recursos, .uid, las tres consolas de pruebas y la documentación. Todos los participantes LAN deben usar protocolo **8**. Se conserva la configuración táctil del paquete: `pointing/emulate_mouse_from_touch=true`.

## Cambios que sustituyen el contexto anterior

- **Peladilla vuelve a ser estricta:** no puede existir un juego ni formarse añadiendo una carta; parejas y proyectos la invalidan. Sustituye la variante permisiva de la subida anterior.
- **Protocolo actual 8**, confirmado en `NetworkProtocol.Version`. Los protocolos 4/5/6/7 en guías anteriores son históricos.
- **Todo `Finished`, victoria o empate, permite consultar manos restantes y el orden restante del mazo.** Durante la partida siguen privados; una desconexión activa no autoriza revelarlos.
- Los tiempos autoritativos son configurables: **5 s para elegir, 20 s para preparar desde la elección, 3 s para Pasa y 15 s para revancha LAN** son los valores predeterminados. El host puede modificarlos antes de iniciar. No hay plazo general para todos los pasos del turno.
- Trasmoche es atómico y conserva al menos tres cartas anteriores válidas en el origen. Pasa incluye también dejar pasar una oferta utilizable en un juego propio publicado cuando nadie la reclama.
- La presentación actual coloca botones y círculos junto a cartas/juegos, prepara dentro de Mi juego y elimina la página/fila inferior.

Las guías antiguas de oferta/manos y mazo final conservan párrafos que niegan revelar manos en empate; sus avisos superiores dan prioridad a **ManosFinalesEnEmpate.md**, coherente con el código actual. Las descripciones antiguas de filas inferiores quedan sustituidas por **BurbujasYAccionesEnMesa.md**. El comentario antiguo de `ExpireClaims` sobre un temporizador futuro tampoco describe el comportamiento vigente: los plazos están implementados en `ServerSession.Timing.cs`.

Este archivo permite continuar el proyecto sin reconstruir todo el historial del chat. Resume el contrato de trabajo, la arquitectura y las decisiones que debe preservar cada cambio. Para las firmas exactas y los casos límite, consulta el código y los documentos de `desmoche/Docs`.

## Objetivo y estado

Desmoche es un juego de cartas 2D para **2–4 jugadores**, construido en **Godot 4 .NET/C#**. Su modalidad original es LAN/Wi-Fi con un dispositivo **HOST = servidor + jugador** y clientes que encuentran el host por código de sala en la misma red local (descubrimiento UDP y conexión ENet). Singleplayer permite a una persona jugar en local contra **1–3 bots**, con la misma Mesa y reglas.

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
| Revancha y plazos de cartas | Tiempos configurables resueltos por la autoridad; valores predeterminados de revancha 15 s, elección 5 s y preparación 20 s. |
| Ajustes de partida | Cuatro tiempos y cinco victorias iniciales configurables en bots y LAN; protocolo 8; se conservan en revancha. |

Los recursos y diseños visuales son provisionales. No se ha certificado la exportación a Android/iOS. La siguiente fase la define el usuario; no asumir que toca exportar, persistir partidas o añadir servicios de Internet.

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
- La réplica pública incluye jugadores/cantidades, juegos publicados, mazo como cantidad, parte pública del pozo, oferta, turno y estado. **Durante la partida no incluye manos ajenas ni identidades/orden de cartas ocultas del mazo.** Al finalizar, FinalDeck revela únicamente el orden restante; las manos restantes también se revelan en todo Finished, incluida victoria o empate. La mano propia y confirmaciones privadas viajan solo al destinatario.
- `MultiplayerSpawner`/`MultiplayerSynchronizer` replican exclusivamente el estado público. No añadir secretos al nodo público, sus propiedades, nombres, metadatos, datos de aparición o registros.
- El ensamblador une partes públicas/privadas de la misma sesión y revisión. No asumir orden entre RPC y sincronización. Toda partida Finished, incluida victoria o empate, autoriza revelar las manos restantes para su consulta. Desconectarse durante una partida activa no la autoriza.
- El dispositivo host aloja el estado completo y necesariamente puede conocerlo. La protección de privacidad descrita corresponde a clientes remotos.
- **Singleplayer:** humano y bots atraviesan la misma autoridad local. Durante la partida cada bot recibe únicamente su propia vista y datos públicos; no recibe manos rivales, mazo ordenado ni la semilla del reparto. El mazo final solo existe al terminar y no participa en decisiones. La dificultad cambia la estrategia, nunca el acceso a información.

## Reglas vigentes que debe preservar el motor

**Ajustes de la sala (14/10/2026).** Bots y multijugador comparten `MatchSettings`. El creador elige segundos para elección (1–120; defecto 5), preparación (5–300; defecto 20), Pasa (1–30; defecto 3) y revancha LAN (5–120; defecto 15). Puede desactivar individualmente A234, JQKA, cuarta, Color y Peladilla. Las referencias a tiempos fijos de los párrafos siguientes son los valores predeterminados. Solo el host cambia opciones y únicamente en `Preparing`; los invitados las consultan. Protocolo 8. Se conservan en revancha, no se guardan permanentemente al cerrar la aplicación. Declaraciones desactivadas se rechazan sin penalizar y no aparecen en UI ni decisiones de bots; la victoria normal de diez cartas sigue activa. Véase [AjustesDePartida.md](desmoche/Docs/AjustesDePartida.md).

Las referencias a plazos fijos siguientes son los valores predeterminados. Los protocolos 4–7 mencionados en la historia de cada revisión han sido sustituidos por protocolo 8. Las cinco victorias especiales están activadas por defecto; la revancha contra bots continúa siendo inmediata.

**Cartas e inicio.** Baraja inglesa de 52 cartas, sin comodines; 2–4 jugadores, nueve cartas por jugador. Todas las cartas tienen identidad única. Siempre se conserva `mazo + pozo + manos + juegos en mesa + oferta + reserva de cambio = 52`, sin duplicaciones. Los asientos avanzan a la derecha. Cada jugador selecciona una carta de sus nueve para entregar a la derecha y recibe la seleccionada a su izquierda; al confirmar, la carta sale inmediatamente de su mano a una reserva privada y quedan ocho cartas. El intercambio se completa de forma conjunta cuando todos confirman. A234/JQKA se pueden declarar antes de confirmar el cambio propio y después de completarlo; las demás declaraciones iniciales se resuelven después.

**Combinaciones.** Tercia: exactamente tres cartas del mismo rango. Cuarta: exactamente cuatro. Tercias y cuartas deben alternar colores rojo/negro; un orden incorrecto abre una oportunidad de Pasa. Escalera: 3–13 cartas consecutivas del mismo palo, en orden ascendente o descendente declarado. El As puede ser bajo (`A-2-3`) o alto (`Q-K-A`); no hay vuelta `K-A-2`. No reutilizar una carta entre grupos ni tratar los valores de `Rank` como puntuación. El orden de la selección importa; no corregirlo silenciosamente en la UI.

**Ofertas, reclamos y pagos.** Toda carta sacada del mazo se muestra públicamente. Si nadie la utiliza, pasa al pozo: no se conserva ni se cambia por otra de la mano. Una carta reclamada, incluida la superior del pozo, debe utilizarse en una combinación válida; no puede descartarse inmediatamente. Reclamar y jugar exige pagar una carta propia salvo victoria o el caso del motor de mano vacía sin victoria. El pago genera una oferta pública exclusiva para los demás: el pagador no puede reclamarla ni rechazarla, tampoco mediante operaciones automáticas, y no se espera su respuesta. La siguiente extracción corresponde al jugador habilitado a la derecha de quien jugó/pagó la carta.

**Oferta obligatoria.** Confirmado el 14/10/2026: si nadie usa una oferta pública del mazo o de un pago y esta puede ampliar un juego propio publicado, hay tres segundos de Pasa por infractor, en orden de asiento. Un rival habilitado que pulsa elimina al infractor; sin denuncia continúa. La carta permanece ofrecida hasta resolver y luego va al pozo, sin ampliación automática de esa misma carta al recién eliminado. Puede cumplirse usando otro juego válido de mano/trasmoche. Si otro reclama la oferta no hay denuncia; se conserva la prioridad. Solo juegos publicados obligan, y el pagador permanece excluido de su pago. El pozo antiguo del turno queda fuera de esta denuncia. Véase [MazoFinalYOfertaObligatoria.md](desmoche/Docs/MazoFinalYOfertaObligatoria.md).

**Excepción de apertura.** Si nadie usa la primera carta mostrada, queda en el pozo y el creador debe extraer otra del mazo; ocurre una sola vez. Si alguien usa esa primera carta, paga salvo victoria y termina la excepción: sigue el jugador a su derecha. La segunda extracción sigue el flujo de oferta normal.

**Plazos de cartas.** Cada oferta pública abre cinco segundos para elegirla. Quien no elige pasa automáticamente, aunque pudiera formar un juego. Elegir una carta sin uso válido la libera sin sanción ni incorporarla a mano. Cada interesado que elige a tiempo dispone de veinte segundos desde su elección para preparar y confirmar. Al vencer, la autoridad recalcula los usos válidos con su mano y juegos propios, incluyendo trasmoche, y escoge uno al azar; si ya no existe ninguno, pasa. Se conserva la prioridad de asiento, sin adjudicar por orden de llegada. La revisión de oferta obligatoria abre Pasa cuando todos dejan pasar una carta que entra en un juego publicado; tener solo una posibilidad en mano sigue sin obligar. El pozo antiguo sigue reservado al jugador del turno: tiene cinco segundos para elegirlo y veinte para prepararlo. Si deja vencer la elección del pozo, continúa con la carta pública del mazo; con mazo vacío se ofrecen oportunidades a los siguientes habilitados y los rechazos terminan en empate. La segunda extracción obligatoria no habilita el pozo. Relojes y vencimientos pertenecen al host, compartidos con Singleplayer; no se añaden plazos a intercambio, declaraciones iniciales ni pago. Véase [RevanchaYTemporizadores.md](desmoche/Docs/RevanchaYTemporizadores.md).

**Revancha LAN.** Al terminar, cada participante conectado, incluso eliminado, puede pulsar Volver a jugar durante quince segundos. Todos ven confirmados/participantes. Al vencer se inicia automáticamente otra partida con los confirmados si son al menos dos; los invitados no confirmados salen de la sesión. Las desconexiones actualizan ambos contadores. El host que no confirma conserva la autoridad como espectador, sin asiento ni cartas, y no migra la sesión. El siguiente inicio rota a la derecha. Con menos de dos confirmados no se inicia otra partida. La revancha local contra bots conserva su botón inmediato.

**Victoria normal.** Exactamente **diez cartas** combinadas entre los juegos propios publicados y los nuevos grupos declarados, sin cartas sueltas ni reutilizadas. Solo la autoridad adjudica victoria. La victoria se comprueba automáticamente tras una publicación, transferencia o reclamo: no requiere botón ni bandera. Ganar al tomar evita el pago. Si hay un orden de colores incorrecto, primero se resuelve Pasa. Tener once cartas combinadas no gana. Mano vacía por sí sola tampoco adjudica victoria.

**Victorias iniciales.** Se evalúan con la mano de nueve cartas: `A-2-3-4` del mismo palo, `J-Q-K-A` del mismo palo, una cuarta, Color o Peladilla. Color exige todas rojas o todas negras, sin exigir mismo palo, y las nueve cartas ordenadas de menor a mayor, con As bajo o alto (no ambos en una sola secuencia). **Peladilla**, corregida por indicación del usuario el 12/10/2026, exige que no exista ningún juego actual **ni pueda formarse añadiendo una sola carta ausente de la mano**, considerando cualquier selección y orden. Una pareja del mismo rango o dos cartas del mismo palo que una tercera complete en escalera la invalidan. Se consideran todas las identidades posibles de la baraja de 52, sin consultar el orden del mazo ni manos rivales. Un juego oculto por el orden también la invalida. La UI conserva el botón cuando no hay un juego contiguo visible; presionarlo con juegos o proyectos elimina al declarante. La autoridad comprueba la mano completa. Color/Peladilla se declaran después del cambio y antes de la primera oferta. Las otras tres pueden completarse con esa primera oferta, y la declaración debe incluirla.

**Juegos y penalizaciones.** Se pueden publicar juegos propios y ampliar grupos conservando sus cartas anteriores, o trasmochar cartas propias publicadas mediante una actualización atómica de origen y destino. El origen debe conservar al menos tres de sus cartas anteriores formando un juego válido; no puede repararse una reducción inválida añadiendo reemplazos. Cada carta retirada debe usarse una sola vez en otro juego de la misma solicitud, junto con cartas de mano o la oferta/pozo cuando se reclama. Mesa permite click/Enter para preparar un juego nuevo desde una carta publicada, o arrastre a un borrador/juego propio; incluye el origen reducido en el mismo comando existente. Quitar un borrador implicado cancela los grupos dependientes del traslado. Véase [Trasmoche.md](desmoche/Docs/Trasmoche.md). Si una tercia/cuarta no alterna colores, se muestra Pasa durante 3.000 ms, con el botón junto a las cartas del grupo cuestionado, a los otros jugadores habilitados. Pulsarlo elimina al infractor; vencer el plazo acomoda sin sanción. En ambos casos los juegos se ordenan y se conservan los pagos automáticos. Solo el reloj monotónico de la autoridad cierra la oportunidad. No se desarman libremente juegos ni se toman cartas de juegos rivales. Declarar victoria incorrectamente elimina al declarante; el motor también aplica la penalización acordada a una declaración de grupo desordenado que puede componerse. Datos ajenos, duplicados, juegos imposibles o fases incorrectas se rechazan. No convertir cualquier rechazo técnico en penalización.

**Ausencias.** El penalizado o desconectado conserva mano y juegos para operaciones automáticas; queda fuera de los turnos y no puede ganar. Desconexión y penalización son estados distintos. Si pierde el host se termina la sesión; no hay migración de host ni reconexión a una mano anterior. En lobby se retira al invitado ausente; la siguiente preparación retira participantes desconectados.

**Estados.** `Preparing → InitialExchange → InitialDeclarations → AwaitingDraw → AwaitingClaims → AwaitingPayment`, con vueltas entre robo/reclamos/pago, interrupción temporal `AwaitingOrderChallenge` para Pasa y final `Finished`. Resultados: `None`, `Desmoche`, `InitialVictory`, `Draw`. El agotamiento del mazo se resuelve junto con la posibilidad de usar el pozo y los participantes habilitados; no asumir que sacar la última carta termina inmediatamente toda partida.

Las victorias iniciales publican su tipo y cartas declaradas únicamente al terminar; Mesa muestra la evidencia en la zona del ganador, sin duplicar cartas en el dominio. Se conservan todas las victorias iniciales para humanos y bots, con la Peladilla estricta de [Peladilla.md](desmoche/Docs/Peladilla.md), que sustituye las referencias anteriores a ganar con parejas o proyectos incompletos. La presentación de la evidencia sigue descrita en [VictoriasVisiblesYPagos.md](desmoche/Docs/VictoriasVisiblesYPagos.md).

**Reparto visual clásico.** Desde el snapshot de `InitialExchange`, Mesa anima nueve rondas en orden de asiento, una carta por jugador desde el mazo. Dura aproximadamente 2,03/2,84/3,65 segundos con dos/tres/cuatro participantes, más dos cuadros de colocación. Solo las cartas propias giran y muestran identidad; las ajenas son dorsos anónimos temporales. El cambio se habilita al terminar. Las confirmaciones ajenas se dibujan al final; cambios de mano propia, fase, sesión o participantes cancelan el reparto y muestran el estado real. Cada revancha vuelve a animarse. Los bots de la sesión nativa esperan cuatro segundos antes de la primera decisión, también al reiniciar, y después conservan su intervalo normal. La simulación pura mantiene demora inicial cero salvo configuración explícita. No cambia reglas ni protocolo. El trébol tiene tres hojas circulares y tallo estrecho en el centro y las esquinas. Véase [RepartoClasicoYTrebol.md](desmoche/Docs/RepartoClasicoYTrebol.md).

**Selección animada.** Las cartas seleccionadas de la mano suben un 18 % de su altura en 0,16 segundos y regresan suavemente al deseleccionar. Toda la selección recibe contorno verde si puede formar un único juego al ordenar copias con As bajo/alto; una selección incompleta o con sobrantes solo se eleva. El indicador reutiliza RulesValidator, no cambia orden ni confirma jugadas. Click en vacío limpia la selección sin borrar borradores/pago; botones y arrastre conservan su gesto. La transformación de input acompaña a la carta elevada y es independiente del layout. Véase [SeleccionAnimada.md](desmoche/Docs/SeleccionAnimada.md).

**Arrastre suave.** La carta propia sigue al cursor en un preview nativo sin dejar su origen visible. Las vecinas se desplazan en 0,16 segundos con curva cúbica de salida, y un hueco dorado «Aquí» señala el destino desde posiciones estables. El orden local se confirma al soltar, reutilizando los mismos nodos y conservando selección y verde. Escape, soltado en vacío, snapshot/contexto nuevo, resize y salida limpian el gesto; los destinos de preparación siguen disponibles. La animación no modifica reglas, autoridad ni protocolo 7. Véase [ArrastreSuave.md](desmoche/Docs/ArrastreSuave.md).

**Pago preparado.** Soltar en Pago/Cambio inicial oculta inmediatamente la carta de la fila y la muestra solo en la casilla, conservando su identidad y orden local. Las restantes cierran el espacio. Aceptar mantiene la preparación mientras espera el snapshot; Reply por sí solo no retira cartas reales. Cancelar/Escape, reemplazo, rechazo y espera vencida restauran la carta. Los contadores incluyen la preparada hasta la confirmación. El arrastre de las restantes usa solo destinos visibles y conserva el lugar lógico de la oculta. No cambia reglas, autoridad ni protocolo 7. Véase [PagoPreparado.md](desmoche/Docs/PagoPreparado.md).

**Oferta y manos finales.** El verde evalúa toda la selección de mano junto con la fuente externa marcada: dos propias más oferta/pozo pueden completar un juego. La fuente también se marca verde, conservando su posición. No cambia orden ni confirma acciones. Al finalizar, con victoria o empate, las manos autorizadas de los rivales se consultan por botones Mostrar juego bajo sus zonas públicas; una ventana translúcida en el espacio derecho muestra una mano a la vez sin tapar juegos publicados. Mazo/pozo se acomodan en el margen izquierdo del final para separar sus contadores. Cerrar/Escape/clic exterior, nueva partida y desconexión limpian la ventana. Empate también revela manos tras la corrección solicitada en [ManosFinalesEnEmpate.md](desmoche/Docs/ManosFinalesEnEmpate.md). La consulta de manos reutiliza RevealedHands. FinalDeck permite consultar una copia numerada en orden de extracción al terminar, también en empate. La revisión de protocolo 7 habilitó además las manos restantes de todos los participantes en Finished. La ventana comparte el espacio derecho y permite desplazamiento; nueva partida/salida elimina la revelación. Véase [OfertaYManosFinales.md](desmoche/Docs/OfertaYManosFinales.md).

**Resolución adaptable.** Base 1280×720 con `canvas_items` y `expand`; ventana mínima 640×360. Móviles en horizontal automática. El tablero virtual 2016×780 conserva escala uniforme, posiciones y recursos. Las revisiones posteriores integran la preparación en Mi juego y colocan las acciones junto a sus elementos, sin fila inferior ni scroll general de la página; Info y las consultas conservan scroll propio. Main transforma el área segura y teclado móvil a márgenes lógicos. No se reconstruye la mano al redimensionar. Véase [ResolucionAdaptable.md](desmoche/Docs/ResolucionAdaptable.md). La aceptación en dispositivos físicos y las exportaciones siguen pendientes.

## Revisiones recientes de presentación

**Burbujas y acciones en mesa.** Los plazos de elección, preparación y Pasa se muestran mediante círculos oscuros/dorados de 44 unidades junto a sus controles; pasan a ámbar en los últimos tres segundos. La fila bajo la mano se reemplaza por botones próximos a oferta/pozo, mazo, pago y Mi juego. Mantiene rutas, señales, autoridad y protocolo 7. Los círculos no generan solicitudes ni resuelven vencimientos; Info conserva el texto completo. Véase [BurbujasYAccionesEnMesa.md](desmoche/Docs/BurbujasYAccionesEnMesa.md).

**Salida de Mesa.** La corrección de [SalidaDePartida.md](desmoche/Docs/SalidaDePartida.md) evita que MenuPanel intercepte el clic en Salir: fondo con MouseFilter Ignore y TopBar al frente de sus hermanos. Se conserva LeaveRoom e IGameSession.Close, la navegación y protocolo 7. Las pruebas comprueban cierre real y llamadas de salida mediante input nativo.

**Edición en mesa.** Reemplaza el pie acumulativo por una pantalla de partida. Los preparados aparecen en Mi juego con marco dorado y Sin confirmar; las cartas propias asignadas se ocultan de la fila sin salir del estado autoritativo. Ediciones/trasmoches sustituyen el dibujo del publicado; Quitar restaura dependencias. Editar elige destino y Nuevo vuelve a grupos independientes. Preparar usa StageCards; las reglas de confirmación no cambian. Estado en cabecera, acciones junto a sus cartas/juegos y plazos en círculos según la revisión posterior; Info completo, salida en Menú y revancha visible al terminar. Navegación interna si Mi juego no cabe; sin página inferior. Mantiene protocolo 7, autoridad, privacidad, animaciones y consultas finales. Véase [EdicionEnMesa.md](desmoche/Docs/EdicionEnMesa.md).

La revisión posterior de [FluidezReparto.md](desmoche/Docs/FluidezReparto.md) conserva las referencias durante los vuelos, termina cada entrega una vez y actualiza la revisión/contadores al cierre sin reconstruir la mano. Cambios de contexto cancelan el reparto antes de representar la vista nueva. El diagnóstico compara coste de CPU y asignaciones de Mesa; no certifica FPS ni recursos de la PC del usuario.

## Políticas todavía PROVISIONALES y límites

No convertir estas decisiones de implementación en reglas permanentes sin acuerdo:

- Pago automático con la carta de menor ID de la mano; ampliación automática compatible de menor ID para jugadores eliminados/desconectados. El juego al vencer una reserva humana se elige al azar, según la regla indicada por el usuario.
- Desconexión durante intercambio: conservar elección previa o usar la carta de menor ID si falta.
- Prioridad de oferta de mazo: quien extrae, después hacia la derecha. En pagos ya quedó confirmado que el pagador se excluye y la prioridad comienza a su derecha.
- Declaraciones iniciales sin primera oferta: se acepta la primera válida procesada; falta acordar arbitraje si se exige simultaneidad.

Los plazos predeterminados son elección de carta (5 segundos), preparación desde su elección (20), revancha LAN (15) y Pasa (3); MatchSettings permite ajustarlos antes de iniciar. No hay límite general para los demás pasos de turno. Solo la autoridad puede vencer una oportunidad. Hay descubrimiento por código LAN de seis caracteres (UDP 7001, cinco segundos de búsqueda); la partida usa ENet UDP 7000. No hay persistencia de partidas ni servicio de descubrimiento por Internet. Todos los participantes deben usar protocolo 8. El código no es una contraseña. La identidad y el contenido de las cartas del cambio no viajan en el descubrimiento ni en la réplica pública. La atención de los bots a Pasa es PROVISIONAL: 20/50/80/100 % para Fácil/Medio/Difícil/Experto, una decisión por oportunidad. Las estrategias de bots no proponen trasmoche; la autoridad sí lo considera al completar una reserva vencida. Experto usa memoria pública y búsqueda acotada, sin garantía de juego óptimo o porcentaje de victorias.

## Componentes añadidos a la arquitectura

Las rutas siguientes son relativas al proyecto Godot:

| Componente | Función |
| --- | --- |
| `Scripts/Application/GameManager.TimedCards.cs` | Elección de carta/pozo, evaluación de usos válidos y finalización de jugadas vencidas, incluido trasmoche. |
| `Scripts/Networking/Core/ServerSession.Timing.cs` | Reloj, reservas, vencimientos y revancha autoritativos. |
| `Scripts/Presentation/Mesa.Deal.cs` y `Interaction/InitialDealPresentation.cs` | Secuencia de reparto y cancelación al cambiar contexto. |
| `Scripts/Presentation/Mesa.Selection.cs` y `Interaction/HandSelectionHint.cs` | Selección elevada e indicador de juego sobre copias, sin ordenar ni confirmar la jugada. |
| `Scripts/Presentation/Mesa.HandDrag.cs` | Arrastre con una sola carta visible, hueco de destino y animación de vecinas. |
| `Scripts/Presentation/Mesa.TableEditing.cs` | Borradores, edición integrada, Info, Menú y navegación de Mi juego. |
| `Scripts/Presentation/Mesa.FinalHands.cs` | Consulta final de manos y mazo autorizados. |
| `Scripts/Presentation/Mesa.Countdowns.cs`, `Mesa.TableActions.cs`, `MesaActionClock.cs` | Representación de tiempos, ubicación contextual de botones y círculos. No vencen oportunidades por su cuenta. |
| `Scripts/Presentation/Main.Responsive.cs` | Área segura y adaptación de presentación. |
| `Scripts/Domain/MatchSettings.cs` | Opciones inmutables, límites y victorias iniciales habilitadas. |
| `Scripts/Presentation/MatchSettingsPanel.cs` | Panel común y engranaje: edición del creador y consulta del invitado. |

El protocolo acumulado añade `ChooseOffer`, `ChooseDiscard`, `JoinRematch` y `UpdateSettings`; opciones públicas MatchSettings; DTO de reservas, plazos y revancha; `OrderChallengeDto.Reason` para `SetOrder`/`UnusedOffer`, y `FinalDeck`. `NetworkManager` notifica por el RPC fiable de autoridad `RematchExcluded` al invitado excluido antes de cerrar su conexión. No hay RPC para que el cliente fuerce vencimientos. Las consultas finales reutilizan vistas; el trasmoche reutiliza solicitudes existentes.

## Verificación de la entrega recibida

[VERIFICACION.md](VERIFICACION.md) registra comprobaciones del 14 de octubre de 2026 para Ajustes de partida. Son resultados documentados de la entrega, separados de las pruebas de integración que se ejecutan para esta publicación.

| Comprobación | Resultado documentado de la entrega |
| --- | --- |
| Compilación | 0 errores / 0 advertencias. |
| Consola general | Aprobada; incluye 53 comprobaciones nuevas de ajustes y 1056 casos de escalera. |
| Interacción | 750 comprobaciones. |
| Singleplayer rápido | 11 984 comprobaciones; 38 partidas: 33 Desmoche y 5 empates. |
| MatchSettings / MenuNavigation / TableActions | 128 / 223 / 1671 comprobaciones. |
| ENet real | Tres procesos: opciones públicas actualizadas, exclusión del no confirmado y revancha con ajustes conservados; manos y mazo finales coincidentes y datos ocultos al reiniciar. |

Se documentan siete resoluciones: 640×360, 844×390, 1280×720, 1920×1080, 2400×1080, 1024×768 y 3440×1440. Render Linux/Xvfb/OpenGL por software; no sustituye entrada táctil, LAN/Wi-Fi entre dispositivos físicos ni rendimiento de esta PC. No se exportó APK/IPA ni se certificó Android/iOS.

La medida histórica de cierre del reparto de unos 52–55 ms a 4–7 ms es un diagnóstico aislado de CPU/asignaciones; no garantiza FPS ni consumo de recursos de esta PC. Para futuras comprobaciones, usar el proyecto completo, las tres consolas hermanas y los comandos del README.

## Entregas revisadas y precedencia

Se conservan las revisiones de Peladilla, trasmoche, revancha y tiempos, reparto y trébol, fluidez, resolución adaptable, selección animada, oferta y manos finales, mazo final y oferta obligatoria, manos finales en empate, arrastre suave, pago preparado, edición en mesa, salida y burbujas/acciones. **Actualizacion-Ajustes-De-Partida** es la referencia acumulativa más reciente y conserva esas revisiones.

Las fechas de extracción por sí solas no definen precedencia. Usar las revisiones explícitas de las guías; los documentos de fases anteriores describen su estado histórico y quedan subordinados al contexto actual. La siguiente tarea la define el usuario.

## Validación de integración para GitHub — 15/10/2026

La copia completa integrada compila con 0 errores y 0 advertencias. Pasaron 4077 comprobaciones generales y 1056 casos de escalera, 750 de interacción y 11 984 de bots con 38 partidas completas (33 Desmoche, 5 empates). Las fuentes de juego/pruebas coinciden por SHA-256 con los archivos recibidos. Se conservaron recursos y .uid anteriores. [Resultados y alcance](outputs/github/desmoche-repository/VERIFICACION.md). En esta integración no se repitieron las pruebas visuales ni ENet de la entrega anterior.
