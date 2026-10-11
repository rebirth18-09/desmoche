using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

public sealed class DiscardPile
{
	private readonly Card[] _cards;

	public int Count => _cards.Length;
	public Card? Top => Count == 0 ? null : _cards[^1];

	internal IReadOnlyList<Card> CardsForValidation =>
		Array.AsReadOnly(_cards);

	public DiscardPile() : this(Array.Empty<Card>())
	{
	}

	// El ultimo elemento es el superior.
	public DiscardPile(IEnumerable<Card> cards)
	{
		_cards = Card.CopyDistinct(cards);
	}

	public DiscardPile Add(Card card)
	{
		ArgumentNullException.ThrowIfNull(card);

		return new DiscardPile(_cards.Append(card));
	}

	// Primitiva de datos.
	// No autoriza reclamar ni jugar una carta del pozo.
	public (Card Card, DiscardPile RemainingPile) TakeTop()
	{
		if (Count == 0)
			throw new InvalidOperationException("El pozo esta vacio.");

		return (
			_cards[^1],
			new DiscardPile(_cards[..^1]));
	}
}
