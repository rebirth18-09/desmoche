using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;

namespace Desmoche.Application;

public enum GamePhase
{
	Preparing, InitialExchange, InitialDeclarations,
	AwaitingDraw, AwaitingClaims, AwaitingPayment, Finished
}
public enum MatchResult { None, Desmoche, InitialVictory, Draw }
public readonly record struct GameActionResult(bool Success, string Message,
	bool Penalized = false);
public sealed record MatchView(GamePhase Phase, int? CurrentPlayerId,
	int? PaymentPlayerId, int? WinnerPlayerId, MatchResult Result,
	IReadOnlyList<int> EliminatedPlayers, GameState.PlayerView Cards);

// Motor local secuencial. La futura red ejecutara comandos en este mismo motor.
public sealed class GameManager
{
	private enum OfferSource { Deck, Discard }
	private sealed record Claim(int PlayerId, IReadOnlyList<MeldPlay> Plays,
		bool DeclareWin, InitialVictoryKind? InitialKind, bool Automatic);

	private readonly List<Player> _roster = new();
	private readonly Dictionary<int, int> _exchange = new();
	private readonly HashSet<int> _initialReady = new();
	private readonly HashSet<int> _eliminated = new();
	private readonly HashSet<int> _disconnected = new();
	private readonly Dictionary<int, Claim?> _responses = new();
	private int _starterId;
	private int _originId;
	private OfferSource _source;
	private bool _firstOffer;
	private bool _openingOffer;
	private bool _extraDrawRequired;

	public GameState? State { get; private set; }
	public GamePhase Phase { get; private set; } = GamePhase.Preparing;
	public int? CurrentPlayerId { get; private set; }
	public int? PaymentPlayerId { get; private set; }
	public int? WinnerPlayerId { get; private set; }
	public MatchResult Result { get; private set; }
	public string LastMessage { get; private set; } = "Preparando partida.";
	public IReadOnlyList<int> EliminatedPlayers =>
		Array.AsReadOnly(_eliminated.OrderBy(id => id).ToArray());
	public IReadOnlyList<int> DisconnectedPlayers =>
		Array.AsReadOnly(_disconnected.OrderBy(id => id).ToArray());
	public bool ClaimsReady => Phase == GamePhase.AwaitingClaims &&
		_roster.All(p => !Eligible(p.Id) || _responses.ContainsKey(p.Id));
	public bool HasExchanged(int playerId) => _exchange.ContainsKey(playerId);
	public bool HasFinishedInitial(int playerId) => _initialReady.Contains(playerId);
	public bool HasAnsweredClaim(int playerId) => _responses.ContainsKey(playerId);
	public IReadOnlyList<Player> Players =>
		State?.Players ?? Array.AsReadOnly(_roster.ToArray());

	public GameManager(int creatorId, string creatorName)
	{
		_roster.Add(new Player(creatorId, creatorName, 0));
		_starterId = creatorId;
	}

	public GameActionResult AddPlayer(int id, string? name)
	{
		if (Phase != GamePhase.Preparing)
			return Fail("La partida ya comenzo.");
		if (_roster.Count == 4 || id <= 0 || string.IsNullOrWhiteSpace(name) ||
			_roster.Any(p => p.Id == id))
			return Fail("Jugador invalido, duplicado o sala completa.");
		_roster.Add(new Player(id, name, _roster.Count));
		return Ok("Jugador incorporado.");
	}

	// Solo la sesion autoritativa invoca estas operaciones de conexion.
	public GameActionResult RemovePreparingPlayer(int playerId)
	{
		int index = _roster.FindIndex(p => p.Id == playerId);
		if (Phase != GamePhase.Preparing || index <= 0)
			return Fail("Solo puede retirarse un invitado durante la preparacion.");
		if (_starterId == playerId) _starterId = RightOf(playerId);
		_roster.RemoveAt(index);
		for (int i = 0; i < _roster.Count; i++)
			_roster[i] = new Player(_roster[i].Id, _roster[i].Name, i);
		_disconnected.Remove(playerId);
		_eliminated.Remove(playerId);
		_exchange.Remove(playerId);
		_initialReady.Remove(playerId);
		_responses.Remove(playerId);
		return Ok("Jugador retirado del lobby; asientos actualizados.");
	}

	public GameActionResult DisconnectPlayer(int playerId)
	{
		if (!Known(playerId) || _roster[0].Id == playerId)
			return Fail("Invitado desconocido; la salida del creador cierra la sesion.");
		if (Phase == GamePhase.Preparing) return RemovePreparingPlayer(playerId);
		if (!_disconnected.Add(playerId))
			return Ok("La desconexion ya estaba registrada.");
		_responses.Remove(playerId);
		if (Phase == GamePhase.Finished)
			return Ok("Desconexion registrada tras finalizar.");
		if (_roster.All(p => !Eligible(p.Id)))
		{
			Finish(MatchResult.Draw, null);
			return Ok("No quedan jugadores habilitados.");
		}
		if (Phase == GamePhase.InitialExchange)
		{
			// PROVISIONAL: conserva una seleccion previa; si falta, elige menor Id.
			if (!_exchange.ContainsKey(playerId))
				_exchange[playerId] = State!.GetPlayer(playerId).Hand.Cards.Min(c => c.Id);
			CompleteExchangeIfReady();
		}
		else if (Phase == GamePhase.InitialDeclarations)
		{
			_initialReady.Add(playerId);
			CompleteInitialDeclarationsIfReady();
		}
		else if (Phase == GamePhase.AwaitingPayment && PaymentPlayerId == playerId)
			AutomaticPayment(playerId);
		else if (Phase == GamePhase.AwaitingDraw && CurrentPlayerId == playerId)
			SetPlayableTurn(NextEligible(playerId));
		return Ok("Jugador desconectado; conserva cartas y juegos para pagos automaticos.");
	}

	public GameActionResult StartMatch(Deck? deck = null)
	{
		if (Phase != GamePhase.Preparing || _roster.Count < 2)
			return Fail("Se necesitan de 2 a 4 jugadores y una partida nueva.");
		if (deck is not null && deck.Count != 52)
			return Fail("El mazo debe tener 52 cartas.");
		State = GameState.Create(_roster, deck).DealInitialHands();
		Phase = GamePhase.InitialExchange;
		CurrentPlayerId = null;
		return Ok("Nueve cartas repartidas; falta elegir el intercambio.");
	}

	public GameActionResult SelectExchangeCard(int playerId, int cardId)
	{
		if (Phase != GamePhase.InitialExchange || !Eligible(playerId))
			return Fail("No se permite elegir una carta ahora.");
		if (_exchange.ContainsKey(playerId) || !State!.GetPlayer(playerId).Hand.Contains(cardId))
			return Fail("Seleccion repetida o carta ajena.");
		_exchange.Add(playerId, cardId);
		CompleteExchangeIfReady();

		return Ok("Seleccion recibida; el intercambio se aplica simultaneamente.");
	}

	public GameActionResult FinishInitialDeclarations(int playerId)
	{
		if (Phase != GamePhase.InitialDeclarations || !Eligible(playerId) ||
			!_initialReady.Add(playerId))
			return Fail("Respuesta inicial invalida o repetida.");
		CompleteInitialDeclarationsIfReady();

		return Ok("Jugador listo para comenzar.");
	}

	public GameActionResult DeclareInitialVictory(int playerId, InitialVictoryKind kind,
		IReadOnlyList<int>? cardIds = null)
	{
		if (Phase != GamePhase.InitialDeclarations || !Eligible(playerId) ||
			_initialReady.Contains(playerId))
			return Fail("La declaracion inicial ya no esta disponible.");
		Card[]? cards = null;
		if (cardIds is not null)
		{
			if (!TryCards(State!.GetPlayer(playerId).Hand.Cards, cardIds, out cards))
				return Fail("Cartas iniciales ajenas o repetidas.");
		}
		var validation = RulesValidator.ValidateInitialVictory(
			State!.GetPlayer(playerId).Hand, kind, cards);
		if (!validation.IsValid)
		{
			// Si el problema es solo el orden, se compone el juego y se elimina.
			Card[]? fixedCards = cards is null ? null : Ordered(cards);
			if (fixedCards is not null && RulesValidator.ValidateInitialVictory(
				State.GetPlayer(playerId).Hand, kind, fixedCards).IsValid)
				State = State.ApplyMeldPlays(playerId,
					new[] { new MeldPlay(fixedCards.Select(c => c.Id)) });
			Eliminate(playerId);
			_initialReady.Add(playerId);
			if (Phase != GamePhase.Finished) CompleteInitialDeclarationsIfReady();

			return Fail("Declaracion inicial incorrecta; jugador eliminado.", true);
		}
		if (cards is not null)
			State = State.ApplyMeldPlays(playerId, new[] { new MeldPlay(cardIds!) });
		Finish(MatchResult.InitialVictory, playerId);
		return Ok("Victoria inicial declarada.");
	}

	public GameActionResult DrawFromDeck(int playerId)
	{
		if (Phase != GamePhase.AwaitingDraw || CurrentPlayerId != playerId ||
			!Eligible(playerId))
			return Fail("No es su turno de sacar una carta.");
		if (State!.DeckCount == 0)
			return Fail("Mazo vacio; solo puede reclamarse el pozo si forma un juego.");
		_extraDrawRequired = false;
		State = State.OfferDeckTop();
		OpenClaims(playerId, OfferSource.Deck, _firstOffer);
		_firstOffer = false;
		return Ok("Carta del mazo mostrada para reclamos.");
	}

	// Reclamo del pozo antiguo durante el turno: uso validado antes de retirarlo.
	public GameActionResult DrawFromDiscard(int playerId, IReadOnlyList<MeldPlay> plays,
		bool declareWin = false)
	{
		if (Phase != GamePhase.AwaitingDraw || CurrentPlayerId != playerId ||
			_extraDrawRequired || !Eligible(playerId) || State!.DiscardTop is null)
			return Fail("No puede reclamar el pozo ahora.");
		GameState offered = State.OfferDiscardTop();
		if (!TryClaim(offered, playerId, plays, out _, out _, out string error))
			return Fail(error);
		State = offered;
		_firstOffer = false;
		OpenClaims(playerId, OfferSource.Discard, false);
		// El pozo ya tuvo una ventana publica. Este reclamo es del turno actual.
		return ExecuteClaim(new Claim(playerId, CopyPlays(plays), declareWin, null, false));
	}

	public GameActionResult SubmitClaim(int playerId, IReadOnlyList<MeldPlay> plays,
		bool declareWin = false, InitialVictoryKind? initialKind = null)
	{
		if (Phase != GamePhase.AwaitingClaims || !Eligible(playerId) ||
			_responses.ContainsKey(playerId))
			return Fail("No puede responder a este reclamo.");
		if (initialKind is not null && (!_openingOffer ||
			initialKind == InitialVictoryKind.Color ||
			initialKind == InitialVictoryKind.Peladilla))
			return Fail("Esta victoria inicial no corresponde a la primera carta.");
		if (!TryClaim(State!, playerId, plays, out _, out _, out string error))
			return Fail(error);
		_responses.Add(playerId, new Claim(playerId, CopyPlays(plays),
			declareWin, initialKind, false));
		return Ok("Reclamo registrado; falta resolver la prioridad.");
	}

	public GameActionResult PassClaim(int playerId)
	{
		if (Phase != GamePhase.AwaitingClaims || !Eligible(playerId) ||
			_responses.ContainsKey(playerId))
			return Fail("Respuesta invalida o repetida.");
		_responses.Add(playerId, null);
		return Ok("Jugador no reclama esta carta.");
	}

	// Sin reloj: la simulacion llama esto tras recibir todas las respuestas.
	public GameActionResult ResolveClaims()
	{
		if (Phase != GamePhase.AwaitingClaims)
			return Fail("No hay reclamos pendientes.");
		if (_roster.Any(p => Eligible(p.Id) && !_responses.ContainsKey(p.Id)))
			return Fail("Faltan respuestas; no se resuelve por orden de llegada.");

		foreach (int id in ClaimOrder())
		{
			Claim? claim;
			if (_eliminated.Contains(id) || _disconnected.Contains(id))
			{
				MeldPlay? play = AutomaticPlay(id);
				claim = play is null ? null : new Claim(id, new[] { play }, false, null, true);
			}
			else _responses.TryGetValue(id, out claim);
			if (claim is not null &&
				TryClaim(State!, id, claim.Plays, out _, out _, out _))
				return ExecuteClaim(claim);
		}

		bool extra = _openingOffer && _source == OfferSource.Deck;
		int origin = _originId;
		State = State!.OfferToDiscard();
		_extraDrawRequired = extra && Eligible(origin);
		_responses.Clear();
		Phase = GamePhase.AwaitingDraw;
		SetPlayableTurn(extra && Eligible(origin) ? origin : NextEligible(origin));
		return Ok(extra ? "Primera carta al pozo; el mismo jugador saca otra."
			: "Nadie uso la carta; pasa al pozo y cambia el turno.");
	}

	// Punto de entrada para un futuro temporizador; sin duracion impuesta.
	public GameActionResult ExpireClaims()
	{
		if (Phase != GamePhase.AwaitingClaims) return Fail("No hay ventana pendiente.");
		foreach (Player player in _roster.Where(p => Eligible(p.Id)))
		{
			if (_responses.ContainsKey(player.Id)) continue;
			MeldPlay? play = AutomaticPlay(player.Id);
			_responses[player.Id] = play is null ? null :
				new Claim(player.Id, new[] { play }, false, null, true);
		}
		return ResolveClaims();
	}

	public GameActionResult PublishMelds(int playerId, IReadOnlyList<MeldPlay> plays)
	{
		if (!Playing() || !Eligible(playerId))
			return Fail("No puede bajar juegos ahora.");
		if (!TryPlays(State!, playerId, plays, out GameState next,
			out bool reordered, out string error))
			return Fail(error);
		State = next;
		_responses.Remove(playerId);
		if (reordered) Penalize(playerId);
		return Ok("Juegos bajados.", reordered);
	}

	public GameActionResult TransferMeldCard(int playerId, int sourceId,
		int cardId, MeldPlay destination)
	{
		if (!Playing() || !Eligible(playerId) || destination is null)
			return Fail("No puede reorganizar ahora.");
		Meld? source = State!.Melds.FirstOrDefault(m => m.Id == sourceId &&
			m.OwnerPlayerId == playerId);
		Card? moved = source?.Cards.FirstOrDefault(c => c.Id == cardId);
		if (moved is null) return Fail("Carta donante ajena.");
		IEnumerable<Card> available = State.GetPlayer(playerId).Hand.Cards.Append(moved);
		if (destination.MeldId is int targetId)
		{
			Meld? target = State.Melds.FirstOrDefault(m => m.Id == targetId &&
				m.OwnerPlayerId == playerId);
			if (target is null || targetId == sourceId) return Fail("Destino invalido.");
			available = available.Concat(target.Cards);
		}
		if (!TryCards(available, destination.CardIds, out Card[] cards))
			return Fail("Cartas de destino ajenas o repetidas.");
		Card[]? ordered = Ordered(cards);
		if (ordered is null) return Fail("El destino no forma un juego.");
		bool reordered = !cards.SequenceEqual(ordered);
		try
		{
			GameState next = State.TransferMeldCard(playerId, sourceId, cardId,
				new MeldPlay(ordered.Select(c => c.Id), destination.MeldId));
			State = next;
			_responses.Remove(playerId);
			if (reordered) Penalize(playerId);
			return Ok("Carta trasladada; el donante conserva su juego.", reordered);
		}
		catch (ArgumentException e) { return Fail(e.Message); }
		catch (InvalidOperationException e) { return Fail(e.Message); }
	}

	public GameActionResult DeclareDesmoche(int playerId,
		IReadOnlyList<MeldPlay>? remainingPlays = null)
	{
		if (Phase != GamePhase.AwaitingPayment || PaymentPlayerId != playerId ||
			!Eligible(playerId)) return Fail("No es el momento de declarar desmoche.");
		GameState candidate = State!;
		bool reordered = false;
		if (remainingPlays is not null && remainingPlays.Count > 0)
		{
			if (!TryPlays(candidate, playerId, remainingPlays, out candidate,
				out reordered, out string error)) return Fail(error);
		}
		State = candidate;
		if (reordered || !Winning(candidate, playerId))
		{
			Eliminate(playerId);
			if (Phase != GamePhase.Finished) AutomaticPayment(playerId);
			return Fail("Declaracion de desmoche incorrecta; jugador eliminado.", true);
		}
		Finish(MatchResult.Desmoche, playerId);
		return Ok("Desmoche declarado: diez combinadas, ninguna suelta.");
	}

	public GameActionResult PayCard(int playerId, int cardId)
	{
		if (Phase != GamePhase.AwaitingPayment || PaymentPlayerId != playerId ||
			!Eligible(playerId) || !State!.GetPlayer(playerId).Hand.Contains(cardId))
			return Fail("Pago fuera de momento o carta ajena.");
		State = State.MoveHandCardToDiscard(playerId, cardId).OfferDiscardTop();
		PaymentPlayerId = null;
		OpenClaims(playerId, OfferSource.Discard, false);
		return Ok("Carta pagada mostrada para reclamos.");
	}

	public GameActionResult CompleteWithoutPayment(int playerId)
	{
		if (Phase != GamePhase.AwaitingPayment || PaymentPlayerId != playerId ||
			!Eligible(playerId) || State!.GetPlayer(playerId).Hand.Count != 0)
			return Fail("Solo se omite el pago cuando la mano esta vacia.");
		PaymentPlayerId = null;
		Phase = GamePhase.AwaitingDraw;
		SetPlayableTurn(NextEligible(playerId));
		return Ok("Sin cartas para pagar; no se adjudica victoria automaticamente.");
	}

	public MatchView GetViewFor(int playerId)
	{
		if (State is null) throw new InvalidOperationException("Falta iniciar la partida.");
		return new MatchView(Phase, CurrentPlayerId, PaymentPlayerId, WinnerPlayerId,
			Result, EliminatedPlayers, State.CreateViewFor(playerId, WinnerPlayerId is not null));
	}

	public GameActionResult PrepareNextMatch()
	{
		if (Phase != GamePhase.Finished) return Fail("Primero debe terminar la partida.");
		_starterId = RightOf(_starterId);
		State = null;
		Phase = GamePhase.Preparing;
		CurrentPlayerId = PaymentPlayerId = WinnerPlayerId = null;
		Result = MatchResult.None;
		_exchange.Clear(); _initialReady.Clear(); _eliminated.Clear(); _responses.Clear();
		_disconnected.Clear();
		_firstOffer = _openingOffer = _extraDrawRequired = false;
		return Ok("Nueva partida preparada; inicia el siguiente a la derecha.");
	}

	private void CompleteExchangeIfReady()
	{
		if (Phase != GamePhase.InitialExchange || _exchange.Count != _roster.Count) return;
		State = State!.ExchangeInitialCards(_exchange);
		Phase = GamePhase.InitialDeclarations;
		foreach (int id in _disconnected) _initialReady.Add(id);
		CompleteInitialDeclarationsIfReady();
	}

	private void CompleteInitialDeclarationsIfReady()
	{
		if (Phase != GamePhase.InitialDeclarations || _initialReady.Count != _roster.Count) return;
		_firstOffer = true;
		Phase = GamePhase.AwaitingDraw;
		SetPlayableTurn(_starterId);
	}

	private GameActionResult ExecuteClaim(Claim claim)
	{
		if (!TryClaim(State!, claim.PlayerId, claim.Plays, out GameState next,
			out bool reordered, out string error)) return Fail(error);
		bool initialValid = false;
		if (claim.InitialKind is InitialVictoryKind kind)
		{
			Card[] declared = claim.Plays.SelectMany(p => p.CardIds)
				.Select(Card.FromId).ToArray();
			initialValid = claim.Plays.Count == 1 &&
				claim.Plays[0].MeldId is null &&
				RulesValidator.ValidateInitialVictory(
					State!.GetPlayer(claim.PlayerId).Hand, kind, declared,
					State.OfferedCard).IsValid;
		}
		bool badWin = claim.InitialKind is not null ? !initialValid :
			claim.DeclareWin && !Winning(next, claim.PlayerId);
		State = next;
		_responses.Clear();
		_openingOffer = false;
		if (reordered || badWin) Eliminate(claim.PlayerId);
		if (Phase == GamePhase.Finished) return Ok("No quedan jugadores habilitados.", true);
		if (Eligible(claim.PlayerId) && !reordered && !badWin &&
			(initialValid || claim.DeclareWin))
		{
			Finish(initialValid ? MatchResult.InitialVictory : MatchResult.Desmoche,
				claim.PlayerId);
			return Ok("Victoria declarada; no hay pago final.");
		}
		Phase = GamePhase.AwaitingPayment;
		CurrentPlayerId = PaymentPlayerId = claim.PlayerId;
		if (claim.Automatic || _eliminated.Contains(claim.PlayerId) ||
			_disconnected.Contains(claim.PlayerId))
			AutomaticPayment(claim.PlayerId);
		return Ok("Carta jugada; corresponde pagar salvo victoria.",
			reordered || badWin);
	}

	private void AutomaticPayment(int playerId)
	{
		if (Phase == GamePhase.Finished) return;
		PlayerHand hand = State!.GetPlayer(playerId).Hand;
		PaymentPlayerId = null;
		if (hand.Count == 0)
		{
			Phase = GamePhase.AwaitingDraw;
			SetPlayableTurn(NextEligible(playerId));
		}
		else
		{
			// PROVISIONAL: pago automatico con la carta de menor Id.
			int id = hand.Cards.Min(c => c.Id);
			State = State.MoveHandCardToDiscard(playerId, id).OfferDiscardTop();
			OpenClaims(playerId, OfferSource.Discard, false);
		}
	}

	private MeldPlay? AutomaticPlay(int playerId)
	{
		Card? offered = State!.OfferedCard;
		if (offered is null) return null;
		// PROVISIONAL: si cabe en varios juegos, se elige el menor Id.
		foreach (Meld meld in State.Melds.Where(m => m.OwnerPlayerId == playerId)
			.OrderBy(m => m.Id))
		{
			Card[]? ordered = Ordered(meld.Cards.Append(offered).ToArray());
			if (ordered is not null) return new MeldPlay(ordered.Select(c => c.Id), meld.Id);
		}
		return null;
	}

	private static bool TryClaim(GameState state, int playerId,
		IReadOnlyList<MeldPlay> plays, out GameState next, out bool reordered,
		out string error)
	{
		next = state; reordered = false; error = "";
		if (state.OfferedCard is null || plays is null ||
			!plays.SelectMany(p => p?.CardIds ?? Array.Empty<int>())
				.Contains(state.OfferedCard.Id))
		{
			error = "La carta reclamada debe usarse en un juego.";
			return false;
		}
		return TryPlays(state.OfferToHand(playerId), playerId, plays,
			out next, out reordered, out error);
	}

	private static bool TryPlays(GameState state, int playerId,
		IReadOnlyList<MeldPlay> plays, out GameState next, out bool reordered,
		out string error)
	{
		next = state; reordered = false; error = "";
		if (plays is null || plays.Count == 0 || plays.Any(p => p is null))
		{ error = "Faltan juegos."; return false; }
		var corrected = new List<MeldPlay>();
		foreach (MeldPlay play in plays)
		{
			IEnumerable<Card> available = state.GetPlayer(playerId).Hand.Cards;
			if (play.MeldId is int id)
			{
				Meld? old = state.Melds.FirstOrDefault(m => m.Id == id &&
					m.OwnerPlayerId == playerId);
				if (old is null) { error = "Juego ajeno o inexistente."; return false; }
				available = available.Concat(old.Cards);
			}
			if (!TryCards(available, play.CardIds, out Card[] cards))
			{ error = "Cartas ajenas o duplicadas."; return false; }
			Card[]? ordered = Ordered(cards);
			if (ordered is null) { error = "Las cartas no forman un juego."; return false; }
			reordered |= !cards.Select(c => c.Id).SequenceEqual(ordered.Select(c => c.Id));
			corrected.Add(new MeldPlay(ordered.Select(c => c.Id), play.MeldId));
		}
		try { next = state.ApplyMeldPlays(playerId, corrected); return true; }
		catch (ArgumentException e) { error = e.Message; return false; }
		catch (InvalidOperationException e) { error = e.Message; return false; }
	}

	private static bool TryCards(IEnumerable<Card> available, IReadOnlyList<int> ids,
		out Card[] cards)
	{
		cards = Array.Empty<Card>();
		if (ids is null || ids.Count != ids.Distinct().Count()) return false;
		Dictionary<int, Card> byId = available.ToDictionary(c => c.Id);
		if (ids.Any(id => !byId.ContainsKey(id))) return false;
		cards = ids.Select(id => byId[id]).ToArray();
		return true;
	}

	private static Card[]? Ordered(Card[] cards)
	{
		if (RulesValidator.ValidateMeld(cards).IsValid) return cards;
		Card[] low = cards.OrderBy(c => (int)c.Rank).ToArray();
		if (RulesValidator.ValidateMeld(low).IsValid) return low;
		Card[] high = cards.OrderBy(c => c.Rank == Rank.Ace ? 14 : (int)c.Rank).ToArray();
		return RulesValidator.ValidateMeld(high).IsValid ? high : null;
	}

	private static IReadOnlyList<MeldPlay> CopyPlays(IReadOnlyList<MeldPlay> plays) =>
		Array.AsReadOnly(plays.Select(p => new MeldPlay(p.CardIds, p.MeldId)).ToArray());

	private static bool Winning(GameState state, int id) =>
		RulesValidator.ValidateDesmoche(state.GetPlayer(id).Hand,
			state.Melds.Where(m => m.OwnerPlayerId == id).Select(m => m.Cards).ToArray(),
			Array.Empty<IReadOnlyList<Card>>()).IsValid;

	private void OpenClaims(int origin, OfferSource source, bool opening)
	{
		_originId = origin; _source = source; _openingOffer = opening;
		_responses.Clear();
		Phase = GamePhase.AwaitingClaims;
		CurrentPlayerId = origin;
		PaymentPlayerId = null;
	}

	private IEnumerable<int> ClaimOrder()
	{
		int start = _source == OfferSource.Deck ? _originId : RightOf(_originId);
		int id = start;
		for (int i = 0; i < _roster.Count; i++) { yield return id; id = RightOf(id); }
	}

	private int RightOf(int id)
	{
		int index = _roster.FindIndex(p => p.Id == id);
		return _roster[(index + 1) % _roster.Count].Id;
	}
	private int NextEligible(int origin)
	{
		int id = origin;
		for (int i = 0; i < _roster.Count; i++)
		{ id = RightOf(id); if (Eligible(id)) return id; }
		return origin;
	}

	private void SetPlayableTurn(int start)
	{
		int id = start;
		for (int i = 0; i < _roster.Count; i++)
		{
			if (Eligible(id) && (State!.DeckCount > 0 ||
				State.DiscardTop is Card top && CanUseDiscard(id, top)))
			{ CurrentPlayerId = id; return; }
			id = RightOf(id);
		}
		Finish(MatchResult.Draw, null);
	}

	private bool CanUseDiscard(int id, Card top)
	{
		foreach (Meld meld in State!.Melds.Where(m => m.OwnerPlayerId == id))
			if (Ordered(meld.Cards.Append(top).ToArray()) is not null) return true;
		var hand = State.GetPlayer(id).Hand.Cards;
		for (int i = 0; i < hand.Count; i++)
			for (int j = i + 1; j < hand.Count; j++)
				if (Ordered(new[] { hand[i], hand[j], top }) is not null) return true;
		return false;
	}

	private void Penalize(int id)
	{
		Eliminate(id);
		if (Phase == GamePhase.AwaitingPayment && PaymentPlayerId == id)
			AutomaticPayment(id);
		else if (Phase == GamePhase.AwaitingDraw && CurrentPlayerId == id)
			SetPlayableTurn(NextEligible(id));
	}

	private void Eliminate(int id)
	{
		_eliminated.Add(id);
		if (_roster.All(p => !Eligible(p.Id))) Finish(MatchResult.Draw, null);
	}
	private void Finish(MatchResult result, int? winner)
	{
		if (State?.OfferedCard is not null) State = State.OfferToDiscard();
		Phase = GamePhase.Finished;
		Result = result; WinnerPlayerId = winner;
		CurrentPlayerId = PaymentPlayerId = null;
		_responses.Clear();
	}

	private bool Playing() => Phase == GamePhase.AwaitingDraw ||
		Phase == GamePhase.AwaitingClaims || Phase == GamePhase.AwaitingPayment;
	private bool Known(int id) => _roster.Any(p => p.Id == id);
	private bool Eligible(int id) => Known(id) && !_eliminated.Contains(id) &&
		!_disconnected.Contains(id);
	private GameActionResult Ok(string message, bool penalized = false)
	{ LastMessage = message; return new(true, message, penalized); }
	private GameActionResult Fail(string message, bool penalized = false)
	{ LastMessage = message; return new(false, message, penalized); }
}
