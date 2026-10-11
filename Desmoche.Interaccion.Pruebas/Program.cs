using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;

namespace Desmoche.Interaccion.Pruebas;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            SelectionAndOrder();
            LocalGroups();
            ExternalSources();
            ExistingMelds();
            Reconciliation();
            DefensiveCopies();
            InvalidData();
            _checks += ActionInteractionTests.Run();
            _checks += IncrementalDraftTests.Run();
            Console.WriteLine($"PRUEBAS INTERACCION COMPLETADAS: {_checks} comprobaciones correctas.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            return 1;
        }
    }

    private static void SelectionAndOrder()
    {
        var state = new HandInteractionState();
        Check(state.Order.Count == 0, "La mano local comienza vacia.");
        Check(!state.Toggle(0), "No se seleccionan cartas antes de recibir una vista.");
        SnapshotDto view = View();
        string before = NetworkProtocol.Encode(view);
        state.Synchronize(view);
        Equal(state.Order, view.OwnHand, "Se representa el orden autorizado inicial.");
        Check(!state.Toggle(51), "No se selecciona una carta ajena.");
        Check(state.Toggle(2) && state.Toggle(0), "Se permite seleccion multiple.");
        Equal(state.SelectedIds, new[] { 0, 2 }, "Seleccion conserva orden visual y no orden de clics.");
        Check(state.Move(2, 0), "La carta puede moverse al inicio.");
        Equal(state.Order, new[] { 2, 0, 1, 4, 5, 6 }, "Arrastre local cambia solamente el orden.");
        Equal(state.SelectedIds, new[] { 2, 0 }, "La seleccion acompana el nuevo orden.");
        Check(state.Move(2, state.Order.Count), "La carta puede moverse al final.");
        Equal(state.Order, new[] { 0, 1, 4, 5, 6, 2 }, "El hueco Count inserta al final.");
        Check(state.Move(0, 1), "Mover a un hueco equivalente se admite.");
        Equal(state.Order, new[] { 0, 1, 4, 5, 6, 2 }, "Un hueco equivalente no altera el orden.");
        int[] order = state.Order.ToArray();
        Check(!state.Move(51, 0), "No se arrastra una carta ajena.");
        Check(!state.Move(0, -1), "No se admite un hueco negativo.");
        Check(!state.Move(0, 7), "No se admite un hueco fuera de la mano.");
        Equal(state.Order, order, "Los movimientos rechazados no cambian la mano visual.");
        Check(state.Toggle(0), "Otro clic deselecciona.");
        Equal(state.SelectedIds, new[] { 2 }, "Deseleccionar no afecta otras cartas.");
        state.ClearSelection();
        Check(state.SelectedIds.Count == 0, "Limpiar seleccion conserva la mano.");
        Check(NetworkProtocol.Encode(view) == before, "Seleccion y arrastre no modifican SnapshotDto.");
    }

    private static void LocalGroups()
    {
        var state = new HandInteractionState();
        state.Synchronize(View());
        Select(state, 0, 1);
        Check(!state.PrepareGroup(), "Un borrador necesita al menos tres cartas.");
        Equal(state.SelectedIds, new[] { 0, 1 }, "Preparacion rechazada conserva seleccion.");
        Check(state.Toggle(2) && state.Move(2, 0), "Se prepara un orden declarado manualmente.");
        Check(state.PrepareGroup(), "Se prepara un borrador de tres cartas.");
        Equal(state.Groups[0].Cards, new[] { 2, 0, 1 }, "No se ordenan automaticamente las cartas del borrador.");
        Check(state.Order.Count == 6, "Preparar no retira cartas de la mano autorizada.");
        Check(state.SelectedIds.Count == 0, "Preparar limpia la seleccion.");
        Check(!state.Toggle(0), "Una carta asignada no se selecciona para duplicarla.");
        Select(state, 4, 5, 6);
        Check(state.PrepareGroup(), "Se admite otra agrupacion disjunta.");
        Check(state.Groups.Count == 2, "Ambos borradores permanecen separados.");
        Check(state.MoveInGroup(0, 2, 3), "Se reorganiza una carta dentro del borrador.");
        Equal(state.Groups[0].Cards, new[] { 0, 1, 2 }, "Mover dentro del borrador conserva su composicion.");
        Check(!state.MoveInGroup(-1, 0, 0), "Se rechaza un indice negativo de grupo.");
        Check(!state.MoveInGroup(2, 0, 0), "Se rechaza un indice fuera de grupos.");
        Check(!state.MoveInGroup(0, 4, 0), "No se mueve una carta de otro grupo.");
        Check(!state.MoveInGroup(0, 0, 4), "No se mueve fuera de los huecos del grupo.");
        Check(!state.RemoveGroup(8), "No se elimina un borrador inexistente.");
        Check(state.RemoveGroup(0), "Se puede eliminar un borrador.");
        Check(state.Toggle(0), "Eliminar libera cartas para otra seleccion.");
        state.ClearGroups();
        Check(state.Groups.Count == 0 && state.Order.Count == 6, "Limpiar borradores conserva la mano.");

        state = new HandInteractionState();
        state.Synchronize(View() with { OwnHand = Enumerable.Range(0, 14).ToArray() });
        Select(state, Enumerable.Range(0, 14).ToArray());
        Check(!state.PrepareGroup(), "Un borrador no excede trece cartas.");
        Check(state.Toggle(13), "Se puede quitar una carta del borrador pendiente.");
        Check(state.PrepareGroup() && state.Groups[0].Cards.Count == 13,
            "Trece cartas se admiten como datos sin validar su juego.");
    }

    private static void ExternalSources()
    {
        var state = new HandInteractionState();
        SnapshotDto claims = View() with { Phase = GamePhase.AwaitingClaims, OfferedCard = 20 };
        state.Synchronize(claims);
        Check(state.ExternalCardId == 20, "Una oferta publica se expone por separado.");
        Check(!state.Order.Contains(20), "La oferta no se incorpora a la mano autorizada.");
        Check(state.SelectExternal(true), "La oferta puede seleccionarse para un borrador.");
        Select(state, 0, 1);
        Check(state.PrepareGroup(), "Se prepara con dos cartas propias y una oferta.");
        Equal(state.Groups[0].Cards, new[] { 0, 1, 20 }, "La oferta se agrega sin mover cartas del servidor.");
        Check(!state.ExternalSelected, "Preparar limpia seleccion externa.");
        Check(!state.SelectExternal(true), "Una oferta no se reutiliza en otro borrador.");
        Check(state.RemoveGroup(0) && state.SelectExternal(true), "Eliminar libera la oferta.");
        Check(state.SelectExternal(false) && !state.ExternalSelected, "La oferta se deselecciona.");
        state.Synchronize(claims with { Revision = 2, OwnResponseSubmitted = true });
        Check(state.ExternalCardId is null && !state.SelectExternal(true), "Una respuesta enviada cierra seleccion de oferta.");
        state.Synchronize(View() with { Phase = GamePhase.AwaitingDraw, DiscardTop = 21, DiscardCount = 1 });
        Check(state.ExternalCardId == 21 && state.SelectExternal(true), "El jugador actual puede preparar con el pozo.");
        state.Synchronize(View() with { Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 2, DiscardTop = 21 });
        Check(state.ExternalCardId is null, "El pozo no se selecciona fuera del turno propio.");
        state.Synchronize(View() with { Phase = GamePhase.AwaitingPayment, DiscardTop = 21, OfferedCard = 20 });
        Check(state.ExternalCardId is null, "Pagar no trata al pozo ni la oferta como carta propia.");
        state.Synchronize(claims with { Players = Players(eliminated: true) });
        Check(state.ExternalCardId is null, "Un eliminado no prepara reclamos.");
        state.Synchronize(claims with { Players = Players(connected: false) });
        Check(state.ExternalCardId is null, "Un desconectado no prepara reclamos.");
    }

    private static void ExistingMelds()
    {
        var state = new HandInteractionState();
        SnapshotDto view = View() with { Melds = new[] {
            new MeldDto(7, 1, new[] { 10, 11, 12 }),
            new MeldDto(8, 2, new[] { 40, 41, 42 }) } };
        state.Synchronize(view);
        Check(!state.PrepareGroup(7), "Extender un juego exige una carta nueva.");
        Select(state, 0);
        Check(!state.PrepareGroup(8), "No se prepara una extension de juego rival.");
        Check(!state.PrepareGroup(0), "No se usa un identificador de juego invalido.");
        Check(state.PrepareGroup(7), "Se prepara una extension propia.");
        Equal(state.Groups[0].Cards, new[] { 10, 11, 12, 0 }, "Extension incluye todas las cartas publicas anteriores.");
        Check(state.Groups[0].MeldId == 7, "Se conserva el ID del juego a extender.");
        Select(state, 1);
        Check(!state.PrepareGroup(7), "No se repite el mismo destino entre borradores.");
        Equal(state.SelectedIds, new[] { 1 }, "Rechazar destino repetido conserva seleccion.");
        Check(state.MoveInGroup(0, 0, 0), "Las cartas anteriores y nuevas pueden ordenarse juntas.");
        Equal(state.BuildPlays()[0].Cards, new[] { 0, 10, 11, 12 }, "La solicitud conserva el orden elegido.");
        Check(state.BuildPlays()[0].MeldId == 7, "La solicitud identifica la extension propia.");

        state = new HandInteractionState();
        state.Synchronize(view with { Melds = new[] {
            new MeldDto(7, 1, new[] { 10, 11, 12 }),
            new MeldDto(9, 1, new[] { 10, 13, 14 }) } });
        Select(state, 0);
        Check(state.PrepareGroup(7), "Primer destino prepara su copia publica.");
        Select(state, 1);
        Check(!state.PrepareGroup(9), "No se reutiliza una carta publica entre borradores.");
    }

    private static void Reconciliation()
    {
        var state = new HandInteractionState();
        SnapshotDto view = View();
        state.Synchronize(view);
        state.Move(2, 0);
        Select(state, 2, 0, 1);
        Check(state.PrepareGroup(), "Se crea un borrador antes de recibir otras revisiones.");
        state.Toggle(4);
        state.Synchronize(view with { Revision = 2 });
        Check(state.Groups.Count == 1, "Una revision ajena conserva borradores.");
        Equal(state.Order, new[] { 2, 0, 1, 4, 5, 6 }, "Una revision ajena conserva orden local.");
        Equal(state.SelectedIds, new[] { 4 }, "Una revision ajena conserva seleccion local.");
        state.Synchronize(view with { Revision = 3, OwnHand = new[] { 0, 1, 2, 4, 6, 8 } });
        Equal(state.Order, new[] { 2, 0, 1, 4, 6, 8 }, "Reconciliar retira cartas ausentes y agrega nuevas al final.");
        Check(state.Groups.Count == 0, "Cambio de mano invalida borradores anteriores.");
        Equal(state.SelectedIds, new[] { 4 }, "Una seleccion propia vigente sobrevive al cambio de mano.");
        state.Synchronize(view with { Revision = 4, OwnHand = new[] { 0, 1, 2, 6, 8 } });
        Check(state.SelectedIds.Count == 0, "Reconciliar elimina selecciones de cartas que salieron.");
        state.Synchronize(view);
        Select(state, 0, 1, 2);
        state.PrepareGroup();
        state.Toggle(4);
        state.Synchronize(view with { Window = 2 });
        Check(state.Groups.Count == 0 && state.SelectedIds.Count == 0, "Otra ventana limpia borradores y seleccion.");
        state.Toggle(0);
        state.Synchronize(view with { Window = 2, Phase = GamePhase.InitialExchange });
        Check(state.SelectedIds.Count == 0, "Cambiar de fase limpia seleccion pendiente.");
        state.Synchronize(view);
        Select(state, 0, 1, 2);
        state.PrepareGroup();
        state.Synchronize(view with { Melds = new[] { new MeldDto(7, 1, new[] { 10, 11, 12 }) } });
        Check(state.Groups.Count == 0, "Cambiar los juegos propios invalida borradores.");
        state.Synchronize(view);
        Select(state, 0, 1, 2);
        state.PrepareGroup();
        state.Synchronize(view with { Melds = new[] { new MeldDto(7, 2, new[] { 10, 11, 12 }) } });
        Check(state.Groups.Count == 1, "Cambiar un juego rival no invalida borradores propios.");
        state.Synchronize(view with { RevealedHands = new[] {
            new RevealedHandDto(2, new[] { 40, 41, 42 }) } });
        Check(!state.Order.Contains(40) && !state.Order.Contains(41) && !state.Order.Contains(42),
            "La mano visual propia nunca incorpora cartas de otros jugadores.");
        state.Toggle(4);
        state.Synchronize(view with { OwnResponseSubmitted = true });
        Check(state.Groups.Count == 0 && state.SelectedIds.Count == 0,
            "Confirmar una respuesta invalida la intencion local anterior.");
        state.Synchronize(view);
        Select(state, 0, 1, 2);
        state.PrepareGroup();
        state.Toggle(4);
        state.Synchronize(view with { Players = Players(eliminated: true) });
        Check(state.Groups.Count == 0 && state.SelectedIds.Count == 0,
            "La eliminacion cancela borradores y seleccion pendiente.");
        state.Synchronize(view with { Session = new string('b', 32) });
        Equal(state.Order, view.OwnHand, "Otra sesion reinicia orden local.");
        Check(state.Groups.Count == 0, "Otra sesion elimina borradores.");
        state.Move(2, 0);
        state.Synchronize(view with { RecipientId = 2 });
        Equal(state.Order, view.OwnHand, "Otro destinatario reinicia orden local.");
        state.Synchronize(null);
        Check(state.Order.Count == 0 && state.Groups.Count == 0 &&
            state.SelectedIds.Count == 0 && state.ExternalCardId is null,
            "Cerrar sesion elimina todo dato privado local.");

        SnapshotDto claims = view with { Phase = GamePhase.AwaitingClaims, OfferedCard = 20 };
        state.Synchronize(claims);
        Select(state, 0, 1);
        state.SelectExternal(true);
        state.PrepareGroup();
        state.Synchronize(claims with { OfferedCard = 21, Revision = 2 });
        Check(state.Groups.Count == 0 && !state.ExternalSelected && state.ExternalCardId == 21,
            "Una oferta diferente invalida el borrador previo.");
    }

    private static void DefensiveCopies()
    {
        var state = new HandInteractionState();
        SnapshotDto view = View() with { Melds = new[] { new MeldDto(7, 1, new[] { 10, 11, 12 }) } };
        state.Synchronize(view);
        view.OwnHand[0] = 51;
        Equal(state.Order, new[] { 0, 1, 2, 4, 5, 6 }, "Modificar array recibido no altera la copia local.");
        view.Melds[0].Cards[0] = 49;
        Select(state, 0);
        Check(state.PrepareGroup(7), "Extension usa la copia local del juego publico.");
        Equal(state.Groups[0].Cards, new[] { 10, 11, 12, 0 }, "Modificar array publico recibido no altera el borrador.");
        PlayDto[] plays = state.BuildPlays();
        plays[0].Cards[0] = 48;
        plays[0] = new PlayDto(new[] { 45, 46, 47 });
        Equal(state.BuildPlays()[0].Cards, new[] { 10, 11, 12, 0 }, "BuildPlays entrega arrays defensivos.");
        Throws<NotSupportedException>(() => ((IList<int>)state.Order)[0] = 50,
            "Order no expone una lista modificable.");
        Throws<NotSupportedException>(() => ((IList<int>)state.Groups[0].Cards)[0] = 50,
            "Un borrador no expone cartas modificables.");
        IReadOnlyList<int> oldOrder = state.Order;
        IReadOnlyList<int> oldGroup = state.Groups[0].Cards;
        state.Move(0, 6);
        state.MoveInGroup(0, 0, 0);
        Equal(oldOrder, new[] { 0, 1, 2, 4, 5, 6 }, "Una lectura anterior del orden permanece estable.");
        Equal(oldGroup, new[] { 10, 11, 12, 0 }, "Una lectura anterior del borrador permanece estable.");
    }

    private static void InvalidData()
    {
        var state = new HandInteractionState();
        Throws<ArgumentException>(() => state.Synchronize(View() with { OwnHand = new[] { -1 } }),
            "Los datos locales rechazan IDs negativos.");
        Throws<ArgumentException>(() => state.Synchronize(View() with { OwnHand = new[] { 52 } }),
            "Los datos locales rechazan IDs fuera de la baraja.");
        Throws<ArgumentException>(() => state.Synchronize(View() with { OwnHand = new[] { 0, 0 } }),
            "Los datos locales rechazan duplicados de mano.");
        Throws<ArgumentException>(() => state.Synchronize(View() with {
            Phase = GamePhase.AwaitingClaims, OfferedCard = 52 }),
            "Los datos locales rechazan una oferta fuera de rango.");
        Throws<ArgumentException>(() => state.Synchronize(View() with {
            Phase = GamePhase.AwaitingClaims, OfferedCard = 0 }),
            "Una carta externa no duplica una carta de la mano.");
        Throws<ArgumentException>(() => state.Synchronize(View() with { Melds = new[] {
            new MeldDto(7, 1, new[] { 10, 11, 12 }),
            new MeldDto(7, 1, new[] { 13, 14, 15 }) } }),
            "No se admiten destinos propios con ID repetido.");
        Check(state.Order.Count == 0, "Rechazar datos invalidos no crea una mano parcial.");
    }

    private static SnapshotDto View() => new(NetworkProtocol.Version,
        new string('a', 32), 1, 1, 1, GamePhase.AwaitingDraw, MatchResult.None,
        1, null, null, Players(), new[] { 0, 1, 2, 4, 5, 6 },
        Array.Empty<MeldDto>(), 40, 0, null, null,
        Array.Empty<RevealedHandDto>(), false);

    private static PublicPlayerDto[] Players(bool connected = true, bool eliminated = false) =>
        new[] { new PublicPlayerDto(1, "Ana", 0, 6, connected, eliminated),
            new PublicPlayerDto(2, "Luis", 1, 6, true, false) };

    private static void Select(HandInteractionState state, params int[] cards)
    {
        foreach (int card in cards)
            if (!state.Toggle(card)) throw new InvalidOperationException($"No se selecciono la carta {card}.");
    }

    private static void Equal(IEnumerable<int> actual, IEnumerable<int> expected, string message) =>
        Check(actual.SequenceEqual(expected), message);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine($"OK: {message}");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }
}
