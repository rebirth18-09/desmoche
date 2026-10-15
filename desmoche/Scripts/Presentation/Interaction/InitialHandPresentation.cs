using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;

namespace Desmoche.Presentation.Interaction;

// Solo detecta cartas juntas y ordenadas. La autoridad evalua la Peladilla real
// considerando cualquier agrupacion, aunque no sea visible en la mano.
public static class InitialHandPresentation
{
    public static IReadOnlyList<InitialVictoryKind> CombinationKinds { get; } = Array.AsReadOnly(new[]
        { InitialVictoryKind.LowStraight, InitialVictoryKind.HighStraight, InitialVictoryKind.Cuarta });

    public static bool HasVisibleMeld(IReadOnlyList<Card> order)
    {
        for (int i = 0; i + 3 <= order.Count; i++)
            if (RulesValidator.ValidateMeld(order.Skip(i).Take(3).ToArray()).IsValid) return true;
        return false;
    }

    public static (InitialVictoryKind Kind, int[] Cards)? FindOrderedVictory(
        IReadOnlyList<int> order, bool allowCuarta, MatchSettings? settings = null)
    {
        if (order.Count != GameState.InitialHandSize || order.Any(c => c is < 0 or > 51) ||
            order.Distinct().Count() != order.Count) return null;
        var hand = new PlayerHand(order.Select(Card.FromId));
        for (int i = 0; i + 4 <= order.Count; i++)
        {
            Card[] cards = order.Skip(i).Take(4).Select(Card.FromId).ToArray();
            foreach (InitialVictoryKind kind in CombinationKinds)
                if ((settings ?? MatchSettings.Default).Allows(kind) && (allowCuarta || kind != InitialVictoryKind.Cuarta) &&
                    RulesValidator.HasAlternatingSetOrder(cards) &&
                    RulesValidator.ValidateInitialVictory(hand, kind, cards).IsValid)
                    return (kind, cards.Select(c => c.Id).ToArray());
        }
        return null;
    }
}
