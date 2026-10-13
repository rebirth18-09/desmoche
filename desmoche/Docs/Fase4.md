> Actualización 12/10/2026: las reglas de victoria, intercambio, Pasa y acceso por código se detallan en [RevisionReglasCodigoSala.md](RevisionReglasCodigoSala.md). Esa revisión prevalece sobre las instrucciones históricas de este documento.

# Fase 4 - Partida local y maquina de estados

## 1. Objetivo de la fase

Motor C# independiente de Godot UI: preparacion, incorporacion de 2-4 jugadores,
reparto, intercambio simultaneo, declaraciones iniciales, ofertas publicas,
reclamos, juegos en mesa, pagos, turnos, desmoche, empate y siguiente partida.

Verificacion realizada contra los archivos actuales de fases 2 y 3:
compilacion .NET 8 sin advertencias, 577 comprobaciones de partida,
85 comprobaciones de reglas con 1056 casos de escaleras y 61 de dominio.

## 2. Decisiones arquitectonicas

GameState sigue siendo inmutable y guarda cartas y zonas.
GameManager guarda el flujo: fase, jugador responsable, pago pendiente,
elegibilidad, respuestas a ofertas y resultado. Cada dato tiene un solo escritor.

Meld representa un grupo publico inmutable con Id estable y dueño.
MeldPlay describe una solicitud: Id de grupo opcional y cartas completas ordenadas.
RulesValidator conserva sus contratos de fase 3.

Toda carta extraida del mazo se ofrece publicamente. No se conserva una carta
que nadie use. Mientras esta ofrecida, cuenta como zona temporal de GameState.
Conservacion: mazo + pozo + manos + mesa + oferta = 52, sin duplicados.

Los asientos se asignan al incorporarse: 0, 1, 2, 3, en direccion a la derecha.
El primero es el creador. La siguiente partida inicia a su derecha.

La simulacion es secuencial. La futura red debe autenticar al solicitante y ejecutar
los comandos del GameManager en el hilo autoritativo. No enviar GameState ni Players
completos a un cliente: usar GetViewFor. Antes de ganar, solo revela mano propia,
cantidades rivales, juegos publicos, pozo y oferta. Tras victoria revela todas las manos.

Politicas PROVISIONALES que no alteran los validadores:
- Pago automatico: carta de menor Id de la mano.
- Una oferta util para varios juegos automaticos: grupo de menor Id.
- En una oferta de descarte, la prioridad comienza a la derecha del pagador y
  completa la vuelta; el pagador queda al final. En una oferta de mazo comienza
  con quien extrajo la carta y sigue hacia la derecha.
- Las declaraciones iniciales sin carta del pozo se procesan secuencialmente:
  termina con la primera declaracion valida recibida en la simulacion local.
  Si se requiere arbitrar declaraciones iniciales simultaneas, falta acordar prioridad.

No se fija duracion del turno. ExpireClaims simula el vencimiento y usa un juego
publico si cabe la carta; si no, pasa. Un futuro temporizador llama ese metodo.
Los jugadores eliminados solo reciben extensiones automaticas de sus juegos publicos;
pagan si tienen mano y acumulan sin ganar si ya no tienen cartas para pagar.

Una declaracion de grupo desordenado que se puede ordenar se compone, publica y
elimina al declarante. Las solicitudes con cartas ajenas, duplicadas, juegos imposibles
o fases incorrectas se rechazan. Declarar victoria sin cumplirla elimina al declarante.
Las bajas y reorganizaciones se habilitan tras las declaraciones iniciales; las
declaraciones iniciales se hacen antes de abrir la primera carta.

## 3. Dependencias y archivos

Necesita todos los archivos existentes de Scripts/Domain y RulesTests.cs.

Copiar sobre el proyecto actual:
- res://Scripts/Domain/GameState.cs: REEMPLAZAR completo.
- res://Scripts/Domain/Meld.cs: NUEVO. Clases Meld y MeldPlay; Desmoche.Domain.
- res://Scripts/Application/GameManager.cs: NUEVO; Desmoche.Application.
  Incluye GamePhase, MatchResult, GameActionResult y MatchView.
- res://Docs/Fase4.md: esta documentacion.
- res://Docs/Fase4.puml: diagramas de estados y clases PlantUML.

En C:\Users\ricar\Documents\Desmoche.Pruebas:
- GameManagerTests.cs: NUEVO; clase estatica global.
- Pruebas.csproj: REEMPLAZAR con el incluido; agrega Application/*.cs.
- Program.cs: REEMPLAZAR con el incluido; conserva las 61 pruebas anteriores
  y llama RulesTests.Run y GameManagerTests.Run una vez.

El paquete contiene archivos nuevos/modificados. Se instala sobre las fases previas,
no es un proyecto independiente. No poner GameManagerTests ni Program dentro de Godot.

## 4. Implementacion y contrato de comandos

Cada accion devuelve GameActionResult: Success, Message y Penalized.
Una accion ilegal ordinaria deja las cartas y el flujo intactos; LastMessage cambia.
Una penalizacion puede cambiar elegibilidad y activar un pago automatico.

Preparacion:
new GameManager(1, "Creador"); AddPlayer(2, "Jugador 2"); StartMatch().
Deck opcional en StartMatch permite fixtures reproducibles; no es una entrada de cliente.

Intercambio:
SelectExchangeCard(id, cardId), una vez por jugador. Hasta recibir todas,
ninguna mano cambia. Despues todas pasan una carta a la derecha y reciben de izquierda.

Declaraciones iniciales:
DeclareInitialVictory(id, kind, cardIds opcionales).
Color/Peladilla no llevan cardIds. LowStraight/HighStraight/Cuarta si.
FinishInitialDeclarations(id) renuncia a declarar al inicio. Cuando todos terminaron,
se habilita la primera extraccion. Juegos iniciales: se declaran antes de las bajas.

Robos y reclamos:
DrawFromDeck(id) publica una carta. Cada jugador habilitado envia SubmitClaim o
PassClaim. ResolveClaims solo funciona despues de todas las respuestas; los
eliminados se evaluan automaticamente. El orden de envio no define la prioridad.
Para declarar victoria normal en el reclamo: SubmitClaim(..., declareWin: true).
Para una excepcion con la PRIMERA carta: SubmitClaim(..., initialKind: kind).
Color/Peladilla no pueden usar esa carta.

Si nadie usa la primera carta, queda en pozo y se exige otra extraccion del mazo
por el mismo jugador. Si la primera se usa, no existe segundo robo extra.
Una carta posterior sin uso va al pozo y sigue el jugador a la derecha del origen.

DrawFromDiscard(id, plays, declareWin) reclama la carta superior del pozo durante
el turno; valida y juega la carta antes de retirarla. La carta no queda suelta en mano.

Mesa:
PublishMelds(id, plays) baja grupos nuevos o extiende grupos propios.
MeldPlay.CardIds contiene TODO el grupo resultante; MeldId es null para grupo nuevo.
Puede bajar juegos fuera de turno durante el juego.
Si cambia la mesa durante una oferta, su respuesta anterior se invalida y debe responder
otra vez. TransferMeldCard mueve una carta entre juegos propios conservando un grupo
valido de al menos 3 en el donante; destino debe usar la carta y no duplicar otras.
No se muestran estados intermedios de la transferencia.

Pago y victoria:
Tras jugar una oferta: AwaitingPayment con PaymentPlayerId.
PayCard solo admite cartas que permanezcan en su mano. Publica el pago para nuevos
reclamos, que pueden encadenarse. Si nadie usa el pago, sigue la derecha del pagador.
DeclareDesmoche verifica diez cartas en juegos y mano vacia; puede incluir bajas
restantes. No adjudica automaticamente por cantidad: debe declararse.
CompleteWithoutPayment solo funciona con mano vacia y no adjudica victoria.
El jugador automatico paga sin esperar una orden manual y no declara victoria.

Final:
WinnerPlayerId solo se asigna tras validacion. Finished rechaza mas acciones.
Si mazo se agota, se buscan usos legales del pozo entre jugadores habilitados;
se saltan turnos sin fuente legal. Si nadie puede usarlo, empate sin reciclar.
Sin jugadores habilitados tampoco hay una fuente legal de robo y finaliza en empate.
PrepareNextMatch limpia el flujo, conserva la lista y rota al primer jugador.
No hay puntuacion acumulativa.

## 5. Configuracion Godot

No hay nodos nuevos, scripts adjuntos, Inspector, señales ni RPC.
GameManager es una clase C# normal, no un Node ni Autoload.
La simulacion de consola es la prueba ejecutable de esta fase.
No se usa ninguna API nueva de Godot ni ENet.

## 6. Integracion

Crear Scripts/Application en el proyecto y copiar GameManager.cs.
El proyecto Godot incluye sus archivos C# automaticamente.
El proyecto de pruebas enlaza Domain y Application desde las carpetas reales.
Mantener RulesValidator, Card, Deck, Player, PlayerHand, Suit, Rank y DiscardPile actuales.
GameState mantiene las firmas de fase 2 y añade mesa/oferta/vistas.

Flujo:
Preparing -> InitialExchange -> InitialDeclarations -> AwaitingDraw ->
AwaitingClaims -> AwaitingPayment -> AwaitingClaims ...
Victoria o sin fuentes legales -> Finished.
Fase4.puml contiene ambos diagramas.

## 7. Prueba de aceptacion

1. Compilar Desmoche en Visual Studio.
2. Abrir PowerShell:
   cd "C:\Users\ricar\Documents\Desmoche.Pruebas"
   dotnet run

Deben aparecer:
REGLAS: 85 comprobaciones correctas; 1056 casos de escalera.
PARTIDA: 577 comprobaciones correctas.
SIMULACION: preparacion, intercambio, apertura, turnos, reclamos, pagos, victoria y empate verificados.
PRUEBAS COMPLETADAS: 61 comprobaciones correctas.

Las pruebas incluyen 2-4 jugadores, intercambio atomico, carta ajena, orden de reclamos
invertido, primera carta usada/no usada, prioridad del que saca, pagos, reclamo valido
e invalido del pozo, mesa, victoria explícita, excepciones iniciales, eliminacion,
pagos automaticos, mano vacia, vencimiento simulado, reorganizacion, empate,
rotacion de la siguiente partida y conservacion de 52 cartas.

## 8. Posibles errores

No encuentra Desmoche.Application: falta copiar GameManager o la linea Application
del Pruebas.csproj. No encuentra Meld: falta Meld.cs en Domain.
Duplicados: reemplazar GameState, no guardar una segunda copia .cs en el proyecto.
Solo 85/61 comprobaciones: falta la llamada GameManagerTests.Run en Program.
No se resuelven reclamos: falta PassClaim o SubmitClaim de un jugador habilitado.
No puede pagar: el pago solo se habilita despues de jugar; una carta de mesa no se paga.
Error de NuGet al crear obj: revisar el bloqueo de dotnet en Seguridad de Windows,
igual que al preparar las pruebas de fase 2.
Un FALLO PARTIDA imprime la comprobacion y el mensaje de la accion.

## 9. Estado de la fase

FASE COMPLETADA: NO en el proyecto del usuario hasta copiar y ejecutar la aceptacion.
Implementacion compilada y verificada: SI.
BLOQUEADORES: ninguno para la simulacion local. Politicas provisionales explicitadas arriba.
SIGUIENTE FASE: la que indique el usuario; no se implementa ENet.
