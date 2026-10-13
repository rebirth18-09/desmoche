using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

// Estado inmutable de cartas. El GameManager autoriza los movimientos.
// La carta ofrecida es una zona publica temporal y tambien cuenta entre las 52.
public sealed class GameState
{
	public const int InitialHandSize = 9;
	private readonly Player[] _players;
	private readonly Deck _deck;
	private readonly DiscardPile _discardPile;
	private readonly Meld[] _melds;
	private readonly int _nextMeldId;
	private readonly Dictionary<int, Card> _exchangeCards;
	public int ExchangeCount => _exchangeCards.Count;

	// Estado completo: solo codigo confiable. Para presentacion usar una vista.
	public IReadOnlyList<Player> Players { get; }
	public IReadOnlyList<Meld> Melds { get; }
	public Card? OfferedCard { get; }
	public int DeckCount => _deck.Count;
	public int DiscardCount => _discardPile.Count;
	public Card? DiscardTop => _discardPile.Top;
	public bool InitialHandsDealt { get; }

	private GameState(Player[] players, Deck deck, DiscardPile discardPile,
		bool dealt, Meld[]? melds = null, Card? offered = null, int nextMeldId = 1, IReadOnlyDictionary<int, Card>? exchangeCards = null)
	{
		_players = (Player[])players.Clone();
		_deck = deck;
		_discardPile = discardPile;
		_melds = melds is null ? Array.Empty<Meld>() : (Meld[])melds.Clone();
		_nextMeldId = nextMeldId;
		_exchangeCards = exchangeCards is null ? new() : new(exchangeCards);
		OfferedCard = offered;
		InitialHandsDealt = dealt;
		Players = Array.AsReadOnly(_players);
		Melds = Array.AsReadOnly(_melds);

		if (_melds.Select(m => m.Id).Distinct().Count() != _melds.Length ||
			_melds.Any(m => !_players.Any(p => p.Id == m.OwnerPlayerId) ||
				m.Id >= nextMeldId))
			throw new InvalidOperationException("Identidad de juegos inconsistente.");

		var zones = _deck.CardsForValidation
			.Concat(_discardPile.CardsForValidation)
			.Concat(_players.SelectMany(p => p.Hand.Cards))
			.Concat(_exchangeCards.Values)
			.Concat(_melds.SelectMany(m => m.Cards))
			.Concat(offered is null ? Array.Empty<Card>() : new[] { offered });
		if (Card.CopyDistinct(zones).Length != Card.StandardDeckSize)
			throw new InvalidOperationException("El estado debe conservar las 52 cartas.");
	}

	public static GameState Create(IEnumerable<Player> players, Deck? deck = null)
	{
		ArgumentNullException.ThrowIfNull(players);
		Player[] roster = players.ToArray();
		if (roster.Length < 2 || roster.Length > 4 ||
			roster.Any(p => p is null))
			throw new ArgumentException("Se necesitan entre 2 y 4 jugadores validos.");
		if (roster.Select(p => p.Id).Distinct().Count() != roster.Length ||
			roster.Select(p => p.SeatIndex).Distinct().Count() != roster.Length)
			throw new ArgumentException("Ids y asientos deben ser unicos.");
		if (roster.Any(p => p.Hand.Count != 0))
			throw new ArgumentException("La partida nueva requiere manos vacias.");
		Deck initial = deck ?? Deck.CreateStandard().Shuffle();
		if (initial.Count != 52)
			throw new ArgumentException("La partida nueva requiere un mazo completo.");
		return new GameState(roster.OrderBy(p => p.SeatIndex).ToArray(),
			initial, new DiscardPile(), false);
	}

	public GameState DealInitialHands()
	{
		if (InitialHandsDealt)
			throw new InvalidOperationException("Ya se repartieron las manos.");
		Deck deck = _deck;
		PlayerHand[] hands = _players.Select(p => p.Hand).ToArray();
		for (int round = 0; round < InitialHandSize; round++)
			for (int i = 0; i < hands.Length; i++)
			{
				var draw = deck.Draw();
				deck = draw.RemainingDeck;
				hands[i] = hands[i].Add(draw.Card);
			}
		return Copy(players: _players.Select((p, i) => p.WithHand(hands[i])).ToArray(),
			deck: deck, dealt: true);
	}

	public Player GetPlayer(int playerId) => _players[FindPlayerIndex(playerId)];

	// Primitivas compatibles con fase 2; no autorizan turnos.
	public (Card Card, GameState State) MoveDeckTopToHand(int playerId)
	{
		EnsureDealt();
		var draw = _deck.Draw();
		return (draw.Card, Copy(players: WithHand(playerId,
			GetPlayer(playerId).Hand.Add(draw.Card)), deck: draw.RemainingDeck));
	}

	public GameState MoveHandCardToDiscard(int playerId, int cardId)
	{
		EnsureDealt();
		Card card = GetPlayer(playerId).Hand.Cards.FirstOrDefault(c => c.Id == cardId)
			?? throw new InvalidOperationException("La carta no pertenece al jugador.");
		return Copy(players: WithHand(playerId, GetPlayer(playerId).Hand.Remove(cardId)),
			pile: _discardPile.Add(card));
	}

	internal GameState ReserveExchangeCard(int playerId, int cardId)
	{
		EnsureDealt();
		PlayerHand hand = GetPlayer(playerId).Hand;
		if (_exchangeCards.ContainsKey(playerId) || hand.Count != InitialHandSize || _melds.Length != 0)
			throw new InvalidOperationException("Intercambio inicial invalido.");
		Card card = hand.Cards.Single(c => c.Id == cardId);
		var held = new Dictionary<int, Card>(_exchangeCards) { [playerId] = card };
		return Copy(players: WithHand(playerId, hand.Remove(cardId)), exchangeCards: held);
	}

	internal GameState CompleteInitialExchange()
	{
		if (_exchangeCards.Count != _players.Length)
			throw new InvalidOperationException("Faltan cartas para completar el intercambio.");
		Player[] players = _players.Select((p, i) => p.WithHand(p.Hand.Add(
			_exchangeCards[_players[(i + _players.Length - 1) % _players.Length].Id]))).ToArray();
		return Copy(players: players, exchangeCards: new Dictionary<int, Card>());
	}

	internal GameState CancelInitialExchange() => Copy(
		players: _players.Select(p => _exchangeCards.TryGetValue(p.Id, out Card? card)
			? p.WithHand(p.Hand.Add(card)) : p).ToArray(), exchangeCards: new Dictionary<int, Card>());

	internal GameState NormalizeSetOrder(int playerId) => Copy(melds: _melds.Select(m =>
		m.OwnerPlayerId == playerId ? new Meld(m.Id, playerId,
			RulesValidator.ArrangeAlternatingSet(m.Cards)) : m).ToArray());

	internal GameState OfferDeckTop()
	{
		EnsureDealt();
		EnsureNoOffer();
		var draw = _deck.Draw();
		return Copy(deck: draw.RemainingDeck, offered: draw.Card, replaceOffer: true);
	}

	internal GameState OfferDiscardTop()
	{
		EnsureDealt();
		EnsureNoOffer();
		var take = _discardPile.TakeTop();
		return Copy(pile: take.RemainingPile, offered: take.Card, replaceOffer: true);
	}

	internal GameState OfferToDiscard() => Copy(
		pile: _discardPile.Add(RequireOffer()), replaceOffer: true);

	internal GameState OfferToHand(int playerId) => Copy(
		players: WithHand(playerId, GetPlayer(playerId).Hand.Add(RequireOffer())),
		replaceOffer: true);

	internal GameState ApplyMeldPlays(int playerId, IReadOnlyList<MeldPlay> plays)
	{
		PlayerHand hand = GetPlayer(playerId).Hand;
		var melds = _melds.ToList();
		var seenCards = new HashSet<int>();
		var seenMelds = new HashSet<int>();
		int nextId = _nextMeldId;
		foreach (MeldPlay play in plays)
		{
			Meld? old = null;
			if (play.MeldId is int existingId)
			{
				old = _melds.FirstOrDefault(m => m.Id == existingId && m.OwnerPlayerId == playerId)
					?? throw new InvalidOperationException("Juego ajeno o inexistente.");
				if (!seenMelds.Add(existingId) || old.Cards.Any(c => !play.CardIds.Contains(c.Id)))
					throw new InvalidOperationException("Una extension conserva el juego anterior.");
			}
			var cards = new List<Card>();
			foreach (int id in play.CardIds)
			{
				if (!seenCards.Add(id))
					throw new InvalidOperationException("Carta repetida entre juegos.");
				Card? previous = old?.Cards.FirstOrDefault(c => c.Id == id);
				if (previous is not null) cards.Add(previous);
				else
				{
					Card card = hand.Cards.FirstOrDefault(c => c.Id == id)
						?? throw new InvalidOperationException("Carta ajena al jugador.");
					cards.Add(card);
					hand = hand.Remove(id);
				}
			}
			var updated = new Meld(old?.Id ?? nextId++, playerId, cards);
			if (old is null) melds.Add(updated);
			else melds[melds.FindIndex(m => m.Id == old.Id)] = updated;
		}
		return Copy(players: WithHand(playerId, hand), melds: melds.ToArray(),
			nextMeldId: nextId);
	}

	internal GameState TransferMeldCard(int playerId, int sourceId, int cardId,
		MeldPlay destination)
	{
		Meld source = _melds.FirstOrDefault(m => m.Id == sourceId &&
			m.OwnerPlayerId == playerId)
			?? throw new InvalidOperationException("Juego donante ajeno.");
		Card[] remainder = source.Cards.Where(c => c.Id != cardId).ToArray();
		if (remainder.Length == source.Cards.Count ||
			!RulesValidator.ValidateMeld(remainder).IsValid ||
			destination.MeldId == sourceId)
			throw new InvalidOperationException("El donante debe conservar un juego valido.");
		// Paso intermedio valido: la carta pasa por la mano solo dentro de esta transaccion.
		var reduced = _melds.Select(m => m.Id == sourceId
			? new Meld(m.Id, playerId, remainder) : m).ToArray();
		Card moved = source.Cards.Single(c => c.Id == cardId);
		GameState staging = Copy(melds: reduced,
			players: WithHand(playerId, GetPlayer(playerId).Hand.Add(moved)));
		if (!destination.CardIds.Contains(cardId))
			throw new InvalidOperationException("El destino debe usar la carta transferida.");
		return staging.ApplyMeldPlays(playerId, new[] { destination });
	}

	public PlayerView CreateViewFor(int playerId, bool revealAllHands = false)
	{
		Player player = GetPlayer(playerId);
		return new PlayerView(player.Id, player.Hand.Cards,
			_players.Select(p => new PlayerSummary(p.Id, p.Name, p.SeatIndex, p.Hand.Count)),
			DeckCount, DiscardCount, DiscardTop, OfferedCard, Melds,
			revealAllHands ? _players.Select(p => new RevealedHand(p.Id,
				Array.AsReadOnly(p.Hand.Cards.ToArray()))) : Array.Empty<RevealedHand>());
	}

	private GameState Copy(Player[]? players = null, Deck? deck = null,
		DiscardPile? pile = null, Meld[]? melds = null, bool? dealt = null,
		Card? offered = null, bool replaceOffer = false, int? nextMeldId = null,
		IReadOnlyDictionary<int, Card>? exchangeCards = null) =>
		new(players ?? _players, deck ?? _deck, pile ?? _discardPile,
			dealt ?? InitialHandsDealt, melds ?? _melds,
			replaceOffer ? offered : OfferedCard, nextMeldId ?? _nextMeldId, exchangeCards ?? _exchangeCards);

	private Player[] WithHand(int id, PlayerHand hand)
	{
		Player[] players = (Player[])_players.Clone();
		int index = FindPlayerIndex(id);
		players[index] = players[index].WithHand(hand);
		return players;
	}

	private int FindPlayerIndex(int id)
	{
		int index = Array.FindIndex(_players, p => p.Id == id);
		return index >= 0 ? index : throw new KeyNotFoundException("El jugador no existe.");
	}
	private void EnsureDealt()
	{
		if (!InitialHandsDealt) throw new InvalidOperationException("Falta repartir.");
	}
	private void EnsureNoOffer()
	{
		if (OfferedCard is not null) throw new InvalidOperationException("Hay una carta pendiente.");
	}
	private Card RequireOffer() => OfferedCard
		?? throw new InvalidOperationException("No hay carta ofrecida.");

	public sealed record PlayerSummary(int Id, string Name, int SeatIndex, int CardCount);
	public sealed record RevealedHand(int PlayerId, IReadOnlyList<Card> Cards);

	public sealed class PlayerView
	{
		public int RecipientPlayerId { get; }
		public IReadOnlyList<Card> OwnHand { get; }
		public IReadOnlyList<PlayerSummary> Players { get; }
		public int DeckCount { get; }
		public int DiscardCount { get; }
		public Card? DiscardTop { get; }
		public Card? OfferedCard { get; }
		public IReadOnlyList<Meld> Melds { get; }
		public IReadOnlyList<RevealedHand> RevealedHands { get; }

		internal PlayerView(int id, IEnumerable<Card> hand,
			IEnumerable<PlayerSummary> players, int deckCount, int discardCount,
			Card? top, Card? offered, IEnumerable<Meld> melds,
			IEnumerable<RevealedHand> revealed)
		{
			RecipientPlayerId = id;
			OwnHand = Array.AsReadOnly(hand.ToArray());
			Players = Array.AsReadOnly(players.ToArray());
			DeckCount = deckCount;
			DiscardCount = discardCount;
			DiscardTop = top;
			OfferedCard = offered;
			Melds = Array.AsReadOnly(melds.ToArray());
			RevealedHands = Array.AsReadOnly(revealed.ToArray());
		}
	}
}
