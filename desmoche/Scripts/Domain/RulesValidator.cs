using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

public enum RuleError
{
	None,
	NullInput,
	EmptyGroups,
	DuplicateCard,
	InvalidMeld,
	CardNotInHand,
	IncompleteHand,
	IncorrectWinningCount,
	MissingClaimedCard,
	MissingTransferredCard,
	SourceWouldBreak,
	IncorrectInitialHandCount,
	InvalidInitialDeclaration
}

public enum InitialVictoryKind
{
	LowStraight,
	HighStraight,
	Cuarta,
	Color,
	Peladilla
}

public readonly record struct RuleValidationResult(
	RuleError Error,
	int GroupIndex = -1)
{
	public bool IsValid => Error == RuleError.None;
}

public static class RulesValidator
{
	public const int WinningCardCount = 10;

	public static bool IsTercia(IReadOnlyList<Card>? cards) =>
		ValidateCards(cards).IsValid &&
		cards!.Count == 3 && SameRank(cards);

	public static bool IsCuarta(IReadOnlyList<Card>? cards) =>
		ValidateCards(cards).IsValid &&
		cards!.Count == 4 && SameRank(cards);

	public static bool IsEscalera(IReadOnlyList<Card>? cards) =>
		ValidateCards(cards).IsValid && IsStraight(cards!);

	public static RuleValidationResult ValidateMeld(
		IReadOnlyList<Card>? cards)
	{
		RuleValidationResult result = ValidateCards(cards);
		if (!result.IsValid)
			return result;

		bool isSet = (cards!.Count == 3 || cards.Count == 4)
			&& SameRank(cards);

		return isSet || IsStraight(cards)
			? Ok()
			: Fail(RuleError.InvalidMeld);
	}

	public static RuleValidationResult ValidateGroups(
		IReadOnlyList<IReadOnlyList<Card>>? groups)
	{
		if (groups is null)
			return Fail(RuleError.NullInput);
		if (groups.Count == 0)
			return Fail(RuleError.EmptyGroups);

		var used = new HashSet<int>();

		for (int i = 0; i < groups.Count; i++)
		{
			RuleValidationResult result = ValidateMeld(groups[i]);
			if (!result.IsValid)
				return new RuleValidationResult(result.Error, i);

			foreach (Card card in groups[i])
			{
				if (!used.Add(card.Id))
					return new RuleValidationResult(
						RuleError.DuplicateCard, i);
			}
		}

		return Ok();
	}

	// Cubre toda la mano; no exige diez cartas ni declara un ganador.
	public static RuleValidationResult ValidateCompleteHand(
		PlayerHand? hand,
		IReadOnlyList<IReadOnlyList<Card>>? groups)
	{
		if (hand is null)
			return Fail(RuleError.NullInput);

		RuleValidationResult result = ValidateGroups(groups);
		if (!result.IsValid)
			return result;

		return ValidateHandCoverage(hand, groups!);
	}

	// El llamador obtiene los grupos propios del estado verdadero.
	// Esta validacion comprueba cartas, no turno ni habilitacion.
	public static RuleValidationResult ValidateDesmoche(
		PlayerHand? hand,
		IReadOnlyList<IReadOnlyList<Card>>? ownTableGroups,
		IReadOnlyList<IReadOnlyList<Card>>? declaredHandGroups)
	{
		if (hand is null || ownTableGroups is null ||
			declaredHandGroups is null)
			return Fail(RuleError.NullInput);

		IReadOnlyList<Card>[] allGroups = ownTableGroups
			.Concat(declaredHandGroups).ToArray();

		RuleValidationResult result = ValidateGroups(allGroups);
		if (!result.IsValid)
			return result;

		foreach (Card card in ownTableGroups.SelectMany(g => g))
		{
			if (hand.Contains(card.Id))
				return Fail(RuleError.DuplicateCard);
		}

		result = ValidateHandCoverage(hand, declaredHandGroups);
		if (!result.IsValid)
			return result;

		return allGroups.Sum(g => g.Count) == WinningCardCount
			? Ok()
			: Fail(RuleError.IncorrectWinningCount);
	}

	// Los grupos representan el resultado de jugar la carta reclamada.
	public static RuleValidationResult ValidateClaimUse(
		Card? claimedCard,
		IReadOnlyList<IReadOnlyList<Card>>? resultingGroups)
	{
		if (claimedCard is null)
			return Fail(RuleError.NullInput);

		RuleValidationResult result = ValidateGroups(resultingGroups);
		if (!result.IsValid)
			return result;

		return resultingGroups!.SelectMany(g => g)
			.Any(c => c.Id == claimedCard.Id)
				? Ok()
				: Fail(RuleError.MissingClaimedCard);
	}

	// Valida el traslado inmediato de UNA carta entre juegos propios.
	// destinationAfter puede ser un juego nuevo.
	public static RuleValidationResult ValidateCardTransfer(
		IReadOnlyList<Card>? sourceBefore,
		int movedCardId,
		IReadOnlyList<Card>? destinationAfter)
	{
		RuleValidationResult result = ValidateMeld(sourceBefore);
		if (!result.IsValid)
			return result;

		if (!sourceBefore!.Any(c => c.Id == movedCardId))
			return Fail(RuleError.CardNotInHand);

		Card[] sourceAfter = sourceBefore!
			.Where(c => c.Id != movedCardId).ToArray();

		if (!ValidateMeld(sourceAfter).IsValid)
			return Fail(RuleError.SourceWouldBreak);

		result = ValidateMeld(destinationAfter);
		if (!result.IsValid)
			return result;

		if (!destinationAfter!.Any(c => c.Id == movedCardId))
			return Fail(RuleError.MissingTransferredCard);

		var remainingIds = sourceAfter.Select(c => c.Id).ToHashSet();

		return destinationAfter!.Any(c => remainingIds.Contains(c.Id))
			? Fail(RuleError.DuplicateCard)
			: Ok();
	}

	// Precondicion del llamador: sigue abierta la declaracion inicial.
	// firstDiscard representa la primera carta que se solicita reclamar.
	public static RuleValidationResult ValidateInitialVictory(
		PlayerHand? initialHand,
		InitialVictoryKind kind,
		IReadOnlyList<Card>? declaredCombination = null,
		Card? firstDiscard = null)
	{
		if (initialHand is null)
			return Fail(RuleError.NullInput);

		if (initialHand.Count != GameState.InitialHandSize)
			return Fail(RuleError.IncorrectInitialHandCount);

		if (kind == InitialVictoryKind.Color ||
			kind == InitialVictoryKind.Peladilla)
		{
			if (firstDiscard is not null || kind == InitialVictoryKind.Peladilla && declaredCombination is not null)
				return Fail(RuleError.InvalidInitialDeclaration);
			bool valid = kind == InitialVictoryKind.Color
				? declaredCombination is not null && ValidateCards(declaredCombination).IsValid &&
					declaredCombination.Count == initialHand.Count &&
					declaredCombination.All(c => initialHand.Contains(c.Id)) &&
					IsColor(declaredCombination) && IsAscendingColor(declaredCombination)
				: IsPeladilla(initialHand.Cards);

			return valid ? Ok() : Fail(RuleError.InvalidInitialDeclaration);
		}

		if (kind != InitialVictoryKind.LowStraight &&
			kind != InitialVictoryKind.HighStraight &&
			kind != InitialVictoryKind.Cuarta)
			return Fail(RuleError.InvalidInitialDeclaration);

		RuleValidationResult result = ValidateMeld(declaredCombination);
		if (!result.IsValid)
			return result;

		if (firstDiscard is not null && initialHand.Contains(firstDiscard.Id))
			return Fail(RuleError.DuplicateCard);

		foreach (Card card in declaredCombination!)
		{
			if (!initialHand.Contains(card.Id) &&
				(firstDiscard is null || card.Id != firstDiscard.Id))
				return Fail(RuleError.CardNotInHand);
		}

		if (firstDiscard is not null &&
			!declaredCombination.Any(c => c.Id == firstDiscard.Id))
			return Fail(RuleError.MissingClaimedCard);

		bool matches = kind switch
		{
			InitialVictoryKind.Cuarta => IsCuarta(declaredCombination),

			InitialVictoryKind.LowStraight =>
				IsEscalera(declaredCombination) &&
				HasRanks(declaredCombination, 1, 2, 3, 4),

			InitialVictoryKind.HighStraight =>
				IsEscalera(declaredCombination) &&
				HasRanks(declaredCombination, 11, 12, 13, 1),

			_ => false
		};

		return matches ? Ok() : Fail(RuleError.InvalidInitialDeclaration);
	}

	public static bool IsRed(Card card) =>
		card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds;

	public static bool IsColor(IReadOnlyList<Card> cards) => cards.Count > 0 &&
		cards.All(c => IsRed(c) == IsRed(cards[0]));

	public static bool IsAscendingColor(IReadOnlyList<Card> cards)
	{
		bool Ordered(bool high) => cards.Zip(cards.Skip(1), (a, b) =>
			(high && a.Rank == Rank.Ace ? 14 : (int)a.Rank) <=
			(high && b.Rank == Rank.Ace ? 14 : (int)b.Rank)).All(x => x);
		return Ordered(false) || Ordered(true);
	}

	// La estructura de la tercia/cuarta sigue siendo valida mientras esta abierta
	// la oportunidad de Pasa. El orden de colores es una regla de declaracion aparte.
	public static bool HasAlternatingSetOrder(IReadOnlyList<Card> cards) =>
		!(IsTercia(cards) || IsCuarta(cards)) ||
		cards.Zip(cards.Skip(1), (a, b) => IsRed(a) != IsRed(b)).All(x => x);

	public static Card[] ArrangeAlternatingSet(IReadOnlyList<Card> cards)
	{
		if (!(IsTercia(cards) || IsCuarta(cards)) || HasAlternatingSetOrder(cards)) return cards.ToArray();
		var red = new Queue<Card>(cards.Where(IsRed).OrderBy(c => c.Id));
		var black = new Queue<Card>(cards.Where(c => !IsRed(c)).OrderBy(c => c.Id));
		bool nextRed = red.Count > black.Count || red.Count == black.Count && IsRed(cards[0]);
		var result = new List<Card>();
		while (result.Count < cards.Count)
		{ result.Add((nextRed ? red : black).Dequeue()); nextRed = !nextRed; }
		return result.ToArray();
	}

	private static bool HasRanks(
		IReadOnlyList<Card> cards, params int[] ranks) =>
		cards.Count == ranks.Length &&
		cards.Select(c => (int)c.Rank).ToHashSet().SetEquals(ranks);

	public static bool IsPeladilla(IReadOnlyList<Card> cards)
	{
		// Solo se consideran juegos presentes en la mano, sin importar su orden.
		if (cards.GroupBy(c => c.Rank).Any(g => g.Count() >= 3))
			return false;

		foreach (Suit suit in Enum.GetValues<Suit>())
		{
			var ranks = cards.Where(c => c.Suit == suit)
				.Select(c => (int)c.Rank).ToHashSet();

			// As bajo en 1 y alto en 14; ninguna vuelta K-A-2.
			for (int start = 1; start <= 12; start++)
			{
				int present = 0;

				for (int value = start; value <= start + 2; value++)
				{
					int rank = value == 14 ? 1 : value;

					if (ranks.Contains(rank))
						present++;
				}

				if (present == 3)
					return false;
			}
		}

		return true;
	}

	private static RuleValidationResult ValidateHandCoverage(
		PlayerHand hand,
		IReadOnlyList<IReadOnlyList<Card>> groups)
	{
		Card[] declared = groups.SelectMany(g => g).ToArray();

		if (declared.Any(c => !hand.Contains(c.Id)))
			return Fail(RuleError.CardNotInHand);

		// ValidateGroups ya garantiza ausencia de duplicados.
		return declared.Length == hand.Count
			? Ok()
			: Fail(RuleError.IncompleteHand);
	}

	private static RuleValidationResult ValidateCards(
		IReadOnlyList<Card>? cards)
	{
		if (cards is null)
			return Fail(RuleError.NullInput);

		var ids = new HashSet<int>();

		foreach (Card card in cards)
		{
			if (card is null)
				return Fail(RuleError.NullInput);

			if (!ids.Add(card.Id))
				return Fail(RuleError.DuplicateCard);
		}

		return Ok();
	}

	private static bool SameRank(IReadOnlyList<Card> cards) =>
		cards.All(c => c.Rank == cards[0].Rank);

	private static bool IsStraight(IReadOnlyList<Card> cards)
	{
		if (cards.Count < 3 || cards.Count > 13)
			return false;

		if (cards.Any(c => c.Suit != cards[0].Suit))
			return false;

		return Consecutive(cards, false, 1) ||
			   Consecutive(cards, false, -1) ||
			   Consecutive(cards, true, 1) ||
			   Consecutive(cards, true, -1);
	}

	private static bool Consecutive(
		IReadOnlyList<Card> cards, bool aceHigh, int step)
	{
		int Value(Card card) =>
			aceHigh && card.Rank == Rank.Ace ? 14 : (int)card.Rank;

		for (int i = 1; i < cards.Count; i++)
		{
			if (Value(cards[i]) - Value(cards[i - 1]) != step)
				return false;
		}

		return true;
	}

	private static RuleValidationResult Ok() =>
		new(RuleError.None);

	private static RuleValidationResult Fail(RuleError error) =>
		new(error);
}
