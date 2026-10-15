using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;

namespace Desmoche.Interaccion.Pruebas;

internal static class PublishedTransferTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool valid, string message)
        { if (!valid) throw new Exception(message); checks++; }
        var view = new SnapshotDto(NetworkProtocol.Version, new string('a', 32), 1, 1, 1,
            GamePhase.AwaitingClaims, MatchResult.None, 1, null, null,
            new[] { new PublicPlayerDto(1, "Yo", 0, 5, true, false), new PublicPlayerDto(2, "Otro", 1, 9, true, false) },
            new[] { 0, 1, 17, 24, 23 }, new[] { new MeldDto(70, 1, new[] { 13, 14, 15, 16 }) },
            33, 0, null, 26, Array.Empty<RevealedHandDto>(), false);
        var hand = new HandInteractionState(); hand.Synchronize(view);
        string before = NetworkProtocol.Encode(view);
        Check(hand.StageCards(13), "Se puede arrastrar el As de A234 publicado a un juego nuevo.");
        Check(hand.Groups.Count == 2 && hand.Groups[0].MeldId == 70 && hand.Groups[0].Cards.SequenceEqual(new[] { 14, 15, 16 }),
            "El borrador incluye el origen reducido 234 y un destino separado.");
        Check(hand.StageCards(0, groupIndex: 1) && hand.StageCards(26, groupIndex: 1),
            "El destino admite el As propio de la mano y el ofrecido.");
        Check(hand.Groups[1].Cards.SequenceEqual(new[] { 13, 0, 26 }), "La tercia conserva el orden preparado.");
        Check(InteractionActions.TryCreate(view, hand, ActionKind.Claim, false, out var request, out _) &&
            request!.Plays!.Length == 2, "El reclamo envia origen y destino juntos.");
        Check(!InteractionActions.TryCreate(view, hand, ActionKind.Publish, false, out _, out _),
            "Publicar no puede tomar la oferta sin reclamarla.");
        Check(NetworkProtocol.Encode(view) == before && hand.Order.SequenceEqual(view.OwnHand),
            "Trasmochar en el borrador no altera la mano ni el snapshot autorizado.");
        Check(!hand.StageCards(13), "No se asigna la misma carta publicada dos veces.");
        Check(hand.RemoveGroup(1) && hand.Groups.Count == 0, "Quitar destino cancela tambien su origen reducido.");
        Check(!hand.StageCards(14) && hand.Groups.Count == 0, "No se retira el 2: A34 no es un juego valido.");
        Check(!hand.StageCards(51), "No se retira una carta publicada ajena o inexistente.");
        Check(hand.StageCards(13) && hand.RemoveGroup(0) && hand.Groups.Count == 0,
            "Quitar el origen cancela tambien el destino dependiente.");
        Check(hand.StageCards(13) && hand.StageCards(17, targetMeldId: 70), "El origen reducido admite una ampliacion local.");
        Check(hand.ReturnToHand(0, 17) && hand.Groups[0].Cards.SequenceEqual(new[] { 14, 15, 16 }),
            "Deshacer una adicion al origen conserva la reduccion necesaria para el traslado.");
        hand.Synchronize(view with { Window = 2, OwnResponseSubmitted = true });
        Check(hand.Groups.Count == 0, "Cambiar de ventana cancela la transaccion local completa.");
        var fourth = view with { Phase = GamePhase.AwaitingDraw, OfferedCard = null,
            OwnHand = new[] { 17, 23 }, Melds = new[] { view.Melds[0], new MeldDto(71, 1, new[] { 0, 26, 39 }) } };
        hand.Synchronize(fourth);
        Check(hand.StageCards(13, targetMeldId: 71) && hand.Groups[1].Cards.SequenceEqual(new[] { 0, 26, 39, 13 }) &&
            InteractionActions.TryCreate(fourth, hand, ActionKind.Publish, false, out _, out _),
            "Una carta del origen completa la cuarta propia y prepara ambas actualizaciones.");
        var extend = fourth with { Melds = new[] {
            new MeldDto(70, 1, new[] { 3, 16, 42, 29 }), new MeldDto(72, 1, new[] { 26, 27, 28 }) } };
        hand.Synchronize(extend);
        Check(hand.StageCards(29, targetMeldId: 72) && hand.Groups[1].Cards.SequenceEqual(new[] { 26, 27, 28, 29 }) &&
            InteractionActions.TryCreate(extend, hand, ActionKind.Publish, false, out _, out _),
            "Trasladar de una cuarta permite continuar una escalera propia.");
        hand.Synchronize(view with { Melds = new[] { view.Melds[0] with { OwnerId = 2 } } });
        Check(!hand.StageCards(13), "Una carta de un juego rival no se prepara como propia.");
        Console.WriteLine($"TRASMOCHE INTERACCION: {checks} comprobaciones correctas.");
        return checks;
    }
}
