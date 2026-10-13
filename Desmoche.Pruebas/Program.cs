using Desmoche.Domain;

try
{
RuleRevisionTests.Run();
RulesTests.Run();
GameManagerTests.Run();
ConnectionLifecycleTests.Run();
NetworkTests.Run();
StateReplicationTests.Run();

int checks = 0;

// 1. Baraja de 52 cartas, sin duplicados.
Deck original = Deck.CreateStandard();
Deck remaining = original;
var cards = new List<Card>();

while (remaining.Count > 0)
{
    var draw = remaining.Draw();
    cards.Add(draw.Card);
    remaining = draw.RemainingDeck;
}

Check(cards.Count == 52, "Baraja de 52 cartas.");

Check(cards.Select(c => c.Id).Distinct().Count() == 52,
    "No hay cartas duplicadas.");

foreach (Suit suit in Enum.GetValues<Suit>())
{
    Check(cards.Count(c => c.Suit == suit) == 13,
        $"Trece cartas del palo {suit}.");
}

Check(original.Count == 52,
    "El mazo original permanece intacto.");

Expect<InvalidOperationException>(
    () => remaining.Draw(),
    "Robar de un mazo vacio se rechaza.");

// 2. Manos y proteccion contra duplicados.
var emptyHand = new PlayerHand();
var newHand = emptyHand.Add(Card.FromId(0));

Check(emptyHand.Count == 0 && newHand.Count == 1,
    "Agregar una carta crea una mano nueva.");

Expect<ArgumentException>(
    () => newHand.Add(Card.FromId(0)),
    "Agregar una carta duplicada se rechaza.");

// 3. Reparto, robo, descarte y privacidad.
for (int count = 2; count <= 4; count++)
{
    Player[] players = Enumerable.Range(1, count)
        .Select(id => new Player(id, $"Jugador {id}", id - 1))
        .ToArray();

    GameState initial = GameState.Create(
        players,
        Deck.CreateStandard());

    GameState state = initial.DealInitialHands();

    Check(state.Players.All(p => p.Hand.Count == 9),
        $"{count} jugadores reciben nueve cartas.");

    Check(state.DeckCount == 52 - count * 9,
        "El mazo conserva la cantidad correcta.");

    Check(state.DiscardCount == 0 && state.DiscardTop is null,
        "El pozo comienza vacio.");

    Check(
        state.Players.SelectMany(p => p.Hand.Cards)
            .Select(c => c.Id).Distinct().Count() == count * 9,
        "No se repiten cartas entre manos.");

    Check(initial.DeckCount == 52,
        "El estado anterior al reparto permanece intacto.");

    Expect<InvalidOperationException>(
        () => state.DealInitialHands(),
        "Repartir dos veces se rechaza.");

    var draw = state.MoveDeckTopToHand(1);
    GameState afterDraw = draw.State;

    Check(afterDraw.GetPlayer(1).Hand.Count == 10,
        "Robar incorpora una carta a la mano.");

    Check(state.GetPlayer(1).Hand.Count == 9,
        "El robo no modifica el estado anterior.");

    GameState afterDiscard = afterDraw.MoveHandCardToDiscard(
        1,
        draw.Card.Id);

    Check(afterDiscard.GetPlayer(1).Hand.Count == 9,
        "Descartar retira la carta de la mano.");

    Check(afterDiscard.DiscardTop == draw.Card,
        "La carta descartada queda arriba del pozo.");

    int foreignCard = afterDiscard.GetPlayer(2).Hand.Cards[0].Id;

    Expect<InvalidOperationException>(
        () => afterDiscard.MoveHandCardToDiscard(1, foreignCard),
        "Descartar una carta ajena se rechaza.");

    Expect<InvalidOperationException>(
        () => afterDiscard.MoveHandCardToDiscard(1, draw.Card.Id),
        "Descartar otra vez la misma carta se rechaza.");

    GameState.PlayerView view = afterDiscard.CreateViewFor(1);

    Check(
        view.OwnHand.SequenceEqual(afterDiscard.GetPlayer(1).Hand.Cards),
        "La vista contiene la mano propia.");

    Check(view.Players.Single(p => p.Id == 2).CardCount == 9,
        "La vista muestra la cantidad de cartas del rival.");

    Check(
        typeof(GameState.PlayerSummary).GetProperty("Hand") is null,
        "El resumen publico no contiene la mano del rival.");

    Expect<NotSupportedException>(
        () => ((IList<Card>)view.OwnHand).Clear(),
        "La mano de la vista no puede modificarse.");

    int total = afterDiscard.DeckCount
        + afterDiscard.DiscardCount
        + afterDiscard.Players.Sum(p => p.Hand.Count);

    Check(total == 52,
        "La partida conserva las 52 cartas.");
}

Console.WriteLine();
Console.WriteLine(
    $"PRUEBAS COMPLETADAS: {checks} comprobaciones correctas.");

void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception($"FALLO: {description}");

    checks++;
    Console.WriteLine($"OK: {description}");
}

void Expect<T>(Action action, string description)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        Check(true, description);
        return;
    }

    throw new Exception(
        $"FALLO: {description} Se esperaba {typeof(T).Name}.");
}

}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
