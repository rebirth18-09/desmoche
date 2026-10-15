using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

public static class StateReplicationTests
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    private const string OtherSession = "abcdef0123456789abcdef0123456789";
    private static int _checks;

    public static void Run()
    {
        _checks = 0;
        ProjectionAndPrivacy();
        InputValidation();
        CoherentAssembly();
        CopiesAndLifecycle();
        RealSessionViews();
        Console.WriteLine($"SINCRONIZACION DE ESTADO: {_checks} comprobaciones correctas.");
    }

    private static SnapshotDto Fixture(long revision = 1) =>
        new(NetworkProtocol.Version, Session, revision, 1, 1,
            GamePhase.InitialExchange, MatchResult.None, null, null, null,
            new[] { new PublicPlayerDto(1, "Host", 0, 9, true, false),
                new PublicPlayerDto(2, "Cliente", 1, 9, true, false) },
            Enumerable.Range(0, 9).ToArray(), Array.Empty<MeldDto>(),
            34, 0, null, null, Array.Empty<RevealedHandDto>(), false);

    private static SnapshotDto WithMeld()
    {
        SnapshotDto v = Fixture();
        return v with
        {
            Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 1,
            Players = new[] { v.Players[0] with { CardCount = 6 }, v.Players[1] },
            OwnHand = Enumerable.Range(3, 6).ToArray(),
            Melds = new[] { new MeldDto(1, 1, new[] { 0, 1, 2 }) }
        };
    }

    private static SnapshotDto Won()
    {
        SnapshotDto v = Fixture();
        return v with
        {
            Phase = GamePhase.Finished, Result = MatchResult.InitialVictory, WinnerPlayerId = 1,
            InitialVictory = new InitialVictoryDto(InitialVictoryKind.Color, v.OwnHand.ToArray()),
            RevealedHands = new[] { new RevealedHandDto(1, v.OwnHand.ToArray()),
                new RevealedHandDto(2, Enumerable.Range(26, 9).ToArray()) },
            FinalDeck = Enumerable.Range(0, 52).Except(v.OwnHand).Except(Enumerable.Range(26, 9)).Reverse().ToArray()
        };
    }

    private static void ProjectionAndPrivacy()
    {
        SnapshotDto v = Fixture();
        PublicSnapshotDto p = StateProjection.Public(v);
        PrivateSnapshotDto own = StateProjection.Private(v);
        Check(StateProjection.IsValidPublic(p), "Proyeccion publica valida.");
        Check(StateProjection.IsValidPrivate(own), "Proyeccion privada valida.");
        string json = NetworkProtocol.Encode(p);
        using JsonDocument doc = JsonDocument.Parse(json);
        string[] expected = { "Version", "Session", "Revision", "Window", "Phase", "Result",
            "CurrentPlayerId", "PaymentPlayerId", "WinnerPlayerId", "Players", "Melds",
            "DeckCount", "DiscardCount", "DiscardTop", "OfferedCard", "RevealedHands",
            "ExchangeCount", "OrderChallenge", "OpeningOffer", "InitialVictory", "OfferTiming", "DiscardTiming", "Rematch", "MustDrawDeck", "FinalDeck", "Settings" };
        Check(doc.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x)
            .SequenceEqual(expected.OrderBy(x => x)), "Contrato publico con lista exacta de campos.");
        Check(!json.Contains("OwnHand") && !json.Contains("RecipientId") &&
            !json.Contains("OwnResponseSubmitted"), "El canal publico no incluye datos individuales.");
        Check(p.RevealedHands.Length == 0, "No revela manos durante partida.");
        SnapshotDto changedSecret = v with
        {
            OwnHand = Enumerable.Range(35, 9).ToArray(),
            OwnResponseSubmitted = true,
            RecipientId = 2
        };
        Check(NetworkProtocol.Encode(StateProjection.Public(changedSecret)) == json,
            "Cambiar datos privados manteniendo lo publico no cambia el mensaje publico.");
        string secret = NetworkProtocol.Encode(own);
        using JsonDocument priv = JsonDocument.Parse(secret);
        Check(priv.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x)
            .SequenceEqual(new[] { "Version", "Session", "Revision", "RecipientId", "OwnHand",
                "OwnResponseSubmitted", "OwnSpectating" }.OrderBy(x => x)), "Canal privado limitado a su destinatario.");
        Check(!secret.Contains("DeckCount") && !secret.Contains("Players") &&
            !secret.Contains("Melds"), "El RPC privado no duplica el estado publico.");
        Check(NetworkProtocol.TryDecode<PublicSnapshotDto>(json, 32768, out var roundtrip) &&
            StateProjection.IsValidPublic(roundtrip), "JSON publico conserva el contrato.");
        Check(NetworkProtocol.TryDecode<PrivateSnapshotDto>(secret, 32768, out var back) &&
            StateProjection.IsValidPrivate(back), "JSON privado conserva el contrato.");
        Check(!NetworkProtocol.TryDecode<PublicSnapshotDto>(json[..^1] + ",\"OwnHand\":[0]}", 32768, out _),
            "Deserializacion publica rechaza agregar OwnHand.");
        Check(!NetworkProtocol.TryDecode<PrivateSnapshotDto>(secret[..^1] + ",\"OtherHands\":[]}", 32768, out _),
            "Deserializacion privada rechaza manos ajenas.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(Won())),
            "Al ganar se permite la revelacion final acordada.");
        Check(StateProjection.IsValidPublic(StateProjection.Public(WithMeld())),
            "Los juegos propios publicados son visibles.");
    }

    private static void InputValidation()
    {
        PublicSnapshotDto p = StateProjection.Public(Fixture());
        PublicPlayerDto[] duplicatePlayers = { p.Players[0], p.Players[0] };
        foreach (PublicSnapshotDto bad in new[]
        {
            p with { Version = NetworkProtocol.Version + 1 },
            p with { Session = "incorrecta" }, p with { Session = new string('z', 32) },
            p with { Revision = 0 }, p with { Window = 0 },
            p with { Phase = (GamePhase)999 }, p with { Result = (MatchResult)999 },
            p with { Players = null! }, p with { Players = Array.Empty<PublicPlayerDto>() },
            p with { Players = duplicatePlayers }, p with { Players = new PublicPlayerDto[] { null! } },
            p with { Players = new[] { p.Players[0] with { Name = "\n" }, p.Players[1] } },
            p with { Players = new[] { p.Players[0] with { SeatIndex = 1 }, p.Players[1] } },
            p with { Players = new[] { p.Players[0] with { CardCount = 53 }, p.Players[1] } },
            p with { CurrentPlayerId = 99 }, p with { PaymentPlayerId = 99 },
            p with { WinnerPlayerId = 1 }, p with { Result = MatchResult.Draw },
            p with { Phase = GamePhase.Finished }, p with { DeckCount = 53 },
            p with { DeckCount = -1 }, p with { DiscardCount = 1 },
            p with { DiscardTop = 12 }, p with { OfferedCard = 52 },
            p with { Melds = null! }, p with { Melds = new MeldDto[] { null! } },
            p with { Melds = new[] { new MeldDto(1, 99, new[] { 0, 1, 2 }) } },
            p with { Melds = new[] { new MeldDto(1, 1, new[] { 0, 0, 2 }) } },
            p with { RevealedHands = null! },
            p with { RevealedHands = new[] { new RevealedHandDto(1, Fixture().OwnHand) } },
            p with { DeckCount = 33 }
        })
            Check(!StateProjection.IsValidPublic(bad), "Datos publicos incoherentes se rechazan.");
        PublicSnapshotDto won = StateProjection.Public(Won());
        foreach (PublicSnapshotDto bad in new[]
        {
            won with { WinnerPlayerId = null },
            won with { RevealedHands = Array.Empty<RevealedHandDto>() },
            won with { RevealedHands = new[] { won.RevealedHands[0], won.RevealedHands[0] } },
            won with { RevealedHands = new[] { won.RevealedHands[0],
                new RevealedHandDto(2, won.RevealedHands[0].Cards) } },
            won with { Result = MatchResult.Draw, WinnerPlayerId = null }
        })
            Check(!StateProjection.IsValidPublic(bad), "Revelacion final invalida se rechaza.");

        PrivateSnapshotDto own = StateProjection.Private(Fixture());
        foreach (PrivateSnapshotDto bad in new[]
        {
            own with { Version = NetworkProtocol.Version + 1 }, own with { Session = null! },
            own with { Revision = 0 }, own with { RecipientId = 0 },
            own with { OwnHand = null! }, own with { OwnHand = new[] { 0, 0 } },
            own with { OwnHand = new[] { -1 } }, own with { OwnHand = new[] { 52 } }
        })
            Check(!StateProjection.IsValidPrivate(bad), "Mano individual invalida se rechaza.");
        Check(!StateProjection.IsValidPublic(null) && !StateProjection.IsValidPrivate(null),
            "Mensajes nulos se rechazan sin excepciones.");
    }

    private static void CoherentAssembly()
    {
        var a = new ClientStateAssembler();
        PublicSnapshotDto p = StateProjection.Public(Fixture());
        PrivateSnapshotDto own = StateProjection.Private(Fixture());
        Check(!a.TryApplyPublic(p, out _) && !a.TryApplyPrivate(own, out _) && a.View is null,
            "No acepta mensajes antes de la admision.");
        a.Start(Session, 1);
        Check(!a.TryApplyPublic(p, out _) && a.View is null, "Publico solo no publica una mano.");
        Check(a.TryApplyPrivate(own, out var first) && first!.Revision == 1,
            "Publico seguido de privado produce vista coherente.");
        Check(!a.TryApplyPrivate(own, out _) && !a.TryApplyPublic(p, out _),
            "Repeticiones no disparan eventos ni retroceden.");
        PrivateSnapshotDto own2 = own with { Revision = 2, OwnResponseSubmitted = true };
        Check(!a.TryApplyPrivate(own2, out _) && a.View!.Revision == 1,
            "Privado adelantado no mezcla su estado con el publico anterior.");
        Check(a.TryApplyPublic(p with { Revision = 2 }, out var second) &&
            second!.OwnResponseSubmitted && second.Revision == 2,
            "Privado seguido de publico produce otra vista coherente.");
        Check(!a.TryApplyPublic(p with { Revision = 4 }, out _) &&
            !a.TryApplyPrivate(own with { Revision = 3 }, out _) &&
            a.View!.Revision == 2, "Revisiones intermedias omitidas no se mezclan.");
        Check(!a.TryApplyPublic(p with { Revision = 3 }, out _) &&
            a.TryApplyPrivate(own with { Revision = 4 }, out var fourth) &&
            fourth!.Revision == 4, "Paquete publico antiguo no reemplaza el mas reciente.");
        Check(!a.TryApplyPrivate(own with { Revision = 7 }, out _) &&
            !a.TryApplyPrivate(own with { Revision = 6 }, out _) &&
            !a.TryApplyPublic(p with { Revision = 6 }, out _) &&
            a.TryApplyPublic(p with { Revision = 7 }, out _),
            "Paquete privado antiguo no reemplaza el mas reciente.");
        Check(!a.TryApplyPublic(p with { Revision = 100, Session = OtherSession }, out _) &&
            !a.TryApplyPrivate(own with { Revision = 100, Session = OtherSession }, out _) &&
            !a.TryApplyPrivate(own with { Revision = 100, RecipientId = 2 }, out _) &&
            a.View!.Revision == 7, "Otra sesion o destinatario no envenenan los buffers.");

        a.Start(Session, 1);
        Check(!a.TryApplyPrivate(own with { OwnHand = new[] { 0 } }, out _) &&
            !a.TryApplyPublic(p, out _) && a.View is null,
            "Rechaza mano de longitud distinta de su resumen publico.");
        a.Start(Session, 1);
        SnapshotDto meld = WithMeld();
        Check(!a.TryApplyPublic(StateProjection.Public(meld), out _) &&
            !a.TryApplyPrivate(StateProjection.Private(meld) with { OwnHand = new[] { 0, 4, 5, 6, 7, 8 } }, out _) &&
            a.View is null, "La mano no puede contener una carta ya publicada.");
        a.Start(Session, 1);
        SnapshotDto won = Won();
        Check(!a.TryApplyPublic(StateProjection.Public(won), out _) &&
            !a.TryApplyPrivate(StateProjection.Private(won) with { OwnHand = Enumerable.Range(9, 9).ToArray() }, out _) &&
            a.View is null, "La mano final privada debe coincidir con su revelacion.");
        a.Start(Session, 1);
        Check(!a.TryApplyPrivate(StateProjection.Private(won), out _) &&
            a.TryApplyPublic(StateProjection.Public(won), out var final) &&
            final!.RevealedHands.Length == 2, "Victoria completa permite revelacion coherente.");

        // Muchos cambios privados pueden ocurrir antes de la siguiente replica publica.
        a.Start(Session, 1);
        for (int revision = 1; revision <= 500; revision++)
            Check(!a.TryApplyPrivate(own with { Revision = revision }, out _),
                "El canal privado espera su revision publica.");
        Check(a.TryApplyPublic(p with { Revision = 500 }, out var latest) &&
            latest!.Revision == 500, "Coalescer 500 revisiones conserva solo el estado mas reciente.");
    }

    private static void CopiesAndLifecycle()
    {
        SnapshotDto original = WithMeld();
        PublicSnapshotDto p = StateProjection.Public(original);
        PrivateSnapshotDto own = StateProjection.Private(original);
        original.OwnHand[0] = 50;
        original.Melds[0].Cards[0] = 49;
        original.Players[0] = original.Players[0] with { Name = "Alterado" };
        Check(own.OwnHand[0] == 3 && p.Melds[0].Cards[0] == 0 &&
            p.Players[0].Name == "Host", "Proyecciones copian arrays y cartas anidadas.");

        var a = new ClientStateAssembler();
        a.Start(Session, 1);
        Check(!a.TryApplyPublic(p, out _), "Almacena copia de publico pendiente.");
        p.Melds[0].Cards[0] = 50;
        p.Players[0] = p.Players[0] with { CardCount = 0 };
        Check(a.TryApplyPrivate(own, out var result) && result!.Melds[0].Cards[0] == 0,
            "Mutar el mensaje publico original no altera el buffer.");
        own.OwnHand[0] = 51;
        result!.OwnHand[0] = 51;
        result.Melds[0].Cards[0] = 51;
        SnapshotDto exposed = a.View!;
        exposed.OwnHand[1] = 51;
        exposed.Players[0] = exposed.Players[0] with { Name = "Mutado" };
        Check(a.View!.OwnHand.SequenceEqual(Enumerable.Range(3, 6)) &&
            a.View.Melds[0].Cards[0] == 0 && a.View.Players[0].Name == "Host",
            "Ni datos de entrada ni vistas de salida mutan el estado ensamblado.");
        a.Reset();
        Check(a.View is null && !a.TryApplyPrivate(StateProjection.Private(Fixture()), out _),
            "Desconectar borra vista, sesion y partes pendientes.");
        a.Start(OtherSession, 2);
        Check(!a.TryApplyPublic(StateProjection.Public(Fixture()), out _) &&
            !a.TryApplyPrivate(StateProjection.Private(Fixture()), out _),
            "Una sesion nueva descarta paquetes atrasados de la anterior.");
        SnapshotDto newSession = Fixture() with { Session = OtherSession, RecipientId = 2,
            OwnHand = Enumerable.Range(26, 9).ToArray() };
        Check(!a.TryApplyPrivate(StateProjection.Private(newSession), out _) &&
            a.TryApplyPublic(StateProjection.Public(newSession), out var fresh) &&
            fresh!.RecipientId == 2 && fresh.Revision == 1,
            "Nueva sesion comienza sin heredar revision anterior.");
        Throws<ArgumentException>(() => a.Start("no", 1), "Sesion de inicio debe ser valida.");
        Throws<ArgumentOutOfRangeException>(() => a.Start(Session, 0), "Destinatario debe existir.");

        SnapshotDto won = Won();
        PublicSnapshotDto publicWon = StateProjection.Public(won);
        won.RevealedHands[0].Cards[0] = 51;
        Check(publicWon.RevealedHands[0].Cards[0] == 0,
            "Las manos reveladas tambien se copian de forma defensiva.");
    }

    private static void RealSessionViews()
    {
        for (int players = 2; players <= 4; players++)
        {
            var session = new ServerSession("Host");
            for (int id = 2; id <= players; id++)
                Check(session.Join(id * 100L, NetworkProtocol.Version, "Jugador " + id, out _),
                    "Admision para prueba de sincronizacion.");
            foreach (long peer in session.Peers) VerifyRealView(session.ViewFor(peer));
            var start = new CommandDto(NetworkProtocol.Version, session.SessionId, 1,
                session.Window, ActionKind.StartMatch);
            Check(session.Receive(1, NetworkProtocol.Encode(start), 0).Success, "Reparto real autorizado.");
            string? canonical = null;
            foreach (long peer in session.Peers)
            {
                SnapshotDto view = session.ViewFor(peer);
                VerifyRealView(view);
                string publicJson = NetworkProtocol.Encode(StateProjection.Public(view));
                canonical ??= publicJson;
                Check(publicJson == canonical, "Todos los destinatarios comparten exactamente el mismo publico.");
                Check(view.OwnHand.Length == 9, "Cada RPC individual conserva solo nueve cartas propias.");
            }
        }
    }

    private static void VerifyRealView(SnapshotDto view)
    {
        var a = new ClientStateAssembler();
        a.Start(view.Session, view.RecipientId);
        Check(!a.TryApplyPrivate(StateProjection.Private(view), out _) &&
            a.TryApplyPublic(StateProjection.Public(view), out var combined) &&
            NetworkProtocol.Encode(combined) == NetworkProtocol.Encode(view),
            "Separar y unir la vista real conserva todos sus campos.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SINCRONIZACION: " + message);
        _checks++;
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException(message);
    }
}
