using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;

namespace Desmoche.Networking.Core;

// Listas explicitas: ningun campo privado puede entrar por serializacion del dominio.
public sealed record PublicSnapshotDto(int Version, string Session, long Revision,
    long Window, GamePhase Phase, MatchResult Result, int? CurrentPlayerId,
    int? PaymentPlayerId, int? WinnerPlayerId, PublicPlayerDto[] Players,
    MeldDto[] Melds, int DeckCount, int DiscardCount, int? DiscardTop,
    int? OfferedCard, RevealedHandDto[] RevealedHands, int ExchangeCount = 0,
    OrderChallengeDto? OrderChallenge = null, bool OpeningOffer = false, InitialVictoryDto? InitialVictory = null);

public sealed record PrivateSnapshotDto(int Version, string Session, long Revision,
    int RecipientId, int[] OwnHand, bool OwnResponseSubmitted);

public static class StateProjection
{
    public static PublicSnapshotDto Public(SnapshotDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PublicSnapshotDto(source.Version, source.Session, source.Revision,
            source.Window, source.Phase, source.Result, source.CurrentPlayerId,
            source.PaymentPlayerId, source.WinnerPlayerId, source.Players.ToArray(),
            CopyMelds(source.Melds), source.DeckCount, source.DiscardCount,
            source.DiscardTop, source.OfferedCard, CopyRevealed(source.RevealedHands),
            source.ExchangeCount, source.OrderChallenge, source.OpeningOffer, CopyInitial(source.InitialVictory));
    }

    public static PrivateSnapshotDto Private(SnapshotDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PrivateSnapshotDto(source.Version, source.Session, source.Revision,
            source.RecipientId, source.OwnHand.ToArray(), source.OwnResponseSubmitted);
    }

    public static bool IsValidPublic(PublicSnapshotDto? value)
    {
        if (value is null || value.Version != NetworkProtocol.Version ||
            !ValidSession(value.Session) || value.Revision <= 0 || value.Window <= 0 ||
            !Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Result) ||
            value.Players is null || value.Players.Length is < 1 or > 4 ||
            value.Melds is null || value.Melds.Length > 17 ||
            value.RevealedHands is null || value.RevealedHands.Length > 4 ||
            value.DeckCount is < 0 or > 52 || value.DiscardCount is < 0 or > 52 ||
            !ValidOptionalCard(value.DiscardTop) || !ValidOptionalCard(value.OfferedCard) ||
            (value.DiscardCount == 0) != (value.DiscardTop is null))
            return false;

        var playerIds = new HashSet<int>();
        var seats = new HashSet<int>();
        foreach (PublicPlayerDto p in value.Players)
            if (p is null || p.Id <= 0 || !NetworkProtocol.ValidName(p.Name) ||
                p.SeatIndex is < 0 or > 3 || p.CardCount is < 0 or > 52 ||
                !playerIds.Add(p.Id) || !seats.Add(p.SeatIndex)) return false;
        if (!KnownPlayer(value.CurrentPlayerId, playerIds) ||
            !KnownPlayer(value.PaymentPlayerId, playerIds) ||
            !KnownPlayer(value.WinnerPlayerId, playerIds)) return false;

        if (value.ExchangeCount is < 0 or > 4 ||
            value.ExchangeCount > 0 && value.Phase != GamePhase.InitialExchange ||
            value.OpeningOffer && value.Phase != GamePhase.AwaitingClaims ||
            (value.Phase == GamePhase.AwaitingOrderChallenge) != (value.OrderChallenge is not null)) return false;
        if (value.OrderChallenge is { } challenge && (!playerIds.Contains(challenge.PlayerId) ||
            challenge.Id <= 0 || challenge.RemainingMilliseconds is < 0 or > ServerSession.PasaDurationMilliseconds)) return false;
        bool won = value.Result is MatchResult.Desmoche or MatchResult.InitialVictory;
        if ((value.Phase == GamePhase.Finished) != (value.Result != MatchResult.None) ||
            won != value.WinnerPlayerId.HasValue ||
            (won ? value.RevealedHands.Length != value.Players.Length :
                value.RevealedHands.Length != 0)) return false;

        var visibleCards = new HashSet<int>();
        var meldIds = new HashSet<int>();
        foreach (MeldDto m in value.Melds)
        {
            if (m is null || m.Id <= 0 || !meldIds.Add(m.Id) ||
                !playerIds.Contains(m.OwnerId) || !ValidCards(m.Cards, 13) ||
                m.Cards.Length < 3 ||
                !RulesValidator.ValidateMeld(m.Cards.Select(Card.FromId).ToArray()).IsValid)
                return false;
            foreach (int id in m.Cards)
                if (!visibleCards.Add(id)) return false;
        }
        if (value.DiscardTop is int top && !visibleCards.Add(top)) return false;
        if (value.OfferedCard is int offered && !visibleCards.Add(offered)) return false;

        var revealedPlayers = new HashSet<int>();
        foreach (RevealedHandDto hand in value.RevealedHands)
        {
            if (hand is null || !playerIds.Contains(hand.PlayerId) ||
                !revealedPlayers.Add(hand.PlayerId) || !ValidCards(hand.Cards, 52) ||
                hand.Cards.Length != value.Players.Single(p => p.Id == hand.PlayerId).CardCount)
                return false;
            foreach (int id in hand.Cards)
                if (!visibleCards.Add(id)) return false;
        }
        if ((value.Result == MatchResult.InitialVictory) != (value.InitialVictory is not null)) return false;
        if (value.InitialVictory is { } proof)
        {
            if (value.Result != MatchResult.InitialVictory || !Enum.IsDefined(proof.Kind) ||
                !ValidCards(proof.Cards, 9)) return false;
            int expected = proof.Kind is InitialVictoryKind.Color or InitialVictoryKind.Peladilla ? 9 : 4;
            var winnerCards = value.Melds.Where(m => m.OwnerId == value.WinnerPlayerId).SelectMany(m => m.Cards)
                .Concat(value.RevealedHands.Where(h => h.PlayerId == value.WinnerPlayerId).SelectMany(h => h.Cards)).ToHashSet();
            if (proof.Cards.Length != expected || proof.Cards.Any(id => !winnerCards.Contains(id))) return false;
            // Evidencia de cartas ya públicas al ganar, no una segunda ubicación de cartas.
        }
        int accounted = value.DeckCount + value.DiscardCount +
            value.Players.Sum(p => p.CardCount) + value.Melds.Sum(m => m.Cards.Length) +
            (value.OfferedCard.HasValue ? 1 : 0) + value.ExchangeCount;
        return value.Phase == GamePhase.Preparing
            ? accounted == 0 && value.Melds.Length == 0 && value.Players.All(p => p.CardCount == 0)
            : value.Players.Length >= 2 && accounted == 52;
    }

    public static bool IsValidPrivate(PrivateSnapshotDto? value) =>
        value is not null && value.Version == NetworkProtocol.Version &&
        ValidSession(value.Session) && value.Revision > 0 && value.RecipientId > 0 &&
        ValidCards(value.OwnHand, 52);

    internal static bool ValidSession(string? value) =>
        value is not null && value.Length == 32 && Guid.TryParseExact(value, "N", out _);

    private static bool ValidCards(int[]? cards, int maximum) =>
        cards is not null && cards.Length <= maximum &&
        cards.All(id => id >= 0 && id < 52) && cards.Distinct().Count() == cards.Length;
    private static bool ValidOptionalCard(int? id) => id is null || id is >= 0 and < 52;
    private static bool KnownPlayer(int? id, HashSet<int> players) =>
        id is null || players.Contains(id.Value);

    internal static InitialVictoryDto? CopyInitial(InitialVictoryDto? source) =>
        source is null ? null : new(source.Kind, source.Cards.ToArray());
    internal static MeldDto[] CopyMelds(MeldDto[] source) =>
        source.Select(m => new MeldDto(m.Id, m.OwnerId, m.Cards.ToArray())).ToArray();
    internal static RevealedHandDto[] CopyRevealed(RevealedHandDto[] source) =>
        source.Select(h => new RevealedHandDto(h.PlayerId, h.Cards.ToArray())).ToArray();
    internal static PublicSnapshotDto Copy(PublicSnapshotDto source) => source with
    {
        Players = source.Players.ToArray(),
        Melds = CopyMelds(source.Melds),
        RevealedHands = CopyRevealed(source.RevealedHands),
        InitialVictory = CopyInitial(source.InitialVictory)
    };
    internal static PrivateSnapshotDto Copy(PrivateSnapshotDto source) =>
        source with { OwnHand = source.OwnHand.ToArray() };
    internal static SnapshotDto Combine(PublicSnapshotDto p, PrivateSnapshotDto own) =>
        new(p.Version, p.Session, p.Revision, p.Window, own.RecipientId,
            p.Phase, p.Result, p.CurrentPlayerId, p.PaymentPlayerId, p.WinnerPlayerId,
            p.Players.ToArray(), own.OwnHand.ToArray(), CopyMelds(p.Melds),
            p.DeckCount, p.DiscardCount, p.DiscardTop, p.OfferedCard,
            CopyRevealed(p.RevealedHands), own.OwnResponseSubmitted, p.ExchangeCount, p.OrderChallenge, p.OpeningOffer, CopyInitial(p.InitialVictory));
}

// No supone orden entre RPC y sincronizacion. Conserva solo la ultima parte de cada canal.
public sealed class ClientStateAssembler
{
    private string? _session;
    private int _recipient;
    private PublicSnapshotDto? _public;
    private PrivateSnapshotDto? _private;
    private SnapshotDto? _view;

    // Copia defensiva tambien al leer: una UI no puede mutar los buffers pendientes.
    public SnapshotDto? View => _view is null ? null :
        StateProjection.Combine(StateProjection.Public(_view), StateProjection.Private(_view));

    public void Start(string session, int recipient)
    {
        if (!StateProjection.ValidSession(session)) throw new ArgumentException("Sesion invalida.", nameof(session));
        if (recipient <= 0) throw new ArgumentOutOfRangeException(nameof(recipient));
        Reset();
        _session = session;
        _recipient = recipient;
    }

    public void Reset()
    {
        _session = null;
        _recipient = 0;
        _public = null;
        _private = null;
        _view = null;
    }

    // true significa que se publico una nueva vista coherente.
    // false: parte pendiente, repetida, antigua, invalida o de otra sesion.
    public bool TryApplyPublic(PublicSnapshotDto value, out SnapshotDto? committed)
    {
        committed = null;
        if (_session is null || !StateProjection.IsValidPublic(value) ||
            value.Session != _session || !value.Players.Any(p => p.Id == _recipient) ||
            value.Revision <= (_view?.Revision ?? 0) ||
            value.Revision <= (_public?.Revision ?? 0)) return false;
        _public = StateProjection.Copy(value);
        return TryCommit(out committed);
    }

    public bool TryApplyPrivate(PrivateSnapshotDto value, out SnapshotDto? committed)
    {
        committed = null;
        if (_session is null || !StateProjection.IsValidPrivate(value) ||
            value.Session != _session || value.RecipientId != _recipient ||
            value.Revision <= (_view?.Revision ?? 0) ||
            value.Revision <= (_private?.Revision ?? 0)) return false;
        _private = StateProjection.Copy(value);
        return TryCommit(out committed);
    }

    private bool TryCommit(out SnapshotDto? committed)
    {
        committed = null;
        if (_public is null || _private is null || _public.Revision != _private.Revision)
            return false;
        PublicPlayerDto player = _public.Players.Single(p => p.Id == _recipient);
        if (player.CardCount != _private.OwnHand.Length) return false;
        var occupied = _public.Melds.SelectMany(m => m.Cards).ToHashSet();
        if (_public.DiscardTop is int top) occupied.Add(top);
        if (_public.OfferedCard is int offered) occupied.Add(offered);
        if (_private.OwnHand.Any(occupied.Contains)) return false;
        if (_public.RevealedHands.Length != 0)
        {
            RevealedHandDto own = _public.RevealedHands.Single(h => h.PlayerId == _recipient);
            if (!own.Cards.SequenceEqual(_private.OwnHand)) return false;
        }
        _view = StateProjection.Combine(_public, _private);
        committed = View;
        return true;
    }
}
