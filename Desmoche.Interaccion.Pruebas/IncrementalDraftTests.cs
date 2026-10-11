using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;

namespace Desmoche.Interaccion.Pruebas;

internal static class IncrementalDraftTests
{
    private static int _checks;

    public static int Run()
    {
        NewDraftAndSelection();
        ExtendDraftAndPublishedMeld();
        ExternalSources();
        ReturnCards();
        RejectedMovesAreAtomic();
        Limits();
        ReconciliationAndCopies();
        return _checks;
    }

    private static void NewDraftAndSelection()
    {
        var state = State();
        SnapshotDto view = View();
        string original = NetworkProtocol.Encode(view);
        state.Synchronize(view);
        Check(state.StageCards(0), "Arrastrar una carta crea un borrador incompleto.");
        Equal(state.Groups[0].Cards, new[] { 0 }, "El borrador conserva la unica carta arrastrada.");
        Equal(state.Order, view.OwnHand, "Preparar por arrastre mantiene la mano autorizada.");
        Check(!InteractionActions.TryCreate(view, state, ActionKind.Publish, false,
            out _, out _), "Un borrador incompleto no produce una solicitud de publicar.");
        Check(state.StageCards(1, groupIndex: 0), "Una segunda carta se agrega al borrador existente.");
        Check(state.StageCards(2, groupIndex: 0), "Una tercera carta completa el borrador local.");
        Equal(state.Groups[0].Cards, new[] { 0, 1, 2 }, "Las cartas se agregan en el orden de arrastre.");
        Check(InteractionActions.TryCreate(view, state, ActionKind.Publish, false,
            out VisualActionRequest? request, out _) && request!.Plays!.Length == 1,
            "El borrador completo produce una intencion validable por el servidor.");
        Check(NetworkProtocol.Encode(view) == original, "El arrastre no modifica la vista autoritativa.");

        state = State();
        Select(state, 0, 2, 4);
        state.Move(4, 0);
        Check(state.StageCards(2), "Arrastrar una carta seleccionada prepara la seleccion completa.");
        Equal(state.Groups[0].Cards, new[] { 4, 0, 2 }, "La seleccion preparada sigue el orden visual.");
        Check(state.SelectedIds.Count == 0, "Una colocacion confirmada limpia la seleccion local.");
        Select(state, 1, 5);
        Check(state.StageCards(6), "Arrastrar una carta sin seleccionar crea su propio borrador.");
        Equal(state.Groups[1].Cards, new[] { 6 }, "Una carta no seleccionada no arrastra otras cartas privadas.");
        Check(state.SelectedIds.Count == 0, "La nueva colocacion limpia seleccion previa.");
        Check(!state.Toggle(0), "Una carta preparada no puede seleccionarse para duplicarla.");
    }

    private static void ExtendDraftAndPublishedMeld()
    {
        var state = State();
        Check(state.StageCards(1), "Un juego nuevo puede empezar por cualquier carta privada.");
        Check(state.StageCards(0, groupIndex: 0, beforeIndex: 0),
            "Soltar antes del inicio inserta la carta en el primer hueco.");
        Select(state, 2, 4);
        Check(state.StageCards(2, groupIndex: 0, beforeIndex: 1),
            "Una seleccion se inserta como bloque en un hueco del borrador.");
        Equal(state.Groups[0].Cards, new[] { 0, 2, 4, 1 },
            "La insercion conserva las cartas anteriores y el orden del bloque.");
        Check(state.MoveInGroup(0, 4, 0), "El borrador incremental permite ordenar por arrastre.");
        Equal(state.Groups[0].Cards, new[] { 4, 0, 2, 1 }, "Mover dentro del borrador conserva todos los IDs.");

        state = State();
        Check(state.StageCards(0, targetMeldId: 7), "Soltar sobre un juego propio prepara su ampliacion.");
        Equal(state.Groups[0].Cards, new[] { 30, 31, 32, 0 },
            "La ampliacion copia las cartas publicas y agrega la carta privada.");
        Check(state.StageCards(1, targetMeldId: 7),
            "Otra carta sobre el mismo juego reutiliza la ampliacion preparada.");
        Check(state.Groups.Count == 1 && state.Groups[0].MeldId == 7,
            "Un juego publicado tiene como maximo un borrador de ampliacion.");
        Check(state.StageCards(2, targetMeldId: 7, groupIndex: 0, beforeIndex: 0),
            "Un destino de grupo y juego coincidentes permite insertar antes de cartas publicas.");
        Equal(state.Groups[0].Cards, new[] { 2, 30, 31, 32, 0, 1 },
            "La ampliacion conserva el orden declarado sin ordenar por valor.");
        Check(state.StageCards(4, groupIndex: 0),
            "El indice de un borrador de ampliacion conserva su juego de destino.");
        Check(state.BuildPlays()[0].MeldId == 7,
            "La intencion identifica el juego propio ampliado.");
        Check(!state.StageCards(30), "Las cartas publicas existentes no se tratan como cartas privadas arrastrables.");
        Check(!state.StageCards(5, targetMeldId: 8), "No se amplia un juego de un rival.");
    }

    private static void ExternalSources()
    {
        SnapshotDto view = View() with { Phase = GamePhase.AwaitingClaims, OfferedCard = 20 };
        var state = State(view);
        Select(state, 0, 1);
        state.Move(1, 0);
        Check(state.StageCards(20), "Arrastrar una oferta incluye las cartas privadas seleccionadas.");
        Equal(state.Groups[0].Cards, new[] { 1, 0, 20 },
            "El arrastre de oferta respeta el orden de mano y agrega la carta publica.");
        Check(!state.Order.Contains(20) && !state.ExternalSelected,
            "Preparar una oferta no la incorpora a la mano ni deja su seleccion activa.");
        Check(InteractionActions.TryCreate(view, state, ActionKind.Claim, false,
            out _, out _), "La oferta preparada en un grupo completo permite solicitar un reclamo.");
        Check(!state.StageCards(20), "La misma oferta no puede agregarse a dos borradores.");

        state = State(view);
        Check(state.StageCards(20), "La oferta sola puede iniciar un borrador incompleto.");
        Check(!InteractionActions.TryCreate(view, state, ActionKind.Claim, false,
            out _, out _), "Un reclamo incompleto no se convierte en accion de red.");
        Check(state.StageCards(0, groupIndex: 0) && state.StageCards(1, groupIndex: 0),
            "La oferta incompleta puede completarse mediante arrastres posteriores.");

        state = State(view);
        Select(state, 0, 1);
        state.SelectExternal(true);
        Check(state.StageCards(0), "Una seleccion privada incorpora tambien la oferta seleccionada.");
        Equal(state.Groups[0].Cards, new[] { 0, 1, 20 }, "La oferta seleccionada aparece exactamente una vez.");
        Check(!state.ExternalSelected && state.SelectedIds.Count == 0,
            "Una colocacion limpia conjuntamente seleccion privada y externa.");

        state = State(view);
        state.SelectExternal(true);
        Check(state.StageCards(4), "Una carta arrastrada incorpora la carta externa seleccionada.");
        Equal(state.Groups[0].Cards, new[] { 4, 20 }, "Un borrador de dos cartas sigue siendo exclusivamente local.");
        state = State(View() with { DiscardTop = 21, DiscardCount = 1 });
        Check(state.StageCards(21, targetMeldId: 7), "El pozo disponible puede preparar una ampliacion propia.");
        Check(!state.Order.Contains(21), "El pozo preparado permanece fuera de la mano verdadera.");
        state = State(view with { OwnResponseSubmitted = true });
        Check(!state.StageCards(20), "Una oferta ya respondida no esta disponible para nuevos borradores.");
        state = State(view with { Players = Players(eliminated: true) });
        Check(!state.StageCards(20), "Un jugador eliminado no recibe una oferta utilizable localmente.");
    }

    private static void ReturnCards()
    {
        var state = State();
        state.StageCards(0);
        Check(state.ReturnToHand(0, 0), "Devolver la unica carta elimina un juego nuevo incompleto.");
        Check(state.Groups.Count == 0 && state.Order.Contains(0),
            "Deshacer una colocacion conserva la carta en la mano autorizada.");
        Select(state, 0, 1, 2);
        state.StageCards(0);
        Check(state.ReturnToHand(0, 1), "Una carta puede devolverse individualmente desde un borrador.");
        Equal(state.Groups[0].Cards, new[] { 0, 2 }, "Las otras cartas del borrador permanecen preparadas.");
        Check(state.Toggle(1), "Devolver libera la carta para otra seleccion.");
        state.ClearSelection();
        Check(state.StageCards(1), "Una carta devuelta puede iniciar otro borrador.");
        Check(state.Groups.Count == 2, "Devolver no combina implicitamente juegos distintos.");
        Check(!state.ReturnToHand(-1, 0) && !state.ReturnToHand(2, 0),
            "Devolver rechaza indices de borrador inexistentes.");
        Check(!state.ReturnToHand(0, 6) && !state.ReturnToHand(0, 51),
            "Devolver rechaza cartas que no pertenecen al borrador.");

        state = State();
        state.StageCards(0, targetMeldId: 7);
        state.StageCards(1, targetMeldId: 7);
        Check(!state.ReturnToHand(0, 30), "Las cartas de un juego publicado no se retiran a la mano local.");
        Equal(state.Groups[0].Cards, new[] { 30, 31, 32, 0, 1 },
            "Una devolucion publica rechazada conserva la ampliacion completa.");
        Check(state.ReturnToHand(0, 0), "Una ampliacion permite devolver su primera carta nueva.");
        Equal(state.Groups[0].Cards, new[] { 30, 31, 32, 1 },
            "Devolver conserva las cartas publicas y las nuevas restantes.");
        Check(state.ReturnToHand(0, 1) && state.Groups.Count == 0,
            "Devolver la ultima carta nueva elimina la ampliacion vacia.");
        Check(state.StageCards(2, targetMeldId: 7),
            "El juego publicado sigue disponible despues de cancelar la ampliacion.");

        SnapshotDto claims = View() with { Phase = GamePhase.AwaitingClaims, OfferedCard = 20 };
        state = State(claims);
        state.StageCards(20);
        Check(state.ReturnToHand(0, 20), "La carta externa puede retirarse del borrador local.");
        Check(state.Groups.Count == 0 && !state.Order.Contains(20) && state.ExternalCardId == 20,
            "Retirar la oferta conserva su origen publico y no la convierte en carta privada.");
        Check(state.SelectExternal(true), "La oferta liberada vuelve a estar disponible para preparar.");
    }

    private static void RejectedMovesAreAtomic()
    {
        var state = State();
        state.StageCards(0, targetMeldId: 7);
        Select(state, 1, 2);
        string original = Dump(state);
        Check(!state.StageCards(40), "No se prepara una carta publica de un rival.");
        Check(!state.StageCards(51), "No se prepara una carta ajena al estado visible propio.");
        Check(!state.StageCards(-1) && !state.StageCards(52), "No se preparan IDs fuera de la baraja.");
        Check(!state.StageCards(0, groupIndex: 0), "Una carta ya preparada no se duplica en el mismo borrador.");
        Check(!state.StageCards(0), "Una carta preparada no se transfiere implicitamente a otro grupo.");
        Check(!state.StageCards(1, groupIndex: -1) && !state.StageCards(1, groupIndex: 1),
            "La colocacion rechaza indices de grupo inexistentes.");
        Check(!state.StageCards(1, targetMeldId: 0) && !state.StageCards(1, targetMeldId: -7),
            "La colocacion rechaza destinos de juego invalidos.");
        Check(!state.StageCards(1, targetMeldId: 9, groupIndex: 0),
            "Un ID de juego contradictorio con el borrador se rechaza.");
        Check(!state.StageCards(1, groupIndex: 0, beforeIndex: -1) &&
            !state.StageCards(1, groupIndex: 0, beforeIndex: 5),
            "La colocacion rechaza huecos fuera del destino.");
        Check(!state.StageCards(1, beforeIndex: 1),
            "Un juego nuevo vacio solo permite su primer hueco.");
        Check(Dump(state) == original, "Las colocaciones rechazadas conservan borradores y seleccion sin cambios.");

        state = State();
        state.StageCards(0);
        Select(state, 1);
        original = Dump(state);
        Check(!state.StageCards(1, groupIndex: 0, targetMeldId: 7),
            "Un juego nuevo no puede convertirse silenciosamente en ampliacion.");
        Check(Dump(state) == original, "Rechazar destinos incompatibles es atomico.");

        state = State(View() with { Melds = new[] {
            new MeldDto(7, 1, new[] { 30, 31, 32 }),
            new MeldDto(9, 1, new[] { 30, 33, 34 }) } });
        state.StageCards(0, targetMeldId: 7);
        Select(state, 1);
        original = Dump(state);
        Check(!state.StageCards(1, targetMeldId: 9),
            "Datos publicos superpuestos no reutilizan una carta entre ampliaciones.");
        Check(Dump(state) == original, "Rechazar cartas publicas repetidas conserva todo el estado local.");
    }

    private static void Limits()
    {
        var state = State(View() with { OwnHand = Enumerable.Range(0, 18).ToArray(),
            Melds = Array.Empty<MeldDto>() });
        for (int id = 0; id < 17; id++)
            Check(state.StageCards(id), $"Se permite el borrador incremental {id + 1} dentro del limite local.");
        string original = Dump(state);
        Check(!state.StageCards(17), "No se permiten mas de diecisiete borradores.");
        Check(Dump(state) == original, "Rechazar el borrador numero dieciocho conserva los anteriores.");
        Check(state.StageCards(17, groupIndex: 16),
            "El limite de grupos permite ampliar uno de los diecisiete existentes.");

        state = State(View() with { OwnHand = Enumerable.Range(0, 14).ToArray(),
            Melds = Array.Empty<MeldDto>() });
        for (int id = 0; id < 13; id++)
            Check(state.StageCards(id, groupIndex: id == 0 ? null : 0),
                $"Un borrador admite su carta incremental {id + 1} hasta trece.");
        state.Toggle(13);
        original = Dump(state);
        Check(!state.StageCards(13, groupIndex: 0), "Una decimocuarta carta no cabe en el borrador.");
        Check(Dump(state) == original, "El limite de trece conserva seleccion y orden previos.");
        Check(state.ReturnToHand(0, 12) && state.StageCards(13, groupIndex: 0),
            "Tras retirar una carta el borrador puede completarse con otra.");

        state = State(View() with { OwnHand = Enumerable.Range(0, 14).ToArray(),
            Melds = Array.Empty<MeldDto>() });
        Select(state, Enumerable.Range(0, 14).ToArray());
        original = Dump(state);
        Check(!state.StageCards(0), "Un bloque seleccionado de catorce cartas se rechaza completo.");
        Check(Dump(state) == original, "El bloque excesivo no prepara parcialmente cartas.");

        state = State(View() with { OwnHand = new[] { 0, 1 }, Melds = new[] {
            new MeldDto(7, 1, Enumerable.Range(20, 12).ToArray()) } });
        Check(state.StageCards(0, targetMeldId: 7), "Una ampliacion puede alcanzar trece cartas incluyendo las publicadas.");
        original = Dump(state);
        Check(!state.StageCards(1, targetMeldId: 7), "El limite tambien cuenta cartas publicadas anteriores.");
        Check(Dump(state) == original, "Una ampliacion excesiva conserva el borrador anterior.");
    }

    private static void ReconciliationAndCopies()
    {
        SnapshotDto view = View();
        var state = State(view);
        state.StageCards(0);
        state.Synchronize(view with { Revision = 2 });
        Check(state.Groups.Count == 1, "Una revision ajena conserva un borrador incompleto vigente.");
        state.Synchronize(view with { Window = 2 });
        Check(state.Groups.Count == 0, "Otra ventana invalida tambien borradores incompletos.");
        state.StageCards(1);
        state.Synchronize(view with { Window = 2, OwnHand = new[] { 0, 1, 2, 4, 6 } });
        Check(state.Groups.Count == 0, "Cambiar la mano autorizada cancela los borradores incrementales.");
        state.StageCards(0);
        state.Synchronize(view with { Session = new string('b', 32) });
        Check(state.Groups.Count == 0, "Otra sesion no conserva preparaciones privadas anteriores.");
        state.StageCards(0);
        state.Synchronize(null);
        Check(state.Groups.Count == 0 && state.Order.Count == 0 && !state.StageCards(0),
            "Cerrar la sesion elimina borradores, mano y acceso a sus cartas.");

        view = View() with { Phase = GamePhase.AwaitingClaims, OfferedCard = 20 };
        state = State(view);
        state.StageCards(20);
        state.Synchronize(view with { Revision = 2, OfferedCard = 21 });
        Check(state.Groups.Count == 0 && !state.StageCards(20),
            "Una nueva oferta cancela el borrador y retira el origen anterior.");
        state.StageCards(21);
        state.Synchronize(view with { Revision = 3, OwnResponseSubmitted = true });
        Check(state.Groups.Count == 0 && state.ExternalCardId is null,
            "Responder cierra la oferta y los borradores asociados.");
        state = State(view);
        state.StageCards(0);
        state.Synchronize(view with { Players = Players(connected: false) });
        Check(state.Groups.Count == 0 && state.ExternalCardId is null,
            "La desconexion invalida los borradores y los origenes externos.");
        state = State(view);
        state.StageCards(0);
        state.Synchronize(view with { Players = Players(eliminated: true) });
        Check(state.Groups.Count == 0 && state.ExternalCardId is null,
            "La eliminacion invalida las preparaciones locales anteriores.");
        state = State(view with { RevealedHands = new[] {
            new RevealedHandDto(2, new[] { 40, 41, 42 }) } });
        Check(!state.StageCards(40), "Una mano rival revelada no pasa a estar disponible para arrastre propio.");

        view = View();
        state = State(view);
        view.OwnHand[0] = 51;
        view.Melds[0].Cards[0] = 49;
        Check(state.StageCards(0, targetMeldId: 7), "La colocacion usa copias defensivas del estado recibido.");
        Equal(state.Groups[0].Cards, new[] { 30, 31, 32, 0 },
            "Modificar arrays recibidos no altera una ampliacion incremental.");
        IReadOnlyList<int> old = state.Groups[0].Cards;
        state.StageCards(1, targetMeldId: 7);
        Equal(old, new[] { 30, 31, 32, 0 }, "Una lectura previa del borrador permanece estable al ampliarlo.");
        PlayDto[] plays = state.BuildPlays();
        plays[0].Cards[0] = 48;
        Equal(state.Groups[0].Cards, new[] { 30, 31, 32, 0, 1 },
            "Modificar datos de la solicitud no altera el borrador incremental.");
        bool immutable = false;
        try { ((IList<int>)state.Groups[0].Cards)[0] = 47; }
        catch (NotSupportedException) { immutable = true; }
        Check(immutable, "Las cartas del borrador incremental no exponen una coleccion modificable.");
    }

    private static HandInteractionState State(SnapshotDto? view = null)
    {
        var state = new HandInteractionState();
        state.Synchronize(view ?? View());
        return state;
    }

    private static SnapshotDto View() => new(NetworkProtocol.Version,
        new string('a', 32), 1, 1, 1, GamePhase.AwaitingDraw, MatchResult.None,
        1, null, null, Players(), new[] { 0, 1, 2, 4, 5, 6 }, new[] {
            new MeldDto(7, 1, new[] { 30, 31, 32 }),
            new MeldDto(8, 2, new[] { 40, 41, 42 }) }, 34, 0, null, null,
        Array.Empty<RevealedHandDto>(), false);

    private static PublicPlayerDto[] Players(bool connected = true, bool eliminated = false) =>
        new[] { new PublicPlayerDto(1, "Ana", 0, 6, connected, eliminated),
            new PublicPlayerDto(2, "Luis", 1, 6, true, false) };

    private static string Dump(HandInteractionState state) =>
        string.Join(",", state.Order) + ":" + string.Join(",", state.SelectedIds) + ":" +
        state.ExternalSelected + ":" + string.Join(";", state.Groups.Select(group =>
            (group.MeldId?.ToString() ?? "nuevo") + "=" + string.Join(",", group.Cards)));

    private static void Select(HandInteractionState state, params int[] cards)
    {
        foreach (int card in cards)
            if (!state.Toggle(card)) throw new InvalidOperationException($"No se selecciono {card}.");
    }

    private static void Equal(IEnumerable<int> actual, IEnumerable<int> expected, string message) =>
        Check(actual.SequenceEqual(expected), message);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine($"OK: {message}");
    }
}
