using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

public sealed class PlayerHand
{
	private readonly Card[] _cards;

	public IReadOnlyList<Card> Cards { get; }
	public int Count => _cards.Length;

	public PlayerHand() : this(Array.Empty<Card>())
	{
	}

	public PlayerHand(IEnumerable<Card> cards)
	{
		_cards = Card.CopyDistinct(cards);
		Cards = Array.AsReadOnly(_cards);
	}

	public bool Contains(int cardId)
	{
		return _cards.Any(card => card.Id == cardId);
	}

	public PlayerHand Add(Card card)
	{
		ArgumentNullException.ThrowIfNull(card);

		return new PlayerHand(_cards.Append(card));
	}

	public PlayerHand Remove(int cardId)
	{
		if (!Contains(cardId))
		{
			throw new InvalidOperationException(
				"La carta no pertenece a esta mano.");
		}

		return new PlayerHand(
			_cards.Where(card => card.Id != cardId));
	}
}
