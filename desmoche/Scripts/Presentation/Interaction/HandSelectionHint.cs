using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;

namespace Desmoche.Presentation.Interaction;

// Indicio local: toda la selección puede formar UN juego al ordenarla.
// No altera el orden visible ni decide la aceptación de una solicitud.
public static class HandSelectionHint
{
    public static bool FormsMeld(IReadOnlyList<int> selected, int? selectedExternal) =>
        FormsMeld(selectedExternal is int external ? selected.Concat(new[] { external }).ToArray() : selected);

    public static bool FormsMeld(IReadOnlyList<int> selected)
    {
        if (selected.Count < 3 || selected.Count > 13 || selected.Any(id => id < 0 || id > 51) ||
            selected.Distinct().Count() != selected.Count) return false;
        Card[] cards = selected.Select(Card.FromId).ToArray();
        Card[] low = cards.OrderBy(card => (int)card.Rank).ToArray();
        if (RulesValidator.ValidateMeld(low).IsValid) return true;
        Card[] high = cards.OrderBy(card => card.Rank == Rank.Ace ? 14 : (int)card.Rank).ToArray();
        return RulesValidator.ValidateMeld(high).IsValid;
    }
}
