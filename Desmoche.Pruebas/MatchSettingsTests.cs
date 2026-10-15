using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Fixture = TimedMatchTests.Fixture;

internal static class MatchSettingsTests
{
    private static int _checks;
    private static readonly MatchSettings Custom = new(12, 45, 8, 30, ColorEnabled: false);
    private static void Check(bool okay, string text)
    { if (!okay) throw new Exception("AJUSTES: " + text); _checks++; }
    private static MatchSettings Disabled(InitialVictoryKind kind) => kind switch
    {
        InitialVictoryKind.LowStraight => Custom with { LowStraightEnabled = false },
        InitialVictoryKind.HighStraight => Custom with { HighStraightEnabled = false },
        InitialVictoryKind.Cuarta => Custom with { CuartaEnabled = false },
        InitialVictoryKind.Color => Custom with { ColorEnabled = false },
        _ => Custom with { PeladillaEnabled = false }
    };

    public static void Run()
    {
        AuthorityAndReplication(); InitialVictories(); Timers();
        Console.WriteLine($"AJUSTES DE PARTIDA: {_checks} comprobaciones correctas.");
    }

    private static void AuthorityAndReplication()
    {
        var session = new ServerSession("Host");
        Check(session.Join(2, NetworkProtocol.Version, "Invitado", out _), "Admite invitado.");
        long id = 0;
        ReplyDto Send(long peer, MatchSettings? settings, ActionKind action = ActionKind.UpdateSettings) =>
            session.Receive(peer, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, session.SessionId,
                ++id, session.Window, action, Settings: settings)), 1000);
        Check(!Send(2, Custom).Success && session.ViewFor(2).Settings == MatchSettings.Default,
            "Un cliente autenticado no cambia las reglas de la sala.");
        foreach (MatchSettings bad in new[] { Custom with { CardChoiceSeconds = 0 }, Custom with { CardChoiceSeconds = 121 },
            Custom with { CardPreparationSeconds = 301 }, Custom with { CardPreparationSeconds = 4 },
            Custom with { PasaSeconds = 31 }, Custom with { PasaSeconds = -1 },
            Custom with { RematchSeconds = 121 }, Custom with { RematchSeconds = 4 } })
            Check(!Send(1, bad).Success && session.ViewFor(1).Settings == MatchSettings.Default, "Valores fuera de rango no mutan la autoridad.");
        Check(!Send(1, null).Success && !Send(1, Custom, ActionKind.StartMatch).Success, "Contrato exige ajustes solo en su intención.");
        Check(Send(1, Custom).Success && session.ViewFor(2).Settings == Custom, "El host aplica y comparte ajustes antes de iniciar.");
        var publicView = StateProjection.Public(session.ViewFor(2));
        Check(NetworkProtocol.TryDecode<PublicSnapshotDto>(NetworkProtocol.Encode(publicView), NetworkProtocol.MaxPublicBytes,
            out var decoded) && decoded!.Settings == Custom && StateProjection.IsValidPublic(decoded), "JSON conserva todos los ajustes.");
        var assembler = new ClientStateAssembler(); assembler.Start(session.SessionId, 2);
        assembler.TryApplyPrivate(StateProjection.Private(session.ViewFor(2)), out _);
        Check(assembler.TryApplyPublic(decoded!, out var view) && view!.Settings == Custom, "Ensamblador conserva configuración pública.");
        Check(!StateProjection.IsValidPublic(publicView with { Settings = Custom with { PasaSeconds = 100 } }), "Réplica rechaza configuración inválida.");
        Check(!NetworkProtocol.Encode(StateProjection.Private(session.ViewFor(2))).Contains("Settings"), "Ajustes no duplican datos privados.");
        Check(Send(1, null, ActionKind.StartMatch).Success, "Inicia con ajustes.");
        Check(!Send(1, MatchSettings.Default).Success && session.ViewFor(1).Settings == Custom, "Partida iniciada bloquea cambios.");
        Check(!session.Join(3, NetworkProtocol.Version - 1, "Antiguo", out _), "Versión antigua rechazada.");
    }

    private static void InitialVictories()
    {
        (InitialVictoryKind Kind, int[] Cards)[] examples =
        {
            (InitialVictoryKind.LowStraight, new[] { 0,1,2,3 }),
            (InitialVictoryKind.HighStraight, new[] { 10,11,12,0 }),
            (InitialVictoryKind.Cuarta, new[] { 5,18,44,31 }),
            (InitialVictoryKind.Color, Enumerable.Range(13,9).ToArray()),
            (InitialVictoryKind.Peladilla, new[] { 1,5,9,15,19,23,29,33,37 })
        };
        foreach (var example in examples)
        {
            foreach (bool enabled in new[] { false, true })
            {
                var settings = enabled ? MatchSettings.Default : Disabled(example.Kind);
                var f = new Fixture(new[] { example.Cards, Array.Empty<int>() }, Array.Empty<int>(), ready: false, settings: settings);
                int[] before = f.View().OwnHand.ToArray();
                var reply = f.TrySend(1, ActionKind.DeclareInitial, initial: example.Kind,
                    cards: example.Kind == InitialVictoryKind.Peladilla ? null : example.Cards);
                Check(enabled ? reply.Success && f.View().WinnerPlayerId == 1 : !reply.Success && !reply.Penalized &&
                    f.View().Phase == GamePhase.InitialDeclarations && f.View().Players.All(p => !p.Eliminated) &&
                    f.View().OwnHand.SequenceEqual(before), example.Kind + " respeta interruptor sin penalizar opción desactivada.");
            }
            if (example.Kind is InitialVictoryKind.Color or InitialVictoryKind.Peladilla) continue;
            var offered = new Fixture(new[] { example.Cards[..^1], Array.Empty<int>() }, new[] { example.Cards[^1] },
                settings: Disabled(example.Kind));
            offered.Send(1, ActionKind.DrawDeck);
            var initial = offered.TrySend(1, ActionKind.Claim, initial: example.Kind, plays: new[] { new PlayDto(example.Cards) });
            Check(!initial.Success && !initial.Penalized && !offered.View().OwnResponseSubmitted,
                "Primera oferta tampoco admite victoria desactivada: " + example.Kind);
            offered.Send(1, ActionKind.Claim, plays: new[] { new PlayDto(example.Cards) }); offered.Send(2, ActionKind.Pass);
            Check(offered.View().Phase == GamePhase.AwaitingPayment && offered.View().Result == MatchResult.None,
                "La misma combinación sigue siendo juego normal válido: " + example.Kind);
        }
        foreach (var example in examples.Take(2))
        {
            var f = new Fixture(new[] { example.Cards.Concat(new[] { 24 }).ToArray(), Array.Empty<int>() }, Array.Empty<int>(),
                ready: false, exchange: false, settings: Disabled(example.Kind));
            Check(!f.TrySend(1, ActionKind.DeclareInitial, initial: example.Kind, cards: example.Cards).Success &&
                f.View().Phase == GamePhase.InitialExchange, "Antes del cambio respeta desactivación: " + example.Kind);
        }
    }

    private static void Timers()
    {
        var f = new Fixture(new[] { new[] { 30,31 }, Array.Empty<int>() }, new[] { 32 }, settings: Custom);
        f.Send(1, ActionKind.DrawDeck);
        Check(f.View().OfferTiming!.RemainingSelectionMilliseconds == 12000, "Elección personalizada 12 s.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(f.View())), "Réplica admite elección superior a 5 s.");
        f.Now += 11999; f.Session.Tick(f.Now);
        f.Send(1, ActionKind.ChooseOffer);
        long reserved = f.Now;
        Check(f.View().OfferTiming!.Reservations.Single().RemainingMilliseconds == 45000, "Preparación 45 s desde elección tardía.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(f.View())), "Réplica admite reserva superior a 20 s.");
        f.Now++; f.Session.Tick(f.Now);
        Check(f.View(2).OwnResponseSubmitted && f.View().Phase == GamePhase.AwaitingClaims, "Vence elección sin cerrar preparación.");
        f.Now = reserved + 44999; f.Session.Tick(f.Now);
        Check(f.View().Melds.Length == 0, "No juega antes del plazo configurado.");
        f.Now++; f.Session.Tick(f.Now);
        Check(f.View().PaymentPlayerId == 1 && f.View().Melds.Single().Cards.Contains(32), "Al vencer juega combinación válida automáticamente.");
        var boundary = new Fixture(new[] { new[] { 30,31 }, Array.Empty<int>() }, new[] { 32 }, settings: Custom);
        boundary.Send(1, ActionKind.DrawDeck); boundary.Now += 12000;
        Check(!boundary.TrySend(1, ActionKind.ChooseOffer).Success && boundary.View().DiscardTop == 32, "Exactamente al vencer rechaza elección.");
        var discard = new Fixture(new[] { Array.Empty<int>(), new[] { 10,11 } }, new[] { 32,12,25 }, settings: Custom);
        for (int i = 0; i < 2; i++)
        { discard.Send(1, ActionKind.DrawDeck); discard.Send(1, ActionKind.Pass); discard.Send(2, ActionKind.Pass); }
        Check(discard.View(2).DiscardTiming!.RemainingMilliseconds == 12000, "Pozo usa el tiempo configurado.");
        discard.Now += 6000; discard.Send(2, ActionKind.ChooseDiscard);
        Check(discard.View(2).OfferTiming!.Reservations.Single().RemainingMilliseconds == 45000, "Elegir pozo inicia preparación personalizada.");
        var pasa = new Fixture(new[] { new[] { 18,31,5 }, Array.Empty<int>() }, Array.Empty<int>(), settings: Custom);
        pasa.Send(1, ActionKind.Publish, plays: new[] { new PlayDto(new[] { 18,31,5 }) });
        Check(pasa.View().OrderChallenge!.RemainingMilliseconds == 8000 && StateProjection.IsValidPublic(StateProjection.Public(pasa.View())), "Pasa y réplica usan 8 s.");
        pasa.Now += 7999; pasa.Session.Tick(pasa.Now);
        Check(pasa.View().Phase == GamePhase.AwaitingOrderChallenge, "Pasa permanece hasta plazo personalizado.");
        pasa.Now++; pasa.Session.Tick(pasa.Now);
        Check(pasa.View().Phase != GamePhase.AwaitingOrderChallenge && pasa.View().Players.All(p => !p.Eliminated), "Vence sin denuncia ni eliminación.");
        var rematch = new Fixture(new[] { new[] { 0,1,2,3 }, Array.Empty<int>() }, Array.Empty<int>(), ready: false, rematch: true, settings: Custom);
        rematch.Send(1, ActionKind.DeclareInitial, initial: InitialVictoryKind.LowStraight, cards: new[] { 0,1,2,3 });
        Check(rematch.View().Rematch!.RemainingMilliseconds == 30000 && StateProjection.IsValidPublic(StateProjection.Public(rematch.View())), "Revancha 30 s replicada.");
        rematch.Send(1, ActionKind.JoinRematch); rematch.Send(2, ActionKind.JoinRematch);
        rematch.Now += 29999; rematch.Session.Tick(rematch.Now);
        Check(rematch.View().Phase == GamePhase.Finished, "Espera plazo personalizado de revancha.");
        rematch.Now++; rematch.Session.Tick(rematch.Now);
        Check(rematch.View().Phase == GamePhase.InitialExchange && rematch.View(2).Settings == Custom, "Revancha conserva ajustes.");
    }
}
