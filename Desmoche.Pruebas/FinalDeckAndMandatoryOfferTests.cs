using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

internal static class FinalDeckAndMandatoryOfferTests
{
    private static int _checks;
    private static int C(int rank) => rank - 1;
    private static int D(int rank) => 13 + rank - 1;
    private static int H(int rank) => 26 + rank - 1;
    private static int S(int rank) => 39 + rank - 1;
    private static PlayDto Play(params int[] ids) => new(ids);
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception("MAZO Y OFERTA OBLIGATORIA: " + message); _checks++; }

    public static void Run()
    {
        _checks = 0;
        Extensions(); Deadlines(); ValidUsesAndPriority(); Payments(); SeveralPlayers(); Absences(); FinalDeck();
        Console.WriteLine($"MAZO FINAL Y OFERTA OBLIGATORIA: {_checks} comprobaciones correctas.");
    }

    private static Fixture Published(int[] group, int offered, int[]? extra = null)
    {
        var f = new Fixture(new[] { Array.Empty<int>(), group.Concat(extra ?? Array.Empty<int>()).ToArray() }, new[] { offered });
        f.Send(2, ActionKind.Publish, plays: new[] { Play(group) });
        f.Send(1, ActionKind.DrawDeck);
        return f;
    }

    private static void Decline(Fixture f)
    {
        foreach (int actor in f.View().Players.Select(p => p.Id))
            if (!f.View(actor).OwnResponseSubmitted) f.Send(actor, ActionKind.Pass);
    }

    private static void Extensions()
    {
        foreach (int suit in Enumerable.Range(0, 4))
            foreach (var example in new[] { (new[] { 2, 3, 4 }, 5), (new[] { 5, 4, 3 }, 2),
                (new[] { 2, 3, 4 }, 1), (new[] { 11, 12, 13 }, 1), (new[] { 12, 13, 1 }, 11) })
            {
                int[] group = example.Item1.Select(rank => suit * 13 + rank - 1).ToArray();
                int offered = suit * 13 + example.Item2 - 1;
                var f = Published(group, offered); Decline(f);
                SnapshotDto pending = f.View();
                Check(pending.Phase == GamePhase.AwaitingOrderChallenge && pending.OrderChallenge is
                    { PlayerId: 2, Reason: PasaReason.UnusedOffer, RemainingMilliseconds: 3000 }, "Cualquier palo/extremo abre tres segundos de Pasa.");
                Check(pending.OrderChallenge!.MeldId == pending.Melds.Single().Id && pending.OfferedCard == offered && pending.DiscardCount == 0,
                    "La denuncia identifica el juego y conserva la oferta pública hasta resolver.");
                Check(StateProjection.IsValidPublic(StateProjection.Public(pending)) && pending.FinalDeck is null,
                    "Denuncia replicable sin revelar el mazo ni manos ajenas.");
                Check(!f.TrySend(2, ActionKind.SayPasa).Success && !f.TrySend(1, ActionKind.DrawDeck).Success,
                    "El infractor no se denuncia a sí mismo y no se avanza el turno durante Pasa.");
                f.Send(1, ActionKind.SayPasa);
                SnapshotDto end = f.View();
                Check(end.Players.Single(p => p.Id == 2).Eliminated && !end.Players.Single(p => p.Id == 1).Eliminated,
                    "Pasa elimina solo al jugador que dejó pasar la carta obligatoria.");
                Check(end.OfferedCard is null && end.DiscardTop == offered && end.Melds.Single().Cards.SequenceEqual(group),
                    "El mismo ofrecimiento va al pozo, sin ampliación automática del recién eliminado.");
                Check(StateProjection.IsValidPublic(StateProjection.Public(end)), "La sanción conserva las 52 cartas.");
            }
        for (int rank = 1; rank <= 13; rank++)
        {
            int[] group = { C(rank), D(rank), S(rank) };
            var f = Published(group, H(rank)); Decline(f);
            Check(f.View().OrderChallenge?.Reason == PasaReason.UnusedOffer, "La cuarta obliga en todos los rangos, también ases y figuras.");
            f.Now += 3000; f.Session.Tick(f.Now);
            Check(f.View().Players.All(p => !p.Eliminated) && f.View().DiscardTop == H(rank) && f.View().Melds.Single().Cards.SequenceEqual(group),
                "Sin denuncia no hay sanción ni modificación del juego.");
        }
        foreach (var example in new[] { (new[] { C(3), C(4), C(5) }, C(7)),
            (new[] { C(3), C(4), C(5) }, D(6)), (new[] { C(12), C(13), C(1) }, C(2)) })
        {
            var f = Published(example.Item1, example.Item2); Decline(f);
            Check(f.View().OrderChallenge is null && f.View().DiscardTop == example.Item2 && f.View().Players.All(p => !p.Eliminated),
                "Huecos, palo diferente y vuelta K-A-2 no obligan.");
        }
        var onlyHand = new Fixture(new[] { Array.Empty<int>(), new[] { C(3), C(4) } }, new[] { C(5) });
        onlyHand.Send(1, ActionKind.DrawDeck); Decline(onlyHand);
        Check(onlyHand.View().OrderChallenge is null && onlyHand.View().Players.All(p => !p.Eliminated),
            "Una posibilidad solo en mano no impone jugar la oferta.");
    }

    private static void Deadlines()
    {
        int[] group = { H(3), H(4), H(5) };
        var f = Published(group, H(6)); long offeredAt = f.Now;
        Check(!f.Session.Tick(offeredAt + 4999) && f.View().Phase == GamePhase.AwaitingClaims, "No elegir aún permite los cinco segundos completos.");
        f.Now = offeredAt + 5000;
        Check(f.Session.Tick(f.Now) && f.View().OrderChallenge is { RemainingMilliseconds: 3000 },
            "El rechazo automático abre Pasa y no lo vence en el mismo Tick.");
        long window = f.Session.Window;
        Check(!f.Session.Tick(f.Now + 2999) && f.View().OrderChallenge!.RemainingMilliseconds == 1,
            "Pasa permanece hasta los 2.999 ms.");
        f.Now += 3000;
        var oldCommand = new CommandDto(NetworkProtocol.Version, f.View().Session, 999, window, ActionKind.SayPasa);
        Check(!f.Session.Receive(1, NetworkProtocol.Encode(oldCommand), f.Now).Success && f.View().OrderChallenge is null &&
            f.View().Players.All(p => !p.Eliminated) && f.View().DiscardTop == H(6), "Pasa a los 3.000 ms ya no sanciona.");
        Check(f.View().Phase == GamePhase.AwaitingDraw && f.View().CurrentPlayerId == 1 && f.View().MustDrawDeck,
            "La primera oferta no usada mantiene la segunda extracción obligatoria.");

        var reserved = Published(group, H(6)); reserved.Send(2, ActionKind.ChooseOffer); reserved.Send(1, ActionKind.Pass);
        long chosenAt = reserved.Now;
        reserved.Session.Tick(chosenAt + 19999);
        Check(reserved.View().Phase == GamePhase.AwaitingClaims && reserved.View().Melds.Single().Cards.Length == 3,
            "Elegir permite preparar durante veinte segundos.");
        reserved.Now = chosenAt + 20000; reserved.Session.Tick(reserved.Now);
        Check(reserved.View().OrderChallenge is null && reserved.View().Melds.Single().Cards.Contains(H(6)) &&
            reserved.View().PaymentPlayerId == 2, "El uso automático válido cumple la obligación y conserva el pago.");
    }

    private static void ValidUsesAndPriority()
    {
        int[] group = { C(3), C(4), C(5) };
        var alternative = Published(group, C(6), new[] { H(6), S(6) });
        alternative.Send(2, ActionKind.Claim, plays: new[] { Play(C(6), H(6), S(6)) });
        alternative.Send(1, ActionKind.Pass);
        Check(alternative.View().OrderChallenge is null && alternative.View().PaymentPlayerId == 2 &&
            alternative.View().Melds.Single(m => m.Cards.Contains(C(3))).Cards.SequenceEqual(group) &&
            alternative.View().Melds.Single(m => m.Cards.Contains(C(6))).Cards.Length == 3,
            "Se permite usar la oferta en otro juego de mano aunque entre en el publicado.");

        var extended = Published(group, C(6)); int id = extended.View().Melds.Single().Id;
        extended.Send(2, ActionKind.Claim, plays: new[] { new PlayDto(group.Append(C(6)).ToArray(), id) }); extended.Send(1, ActionKind.Pass);
        Check(extended.View().OrderChallenge is null && extended.View().Melds.Single().Cards.Length == 4,
            "Ampliar explícitamente el juego evita la denuncia.");

        var another = new Fixture(new[] { new[] { H(6), S(6) }, group }, new[] { C(6) });
        another.Send(2, ActionKind.Publish, plays: new[] { Play(group) }); another.Send(1, ActionKind.DrawDeck);
        another.Send(2, ActionKind.Pass); another.Send(1, ActionKind.Claim, plays: new[] { Play(C(6), H(6), S(6)) });
        Check(another.View().OrderChallenge is null && another.View().Players.All(p => !p.Eliminated) && another.View().PaymentPlayerId == 1,
            "La regla confirmada no denuncia si otro jugador utiliza la oferta.");
    }

    private static void Payments()
    {
        int[] group = { C(3), C(4), C(5) };
        var f = new Fixture(new[] { new[] { C(6), H(8), S(8) }, group }, new[] { D(8) });
        f.Send(2, ActionKind.Publish, plays: new[] { Play(group) }); f.Send(1, ActionKind.DrawDeck);
        f.Send(1, ActionKind.Claim, plays: new[] { Play(D(8), S(8), H(8)) }); f.Send(2, ActionKind.Pass);
        f.Send(1, ActionKind.Pay, card: C(6)); f.Send(2, ActionKind.Pass);
        Check(f.View().OrderChallenge is { PlayerId: 2, Reason: PasaReason.UnusedOffer } && f.View().OfferedCard == C(6),
            "La oferta del pago también impone ampliar un juego rival.");
        f.Send(1, ActionKind.SayPasa);
        Check(f.View().DiscardTop == C(6) && !f.View().MustDrawDeck && f.View().CurrentPlayerId == 1,
            "Una denuncia en pago conserva la rotación sin repetir la apertura.");

        var ownPayment = new Fixture(new[] { group.Concat(new[] { C(6), H(8), S(8) }).ToArray(), Array.Empty<int>() }, new[] { D(8) });
        ownPayment.Send(1, ActionKind.Publish, plays: new[] { Play(group) }); ownPayment.Send(1, ActionKind.DrawDeck);
        ownPayment.Send(1, ActionKind.Claim, plays: new[] { Play(D(8), S(8), H(8)) }); ownPayment.Send(2, ActionKind.Pass);
        ownPayment.Send(1, ActionKind.Pay, card: C(6)); ownPayment.Send(2, ActionKind.Pass);
        Check(ownPayment.View().OrderChallenge is null && ownPayment.View().Players.All(p => !p.Eliminated),
            "El pagador, excluido de su oferta, no incurre en obligación sobre su propio pago.");

        var oldDiscard = new Fixture(new[] { new[] { C(6), H(8), S(8) }, group }, new[] { D(8) });
        oldDiscard.Send(2, ActionKind.Publish, plays: new[] { Play(group) }); oldDiscard.Send(1, ActionKind.DrawDeck);
        oldDiscard.Send(1, ActionKind.Claim, plays: new[] { Play(D(8), S(8), H(8)) }); oldDiscard.Send(2, ActionKind.Pass);
        oldDiscard.Send(1, ActionKind.Pay, card: C(6)); oldDiscard.Send(2, ActionKind.Pass);
        oldDiscard.Now += 3000; oldDiscard.Session.Tick(oldDiscard.Now);
        oldDiscard.Send(2, ActionKind.ChooseDiscard); oldDiscard.Send(2, ActionKind.Pass);
        Check(oldDiscard.View().OrderChallenge is null && oldDiscard.View().DiscardTop == C(6) && oldDiscard.View().Players.All(p => !p.Eliminated),
            "Rechazar el pozo antiguo elegido no genera la denuncia de oferta pública del mazo/pago.");
    }

    private static void SeveralPlayers()
    {
        var f = new Fixture(new[] { new[] { C(4), C(5), C(6) }, new[] { C(8), C(9), C(10) }, Array.Empty<int>() }, new[] { C(7) });
        f.Send(1, ActionKind.Publish, plays: new[] { Play(C(4), C(5), C(6)) });
        f.Send(2, ActionKind.Publish, plays: new[] { Play(C(8), C(9), C(10)) }); f.Send(1, ActionKind.DrawDeck); Decline(f);
        SnapshotDto first = f.View(3); Check(first.OrderChallenge?.PlayerId == 1, "Varios infractores se presentan en orden de asiento.");
        var stale = new CommandDto(NetworkProtocol.Version, first.Session, 999, first.Window, ActionKind.SayPasa);
        f.Send(3, ActionKind.SayPasa);
        SnapshotDto second = f.View(3);
        Check(second.OrderChallenge is { PlayerId: 2, RemainingMilliseconds: 3000 } && second.OrderChallenge.Id != first.OrderChallenge!.Id &&
            second.Window > first.Window && second.OfferedCard == C(7), "Cada infractor tiene una oportunidad y ventana nuevas, conservando la misma oferta.");
        Check(!f.Session.Receive(3, NetworkProtocol.Encode(stale), f.Now).Success && !f.View().Players.Single(p => p.Id == 2).Eliminated,
            "Un Pasa atrasado de la primera oportunidad no elimina al siguiente.");
        f.Now += 2999; Check(!f.Session.Tick(f.Now), "La segunda oportunidad no hereda el vencimiento anterior.");
        f.Now++; f.Session.Tick(f.Now);
        Check(f.View().Players.Single(p => p.Id == 1).Eliminated && !f.View().Players.Single(p => p.Id == 2).Eliminated &&
            f.View().DiscardTop == C(7) && f.View().OrderChallenge is null, "Solo se sanciona al denunciado y luego la carta llega al pozo.");
    }

    private static void Absences()
    {
        var disconnected = Published(new[] { C(3), C(4), C(5) }, C(6)); Decline(disconnected);
        disconnected.Session.Disconnect(2); disconnected.Now += 3000; disconnected.Session.Tick(disconnected.Now);
        Check(disconnected.View().OrderChallenge is null && disconnected.View().DiscardTop == C(6) &&
            !disconnected.View().Players.Single(p => p.Id == 2).Connected, "Desconectar al infractor durante Pasa no bloquea la oferta.");

        var noObserver = Published(new[] { C(3), C(4), C(5) }, C(6)); Decline(noObserver);
        Check(!noObserver.TrySend(2, ActionKind.SayPasa).Success, "El propio infractor nunca puede denunciarse.");
        noObserver.Now += 3000; noObserver.Session.Tick(noObserver.Now);
        Check(noObserver.View().Players.All(p => !p.Eliminated), "El paso de tiempo por sí solo no elimina.");
    }

    private static void FinalDeck()
    {
        for (int count = 2; count <= 4; count++)
        {
            int[][] hands = Enumerable.Range(0, count).Select(suit => Enumerable.Range(suit * 13, 9).ToArray()).ToArray();
            var f = new Fixture(hands, new[] { S(13), H(13), D(13) }, ready: false, rematch: true);
            Check(f.View().FinalDeck is null && StateProjection.Public(f.View()).FinalDeck is null, "La partida activa no revela ninguna carta del mazo.");
            f.Send(1, ActionKind.DeclareInitial, initial: InitialVictoryKind.LowStraight, cards: hands[0].Take(4).ToArray());
            SnapshotDto end = f.View();
            Check(end.Phase == GamePhase.Finished && end.FinalDeck!.SequenceEqual(f.RemainingOrder) && end.FinalDeck!.Length == end.DeckCount,
                "Al terminar, todas las cartas restantes tienen el orden real de extracción, primera arriba.");
            PublicSnapshotDto p = StateProjection.Public(end);
            Check(StateProjection.IsValidPublic(p), "El mazo final conserva réplica y conteos válidos.");
            int first = end.FinalDeck![0]; end.FinalDeck[0] = -1;
            Check(f.View().FinalDeck![0] == first && p.FinalDeck![0] == first, "Vista y proyección copian defensivamente el mazo final.");
            var assembler = new ClientStateAssembler(); assembler.Start(p.Session, 1);
            bool publicPending = !assembler.TryApplyPublic(p, out _);
            bool privateCommitted = assembler.TryApplyPrivate(StateProjection.Private(f.View()), out var combined);
            Check(publicPending && privateCommitted &&
                combined!.FinalDeck!.SequenceEqual(f.RemainingOrder), "El cliente ensambla la revelación con la revisión privada correspondiente.");
            combined!.FinalDeck![0] = -1;
            Check(assembler.View!.FinalDeck![0] == first, "Leer la réplica no permite mutar el orden conservado.");
            foreach (var invalid in new[] { p with { FinalDeck = null }, p with { FinalDeck = new[] { -1 } },
                p with { FinalDeck = p.FinalDeck!.Skip(1).ToArray() }, p with { FinalDeck = Enumerable.Repeat(first, p.DeckCount).ToArray() },
                p with { FinalDeck = p.FinalDeck!.Skip(1).Prepend(p.RevealedHands[0].Cards[0]).ToArray() } })
                Check(!StateProjection.IsValidPublic(invalid), "Se rechazan mazos faltantes, inválidos, duplicados o solapados con manos finales.");
            f.Send(1, ActionKind.JoinRematch); f.Send(2, ActionKind.JoinRematch);
            f.Now += 15000; f.Session.Tick(f.Now);
            Check(f.View().Phase == GamePhase.InitialExchange && f.View().FinalDeck is null,
                "La revancha borra el orden final antes de repartir de nuevo.");
        }
        var draw = new Fixture(new[] { Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>(), ready: false);
        foreach (int actor in new[] { 1, 2 })
        {
            ReplyDto invalid = draw.TrySend(actor, ActionKind.DeclareInitial, initial: InitialVictoryKind.LowStraight, cards: draw.View(actor).OwnHand.Take(3).ToArray());
            Check(!invalid.Success && invalid.Penalized, "La declaración deliberadamente inválida elimina en la prueba de empate.");
        }
        SnapshotDto tied = draw.View();
        Check(tied.Result == MatchResult.Draw && tied.RevealedHands.Length == tied.Players.Length && tied.FinalDeck!.SequenceEqual(draw.RemainingOrder),
            "El empate permite consultar las manos finales junto con el mazo restante.");
        foreach (int player in tied.Players.Select(p => p.Id))
        {
            SnapshotDto own = draw.View(player);
            Check(own.RevealedHands.Single(h => h.PlayerId == player).Cards.SequenceEqual(own.OwnHand),
                "Cada mano final coincide con la vista privada de su propietario.");
            var assembled = new ClientStateAssembler(); assembled.Start(own.Session, player);
            assembled.TryApplyPublic(StateProjection.Public(own), out _);
            Check(assembled.TryApplyPrivate(StateProjection.Private(own), out var final) && final!.RevealedHands.Length == tied.Players.Length,
                "Los clientes aceptan las manos reveladas en empate sin mezclar destinatarios.");
        }
        PublicSnapshotDto tiedPublic = StateProjection.Public(tied);
        foreach (RevealedHandDto[] invalid in new[] { Array.Empty<RevealedHandDto>(), tied.RevealedHands.Take(1).ToArray(),
            new[] { tied.RevealedHands[0], tied.RevealedHands[0] },
            new[] { tied.RevealedHands[0], new RevealedHandDto(2, tied.RevealedHands[0].Cards) } })
            Check(!StateProjection.IsValidPublic(tiedPublic with { RevealedHands = invalid }), "Empate exige revelaciones completas, únicas y coherentes.");
        int saved = tied.RevealedHands[1].Cards[0]; tied.RevealedHands[1].Cards[0] = -1;
        Check(draw.View().RevealedHands[1].Cards[0] == saved && tiedPublic.RevealedHands[1].Cards[0] == saved,
            "Consultar/manipular la copia final no modifica otras vistas ni la proyección.");
        tied = draw.View();
        // Agotar físicamente el mazo sin publicar juegos termina en empate.
        var exhausted = new Fixture(new[] { Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>());
        int safety = 0;
        while (exhausted.View().Phase != GamePhase.Finished && safety++ < 100)
        {
            exhausted.Now += 1000;
            if (exhausted.View().Phase == GamePhase.AwaitingDraw)
                exhausted.Send(exhausted.View().CurrentPlayerId!.Value, ActionKind.DrawDeck);
            else Decline(exhausted);
        }
        Check(exhausted.View().Phase == GamePhase.Finished && exhausted.View().DeckCount == 0 && exhausted.View().FinalDeck is { Length: 0 } &&
            StateProjection.IsValidPublic(StateProjection.Public(exhausted.View())), "El mazo agotado tiene una revelación final vacía y válida.");
        PublicSnapshotDto active = StateProjection.Public(new Fixture(new[] { Array.Empty<int>(), Array.Empty<int>() }, Array.Empty<int>()).View());
        Check(!StateProjection.IsValidPublic(active with { FinalDeck = Array.Empty<int>() }) &&
            !StateProjection.IsValidPublic(active with { FinalDeck = draw.RemainingOrder }), "Cualquier revelación del mazo durante una partida activa se rechaza.");
        Check(!StateProjection.IsValidPublic(active with { RevealedHands = tied.RevealedHands }), "Las manos finales siguen prohibidas antes de Finished.");

        var pending = Published(new[] { C(3), C(4), C(5) }, C(6)); Decline(pending);
        PublicSnapshotDto challenge = StateProjection.Public(pending.View());
        Check(NetworkProtocol.TryDecode<PublicSnapshotDto>(NetworkProtocol.Encode(challenge), NetworkProtocol.MaxPublicBytes, out var received) &&
            received!.OrderChallenge == challenge.OrderChallenge && StateProjection.IsValidPublic(received),
            "El motivo, juego y plazo de Pasa sobreviven al contrato JSON público.");
        var challengeAssembler = new ClientStateAssembler(); challengeAssembler.Start(challenge.Session, 1);
        bool privatePending = !challengeAssembler.TryApplyPrivate(StateProjection.Private(pending.View()), out _);
        Check(privatePending && challengeAssembler.TryApplyPublic(received!, out var assembledChallenge) &&
            assembledChallenge!.OrderChallenge == challenge.OrderChallenge && assembledChallenge.FinalDeck is null,
            "El cliente ensambla Pasa de oferta sin revelar mazo ni mezclar revisiones.");
        foreach (OrderChallengeDto invalid in new[] { challenge.OrderChallenge! with { Reason = (PasaReason)99 },
            challenge.OrderChallenge! with { MeldId = 999 }, challenge.OrderChallenge! with { MeldId = null },
            challenge.OrderChallenge! with { PlayerId = 1 } })
            Check(!StateProjection.IsValidPublic(challenge with { OrderChallenge = invalid }), "El cliente valida el motivo y el juego público de Pasa.");
        Check(!StateProjection.IsValidPublic(challenge with { OfferedCard = C(8) }),
            "Una oferta que no puede ampliar el juego no acredita una denuncia.");
        var tieAssembler = new ClientStateAssembler(); tieAssembler.Start(tied.Session, 1);
        tieAssembler.TryApplyPublic(StateProjection.Public(tied), out _);
        Check(!tieAssembler.TryApplyPrivate(StateProjection.Private(tied) with { OwnHand = new[] { tied.FinalDeck![0] }
            .Concat(tied.OwnHand.Skip(1)).ToArray() }, out _), "El ensamblador rechaza un mazo final solapado con la mano privada en empate.");
        Check(!new ServerSession("Host").Join(2, NetworkProtocol.Version - 1, "Antiguo", out _),
            "Una versión sin el contrato de mazo/Pasa no puede entrar a la sala.");
    }

    private sealed class Fixture
    {
        public ServerSession Session { get; }
        public int[] RemainingOrder { get; }
        public long Now = 1000;
        private readonly Dictionary<int, long> _requests = new();
        public Fixture(int[][] partial, int[] top, bool ready = true, bool rematch = false)
        {
            top = top.Distinct().Except(partial.SelectMany(x => x)).ToArray();
            var used = partial.SelectMany(x => x).Concat(top).ToHashSet();
            Check(used.Count == partial.Sum(x => x.Length) + top.Length, "Reparto de prueba sin duplicados.");
            var desired = partial.Select(x => x.ToList()).ToArray(); int next = 0;
            foreach (var hand in desired) while (hand.Count < 9) { while (used.Contains(next)) next++; hand.Add(next); used.Add(next++); }
            int[][] before = desired.Select((hand, i) => hand.Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray()).ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++) foreach (int[] hand in before) draws.Add(hand[round]);
            RemainingOrder = top.Concat(Enumerable.Range(0, 52).Where(id => !used.Contains(id))).ToArray(); draws.AddRange(RemainingOrder);
            Session = new ServerSession("Uno", () => new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId)), rematch);
            for (int id = 2; id <= partial.Length; id++) Check(Session.Join(id, NetworkProtocol.Version, "Jugador " + id, out _), "Admitir jugador de prueba.");
            Send(1, ActionKind.StartMatch);
            for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.ExchangeCard, card: View(id).OwnHand[^1]);
            if (ready) for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.FinishInitial);
        }
        public SnapshotDto View(int actor = 1) => Session.ViewFor(actor);
        public ReplyDto TrySend(int actor, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, int[]? cards = null)
        {
            long request = _requests.GetValueOrDefault(actor) + 1; _requests[actor] = request;
            return Session.Receive(actor, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, Session.SessionId,
                request, Session.Window, kind, CardId: card, Plays: plays, InitialKind: initial, Cards: cards)), Now);
        }
        public void Send(int actor, ActionKind kind, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? initial = null, int[]? cards = null)
        { ReplyDto reply = TrySend(actor, kind, card, plays, initial, cards); Check(reply.Success && !reply.Penalized, kind + ": " + reply.Message); }
    }
}
