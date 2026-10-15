using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;

namespace Desmoche.Presentation.Interaction;

// Plan exclusivamente visual. Las cartas ajenas carecen de identidad, incluso
// durante el vuelo: el dorso representa una entrega, no una carta del mazo.
public sealed record DealVisualCard(int PlayerId, int Index, int? OwnCardId);

public static class InitialDealPresentation
{
    public const double CardIntervalSeconds = 0.09;
    public const double FlightSeconds = 0.34;
    public const double RevealSeconds = 0.16;

    public static bool CanStart(SnapshotDto? previous, SnapshotDto next) =>
        next.Phase == GamePhase.InitialExchange && !next.OwnResponseSubmitted &&
        next.Players.Length is >= 2 and <= 4 && next.Melds.Length == 0 &&
        next.Players.All(p => p.Connected && p.CardCount is 8 or 9) &&
        next.Players.Sum(p => p.CardCount) + next.ExchangeCount == next.Players.Length * 9 &&
        (next.OwnSpectating ? next.OwnHand.Length == 0 : next.OwnHand.Length == 9) &&
        (previous is null || previous.Session != next.Session ||
            previous.Phase is GamePhase.Preparing or GamePhase.Finished);

    public static IReadOnlyList<DealVisualCard> Build(SnapshotDto view)
    {
        if (!CanStart(null, view)) return Array.Empty<DealVisualCard>();
        PublicPlayerDto[] players = view.Players.OrderBy(p => p.SeatIndex).ToArray();
        var result = new List<DealVisualCard>(players.Length * 9);
        for (int round = 0; round < 9; round++)
            foreach (PublicPlayerDto player in players)
                result.Add(new DealVisualCard(player.Id, round,
                    player.Id == view.RecipientId ? view.OwnHand[round] : null));
        return result.AsReadOnly();
    }

    public static double Duration(int count) => count <= 0 ? 0 :
        (count - 1) * CardIntervalSeconds + FlightSeconds + RevealSeconds;
}
