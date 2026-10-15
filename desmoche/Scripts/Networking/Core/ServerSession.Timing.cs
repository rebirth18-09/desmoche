using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;

namespace Desmoche.Networking.Core;

public sealed partial class ServerSession
{
    public const int CardChoiceMilliseconds = 5000;
    public const int CardPreparationMilliseconds = 20000;
    public const int RematchMilliseconds = 15000;
    private readonly bool _multiplayerRematch;
    private readonly Random _automaticRandom;
    private readonly Dictionary<int, long> _reservations = new();
    private readonly HashSet<int> _rematchEligible = new(), _rematchReady = new();
    private readonly Queue<(long Peer, string Reason)> _removedPeers = new();
    private long _selectionDeadline, _discardDeadline, _rematchDeadline;
    private bool _selectionClosed, _rematchClosed;

    public (long Peer, string Reason)[] TakeRemovedPeers()
    {
        var result = _removedPeers.ToArray();
        _removedPeers.Clear();
        return result;
    }

    private bool CanActBeforeDeadline(int actor, ActionKind kind) => kind switch
    {
        ActionKind.Claim => _game.Phase != GamePhase.AwaitingClaims ||
            _reservations.ContainsKey(actor) || !_selectionClosed && _nowMilliseconds < _selectionDeadline,
        ActionKind.DrawDiscard => _discardDeadline == 0 || _nowMilliseconds < _discardDeadline,
        _ => true
    };

    private GameActionResult ChooseOffer(int actor)
    {
        if (!_game.CanChooseOffer(actor) || _reservations.ContainsKey(actor) ||
            _selectionClosed || _nowMilliseconds >= _selectionDeadline)
            return new(false, "No puede elegir esta oferta ahora.");
        if (_game.PossibleOfferPlays(actor).Count == 0)
        {
            _game.PassClaim(actor);
            return new(true, "Esta carta no forma ningun juego valido; se libera para los demas.");
        }
        _reservations.Add(actor, _nowMilliseconds + (_game.Settings.CardPreparationSeconds * 1000));
        return new(true, $"Carta elegida. Tienes {_game.Settings.CardPreparationSeconds} segundos para preparar y confirmar el juego.");
    }

    private GameActionResult ChooseDiscard(int actor)
    {
        if (_discardDeadline == 0 || _nowMilliseconds >= _discardDeadline)
            return new(false, "El plazo para elegir el pozo termino.");
        GameActionResult chosen = _game.ChooseDiscard(actor);
        if (!chosen.Success) return chosen;
        // Esta eleccion ya se hizo dentro del plazo de elección del turno.
        _selectionDeadline = _nowMilliseconds;
        _selectionClosed = true;
        if (_game.PossibleOfferPlays(actor).Count == 0)
        { _game.PassClaim(actor); return new(true, "El pozo no te sirve; la carta sigue disponible en el pozo."); }
        _reservations[actor] = _nowMilliseconds + (_game.Settings.CardPreparationSeconds * 1000);
        return new(true, $"Pozo elegido. Tienes {_game.Settings.CardPreparationSeconds} segundos para preparar el juego.");
    }

    private GameActionResult JoinRematch(int actor)
    {
        if (!_multiplayerRematch || _game.Phase != GamePhase.Finished || _rematchClosed ||
            _nowMilliseconds >= _rematchDeadline || !_rematchEligible.Contains(actor) ||
            !_connections.Values.Any(c => c.PlayerId == actor) || !_rematchReady.Add(actor))
            return new(false, "No puedes apuntarte a esta revancha ahora.");
        return new(true, "Apuntado a la siguiente partida.");
    }

    private bool TickCardTimers()
    {
        bool changed = false;
        if (_game.Phase == GamePhase.AwaitingClaims)
        {
            foreach (int actor in _reservations.Where(p => p.Value <= _nowMilliseconds)
                .Select(p => p.Key).ToArray())
            {
                if (!_game.CanChooseOffer(actor)) continue;
                IReadOnlyList<IReadOnlyList<MeldPlay>> uses = _game.PossibleOfferPlays(actor);
                if (uses.Count == 0) _game.PassClaim(actor);
                else _game.SubmitClaim(actor, uses[_automaticRandom.Next(uses.Count)]);
                changed = true;
            }
            if (!_selectionClosed && _nowMilliseconds >= _selectionDeadline)
            {
                _selectionClosed = true;
                foreach (int actor in _game.Players.Select(p => p.Id).Where(_game.CanChooseOffer))
                    if (!_reservations.ContainsKey(actor)) _game.PassClaim(actor);
                changed = true;
            }
            if (_game.ClaimsReady) { _game.ResolveClaims(); changed = true; }
        }
        else if (_game.Phase == GamePhase.AwaitingDraw && _discardDeadline > 0 &&
            _nowMilliseconds >= _discardDeadline && _game.CurrentPlayerId is int current)
        {
            _game.DeclineDiscardChoice(current);
            changed = true;
        }
        return changed;
    }

    private bool TickRematch()
    {
        if (!_multiplayerRematch || _game.Phase != GamePhase.Finished || _rematchDeadline == 0 ||
            _rematchClosed || _nowMilliseconds < _rematchDeadline) return false;
        _rematchClosed = true;
        int[] returning = _rematchReady.Where(id => _connections.Values.Any(c => c.PlayerId == id))
            .OrderBy(id => id).ToArray();
        foreach (var pair in _connections.Where(p => p.Key != 1 && !returning.Contains(p.Value.PlayerId)).ToArray())
        {
            _connections.Remove(pair.Key);
            _departed.Add(pair.Value.PlayerId);
            _game.DisconnectPlayer(pair.Value.PlayerId);
            _removedPeers.Enqueue((pair.Key, "No te apuntaste antes de terminar el plazo de revancha."));
        }
        if (returning.Length >= 2)
        {
            _game.PrepareNextMatch(returning);
            _departed.Clear();
            _game.StartMatch(_deckFactory?.Invoke());
        }
        return true;
    }

    private void UpdateTimers((GamePhase Phase, int? Current, int? Payment, int? Offer) before)
    {
        bool changed = before != Context();
        // Pasa por oferta sin usar pausa la misma carta, no abre otro plazo
        // de elección. Todas sus respuestas ya se resolvieron antes de denunciar.
        if (_game.Phase == GamePhase.AwaitingOrderChallenge && _game.ChallengeReason == PasaReason.UnusedOffer)
        { _reservations.Clear(); _selectionClosed = true; _discardDeadline = 0; }
        else if (changed)
        {
            // ChooseDiscard establece su reserva antes de este punto: conservarla.
            bool chosenDiscard = before.Phase == GamePhase.AwaitingDraw &&
                _game.Phase == GamePhase.AwaitingClaims && _game.OfferIsFromDiscard &&
                _reservations.ContainsKey(_game.CurrentPlayerId ?? 0);
            if (!chosenDiscard) _reservations.Clear();
            _selectionClosed = chosenDiscard;
            _selectionDeadline = _game.Phase == GamePhase.AwaitingClaims
                ? _nowMilliseconds + (chosenDiscard ? 0 : (_game.Settings.CardChoiceSeconds * 1000)) : 0;
            _discardDeadline = _game.Phase == GamePhase.AwaitingDraw && _game.State?.DiscardTop is not null &&
                !_game.MustDrawDeck ? _nowMilliseconds + (_game.Settings.CardChoiceSeconds * 1000) : 0;
        }
        if (_multiplayerRematch && _game.Phase == GamePhase.Finished && before.Phase != GamePhase.Finished)
        {
            _rematchEligible.Clear(); _rematchReady.Clear();
            foreach (int id in _connections.Values.Select(c => c.PlayerId)
                .Where(id => _game.Players.Any(p => p.Id == id))) _rematchEligible.Add(id);
            _rematchDeadline = _nowMilliseconds + (_game.Settings.RematchSeconds * 1000);
            _rematchClosed = false;
        }
        else if (_game.Phase != GamePhase.Finished)
        { _rematchDeadline = 0; _rematchEligible.Clear(); _rematchReady.Clear(); _rematchClosed = false; }
    }

    private OfferTimingDto? OfferTiming() => _game.Phase != GamePhase.AwaitingClaims ||
        _game.State?.OfferedCard is not Card card ? null : new(card.Id,
        Remaining(_selectionDeadline, (_game.Settings.CardChoiceSeconds * 1000)),
        _reservations.Where(p => _game.CanChooseOffer(p.Key)).OrderBy(p => p.Key)
            .Select(p => new CardReservationDto(p.Key, Remaining(p.Value, (_game.Settings.CardPreparationSeconds * 1000)))).ToArray(),
        _game.OfferIsFromDiscard);
    private DiscardTimingDto? DiscardTiming() => _discardDeadline == 0 ||
        _game.Phase != GamePhase.AwaitingDraw || _game.State?.DiscardTop is not Card card ||
        _game.CurrentPlayerId is not int actor ? null : new(actor, card.Id, Remaining(_discardDeadline, (_game.Settings.CardChoiceSeconds * 1000)));
    private RematchDto? Rematch() => !_multiplayerRematch || _game.Phase != GamePhase.Finished ? null :
        new(_rematchEligible.OrderBy(id => id).ToArray(), _rematchReady.OrderBy(id => id).ToArray(),
            Remaining(_rematchDeadline, (_game.Settings.RematchSeconds * 1000)), _rematchClosed);
    private int Remaining(long deadline, int maximum) => (int)Math.Clamp(deadline - _nowMilliseconds, 0, maximum);
}
