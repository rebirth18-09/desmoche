using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    private Label _cardCountdown = null!, _rematchCountdown = null!;
    private ulong _countdownAt;
    private long _countdownRevision;
    private string _countdownSession = "";

    private void InitializeCountdowns()
    {
        _cardCountdown = Text("", 14); _cardCountdown.Name = "CardCountdownLabel";
        _cardCountdown.Visible = false; _contextActions.AddChild(_cardCountdown);
        _rematchCountdown = Text("", 14); _rematchCountdown.Name = "RematchCountdownLabel";
        _rematchCountdown.Visible = false; _rematch.GetParent().AddChild(_rematchCountdown);
    }

    private void SynchronizeCountdowns(SnapshotDto? view)
    {
        if (view?.Session != _countdownSession || view?.Revision != _countdownRevision)
        {
            _countdownAt = Time.GetTicksMsec();
            _countdownSession = view?.Session ?? "";
            _countdownRevision = view?.Revision ?? 0;
        }
        UpdateCountdowns();
    }

    private bool NeedsExternalChoice => _interactionView is { } view &&
        (InteractionActions.CanRequest(view, ActionKind.ChooseOffer) ||
         InteractionActions.CanRequest(view, ActionKind.ChooseDiscard));

    private bool TryChooseExternal()
    {
        if (!CanSend || _interactionView is not { } view) return false;
        ActionKind? choice = InteractionActions.CanRequest(view, ActionKind.ChooseOffer) ? ActionKind.ChooseOffer :
            InteractionActions.CanRequest(view, ActionKind.ChooseDiscard) ? ActionKind.ChooseDiscard : null;
        if (choice is not ActionKind kind) return false;
        RequestAction(kind);
        return true;
    }

    private void UpdateCountdowns()
    {
        if (_cardCountdown is null || _rematchCountdown is null) return;
        SnapshotDto? view = _interactionView;
        int elapsed = (int)Math.Min(int.MaxValue, Time.GetTicksMsec() - _countdownAt);
        int Seconds(int remaining) => (Math.Max(0, remaining - elapsed) + 999) / 1000;
        _cardCountdown.Visible = false;
        _cardCountdown.Text = "";
        if (view?.OrderChallenge is { } challenge)
        {
            _pasaButton.TooltipText = challenge.Reason == PasaReason.UnusedOffer
                ? "Señalar que dejó pasar una oferta que entra en este juego."
                : "Señalar que este juego no alterna los colores.";
            _pasaButton.Disabled = !CanSend || Seconds(challenge.RemainingMilliseconds) == 0;
        }
        if (view?.OfferTiming is { } offer)
        {
            CardReservationDto? own = offer.Reservations.FirstOrDefault(r => r.PlayerId == view.RecipientId);
            _cardCountdown.Text = own is not null ? "Armar juego: " + Seconds(own.RemainingMilliseconds) + " s" :
                !view.OwnResponseSubmitted && Seconds(offer.RemainingSelectionMilliseconds) > 0
                    ? "Elegir carta: " + Seconds(offer.RemainingSelectionMilliseconds) + " s"
                    : "Esperando los juegos de quienes eligieron la carta";
            if (_contextButtons.TryGetValue(ActionKind.ChooseOffer, out Button? choose))
                choose.Disabled = !CanSend || Seconds(offer.RemainingSelectionMilliseconds) == 0;
            _contextButtons[ActionKind.Claim].Text = "Confirmar juego";
            _contextButtons[ActionKind.Claim].TooltipText = offer.FromDiscard ? "Confirmar juego con pozo" : "Confirmar juego con oferta";
            if (own is not null) _contextButtons[ActionKind.Claim].Disabled = !CanSend || Seconds(own.RemainingMilliseconds) == 0;
        }
        else if (view?.DiscardTiming is { } discard && discard.PlayerId == view.RecipientId)
        {
            _cardCountdown.Text = "Elegir pozo: " + Seconds(discard.RemainingMilliseconds) + " s";
            _contextButtons[ActionKind.ChooseDiscard].Disabled = !CanSend || Seconds(discard.RemainingMilliseconds) == 0;
        }
        else _cardCountdown.Text = "";

        bool local = _session?.IsLocal == true;
        _rematch.Visible = view?.Phase == GamePhase.Finished && (local || view.Rematch is not null);
        _rematchCountdown.Visible = !local && _rematch.Visible;
        if (local)
        { _rematch.Text = "Otra partida"; _rematch.Disabled = _pendingRequest != 0; }
        else if (view?.Rematch is { } rematch)
        {
            string count = rematch.ReadyPlayerIds.Length + "/" + rematch.EligiblePlayerIds.Length;
            _rematch.Text = (rematch.ReadyPlayerIds.Contains(view.RecipientId) ? "Apuntado" : "Volver a jugar") + " · " + count;
            _rematch.Disabled = !CanSend || !InteractionActions.CanRequest(view, ActionKind.JoinRematch) || Seconds(rematch.RemainingMilliseconds) == 0;
            _rematchCountdown.Text = rematch.Closed ? "No hay suficientes jugadores para otra partida" :
                "Siguiente partida en " + Seconds(rematch.RemainingMilliseconds) + " s";
        }
        UpdateTableClocks(view, elapsed);
    }
}
