using System;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Desmoche.Sessions;
using Godot;

namespace Desmoche.Diagnostics;

public partial class HandDragSmoke
{
    // Controla únicamente el orden de llegada de Reply y ViewChanged para la
    // prueba visual. No pretende simular ni sustituir las reglas del servidor.
    private sealed class PaymentSession : IGameSession
    {
        public bool IsActive => true;
        public bool IsLocal => false;
        public bool CanManageMatch => false;
        public SnapshotDto? View { get; private set; }
        public string Status => "Pago de prueba";
        public event Action<SnapshotDto>? ViewChanged;
        public event Action<ReplyDto>? ActionCompleted;
        public event Action? SessionChanged;
        public int Requests { get; private set; }
        public int? SubmittedCard { get; private set; }
        public bool RefuseSubmission { get; set; }
        public PaymentSession(SnapshotDto view) { View = view; }
        public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
            bool declareWin = false, InitialVictoryKind? initialKind = null, int[]? cards = null, int? sourceMeldId = null)
        {
            if (kind != ActionKind.Pay || cardId is not int id || !View!.OwnHand.Contains(id))
                throw new InvalidOperationException("Solicitud inesperada en fixture de pago.");
            if (RefuseSubmission) return 0;
            Requests++; SubmittedCard = cardId; return 100 + Requests;
        }
        public void Reply(bool success) => ActionCompleted?.Invoke(new ReplyDto(100 + Requests,
            success, success ? "Pago aceptado" : "Pago rechazado", false, View!.Revision));
        public void Update(SnapshotDto view) { View = view; ViewChanged?.Invoke(view); }
        public void Close(string reason = "Partida cerrada.") { SessionChanged?.Invoke(); }
    }

    private Button PaymentButton(string name) => _mesa.GetNode<Button>(Mesa.ContextActionsPath + name);
    private Carta PaymentCard() => _mesa.GetNode<Carta>(Mesa.CenterPath + "Pago/Content/Carta");
    private int[] VisibleHandIds() => Cards().Where(card => card.IsVisibleInTree())
        .Select(card => card.FaceCardId!.Value).ToArray();

    private SnapshotDto PaymentFixture() => _view with
    {
        Session = Guid.NewGuid().ToString("N"), Revision = 1, Window = 20,
        Phase = GamePhase.AwaitingPayment, CurrentPlayerId = 1, PaymentPlayerId = 1,
        Players = new[] { new PublicPlayerDto(1, "Tú", 0, 4, true, false),
            new PublicPlayerDto(2, "Bot Alba", 1, 6, true, false), new PublicPlayerDto(3, "Bot Bruno", 2, 6, true, false) },
        OwnHand = new[] { 50, 46, 47, 22 },
        Melds = new[] { new MeldDto(1, 1, new[] { 10, 23, 49 }), new MeldDto(2, 1, new[] { 16, 42, 29 }),
            new MeldDto(3, 2, new[] { 25, 51, 38 }), new MeldDto(4, 3, new[] { 17, 18, 19 }) },
        DeckCount = 22, DiscardCount = 2, DiscardTop = 21
    };

    private async Task StagePayment(int id)
    {
        Carta source = Card(id); Vector2 start = source.GetGlobalRect().GetCenter();
        Vector2 target = PaymentCard().GetGlobalRect().GetCenter();
        await Begin(source, start, target); await Release(target);
    }

    private async Task ValidatePaymentPreparation()
    {
        SnapshotDto original = _view;
        foreach (Vector2I resolution in new[] { new Vector2I(640, 360), new(844, 390), new(1280, 720),
            new(1920, 1080), new(2400, 1080), new(1024, 768), new(3440, 1440) })
        {
            _viewport.Size = resolution;
            var session = new PaymentSession(PaymentFixture()); _mesa.Bind(session); await Settle();
            string before = NetworkProtocol.Encode(session.View!);
            var objects = Cards().ToDictionary(card => card.FaceCardId!.Value);
            await StagePayment(50);
            Check(_mesa.StagedCardId == 50 && PaymentCard().FaceCardId == 50 && !Card(50).Visible && !Card(50).Interactive,
                "Al soltar en Pago, la carta solo queda visible en la casilla");
            Check(VisibleHandIds().SequenceEqual(new[] { 46, 47, 22 }) && session.Requests == 0 &&
                NetworkProtocol.Encode(session.View!) == before, "Preparar retira el dibujo de la fila sin pagar ni modificar la mano real");
            Check(Descendants(_mesa).OfType<Carta>().Count(card => card.IsVisibleInTree() && card.FaceCardId == 50) == 1,
                "No hay duplicado visible después del fin del drag nativo");
            Check(Cards().All(card => ReferenceEquals(card, objects[card.FaceCardId!.Value])) &&
                Cards().All(card => card.OffsetTransformPosition.IsZeroApprox()), "Preparar reutiliza nodos y no deja desplazamientos residuales");
            Vector2[] compact = VisibleHandIds().Select(id => Card(id).Position).ToArray();
            Check(compact.Zip(compact.Skip(1)).All(pair =>
                Mathf.Abs(pair.Second.X - pair.First.X - Card(46).Size.X - 12) < 1), "Las restantes cierran el espacio de la carta preparada");
            if (resolution == new Vector2I(1280, 720)) await Capture("Pago-sin-carta-duplicada-1280x720.png");
            if (resolution == new Vector2I(844, 390)) await Capture("Pago-sin-carta-duplicada-844x390.png");

            await StagePayment(46);
            Check(_mesa.StagedCardId == 46 && Card(50).Visible && !Card(46).Visible &&
                VisibleHandIds().SequenceEqual(new[] { 50, 47, 22 }), "Preparar otra devuelve la anterior a su lugar");
            Carta last = Card(22); Vector2 start = last.GetGlobalRect().GetCenter();
            Vector2 first = Card(50).GetGlobalRect().GetCenter();
            await Begin(last, start, first); await Release(first);
            Check(VisibleHandIds().SequenceEqual(new[] { 22, 50, 47 }) && !Card(46).Visible && _mesa.StagedCardId == 46,
                "El arrastre suave ordena únicamente las cartas visibles y conserva el pago preparado");
            await Click(PaymentButton("CancelCardButton"));
            Check(_mesa.StagedCardId is null && Cards().All(card => card.Visible && card.Interactive) &&
                VisibleHandIds().SequenceEqual(_mesa.LocalHandOrder), "Cambiar carta devuelve una sola copia al orden local correcto");

            await StagePayment(50);
            session.Update(session.View! with { Revision = 2 }); await Settle();
            Check(!Card(50).Visible && PaymentCard().FaceCardId == 50,
                "Una revisión del mismo contexto conserva la preparación sin duplicar la carta");
            Feed(new InputEventKey { Pressed = true, Keycode = Key.Escape }); await Settle();
            Check(_mesa.StagedCardId is null && Cards().All(card => card.Visible), "Escape restaura la carta preparada");
            await StagePayment(50);
            session.Update(session.View! with { Revision = 3, Window = 21, Phase = GamePhase.AwaitingDraw,
                PaymentPlayerId = null }); await Settle();
            Check(_mesa.StagedCardId is null && Cards().All(card => card.Visible) && session.Requests == 0,
                "Cambiar de fase cancela la preparación visual sin pagar");
            _mesa.Unbind();
            GD.Print("PAYMENT PREPARATION CASE: " + resolution);
        }

        _viewport.Size = new Vector2I(1280, 720);
        var pending = new PaymentSession(PaymentFixture()); _mesa.Bind(pending); await Settle();
        await StagePayment(50); await Click(PaymentButton("ConfirmCardButton"));
        Check(pending.Requests == 1 && pending.SubmittedCard == 50 && !Card(50).Visible && PaymentCard().FaceCardId == 50,
            "Aceptar envía un pago y mantiene la carta en su casilla mientras espera respuesta");
        await Click(PaymentButton("ConfirmCardButton"));
        Check(pending.Requests == 1 && PaymentButton("CancelCardButton").Disabled,
            "Una solicitud pendiente bloquea confirmaciones repetidas y cambios de carta");
        pending.Update(pending.View! with { Revision = 2 }); await Settle();
        Check(!Card(50).Visible && PaymentCard().FaceCardId == 50, "Una actualización intermedia no hace reaparecer la carta pendiente");
        pending.Reply(false); await Settle();
        Check(_mesa.StagedCardId is null && Card(50).Visible && Card(50).Interactive && PaymentCard().FaceCardId is null,
            "Un pago rechazado devuelve la carta a la mano sin duplicarla");

        await StagePayment(46); await Click(PaymentButton("ConfirmCardButton")); pending.Reply(true); await Settle();
        Check(!Card(46).Visible && PaymentCard().FaceCardId == 46 && pending.View!.OwnHand.Contains(46),
            "Reply aceptado por sí solo no inventa un cambio en la mano real");
        pending.Update(PaidView(pending.View!, 46)); await Settle();
        Check(_mesa.StagedCardId is null && !VisibleHandIds().Contains(46) && VisibleHandIds().Length == 3 &&
            _mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta").FaceCardId == 46,
            "El snapshot confirmado retira la carta real y la muestra como oferta del pago");
        _mesa.Unbind();

        var reverse = new PaymentSession(PaymentFixture()); _mesa.Bind(reverse); await Settle();
        await StagePayment(50); await Click(PaymentButton("ConfirmCardButton"));
        reverse.Update(PaidView(reverse.View!, 50)); reverse.Reply(true); await Settle();
        Check(_mesa.StagedCardId is null && VisibleHandIds().Length == 3 && !VisibleHandIds().Contains(50),
            "Snapshot antes del Reply también limpia la casilla sin restaurar una carta ya pagada");
        _mesa.Unbind();

        var failedSend = new PaymentSession(PaymentFixture()) { RefuseSubmission = true };
        _mesa.Bind(failedSend); await Settle(); await StagePayment(50); await Click(PaymentButton("ConfirmCardButton"));
        Check(failedSend.Requests == 0 && _mesa.StagedCardId == 50 && !Card(50).Visible && !PaymentButton("CancelCardButton").Disabled,
            "Un envío que no comienza conserva la preparación y permite cambiarla");
        failedSend.RefuseSubmission = false; await Click(PaymentButton("ConfirmCardButton"));
        await ToSignal(GetTree().CreateTimer(10.2), Timer.SignalName.Timeout); await Settle();
        Check(_mesa.StagedCardId is null && Card(50).Visible && Card(50).Interactive,
            "Vencer la espera de respuesta restaura el último estado recibido");
        await StagePayment(50); _mesa.Unbind(); _mesa.DisplaySnapshot(null, "Salir"); await Settle();
        Check(Cards().Length == 0 && _mesa.StagedCardId is null, "Salir limpia la preparación y su carta oculta");
        _view = original;
    }

    private static SnapshotDto PaidView(SnapshotDto view, int id) => view with
    {
        Revision = view.Revision + 1, Window = view.Window + 1, Phase = GamePhase.AwaitingClaims,
        PaymentPlayerId = null, OfferedCard = id, OwnHand = view.OwnHand.Where(card => card != id).ToArray(),
        Players = view.Players.Select(player => player.Id == 1 ? player with { CardCount = player.CardCount - 1 } : player).ToArray()
    };
}
