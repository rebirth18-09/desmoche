using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

namespace Desmoche.Presentation.Interaction;

// Intencion local. No aplica el resultado ni transporta una identidad de jugador.
public sealed record VisualActionRequest(ActionKind Kind, int? CardId = null,
    PlayDto[]? Plays = null, bool DeclareWin = false,
    InitialVictoryKind? InitialKind = null, int[]? Cards = null);

public static class InteractionActions
{
    // Habilita controles segun la vista autorizada. El servidor conserva la
    // validacion final, incluyendo restricciones que no publica en el DTO.
    public static bool CanRequest(SnapshotDto? view, ActionKind kind)
    {
        if (view is null) return false;
        if (kind == ActionKind.JoinRematch) return view.Phase == GamePhase.Finished &&
            view.Rematch is { Closed: false, RemainingMilliseconds: > 0 } rematch &&
            rematch.EligiblePlayerIds.Contains(view.RecipientId) && !rematch.ReadyPlayerIds.Contains(view.RecipientId) &&
            view.Players.Any(p => p.Id == view.RecipientId && p.Connected);
        if (!view.Players.Any(player => player.Id == view.RecipientId &&
                player.Connected && !player.Eliminated)) return false;
        bool ownTurn = view.CurrentPlayerId == view.RecipientId;
        bool ownPayment = view.Phase == GamePhase.AwaitingPayment &&
            view.PaymentPlayerId == view.RecipientId;
        return kind switch
        {
            ActionKind.ExchangeCard => view.Phase == GamePhase.InitialExchange &&
                !view.OwnResponseSubmitted,
            ActionKind.FinishInitial => view.Phase == GamePhase.InitialDeclarations && !view.OwnResponseSubmitted,
            ActionKind.DeclareInitial => view.Phase is GamePhase.InitialExchange or GamePhase.InitialDeclarations &&
                !view.OwnResponseSubmitted,
            ActionKind.SayPasa => view.Phase == GamePhase.AwaitingOrderChallenge &&
                view.OrderChallenge is { } challenge && challenge.PlayerId != view.RecipientId,
            ActionKind.DrawDeck => view.Phase == GamePhase.AwaitingDraw && ownTurn &&
                view.DeckCount > 0,
            ActionKind.DrawDiscard => view.Phase == GamePhase.AwaitingDraw && ownTurn &&
                !view.MustDrawDeck && view.DiscardTop is not null && view.DiscardTiming is null,
            ActionKind.ChooseDiscard => view.Phase == GamePhase.AwaitingDraw && ownTurn &&
                view.DiscardTiming is { RemainingMilliseconds: > 0 },
            ActionKind.ChooseOffer => view.Phase == GamePhase.AwaitingClaims && !view.OwnResponseSubmitted &&
                view.OfferTiming is { RemainingSelectionMilliseconds: > 0 } offer &&
                !offer.Reservations.Any(r => r.PlayerId == view.RecipientId),
            ActionKind.Claim => view.Phase == GamePhase.AwaitingClaims &&
                !view.OwnResponseSubmitted && view.OfferedCard is not null &&
                (view.OfferTiming is null || view.OfferTiming.Reservations.Any(r =>
                    r.PlayerId == view.RecipientId && r.RemainingMilliseconds > 0)),
            ActionKind.Pass => view.Phase == GamePhase.AwaitingClaims &&
                !view.OwnResponseSubmitted,
            // Bajar juegos no esta limitado al turno propio por las reglas acordadas.
            ActionKind.Publish => view.Phase is GamePhase.AwaitingDraw or
                GamePhase.AwaitingClaims or GamePhase.AwaitingPayment,
            ActionKind.Pay or ActionKind.DeclareDesmoche => ownPayment,
            ActionKind.CompleteWithoutPayment => ownPayment && view.OwnHand.Length == 0,
            _ => false
        };
    }

    // Comprueba forma y pertenencia de los datos visibles. No decide si una
    // tercia, cuarta, escalera o declaracion de victoria es valida.
    public static bool TryCreate(SnapshotDto view, HandInteractionState hand,
        ActionKind kind, bool declareWin, out VisualActionRequest? request,
        out string message)
    {
        request = null;
        message = "Esta accion no esta disponible en el estado actual.";
        if (!CanRequest(view, kind)) return false;
        if (kind is ActionKind.ExchangeCard or ActionKind.Pay)
        {
            int[] selected = hand.SelectedIds.ToArray();
            if (selected.Length != 1)
            { message = "Selecciona exactamente una carta de tu mano."; return false; }
            if (!view.OwnHand.Contains(selected[0]))
            { message = "La carta seleccionada ya no esta en tu mano."; return false; }
            request = new VisualActionRequest(kind, selected[0]);
        }
        else if (kind is ActionKind.Publish or ActionKind.Claim or
                 ActionKind.DrawDiscard or ActionKind.DeclareDesmoche)
        {
            PlayDto[] plays = hand.BuildPlays();
            if (kind != ActionKind.DeclareDesmoche && plays.Length == 0)
            { message = "Prepara al menos una combinacion antes de enviarla."; return false; }
            int? source = kind switch
            {
                ActionKind.Claim => view.OfferedCard,
                ActionKind.DrawDiscard => view.DiscardTop,
                _ => null
            };
            if (source is int required && plays.SelectMany(play => play.Cards)
                    .Count(id => id == required) != 1)
            { message = "La carta ofrecida o del pozo debe estar en una combinacion."; return false; }
            if (kind is ActionKind.Publish or ActionKind.DeclareDesmoche &&
                hand.ExternalCardId is int external &&
                plays.Any(play => play.Cards.Contains(external)))
            { message = "Esta accion solo usa cartas de tu mano y de tus juegos publicados."; return false; }
            if (!ValidVisiblePlays(view, plays, source, out message)) return false;
            request = new VisualActionRequest(kind, Plays: plays,
                DeclareWin: kind is ActionKind.Claim or ActionKind.DrawDiscard && declareWin);
        }
        else
            request = new VisualActionRequest(kind);
        message = "Solicitud preparada; el servidor validara la accion.";
        return true;
    }

    private static bool ValidVisiblePlays(SnapshotDto view, PlayDto[] plays,
        int? source, out string message)
    {
        message = "El borrador contiene datos que ya no estan disponibles.";
        if (plays.Length > 17) return false;
        int[] all = plays.SelectMany(play => play.Cards).ToArray();
        if (all.Length > 52 || all.Distinct().Count() != all.Length) return false;
        var targets = new System.Collections.Generic.HashSet<int>();
        var published = new System.Collections.Generic.HashSet<int>();
        foreach (PlayDto play in plays.Where(p => p.MeldId is not null))
        {
            MeldDto? meld = view.Melds.FirstOrDefault(m => m.Id == play.MeldId && m.OwnerId == view.RecipientId);
            if (meld is null || !targets.Add(meld.Id) || meld.Cards.Any(id => !all.Contains(id))) return false;
            published.UnionWith(meld.Cards);
        }
        foreach (PlayDto play in plays)
        {
            if (play.Cards.Length is < 3 or > 13 ||
                play.Cards.Any(id => id is < 0 or > 51)) return false;
            if (play.Cards.Any(id => !view.OwnHand.Contains(id) &&
                    !published.Contains(id) && source != id)) return false;
        }
        message = "";
        return true;
    }
}
