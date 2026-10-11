using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;

public static class ConnectionLifecycleTests
{
    public static void Run()
    {
        int checks = 0;
        var lobby = new GameManager(1, "Creador");
        Accept(lobby.AddPlayer(2, "Dos"), "Ingreso 2");
        Accept(lobby.AddPlayer(3, "Tres"), "Ingreso 3");
        Check(!lobby.RemovePreparingPlayer(1).Success, "No se retira el creador");
        Check(!lobby.RemovePreparingPlayer(99).Success, "No se retira un desconocido");
        Accept(lobby.DisconnectPlayer(2), "Salir durante preparacion");
        Check(lobby.Players.Select(p => p.Id).SequenceEqual(new[] { 1, 3 }) &&
            lobby.Players.Select(p => p.SeatIndex).SequenceEqual(new[] { 0, 1 }),
            "Salir del lobby reordena asientos");
        Accept(lobby.AddPlayer(4, "Cuatro"), "Se reutiliza el asiento libre");
        Check(lobby.Players[2].SeatIndex == 2, "No hay asientos duplicados");
        Accept(lobby.StartMatch(Deck.CreateStandard()), "Reparto de prueba");
        Check(!lobby.RemovePreparingPlayer(3).Success, "No se borra un jugador con cartas");
        Check(!lobby.DisconnectPlayer(1).Success, "El host debe cerrar la sesion");
        Check(!lobby.DisconnectPlayer(99).Success, "Desconexion desconocida rechazada");

        for (int count = 2; count <= 4; count++)
        {
            var exchange = Create(count);
            int chosen = exchange.State!.GetPlayer(2).Hand.Cards.Min(c => c.Id);
            Accept(exchange.DisconnectPlayer(2), "Desconexion antes de elegir intercambio");
            Check(exchange.HasExchanged(2), "Seleccion automatica pendiente registrada");
            Check(!exchange.SelectExchangeCard(2, chosen).Success, "Desconectado no elige manualmente");
            foreach (Player p in exchange.Players.Where(p => p.Id != 2))
                Accept(exchange.SelectExchangeCard(p.Id, p.Hand.Cards[0].Id), "Eleccion conectada");
            int receiver = count == 2 ? 1 : 3;
            Check(exchange.State!.GetPlayer(receiver).Hand.Contains(chosen),
                "La seleccion automatica llega a la derecha");
            Check(exchange.Phase == GamePhase.InitialDeclarations && exchange.HasFinishedInitial(2),
                "Desconectado queda listo al terminar el intercambio mas tarde");
            Check(exchange.Players.All(p => p.Hand.Count == 9), "Intercambio conserva nueve por jugador");
            Check(!exchange.FinishInitialDeclarations(2).Success, "No acepta respuesta manual del ausente");
            foreach (Player p in exchange.Players.Where(p => p.Id != 2))
                Accept(exchange.FinishInitialDeclarations(p.Id), "Conectado termina declaraciones");
            Check(exchange.Phase == GamePhase.AwaitingDraw && exchange.CurrentPlayerId == 1,
                "La desconexion no bloquea el inicio");
            Check(exchange.DisconnectedPlayers.Contains(2) && !exchange.EliminatedPlayers.Contains(2),
                "Desconexion y penalizacion son distintas");
            Integrity(exchange);
        }

        var selected = Create(3);
        int selectedId = selected.State!.GetPlayer(2).Hand.Cards.Max(c => c.Id);
        Accept(selected.SelectExchangeCard(2, selectedId), "Seleccion previa");
        Accept(selected.DisconnectPlayer(2), "Desconexion con seleccion previa");
        foreach (Player p in selected.Players.Where(p => p.Id != 2))
            Accept(selected.SelectExchangeCard(p.Id, p.Hand.Cards[0].Id), "Completar intercambio");
        Check(selected.State!.GetPlayer(3).Hand.Contains(selectedId), "Se conserva la seleccion previa");

        var declarations = Create(3);
        Exchange(declarations);
        Accept(declarations.FinishInitialDeclarations(1), "Creador listo");
        Accept(declarations.DisconnectPlayer(2), "Ausente durante declaraciones");
        Check(declarations.HasFinishedInitial(2), "Ausente no bloquea declaraciones");
        Accept(declarations.FinishInitialDeclarations(3), "Ultimo listo");
        Check(declarations.Phase == GamePhase.AwaitingDraw, "Declaraciones completadas");
        GameState beforeRepeat = declarations.State!;
        Accept(declarations.DisconnectPlayer(2), "Desconexion idempotente");
        Check(ReferenceEquals(beforeRepeat, declarations.State), "Repetir desconexion no mueve cartas");

        var turn = Create(3);
        Ready(turn);
        Accept(turn.DrawFromDeck(1), "Primera oferta");
        PassAll(turn);
        Accept(turn.DrawFromDeck(1), "Segunda oferta");
        PassAll(turn);
        Check(turn.CurrentPlayerId == 2, "Le corresponde al futuro ausente");
        int handBefore = turn.State!.GetPlayer(2).Hand.Count;
        Accept(turn.DisconnectPlayer(2), "Desconexion durante turno");
        Check(turn.CurrentPlayerId == 3 && turn.State.GetPlayer(2).Hand.Count == handBefore,
            "Salta el turno y conserva la mano");
        Check(!turn.DrawFromDeck(2).Success, "Ausente no puede robar");
        Integrity(turn);

        var waiting = Create(3);
        Ready(waiting);
        Accept(waiting.DrawFromDeck(1), "Oferta con tres");
        Check(!waiting.ClaimsReady, "Requiere respuestas");
        Accept(waiting.PassClaim(2), "Respuesta antes de desconectar");
        Check(waiting.HasAnsweredClaim(2), "Bandera privada de respuesta");
        Accept(waiting.DisconnectPlayer(2), "Ausente durante oferta");
        Check(!waiting.HasAnsweredClaim(2), "Se elimina respuesta del ausente");
        Accept(waiting.PassClaim(1), "Creador responde");
        Check(!waiting.ClaimsReady, "Aun falta conectado");
        Accept(waiting.PassClaim(3), "Ultimo conectado responde");
        Check(waiting.ClaimsReady, "El ausente no bloquea la ventana");
        Accept(waiting.ResolveClaims(), "Resolver tras desconexion");
        Check(!waiting.ClaimsReady, "No indica respuestas listas fuera de ventana");
        Integrity(waiting);

        var queued = Rig(new[] { Array.Empty<int>(), new[] { S(3), S(4) } }, new[] { S(5) });
        Accept(queued.DrawFromDeck(1), "Oferta reclamable");
        Accept(queued.SubmitClaim(2, new[] { Play(S(3), S(4), S(5)) }), "Reclamo privado registrado");
        Accept(queued.DisconnectPlayer(2), "Ausente antes de resolver");
        Accept(queued.PassClaim(1), "Conectado pasa");
        Check(queued.ClaimsReady, "Puede resolver sin el ausente");
        Accept(queued.ResolveClaims(), "No ejecuta reclamo manual del desconectado");
        Check(queued.State!.Melds.Count == 0 && queued.State.DiscardTop!.Id == S(5),
            "Solo quedan habilitadas extensiones automaticas publicas");
        Integrity(queued);

        var payment = Rig(new[] { Array.Empty<int>(), new[] { S(3), S(4) }, Array.Empty<int>() },
            new[] { S(5) });
        Accept(payment.DrawFromDeck(1), "Oferta para pago");
        Accept(payment.SubmitClaim(2, new[] { Play(S(3), S(4), S(5)) }), "Usar oferta");
        Accept(payment.PassClaim(1), "Pasa 1");
        Accept(payment.PassClaim(3), "Pasa 3");
        Accept(payment.ResolveClaims(), "Reclamo aceptado");
        Check(payment.PaymentPlayerId == 2, "Pago pendiente");
        int payId = payment.State!.GetPlayer(2).Hand.Cards.Min(c => c.Id);
        int payCount = payment.State.GetPlayer(2).Hand.Count;
        Accept(payment.DisconnectPlayer(2), "Desconexion con pago pendiente");
        Check(payment.Phase == GamePhase.AwaitingClaims && payment.State.OfferedCard!.Id == payId &&
            payment.State.GetPlayer(2).Hand.Count == payCount - 1,
            "Paga automaticamente una sola carta y abre nueva oferta");
        Check(!payment.PayCard(2, payId).Success && !payment.DeclareDesmoche(2).Success,
            "Ausente no puede pagar manualmente ni ganar");
        PassAll(payment);
        Check(payment.CurrentPlayerId == 3, "Siguiente turno a derecha del pagador");
        Integrity(payment);

        var automatic = Rig(new[] { Array.Empty<int>(), new[] { S(2), S(3), S(4) }, Array.Empty<int>() },
            new[] { S(5), S(6) });
        Accept(automatic.PublishMelds(2, new[] { Play(S(2), S(3), S(4)) }), "Juego publico antes de salir");
        Accept(automatic.DisconnectPlayer(2), "Ausente con juego publico");
        int autoHand = automatic.State!.GetPlayer(2).Hand.Count;
        Accept(automatic.DrawFromDeck(1), "Oferta compatible con juego ausente");
        Accept(automatic.PassClaim(1), "Pasa creador");
        Accept(automatic.PassClaim(3), "Pasa tercero");
        Accept(automatic.ResolveClaims(), "Extension automatica");
        Check(automatic.State.Melds.Single().Cards.Count == 4 &&
            automatic.State.GetPlayer(2).Hand.Count == autoHand - 1 &&
            automatic.WinnerPlayerId is null && automatic.Phase == GamePhase.AwaitingClaims,
            "Ausente extiende juego, paga y nunca gana");
        PassAll(automatic);
        Check(automatic.CurrentPlayerId == 3, "Prioridad de turnos tras pago automatico");
        Integrity(automatic);

        int[] red = { 26, 28, 30, 32, 34, 14, 16, 18, 20 };
        var finished = Rig(new[] { red, Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>(), false);
        Accept(finished.DeclareInitialVictory(1, InitialVictoryKind.Color), "Finalizar por color");
        GameState end = finished.State!;
        Accept(finished.DisconnectPlayer(2), "Salida tras finalizar");
        Check(ReferenceEquals(end, finished.State) && finished.WinnerPlayerId == 1,
            "Salida posterior no cambia resultado");
        Accept(finished.PrepareNextMatch(), "Preparar revancha");
        Check(finished.DisconnectedPlayers.Count == 0, "Se limpia estado de conexion de partida");
        Accept(finished.RemovePreparingPlayer(2), "Sesion retira ausente antes del nuevo reparto");
        Accept(finished.StartMatch(Deck.CreateStandard()), "Repartir solo presentes");
        Ready(finished);
        Check(finished.Players.Count == 2 && finished.CurrentPlayerId == 3,
            "Si salio el siguiente inicial, comienza el siguiente presente");
        Integrity(finished);

        Console.WriteLine("CONEXIONES: " + checks + " comprobaciones correctas.");

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FALLO CONEXIONES: " + description);
            checks++;
        }
        void Accept(GameActionResult result, string description) =>
            Check(result.Success, description + ": " + result.Message);
        GameManager Create(int count)
        {
            var game = new GameManager(1, "Creador");
            for (int id = 2; id <= count; id++) Accept(game.AddPlayer(id, "Jugador " + id), "Incorporar");
            Accept(game.StartMatch(Deck.CreateStandard()), "Repartir");
            return game;
        }
        void Exchange(GameManager game)
        {
            foreach (Player p in game.Players)
                Accept(game.SelectExchangeCard(p.Id, p.Hand.Cards[0].Id), "Seleccionar intercambio");
        }
        void Ready(GameManager game)
        {
            Exchange(game);
            foreach (Player p in game.Players)
                Accept(game.FinishInitialDeclarations(p.Id), "Terminar declaraciones");
        }
        void PassAll(GameManager game)
        {
            foreach (Player p in game.Players)
                if (!game.EliminatedPlayers.Contains(p.Id) && !game.DisconnectedPlayers.Contains(p.Id))
                    Accept(game.PassClaim(p.Id), "Pasar");
            Accept(game.ResolveClaims(), "Resolver");
        }
        void Integrity(GameManager game)
        {
            GameState state = game.State!;
            Check(state.DeckCount + state.DiscardCount + state.Players.Sum(p => p.Hand.Count) +
                state.Melds.Sum(m => m.Cards.Count) + (state.OfferedCard is null ? 0 : 1) == 52,
                "Conserva exactamente 52 cartas");
        }
        GameManager Rig(int[][] partial, int[] top, bool ready = true)
        {
            var reserved = new HashSet<int>(partial.SelectMany(h => h).Concat(top));
            if (reserved.Count != partial.Sum(h => h.Length) + top.Length)
                throw new Exception("Fixture de conexion duplicado");
            var desired = partial.Select(h => h.ToList()).ToArray();
            int candidate = 0;
            for (int i = 0; i < desired.Length; i++)
                while (desired[i].Count < 9)
                {
                    while (reserved.Contains(candidate)) candidate++;
                    desired[i].Add(candidate);
                    reserved.Add(candidate++);
                }
            var before = Enumerable.Range(0, desired.Length)
                .Select(i => desired[i].Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray()).ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++)
                for (int i = 0; i < before.Length; i++) draws.Add(before[i][round]);
            draws.AddRange(top);
            draws.AddRange(Enumerable.Range(0, 52).Where(id => !reserved.Contains(id)));
            var game = new GameManager(1, "Creador");
            for (int id = 2; id <= partial.Length; id++) Accept(game.AddPlayer(id, "Jugador " + id), "Agregar fixture");
            Accept(game.StartMatch(new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId))), "Repartir fixture");
            foreach (Player p in game.Players)
                Accept(game.SelectExchangeCard(p.Id, p.Hand.Cards[^1].Id), "Intercambiar fixture");
            if (ready)
                foreach (Player p in game.Players) Accept(game.FinishInitialDeclarations(p.Id), "Listo fixture");
            return game;
        }
    }
    private static int S(int rank) => 39 + rank - 1;
    private static MeldPlay Play(params int[] ids) => new(ids);
}
