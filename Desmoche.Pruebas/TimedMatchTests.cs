using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

internal static class TimedMatchTests
{
    private static int _checks;
    private static int C(int rank) => rank - 1;
    private static int D(int rank) => 13 + rank - 1;
    private static int H(int rank) => 26 + rank - 1;
    private static int S(int rank) => 39 + rank - 1;
    private static PlayDto Play(params int[] ids) => new(ids);
    private static void Check(bool valid, string text)
    { if (!valid) throw new Exception("TEMPORIZADORES: " + text); _checks++; }

    public static void Run()
    {
        ChoiceAndPriority();
        PreparationAndRandomUses();
        DiscardChoice();
        Rematches();
        Contracts();
        Console.WriteLine($"TEMPORIZADORES Y REVANCHA: {_checks} comprobaciones correctas.");
    }

    private static void ChoiceAndPriority()
    {
        var ignored = new Fixture(new[] { new[] { H(5), H(6) }, Array.Empty<int>() }, new[] { H(7) });
        ignored.Send(1, ActionKind.DrawDeck);
        long at = ignored.Now;
        Check(ignored.View().OfferTiming is { RemainingSelectionMilliseconds: 5000 }, "La oferta abre cinco segundos.");
        Check(!ignored.Session.Tick(at + 4999) && ignored.View().OfferedCard == H(7), "Antes del limite no se rechaza.");
        ignored.Now = at + 5000;
        Check(ignored.Session.Tick(ignored.Now) && ignored.View().DiscardTop == H(7) && ignored.View().OfferedCard is null,
            "Quien no elige pasa, aunque tuviera un juego posible.");
        Check(ignored.View().CurrentPlayerId == 1 && ignored.View().DiscardTiming is null,
            "El rechazo de primera oferta conserva la segunda extraccion obligatoria.");
        Check(!ignored.TrySend(1, ActionKind.ChooseOffer).Success, "Elegir fuera de la ventana se rechaza.");

        int[] useless = { C(2), C(4), C(6), C(8), C(10), C(12), H(3), H(7), S(9) };
        var released = new Fixture(new[] { useless, new[] { D(2), D(3) } }, new[] { D(1) });
        released.Send(1, ActionKind.DrawDeck);
        ReplyDto noUse = released.TrySend(1, ActionKind.ChooseOffer);
        Check(noUse.Success && !noUse.Penalized && released.View().OwnResponseSubmitted &&
            released.View().Players.All(p => !p.Eliminated) && released.View().OfferedCard == D(1),
            "Elegir carta inutil la libera sin incorporarla a mano ni sancionar.");
        released.Send(2, ActionKind.ChooseOffer);
        released.Send(2, ActionKind.Claim, plays: new[] { Play(D(1), D(2), D(3)) });
        Check(released.View().PaymentPlayerId == 2, "Otro interesado usa la carta liberada.");

        var priority = new Fixture(new[] { new[] { D(7), S(7) }, new[] { H(5), H(6) }, new[] { H(10), H(11), H(12) } }, new[] { H(7) });
        priority.Send(1, ActionKind.DrawDeck);
        priority.Now += 100;
        priority.Send(1, ActionKind.ChooseOffer);
        priority.Send(2, ActionKind.ChooseOffer);
        long chosen = priority.Now;
        priority.Send(2, ActionKind.Claim, plays: new[] { Play(H(5), H(6), H(7)) });
        Check(priority.View().OfferedCard == H(7) && priority.View().Melds.Length == 0,
            "El primero en confirmar no salta la prioridad de quien esta preparando.");
        priority.Now += 5000;
        priority.Session.Tick(priority.Now);
        Check(priority.View(3).OwnResponseSubmitted && priority.View().OfferTiming!.Reservations.Length == 1,
            "A cinco segundos pasan los indecisos y sigue la reserva del interesado.");
        priority.Send(3, ActionKind.Publish, plays: new[] { Play(H(10), H(11), H(12)) });
        Check(priority.View(3).OwnResponseSubmitted, "Publicar despues de pasar no vuelve a abrir una respuesta sin plazo.");
        priority.Now = chosen + 19999;
        Check(!priority.Session.Tick(priority.Now) && priority.View().Melds.All(m => m.OwnerId == 3),
            "El interesado conserva sus veinte segundos completos.");
        priority.Now++;
        Check(priority.Session.Tick(priority.Now) && priority.View().PaymentPlayerId == 1 &&
            priority.View().Melds.Single(m => m.OwnerId == 1).Cards.Contains(H(7)), "Al vencer arma el juego del interesado con prioridad.");
        Check(priority.View(2).OwnHand.Length == 9 && !priority.View(2).Melds.Any(m => m.OwnerId == 2),
            "El reclamo que pierde prioridad no consume sus cartas.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(priority.View())), "La resolucion conserva 52 cartas y replica valida.");
        priority.Send(1, ActionKind.Pay, card: priority.View().OwnHand[0]);
        Check(priority.View().OwnResponseSubmitted && !priority.TrySend(1, ActionKind.ChooseOffer).Success,
            "El pagador no puede reservar su propia oferta de pago.");

        var boundary = new Fixture(new[] { new[] { H(5), H(6) }, Array.Empty<int>() }, new[] { H(7) });
        boundary.Send(1, ActionKind.DrawDeck);
        boundary.Now += 5000;
        Check(!boundary.TrySend(1, ActionKind.ChooseOffer).Success && boundary.View().OfferedCard is null,
            "Receive expira antes de admitir una eleccion exactamente a cinco segundos.");
    }

    private static void PreparationAndRandomUses()
    {
        var outcomes = new HashSet<string>();
        for (int seed = 0; seed < 12; seed++)
        {
            var f = new Fixture(new[] { new[] { H(5), H(6), H(8), H(9) }, Array.Empty<int>() }, new[] { H(7) }, random: new Random(seed));
            f.Send(1, ActionKind.DrawDeck);
            f.Now += 4900;
            f.Send(1, ActionKind.ChooseOffer);
            long reserved = f.Now;
            Check(!f.TrySend(1, ActionKind.ChooseOffer).Success && f.View().OfferTiming!.Reservations.Single().RemainingMilliseconds == 20000,
                "Repetir eleccion no renueva los veinte segundos.");
            f.Send(2, ActionKind.Pass);
            f.Session.Tick(reserved - 100);
            Check(f.View().OfferTiming!.Reservations.Single().RemainingMilliseconds == 20000, "Retroceder reloj no extiende la reserva.");
            f.Now = reserved + 20000;
            Check(f.Session.Tick(f.Now) && f.View().Phase == GamePhase.AwaitingPayment, "El reloj baja una combinacion al vencer.");
            MeldDto played = f.View().Melds.Single();
            Check(played.Cards.Contains(H(7)) && RulesValidator.ValidateMeld(played.Cards.Select(Card.FromId).ToArray()).IsValid &&
                f.View().Players.All(p => !p.Eliminated), "El juego automatico es valido, usa la carta elegida y no penaliza.");
            outcomes.Add(string.Join(",", played.Cards));
        }
        Check(outcomes.Count > 1, "Distintas semillas del host eligen distintos juegos posibles.");

        int[] source = { D(1), D(2), D(3), D(4) };
        int[] hand = source.Concat(new[] { C(1), S(13), H(12), C(11), C(9) }).ToArray();
        var moved = new Fixture(new[] { hand, Array.Empty<int>() }, new[] { H(1) });
        moved.Send(1, ActionKind.Publish, plays: new[] { Play(source) });
        int sourceId = moved.View().Melds.Single().Id;
        moved.Send(1, ActionKind.DrawDeck);
        moved.Send(1, ActionKind.ChooseOffer);
        moved.Send(2, ActionKind.Pass);
        moved.Now += 20000;
        moved.Session.Tick(moved.Now);
        Check(moved.View().Melds.Single(m => m.Id == sourceId).Cards.SequenceEqual(new[] { D(2), D(3), D(4) }) &&
            moved.View().Melds.Single(m => m.Id != sourceId).Cards.ToHashSet().SetEquals(new[] { D(1), C(1), H(1) }),
            "La busqueda automatica usa trasmoche conservando 2-3-4 para formar los tres Ases.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(moved.View())), "El trasmoche automatico conserva el contrato.");

        var manual = new Fixture(new[] { new[] { H(5), H(6) }, Array.Empty<int>() }, new[] { H(7) });
        manual.Send(1, ActionKind.DrawDeck); manual.Send(1, ActionKind.ChooseOffer);
        manual.Now += 6000;
        manual.Send(1, ActionKind.Claim, plays: new[] { Play(H(5), H(6), H(7)) });
        Check(manual.View().PaymentPlayerId == 1, "Puede confirmar despues de cinco segundos si eligio a tiempo.");
    }

    private static Fixture OldDiscard()
    {
        var f = new Fixture(new[] { Array.Empty<int>(), new[] { C(11), C(12) } }, new[] { H(7), C(13), D(13) });
        f.Send(1, ActionKind.DrawDeck); f.Send(1, ActionKind.Pass); f.Send(2, ActionKind.Pass);
        f.Send(1, ActionKind.DrawDeck); f.Send(1, ActionKind.Pass); f.Send(2, ActionKind.Pass);
        return f;
    }

    private static void DiscardChoice()
    {
        var f = OldDiscard();
        Check(f.View().DiscardTiming is { PlayerId: 2, RemainingMilliseconds: 5000 } && f.View().DiscardTop == C(13),
            "El pozo del turno abre cinco segundos al jugador responsable.");
        f.Now += 4000;
        f.Send(2, ActionKind.ChooseDiscard);
        Check(f.View(2).OfferTiming is { FromDiscard: true } timing && timing.Reservations.Single().PlayerId == 2 &&
            f.View().OwnResponseSubmitted && f.View().DiscardCount == 1,
            "Elegir el pozo reserva veinte segundos y no pide respuesta a jugadores fuera de ese turno.");
        f.Now += 20000;
        f.Session.Tick(f.Now);
        Check(f.View().PaymentPlayerId == 2 && f.View().Melds.Single().Cards.Contains(C(13)), "Vencer veinte segundos tambien arma desde el pozo.");

        var noChoice = OldDiscard();
        noChoice.Now += 5000;
        Check(noChoice.Session.Tick(noChoice.Now) && noChoice.View().OfferedCard == D(13) &&
            noChoice.View().OfferTiming is { RemainingSelectionMilliseconds: 5000, FromDiscard: false },
            "Rechazar el pozo por tiempo continua con la siguiente carta del mazo y una ventana nueva.");

        var emptyDeck = new Fixture(new[] { Array.Empty<int>(), Array.Empty<int>() }, Enumerable.Range(18, 34).ToArray(), exactDeal: true);
        emptyDeck.Send(1, ActionKind.DrawDeck);
        int steps = 0;
        while (emptyDeck.View().Phase != GamePhase.Finished && steps++ < 200)
        {
            emptyDeck.Now += 1000;
            SnapshotDto view = emptyDeck.View();
            if (view.Phase == GamePhase.AwaitingClaims)
                foreach (int id in view.Players.Select(p => p.Id).Where(id => !emptyDeck.View(id).OwnResponseSubmitted)) emptyDeck.Send(id, ActionKind.Pass);
            else if (view.Phase == GamePhase.AwaitingDraw && view.DeckCount > 0) emptyDeck.Send(view.CurrentPlayerId!.Value, ActionKind.DrawDeck);
            else { emptyDeck.Now += 5000; emptyDeck.Session.Tick(emptyDeck.Now); }
        }
        Check(emptyDeck.View().Result == MatchResult.Draw && steps < 200, "Mazo vacio y rechazos del pozo terminan sin ciclo infinito.");
    }

    private static void Rematches()
    {
        for (int count = 2; count <= 4; count++)
        {
            var f = Finished(count);
            long finishedAt = f.Now;
            Check(f.View().Rematch is { RemainingMilliseconds: 15000, Closed: false } r && r.EligiblePlayerIds.Length == count,
                "Al finalizar abre quince segundos con el denominador de la sala.");
            f.Send(1, ActionKind.JoinRematch);
            Check(f.View(2).Rematch!.ReadyPlayerIds.SequenceEqual(new[] { 1 }), "Todos ven 1/N despues de la primera confirmacion.");
            Check(!f.TrySend(1, ActionKind.JoinRematch).Success, "No cuenta dos veces al mismo participante.");
            f.Send(2, ActionKind.JoinRematch);
            Check(f.View().Rematch!.ReadyPlayerIds.Length == 2 && f.View().Phase == GamePhase.Finished, "No reinicia antes del plazo y muestra 2/N.");
            f.Now = finishedAt + 14999;
            Check(!f.Session.Tick(f.Now), "La revancha espera hasta el limite.");
            f.Now++;
            Check(f.Session.Tick(f.Now) && f.View().Phase == GamePhase.InitialExchange && f.View().Players.Length == 2 &&
                f.View().Players.All(p => p.CardCount == 9 && !p.Eliminated) && f.View().DeckCount == 34,
                "Reparte otra partida solo a quienes confirmaron.");
            Check(f.Session.Peers.SequenceEqual(new long[] { 1, 2 }) && f.Session.TakeRemovedPeers().Length == count - 2,
                "Los invitados que no confirmaron salen de la sesion.");
            Check(StateProjection.IsValidPublic(StateProjection.Public(f.View())), "La nueva partida conserva las 52 cartas.");
        }

        var hostOut = Finished(4);
        hostOut.Send(2, ActionKind.JoinRematch); hostOut.Send(3, ActionKind.JoinRematch);
        hostOut.Now += 15000; hostOut.Session.Tick(hostOut.Now);
        SnapshotDto observer = hostOut.View();
        Check(observer.OwnSpectating && observer.OwnHand.Length == 0 && observer.Players.Select(p => p.Id).SequenceEqual(new[] { 2, 3 }),
            "Host que no confirma aloja sin ocupar asiento ni recibir cartas.");
        var assembler = new ClientStateAssembler(); assembler.Start(observer.Session, 1);
        Check(!assembler.TryApplyPublic(StateProjection.Public(observer), out _) &&
            assembler.TryApplyPrivate(StateProjection.Private(observer), out var assembled) && assembled!.OwnSpectating && assembled.OwnHand.Length == 0,
            "Replica del host espectador combina partes sin revelar la mano del primer jugador.");
        Check(!hostOut.TrySend(1, ActionKind.ExchangeCard, card: hostOut.View(2).OwnHand[0]).Success, "Host espectador no juega cartas de otros.");
        hostOut.Session.Disconnect(2);
        Check(!hostOut.View().Players.Single(p => p.Id == 2).Connected, "El primer asiento invitado puede desconectarse sin convertirse en host.");

        var fewer = Finished(3);
        fewer.Send(1, ActionKind.JoinRematch); fewer.Now += 15000; fewer.Session.Tick(fewer.Now);
        Check(fewer.View().Phase == GamePhase.Finished && fewer.View().Rematch is { Closed: true } && fewer.Session.Peers.Count == 1,
            "Con menos de dos no inicia una partida invalida y excluye a quien no confirmo.");
        Check(!fewer.TrySend(1, ActionKind.JoinRematch).Success, "No admite confirmaciones despues del cierre.");

        var leave = Finished(3);
        leave.Send(1, ActionKind.JoinRematch); leave.Send(2, ActionKind.JoinRematch); leave.Send(3, ActionKind.JoinRematch);
        leave.Session.Disconnect(2);
        Check(leave.View().Rematch!.EligiblePlayerIds.Length == 2 && leave.View().Rematch!.ReadyPlayerIds.Length == 2,
            "La desconexion actualiza participantes y confirmaciones sin reiniciar el plazo.");
        leave.Now += 15000; leave.Session.Tick(leave.Now);
        Check(leave.View().Players.Select(p => p.Id).SequenceEqual(new[] { 1, 3 }) && leave.View().Players[1].SeatIndex == 1,
            "La revancha compacta los asientos de quienes siguen conectados.");
    }

    private static Fixture Finished(int count)
    {
        int[][] hands = Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray();
        hands[0] = new[] { C(1), C(2), C(3), C(4) };
        var f = new Fixture(hands, Array.Empty<int>(), ready: false, rematch: true);
        f.Send(1, ActionKind.DeclareInitial, initial: InitialVictoryKind.LowStraight, cards: hands[0]);
        return f;
    }

    private static void Contracts()
    {
        var f = Finished(3); SnapshotDto view = f.View();
        PublicSnapshotDto p = StateProjection.Public(view);
        Check(StateProjection.IsValidPublic(p) && !StateProjection.IsValidPublic(p with { Rematch = p.Rematch! with { ReadyPlayerIds = new[] { 999 } } }),
            "La replica rechaza participantes de revancha desconocidos.");
        Check(!StateProjection.IsValidPublic(p with { Rematch = p.Rematch! with { RemainingMilliseconds = 15001 } }), "Rechaza un plazo de revancha fuera del contrato.");
        string json = NetworkProtocol.Encode(p);
        Check(!json.Contains("OwnHand") && !json.Contains("OwnSpectating") && !json.Contains("DeckOrder"), "Los temporizadores no publican datos privados.");
        p.Rematch!.EligiblePlayerIds[0] = 999;
        Check(view.Rematch!.EligiblePlayerIds.Contains(1), "La proyeccion copia defensivamente las listas de revancha.");
        var offer = new Fixture(new[] { new[] { H(5), H(6) }, Array.Empty<int>() }, new[] { H(7) });
        offer.Send(1, ActionKind.DrawDeck); offer.Send(1, ActionKind.ChooseOffer);
        PublicSnapshotDto timed = StateProjection.Public(offer.View());
        foreach (OfferTimingDto invalid in new[] {
            timed.OfferTiming! with { CardId = 99 },
            timed.OfferTiming! with { RemainingSelectionMilliseconds = 5001 },
            timed.OfferTiming! with { Reservations = new[] { new CardReservationDto(1, 20001) } },
            timed.OfferTiming! with { Reservations = new[] { new CardReservationDto(999, 20000) } },
            timed.OfferTiming! with { Reservations = new[] { new CardReservationDto(1, 20000), new CardReservationDto(1, 20000) } } })
            Check(!StateProjection.IsValidPublic(timed with { OfferTiming = invalid }), "Contrato rechaza una reserva o reloj falsificado.");
        Check(!NetworkProtocol.ValidCommand(new CommandDto(NetworkProtocol.Version, timed.Session, 1, 1,
            ActionKind.ChooseOffer, Cards: new[] { H(7) })), "La eleccion no admite datos de cartas inventados por el cliente.");
    }

    internal sealed class Fixture
    {
        public ServerSession Session { get; }
        public long Now = 1000;
        private readonly Dictionary<int, long> _requests = new();
        public Fixture(int[][] partial, int[] top, bool ready = true, bool rematch = false, Random? random = null, bool exactDeal = false, MatchSettings? settings = null, bool exchange = true)
        {
            if (exactDeal) partial = new[] { Enumerable.Range(0, 9).ToArray(), Enumerable.Range(9, 9).ToArray() };
            var used = partial.SelectMany(x => x).Concat(top).ToHashSet();
            Check(used.Count == partial.Sum(x => x.Length) + top.Length, "Reparto de prueba sin duplicados.");
            var desired = partial.Select(x => x.ToList()).ToArray(); int next = 0;
            foreach (var hand in desired) while (hand.Count < 9) { while (used.Contains(next)) next++; hand.Add(next); used.Add(next++); }
            int[][] before = desired.Select((hand, i) => hand.Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray()).ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++) foreach (int[] hand in before) draws.Add(hand[round]);
            draws.AddRange(top); draws.AddRange(Enumerable.Range(0, 52).Where(id => !used.Contains(id)));
            Session = new ServerSession("Uno", () => new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId)), rematch, random, settings);
            for (int id = 2; id <= partial.Length; id++) Check(Session.Join(id, NetworkProtocol.Version, "Jugador " + id, out _), "Admitir jugador de prueba.");
            Send(1, ActionKind.StartMatch);
            if (exchange) for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.ExchangeCard, card: View(id).OwnHand[^1]);
            if (ready) for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.FinishInitial);
        }
        public SnapshotDto View(int actor = 1) => Session.ViewFor(actor);
        public ReplyDto TrySend(int actor, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, int[]? cards = null, MatchSettings? settings = null)
        {
            long request = _requests.GetValueOrDefault(actor) + 1; _requests[actor] = request;
            return Session.Receive(actor, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, Session.SessionId,
                request, Session.Window, kind, CardId: card, Plays: plays, InitialKind: initial, Cards: cards, Settings: settings)), Now);
        }
        public void Send(int actor, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, int[]? cards = null)
        { ReplyDto reply = TrySend(actor, kind, card, plays, initial, cards); Check(reply.Success && !reply.Penalized, kind + ": " + reply.Message); }
    }
}
