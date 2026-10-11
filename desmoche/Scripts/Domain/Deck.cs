using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Desmoche.Domain;

public sealed class Deck
{
	private readonly Card[] _cards;

	public int Count => _cards.Length;

	internal IReadOnlyList<Card> CardsForValidation =>
		Array.AsReadOnly(_cards);

	// El ultimo elemento es el superior.
	public Deck(IEnumerable<Card> cards)
	{
		_cards = Card.CopyDistinct(cards);
	}

	public static Deck CreateStandard()
	{
		return new Deck(
			Enumerable.Range(0, Card.StandardDeckSize)
				.Select(Card.FromId));
	}

	public Deck Shuffle(Random? random = null)
	{
		Card[] shuffled = (Card[])_cards.Clone();

		for (int i = shuffled.Length - 1; i > 0; i--)
		{
			int j = random is null
				? RandomNumberGenerator.GetInt32(i + 1)
				: random.Next(i + 1);

			(shuffled[i], shuffled[j]) =
				(shuffled[j], shuffled[i]);
		}

		return new Deck(shuffled);
	}

	public (Card Card, Deck RemainingDeck) Draw()
	{
		if (Count == 0)
			throw new InvalidOperationException(
				"El mazo esta vacio.");

		return (
			_cards[^1],
			new Deck(_cards[..^1]));
	}
}
