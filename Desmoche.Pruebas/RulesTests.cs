using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;

public static class RulesTests
{
    public static void Run()
    {
        int checks = 0;
        Card[] three = Set(6, 3);
        Card[] four = Set(6, 4);
        Card[] run = C(Suit.Hearts, 2, 3, 4);

        Check(RulesValidator.IsTercia(three), "Tercia de rango exacto.");
        Check(!RulesValidator.IsTercia(four), "Una cuarta no es tercia.");
        Check(RulesValidator.IsCuarta(four), "Cuarta de cuatro palos.");
        Check(!RulesValidator.IsCuarta(three), "Tres cartas no son cuarta.");

        Check(!RulesValidator.IsTercia(new[]
        {
            new Card(Suit.Clubs, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Queen),
            new Card(Suit.Hearts, Rank.King)
        }), "J, Q y K no forman tercia.");

        Check(RulesValidator.IsTercia(Set(11, 3)), "Tercia de J.");
        Check(RulesValidator.IsCuarta(Set(1, 4)), "Cuarta de ases.");
        Check(RulesValidator.IsTercia(three.Reverse().ToArray()),
            "El orden de palos no afecta una tercia.");

        Check(RulesValidator.IsEscalera(run), "Escalera ascendente.");
        Check(RulesValidator.IsEscalera(run.Reverse().ToArray()),
            "Escalera descendente.");

        Check(RulesValidator.IsEscalera(C(Suit.Clubs, 1, 2, 3)),
            "As bajo: A-2-3.");
        Check(RulesValidator.IsEscalera(C(Suit.Clubs, 12, 13, 1)),
            "As alto: Q-K-A.");
        Check(RulesValidator.IsEscalera(C(Suit.Clubs, 1, 13, 12)),
            "As alto descendente: A-K-Q.");
        Check(RulesValidator.IsEscalera(C(Suit.Clubs, 10, 11, 12, 13)),
            "Orden 10-J-Q-K.");

        Check(!RulesValidator.IsEscalera(C(Suit.Clubs, 13, 1, 2)),
            "K-A-2 no es escalera.");
        Check(!RulesValidator.IsEscalera(C(Suit.Clubs, 12, 13, 1, 2)),
            "Q-K-A-2 no es escalera.");
        Check(!RulesValidator.IsEscalera(C(Suit.Clubs, 2, 4, 5)),
            "Escalera con hueco rechazada.");
        Check(!RulesValidator.IsEscalera(C(Suit.Clubs, 2, 4, 3)),
            "Declaracion desordenada rechazada.");

        Check(!RulesValidator.IsEscalera(new[]
        {
            new Card(Suit.Clubs, Rank.Two),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Clubs, Rank.Four)
        }), "Mezclar palos no forma escalera.");

        Check(!RulesValidator.IsEscalera(C(Suit.Clubs, 1, 2)),
            "Dos cartas no forman escalera.");

        Expect(RulesValidator.ValidateMeld(null),
            RuleError.NullInput, "Grupo nulo rechazado.");
        Expect(RulesValidator.ValidateMeld(Array.Empty<Card>()),
            RuleError.InvalidMeld, "Grupo vacio rechazado.");
        Expect(RulesValidator.ValidateMeld(new Card[]
        {
            Card.FromId(0), null!, Card.FromId(2)
        }), RuleError.NullInput, "Carta nula rechazada.");

        Expect(RulesValidator.ValidateMeld(new[]
        {
            three[0], new Card(three[0].Suit, three[0].Rank), three[1]
        }), RuleError.DuplicateCard, "Duplicado fisico por Id rechazado.");

        var validGroups = G(three, run);

        Expect(RulesValidator.ValidateGroups(validGroups),
            RuleError.None, "Agrupaciones validas.");
        Expect(RulesValidator.ValidateGroups(null),
            RuleError.NullInput, "Agrupaciones nulas rechazadas.");
        Expect(RulesValidator.ValidateGroups(G()),
            RuleError.EmptyGroups, "Agrupaciones vacias rechazadas.");
        Expect(RulesValidator.ValidateGroups(G(three, four)),
            RuleError.DuplicateCard, "Una carta no pertenece a dos grupos.");

        var badGroup = RulesValidator.ValidateGroups(
            G(three, C(Suit.Spades, 2, 4, 5)));

        Check(badGroup.Error == RuleError.InvalidMeld &&
              badGroup.GroupIndex == 1,
            "El error identifica el grupo incorrecto.");

        var hand = new PlayerHand(three.Concat(run));

        Expect(RulesValidator.ValidateCompleteHand(hand, validGroups),
            RuleError.None, "Mano completa de seis cartas.");
        Expect(RulesValidator.ValidateCompleteHand(hand, G(three)),
            RuleError.IncompleteHand, "Carta sin agrupar rechazada.");
        Expect(RulesValidator.ValidateCompleteHand(
            hand, G(three, C(Suit.Spades, 2, 3, 4))),
            RuleError.CardNotInHand, "Cartas ajenas rechazadas.");
        Expect(RulesValidator.ValidateCompleteHand(new PlayerHand(), G()),
            RuleError.EmptyGroups, "Mano vacia no es una mano combinada.");

        var tenGroups = G(
            four,
            C(Suit.Hearts, 2, 3, 4),
            C(Suit.Spades, 9, 10, 11));

        var tenHand = new PlayerHand(tenGroups.SelectMany(g => g));

        Expect(RulesValidator.ValidateDesmoche(tenHand, G(), tenGroups),
            RuleError.None, "Desmoche con 4+3+3, sin descarte.");

        var fiveFive = G(
            C(Suit.Clubs, 2, 3, 4, 5, 6),
            C(Suit.Diamonds, 7, 8, 9, 10, 11));

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(fiveFive.SelectMany(g => g)), G(), fiveFive),
            RuleError.None, "Desmoche con dos escaleras de cinco.");

        var nineGroups = G(Set(3, 3), Set(6, 3), Set(9, 3));
        var nineHand = new PlayerHand(nineGroups.SelectMany(g => g));

        Expect(RulesValidator.ValidateCompleteHand(nineHand, nineGroups),
            RuleError.None, "Nueve cartas pueden estar todas combinadas.");
        Expect(RulesValidator.ValidateDesmoche(nineHand, G(), nineGroups),
            RuleError.IncorrectWinningCount, "Nueve combinadas no ganan.");

        var elevenGroups = G(Set(3, 4), Set(6, 4), Set(9, 3));

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(elevenGroups.SelectMany(g => g)),
            G(), elevenGroups),
            RuleError.IncorrectWinningCount, "Once combinadas no ganan.");

        Expect(RulesValidator.ValidateDesmoche(
            tenHand.Add(new Card(Suit.Spades, Rank.Ace)), G(), tenGroups),
            RuleError.IncompleteHand, "Diez combinadas y una suelta no ganan.");

        var table = G(four);
        var fromHand = G(tenGroups[1].ToArray(), tenGroups[2].ToArray());

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(fromHand.SelectMany(g => g)), table, fromHand),
            RuleError.None, "La victoria suma mesa propia y mano.");

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(), tenGroups, G()),
            RuleError.None, "Diez en mesa y mano vacia son estructura ganadora.");

        Expect(RulesValidator.ValidateDesmoche(tenHand, table, tenGroups),
            RuleError.DuplicateCard, "No se duplican cartas entre mesa y mano.");

        Expect(RulesValidator.ValidateDesmoche(tenHand,
            G(C(Suit.Clubs, 2, 4, 5)), tenGroups),
            RuleError.InvalidMeld, "Los juegos de mesa tambien se validan.");

        Expect(RulesValidator.ValidateClaimUse(three[0], G(three)),
            RuleError.None, "Reclamada usada en tercia.");
        Expect(RulesValidator.ValidateClaimUse(four[3], G(four)),
            RuleError.None, "Reclamada usada en cuarta.");
        Expect(RulesValidator.ValidateClaimUse(run[2], G(run)),
            RuleError.None, "Reclamada usada en escalera.");

        Expect(RulesValidator.ValidateClaimUse(
            new Card(Suit.Spades, Rank.Ace), validGroups),
            RuleError.MissingClaimedCard, "Reclamada sin jugar rechazada.");

        Expect(RulesValidator.ValidateClaimUse(three[0], G(three, four)),
            RuleError.DuplicateCard, "Reclamada repetida rechazada.");

        Card[] target = C(Suit.Spades, 4, 5, 6);
        int moved = four[3].Id;

        Expect(RulesValidator.ValidateCardTransfer(four, moved, target),
            RuleError.None, "Cuarta queda tercia y carta forma nueva escalera.");

        Expect(RulesValidator.ValidateCardTransfer(
            C(Suit.Spades, 2, 3, 4, 5),
            new Card(Suit.Spades, Rank.Five).Id, Set(5, 4)),
            RuleError.None, "Quitar un extremo conserva la escalera.");

        Expect(RulesValidator.ValidateCardTransfer(
            C(Suit.Spades, 2, 3, 4, 5),
            new Card(Suit.Spades, Rank.Three).Id, Set(3, 4)),
            RuleError.SourceWouldBreak, "Quitar una carta intermedia rechazado.");

        Expect(RulesValidator.ValidateCardTransfer(three, three[0].Id,
            C(Suit.Clubs, 4, 5, 6)),
            RuleError.SourceWouldBreak, "No se retira una carta de una tercia.");

        Expect(RulesValidator.ValidateCardTransfer(
            four, Card.FromId(0).Id, target),
            RuleError.CardNotInHand, "Carta ausente del origen rechazada.");

        Expect(RulesValidator.ValidateCardTransfer(
            four, moved, C(Suit.Spades, 2, 3, 4)),
            RuleError.MissingTransferredCard, "Destino debe usar la carta.");

        Expect(RulesValidator.ValidateCardTransfer(four, moved, four),
            RuleError.DuplicateCard, "Destino no reutiliza cartas del donante.");

        var low = C(Suit.Hearts, 1, 2, 3, 4);
        var lowHand = new PlayerHand(
            low.Concat(C(Suit.Hearts, 5)).Concat(C(Suit.Clubs, 7, 9, 11, 13)));

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, low),
            RuleError.None, "Victoria inicial A-2-3-4.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, low.Reverse().ToArray()),
            RuleError.None, "Victoria inicial descendente 4-3-2-A.");

        var high = C(Suit.Spades, 11, 12, 13, 1);
        var highHand = new PlayerHand(
            high.Concat(C(Suit.Spades, 10)).Concat(C(Suit.Clubs, 2, 4, 6, 8)));

        Expect(RulesValidator.ValidateInitialVictory(highHand,
            InitialVictoryKind.HighStraight, high),
            RuleError.None, "Victoria inicial J-Q-K-A.");

        var quadHand = new PlayerHand(
            four.Concat(C(Suit.Hearts, 1, 3, 5, 7, 9)));

        Expect(RulesValidator.ValidateInitialVictory(quadHand,
            InitialVictoryKind.Cuarta, four),
            RuleError.None, "Victoria inicial por cuarta.");

        var almostQuad = new PlayerHand(
            three.Concat(C(Suit.Hearts, 1, 3, 5, 7, 9, 11)));

        Expect(RulesValidator.ValidateInitialVictory(almostQuad,
            InitialVictoryKind.Cuarta, four, four[3]),
            RuleError.None, "Cuarta inicial completada con primera carta del pozo.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, low,
            new Card(Suit.Spades, Rank.Seven)),
            RuleError.MissingClaimedCard, "Primera carta reclamada debe usarse.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, low, low[0]),
            RuleError.DuplicateCard, "Primera carta no puede estar ya en mano.");

        var red = new PlayerHand(C(Suit.Hearts, 1, 3, 5, 7, 9)
            .Concat(C(Suit.Diamonds, 2, 4, 6, 8)));

        Expect(RulesValidator.ValidateInitialVictory(
            red, InitialVictoryKind.Color, red.Cards.OrderBy(c => (int)c.Rank).ToArray()),
            RuleError.None, "Color rojo con distintos palos.");

        var black = new PlayerHand(C(Suit.Clubs, 1, 3, 5, 7, 9)
            .Concat(C(Suit.Spades, 2, 4, 6, 8)));

        Expect(RulesValidator.ValidateInitialVictory(
            black, InitialVictoryKind.Color, black.Cards.OrderBy(c => (int)c.Rank).ToArray()),
            RuleError.None, "Color negro con distintos palos.");

        Expect(RulesValidator.ValidateInitialVictory(
            lowHand, InitialVictoryKind.Color),
            RuleError.InvalidInitialDeclaration, "Colores mezclados no ganan.");

        Expect(RulesValidator.ValidateInitialVictory(new PlayerHand(low),
            InitialVictoryKind.Color),
            RuleError.IncorrectInitialHandCount,
            "Declaracion inicial requiere nueve cartas.");

        var peladillaCards = C(Suit.Clubs, 2, 6, 10)
            .Concat(C(Suit.Diamonds, 3, 7, 11))
            .Concat(C(Suit.Hearts, 4, 8, 12)).ToArray();

        var peladilla = new PlayerHand(peladillaCards);

        Expect(RulesValidator.ValidateInitialVictory(
            peladilla, InitialVictoryKind.Peladilla),
            RuleError.None, "Peladilla sin juegos ni proyectos con una carta.");

        CheckPeladillaReplacement(Suit.Diamonds, 3, 2,
            "Dos del mismo rango excluyen peladilla: otra carta completa la tercia.");
        CheckPeladillaReplacement(Suit.Clubs, 6, 3,
            "Dos consecutivas excluyen peladilla aunque falte la tercera.");
        CheckPeladillaReplacement(Suit.Clubs, 6, 4,
            "Dos con hueco excluyen peladilla: una carta completa la escalera.");
        CheckPeladillaReplacement(Suit.Hearts, 4, 1,
            "Q-A del mismo palo excluye peladilla aunque falte K.");

        Expect(RulesValidator.ValidateInitialVictory(
            lowHand, InitialVictoryKind.Peladilla),
            RuleError.InvalidInitialDeclaration,
            "Un juego ya formado excluye peladilla.");

        Expect(RulesValidator.ValidateInitialVictory(
            peladilla, (InitialVictoryKind)99),
            RuleError.InvalidInitialDeclaration,
            "Tipo de victoria desconocido rechazado.");

        Expect(RulesValidator.ValidateInitialVictory(red,
            InitialVictoryKind.Color, firstDiscard: Card.FromId(0)),
            RuleError.InvalidInitialDeclaration,
            "Color no reclama la primera carta del pozo.");

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(four), tenGroups, G()),
            RuleError.DuplicateCard,
            "Mesa y mano no pueden contener la misma carta.");

        var tenRun = C(Suit.Clubs, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        Expect(RulesValidator.ValidateDesmoche(
            new PlayerHand(tenRun), G(), G(tenRun)),
            RuleError.None, "Escalera de diez gana sin descarte.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, C(Suit.Hearts, 2, 3, 4, 5)),
            RuleError.InvalidInitialDeclaration,
            "2-3-4-5 no cumple la victoria A234.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, C(Suit.Hearts, 1, 3, 2, 4, 5)),
            RuleError.InvalidMeld, "Victoria inicial tambien respeta el orden.");

        Expect(RulesValidator.ValidateInitialVictory(lowHand,
            InitialVictoryKind.LowStraight, C(Suit.Spades, 1, 2, 3, 4, 5)),
            RuleError.CardNotInHand, "Victoria inicial no acepta cartas ajenas.");

        Expect(RulesValidator.ValidateInitialVictory(
            lowHand, InitialVictoryKind.Cuarta),
            RuleError.NullInput, "Cuarta inicial necesita declarar sus cartas.");

        var almostLow = new PlayerHand(C(Suit.Hearts, 1, 2, 3)
            .Concat(C(Suit.Clubs, 5, 6, 8, 10, 12, 13)));

        Expect(RulesValidator.ValidateInitialVictory(almostLow,
            InitialVictoryKind.LowStraight, low, low[3]),
            RuleError.None, "A234 puede completarse con primera carta del pozo.");

        var referenceHands = new List<PlayerHand>
        {
            peladilla,
            lowHand,
            quadHand,
            new(C(Suit.Clubs, 1, 4, 7)
                .Concat(C(Suit.Diamonds, 2, 5, 8))
                .Concat(C(Suit.Hearts, 3, 6, 9))),
            new(C(Suit.Clubs, 13, 2, 5)
                .Concat(C(Suit.Diamonds, 3, 6, 9))
                .Concat(C(Suit.Hearts, 4, 7, 10)))
        };

        var random = new Random(7303);

        for (int sample = 0; sample < 128; sample++)
        {
            Deck deck = Deck.CreateStandard().Shuffle(random);
            var sampleHand = new PlayerHand();

            for (int i = 0; i < 9; i++)
            {
                var draw = deck.Draw();
                deck = draw.RemainingDeck;
                sampleHand = sampleHand.Add(draw.Card);
            }

            referenceHands.Add(sampleHand);
        }

        Check(referenceHands.All(h =>
            RulesValidator.ValidateInitialVictory(
                h, InitialVictoryKind.Peladilla).IsValid
                    == ReferencePeladilla(h)),
            "Peladilla coincide con busqueda independiente de juegos y proyectos en 133 manos.");

        Card[] standardCards = Enumerable.Range(0, Card.StandardDeckSize).Select(Card.FromId).ToArray();
        int pairCases = 0;
        bool pairsMatch = true;
        for (int a = 0; a < standardCards.Length; a++)
            for (int b = a + 1; b < standardCards.Length; b++)
            {
                Card[] pair = { standardCards[a], standardCards[b] };
                bool canComplete = standardCards.Where(c => c.Id != pair[0].Id && c.Id != pair[1].Id)
                    .Any(c => ReferenceTriple(pair[0], pair[1], c));
                pairsMatch &= RulesValidator.IsPeladilla(pair) == !canComplete;
                pairCases++;
            }
        Check(pairCases == 1326 && pairsMatch,
            "Las 1326 parejas posibles coinciden con una busqueda de todas las cartas que pueden completarlas.");

        Check(RulesValidator.ValidateDesmoche(tenHand, G(), tenGroups) ==
              RulesValidator.ValidateDesmoche(tenHand, G(), tenGroups),
            "Mismos datos producen el mismo resultado.");

        Check(tenHand.Count == 10 && four.SequenceEqual(Set(6, 4)),
            "El validador no modifica mano ni grupos.");

        int runCases = 0;

        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (int first in new[] { 1, 2 })
            {
                int last = first == 1 ? 13 : 14;

                for (int start = first; start <= last - 2; start++)
                {
                    for (int end = start + 2; end <= last; end++)
                    {
                        Card[] sequence = Enumerable
                            .Range(start, end - start + 1)
                            .Select(value => new Card(suit,
                                (Rank)(value == 14 ? 1 : value)))
                            .ToArray();

                        if (!RulesValidator.IsEscalera(sequence) ||
                            !RulesValidator.IsEscalera(
                                sequence.Reverse().ToArray()))
                        {
                            throw new Exception(
                                "FALLO: escalera del catalogo.");
                        }

                        runCases += 2;
                    }
                }
            }
        }

        Check(runCases == 1056,
            "Catalogo de 1056 escaleras verificadas.");

        Console.WriteLine(
            $"REGLAS: {checks} comprobaciones correctas; " +
            $"{runCases} casos de escalera.");

        void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception($"FALLO REGLAS: {description}");

            checks++;
            Console.WriteLine($"OK REGLAS: {description}");
        }

        void Expect(
            RuleValidationResult result,
            RuleError error,
            string description)
        {
            Check(
                result.Error == error &&
                result.IsValid == (error == RuleError.None),
                description + $" Resultado: {result.Error}.");
        }

        void CheckPeladillaReplacement(
            Suit suit,
            int oldRank,
            int newRank,
            string description)
        {
            var changed = new PlayerHand(peladillaCards.Select(c =>
                c.Suit == suit && (int)c.Rank == oldRank
                    ? new Card(suit, (Rank)newRank)
                    : c));

            Expect(RulesValidator.ValidateInitialVictory(
                changed, InitialVictoryKind.Peladilla),
                RuleError.InvalidInitialDeclaration, description);
        }
    }

    // Oraculo independiente: ternas actuales y parejas con cualquier carta ausente.
    private static bool ReferencePeladilla(PlayerHand hand)
    {
        IReadOnlyList<Card> cards = hand.Cards;

        for (int i = 0; i < cards.Count; i++)
        {
            for (int j = i + 1; j < cards.Count; j++)
            {
                for (int k = j + 1; k < cards.Count; k++)
                {
                    if (ReferenceTriple(cards[i], cards[j], cards[k]))
                        return false;
                }

                for (int id = 0; id < Card.StandardDeckSize; id++)
                {
                    if (!hand.Contains(id) && ReferenceTriple(cards[i], cards[j], Card.FromId(id)))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool ReferenceTriple(Card a, Card b, Card c)
    {
        if (a.Rank == b.Rank && b.Rank == c.Rank)
            return true;

        if (a.Suit != b.Suit || b.Suit != c.Suit)
            return false;

        int[] ranks = { (int)a.Rank, (int)b.Rank, (int)c.Rank };
        Array.Sort(ranks);

        return (ranks[1] == ranks[0] + 1 &&
                ranks[2] == ranks[1] + 1) ||
            ranks.SequenceEqual(new[] { 1, 12, 13 });
    }

    private static Card[] C(Suit suit, params int[] ranks) =>
        ranks.Select(rank => new Card(suit, (Rank)rank)).ToArray();

    private static Card[] Set(int rank, int count) =>
        Enumerable.Range(0, count)
            .Select(suit => new Card((Suit)suit, (Rank)rank)).ToArray();

    private static IReadOnlyList<Card>[] G(params Card[][] groups) =>
        groups.Cast<IReadOnlyList<Card>>().ToArray();
}
