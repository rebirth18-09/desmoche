using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

public sealed record Card
{
	public const int StandardDeckSize = 52;

	public Suit Suit { get; }
	public Rank Rank { get; }

	public int Id => (int)Suit * 13 + (int)Rank - 1;

	public Card(Suit suit, Rank rank)
	{
		if (!Enum.IsDefined(suit))
			throw new ArgumentOutOfRangeException(nameof(suit));

		if (!Enum.IsDefined(rank))
			throw new ArgumentOutOfRangeException(nameof(rank));

		Suit = suit;
		Rank = rank;
	}

	public static Card FromId(int id)
	{
		if (id < 0 || id >= StandardDeckSize)
			throw new ArgumentOutOfRangeException(nameof(id));

		return new Card(
			(Suit)(id / 13),
			(Rank)(id % 13 + 1));
	}

	// Copia defensiva comun a las zonas.
	internal static Card[] CopyDistinct(IEnumerable<Card> cards)
	{
		ArgumentNullException.ThrowIfNull(cards);

		Card[] copy = cards.ToArray();
		var ids = new HashSet<int>();

		foreach (Card card in copy)
		{
			if (card is null)
			{
				throw new ArgumentException(
					"Una zona no admite cartas nulas.",
					nameof(cards));
			}

			if (!ids.Add(card.Id))
			{
				throw new ArgumentException(
					"Una zona no admite cartas duplicadas.",
					nameof(cards));
			}
		}

		return copy;
	}
}
