using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Sesion ENet real. Solo accede a la vista autorizada y a controles publicos de Mesa.
public partial class MesaNetworkSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private NetworkManager _net = null!;
    private Mesa _table = null!;
    private ulong _began, _lastAction, _quitAt;
    private bool _done, _startSent, _exchanged, _continued, _sawPayment, _awaitingReply;
    private ActionKind _requested;
    private long _actedWindow = -1;
    private long _startRequest;
    private GamePhase _actedPhase;
    private int _checks, _exitCode, _payments;
    private string _lastState = "";

    public override void _Ready()
    {
        GD.Print("MESA NETWORK READY: inicializando escena normal.");
        base._Ready();
        _net = GetNode<NetworkManager>("NetworkManager");
        _table = GetNode<Mesa>("Margin/Screens/Mesa");
        string[] args = OS.GetCmdlineUserArgs();
        string mode = Option(args, "--mode", "host");
        int port = int.TryParse(Option(args, "--port", "7129"), out int n) ? n : 7129;
        _began = Time.GetTicksMsec();
        _net.ActionCompleted += OnReply;
        _net.ConnectionChanged += (state, message) => GD.Print("MESA NETWORK CONNECTION: " + state + " / " + message);
        Error error = mode == "host" ? _net.Host("Ana", port) : _net.Join("127.0.0.1", "Pedro", port);
        GD.Print("MESA NETWORK START: " + mode + " / " + error + " / " + _net.State);
        if (error != Error.Ok) Finish(false, "No se pudo abrir ENet: " + error);
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            if (Time.GetTicksMsec() >= _quitAt) { _net.Close(); GetTree().Quit(_exitCode); }
            return;
        }
        try
        {
            if (Time.GetTicksMsec() - _began > 45000)
                throw new InvalidOperationException("Tiempo agotado sin completar una oferta y pago reales. " +
                    _net.State + " / " + _net.Status + " / Vista=" + (_net.View?.Phase.ToString() ?? "null"));
            if (_net.View is not { } view) return;
            string state = view.Phase + "/" + view.Window + "/" + view.Players.Length;
            if (state != _lastState)
            { _lastState = state; GD.Print("MESA NETWORK VIEW: " + state); }
            if (view.Phase == GamePhase.AwaitingPayment) _sawPayment = true;
            if (_sawPayment && _exchanged && _continued && !_awaitingReply &&
                view.Phase == GamePhase.AwaitingDraw && view.DiscardCount >= 2)
            { Finish(true, "Intercambio confirmado, controles de turno/oferta y pago autoritativo."); return; }
            if (_awaitingReply || Time.GetTicksMsec() - _lastAction < 120) return;
            if (view.Phase == GamePhase.Preparing && _net.IsHost && view.Players.Length == 2 && !_startSent)
            { _startSent = true; _startRequest = _net.Submit(ActionKind.StartMatch); return; }
            if (view.OwnResponseSubmitted || _actedWindow == view.Window && _actedPhase == view.Phase) return;
            if (view.Phase == GamePhase.InitialExchange)
            {
                Check(view.OwnHand.Length == 9, "Reparto real de nueve cartas.");
                StageAndConfirm(view, ActionKind.ExchangeCard);
            }
            else if (view.Phase == GamePhase.InitialDeclarations)
                Press(view, ActionKind.FinishInitial, "ContinueButton");
            else if (view.Phase == GamePhase.AwaitingDraw && view.CurrentPlayerId == view.RecipientId)
                Press(view, ActionKind.DrawDeck, "DrawDeckButton");
            else if (view.Phase == GamePhase.AwaitingClaims)
            {
                int[]? group = FindClaim(view);
                if (group is null) Press(view, ActionKind.Pass, "PassButton");
                else { PrepareClaim(view, group); Press(view, ActionKind.Claim, "ClaimButton"); }
            }
            else if (view.Phase == GamePhase.AwaitingPayment && view.PaymentPlayerId == view.RecipientId)
                StageAndConfirm(view, ActionKind.Pay);
        }
        catch (Exception error) { Finish(false, error.Message); }
    }

    private void StageAndConfirm(SnapshotDto view, ActionKind kind)
    {
        string original = NetworkProtocol.Encode(view);
        int id = view.OwnHand[0];
        var slot = _table.GetNode<CardDropZone>(Mesa.CenterPath + "Pago");
        Variant drag = CardDragData.Create(view, id, CardDragOrigin.Hand);
        Check(slot.Visible && slot._CanDropData(Vector2.Zero, drag), "Casilla autoritativa disponible.");
        slot._DropData(Vector2.Zero, drag);
        Check(_table.StagedCardId == id, "Soltar prepara exactamente una carta.");
        Check(NetworkProtocol.Encode(_net.View!) == original, "Soltar no envia ni modifica la mano.");
        Button confirm = _table.GetNode<Button>(Mesa.ContextActionsPath + "ConfirmCardButton");
        Check(confirm.IsVisibleInTree() && !confirm.Disabled, "Aceptar requiere sesion real vigente.");
        MarkRequest(view, kind);
        confirm.EmitSignal(Button.SignalName.Pressed);
        Check(_table.StagedCardId is null && !confirm.Visible, "Aceptar limpia la preparacion y oculta el boton.");
        Check(NetworkProtocol.Encode(_net.View!) == original, "Enviar no aplica optimistamente el pago.");
        confirm.EmitSignal(Button.SignalName.Pressed);
        Check(_table.StagedCardId is null, "Otro Aceptar no reutiliza la carta ni duplica la solicitud.");
    }

    private void PrepareClaim(SnapshotDto view, int[] group)
    {
        string original = NetworkProtocol.Encode(view);
        var zone = _table.GetNode<CardDropZone>(Mesa.InteractionPath + "DraftZone");
        for (int i = 0; i < group.Length; i++)
        {
            Variant drag = CardDragData.Create(view, group[i], group[i] == view.OfferedCard
                ? CardDragOrigin.External : CardDragOrigin.Hand);
            if (i == 0) zone._DropData(Vector2.Zero, drag);
            else
            {
                Carta last = _table.GetNode<Carta>(Mesa.InteractionPath +
                    "DraftZone/Content/Rows/Borrador0/Cards/Carta" + group[i - 1]);
                last._DropData(new Vector2(last.Size.X, 0), drag);
            }
        }
        Check(_table.LocalDraftCount == 1, "La oferta se prepara sin menu mediante destinos de drag.");
        Check(NetworkProtocol.Encode(_net.View!) == original, "Preparar la oferta conserva la mano privada.");
    }

    private void Press(SnapshotDto view, ActionKind kind, string node)
    {
        Button button = _table.GetNode<Button>(Mesa.ContextActionsPath + node);
        Check(button.IsVisibleInTree() && !button.Disabled, "Control contextual disponible: " + node);
        MarkRequest(view, kind); button.EmitSignal(Button.SignalName.Pressed);
    }

    private void MarkRequest(SnapshotDto view, ActionKind kind)
    {
        _requested = kind; _awaitingReply = true;
        _actedWindow = view.Window; _actedPhase = view.Phase; _lastAction = Time.GetTicksMsec();
    }

    private void OnReply(ReplyDto reply)
    {
        if (!_awaitingReply || reply.RequestId == 0 || reply.RequestId == _startRequest) return;
        _awaitingReply = false;
        if (!reply.Success) { Finish(false, "El servidor rechazo " + _requested + ": " + reply.Message); return; }
        if (_requested == ActionKind.ExchangeCard) _exchanged = true;
        if (_requested == ActionKind.FinishInitial) _continued = true;
        if (_requested == ActionKind.Pay) _payments++;
        GD.Print("MESA NETWORK ACTION: " + _requested + " confirmado; revision=" + reply.Revision);
    }

    // Las vistas del diagnostico contienen solo la propia mano y la oferta publica.
    // Buscar aqui una jugada permite verificar el envio; el servidor la vuelve a validar.
    private static int[]? FindClaim(SnapshotDto view)
    {
        if (view.OfferedCard is not int offered) return null;
        foreach (int first in view.OwnHand)
            foreach (int second in view.OwnHand.Where(id => id != first))
                foreach (int[] candidate in new[] { new[] { first, second, offered },
                    new[] { first, offered, second }, new[] { offered, first, second } })
                    if (RulesValidator.ValidateMeld(candidate.Select(Card.FromId).ToArray()).IsValid)
                        return candidate;
        return null;
    }

    private void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks++;
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true; _exitCode = success ? 0 : 1; _quitAt = Time.GetTicksMsec() + 1000;
        if (success) GD.Print($"MESA NETWORK PASS: {message} Comprobaciones={_checks}; PagosPropios={_payments}");
        else GD.PushError("MESA NETWORK FAIL: " + message);
    }

    private static string Option(string[] args, string key, string fallback)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
