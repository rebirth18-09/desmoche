using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;

namespace Desmoche.Application;

public sealed partial class GameManager
{
    private readonly HashSet<int> _discardRefusals = new();
    private int? _refusedDiscardId;

    // El pozo antiguo sigue perteneciendo al turno actual. Elegirlo no lo
    // incorpora a la mano hasta confirmar una combinacion valida.
    public GameActionResult ChooseDiscard(int playerId)
    {
        if (Phase != GamePhase.AwaitingDraw || CurrentPlayerId != playerId ||
            _extraDrawRequired || !Eligible(playerId) || State!.DiscardTop is null)
            return Fail("No puede elegir el pozo ahora.");
        State = State.OfferDiscardTop();
        _firstOffer = false;
        OpenClaims(playerId, OfferSource.TurnDiscard, false);
        return Ok("Pozo elegido; prepara el juego antes de confirmar.");
    }

    public GameActionResult DeclineDiscardChoice(int playerId)
    {
        if (Phase != GamePhase.AwaitingDraw || CurrentPlayerId != playerId ||
            _extraDrawRequired || State!.DiscardTop is null || !Eligible(playerId))
            return Fail("No hay eleccion del pozo pendiente.");
        // Rechazar el pozo continua con la carta publica del mazo.
        if (State.DeckCount > 0) return DrawFromDeck(playerId);
        if (_refusedDiscardId != State.DiscardTop.Id)
        { _discardRefusals.Clear(); _refusedDiscardId = State.DiscardTop.Id; }
        _discardRefusals.Add(playerId);
        int id = playerId;
        for (int i = 0; i < _roster.Count; i++)
        {
            id = RightOf(id);
            if (Eligible(id) && !_discardRefusals.Contains(id) && CanUseDiscard(id, State.DiscardTop))
            { CurrentPlayerId = id; return Ok("Pozo rechazado; el siguiente jugador puede elegirlo."); }
        }
        Finish(MatchResult.Draw, null);
        return Ok("Mazo vacio y ningun jugador utiliza el pozo; empate.");
    }

    // Solo la autoridad usa estas opciones. Se calculan de nuevo al vencer el
    // plazo, con la mano propia y los juegos propios vigentes, nunca del mazo.
    public IReadOnlyList<IReadOnlyList<MeldPlay>> PossibleOfferPlays(int playerId)
    {
        if (State?.OfferedCard is not Card offered || !CanChooseOffer(playerId))
            return Array.Empty<IReadOnlyList<MeldPlay>>();
        return FindCardUses(State, playerId, offered);
    }

    private static IReadOnlyList<IReadOnlyList<MeldPlay>> FindCardUses(GameState offeredState,
        int playerId, Card offered)
    {
        Meld[] own = offeredState.Melds.Where(m => m.OwnerPlayerId == playerId).ToArray();
        Card[] pool = offeredState.GetPlayer(playerId).Hand.Cards
            .Concat(own.SelectMany(m => m.Cards)).Append(offered).ToArray();
        var candidates = new List<Card[]>();
        Card[] rank = pool.Where(c => c.Rank == offered.Rank && c.Id != offered.Id).ToArray();
        for (int mask = 0; mask < (1 << rank.Length); mask++)
        {
            Card[] group = rank.Where((_, i) => (mask & (1 << i)) != 0).Append(offered).ToArray();
            if (group.Length is 3 or 4) candidates.Add(RulesValidator.ArrangeAlternatingSet(group));
        }
        foreach (bool aceHigh in new[] { false, true })
        {
            var suited = pool.Where(c => c.Suit == offered.Suit).ToDictionary(
                c => aceHigh && c.Rank == Rank.Ace ? 14 : (int)c.Rank);
            int offeredRank = aceHigh && offered.Rank == Rank.Ace ? 14 : (int)offered.Rank;
            int minimum = aceHigh ? 2 : 1, maximum = aceHigh ? 14 : 13;
            for (int start = minimum; start <= offeredRank; start++)
                for (int end = Math.Max(start + 2, offeredRank); end <= maximum; end++)
                {
                    int[] ranks = Enumerable.Range(start, end - start + 1).ToArray();
                    if (ranks.All(suited.ContainsKey)) candidates.Add(ranks.Select(r => suited[r]).ToArray());
                }
        }
        var results = new List<IReadOnlyList<MeldPlay>>();
        var unique = new HashSet<string>();
        foreach (Card[] candidate in candidates)
        {
            var ids = candidate.Select(c => c.Id).ToHashSet();
            foreach (int? target in new int?[] { null }.Concat(own
                .Where(m => m.Cards.All(c => ids.Contains(c.Id))).Select(m => (int?)m.Id)))
            {
                var plays = new List<MeldPlay>();
                bool valid = true;
                foreach (Meld source in own.Where(m => m.Id != target && m.Cards.Any(c => ids.Contains(c.Id))))
                {
                    Card[] retained = source.Cards.Where(c => !ids.Contains(c.Id)).ToArray();
                    if (!RulesValidator.ValidateMeld(retained).IsValid) { valid = false; break; }
                    plays.Add(new MeldPlay(RulesValidator.ArrangeAlternatingSet(retained).Select(c => c.Id), source.Id));
                }
                if (!valid) continue;
                plays.Add(new MeldPlay(candidate.Select(c => c.Id), target));
                if (!TryClaim(offeredState, playerId, plays, out _, out bool reordered, out _) || reordered) continue;
                string key = string.Join("|", plays.OrderBy(p => p.MeldId ?? int.MaxValue)
                    .Select(p => $"{p.MeldId}:{string.Join(",", p.CardIds.OrderBy(id => id))}"));
                if (unique.Add(key)) results.Add(CopyPlays(plays));
            }
        }
        return results.AsReadOnly();
    }
}
