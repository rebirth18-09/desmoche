using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

public static class RuleRevisionTests
{
    private static int _checks;
    private static int C(int rank) => rank - 1;
    private static int D(int rank) => 13 + rank - 1;
    private static int H(int rank) => 26 + rank - 1;
    private static int S(int rank) => 39 + rank - 1;
    private static PlayDto Play(params int[] ids) => new(ids);
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception("REVISION: " + message); _checks++; }

    public static void Run()
    {
        _checks = 0;
        ExchangeAndPrivacy(); InitialRules(); MeldReorganization(); Pasa(); AutomaticWins(); PaymentExclusion(); Codes();
        Console.WriteLine($"REVISION REGLAS Y CODIGOS: {_checks} comprobaciones correctas.");
    }

    private static void ExchangeAndPrivacy()
    {
        for (int count = 2; count <= 4; count++)
        {
            var f = new Fixture(Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray(), exchange: false);
            SnapshotDto[] original = Enumerable.Range(1, count).Select(id => f.Session.ViewFor(id)).ToArray();
            for (int player = 1; player <= count; player++)
            {
                int outgoing = original[player - 1].OwnHand[^1];
                f.Send(player, ActionKind.ExchangeCard, card: outgoing);
                SnapshotDto own = f.Session.ViewFor(player);
                if (player < count)
                {
                    Check(own.OwnHand.Length == 8 && !own.OwnHand.Contains(outgoing), "Carta confirmada desaparece de la mano.");
                    Check(own.ExchangeCount == player && own.Players.Single(p => p.Id == player).CardCount == 8,
                        "Ocho cartas reales y reserva separada.");
                    Check(StateProjection.IsValidPublic(StateProjection.Public(own)), "Replica valida conserva 52 cartas durante cambio.");
                    Check(!f.Session.ViewFor(player + 1).OwnHand.Contains(outgoing), "Reserva no se filtra al siguiente antes de completar.");
                    Check(!f.TrySend(player, ActionKind.ExchangeCard, card: outgoing).Success, "No puede entregar la reserva otra vez.");
                }
            }
            SnapshotDto final = f.Session.ViewFor(1);
            Check(final.Phase == GamePhase.InitialDeclarations && final.ExchangeCount == 0, "El cambio termina sin reservas.");
            Check(final.Players.All(p => p.CardCount == 9), "Nueve cartas para todos al recibir.");
            for (int player = 1; player <= count; player++)
                Check(f.Session.ViewFor(player % count + 1).OwnHand.Contains(original[player - 1].OwnHand[^1]), "Entrega a la derecha.");
        }
    }

    private static void InitialRules()
    {
        int[] low = { C(1), C(2), C(3), C(4) }, high = { S(11), S(12), S(13), S(1) };
        foreach (var item in new[] { (InitialVictoryKind.LowStraight, low), (InitialVictoryKind.HighStraight, high) })
        {
            foreach (bool after in new[] { false, true })
            {
                var f = new Fixture(new[] { new[] { D(8) }.Concat(item.Item2).ToArray(), Array.Empty<int>() }, exchange: after);
                if (!after)
                {
                    int outgoing = f.Session.ViewFor(2).OwnHand[^1];
                    f.Send(2, ActionKind.ExchangeCard, card: outgoing);
                }
                f.Send(1, ActionKind.DeclareInitial, kind: item.Item1, cards: item.Item2);
                SnapshotDto won = f.Session.ViewFor(2);
                Check(won.WinnerPlayerId == 1 && won.Result == MatchResult.InitialVictory, "Cuatro cartas ganan antes/despues del cambio.");
                Check(won.InitialVictory?.Kind == item.Item1 && won.InitialVictory.Cards.SequenceEqual(item.Item2),
                    "La victoria inicial publica las cuatro cartas declaradas en su orden.");
                Check(won.ExchangeCount == 0 && StateProjection.IsValidPublic(StateProjection.Public(won)), "Victoria anticipada devuelve reservas y conserva cartas.");
            }
        }
        var five = new PlayerHand(new[] { C(1), C(2), C(3), C(4), C(5), D(7), H(8), S(9), D(11) }.Select(Card.FromId));
        Check(!RulesValidator.ValidateInitialVictory(five, InitialVictoryKind.LowStraight,
            new[] { C(1), C(2), C(3), C(4), C(5) }.Select(Card.FromId).ToArray()).IsValid, "Cinco cartas ya no declaran A234.");
        int[] red = { H(1), H(3), H(5), H(7), H(9), D(2), D(4), D(6), D(8) };
        foreach (bool highAce in new[] { false, true })
        {
            var f = new Fixture(new[] { red, Array.Empty<int>() });
            int[] order = red.OrderBy(id => highAce && Card.FromId(id).Rank == Rank.Ace ? 14 : (int)Card.FromId(id).Rank).ToArray();
            f.Send(1, ActionKind.DeclareInitial, kind: InitialVictoryKind.Color, cards: order);
            SnapshotDto colorView = f.Session.ViewFor(1);
            Check(colorView.WinnerPlayerId == 1, "Color gana ordenado con As bajo o alto.");
            Check(colorView.InitialVictory?.Kind == InitialVictoryKind.Color && colorView.InitialVictory.Cards.SequenceEqual(order),
                "Color publica las nueve cartas en el orden con el que gano.");
            var publicView = StateProjection.Public(colorView);
            Check(StateProjection.IsValidPublic(publicView), "La evidencia de victoria no duplica cartas en el conteo.");
            Check(!StateProjection.IsValidPublic(publicView with { Phase = GamePhase.InitialDeclarations, Result = MatchResult.None,
                WinnerPlayerId = null, RevealedHands = Array.Empty<RevealedHandDto>() }), "La evidencia no puede filtrarse antes de terminar.");
            Check(!StateProjection.IsValidPublic(publicView with { InitialVictory = new(InitialVictoryKind.Color, new[] { 0 }) }),
                "Evidencia truncada o ajena rechazada.");
            var client = new ClientStateAssembler(); client.Start(colorView.Session, 1);
            client.TryApplyPublic(publicView, out _); client.TryApplyPrivate(StateProjection.Private(colorView), out _);
            Check(client.View?.InitialVictory?.Cards.SequenceEqual(order) == true, "Replica conserva evidencia ordenada.");
            publicView.InitialVictory!.Cards[0] = 0;
            Check(colorView.InitialVictory!.Cards.SequenceEqual(order) && client.View!.InitialVictory!.Cards.SequenceEqual(order),
                "Evidencia publica y ensamblador conservan copias defensivas.");
            f.Send(1, ActionKind.PrepareNextMatch);
            Check(f.Session.ViewFor(1).InitialVictory is null, "La revancha borra la evidencia anterior.");
        }
        var badColor = new Fixture(new[] { red, Array.Empty<int>() });
        Check(badColor.TrySend(1, ActionKind.DeclareInitial, kind: InitialVictoryKind.Color, cards: red).Penalized,
            "Color desordenado no gana y se penaliza la declaracion.");
        int[] noMeld = { C(2), C(6), C(10), D(3), D(7), D(11), H(4), H(8), H(12) };
        var peladilla = new Fixture(new[] { noMeld, Array.Empty<int>() });
        peladilla.Send(1, ActionKind.DeclareInitial, kind: InitialVictoryKind.Peladilla);
        Check(peladilla.Session.ViewFor(1).WinnerPlayerId == 1, "Sin juegos ni proyectos con una carta se gana por Peladilla.");
        Check(peladilla.Session.ViewFor(2).InitialVictory?.Kind == InitialVictoryKind.Peladilla &&
            peladilla.Session.ViewFor(2).InitialVictory!.Cards.ToHashSet().SetEquals(noMeld),
            "Todos ven las nueve cartas que justifican Peladilla.");
        int[][] projects = {
            new[] { C(2), D(2), H(4), S(6), C(8), D(10), H(12), S(13), C(11) },
            new[] { C(2), C(3), C(10), D(4), D(8), D(12), H(5), H(9), H(13) },
            new[] { C(2), C(4), C(10), D(3), D(7), D(11), H(5), H(9), H(13) },
            // Mano de Bot Bruno en la captura: parejas de 9 y Q, ademas de Q-K de picas.
            new[] { S(2), S(13), H(11), H(9), S(12), H(1), C(9), C(12), C(4) }
        };
        foreach (int[] project in projects)
            for (int count = 2; count <= 4; count++)
            {
                int[][] hands = Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray();
                hands[count - 1] = project;
                var attempted = new Fixture(hands);
                ReplyDto reply = attempted.TrySend(count, ActionKind.DeclareInitial, kind: InitialVictoryKind.Peladilla);
                SnapshotDto rejected = attempted.Session.ViewFor(count);
                Check(!reply.Success && reply.Penalized && rejected.Players.Single(p => p.Id == count).Eliminated,
                    "Una pareja o proyecto elimina al declarar Peladilla despues del cambio, con 2–4 jugadores.");
                Check(rejected.WinnerPlayerId != count && rejected.InitialVictory is null && rejected.RevealedHands.Length == 0,
                    "Peladilla falsa no publica victoria ni revela manos rivales.");
                Check(rejected.OwnHand.ToHashSet().SetEquals(project) && StateProjection.IsValidPublic(StateProjection.Public(rejected)),
                    "La sancion conserva la mano, las 52 cartas y una replica publica valida.");
            }
        var hidden = new Fixture(new[] { new[] { C(2), D(5), H(2), S(9), C(7), D(2), H(12), S(5), C(11) }, Array.Empty<int>() });
        ReplyDto falsePeladilla = hidden.TrySend(1, ActionKind.DeclareInitial, kind: InitialVictoryKind.Peladilla);
        Check(!falsePeladilla.Success && falsePeladilla.Penalized && hidden.Session.ViewFor(1).Players.Single(p => p.Id == 1).Eliminated,
            "Tercia oculta por desorden invalida Peladilla y elimina.");
    }

    private static void MeldReorganization()
    {
        int[] source = { D(1), D(2), D(3), D(4) };
        int[] ownHand = source.Concat(new[] { C(1), C(2), D(5), D(12), D(11) }).ToArray();
        for (int count = 2; count <= 4; count++)
        {
            int[][] hands = Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray();
            hands[count - 1] = ownHand;
            var f = new Fixture(hands, new[] { H(1) }, ready: true);
            f.Send(count, ActionKind.Publish, plays: new[] { Play(source) });
            int donor = f.Session.ViewFor(count).Melds.Single().Id;
            f.Send(1, ActionKind.DrawDeck);
            PlayDto[] plays = { new(new[] { D(2), D(3), D(4) }, donor), Play(D(1), C(1), H(1)) };
            f.Send(count, ActionKind.Claim, plays: plays);
            SnapshotDto pending = f.Session.ViewFor(count);
            Check(pending.Melds.Single().Cards.SequenceEqual(source) && pending.OwnHand.Length == 5,
                "El reclamo no traslada cartas antes de resolver la prioridad.");
            for (int id = 1; id < count; id++) f.Send(id, ActionKind.Pass);
            SnapshotDto taken = f.Session.ViewFor(count);
            Check(taken.Melds.Single(m => m.Id == donor).Cards.SequenceEqual(new[] { D(2), D(3), D(4) }) &&
                taken.Melds.Single(m => m.Id != donor).Cards.SequenceEqual(new[] { D(1), C(1), H(1) }),
                "As del extremo mas As de mano y oferta crea tercia y conserva 2-3-4.");
            Check(taken.Phase == GamePhase.AwaitingPayment && taken.PaymentPlayerId == count && taken.OwnHand.Length == 4,
                "Trasmochar durante reclamo conserva el pago y consume solo una carta propia de la mano.");
            Check(taken.Players.All(p => !p.Eliminated) && StateProjection.IsValidPublic(StateProjection.Public(taken)),
                "Reorganizacion valida no penaliza ni duplica identidades en las 52 cartas.");
            f.Send(count, ActionKind.Pay, card: D(5));
            Check(f.Session.ViewFor(count).OfferedCard == D(5), "El reclamo reorganizado permite pagar normalmente.");
        }

        var invalid = new Fixture(new[] { Array.Empty<int>(), ownHand }, new[] { H(1) }, ready: true);
        invalid.Send(2, ActionKind.Publish, plays: new[] { Play(source) });
        int sourceId = invalid.Session.ViewFor(2).Melds.Single().Id;
        invalid.Send(1, ActionKind.DrawDeck);
        SnapshotDto original = invalid.Session.ViewFor(2);
        string before = NetworkProtocol.Encode(original with { OfferTiming = null });
        foreach (PlayDto[] bad in new[] {
            new[] { new PlayDto(new[] { D(1), D(3), D(4) }, sourceId), Play(D(2), C(2), H(1)) },
            new[] { Play(D(1), C(1), H(1)) },
            new[] { new PlayDto(source, sourceId), Play(D(1), C(1), H(1)) },
            new[] { new PlayDto(new[] { D(2), D(3), D(4) }, sourceId) } })
        {
            ReplyDto reply = invalid.TrySend(2, ActionKind.Claim, plays: bad);
            SnapshotDto unchanged = invalid.Session.ViewFor(2);
            Check(!reply.Success && !reply.Penalized && NetworkProtocol.Encode(unchanged with {
                Revision = original.Revision, OfferTiming = null }) == before &&
                unchanged.OfferTiming!.CardId == original.OfferTiming!.CardId &&
                unchanged.OfferTiming.Reservations.Length == original.OfferTiming.Reservations.Length &&
                unchanged.OfferTiming.RemainingSelectionMilliseconds <= original.OfferTiming.RemainingSelectionMilliseconds,
                "Origen roto, donante omitido, carta duplicada u oferta ausente se rechaza atomicamente.");
        }
        Check(!invalid.TrySend(1, ActionKind.Claim, plays: new[] {
            new PlayDto(new[] { D(2), D(3), D(4) }, sourceId), Play(D(1), C(1), H(1)) }).Success,
            "Otro jugador no puede apropiarse de un juego ajeno para reclamar.");

        foreach (bool straight in new[] { false, true })
        {
            int[] origin = straight ? new[] { C(4), H(4), S(4), D(4) } : source;
            int[] extra = straight ? new[] { D(5) } : new[] { C(1), S(1) };
            int offered = straight ? D(6) : H(1);
            int[] destination = straight ? new[] { D(4), D(5), D(6) } : new[] { D(1), C(1), H(1), S(1) };
            int[] retained = straight ? origin.Take(3).ToArray() : origin.Skip(1).ToArray();
            var claim = new Fixture(new[] { Array.Empty<int>(), origin.Concat(extra).ToArray() }, new[] { offered }, ready: true);
            claim.Send(2, ActionKind.Publish, plays: new[] { Play(origin) });
            int originId = claim.Session.ViewFor(2).Melds.Single().Id;
            claim.Send(1, ActionKind.DrawDeck);
            claim.Send(2, ActionKind.Claim, plays: new[] {
                new PlayDto(retained, originId), Play(destination) });
            claim.Send(1, ActionKind.Pass);
            Check(claim.Session.ViewFor(2).Melds.Single(m => m.Id != originId).Cards.SequenceEqual(destination),
                "La oferta tambien completa una cuarta o una escalera con una carta trasmochada.");
        }

        var repair = new Fixture(new[] { Array.Empty<int>(), new[] { D(2), D(3), D(4), D(5), C(2) } }, new[] { H(2) }, ready: true);
        repair.Send(2, ActionKind.Publish, plays: new[] { Play(D(2), D(3), D(4)) });
        int repairId = repair.Session.ViewFor(2).Melds.Single().Id;
        repair.Send(1, ActionKind.DrawDeck);
        Check(!repair.TrySend(2, ActionKind.Claim, plays: new[] {
            new PlayDto(new[] { D(3), D(4), D(5) }, repairId), Play(D(2), C(2), H(2)) }).Success &&
            repair.Session.ViewFor(2).Melds.Single().Cards.SequenceEqual(new[] { D(2), D(3), D(4) }),
            "No se retira de una tercia aunque otra carta de mano pudiera reparar luego el origen.");

        var lost = new Fixture(new[] { Array.Empty<int>(), source.Concat(new[] { C(1), S(1) }).ToArray() }, new[] { H(1) }, ready: true);
        lost.Send(2, ActionKind.Publish, plays: new[] { Play(source) });
        int lostId = lost.Session.ViewFor(2).Melds.Single().Id;
        lost.Send(1, ActionKind.DrawDeck);
        Check(!lost.TrySend(2, ActionKind.Claim, plays: new[] {
            new PlayDto(new[] { D(2), D(3), D(4) }, lostId), Play(C(1), H(1), S(1)) }).Success &&
            lost.Session.ViewFor(2).Melds.Single().Cards.SequenceEqual(source),
            "Una carta publicada retirada no puede desaparecer aunque los grupos resultantes sean validos.");

        int[] aces = { C(1), H(1), S(1) };
        var fourth = new Fixture(new[] { source.Concat(aces).ToArray(), Array.Empty<int>() }, ready: true);
        fourth.Send(1, ActionKind.Publish, plays: new[] { Play(source), Play(aces) });
        MeldDto[] table = fourth.Session.ViewFor(1).Melds;
        fourth.Send(1, ActionKind.Publish, plays: new[] {
            new PlayDto(new[] { D(2), D(3), D(4) }, table[0].Id),
            new PlayDto(aces.Append(D(1)).ToArray(), table[1].Id) });
        Check(fourth.Session.ViewFor(1).Melds.Select(m => m.Cards.Length).SequenceEqual(new[] { 3, 4 }),
            "Traslado a tercia existente completa cuarta sin reducir el origen a menos de tres.");

        int[] set = { C(4), D(4), S(4), H(4) }, run = { H(1), H(2), H(3) };
        var extend = new Fixture(new[] { set.Concat(run).ToArray(), Array.Empty<int>() }, ready: true);
        extend.Send(1, ActionKind.Publish, plays: new[] { Play(set), Play(run) });
        table = extend.Session.ViewFor(1).Melds;
        extend.Send(1, ActionKind.Publish, plays: new[] {
            new PlayDto(new[] { C(4), D(4), S(4) }, table[0].Id),
            new PlayDto(run.Append(H(4)).ToArray(), table[1].Id) });
        Check(extend.Session.ViewFor(1).Melds[1].Cards.SequenceEqual(new[] { H(1), H(2), H(3), H(4) }),
            "Traslado de una cuarta deja tercia y continua una escalera propia.");

        int[] fives = { D(5), C(5), H(5) };
        var win = new Fixture(new[] { Array.Empty<int>(), source.Concat(fives).Concat(new[] { C(1), S(5) }).ToArray() },
            new[] { H(1) }, ready: true);
        win.Send(2, ActionKind.Publish, plays: new[] { Play(source), Play(fives) });
        table = win.Session.ViewFor(2).Melds;
        win.Send(1, ActionKind.DrawDeck);
        win.Send(2, ActionKind.Claim, plays: new[] {
            new PlayDto(new[] { D(2), D(3), D(4) }, table[0].Id), Play(D(1), C(1), H(1)),
            new PlayDto(fives.Append(S(5)).ToArray(), table[1].Id) });
        win.Send(1, ActionKind.Pass);
        SnapshotDto won = win.Session.ViewFor(2);
        Check(won.Result == MatchResult.Desmoche && won.WinnerPlayerId == 2 && won.OwnHand.Length == 0 && won.PaymentPlayerId is null,
            "Reclamo con traslado y diez cartas combinadas gana automaticamente sin pago.");
    }

    private static void Pasa()
    {
        Fixture StartFault()
        {
            var f = new Fixture(new[] { new[] { D(6), H(6), C(6) }, Array.Empty<int>() }, new[] { S(6) }, ready: true);
            f.Send(1, ActionKind.Publish, plays: new[] { Play(D(6), H(6), C(6)) });
            SnapshotDto view = f.Session.ViewFor(2);
            Check(view.Phase == GamePhase.AwaitingOrderChallenge && view.OrderChallenge?.PlayerId == 1,
                "Rojo-rojo-negro abre ventana publica de Pasa.");
            Check(view.Melds[0].Cards.SequenceEqual(new[] { D(6), H(6), C(6) }) && view.OwnHand.Length == 9,
                "Se muestra el orden incorrecto sin revelar la mano ajena.");
            Check(StateProjection.IsValidPublic(StateProjection.Public(view)), "Réplica de Pasa valida.");
            return f;
        }
        var timeout = StartFault();
        long start = timeout.Now;
        Check(!timeout.TrySend(1, ActionKind.SayPasa).Success, "No se puede señalar el juego propio.");
        Check(!timeout.TrySend(2, ActionKind.DrawDeck).Success, "La mesa espera durante los tres segundos.");
        Check(!timeout.Session.Tick(start + 2999), "Antes de tres segundos sigue pendiente.");
        Check(timeout.Session.Tick(start + 3000), "Al cumplirse tres segundos se resuelve sin accion cliente.");
        SnapshotDto corrected = timeout.Session.ViewFor(1);
        Check(corrected.OrderChallenge is null && corrected.Players.All(p => !p.Eliminated) &&
            RulesValidator.HasAlternatingSetOrder(corrected.Melds[0].Cards.Select(Card.FromId).ToArray()), "Sin Pasa: orden automatico sin sancion.");

        var caught = StartFault();
        caught.Now += 2999;
        ReplyDto report = caught.TrySend(2, ActionKind.SayPasa, advance: false);
        SnapshotDto penalized = caught.Session.ViewFor(2);
        Check(report.Success && !report.Penalized && penalized.Players.Single(p => p.Id == 1).Eliminated &&
            !penalized.Players.Single(p => p.Id == 2).Eliminated, "Pasa a los 2999ms elimina solo al infractor.");
        Check(penalized.Melds[0].Cards.Length == 3 && penalized.Players.Single(p => p.Id == 1).CardCount == 6,
            "El infractor conserva juego y mano.");
        caught.Send(2, ActionKind.DrawDeck);
        caught.Send(2, ActionKind.Pass);
        SnapshotDto paid = caught.Session.ViewFor(2);
        Check(paid.Melds[0].Cards.Length == 4 && paid.Players.Single(p => p.Id == 1).CardCount == 5 && paid.WinnerPlayerId is null,
            "El eliminado recibe ampliaciones y paga automaticamente sin poder ganar.");

        var late = StartFault();
        SnapshotDto old = late.Session.ViewFor(2);
        var lateCommand = new CommandDto(NetworkProtocol.Version, old.Session, 999, old.Window, ActionKind.SayPasa);
        Check(!late.Session.Receive(2, NetworkProtocol.Encode(lateCommand), late.Now + 3000).Success &&
            late.Session.ViewFor(1).Players.All(p => !p.Eliminated), "Pasa al limite exacto ya no puede sancionar.");
        var absent = StartFault(); absent.Session.Disconnect(2); absent.Session.Tick(absent.Now + 3000);
        Check(absent.Session.ViewFor(1).OrderChallenge is null, "Desconexion del rival no bloquea el temporizador.");
        var thirdParty = new Fixture(new[] { Array.Empty<int>(), new[] { C(2), C(3) }, new[] { D(6), H(6), S(6) } },
            new[] { C(4) }, ready: true);
        thirdParty.Send(1, ActionKind.DrawDeck);
        thirdParty.Send(2, ActionKind.Claim, plays: new[] { Play(C(2), C(3), C(4)) });
        thirdParty.Send(1, ActionKind.Pass); thirdParty.Send(3, ActionKind.Pass);
        int beforePay = thirdParty.Session.ViewFor(1).Players.Single(p => p.Id == 2).CardCount;
        thirdParty.Send(3, ActionKind.Publish, plays: new[] { Play(D(6), H(6), S(6)) });
        thirdParty.Session.Disconnect(2);
        thirdParty.Session.Tick(thirdParty.Now + 3000);
        Check(thirdParty.Session.ViewFor(1).Phase == GamePhase.AwaitingClaims &&
            thirdParty.Session.ViewFor(1).Players.Single(p => p.Id == 2).CardCount == beforePay - 1,
            "Desconectar a un pagador distinto del infractor durante Pasa no bloquea el pago automático.");

        var initial = new Fixture(new[] { Array.Empty<int>(), new[] { C(6), S(6), D(6), H(6) }, Array.Empty<int>() });
        initial.Send(2, ActionKind.DeclareInitial, kind: InitialVictoryKind.Cuarta, cards: new[] { C(6), S(6), D(6), H(6) });
        initial.Session.Disconnect(3);
        initial.Send(1, ActionKind.SayPasa);
        initial.Send(1, ActionKind.FinishInitial);
        Check(initial.Session.ViewFor(1).Phase == GamePhase.AwaitingDraw,
            "Desconexión durante Pasa inicial no deja declaraciones pendientes de un ausente.");

        var turn = new Fixture(new[] { Array.Empty<int>(), Array.Empty<int>(), new[] { D(6), H(6), S(6) } }, ready: true);
        for (int i = 0; i < 2; i++)
        {
            turn.Send(1, ActionKind.DrawDeck);
            for (int player = 1; player <= 3; player++) turn.Send(player, ActionKind.Pass);
        }
        Check(turn.Session.ViewFor(1).CurrentPlayerId == 2, "Fixture alcanza turno del invitado.");
        turn.Send(3, ActionKind.Publish, plays: new[] { Play(D(6), H(6), S(6)) });
        turn.Session.Disconnect(2); turn.Session.Tick(turn.Now + 3000);
        Check(turn.Session.ViewFor(1).Phase == GamePhase.AwaitingDraw && turn.Session.ViewFor(1).CurrentPlayerId != 2,
            "Desconectar a quien tiene turno durante Pasa salta correctamente al siguiente jugador.");
    }

    private static void AutomaticWins()
    {
        foreach (bool ordered in new[] { true, false })
        {
            int[] hand = { C(6), D(6), H(6), S(2), S(3), S(4), S(7), S(8), S(9) };
            var f = new Fixture(new[] { hand, Array.Empty<int>() }, new[] { S(6) }, ready: true);
            f.Send(1, ActionKind.Publish, plays: new[] { Play(D(6), C(6), H(6)), Play(S(2), S(3), S(4)), Play(S(7), S(8), S(9)) });
            Check(f.Session.ViewFor(1).WinnerPlayerId is null, "Nueve combinadas no ganan.");
            int meld = f.Session.ViewFor(1).Melds[0].Id;
            f.Send(1, ActionKind.DrawDeck);
            int[] fourth = ordered ? new[] { C(6), D(6), S(6), H(6) } : new[] { C(6), D(6), H(6), S(6) };
            f.Send(1, ActionKind.Claim, plays: new[] { new PlayDto(fourth, meld) });
            f.Send(2, ActionKind.Pass);
            if (!ordered)
            {
                Check(f.Session.ViewFor(1).WinnerPlayerId is null, "Diez mal ordenadas esperan Pasa antes de ganar.");
                f.Session.Tick(f.Now + 3000);
            }
            SnapshotDto won = f.Session.ViewFor(1);
            Check(won.WinnerPlayerId == 1 && won.Result == MatchResult.Desmoche && won.OwnHand.Length == 0,
                "Diez combinadas ganan automaticamente sin bandera ni boton Desmoche.");
        }
    }

    private static void PaymentExclusion()
    {
        for (int count = 2; count <= 4; count++)
        {
            int[][] hands = Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray();
            hands[0] = new[] { C(2), C(3), C(6), D(6), H(6) };
            var f = new Fixture(hands, new[] { C(4) }, ready: true);
            Check(f.Session.ViewFor(1).InitialVictory is null, "Ninguna evidencia privada durante la partida.");
            f.Send(1, ActionKind.DrawDeck);
            f.Send(1, ActionKind.Claim, plays: new[] { Play(C(2), C(3), C(4)) });
            for (int id = 2; id <= count; id++) f.Send(id, ActionKind.Pass);
            f.Send(1, ActionKind.Pay, card: H(6));
            Check(f.Session.ViewFor(1).OwnResponseSubmitted && f.Session.ViewFor(1).OfferedCard == H(6),
                "El pagador observa su pago pero no tiene respuesta pendiente.");
            Check(!f.TrySend(1, ActionKind.Claim, plays: new[] { Play(D(6), C(6), H(6)) }).Success,
                "Servidor rechaza retomar su propio pago incluso si formaria un juego valido.");
            Check(!f.TrySend(1, ActionKind.Pass).Success, "El pagador tampoco responde rechazar.");
            for (int id = 2; id <= count; id++) f.Send(id, ActionKind.Pass);
            SnapshotDto next = f.Session.ViewFor(1);
            Check(next.Phase == GamePhase.AwaitingDraw && next.CurrentPlayerId == 2 && next.DiscardTop == H(6),
                "La ventana termina solo con los demas y sigue el jugador de la derecha.");
        }
    }

    private static void Codes()
    {
        string code = RoomCodeProtocol.CreateCode(), nonce = Guid.NewGuid().ToString("N");
        Check(RoomCodeProtocol.IsCode(code) && RoomCodeProtocol.Normalize(" ab7-k2m ") == "AB7K2M", "Codigo corto admite minusculas y guion.");
        Check(RoomCodeProtocol.TryRead(RoomCodeProtocol.Query(code, nonce), out bool query, out string read, out string token, out _) &&
            query && read == code && token == nonce, "Consulta de descubrimiento valida.");
        Check(RoomCodeProtocol.TryRead(RoomCodeProtocol.Answer(code, nonce, 7000), out query, out _, out _, out int port) &&
            !query && port == 7000, "Respuesta de descubrimiento valida.");
        foreach (string invalid in new[] { "", new string('X', 129), $"DESMOCHE|3|R|{code}|{nonce}|0",
            $"DESMOCHE|3|R|{code}|{nonce}|65536", $"DESMOCHE|2|Q|{code}|{nonce}", $"DESMOCHE|3|Q|{code}|invalid" })
            Check(!RoomCodeProtocol.TryRead(invalid, out _, out _, out _, out _), "Paquete invalido/version antigua rechazado.");
    }

    private sealed class Fixture
    {
        public ServerSession Session { get; }
        public long Now = 1000;
        private readonly Dictionary<int, long> _requests = new();
        public Fixture(int[][] partial, int[]? top = null, bool ready = false, bool exchange = true)
        {
            top ??= Array.Empty<int>();
            var used = partial.SelectMany(x => x).Concat(top).ToHashSet();
            Check(used.Count == partial.Sum(x => x.Length) + top.Length, "Fixture sin duplicados.");
            var desired = partial.Select(x => x.ToList()).ToArray();
            int next = 0;
            foreach (var hand in desired) while (hand.Count < 9) { while (used.Contains(next)) next++; hand.Add(next); used.Add(next++); }
            int[][] before = desired.Select((hand, i) => hand.Skip(1).Append(desired[(i + 1) % desired.Length][0]).ToArray()).ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++) foreach (int[] hand in before) draws.Add(hand[round]);
            draws.AddRange(top); draws.AddRange(Enumerable.Range(0, 52).Where(id => !used.Contains(id)));
            Session = new ServerSession("Uno", () => new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId)));
            for (int id = 2; id <= partial.Length; id++) Check(Session.Join(id, NetworkProtocol.Version, "Jugador " + id, out _), "Admitir fixture.");
            Send(1, ActionKind.StartMatch);
            if (!exchange) return;
            for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.ExchangeCard, card: Session.ViewFor(id).OwnHand[^1]);
            if (ready) for (int id = 1; id <= partial.Length; id++) Send(id, ActionKind.FinishInitial);
        }
        public ReplyDto TrySend(int player, ActionKind action, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? kind = null, int[]? cards = null, bool advance = true)
        {
            if (advance) Now += 10;
            SnapshotDto view = Session.ViewFor(player);
            long request = _requests.GetValueOrDefault(player) + 1; _requests[player] = request;
            return Session.Receive(player, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version, view.Session,
                request, view.Window, action, CardId: card, Plays: plays, InitialKind: kind, Cards: cards)), Now);
        }
        public void Send(int player, ActionKind action, int? card = null, PlayDto[]? plays = null,
            InitialVictoryKind? kind = null, int[]? cards = null)
        {
            ReplyDto result = TrySend(player, action, card, plays, kind, cards);
            Check(result.Success && !result.Penalized, action + ": " + result.Message);
        }
    }
}
