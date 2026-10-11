using System;
using System.Collections.Generic;
using System.Linq;

namespace Desmoche.Domain;

// Grupo publico inmutable. Id estable durante toda la partida.
public sealed class Meld
{
    public int Id { get; }
    public int OwnerPlayerId { get; }
    public IReadOnlyList<Card> Cards { get; }

    internal Meld(int id, int ownerPlayerId, IEnumerable<Card> cards)
    {
        if (id <= 0 || ownerPlayerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        Card[] copy = Card.CopyDistinct(cards);
        if (!RulesValidator.ValidateMeld(copy).IsValid)
            throw new ArgumentException("El juego no es valido.", nameof(cards));
        Id = id;
        OwnerPlayerId = ownerPlayerId;
        Cards = Array.AsReadOnly(copy);
    }
}

// Solicitud: grupo nuevo si MeldId es null; extension si tiene Id.
// CardIds describe el grupo completo en el orden declarado.
public sealed class MeldPlay
{
    public int? MeldId { get; }
    public IReadOnlyList<int> CardIds { get; }

    public MeldPlay(IEnumerable<int> cardIds, int? meldId = null)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        CardIds = Array.AsReadOnly(cardIds.ToArray());
        MeldId = meldId;
    }
}
