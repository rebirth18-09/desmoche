using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;

internal static class InitialDealTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception("REPARTO: " + message); checks++; }
        for (int count = 2; count <= 4; count++)
            for (int recipient = 1; recipient <= count; recipient++)
            {
                var view = new SnapshotDto(NetworkProtocol.Version, new string('a', 32), 2, 2, recipient,
                    GamePhase.InitialExchange, MatchResult.None, null, null, null,
                    Enumerable.Range(1, count).Select(id => new PublicPlayerDto(id, "Jugador " + id, id - 1, 9, true, false)).ToArray(),
                    Enumerable.Range((recipient - 1) * 9, 9).ToArray(), Array.Empty<MeldDto>(),
                    52 - count * 9, 0, null, null, Array.Empty<RevealedHandDto>(), false);
                string before = NetworkProtocol.Encode(view);
                DealVisualCard[] plan = InitialDealPresentation.Build(view).ToArray();
                Check(plan.Length == count * 9 && plan.GroupBy(c => c.PlayerId).All(g => g.Count() == 9),
                    "Entrega nueve cartas a cada participante, con dos a cuatro jugadores.");
                Check(plan.Where(c => c.PlayerId != recipient).All(c => c.OwnCardId is null),
                    "Ni el plan ni las cartas en vuelo contienen identidades ajenas.");
                Check(plan.Where(c => c.PlayerId == recipient).Select(c => c.OwnCardId!.Value).SequenceEqual(view.OwnHand),
                    "Solo la mano propia autorizada se revela en su orden.");
                Check(plan.Select(c => c.PlayerId).SequenceEqual(Enumerable.Range(0, 9).SelectMany(_ => Enumerable.Range(1, count))),
                    "Las nueve rondas siguen el orden de asientos hacia la derecha.");
                Check(NetworkProtocol.Encode(view) == before, "Planificar no cambia el snapshot ni las cartas reales.");
                Check(InitialDealPresentation.Duration(plan.Length) is >= 2 and <= 4,
                    "El reparto dura entre dos y cuatro segundos.");
                Check(!InitialDealPresentation.CanStart(view with { Phase = GamePhase.AwaitingClaims }, view),
                    "Una vista de intercambio fuera de un inicio no reproduce el reparto.");
                Check(!InitialDealPresentation.CanStart(null, view with { OwnResponseSubmitted = true }),
                    "No se reparten de nuevo cartas tras confirmar el cambio propio.");
            }
        var observer = new SnapshotDto(NetworkProtocol.Version, new string('b', 32), 5, 4, 1,
            GamePhase.InitialExchange, MatchResult.None, null, null, null,
            new[] { new PublicPlayerDto(2, "Dos", 0, 9, true, false), new PublicPlayerDto(3, "Tres", 1, 9, true, false) },
            Array.Empty<int>(), Array.Empty<MeldDto>(), 34, 0, null, null, Array.Empty<RevealedHandDto>(), false,
            OwnSpectating: true);
        Check(InitialDealPresentation.Build(observer).Count == 18 &&
            InitialDealPresentation.Build(observer).All(c => c.OwnCardId is null),
            "Host espectador ve solo entregas ocultas y no recibe mano.");
        Check(InitialDealPresentation.CanStart(observer with { Phase = GamePhase.Finished }, observer),
            "La revancha permite otra animacion con los participantes actuales.");
        Console.WriteLine($"REPARTO VISUAL: {checks} comprobaciones correctas.");
        return checks;
    }
}
