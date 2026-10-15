using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;

namespace Desmoche.Interaccion.Pruebas;

public static class ActionInteractionTests
{
    private static int _checks;

    public static int Run()
    {
        _checks = 0;
        CheckPhasePolicy();
        CheckRequests();
        CheckAuthoritativeEngine();
        CheckServerRequests();
        CheckInitialPresentation();
        Console.WriteLine($"OK: solicitudes visuales y validacion autoritativa ({_checks} comprobaciones).");
        return _checks;
    }

    private static void CheckPhasePolicy()
    {
        SnapshotDto initial = View();
        foreach (GamePhase phase in Enum.GetValues<GamePhase>())
        {
            SnapshotDto view = initial with { Phase = phase, OfferedCard = 33,
                DiscardTop = 34, DiscardCount = 1, PaymentPlayerId = 1 };
            foreach (ActionKind kind in Enum.GetValues<ActionKind>())
            {
                bool expected = phase switch
                {
                    GamePhase.InitialExchange => kind is ActionKind.ExchangeCard or ActionKind.DeclareInitial,
                    GamePhase.InitialDeclarations => kind is ActionKind.FinishInitial or ActionKind.DeclareInitial,
                    GamePhase.AwaitingDraw => kind is ActionKind.DrawDeck or
                        ActionKind.DrawDiscard or ActionKind.Publish,
                    GamePhase.AwaitingClaims => kind is ActionKind.Claim or
                        ActionKind.Pass or ActionKind.Publish,
                    GamePhase.AwaitingPayment => kind is ActionKind.Pay or
                        ActionKind.DeclareDesmoche or ActionKind.Publish,
                    _ => false
                };
                Check(InteractionActions.CanRequest(view, kind) == expected,
                    $"Disponibilidad de {kind} durante {phase}.");
            }
        }
        Check(!InteractionActions.CanRequest(null, ActionKind.Publish), "Sin vista no se solicita una accion.");
        Check(!InteractionActions.CanRequest(initial with
            { Players = initial.Players.Where(p => p.Id != 1).ToArray() }, ActionKind.Publish),
            "Un destinatario no registrado no puede solicitar acciones.");
        foreach (PublicPlayerDto own in new[]
                 { initial.Players[0] with { Connected = false }, initial.Players[0] with { Eliminated = true } })
            Check(!InteractionActions.CanRequest(initial with
                { Players = new[] { own, initial.Players[1] } }, ActionKind.Publish),
                "Un jugador desconectado o eliminado no puede publicar.");
        Check(InteractionActions.CanRequest(initial with { CurrentPlayerId = 2 }, ActionKind.Publish),
            "Publicar juegos no requiere turno propio.");
        Check(!InteractionActions.CanRequest(initial with { CurrentPlayerId = 2 }, ActionKind.DrawDeck),
            "Sacar del mazo requiere turno propio.");
        Check(!InteractionActions.CanRequest(initial with { DeckCount = 0 }, ActionKind.DrawDeck),
            "Un mazo agotado no puede utilizarse.");
        Check(!InteractionActions.CanRequest(initial, ActionKind.DrawDiscard), "Un pozo vacio no puede utilizarse.");
        Check(!InteractionActions.CanRequest(initial with { Phase = GamePhase.AwaitingClaims }, ActionKind.Claim),
            "Reclamar requiere una carta ofrecida.");
        foreach (var pair in new[] { (GamePhase.InitialExchange, ActionKind.ExchangeCard),
            (GamePhase.InitialDeclarations, ActionKind.FinishInitial),
            (GamePhase.InitialDeclarations, ActionKind.DeclareInitial), (GamePhase.AwaitingClaims, ActionKind.Claim),
            (GamePhase.AwaitingClaims, ActionKind.Pass) })
            Check(!InteractionActions.CanRequest(initial with { Phase = pair.Item1,
                OfferedCard = 33, OwnResponseSubmitted = true }, pair.Item2),
                "Una respuesta ya enviada no se repite.");
        Check(InteractionActions.CanRequest(initial with { Phase = GamePhase.AwaitingClaims,
            OfferedCard = 33, OwnResponseSubmitted = true }, ActionKind.Publish),
            "Una respuesta previa no impide publicar un juego propio.");
        SnapshotDto payment = initial with { Phase = GamePhase.AwaitingPayment, PaymentPlayerId = 2 };
        Check(!InteractionActions.CanRequest(payment, ActionKind.Pay), "No se paga por otro jugador.");
        Check(!InteractionActions.CanRequest(payment, ActionKind.DeclareDesmoche), "No se declara por otro jugador.");
        payment = payment with { PaymentPlayerId = 1 };
        Check(!InteractionActions.CanRequest(payment, ActionKind.CompleteWithoutPayment), "Una mano no vacia debe pagar.");
        Check(InteractionActions.CanRequest(payment with { OwnHand = Array.Empty<int>() },
            ActionKind.CompleteWithoutPayment), "La mano vacia puede terminar sin pagar.");
    }

    private static void CheckRequests()
    {
        SnapshotDto exchange = View() with { Phase = GamePhase.InitialExchange };
        var hand = new HandInteractionState();
        hand.Synchronize(exchange);
        Check(!Create(exchange, hand, ActionKind.ExchangeCard, out _), "El intercambio requiere seleccion.");
        hand.Toggle(0); hand.Toggle(1);
        Check(!Create(exchange, hand, ActionKind.ExchangeCard, out _), "El intercambio rechaza varias cartas.");
        hand.Toggle(1);
        Check(Create(exchange, hand, ActionKind.ExchangeCard, out var request) && request!.CardId == 0 &&
            request.Plays is null && !request.DeclareWin, "El intercambio contiene una carta y ninguna combinacion.");
        Check(!Create(exchange with { OwnHand = exchange.OwnHand.Where(id => id != 0).ToArray() },
            hand, ActionKind.ExchangeCard, out _), "Una seleccion antigua no puede utilizar una carta ausente.");
        SnapshotDto payment = View() with { Phase = GamePhase.AwaitingPayment, PaymentPlayerId = 1 };
        hand.Synchronize(payment); hand.Toggle(13);
        Check(Create(payment, hand, ActionKind.Pay, out request) && request!.CardId == 13,
            "El pago prepara una solicitud sin retirar la carta de la vista.");
        Check(payment.OwnHand.Contains(13), "Preparar un pago no modifica el estado recibido.");
        hand.ClearSelection();
        Check(Create(payment, hand, ActionKind.DeclareDesmoche, out request) && request!.Plays!.Length == 0,
            "Se puede declarar desmoche con los juegos ya publicados y sin borradores.");

        SnapshotDto publish = View() with { OwnHand = new[] { 0, 1, 2, 4, 8 } };
        string before = NetworkProtocol.Encode(publish);
        hand.Synchronize(publish);
        hand.Move(2, 0); hand.Toggle(2); hand.Toggle(0); hand.Toggle(1); hand.PrepareGroup();
        Check(Create(publish, hand, ActionKind.Publish, out request) &&
            request!.Plays![0].Cards.SequenceEqual(new[] { 2, 0, 1 }),
            "La solicitud conserva el orden declarado incluso si el servidor lo penalizaria.");
        Check(NetworkProtocol.Encode(publish) == before, "Preparar y construir una solicitud no muta el DTO.");
        int[] firstCopy = request!.Plays![0].Cards;
        firstCopy[0] = 51;
        Check(hand.BuildPlays()[0].Cards.SequenceEqual(new[] { 2, 0, 1 }), "La solicitud no comparte arrays con el borrador.");
        Check(!Create(publish with { OwnHand = new[] { 0, 1, 4, 8 } }, hand, ActionKind.Publish, out _),
            "Las cartas de un borrador antiguo deben seguir perteneciendo al jugador.");
        hand.ClearGroups(); hand.Toggle(0); hand.Toggle(4); hand.Toggle(8); hand.PrepareGroup();
        Check(Create(publish, hand, ActionKind.Publish, out _),
            "La politica visual no duplica las reglas de tercia, cuarta o escalera.");

        SnapshotDto claim = View() with { Phase = GamePhase.AwaitingClaims,
            OwnHand = new[] { 0, 1, 4 }, OfferedCard = 2 };
        hand.Synchronize(claim); hand.Toggle(0); hand.Toggle(1); hand.Toggle(4); hand.PrepareGroup();
        Check(!Create(claim, hand, ActionKind.Claim, out _), "Un reclamo debe usar la carta externa.");
        hand.ClearGroups(); hand.Toggle(0); hand.Toggle(1); hand.SelectExternal(true); hand.PrepareGroup();
        Check(!hand.ExternalSelected && hand.ExternalCardId == 2, "Preparar el grupo consume la seleccion externa, no su contexto.");
        Check(!Create(claim, hand, ActionKind.Publish, out _), "Publicar no puede apropiarse de una carta externa preparada.");
        Check(InteractionActions.TryCreate(claim, hand, ActionKind.Claim, true, out request, out _) &&
            request!.DeclareWin && request.Plays![0].Cards.SequenceEqual(new[] { 0, 1, 2 }),
            "El reclamo conserva la carta externa y la declaracion explicita.");
        SnapshotDto discard = claim with { Phase = GamePhase.AwaitingDraw, OfferedCard = null,
            DiscardTop = 2, DiscardCount = 1 };
        hand.Synchronize(discard); hand.Toggle(0); hand.Toggle(1); hand.SelectExternal(true); hand.PrepareGroup();
        Check(Create(discard, hand, ActionKind.DrawDiscard, out request) && request!.Plays![0].Cards.Contains(2),
            "Robar del pozo solo prepara una combinacion que usa su carta superior.");
        Check(!Create(discard with { DiscardTop = 3 }, hand, ActionKind.DrawDiscard, out _),
            "Una carta superior nueva invalida el reclamo antiguo.");

        SnapshotDto extension = View() with { OwnHand = new[] { 3, 4, 5 },
            Melds = new[] { new MeldDto(50, 1, new[] { 0, 1, 2 }), new MeldDto(60, 2, new[] { 13, 14, 15 }) } };
        hand.Synchronize(extension); hand.Toggle(3);
        Check(hand.PrepareGroup(50), "Una extension puede prepararse sobre un juego propio.");
        Check(Create(extension, hand, ActionKind.Publish, out request) && request!.Plays![0].MeldId == 50 &&
            request.Plays[0].Cards.SequenceEqual(new[] { 0, 1, 2, 3 }), "La extension envia el juego completo.");
        Check(!Create(extension with { Melds = new[] { extension.Melds[0] with { OwnerId = 2 } } },
            hand, ActionKind.Publish, out _), "Un objetivo que ya no es propio se rechaza.");
        Check(!Create(extension with { Melds = new[] { extension.Melds[0] with { Cards = new[] { 0, 1, 2, 6 } } } },
            hand, ActionKind.Publish, out _), "La extension debe conservar todas las cartas actuales del juego.");
        Check(InteractionActions.TryCreate(View(), new HandInteractionState(), ActionKind.DrawDeck,
            true, out request, out _) && !request!.DeclareWin, "Los robos del mazo nunca llevan una declaracion de victoria.");
    }

    private static void CheckAuthoritativeEngine()
    {
        GameManager game = PlayingGame();
        Check(game.CurrentPlayerId == 1, "La partida controlada comienza con el host.");
        GameActionResult published = game.PublishMelds(2, new[] { new MeldPlay(new[] { 4, 5, 6 }) });
        Check(published.Success && !published.Penalized, "El motor acepta una publicacion valida fuera del turno propio.");
        Check(game.CurrentPlayerId == 1 && game.State!.GetPlayer(2).Hand.Count == 6,
            "Publicar actualiza solo la mano correspondiente y conserva el turno.");
        Check(!game.PayCard(2, 7).Success, "El servidor rechaza pagos fuera del estado correspondiente.");
        int target = game.State!.Melds.Single().Id;
        Check(!game.PublishMelds(1, new[] { new MeldPlay(new[] { 4, 5, 6, 7 }, target) }).Success,
            "El motor rechaza extender el juego de otro jugador.");
        game = PlayingGame();
        published = game.PublishMelds(2, new[] { new MeldPlay(new[] { 4, 6, 5 }) });
        Check(published.Success && published.Penalized && game.EliminatedPlayers.Contains(2),
            "Una escalera desordenada es corregida y penalizada por el servidor.");
        Check(game.State!.Melds.Single().Cards.Select(c => c.Id).SequenceEqual(new[] { 4, 5, 6 }),
            "La correccion autoritativa conserva el juego del jugador eliminado.");
        game = PlayingGame();
        Check(!game.PublishMelds(2, new[] { new MeldPlay(new[] { 4, 7, 9 }) }).Success,
            "Las reglas del motor rechazan una combinacion que la UI puede preparar.");
        Check(game.State!.GetPlayer(2).Hand.Count == 9 && game.State.Melds.Count == 0,
            "Una combinacion rechazada no retira cartas ni publica juegos.");
        Check(game.DrawFromDeck(1).Success && game.State.OfferedCard!.Id == 2,
            "El robo abre una oferta publica y no entrega una carta libre a la mano.");
        Check(!game.SubmitClaim(2, new[] { new MeldPlay(new[] { 4, 5, 6 }) }).Success,
            "El servidor exige usar la carta ofrecida.");
        Check(game.SubmitClaim(2, new[] { new MeldPlay(new[] { 0, 1, 2 }) }).Success,
            "El servidor acepta un reclamo ordenado con la carta ofrecida.");
        Check(game.PassClaim(1).Success && game.ResolveClaims().Success,
            "La prioridad se resuelve despues de recibir las respuestas.");
        Check(game.Phase == GamePhase.AwaitingPayment && game.PaymentPlayerId == 2,
            "Reclamar una carta valida obliga al reclamante a pagar.");
        Check(!game.PayCard(1, 13).Success, "Otro jugador no puede pagar por el reclamante.");
        Check(game.PayCard(2, 4).Success && game.State.OfferedCard!.Id == 4 &&
            game.State.DiscardCount == 0 && !game.State.GetPlayer(2).Hand.Contains(4),
            "El pago confirmado queda como oferta para nuevos reclamos, no como pozo definitivo.");
        game = PlayingGame(); game.DrawFromDeck(1);
        GameActionResult registration = game.SubmitClaim(2, new[] { new MeldPlay(new[] { 0, 1, 2 }) }, true);
        Check(registration.Success && !registration.Penalized, "Una falsa victoria puede registrarse antes de resolver prioridad.");
        game.PassClaim(1); game.ResolveClaims();
        Check(game.EliminatedPlayers.Contains(2) && game.WinnerPlayerId is null,
            "El servidor penaliza la falsa victoria al ejecutar el reclamo.");
        game = PlayingGame(); game.DrawFromDeck(1); game.PassClaim(2);
        Check(game.HasAnsweredClaim(2), "El motor registra que el jugador paso.");
        Check(game.PublishMelds(2, new[] { new MeldPlay(new[] { 4, 5, 6 }) }).Success &&
            !game.HasAnsweredClaim(2), "Publicar durante reclamos obliga a responder nuevamente.");
    }

    private static void CheckServerRequests()
    {
        var server = new ServerSession("Host");
        Check(server.Join(77, NetworkProtocol.Version, "Invitado", out _), "La prueba incorpora un cliente real al contrato de sesion.");
        SnapshotDto host = server.ViewFor(1);
        var start = new CommandDto(NetworkProtocol.Version, host.Session, 1, host.Window, ActionKind.StartMatch);
        ReplyDto reply = server.Receive(1, NetworkProtocol.Encode(start), 1000);
        Check(reply.Success && server.ViewFor(1).Phase == GamePhase.InitialExchange, "La sesion inicia mediante comando validado.");
        SnapshotDto client = server.ViewFor(77);
        int ownCard = client.OwnHand[0];
        var pay = new CommandDto(NetworkProtocol.Version, client.Session, 1, client.Window, ActionKind.Pay, ownCard);
        string before = NetworkProtocol.Encode(client);
        reply = server.Receive(77, NetworkProtocol.Encode(pay), 2000);
        Check(!reply.Success && !reply.Penalized && server.ViewFor(77).OwnHand.Contains(ownCard),
            "Un cliente no puede pagar durante el intercambio aunque la carta sea propia.");
        Check(NetworkProtocol.Encode(client) == before, "La solicitud no modifica el snapshot anterior del cliente.");
        var duplicate = pay with { RequestId = 2, Kind = ActionKind.Publish, CardId = null,
            Plays = new[] { new PlayDto(new[] { ownCard, ownCard, ownCard }) } };
        Check(!NetworkProtocol.ValidCommand(duplicate), "El protocolo rechaza cartas duplicadas antes del motor.");
        reply = server.Receive(77, NetworkProtocol.Encode(duplicate), 3000);
        Check(!reply.Success && reply.RequestId == 0, "Una solicitud mal formada no obtiene una confirmacion de juego.");
        var stale = pay with { RequestId = 3, Kind = ActionKind.ExchangeCard, Window = client.Window - 1 };
        reply = server.Receive(77, NetworkProtocol.Encode(stale), 4000);
        Check(!reply.Success && reply.RequestId == 3, "Una oportunidad antigua se rechaza con su identificador correlacionado.");
        var foreign = pay with { RequestId = 4, Kind = ActionKind.ExchangeCard,
            CardId = server.ViewFor(1).OwnHand[0] };
        reply = server.Receive(77, NetworkProtocol.Encode(foreign), 5000);
        Check(!reply.Success && !reply.Penalized, "La identidad del remitente impide usar cartas de la mano del host.");
        var valid = pay with { RequestId = 5, Kind = ActionKind.ExchangeCard };
        reply = server.Receive(77, NetworkProtocol.Encode(valid), 6000);
        Check(reply.Success && server.ViewFor(77).OwnResponseSubmitted,
            "La selección válida confirma la respuesta del jugador.");
        Check(!server.ViewFor(77).OwnHand.Contains(ownCard) && server.ViewFor(77).OwnHand.Length == 8,
            "La carta desaparece al confirmar y espera en la reserva del intercambio.");
    }

    private static void CheckInitialPresentation()
    {
        int[] unordered = { 0, 14, 2, 26, 1, 45, 3, 22, 50 };
        Check(InitialHandPresentation.FindOrderedVictory(unordered, false) is null,
            "A234 separada no muestra Desmoche hasta ordenar la mano.");
        int[] ordered = { 0, 1, 2, 3, 14, 26, 45, 22, 50 };
        Check(InitialHandPresentation.FindOrderedVictory(ordered, false)?.Kind == InitialVictoryKind.LowStraight,
            "A234 ordenada habilita Desmoche antes del cambio.");
        Check(InitialHandPresentation.FindOrderedVictory(ordered, true)?.Kind == InitialVictoryKind.LowStraight,
            "A234 ordenada habilita Desmoche después del cambio.");
        Check(!InitialHandPresentation.HasVisibleMeld(unordered.Select(Card.FromId).ToArray()) &&
            !RulesValidator.IsPeladilla(unordered.Select(Card.FromId).ToArray()),
            "Un juego oculto permite mostrar Peladilla pero el servidor rechaza la declaración.");
        Check(InitialHandPresentation.HasVisibleMeld(ordered.Select(Card.FromId).ToArray()),
            "Un juego visible oculta Peladilla.");
        int[] paired = { 1, 14, 29, 44, 7, 22, 37, 51, 10 };
        Check(!InitialHandPresentation.HasVisibleMeld(paired.Select(Card.FromId).ToArray()) &&
            !RulesValidator.IsPeladilla(paired.Select(Card.FromId).ToArray()),
            "Una pareja sin juego visible permite mostrar Peladilla, pero la declaracion es falsa.");
        SnapshotDto challenge = View() with { Phase = GamePhase.AwaitingOrderChallenge,
            OrderChallenge = new OrderChallengeDto(2, 1, 2000) };
        Check(InteractionActions.CanRequest(challenge, ActionKind.SayPasa), "Pasa disponible para el rival.");
        SnapshotDto unused = challenge with { OrderChallenge = new OrderChallengeDto(2, 2, 3000, PasaReason.UnusedOffer, 1) };
        Check(InteractionActions.CanRequest(unused, ActionKind.SayPasa), "La oferta sin jugar habilita Pasa para los demás.");
        Check(!InteractionActions.CanRequest(unused with { RecipientId = 2 }, ActionKind.SayPasa), "El infractor de oferta no puede denunciarse.");
        Check(!InteractionActions.CanRequest(unused, ActionKind.Pass) && !InteractionActions.CanRequest(unused, ActionKind.Claim),
            "La denuncia pausa los reclamos y rechazos de oferta.");
        Check(!InteractionActions.CanRequest(challenge with { RecipientId = 2 }, ActionKind.SayPasa),
            "El infractor no puede presionar Pasa sobre su propio juego.");
        Check(!InteractionActions.CanRequest(challenge with { Players = challenge.Players.Select(p =>
            p.Id == 1 ? p with { Eliminated = true } : p).ToArray() }, ActionKind.SayPasa),
            "Un eliminado no participa en la oportunidad de Pasa.");
    }

    private static GameManager PlayingGame()
    {
        int[] clientHand = { 0, 1, 4, 5, 6, 7, 8, 9, 10 };
        int[] hostHand = Enumerable.Range(13, 9).ToArray();
        int[] firstDraws = Enumerable.Range(0, 9)
            .SelectMany(i => new[] { hostHand[i], clientHand[i] }).ToArray();
        int[] drawOrder = firstDraws.Concat(Enumerable.Range(0, 52).Except(firstDraws)).ToArray();
        var game = new GameManager(1, "Host");
        Check(game.AddPlayer(2, "Invitado").Success, "El motor controlado incorpora el invitado.");
        Check(game.StartMatch(new Deck(drawOrder.Reverse().Select(Card.FromId))).Success,
            "El motor controlado reparte una baraja completa sin duplicados.");
        Check(game.SelectExchangeCard(1, 21).Success && game.SelectExchangeCard(2, 10).Success,
            "El intercambio simultaneo deja intactas las cartas del caso de prueba.");
        Check(game.FinishInitialDeclarations(1).Success && game.FinishInitialDeclarations(2).Success,
            "Los jugadores pasan al primer turno normal.");
        return game;
    }

    private static SnapshotDto View() => new(NetworkProtocol.Version, new string('9', 32), 1, 5, 1,
        GamePhase.AwaitingDraw, MatchResult.None, 1, null, null,
        new[] { new PublicPlayerDto(1, "Host", 0, 9, true, false),
            new PublicPlayerDto(2, "Rival", 1, 9, true, false) },
        new[] { 0, 1, 2, 13, 14, 15, 26, 27, 28 }, Array.Empty<MeldDto>(),
        34, 0, null, null, Array.Empty<RevealedHandDto>(), false);

    private static bool Create(SnapshotDto view, HandInteractionState hand,
        ActionKind kind, out VisualActionRequest? request) =>
        InteractionActions.TryCreate(view, hand, kind, false, out request, out _);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Fallo: " + message);
        _checks++;
    }
}
