using System.Diagnostics;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Singleplayer.Core;

internal static class Program
{
    private static int _checks;
    private static int _games;
    private static readonly Dictionary<MatchResult, int> Results = new();
    private static readonly HashSet<ActionKind> Actions = new();
    private static readonly HashSet<GamePhase> Phases = new();
    private static readonly List<double> DecisionTimes = new();
    private static readonly List<double> TickTimes = new();

    private static void Check(bool value, string name)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(name);
    }
    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { Check(true, name); return; }
        Check(false, name);
    }
    private static SnapshotDto View(int[] hand, GamePhase phase = GamePhase.AwaitingClaims,
        int? offer = null, MeldDto[]? melds = null, int? discard = null, int deck = 20) => new(
        NetworkProtocol.Version, "11111111111111111111111111111111", 10, 10, 1,
        phase, MatchResult.None, 1, phase == GamePhase.AwaitingPayment ? 1 : null, null,
        new[] { new PublicPlayerDto(1, "Yo", 0, hand.Length, true, false),
            new PublicPlayerDto(2, "Otro", 1, 9, true, false) }, hand,
        melds ?? Array.Empty<MeldDto>(), deck, discard is null ? 0 : 1, discard, offer,
        Array.Empty<RevealedHandDto>(), false);

    public static int Main(string[] args)
    {
        try
        {
            var timer = Stopwatch.StartNew();
            UnitChecks();
            int seedCount = args.Contains("--quick") ? 1 : 12;
            foreach (BotDifficulty difficulty in Enum.GetValues<BotDifficulty>())
            {
                for (int bots = 1; bots <= 3; bots++)
                    for (int seed = 0; seed < seedCount; seed++)
                        Simulate(difficulty, bots, 2718 + seed * 97 + bots * 11, seed == 0);
                Console.WriteLine($"OK: {LocalMatchOptions.DifficultyName(difficulty)}, 2–4 jugadores, {seedCount} semillas por tamaño.");
            }
            Check(Results.GetValueOrDefault(MatchResult.Desmoche) > 0, "Las simulaciones alcanzan desmoche normal.");
            Check(Results.GetValueOrDefault(MatchResult.Draw) > 0, "Las simulaciones agotan el mazo y terminan empatadas.");
            Check(Phases.Contains(GamePhase.AwaitingPayment), "Las simulaciones pagan tras reclamar.");
            Check(Actions.Contains(ActionKind.Claim), "Las simulaciones reclaman ofertas.");
            ReportTiming("Decide", DecisionTimes);
            ReportTiming("Tick completo (decisión bot + validación + vista)", TickTimes);
            Console.WriteLine($"PRUEBAS COMPLETADAS: {_checks} comprobaciones; {_games} partidas completas; " +
                string.Join(", ", Results.OrderBy(r => r.Key).Select(r => $"{r.Key}={r.Value}")) +
                $"; {timer.Elapsed.TotalSeconds:F1} s.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FALLO: {error}");
            return 1;
        }
    }

    private static void UnitChecks()
    {
        foreach (int bots in new[] { 0, 4 })
            Throws<ArgumentOutOfRangeException>(() => new LocalMatchOptions("Ana", bots, BotDifficulty.Easy).Validate(), "Límite de jugadores.");
        Throws<ArgumentException>(() => new LocalMatchOptions("  ", 1, BotDifficulty.Easy).Validate(), "Nombre vacío.");
        Throws<ArgumentException>(() => new LocalMatchOptions(new string('a', 25), 1, BotDifficulty.Easy).Validate(), "Nombre demasiado largo.");
        Throws<ArgumentOutOfRangeException>(() => new LocalMatchOptions("Ana", 1, (BotDifficulty)99).Validate(), "Dificultad inválida.");
        Throws<ArgumentOutOfRangeException>(() => new BotBrain((BotDifficulty)99, 1), "Bot inválido.");

        foreach (BotDifficulty difficulty in Enum.GetValues<BotDifficulty>())
        {
            var brain = new BotBrain(difficulty, 123);
            // Pares de seis, oferta seis de corazones: solo se toman con un juego válido.
            SnapshotDto claims = View(new[] { 5, 18, 1, 8 }, offer: 31);
            BotAction action = brain.Decide(claims)!;
            Check(action.Kind == ActionKind.Claim, "Tercia con oferta.");
            Check(action.Plays!.SelectMany(p => p.Cards).Contains(31), "La oferta forma parte de los juegos.");
            Check(action.Plays!.All(p => RulesValidator.ValidateMeld(p.Cards.Select(Card.FromId).ToArray()).IsValid), "El bot usa el validador real.");
            Check(!action.DeclareWin, "Cuatro cartas no son victoria.");
            Check(brain.Decide(claims with { OwnResponseSubmitted = true }) is null, "No responder dos veces.");
            Check(brain.Decide(claims with { Phase = GamePhase.Finished }) is null, "No jugar al terminar.");
            Check(brain.Decide(claims with { Players = claims.Players.Select(p => p.Id == 1 ? p with { Eliminated = true } : p).ToArray() }) is null, "El eliminado no decide.");
            Check(brain.Decide(View(new[] { 1, 18 }, offer: 38))!.Kind == ActionKind.Pass, "Oferta inútil se rechaza.");
            Check(brain.Decide(View(new[] { 11, 12 }, offer: 0))!.Plays![0].Cards.SequenceEqual(new[] { 11, 12, 0 }), "Q-K-A ordenado.");
            Check(brain.Decide(View(new[] { 12, 0 }, offer: 1))!.Kind == ActionKind.Pass, "K-A-2 prohibido.");

            SnapshotDto extend = View(new[] { 4, 19 }, offer: 3, melds: new[] { new MeldDto(8, 1, new[] { 0, 1, 2 }) });
            action = brain.Decide(extend)!;
            Check(action.Kind == ActionKind.Claim && action.Plays!.Any(p => p.MeldId == 8 && p.Cards.Contains(3)), "Ampliar un juego propio.");
            SnapshotDto take = claims with { Phase = GamePhase.AwaitingDraw, OfferedCard = null, DiscardTop = 31, DiscardCount = 1, DeckCount = 0 };
            Check(brain.Decide(take)!.Kind == ActionKind.DrawDiscard, "Pozo utilizable con mazo vacío.");
            Check(brain.Decide(take with { DeckCount = 10 }, new(false, true))!.Kind == ActionKind.DrawDeck, "Segunda carta de apertura se saca del mazo.");
            Check(brain.Decide(take with { CurrentPlayerId = 2 }) is null, "No robar fuera de turno.");

            SnapshotDto win = View(new[] { 17, 30, 43, 7, 20 }, offer: 33,
                melds: new[] { new MeldDto(3, 1, new[] { 0, 1, 2, 3 }) });
            action = brain.Decide(win)!;
            Check(action.Kind == ActionKind.Claim && action.DeclareWin &&
                action.Plays!.SelectMany(p => p.Cards).Distinct().Count() == 6, "Victoria exacta de diez con mano cubierta.");
            action = brain.Decide(win with { OwnHand = win.OwnHand.Append(11).ToArray() })!;
            Check(!action.DeclareWin, "Once cartas o carta suelta impiden declarar.");
            action = brain.Decide(View(Array.Empty<int>(), GamePhase.AwaitingPayment,
                melds: new[] { new MeldDto(1, 1, Enumerable.Range(0, 10).ToArray()) }))!;
            Check(action.Kind == ActionKind.DeclareDesmoche, "Desmoche con diez ya publicadas.");
            action = brain.Decide(View(Array.Empty<int>(), GamePhase.AwaitingPayment,
                melds: new[] { new MeldDto(1, 1, Enumerable.Range(0, 9).ToArray()) }))!;
            Check(action.Kind == ActionKind.CompleteWithoutPayment, "Mano vacía sin diez termina turno sin victoria.");
            action = brain.Decide(View(new[] { 1, 7 }, GamePhase.AwaitingPayment))!;
            Check(action.Kind == ActionKind.Pay && new[] { 1, 7 }.Contains(action.CardId!.Value), "Pago pertenece a su mano.");

            foreach ((InitialVictoryKind kind, int[] cards) in new[] {
                (InitialVictoryKind.Cuarta, new[] { 5, 18, 31, 44, 1, 8, 15, 24, 36 }),
                (InitialVictoryKind.LowStraight, new[] { 0, 1, 2, 3, 4, 19, 25, 36, 47 }),
                (InitialVictoryKind.HighStraight, new[] { 9, 10, 11, 12, 0, 19, 25, 36, 47 }),
                (InitialVictoryKind.Color, new[] { 13, 15, 17, 19, 21, 26, 28, 31, 35 }),
                (InitialVictoryKind.Peladilla, new[] { 0, 4, 8, 14, 18, 22, 28, 32, 36 }) })
            {
                action = brain.Decide(View(cards, GamePhase.InitialDeclarations))!;
                Check(action.Kind == ActionKind.DeclareInitial && action.InitialKind == kind, "Victoria inicial " + kind);
            }
            SnapshotDto initialOffer = View(new[] { 0, 1, 2, 3, 19, 25, 30, 36, 47 }, offer: 4);
            action = brain.Decide(initialOffer, new(true, false))!;
            Check(action.InitialKind == InitialVictoryKind.LowStraight, "Primera oferta permite victoria inicial con cinco cartas.");
            Check(brain.Decide(initialOffer)!.InitialKind is null, "Ofertas siguientes no declaran victoria inicial.");

            // Datos de otra mano revelada no influyen; no hay parámetro para orden del mazo.
            var a = new BotBrain(difficulty, 91);
            var b = new BotBrain(difficulty, 91);
            SnapshotDto paying = View(new[] { 0, 1, 8, 17, 31, 48 }, GamePhase.AwaitingPayment);
            BotAction? first = a.Decide(paying);
            BotAction? second = b.Decide(paying with { RevealedHands = new[] { new RevealedHandDto(2, new[] { 3, 4, 5 }) } });
            Check(NetworkProtocol.Encode(first) == NetworkProtocol.Encode(second), "No usa manos reveladas rivales para decidir.");
            Check(a.LastSearchStates <= BotBrain.MaximumSearchStates, "Búsqueda acotada.");
        }

        SnapshotDto twoPairs = View(new[] { 5, 18, 7, 20 }, GamePhase.AwaitingPayment);
        var withoutMemory = new BotBrain(BotDifficulty.Expert, 1);
        var withMemory = new BotBrain(BotDifficulty.Expert, 1);
        withMemory.Observe(twoPairs with { OfferedCard = 33 });
        withMemory.Observe(twoPairs with { OfferedCard = 46 });
        Check(withoutMemory.Decide(twoPairs)!.CardId == 5 && withMemory.Decide(twoPairs)!.CardId == 7,
            "Experto cambia el pago usando exclusivamente cartas vistas públicamente.");
        withMemory.Reset();
        Check(withMemory.Decide(twoPairs)!.CardId == 5, "La memoria se limpia entre partidas.");
        SnapshotDto longRun = View(Enumerable.Range(0, 9).ToArray(), GamePhase.AwaitingDraw);
        Check(new BotBrain(BotDifficulty.Easy, 1).Decide(longRun)!.Plays![0].Cards.Length == 3 &&
            new BotBrain(BotDifficulty.Medium, 1).Decide(longRun)!.Plays![0].Cards.Length == 9,
            "Medio evalúa el tamaño de los juegos frente al primer juego legal de Fácil.");
        SnapshotDto overlapping = View(new[] { 0, 1, 2, 3, 4, 14, 27, 15, 28 }, GamePhase.AwaitingDraw);
        BotAction mediumPlan = new BotBrain(BotDifficulty.Medium, 1).Decide(overlapping)!;
        BotAction hardPlan = new BotBrain(BotDifficulty.Hard, 1).Decide(overlapping)!;
        Check(mediumPlan.Plays!.SelectMany(p => p.Cards).Count() == 5 &&
            hardPlan.Plays!.SelectMany(p => p.Cards).Count() == 6,
            "Difícil encuentra una partición mejor que escoger primero la escalera más larga.");

        var local = new LocalMatchController(new(" Ana ", 2, BotDifficulty.Expert), 37) { BotDelayMilliseconds = 0 };
        Check(local.Options.PlayerName == "Ana" && local.View.Players.Length == 3, "Nombre y cantidad de jugadores.");
        SnapshotDto original = local.View;
        Check(original.OwnHand.Length == 9 && original.DeckCount == 25 && original.RevealedHands.Length == 0, "Reparto privado de 3 jugadores.");
        original.OwnHand[0] = -1;
        original.Players[0] = original.Players[0] with { CardCount = 500 };
        Check(local.View.OwnHand.All(id => id >= 0) && local.View.Players[0].CardCount == 9, "Vista defensiva.");
        long revision = local.View.Revision;
        Check(!local.Tick(1000) && local.View.Revision == revision, "Elegir intercambio de un bot no interrumpe interacción humana.");
        Check(!local.Tick(2000) && local.View.Revision == revision, "Segundo intercambio privado tampoco cambia la revisión expuesta.");
        ReplyDto wrong = local.Submit(new BotAction(ActionKind.Pay, 51).ToCommand(local.View, local.NextHumanRequestId), 3000);
        Check(!wrong.Success && !wrong.Penalized, "Acción fuera de fase validada por la autoridad.");
        int foreign = Enumerable.Range(0, 52).First(id => !local.View.OwnHand.Contains(id));
        wrong = local.Submit(new BotAction(ActionKind.ExchangeCard, foreign).ToCommand(local.View, local.NextHumanRequestId), 3100);
        Check(!wrong.Success && !wrong.Penalized, "La partida local rechaza cartas ajenas igual que LAN.");
        SnapshotDto oldWindow = local.View;
        Check(local.Submit(new BotAction(ActionKind.ExchangeCard, oldWindow.OwnHand[0]).ToCommand(oldWindow, local.NextHumanRequestId), 3200).Success,
            "Intercambio válido aplicado simultáneamente al terminar todas las elecciones.");
        wrong = local.Submit(new BotAction(ActionKind.FinishInitial).ToCommand(oldWindow, local.NextHumanRequestId), 3300);
        Check(!wrong.Success && local.View.Phase == GamePhase.InitialDeclarations, "Acción atrasada no se aplica a la fase nueva.");
        SnapshotDto beforeClose = local.View;
        local.Close();
        Check(!local.IsActive && !local.Tick(9000), "Cerrar detiene todos los bots.");
        Check(!local.Submit(new BotAction(ActionKind.ExchangeCard, beforeClose.OwnHand[0]).ToCommand(beforeClose, local.NextHumanRequestId), 9000).Success, "Cerrar impide acciones humanas pendientes.");
        Check(local.View.Revision == beforeClose.Revision, "Cerrar conserva la última vista sin mutaciones tardías.");
        Throws<InvalidOperationException>(() => local.Restart(9001), "No reiniciar una sesión cerrada.");
        Console.WriteLine("OK: contratos, privacidad, reglas, decisiones, cierre y configuraciones inválidas.");
    }

    private static void Simulate(BotDifficulty difficulty, int botCount, int seed, bool replay)
    {
        var match = new LocalMatchController(new("Persona", botCount, difficulty), seed) { BotDelayMilliseconds = 0 };
        string trace = PlayToEnd(match, new BotBrain(difficulty, 8001), 1000, seed);
        if (replay)
        {
            var same = new LocalMatchController(new("Persona", botCount, difficulty), seed) { BotDelayMilliseconds = 0 };
            Check(trace == PlayToEnd(same, new BotBrain(difficulty, 8001), 1000, seed), "Determinismo por semilla y decisiones, sin usar identidad de sesión.");
            long oldWindow = match.View.Window;
            match.Restart(10_000_000);
            Check(match.View.Phase == GamePhase.InitialExchange && match.View.Players.Length == botCount + 1 &&
                match.View.Window > oldWindow && match.View.RevealedHands.Length == 0, "Revancha limpia, mismos jugadores, sin cartas reveladas.");
            PlayToEnd(match, new BotBrain(difficulty, 8101), 10_001_000, seed);
        }
    }

    private static string PlayToEnd(LocalMatchController match, BotBrain human, long start, int seed)
    {
        var trace = new List<string>();
        long now = start;
        int stagnant = 0;
        for (int step = 0; step < 5000; step++, now += 100)
        {
            SnapshotDto view = match.View;
            Phases.Add(view.Phase);
            if (view.Phase == GamePhase.Finished)
            {
                _games++;
                Results[view.Result] = Results.GetValueOrDefault(view.Result) + 1;
                Check(match.BotRejectedActions == 0, "Bot rechazado: " + match.LastBotError);
                Check(view.Result != MatchResult.None, "Resultado al finalizar.");
                Check(!match.Tick(now + 1000), "No hay turnos de bots tras finalizar.");
                if (view.Result == MatchResult.Desmoche)
                    Check(view.Melds.Where(m => m.OwnerId == view.WinnerPlayerId).Sum(m => m.Cards.Length) == 10 &&
                        view.Players.Single(p => p.Id == view.WinnerPlayerId).CardCount == 0, "Ganador: diez combinadas y ninguna suelta.");
                return string.Join("|", trace);
            }
            Check(view.OwnHand.Distinct().Count() == view.OwnHand.Length && view.RevealedHands.Length == 0, "Solo mano propia única mientras se juega.");
            int cardCount = view.Players.Sum(p => p.CardCount) + view.Melds.Sum(m => m.Cards.Length) +
                view.DeckCount + view.DiscardCount + (view.OfferedCard is null ? 0 : 1);
            Check(cardCount == 52, $"Conservación de cartas: {cardCount}.");
            Check(view.Melds.All(m => RulesValidator.ValidateMeld(m.Cards.Select(Card.FromId).ToArray()).IsValid), "Todos los juegos publicados son válidos.");
            long stamp = Stopwatch.GetTimestamp();
            BotAction? action = human.Decide(view, match.PublicContext);
            DecisionTimes.Add(Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
            if (action is not null)
            {
                Actions.Add(action.Kind);
                ReplyDto reply = match.Submit(action.ToCommand(view, match.NextHumanRequestId), now);
                Check(reply.Success && !reply.Penalized, $"Persona simulada: seed={seed}, {match.Options}, {view.Phase}, {action.Kind}: {reply.Message}");
            }
            stamp = Stopwatch.GetTimestamp();
            bool changed = match.Tick(now);
            TickTimes.Add(Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
            SnapshotDto current = match.View;
            trace.Add(NetworkProtocol.Encode(current with { Session = "fixed" }));
            Check(match.BotRejectedActions == 0, $"seed={seed}, {match.Options}: {match.LastBotError}");
            stagnant = action is null && !changed ? stagnant + 1 : 0;
            Check(stagnant < 30, $"Partida atascada: seed={seed}, {match.Options}, {current.Phase}, turno={current.CurrentPlayerId}, pago={current.PaymentPlayerId}, mazo={current.DeckCount}.");
        }
        throw new InvalidOperationException("Partida excedió 5000 pasos: " + match.Options + ", seed=" + seed);
    }

    private static void ReportTiming(string label, List<double> samples)
    {
        samples.Sort();
        Console.WriteLine($"TIEMPO {label}: {samples.Count} muestras; media={samples.Average():F2} ms, " +
            $"p95={samples[(int)((samples.Count - 1) * .95)]:F2} ms, máximo={samples[^1]:F2} ms.");
    }
}
