using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

namespace Desmoche.Singleplayer.Core;

// Entrada limitada a la vista del propio jugador. No recibe GameManager, ServerSession,
// manos rivales, orden del mazo, ni la semilla usada para barajar.
public sealed class BotBrain
{
    private sealed record Candidate(PlayDto Play, int Mask, int TargetBit);
    private sealed record Plan(PlayDto[] Plays, int Mask);
    private readonly BotDifficulty _difficulty;
    private readonly Random _random;
    private readonly HashSet<int> _seenPublic = new();
    public const int MaximumSearchStates = 8192;
    public int LastSearchStates { get; private set; }

    public BotBrain(BotDifficulty difficulty, int seed)
    {
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
        _difficulty = difficulty;
        _random = new Random(seed);
    }

    public void Reset() => _seenPublic.Clear();

    public void Observe(SnapshotDto view)
    {
        if (view.DiscardTop is int top) _seenPublic.Add(top);
        if (view.OfferedCard is int offer) _seenPublic.Add(offer);
        foreach (int card in view.Melds.SelectMany(m => m.Cards)) _seenPublic.Add(card);
        // RevealedHands deliberadamente no participa en las decisiones: solo existe al terminar.
    }

    public BotAction? Decide(SnapshotDto view, BotPublicContext context = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        Observe(view);
        LastSearchStates = 0;
        PublicPlayerDto? self = view.Players.FirstOrDefault(p => p.Id == view.RecipientId);
        if (self is null || self.Eliminated || !self.Connected ||
            view.Phase is GamePhase.Preparing or GamePhase.Finished) return null;

        if (view.Phase == GamePhase.InitialExchange)
            return view.OwnResponseSubmitted ? null :
                new BotAction(ActionKind.ExchangeCard, SelectPayment(view));

        if (view.Phase == GamePhase.InitialDeclarations)
            return view.OwnResponseSubmitted ? null :
                FindInitialVictory(view) ?? new BotAction(ActionKind.FinishInitial);

        if (view.Phase == GamePhase.AwaitingClaims)
        {
            if (view.OwnResponseSubmitted || view.OfferedCard is not int offer) return null;
            if (context.OpeningOffer)
            {
                BotAction? initial = FindInitialVictory(view, offer);
                if (initial is not null) return initial;
            }
            return Claim(view, offer, ActionKind.Claim) ?? new BotAction(ActionKind.Pass);
        }

        bool drawing = view.Phase == GamePhase.AwaitingDraw && view.CurrentPlayerId == view.RecipientId;
        bool paying = view.Phase == GamePhase.AwaitingPayment && view.PaymentPlayerId == view.RecipientId;
        if (!drawing && !paying) return null;

        if (drawing && !context.MustDrawDeck && view.DiscardTop is int discard)
        {
            BotAction? take = Claim(view, discard, ActionKind.DrawDiscard);
            if (take is not null) return take;
        }

        Plan plan = ChoosePlan(view, view.OwnHand, null);
        int ownTableCount = OwnMelds(view).Sum(m => m.Cards.Length);
        if (paying && ownTableCount + view.OwnHand.Length == RulesValidator.WinningCardCount &&
            Count(plan.Mask) == view.OwnHand.Length)
            return new BotAction(ActionKind.DeclareDesmoche, Plays: plan.Plays);
        if (plan.Plays.Length > 0)
            return new BotAction(ActionKind.Publish, Plays: plan.Plays);
        if (paying)
            return view.OwnHand.Length == 0 ? new BotAction(ActionKind.CompleteWithoutPayment) :
                new BotAction(ActionKind.Pay, SelectPayment(view));
        return view.DeckCount > 0 ? new BotAction(ActionKind.DrawDeck) : null;
    }

    private BotAction? Claim(SnapshotDto view, int offer, ActionKind kind)
    {
        int[] available = view.OwnHand.Append(offer).OrderBy(id => id).ToArray();
        Plan plan = ChoosePlan(view, available, offer);
        if (plan.Plays.Length == 0) return null;
        bool win = Count(plan.Mask) == available.Length &&
            OwnMelds(view).Sum(m => m.Cards.Length) + available.Length == RulesValidator.WinningCardCount;
        return new BotAction(kind, Plays: plan.Plays, DeclareWin: win);
    }

    private BotAction? FindInitialVictory(SnapshotDto view, int? offer = null)
    {
        if (view.OwnHand.Length != GameState.InitialHandSize) return null;
        var hand = new PlayerHand(view.OwnHand.Select(Card.FromId));
        if (offer is null)
        {
            foreach (InitialVictoryKind kind in new[] { InitialVictoryKind.Color, InitialVictoryKind.Peladilla })
                if (RulesValidator.ValidateInitialVictory(hand, kind).IsValid)
                    return new BotAction(ActionKind.DeclareInitial, InitialKind: kind);
        }
        int[] cards = (offer is int id ? view.OwnHand.Append(id) : view.OwnHand).OrderBy(id => id).ToArray();
        foreach (Candidate candidate in Candidates(cards, Array.Empty<MeldDto>()))
        {
            if (offer is int required && !candidate.Play.Cards.Contains(required)) continue;
            foreach (InitialVictoryKind kind in new[]
                     { InitialVictoryKind.Cuarta, InitialVictoryKind.LowStraight, InitialVictoryKind.HighStraight })
            {
                if (!RulesValidator.ValidateInitialVictory(hand, kind,
                        candidate.Play.Cards.Select(Card.FromId).ToArray(),
                        offer is int offered ? Card.FromId(offered) : null).IsValid) continue;
                return offer is null
                    ? new BotAction(ActionKind.DeclareInitial, InitialKind: kind, Cards: candidate.Play.Cards)
                    : new BotAction(ActionKind.Claim, Plays: new[] { candidate.Play }, InitialKind: kind);
            }
        }
        return null;
    }

    private Plan ChoosePlan(SnapshotDto view, int[] available, int? required)
    {
        if (available.Length > 13 || available.Distinct().Count() != available.Length)
            throw new ArgumentException("La vista del bot contiene una mano inválida.");
        Candidate[] candidates = Candidates(available, OwnMelds(view)).ToArray();
        int requiredMask = required is int id ? 1 << Array.IndexOf(available, id) : 0;
        if (requiredMask != 0 && !candidates.Any(c => (c.Mask & requiredMask) != 0))
            return new Plan(Array.Empty<PlayDto>(), 0);
        if (_difficulty <= BotDifficulty.Medium)
            return GreedyPlan(view, available, candidates, requiredMask);

        var used = new HashSet<(int Cards, int Targets)>();
        var current = new List<PlayDto>();
        Plan best = new(Array.Empty<PlayDto>(), 0);
        double bestScore = requiredMask == 0 ? Potential(available, view) : double.NegativeInfinity;
        int explored = 0;
        void Search(int mask, int targets)
        {
            if (explored >= MaximumSearchStates || !used.Add((mask, targets))) return;
            explored++;
            if ((mask & requiredMask) == requiredMask)
            {
                double score = Count(mask) * 1000 + Potential(Remaining(available, mask), view);
                if (score > bestScore)
                { bestScore = score; best = new Plan(current.ToArray(), mask); }
            }
            foreach (Candidate candidate in candidates)
            {
                if ((candidate.Mask & mask) != 0 || (candidate.TargetBit & targets) != 0) continue;
                current.Add(candidate.Play);
                Search(mask | candidate.Mask, targets | candidate.TargetBit);
                current.RemoveAt(current.Count - 1);
            }
        }
        Search(0, 0);
        LastSearchStates += explored;
        return best;
    }

    private Plan GreedyPlan(SnapshotDto view, int[] available, Candidate[] candidates, int requiredMask)
    {
        int mask = 0, targets = 0;
        var plays = new List<PlayDto>();
        while (true)
        {
            IEnumerable<Candidate> legal = candidates.Where(c => (c.Mask & mask) == 0 &&
                (c.TargetBit & targets) == 0 &&
                (requiredMask == 0 || (mask & requiredMask) != 0 || (c.Mask & requiredMask) != 0));
            if (_difficulty == BotDifficulty.Medium)
                legal = legal.OrderByDescending(c => Count(c.Mask) * 1000 +
                    Potential(Remaining(available, mask | c.Mask), view));
            Candidate? selected = legal.FirstOrDefault();
            if (selected is null) break;
            plays.Add(selected.Play);
            mask |= selected.Mask;
            targets |= selected.TargetBit;
        }
        return new Plan(plays.ToArray(), mask);
    }

    private int SelectPayment(SnapshotDto view)
    {
        if (view.OwnHand.Length == 0) throw new ArgumentException("No hay carta para pagar.");
        if (_difficulty == BotDifficulty.Easy)
            return view.OwnHand[_random.Next(view.OwnHand.Length)];
        // Conservar cartas que ya forman juegos y parejas útiles. Solo Difícil/Experto
        // consideran si el pago ampliaría un juego rival que ya está visible.
        return view.OwnHand.OrderByDescending(card =>
        {
            int[] kept = view.OwnHand.Where(id => id != card).ToArray();
            double score = Potential(kept, view);
            score += Candidates(kept, OwnMelds(view)).Select(c => Count(c.Mask)).DefaultIfEmpty(0).Max() * 40;
            if (_difficulty >= BotDifficulty.Hard && view.Phase != GamePhase.InitialExchange)
                score -= PublicDanger(card, view) * (_difficulty == BotDifficulty.Expert ? 45 : 20);
            return score;
        }).ThenBy(id => id).First();
    }

    private double Potential(int[] hand, SnapshotDto view)
    {
        if (_difficulty == BotDifficulty.Easy) return 0;
        var held = hand.ToHashSet();
        double potential = 0;
        var allOwn = view.OwnHand.ToHashSet();
        foreach (IGrouping<int, int> rank in hand.GroupBy(id => id % 13))
        {
            if (rank.Count() < 2) continue;
            if (_difficulty < BotDifficulty.Expert) potential += 8 * (rank.Count() - 1);
            else potential += Enumerable.Range(0, 4).Select(suit => suit * 13 + rank.Key)
                .Count(id => !allOwn.Contains(id) && !_seenPublic.Contains(id)) * 4;
        }
        for (int suit = 0; suit < 4; suit++)
            for (int start = 1; start <= 12; start++)
            {
                int[] run = Enumerable.Range(start, 3).Select(rank => suit * 13 + (rank == 14 ? 0 : rank - 1)).ToArray();
                if (run.Count(held.Contains) != 2) continue;
                int missing = run.Single(id => !held.Contains(id));
                if (_difficulty < BotDifficulty.Expert) potential += 3;
                else if (!allOwn.Contains(missing) && !_seenPublic.Contains(missing)) potential += 5;
            }
        return potential;
    }

    private static int PublicDanger(int card, SnapshotDto view)
    {
        int danger = 0;
        foreach (MeldDto meld in view.Melds.Where(m => m.OwnerId != view.RecipientId))
            if (Ordered(meld.Cards.Append(card)) is not null)
            {
                PublicPlayerDto? owner = view.Players.FirstOrDefault(p => p.Id == meld.OwnerId);
                danger += owner is { CardCount: <= 2, Eliminated: false } ? 3 : 1;
            }
        return danger;
    }

    private static IEnumerable<Candidate> Candidates(int[] available, MeldDto[] ownMelds)
    {
        int limit = 1 << available.Length;
        for (int mask = 1; mask < limit; mask++)
        {
            int[] added = available.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            if (added.Length >= 3 && Ordered(added) is int[] newGroup)
                yield return new Candidate(new PlayDto(newGroup), mask, 0);
            for (int target = 0; target < ownMelds.Length; target++)
                if (Ordered(ownMelds[target].Cards.Concat(added)) is int[] extended)
                    yield return new Candidate(new PlayDto(extended, ownMelds[target].Id), mask, 1 << target);
        }
    }

    // La autoridad acepta ambos sentidos. Elegimos uno canónico y consultamos
    // siempre al validador compartido, incluida Q-K-A y la prohibición de K-A-2.
    private static int[]? Ordered(IEnumerable<int> ids)
    {
        Card[] cards = ids.Select(Card.FromId).ToArray();
        if (cards.Length is < 3 or > 13) return null;
        Card[] low = cards.OrderBy(c => (int)c.Rank).ThenBy(c => (int)c.Suit).ToArray();
        if (RulesValidator.ValidateMeld(low).IsValid) return low.Select(c => c.Id).ToArray();
        Card[] high = cards.OrderBy(c => c.Rank == Rank.Ace ? 14 : (int)c.Rank).ThenBy(c => (int)c.Suit).ToArray();
        return RulesValidator.ValidateMeld(high).IsValid ? high.Select(c => c.Id).ToArray() : null;
    }

    private static MeldDto[] OwnMelds(SnapshotDto view) =>
        view.Melds.Where(m => m.OwnerId == view.RecipientId).OrderBy(m => m.Id).ToArray();
    private static int Count(int mask) => BitOperations.PopCount((uint)mask);
    private static int[] Remaining(int[] cards, int mask) => cards.Where((_, index) => (mask & (1 << index)) == 0).ToArray();
}
