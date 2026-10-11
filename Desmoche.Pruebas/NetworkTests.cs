using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

public static class NetworkTests
{
    private static int _checks;
    public static void Run()
    {
        _checks = 0;
        ProtocolValidation();
        AdmissionAndPrivacy();
        RequestsAndWindows();
        Disconnections();
        ClaimsAndVictories();
        Console.WriteLine($"PRUEBAS DE RED SIN GODOT COMPLETADAS: {_checks} comprobaciones correctas.");
    }

    private static void ProtocolValidation()
    {
        Check(NetworkProtocol.ValidName("Jugador") && NetworkProtocol.ValidName(new string('a', 24)),
            "Acepta nombres dentro del limite");
        foreach (string? bad in new[] { null, "", "  ", "\nJugador", "Jugador\t", new string('a', 25) })
            Check(!NetworkProtocol.ValidName(bad), "Rechaza nombre invalido");

        var valid = new CommandDto(NetworkProtocol.Version, new string('a', 32), 1, 1, ActionKind.Pass);
        Check(NetworkProtocol.ValidCommand(valid), "Comando minimo valido");
        Check(NetworkProtocol.TryDecode<CommandDto>(NetworkProtocol.Encode(valid), 8192, out var decoded)
            && decoded == valid, "Serializacion de comando conserva sus campos");
        foreach (string? bad in new[] { null, "", "null", "[]", "{", "{\"ActorId\":1}", "{\"Version\":1,\"Version\":1}",
            "{\"Plays\":[{\"Cards\":[1,2,3],\"Cards\":[4,5,6]}]}", new string('x', 8193),
            "{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":1}}}}}}}}}" })
            Check(!NetworkProtocol.TryDecode<CommandDto>(bad, 8192, out _), "JSON peligroso o incorrecto rechazado");
        string utf8 = NetworkProtocol.Encode(valid).Replace(new string('a', 32), new string('\u6f22', 32));
        // Encoder puede escapar caracteres; comprobar directamente texto Unicode de entrada.
        utf8 = "{\"Session\":\"" + new string('\u6f22', 100) + "\"}";
        Check(utf8.Length < 200 && !NetworkProtocol.TryDecode<CommandDto>(utf8, 200, out _),
            "El limite se aplica a bytes UTF-8, no solo caracteres");
        foreach (CommandDto bad in new[]
        {
            valid with { Version = NetworkProtocol.Version + 1 }, valid with { Session = null! }, valid with { Session = "corta" },
            valid with { RequestId = 0 }, valid with { RequestId = -1 }, valid with { Window = 0 },
            valid with { Kind = (ActionKind)999 }, valid with { CardId = 1 },
            valid with { SourceMeldId = 1 }, valid with { DeclareWin = true },
            valid with { Cards = new[] { 1, 2, 3 } }, valid with { Plays = Array.Empty<PlayDto>() },
            valid with { InitialKind = InitialVictoryKind.Color },
            valid with { Kind = ActionKind.ExchangeCard }, valid with { Kind = ActionKind.Pay, CardId = 52 },
            valid with { Kind = ActionKind.Pay, CardId = -1 },
            valid with { Kind = ActionKind.DeclareInitial, InitialKind = (InitialVictoryKind)999 },
            valid with { Kind = ActionKind.DeclareInitial },
            valid with { Kind = ActionKind.DeclareInitial, InitialKind = InitialVictoryKind.Cuarta, Cards = new[] { 1, 1 } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 1, 2 }) } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 1, 2, 52 }) } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 1, 1, 2 }) } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(null!) } },
            valid with { Kind = ActionKind.Claim, Plays = new PlayDto[] { null! } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 1, 2, 3 }, 0) } },
            valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 1, 2, 3 }), new PlayDto(new[] { 3, 4, 5 }) } },
            valid with { Kind = ActionKind.Transfer, CardId = 1, SourceMeldId = 1, Plays = Array.Empty<PlayDto>() },
            valid with { Kind = ActionKind.Claim, InitialKind = InitialVictoryKind.Cuarta, DeclareWin = true, Plays = new[] { new PlayDto(new[] { 1, 2, 3 }) } }
        }) Check(!NetworkProtocol.ValidCommand(bad), "Campos o combinaciones de comando invalidos");

        Check(NetworkProtocol.ValidCommand(valid with { Kind = ActionKind.ExchangeCard, CardId = 51 }), "Id maximo valido");
        Check(NetworkProtocol.ValidCommand(valid with { Kind = ActionKind.DeclareDesmoche }), "Desmoche sin juegos nuevos");
        Check(NetworkProtocol.ValidCommand(valid with { Kind = ActionKind.DeclareInitial, InitialKind = InitialVictoryKind.Color }),
            "Victoria color sin cartas declaradas");
        Check(NetworkProtocol.ValidCommand(valid with { Kind = ActionKind.Claim, Plays = new[] { new PlayDto(new[] { 0, 13, 26 }) } }),
            "Forma estructural valida no presupone autoridad");
    }

    private static void AdmissionAndPrivacy()
    {
        Throws<ArgumentException>(() => new ServerSession(""), "Host sin nombre rechazado");
        for (int count = 2; count <= 4; count++)
        {
            var f = new Fixture(count);
            var lobby = f.View();
            Check(lobby.Players.Length == count && lobby.OwnHand.Length == 0 && lobby.DeckCount == 0,
                "Lobby solo contiene identidades publicas");
            Check(f.View(f.Peers[1]).RecipientId == 2 && f.Peers[1] != 2,
                "Peer de transporte se asigna a identidad de jugador distinta");
            Check(!f.Send(f.Peers[1], ActionKind.StartMatch).Success, "Cliente no inicia la partida");
            Accept(f.Send(1, ActionKind.StartMatch), "Host inicia por el mismo despacho");
            Check(!f.Session.AcceptingPlayers && !f.Session.Join(900, NetworkProtocol.Version, "Tarde", out _), "No admite ingreso tardio");
            var views = f.Peers.Select(f.View).ToArray();
            Check(views.All(v => v.OwnHand.Length == 9 && v.RevealedHands.Length == 0), "Cada cliente recibe nueve cartas propias");
            Check(views.SelectMany(v => v.OwnHand).Distinct().Count() == count * 9, "Manos distintas de todos los destinatarios");
            Check(views.All(v => v.Players.All(p => p.CardCount == 9)), "Solo cantidades de manos rivales");
            Check(views[0].DeckCount + count * 9 == 52, "Cantidad de cartas privadas mas mazo correcta");
            foreach (var view in views)
            {
                using var doc = JsonDocument.Parse(NetworkProtocol.Encode(view));
                var root = doc.RootElement;
                Check(!root.TryGetProperty("Deck", out _) && !root.TryGetProperty("DeckCards", out _) &&
                    !root.TryGetProperty("State", out _), "Snapshot no expone mazo ni estado completo");
                foreach (var player in root.GetProperty("Players").EnumerateArray())
                    Check(!player.TryGetProperty("Hand", out _) && !player.TryGetProperty("Cards", out _),
                        "Jugador publico no contiene cartas");
                Check(NetworkProtocol.TryDecode<SnapshotDto>(NetworkProtocol.Encode(view),
                    NetworkProtocol.MaxSnapshotBytes, out var roundTrip) && roundTrip!.OwnHand.SequenceEqual(view.OwnHand),
                    "Snapshot concreto viaja dentro del limite");
            }
            var ownBefore = f.View().OwnHand.ToArray();
            var mutable = f.View();
            mutable.OwnHand[0] = -100;
            mutable.Players[0] = new PublicPlayerDto(900, "Intruso", 3, 999, false, true);
            Check(f.View().OwnHand.SequenceEqual(ownBefore) && f.View().Players[0].Id == 1,
                "Modificar DTO del cliente no modifica servidor");
            int foreign = f.View().OwnHand[0];
            Check(!f.Send(f.Peers[1], ActionKind.ExchangeCard, card: foreign).Success,
                "Peer no puede actuar sobre carta del host");
            var forged = f.Command(f.Peers[1], ActionKind.ExchangeCard, card: foreign);
            string json = NetworkProtocol.Encode(forged).Insert(1, "\"PlayerId\":1,");
            Check(!f.Raw(f.Peers[1], json).Success, "Suplantacion por PlayerId desconocido rechazada");
            string fakeState = "{\"Version\":1,\"State\":{\"WinnerPlayerId\":2}}";
            Check(!f.Raw(f.Peers[1], fakeState).Success && f.View().WinnerPlayerId is null, "No se aceptan estados enviados por cliente");
            foreach (long peer in f.Peers)
                Accept(f.Send(peer, ActionKind.ExchangeCard, card: f.View(peer).OwnHand[0]), "Intercambio solo con carta propia");
            Check(f.View().Phase == GamePhase.InitialDeclarations && f.Peers.All(p => f.View(p).OwnHand.Length == 9),
                "Intercambio simultaneo conserva manos");
        }

        var s = new ServerSession("Host");
        foreach (long peer in new long[] { -1, 0, 1, (long)int.MaxValue + 1 })
            Check(!s.Join(peer, NetworkProtocol.Version, "Invalido", out _), "Peer fuera del rango de ENet");
        Check(!s.Join(100, NetworkProtocol.Version + 1, "Version", out _), "Protocolo distinto rechazado");
        Check(!s.Join(100, NetworkProtocol.Version, "\nNombre", out _), "Nombre remoto invalido rechazado");
        Check(s.Join(100, NetworkProtocol.Version, "Dos", out _) && !s.Join(100, NetworkProtocol.Version, "Duplicado", out _), "No admite dos veces un peer");
        Check(s.Join(200, NetworkProtocol.Version, "Tres", out _) && s.Join(300, NetworkProtocol.Version, "Cuatro", out _) && !s.Join(400, NetworkProtocol.Version, "Cinco", out _),
            "Maximo cuatro participantes incluido host");
        Throws<ArgumentException>(() => s.ViewFor(777), "Desconocido no obtiene vista");
        Check(!s.Receive(777, "{}", 0).Success, "Desconocido no ejecuta comandos");
        s.Disconnect(1); s.Disconnect(777);
        Check(s.Peers.Count == 4, "Host y peer desconocido no se eliminan por Disconnect remoto");
    }

    private static void RequestsAndWindows()
    {
        var f = new Fixture(3);
        var start = f.Command(1, ActionKind.StartMatch);
        Accept(f.Raw(1, NetworkProtocol.Encode(start)), "Primer request admitido");
        long revision = f.Session.Revision;
        Check(!f.Raw(1, NetworkProtocol.Encode(start)).Success && f.Session.Revision == revision, "Repeticion no vuelve a repartir");
        var wrongSession = f.Command(f.Peers[1], ActionKind.ExchangeCard, card: f.View(f.Peers[1]).OwnHand[0])
            with { Session = new string('b', 32) };
        Check(!f.Raw(f.Peers[1], NetworkProtocol.Encode(wrongSession)).Success, "Sesion ajena rechazada");
        var stale = f.Command(f.Peers[1], ActionKind.ExchangeCard, card: f.View(f.Peers[1]).OwnHand[0]) with { Window = 1 };
        Check(!f.Raw(f.Peers[1], NetworkProtocol.Encode(stale)).Success, "Ventana antigua rechazada");
        Check(!f.Raw(f.Peers[1], NetworkProtocol.Encode(stale with { Window = f.Session.Window })).Success,
            "Request rechazado por ventana tambien queda consumido");
        f.Exchange(); f.Ready();
        Check(!f.Send(f.Peers[1], ActionKind.DrawDeck).Success, "Solo el jugador del turno puede robar");
        Accept(f.Send(1, ActionKind.DrawDeck), "Oferta del host");
        long window = f.Session.Window;
        var commands = f.Peers.Select(p => (Peer: p, Cmd: f.Command(p, ActionKind.Pass))).ToArray();
        revision = f.Session.Revision;
        Accept(f.Raw(commands[2].Peer, NetworkProtocol.Encode(commands[2].Cmd)), "Respuesta tercera llega primero");
        Check(f.Session.Window == window && f.Session.Revision > revision, "La revision cambia sin cerrar la oportunidad");
        Check(f.View(commands[2].Peer).OwnResponseSubmitted && !f.View(commands[1].Peer).OwnResponseSubmitted,
            "Solo destinatario conoce su propia respuesta pendiente");
        Accept(f.Raw(commands[0].Peer, NetworkProtocol.Encode(commands[0].Cmd)), "Respuesta concurrente con revision anterior");
        Accept(f.Raw(commands[1].Peer, NetworkProtocol.Encode(commands[1].Cmd)), "Ultima respuesta resuelve ventana");
        Check(f.View().Phase == GamePhase.AwaitingDraw && f.View().CurrentPlayerId == 1 && f.Session.Window > window,
            "Primera carta sin uso conserva turno y cambia ventana");
        Accept(f.Send(1, ActionKind.DrawDeck), "Segunda oferta");
        var oldPass = f.Command(f.Peers[1], ActionKind.Pass) with { Window = window };
        Check(!f.Raw(f.Peers[1], NetworkProtocol.Encode(oldPass)).Success && !f.View(f.Peers[1]).OwnResponseSubmitted,
            "Pass retrasado no se aplica a la siguiente carta");
        Accept(f.Session.ExpireClaims(), "Solo servidor puede cerrar respuestas pendientes");
        Check(f.View().Phase == GamePhase.AwaitingDraw, "Expiracion sin juegos publicados avanza");

        var limited = new Fixture(2);
        ReplyDto result = null!;
        for (int i = 1; i <= 30; i++)
            result = limited.Session.Receive(1, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, limited.Session.SessionId, i,
                limited.Session.Window, ActionKind.Pass)), 100);
        Check(!result.Message.Contains("Demasiadas"), "Treinta solicitudes entran al despacho");
        result = limited.Session.Receive(1, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, limited.Session.SessionId, 31,
            limited.Session.Window, ActionKind.Pass)), 100);
        Check(result.Message.Contains("Demasiadas"), "Limita solicitudes por peer y segundo");
        var independent = limited.Session.Receive(limited.Peers[1], NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version,
            limited.Session.SessionId, 1, limited.Session.Window, ActionKind.Pass)), 100);
        Check(!independent.Message.Contains("Demasiadas"), "Limite de un peer no bloquea a otro");
        result = limited.Session.Receive(1, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, limited.Session.SessionId, 31,
            limited.Session.Window, ActionKind.Pass)), 1100);
        Check(!result.Message.Contains("Demasiadas"), "Nueva ventana temporal permite solicitudes");
    }

    private static void Disconnections()
    {
        var lobby = new Fixture(4);
        lobby.Session.Disconnect(lobby.Peers[1]);
        Check(lobby.View().Players.Select(p => p.SeatIndex).SequenceEqual(new[] { 0, 1, 2 }), "Salida del lobby compacta asientos");
        Check(lobby.Session.Join(999, NetworkProtocol.Version, "Nuevo", out _) && lobby.Session.ViewFor(999).RecipientId == 5,
            "Reentrada recibe identidad nueva sin reutilizar jugador");

        var exchange = new Fixture(2);
        Accept(exchange.Send(1, ActionKind.StartMatch), "Inicio para desconexion en intercambio");
        exchange.Session.Disconnect(exchange.Peers[1]);
        Accept(exchange.Send(1, ActionKind.ExchangeCard, card: exchange.View().OwnHand[0]), "Intercambio del conectado");
        Check(exchange.View().Phase == GamePhase.InitialDeclarations && exchange.View().Players[1].CardCount == 9,
            "Ausente conserva mano despues del intercambio automatico");
        Accept(exchange.Send(1, ActionKind.FinishInitial), "Ausente no bloquea declaraciones");
        Check(exchange.View().Phase == GamePhase.AwaitingDraw && !exchange.View().Players[1].Connected &&
            !exchange.View().Players[1].Eliminated, "Desconexion es distinta de sancion");
        Check(!exchange.Raw(exchange.Peers[1], "{}").Success, "Antiguo peer no puede enviar acciones");

        var initial = new Fixture(3);
        initial.StartAndExchange();
        Accept(initial.Send(1, ActionKind.FinishInitial), "Host listo antes de salida");
        initial.Session.Disconnect(initial.Peers[1]);
        Accept(initial.Send(initial.Peers[2], ActionKind.FinishInitial), "Ultimo conectado listo");
        Check(initial.View().Phase == GamePhase.AwaitingDraw, "Desconexion durante declaraciones no bloquea");

        var draw = Rig(new[] { Array.Empty<int>(), Array.Empty<int>() }, new[] { 51, 50 });
        for (int i = 0; i < 2; i++) { Accept(draw.Send(1, ActionKind.DrawDeck), "Sacar sin reclamar"); draw.PassAll(); }
        Check(draw.View().CurrentPlayerId == 2, "Siguiente turno remoto");
        draw.Session.Disconnect(draw.Peers[1]);
        Check(draw.View().CurrentPlayerId == 1 && draw.View().Phase == GamePhase.AwaitingDraw, "Salta turno de desconectado");

        var claims = Rig(new[] { Array.Empty<int>(), new[] { 2, 3 } }, new[] { 4 });
        Accept(claims.Send(1, ActionKind.DrawDeck), "Oferta con reclamo remoto");
        Accept(claims.Send(claims.Peers[1], ActionKind.Claim, plays: new[] { Play(2, 3, 4) }), "Reclamo pendiente remoto");
        claims.Session.Disconnect(claims.Peers[1]);
        Accept(claims.Send(1, ActionKind.Pass), "Conectado termina ventana");
        Check(claims.View().Melds.Length == 0 && claims.View().DiscardTop == 4, "Salida cancela reclamo privado pendiente");

        var payment = Rig(new[] { Array.Empty<int>(), new[] { 2, 3 } }, new[] { 4 });
        Accept(payment.Send(1, ActionKind.DrawDeck), "Oferta para pago remoto");
        Accept(payment.Send(payment.Peers[1], ActionKind.Claim, plays: new[] { Play(2, 3, 4) }), "Remoto reclama");
        Accept(payment.Send(1, ActionKind.Pass), "Resolver prioridad");
        Check(payment.View().PaymentPlayerId == 2 && payment.View().Phase == GamePhase.AwaitingPayment, "Debe pagar el reclamante");
        int before = payment.View().Players.Single(p => p.Id == 2).CardCount;
        payment.Session.Disconnect(payment.Peers[1]);
        Check(payment.View().Phase == GamePhase.AwaitingClaims && payment.View().OfferedCard is not null &&
            payment.View().Players.Single(p => p.Id == 2).CardCount == before - 1 && payment.View().Melds.Length == 1,
            "Salida durante pago conserva juego y paga automaticamente desde mano");
        Accept(payment.Send(1, ActionKind.Pass), "Termina oferta automatica");
        Check(payment.View().Phase == GamePhase.AwaitingDraw && payment.View().CurrentPlayerId == 1,
            "Sesion continua con unico conectado");

        var finished = Rig(new[] { Enumerable.Range(0, 9).ToArray(), Array.Empty<int>() }, Array.Empty<int>(), ready: false);
        Accept(finished.Send(1, ActionKind.DeclareInitial, initial: InitialVictoryKind.Color), "Victoria para finalizar sesion");
        finished.Session.Disconnect(finished.Peers[1]);
        Check(finished.View().Phase == GamePhase.Finished && !finished.View().Players[1].Connected, "Desconexion tras finalizar conserva resultado");
        Accept(finished.Send(1, ActionKind.PrepareNextMatch), "Host prepara siguiente partida");
        Check(finished.View().Phase == GamePhase.Preparing && finished.View().Players.Length == 1 &&
            finished.View().OwnHand.Length == 0 && finished.View().RevealedHands.Length == 0, "Revancha elimina ausentes y limpia cartas reveladas");
        Check(finished.Session.Join(999, NetworkProtocol.Version, "Nuevo", out _) && finished.Session.ViewFor(999).RecipientId == 3,
            "Lobby siguiente admite nuevo jugador");
        Accept(finished.Send(1, ActionKind.StartMatch), "Nueva partida con conectado nuevo");
        Check(finished.View().Players.All(p => p.Connected && !p.Eliminated && p.CardCount == 9), "Nueva partida sin estados antiguos");
    }

    private static void ClaimsAndVictories()
    {
        var priority = Rig(new[] { Array.Empty<int>(), new[] { 2, 3 }, new[] { 5, 6 } }, new[] { 4 });
        Accept(priority.Send(1, ActionKind.DrawDeck), "Carta valida para dos rivales");
        Accept(priority.Send(priority.Peers[2], ActionKind.Claim, plays: new[] { Play(4, 5, 6) }), "Mas lejano envia primero");
        Accept(priority.Send(priority.Peers[1], ActionKind.Claim, plays: new[] { Play(2, 3, 4) }), "Mas cercano envia despues");
        Accept(priority.Send(1, ActionKind.Pass), "Resolver reclamaciones concurrentes");
        Check(priority.View().PaymentPlayerId == 2 && priority.View().Melds.Single().OwnerId == 2,
            "Prioridad por asiento, nunca por orden de llegada");
        var meldDto = priority.View().Melds[0];
        meldDto.Cards[0] = -1;
        Check(priority.View().Melds[0].Cards.All(id => id >= 0), "Modificar cartas publicas recibidas no cambia servidor");

        var bad = Rig(new[] { Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>(), ready: false);
        long revision = bad.Session.Revision;
        ReplyDto penalty = bad.Send(bad.Peers[1], ActionKind.DeclareInitial, initial: InitialVictoryKind.Cuarta);
        Check(!penalty.Success && penalty.Penalized && bad.Session.Revision > revision, "Declaracion fallida tambien produce actualizacion");
        Check(bad.View().Players.Single(p => p.Id == 2).Eliminated && bad.View().RevealedHands.Length == 0,
            "Sancion llega al host sin revelar mano privada");
        Accept(bad.Send(1, ActionKind.FinishInitial), "Sancionado no bloquea inicio");
        Check(!bad.Send(bad.Peers[1], ActionKind.DrawDeck).Success, "Sancionado no puede robar");
        Check(!bad.Send(bad.Peers[1], ActionKind.PrepareNextMatch).Success, "Cliente nunca controla revancha");

        var wrongWin = Rig(new[] { Array.Empty<int>(), new[] { 2, 3 } }, new[] { 4 });
        Accept(wrongWin.Send(1, ActionKind.DrawDeck), "Oferta para falsa victoria");
        Accept(wrongWin.Send(wrongWin.Peers[1], ActionKind.Claim, plays: new[] { Play(2, 3, 4) }, win: true),
            "Solicitud de victoria se registra para resolver prioridad");
        Accept(wrongWin.Send(1, ActionKind.Pass), "Servidor comprueba victoria al resolver");
        Check(wrongWin.View().WinnerPlayerId is null && wrongWin.View().Players.Single(p => p.Id == 2).Eliminated &&
            wrongWin.View().Melds.Length == 1 && wrongWin.View().OfferedCard is not null,
            "Victoria incorrecta conserva juegos, elimina y paga sin adjudicar ganador");

        var winner = Rig(new[] { new[] { 0, 1, 2, 13, 14, 15, 26, 27, 28 }, Array.Empty<int>() }, new[] { 29 });
        Accept(winner.Send(1, ActionKind.DrawDeck), "Decima carta combinable");
        Accept(winner.Send(1, ActionKind.Claim, plays: new[] { Play(0, 1, 2), Play(13, 14, 15), Play(26, 27, 28, 29) }, win: true),
            "Solicitud de desmoche completo");
        Accept(winner.Send(winner.Peers[1], ActionKind.Pass), "Resolver victoria");
        Check(winner.View().Phase == GamePhase.Finished && winner.View().Result == MatchResult.Desmoche &&
            winner.View().WinnerPlayerId == 1 && winner.View().OwnHand.Length == 0 && winner.View().Melds.Sum(m => m.Cards.Length) == 10,
            "Servidor decide diez combinadas y ninguna carta suelta");
        Check(winner.View().RevealedHands.Length == 2 && winner.View(winner.Peers[1]).RevealedHands.Length == 2,
            "Solo al ganar se revelan todas las manos");
        int original = winner.View().RevealedHands.Single(h => h.PlayerId == 2).Cards[0];
        winner.View().RevealedHands.Single(h => h.PlayerId == 2).Cards[0] = -1;
        Check(winner.View().RevealedHands.Single(h => h.PlayerId == 2).Cards[0] == original,
            "Revelacion final tambien es copia defensiva");
        Check(!winner.Send(1, ActionKind.DrawDeck).Success, "No se juega despues de finalizar");

        var noWinner = Rig(new[] { Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>(), ready: false);
        Check(!noWinner.Send(1, ActionKind.DeclareInitial, initial: InitialVictoryKind.Cuarta).Success, "Primera declaracion invalida");
        Check(!noWinner.Send(noWinner.Peers[1], ActionKind.DeclareInitial, initial: InitialVictoryKind.Cuarta).Success, "Segunda declaracion invalida");
        Check(noWinner.View().Phase == GamePhase.Finished && noWinner.View().Result == MatchResult.Draw &&
            noWinner.View().RevealedHands.Length == 0, "Empate sin ganador no revela cartas");
    }

    // Fixture solo de pruebas: el campo privado permite suministrar un mazo conocido
    // sin agregar al protocolo ninguna forma de elegir o leer el orden del servidor.
    private static Fixture Rig(int[][] partial, int[] top, bool ready = true)
    {
        var f = new Fixture(partial.Length);
        var used = new HashSet<int>(partial.SelectMany(h => h).Concat(top));
        Check(used.Count == partial.Sum(h => h.Length) + top.Length, "Fixture sin duplicados");
        var desired = partial.Select(h => h.ToList()).ToArray();
        int candidate = 0;
        foreach (var hand in desired)
            while (hand.Count < 9)
            {
                while (used.Contains(candidate)) candidate++;
                hand.Add(candidate); used.Add(candidate++);
            }
        var before = Enumerable.Range(0, desired.Length).Select(i =>
            desired[i].Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray()).ToArray();
        var draws = new List<int>();
        for (int round = 0; round < 9; round++)
            foreach (var hand in before) draws.Add(hand[round]);
        draws.AddRange(top);
        draws.AddRange(Enumerable.Range(0, 52).Where(id => !used.Contains(id)));
        var field = typeof(ServerSession).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Actualizar fixture: campo del motor cambio.");
        var game = (GameManager)field.GetValue(f.Session)!;
        Check(game.StartMatch(new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId))).Success, "Reparto determinista");
        foreach (long peer in f.Peers)
            Accept(f.Send(peer, ActionKind.ExchangeCard, card: f.View(peer).OwnHand[^1]), "Intercambio determinista por protocolo");
        for (int i = 0; i < desired.Length; i++)
            Check(f.View(f.Peers[i]).OwnHand.ToHashSet().SetEquals(desired[i]), "Mano determinista tras intercambio");
        if (ready) f.Ready();
        return f;
    }

    private sealed class Fixture
    {
        public ServerSession Session { get; } = new("Creador");
        public long[] Peers { get; }
        private readonly Dictionary<long, long> _requests = new();
        private long _clock;
        public Fixture(int count)
        {
            Peers = new[] { 1L }.Concat(Enumerable.Range(1, count - 1).Select(i => 100L + i)).ToArray();
            foreach (long peer in Peers.Skip(1))
                Check(Session.Join(peer, NetworkProtocol.Version, "Jugador " + peer, out _), "Admitir invitado de fixture");
        }
        public SnapshotDto View(long peer = 1) => Session.ViewFor(peer);
        public CommandDto Command(long peer, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, bool win = false)
        {
            _requests.TryGetValue(peer, out long id);
            _requests[peer] = ++id;
            return new CommandDto(NetworkProtocol.Version, Session.SessionId, id, Session.Window, kind, CardId: card,
                InitialKind: initial, DeclareWin: win, Plays: plays);
        }
        public ReplyDto Raw(long peer, string json) => Session.Receive(peer, json, _clock += 1001);
        public ReplyDto Send(long peer, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, bool win = false) =>
            Raw(peer, NetworkProtocol.Encode(Command(peer, kind, card, plays, initial, win)));
        public void Exchange()
        {
            foreach (long peer in Peers)
                Accept(Send(peer, ActionKind.ExchangeCard, card: View(peer).OwnHand[0]), "Seleccion de intercambio");
        }
        public void Ready()
        {
            foreach (long peer in Peers) Accept(Send(peer, ActionKind.FinishInitial), "Termina declaraciones iniciales");
        }
        public void StartAndExchange()
        {
            Accept(Send(1, ActionKind.StartMatch), "Inicio por protocolo"); Exchange();
        }
        public void PassAll()
        {
            foreach (long peer in Peers) Accept(Send(peer, ActionKind.Pass), "Respuesta sin reclamo");
        }
    }
    private static PlayDto Play(params int[] cards) => new(cards);
    private static void Accept(ReplyDto result, string label) => Check(result.Success, label + ": " + result.Message);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FALLO RED: " + label);
        _checks++;
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new Exception("FALLO RED: " + label);
    }
}