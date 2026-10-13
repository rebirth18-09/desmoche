using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;
using Desmoche.Application;

public static class GameManagerTests
{
    public static void Run()
    {
        int checks = 0;
        var lobby = new GameManager(1, "Creador");
        Check(!lobby.StartMatch().Success, "No inicia con un jugador.");
        Accept(lobby.AddPlayer(2, "Dos"), "Incorporar segundo jugador.");
        Check(!lobby.AddPlayer(2, "Duplicado").Success, "Id repetido rechazado.");
        Accept(lobby.AddPlayer(3, "Tres"), "Incorporar tercero.");
        Accept(lobby.AddPlayer(4, "Cuatro"), "Incorporar cuarto.");
        Check(!lobby.AddPlayer(5, "Cinco").Success, "Quinto jugador rechazado.");
        Accept(lobby.StartMatch(Deck.CreateStandard()), "Reparto local.");
        Check(!lobby.AddPlayer(6, "Tarde").Success, "Ingreso durante partida rechazado.");
        Check(!lobby.DrawFromDeck(1).Success, "No roba durante intercambio.");
        GameState beforeExchange = lobby.State!;
        int choice = beforeExchange.GetPlayer(1).Hand.Cards[0].Id;
        Accept(lobby.SelectExchangeCard(1, choice), "Primera seleccion privada.");
        Check(lobby.State!.GetPlayer(1).Hand.Count == 8 && !lobby.State.GetPlayer(1).Hand.Contains(choice) &&
            beforeExchange.GetPlayer(1).Hand.Count == 9, "La carta confirmada queda reservada fuera de la mano sin modificar el estado anterior.");
        Check(!lobby.SelectExchangeCard(1, choice).Success, "Seleccion repetida rechazada.");
        Check(!lobby.SelectExchangeCard(2, choice).Success, "Carta ajena en intercambio rechazada.");
        var outgoing = new Dictionary<int, int> { [1] = choice };
        for (int id = 2; id <= 4; id++)
        {
            outgoing[id] = beforeExchange.GetPlayer(id).Hand.Cards[0].Id;
            Accept(lobby.SelectExchangeCard(id, outgoing[id]), "Seleccion para intercambio.");
        }
        for (int id = 1; id <= 4; id++)
            Check(lobby.State!.GetPlayer(id == 4 ? 1 : id + 1).Hand.Contains(outgoing[id]),
                "Entrega al jugador de la derecha.");
        Check(lobby.State!.Players.All(p => p.Hand.Count == 9), "Todos conservan nueve.");
        Integrity(lobby);
        for (int id = 1; id <= 4; id++) Accept(lobby.FinishInitialDeclarations(id), "Listo.");
        Check(lobby.CurrentPlayerId == 1, "Inicia el creador.");

        for (int count = 2; count <= 4; count++)
        {
            var g = Rig(Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray(),
                new[] { 51, 50, 49 });
            int initialDeck = g.State!.DeckCount;
            Check(!g.DrawFromDeck(2).Success, "Robo fuera de turno rechazado.");
            Check(!g.PayCard(1, g.State.GetPlayer(1).Hand.Cards[0].Id).Success,
                "No descarta antes de jugar una carta.");
            Accept(g.DrawFromDeck(1), "Primera carta publica.");
            Check(g.GetViewFor(2).Cards.OfferedCard!.Id == 51, "La oferta es publica.");
            GameState offered = g.State!;
            Check(!g.DrawFromDeck(1).Success && ReferenceEquals(offered, g.State),
                "Doble robo rechazado sin cambiar cartas.");
            Check(!g.ResolveClaims().Success, "Espera todas las respuestas.");
            PassAll(g);
            Check(g.Phase == GamePhase.AwaitingDraw && g.CurrentPlayerId == 1,
                "Primera no usada: mismo jugador.");
            Check(g.State!.DiscardTop!.Id == 51 && g.State.DeckCount == initialDeck - 1,
                "Primera carta queda en pozo.");
            Check(!g.DrawFromDiscard(1, new[] { Play(51, 50, 49) }).Success,
                "La segunda extraccion obligatoria es del mazo.");
            Accept(g.DrawFromDeck(1), "Segunda carta normal.");
            PassAll(g);
            Check(g.CurrentPlayerId == 2, "Segunda no usada cambia turno.");
            Integrity(g);
        }

        // Dos reclamos: el orden de envio no cambia la prioridad.
        foreach (bool reverse in new[] { false, true })
        {
            var priority = Rig(new[] { Array.Empty<int>(),
                new[] { S(3), S(4) }, new[] { S(6), S(7) } }, new[] { S(5) });
            Accept(priority.DrawFromDeck(1), "Oferta compartida.");
            var claims = reverse ? new[] { 3, 2 } : new[] { 2, 3 };
            foreach (int id in claims)
                Accept(priority.SubmitClaim(id, new[] { id == 2 ?
                    Play(S(3), S(4), S(5)) : Play(S(5), S(6), S(7)) }),
                    "Registrar reclamo.");
            Accept(priority.PassClaim(1), "Creador pasa.");
            Accept(priority.ResolveClaims(), "Resolver prioridad.");
            Check(priority.PaymentPlayerId == 2, "Gana el primero a la derecha.");
            int remainingDeck = priority.State!.DeckCount;
            Check(!priority.PayCard(2, S(5)).Success, "La reclamada no se descarta.");
            Check(!priority.PayCard(1, priority.State.GetPlayer(1).Hand.Cards[0].Id).Success,
                "Otro jugador no paga por el reclamante.");
            Accept(priority.PayCard(2, priority.State.GetPlayer(2).Hand.Cards[0].Id),
                "Pagar desde mano propia.");
            PassAll(priority);
            Check(priority.CurrentPlayerId == 3 && priority.State.DeckCount == remainingDeck,
                "Primera usada: sigue derecha del reclamante, sin segunda extra.");
            Integrity(priority);
        }

        var ownerPriority = Rig(new[] { new[] { S(3), S(4) },
            new[] { S(6), S(7) } }, new[] { S(5) });
        Accept(ownerPriority.DrawFromDeck(1), "Oferta del creador.");
        Accept(ownerPriority.SubmitClaim(2, new[] { Play(S(5), S(6), S(7)) }), "Otro reclama.");
        Accept(ownerPriority.SubmitClaim(1, new[] { Play(S(3), S(4), S(5)) }), "Creador reclama.");
        Accept(ownerPriority.ResolveClaims(), "Prioridad del que saca.");
        Check(ownerPriority.PaymentPlayerId == 1, "Quien saca tiene prioridad si la usa.");

        int[] winningHand = { C(6), D(6), H(6), S(2), S(3), S(4), S(7), S(8), S(9) };
        var winner = Rig(new[] { winningHand, Array.Empty<int>() },
            new[] { D(1), S(6) });
        Accept(winner.PublishMelds(1, new[] { Play(D(6), C(6), H(6)),
            Play(S(2), S(3), S(4)), Play(S(7), S(8), S(9)) }),
            "Bajar juegos iniciales durante la partida.");
        Check(winner.State!.GetPlayer(1).Hand.Count == 0 && winner.State.Melds.Count == 3,
            "Las bajas abandonan la mano.");
        Check(winner.GetViewFor(2).Cards.Melds.Count == 3 &&
            winner.GetViewFor(2).Cards.RevealedHands.Count == 0,
            "Juegos publicos; manos rivales ocultas.");
        Accept(winner.DrawFromDeck(1), "Primera carta sin uso.");
        PassAll(winner);
        Accept(winner.DrawFromDeck(1), "Carta diez.");
        int setId = winner.State.Melds[0].Id;
        Accept(winner.SubmitClaim(1, new[] {
            new MeldPlay(new[] { C(6), D(6), S(6), H(6) }, setId) }, true),
            "Declarar desmoche al extender juego.");
        Accept(winner.PassClaim(2), "Rival pasa.");
        Accept(winner.ResolveClaims(), "Adjudicar victoria.");
        Check(winner.Phase == GamePhase.Finished && winner.WinnerPlayerId == 1 &&
            winner.Result == MatchResult.Desmoche, "Desmoche finaliza.");
        Check(winner.State.OfferedCard is null && winner.State.DiscardTop!.Id == D(1),
            "No hay descarte final.");
        Check(winner.GetViewFor(2).Cards.RevealedHands.Count == 2,
            "Victoria revela todas las manos.");
        Check(!winner.DrawFromDeck(1).Success && !winner.PayCard(1, S(6)).Success,
            "No se juega despues de finalizar.");
        Integrity(winner);
        Accept(winner.PrepareNextMatch(), "Preparar siguiente partida.");
        Accept(winner.StartMatch(Deck.CreateStandard()), "Nuevo reparto.");
        foreach (Player p in winner.State!.Players)
            Accept(winner.SelectExchangeCard(p.Id, p.Hand.Cards[0].Id), "Nuevo intercambio.");
        foreach (Player p in winner.State.Players)
            Accept(winner.FinishInitialDeclarations(p.Id), "Listo en nueva partida.");
        Check(winner.CurrentPlayerId == 2, "Siguiente partida inicia a la derecha.");

        var lateDeclaration = Rig(new[] { winningHand, Array.Empty<int>() },
            new[] { S(6) });
        Accept(lateDeclaration.DrawFromDeck(1), "Oferta ganadora.");
        Accept(lateDeclaration.SubmitClaim(1, new[] {
            Play(C(6), D(6), S(6), H(6)), Play(S(2), S(3), S(4)),
            Play(S(7), S(8), S(9)) }), "Jugar sin declarar todavia.");
        Accept(lateDeclaration.PassClaim(2), "Rival pasa.");
        Accept(lateDeclaration.ResolveClaims(), "Jugar las diez.");
        Check(lateDeclaration.WinnerPlayerId == 1, "Diez cartas ganan automaticamente al jugarse.");
        Check(!lateDeclaration.DeclareDesmoche(1).Success, "No se repite una victoria ya resuelta.");
        Check(lateDeclaration.WinnerPlayerId == 1, "Declaracion verificada por motor.");

        var initial = Rig(new[] { new[] { H(1), H(2), H(3) },
            Array.Empty<int>() }, new[] { H(4) });
        Accept(initial.DrawFromDeck(1), "Primera carta completa excepcion.");
        Accept(initial.SubmitClaim(1, new[] { Play(H(1), H(2), H(3), H(4)) },
            initialKind: InitialVictoryKind.LowStraight), "Declarar A234 con primera carta.");
        Accept(initial.PassClaim(2), "Rival pasa.");
        Accept(initial.ResolveClaims(), "Resolver excepcion inicial.");
        Check(initial.Result == MatchResult.InitialVictory &&
            initial.State!.GetPlayer(1).Hand.Count == 6, "Excepcion permite cartas sobrantes.");
        Integrity(initial);

        int[] red = { H(1), H(3), H(5), H(7), H(9), D(2), D(4), D(6), D(8) };
        var color = Rig(new[] { red, Array.Empty<int>() }, Array.Empty<int>(), false);
        Accept(color.DeclareInitialVictory(1, InitialVictoryKind.Color, red.OrderBy(id => (int)Card.FromId(id).Rank).ToArray()), "Declarar color.");
        Check(color.Phase == GamePhase.Finished && color.State!.Melds.Count == 0,
            "Color gana antes de sacar del mazo.");

        var penalty = Rig(new[] { new[] { S(2), S(3), S(4) }, Array.Empty<int>() },
            Enumerable.Range(5, 8).Select(S).ToArray());
        GameActionResult badOrder = penalty.PublishMelds(1,
            new[] { Play(S(2), S(4), S(3)) });
        Check(badOrder.Success && badOrder.Penalized &&
            penalty.EliminatedPlayers.Contains(1), "Orden incorrecto elimina y compone.");
        Check(penalty.State!.Melds[0].Cards.Select(c => (int)c.Rank)
            .SequenceEqual(new[] { 2, 3, 4 }), "Juego corregido automaticamente.");
        Check(!penalty.DrawFromDeck(1).Success && !penalty.SubmitClaim(1,
            new[] { Play(S(2), S(3), S(4), S(5)) }).Success,
            "El eliminado no tiene turnos ni reclamos manuales.");
        for (int rank = 5; rank <= 12; rank++)
        {
            Accept(penalty.DrawFromDeck(2), "Carta para juego del eliminado.");
            Accept(penalty.PassClaim(2), "Activo pasa.");
            int handBefore = penalty.State.GetPlayer(1).Hand.Count;
            Accept(penalty.ResolveClaims(), "Ingreso y pago automaticos.");
            Check(penalty.State.Melds[0].Cards.Count == rank - 1 &&
                penalty.WinnerPlayerId is null, "El eliminado acumula sin ganar.");
            if (handBefore > 0)
            {
                Check(penalty.State.GetPlayer(1).Hand.Count == handBefore - 1,
                    "Pago automatico retira una carta.");
                PassAll(penalty);
            }
            else Check(penalty.Phase == GamePhase.AwaitingDraw,
                "Sin mano para pagar, continua la partida.");
            Integrity(penalty);
        }
        Check(penalty.State.GetPlayer(1).Hand.Count == 0 &&
            penalty.State.Melds[0].Cards.Count == 11 && penalty.CurrentPlayerId == 2,
            "Once en mesa del eliminado, sin victoria.");

        var timeout = Rig(new[] { new[] { S(2), S(3), S(4) }, Array.Empty<int>() },
            new[] { S(5) });
        Accept(timeout.PublishMelds(1, new[] { Play(S(2), S(3), S(4)) }),
            "Juego publico para vencimiento.");
        Accept(timeout.DrawFromDeck(1), "Oferta pendiente.");
        Accept(timeout.ExpireClaims(), "Vencimiento simulado.");
        Check(timeout.State!.Melds[0].Cards.Count == 4 &&
            timeout.EliminatedPlayers.Count == 0, "Vencimiento usa juego publico sin eliminar.");
        Integrity(timeout);

        var reorganize = Rig(new[] { new[] { C(6), D(6), H(6), S(6), S(4), S(5) },
            Array.Empty<int>() }, Array.Empty<int>());
        Accept(reorganize.PublishMelds(1, new[] { Play(C(6), D(6), S(6), H(6)) }),
            "Publicar cuarta donante.");
        int donor = reorganize.State!.Melds[0].Id;
        Accept(reorganize.TransferMeldCard(1, donor, S(6), Play(S(4), S(5), S(6))),
            "Traslado atomico a nuevo juego.");
        Check(reorganize.State.Melds[0].Cards.Count == 3 &&
            reorganize.State.Melds[1].Cards.Count == 3, "Donante y destino validos.");
        GameState beforeBadTransfer = reorganize.State;
        Check(!reorganize.TransferMeldCard(1, donor, C(6), Play(C(4), C(5), C(6))).Success &&
            ReferenceEquals(beforeBadTransfer, reorganize.State),
            "Traslado ilegal no modifica cartas.");
        Integrity(reorganize);

        // Reclamar el pozo antiguo durante el turno; uso obligatorio.
        var pile = Rig(new[] { Array.Empty<int>(), new[] { S(3), S(4) } },
            new[] { D(13), S(5) });
        Accept(pile.DrawFromDeck(1), "Primera carta al pozo.");
        PassAll(pile);
        Accept(pile.DrawFromDeck(1), "Segunda carta normal.");
        PassAll(pile);
        Check(pile.CurrentPlayerId == 2 && pile.State!.DiscardTop!.Id == S(5),
            "Pozo disponible al siguiente turno.");
        GameState beforeBadPile = pile.State!;
        Check(!pile.DrawFromDiscard(2, new[] { Play(S(3), S(4)) }).Success &&
            ReferenceEquals(beforeBadPile, pile.State),
            "Reclamo sin juego o sin carta del pozo rechazado.");
        Accept(pile.DrawFromDiscard(2, new[] { Play(S(3), S(4), S(5)) }),
            "Reclamo legal del pozo.");
        Check(pile.PaymentPlayerId == 2 && pile.State!.DiscardCount == 1 &&
            pile.State.Melds[0].Cards.Any(c => c.Id == S(5)) &&
            pile.State.GetPlayer(2).Hand.Count == 7,
            "El pozo se retira solo al jugar la carta.");
        Check(!pile.PayCard(2, S(5)).Success, "No se paga con la carta del pozo jugada.");
        Accept(pile.PayCard(2, pile.State!.GetPlayer(2).Hand.Cards[0].Id), "Pago del reclamo.");
        PassAll(pile);
        Check(pile.CurrentPlayerId == 1, "Turno a la derecha del reclamante del pozo.");
        Integrity(pile);

        // Mazo agotado: fixture elige una ultima carta sin uso legal.
        var drawGame = Rig(new[] { Array.Empty<int>(), Array.Empty<int>() },
            Array.Empty<int>());
        // Todas las cartas se rechazan explicitamente; si la ultima es reclamable,
        // se juega para agotar las posibilidades restantes sin reciclar el pozo.
        int safety = 0;
        while (drawGame.Phase != GamePhase.Finished && safety++ < 200)
        {
            if (drawGame.State!.DeckCount > 0)
            { Accept(drawGame.DrawFromDeck(drawGame.CurrentPlayerId!.Value), "Simular robo."); PassAll(drawGame); }
            else
            {
                int id = drawGame.CurrentPlayerId!.Value;
                MeldPlay? legal = FindUse(drawGame.State, id, drawGame.State.DiscardTop!);
                Check(legal is not null, "Si sigue la partida existe un reclamo legal.");
                Accept(drawGame.DrawFromDiscard(id, new[] { legal! }), "Ultimo recurso del pozo.");
                if (drawGame.State.GetPlayer(id).Hand.Count == 0)
                    Accept(drawGame.CompleteWithoutPayment(id), "Sin pago disponible.");
                else
                { Accept(drawGame.PayCard(id, drawGame.State.GetPlayer(id).Hand.Cards[0].Id), "Pagar."); PassAll(drawGame); }
            }
            Integrity(drawGame);
        }
        Check(drawGame.Phase == GamePhase.Finished && drawGame.Result == MatchResult.Draw &&
            drawGame.WinnerPlayerId is null, "Sin fuentes legales termina en empate.");

        Console.WriteLine("PARTIDA: " + checks + " comprobaciones correctas.");
        Console.WriteLine("SIMULACION: preparacion, intercambio, apertura, turnos, reclamos, pagos, victoria y empate verificados.");

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FALLO PARTIDA: " + description);
            checks++;
        }
        void Accept(GameActionResult result, string description)
            => Check(result.Success, description + " " + result.Message);
        void Integrity(GameManager game)
        {
            GameState state = game.State!;
            int total = state.DeckCount + state.DiscardCount +
                state.Players.Sum(p => p.Hand.Count) + state.Melds.Sum(m => m.Cards.Count) +
                (state.OfferedCard is null ? 0 : 1);
            Check(total == 52, "Conservacion de 52 cartas.");
        }
        void PassAll(GameManager game)
        {
            foreach (Player player in game.Players)
                if (!game.EliminatedPlayers.Contains(player.Id) && !game.HasAnsweredClaim(player.Id))
                    Accept(game.PassClaim(player.Id), "No reclamar.");
            Accept(game.ResolveClaims(), "Cerrar ventana de reclamos.");
        }
        GameManager Rig(int[][] partial, int[] top, bool ready = true)
        {
            var reserved = new HashSet<int>(partial.SelectMany(h => h).Concat(top));
            int expected = partial.Sum(h => h.Length) + top.Length;
            if (reserved.Count != expected) throw new Exception("Fixture duplicado.");
            var desired = partial.Select(h => h.ToList()).ToArray();
            int candidate = 0;
            for (int i = 0; i < desired.Length; i++)
                while (desired[i].Count < 9)
                {
                    while (reserved.Contains(candidate)) candidate++;
                    desired[i].Add(candidate); reserved.Add(candidate++);
                }
            var before = Enumerable.Range(0, desired.Length)
                .Select(i => desired[i].Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray())
                .ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++)
                for (int i = 0; i < before.Length; i++) draws.Add(before[i][round]);
            draws.AddRange(top);
            draws.AddRange(Enumerable.Range(0, 52).Where(id => !reserved.Contains(id)));
            var game = new GameManager(1, "Creador");
            for (int id = 2; id <= partial.Length; id++) Accept(game.AddPlayer(id, "Jugador " + id), "Agregar fixture.");
            Accept(game.StartMatch(new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId))), "Repartir fixture.");
            foreach (Player player in game.State!.Players)
                Accept(game.SelectExchangeCard(player.Id, player.Hand.Cards[^1].Id), "Intercambiar fixture.");
            for (int i = 0; i < desired.Length; i++)
                Check(game.State.GetPlayer(i + 1).Hand.Cards.Select(c => c.Id).ToHashSet()
                    .SetEquals(desired[i]), "Intercambio simultaneo del fixture.");
            if (ready) foreach (Player player in game.State.Players)
                Accept(game.FinishInitialDeclarations(player.Id), "Listo fixture.");
            Integrity(game);
            return game;
        }
    }

    private static MeldPlay? FindUse(GameState state, int id, Card top)
    {
        foreach (Meld meld in state.Melds.Where(m => m.OwnerPlayerId == id))
        {
            Card[] cards = meld.Cards.Append(top).OrderBy(c => (int)c.Rank).ToArray();
            if (RulesValidator.ValidateMeld(cards).IsValid)
                return new MeldPlay(cards.Select(c => c.Id), meld.Id);
        }
        var hand = state.GetPlayer(id).Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
            for (int j = i + 1; j < hand.Count; j++)
            {
                Card[] cards = new[] { hand[i], hand[j], top }.OrderBy(c => (int)c.Rank).ToArray();
                if (RulesValidator.ValidateMeld(cards).IsValid)
                    return new MeldPlay(cards.Select(c => c.Id));
                cards = cards.OrderBy(c => c.Rank == Rank.Ace ? 14 : (int)c.Rank).ToArray();
                if (RulesValidator.ValidateMeld(cards).IsValid)
                    return new MeldPlay(cards.Select(c => c.Id));
            }
        return null;
    }

    private static MeldPlay Play(params int[] ids) => new(ids);
    private static int C(int rank) => rank - 1;
    private static int D(int rank) => 13 + rank - 1;
    private static int H(int rank) => 26 + rank - 1;
    private static int S(int rank) => 39 + rank - 1;
}
