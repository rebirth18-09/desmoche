using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    private Button _declareInitial = null!, _declareColor = null!, _declarePeladilla = null!;

    private void InitializeInitialDeclarations()
    {
        _declareInitial = ContextButton("DeclareInitialButton", "Desmoche");
        _declareColor = ContextButton("ColorButton", "Color");
        _declarePeladilla = ContextButton("PeladillaButton", "Peladilla");
        _declareInitial.Pressed += RequestInitialVictory;
        _declareColor.Pressed += () => SendInitial(InitialVictoryKind.Color, _handState.Order.ToArray());
        _declarePeladilla.Pressed += () => SendInitial(InitialVictoryKind.Peladilla, null);
        _declareColor.TooltipText = "Ordena toda tu mano de menor a mayor. El As puede ir al principio o al final.";
        _declarePeladilla.TooltipText = "Declara que no se puede formar ningún juego. Si existe uno aunque esté desordenado, quedas eliminado.";
    }

    private void RefreshInitialDeclarations()
    {
        SnapshotDto? view = _interactionView;
        bool initial = InteractionActions.CanRequest(view, ActionKind.DeclareInitial);
        bool afterExchange = initial && view!.Phase == GamePhase.InitialDeclarations;
        Card[] order = _handState.Order.Select(Card.FromId).ToArray();
        _declareColor.Visible = afterExchange && order.Length == 9 && RulesValidator.IsColor(order);
        _declarePeladilla.Visible = afterExchange && order.Length == 9 &&
            !InitialHandPresentation.HasVisibleMeld(order);
        _declareInitial.Visible = FindInitialDeclaration() is not null;
        _declareInitial.Disabled = _declareColor.Disabled = _declarePeladilla.Disabled = !CanSend;
    }

    private (InitialVictoryKind Kind, int[] Cards, bool FromOffer)? FindInitialDeclaration()
    {
        if (_interactionView is not { } view || view.OwnHand.Length != 9) return null;
        if (InteractionActions.CanRequest(view, ActionKind.DeclareInitial))
        {
            var choice = InitialHandPresentation.FindOrderedVictory(_handState.Order,
                view.Phase == GamePhase.InitialDeclarations);
            if (choice is not null) return (choice.Value.Kind, choice.Value.Cards, false);
        }
        if (view.OpeningOffer && InteractionActions.CanRequest(view, ActionKind.Claim))
        {
            PlayDto[] plays = _handState.BuildPlays();
            if (plays.Length != 1 || plays[0].MeldId is not null || view.OfferedCard is not int offer) return null;
            var hand = new PlayerHand(view.OwnHand.Select(Card.FromId));
            foreach (InitialVictoryKind kind in InitialHandPresentation.CombinationKinds)
                if (RulesValidator.ValidateInitialVictory(hand, kind,
                    plays[0].Cards.Select(Card.FromId).ToArray(), Card.FromId(offer)).IsValid &&
                    RulesValidator.HasAlternatingSetOrder(plays[0].Cards.Select(Card.FromId).ToArray()))
                    return (kind, plays[0].Cards, true);
        }
        return null;
    }

    private void SendInitial(InitialVictoryKind kind, int[]? cards)
    {
        if (!CanSend || !InteractionActions.CanRequest(_interactionView, ActionKind.DeclareInitial)) return;
        SendRequest(new VisualActionRequest(ActionKind.DeclareInitial, InitialKind: kind, Cards: cards));
    }

    private void RequestInitialVictory()
    {
        if (!CanSend || FindInitialDeclaration() is not { } choice) return;
        if (!choice.FromOffer) SendInitial(choice.Kind, choice.Cards);
        else if (InteractionActions.TryCreate(_interactionView!, _handState, ActionKind.Claim, false,
            out VisualActionRequest? request, out _))
            SendRequest(request! with { InitialKind = choice.Kind });
    }
}
