using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Fixtures de presentacion aislados: no crea peers, no envia RPC y no reemplaza
// las pruebas del transporte ni del motor. F6 conserva una mesa para practicar.
public partial class MesaInteractionSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private Mesa _mesa = null!;
    private SnapshotDto? _shown;
    private bool _auto, _done;
    private int _checks;
    private ulong _runAt;
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";
    private const string DraftPath = Mesa.InteractionPath + "DraftZone/Content/Rows";

    public override void _Ready()
    {
        base._Ready();
        _mesa = GetNode<Mesa>("Margin/Screens/Mesa");
        GetNode<Control>("Margin/Screens/Lobby").Visible = false;
        _mesa.Visible = true;
        _auto = OS.GetCmdlineUserArgs().Contains("--auto");
        try
        {
            Show(Claims());
            _runAt = Time.GetTicksMsec() + 150;
            if (!_auto)
                GD.Print("MESA INTERACCION MANUAL: selecciona cartas, ordena tu mano y prepara borradores. " +
                    "La oferta 6 de treboles puede incluirse. Las acciones de red permanecen deshabilitadas.");
        }
        catch (Exception error) { Finish(false, error.Message); }
    }

    public override void _Process(double delta)
    {
        if (!_auto || _done || Time.GetTicksMsec() < _runAt) return;
        _done = true;
        try
        {
            CheckInputAndDrafts();
            CheckExternalAndContexts();
            CheckOfflineAndPrivacy();
            Finish(true, "Gestos locales, borradores y proteccion del estado comprobados.");
        }
        catch (Exception error) { Finish(false, error.Message); }
    }

    private void CheckInputAndDrafts()
    {
        SnapshotDto view = Fixture();
        Show(view);
        CheckDragTokens(view);
        Equal(_mesa.LocalHandOrder, view.OwnHand, "La mano visual comienza con los IDs autorizados.");
        Check(Hand().GetChildCount() == view.OwnHand.Length, "Existe una Carta por carta propia.");
        Check(CardsUnder(Hand()).All(card => card.Interactive &&
            card.FocusMode == Control.FocusModeEnum.All && card.MouseFilter == Control.MouseFilterEnum.Stop),
            "Las cartas propias aceptan input de raton y teclado.");
        KeySelect(2); KeySelect(3); KeySelect(4);
        Check(CardsUnder(Hand()).Count(card => card.Selected) == 3, "Enter permite seleccion multiple.");
        KeySelect(3);
        Check(!HandCard(3).Selected && HandCard(2).Selected && HandCard(4).Selected,
            "Otro Enter deselecciona sin cambiar otras cartas.");
        KeySelect(3);

        Variant handDrag = CardDragData.Create(view, 2, CardDragOrigin.Hand);
        Carta target = HandCard(4);
        Vector2 right = new(target.Size.X, 0);
        Check(target._CanDropData(right, handDrag), "Una carta propia acepta un token de arrastre vigente.");
        Gesture(() => target._DropData(right, handDrag), "Reordenar mano conserva el snapshot.");
        Equal(_mesa.LocalHandOrder, new[] { 3, 4, 2, 19, 32, 48 }, "Soltar en la mitad derecha cambia el orden local.");
        PressLocal("PrepareButton");
        Check(_mesa.LocalDraftCount == 1, "Preparar crea un borrador local.");
        Equal(DraftIds(0), new[] { 3, 4, 2 }, "El borrador conserva el orden declarado, sin corregir reglas.");
        Equal(view.OwnHand, new[] { 2, 3, 4, 19, 32, 48 }, "Preparar no retira cartas del estado autorizado.");
        Check(Hand().GetChildCount() == 6, "La mano completa permanece visible hasta confirmacion.");
        Check(SelectionLabel().Text.StartsWith("0 cartas seleccionadas", StringComparison.Ordinal),
            "Preparar limpia seleccion y conserva asignacion visual.");

        Variant draftDrag = CardDragData.Create(view, 2, CardDragOrigin.Draft, 0);
        Carta draftTarget = DraftCard(0, 3);
        Check(draftTarget._CanDropData(Vector2.Zero, draftDrag), "Un borrador admite reorganizar sus propias cartas.");
        Gesture(() => draftTarget._DropData(Vector2.Zero, draftDrag), "Reordenar borrador conserva el snapshot.");
        Equal(DraftIds(0), new[] { 2, 3, 4 }, "Soltar en la mitad izquierda cambia solo el orden del borrador.");
        Equal(_mesa.LocalHandOrder, new[] { 3, 4, 2, 19, 32, 48 }, "Reordenar borrador no reordena la mano.");
        Variant foreignGroup = CardDragData.Create(view, 2, CardDragOrigin.Draft, 1);
        Check(!DraftCard(0, 3)._CanDropData(Vector2.Zero, foreignGroup), "No se mezclan borradores por un indice falso.");

        KeySelect(19); KeySelect(32); KeySelect(48);
        Variant nextGroupDrag = CardDragData.Create(view, 19, CardDragOrigin.Hand);
        Carta occupiedTarget = DraftCard(0, 3);
        Check(occupiedTarget._CanDropData(Vector2.Zero, nextGroupDrag),
            "Una carta de borrador permite soltar cartas propias nuevas sobre el area ocupada.");
        Gesture(() => occupiedTarget._DropData(Vector2.Zero, nextGroupDrag),
            "Soltar sobre un borrador existente conserva el snapshot.");
        Check(_mesa.LocalDraftCount == 1, "El drop ocupado agrega cartas al mismo borrador, sin menu.");
        Equal(DraftIds(0), new[] { 2, 19, 32, 48, 3, 4 }, "La insercion respeta el hueco y el orden seleccionado.");

        Button remove = _mesa.GetNode<VBoxContainer>(DraftPath).GetChild(0).GetChild(0)
            .GetChildren().OfType<Button>().Single();
        Gesture(() => remove.EmitSignal(Button.SignalName.Pressed), "Quitar borrador conserva el snapshot.");
        Check(_mesa.LocalDraftCount == 0, "Quitar elimina el borrador y libera sus cartas.");
        KeySelect(2); KeySelect(3); KeySelect(4);
        PressLocal("PrepareButton");
        Check(_mesa.LocalDraftCount == 1, "Las cartas liberadas se pueden preparar nuevamente.");
        PressLocal("ClearDraftsButton");
        Check(_mesa.LocalDraftCount == 0 && Hand().GetChildCount() == 6, "Limpiar borradores conserva toda la mano.");
        KeySelect(19);
        PressLocal("ClearSelectionButton");
        Check(CardsUnder(Hand()).All(card => !card.Selected), "Limpiar seleccion desmarca las cartas libres.");
        Variant unavailable = CardDragData.Create(view, 51, CardDragOrigin.Hand);
        Check(!HandCard(2)._CanDropData(Vector2.Zero, unavailable), "No se arrastra una carta ausente de la mano.");
    }

    private void CheckExternalAndContexts()
    {
        SnapshotDto claims = Claims();
        Show(claims);
        KeySelect(3); KeySelect(4);
        CheckButton include = _mesa.GetNode<CheckButton>(Mesa.InteractionPath + "LocalActions/ExternalSelect");
        Check(!include.Disabled, "La oferta vigente puede incluirse en un borrador.");
        Gesture(() => include.EmitSignal(BaseButton.SignalName.Toggled, true), "Seleccionar oferta conserva el snapshot.");
        Check(include.ButtonPressed, "La seleccion externa actualiza su control.");
        PressLocal("PrepareButton");
        Check(_mesa.LocalDraftCount == 1, "Se prepara un juego con la oferta publica.");
        Equal(DraftIds(0), new[] { 3, 4, 5 }, "La oferta forma parte del borrador declarado.");
        Check(!_mesa.LocalHandOrder.Contains(5) && Hand().GetChildCount() == claims.OwnHand.Length,
            "La carta externa nunca entra anticipadamente en la mano propia.");
        Check(_mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta").FaceCardId == 5,
            "Preparar no oculta la oferta publica real.");
        Check(include.Disabled, "No se reutiliza la oferta en otro borrador.");

        Variant oldDrag = CardDragData.Create(claims, 19, CardDragOrigin.Hand);
        SnapshotDto newer = claims with { Revision = claims.Revision + 1 };
        Show(newer);
        Check(_mesa.LocalDraftCount == 1, "Una revision de otro jugador conserva el borrador local.");
        Check(!CardDragData.TryRead(oldDrag, newer, out _), "El token anterior no corresponde a la nueva revision.");
        int[] order = _mesa.LocalHandOrder;
        Carta target = HandCard(2);
        Check(!target._CanDropData(Vector2.Zero, oldDrag), "La Carta rechaza soltar un token obsoleto.");
        Gesture(() => target._DropData(Vector2.Zero, oldDrag), "Un drop obsoleto conserva el snapshot.");
        Equal(_mesa.LocalHandOrder, order, "Un drop obsoleto no modifica el orden local.");

        SnapshotDto nextWindow = newer with { Revision = 3, Window = 2 };
        Show(nextWindow);
        Check(_mesa.LocalDraftCount == 0 && SelectionLabel().Text.StartsWith("0 cartas seleccionadas", StringComparison.Ordinal),
            "Una ventana diferente cancela borradores y selecciones.");
        KeySelect(3); KeySelect(4);
        Gesture(() => include.EmitSignal(BaseButton.SignalName.Toggled, true), "Seleccion externa en nueva ventana conserva el snapshot.");
        PressLocal("PrepareButton");
        Check(_mesa.LocalDraftCount == 1, "La nueva ventana permite una nueva intencion local.");
        SnapshotDto anotherSession = nextWindow with { Session = new string('b', 32), Revision = 1 };
        Show(anotherSession);
        Check(_mesa.LocalDraftCount == 0, "Otra sesion borra los borradores privados anteriores.");
        Equal(_mesa.LocalHandOrder, anotherSession.OwnHand, "Otra sesion restablece el orden recibido.");

        KeySelect(3); KeySelect(4);
        Gesture(() => include.EmitSignal(BaseButton.SignalName.Toggled, true), "Seleccion previa a respuesta conserva el snapshot.");
        PressLocal("PrepareButton");
        Show(anotherSession with { Revision = 2, OwnResponseSubmitted = true });
        Check(_mesa.LocalDraftCount == 0 && include.Disabled, "Una respuesta registrada invalida el borrador y la oferta.");
        Show(anotherSession with { Revision = 3, Phase = GamePhase.AwaitingPayment,
            PaymentPlayerId = 1, OfferedCard = null });
        Check(_mesa.LocalDraftCount == 0, "Cambiar de fase no conserva una reclamacion pendiente.");
    }

    private void CheckOfflineAndPrivacy()
    {
        SnapshotDto view = Fixture();
        Show(view);
        var rivals = _mesa.GetNode<HFlowContainer>(Mesa.RivalGamesPath);
        Carta[] publicCards = CardsUnder(rivals).ToArray();
        Check(publicCards.Length == 3 && publicCards.All(card => !card.Interactive &&
            card.FocusMode == Control.FocusModeEnum.None && card.MouseFilter == Control.MouseFilterEnum.Ignore),
            "Los juegos rivales son visibles y no aceptan seleccion o arrastre.");
        Gesture(() => publicCards[0]._GuiInput(new InputEventKey { Pressed = true, Keycode = Key.Enter }),
            "Input sobre una carta rival conserva el snapshot.");
        Check(CardsUnder(Hand()).All(card => !card.Selected), "Input rival no selecciona cartas propias.");
        Check(NetworkButtons().All(button => button.Disabled), "Los fixtures offline no permiten enviar acciones de red.");
        Check(!_mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta").Interactive,
            "El dorso del mazo no actua como una carta privada seleccionable.");

        SnapshotDto payment = view with { Phase = GamePhase.AwaitingPayment,
            PaymentPlayerId = 1, DiscardTop = 21, DiscardCount = 1, DeckCount = 39 };
        Show(payment);
        var pozo = _mesa.GetNode<CardDropZone>(Mesa.CenterPath + "Pozo");
        var pago = _mesa.GetNode<CardDropZone>(Mesa.CenterPath + "Pago");
        Variant payDrag = CardDragData.Create(payment, 2, CardDragOrigin.Hand);
        Check(!pozo._CanDropData(Vector2.Zero, payDrag),
            "Soltar en el pozo no envia un pago automaticamente.");
        Gesture(() => pozo._DropData(Vector2.Zero, payDrag), "Un pago offline conserva el snapshot.");
        Check(pago._CanDropData(Vector2.Zero, payDrag), "La casilla permite preparar una carta localmente.");
        Gesture(() => pago._DropData(Vector2.Zero, payDrag), "Preparar el pago conserva el snapshot.");
        Check(_mesa.StagedCardId == 2, "La casilla muestra la carta preparada.");
        Button confirm = _mesa.GetNode<Button>(Mesa.ContextActionsPath + "ConfirmCardButton");
        Check(confirm.Visible && confirm.Disabled, "Aceptar aparece, pero requiere conexion para enviar.");
        Gesture(() => confirm.EmitSignal(Button.SignalName.Pressed), "Aceptar offline no aplica el pago.");
        Check(Hand().GetChildCount() == payment.OwnHand.Length, "Un drop offline no retira ninguna carta.");
        Button pay = _mesa.GetNode<Button>(Mesa.InteractionPath + "NetworkActions/PayButton");
        Check(pay.Disabled, "El boton de pagar permanece deshabilitado offline.");
        KeySelect(2);
        Gesture(() => pay.EmitSignal(Button.SignalName.Pressed), "Una senal de pago offline conserva el snapshot.");
        Check(Hand().GetChildCount() == payment.OwnHand.Length, "La UI no aplica un pago sin respuesta del servidor.");

        SnapshotDto eliminated = payment with { Revision = 2, Players = new[] {
            payment.Players[0] with { Eliminated = true }, payment.Players[1] } };
        Show(eliminated);
        Check(CardsUnder(Hand()).All(card => !card.Interactive), "Un jugador eliminado no controla sus cartas.");
        Check(LocalButtons().All(button => button.Disabled), "La eliminacion deshabilita preparacion y reorganizacion.");
        Gesture(() => HandCard(3)._GuiInput(new InputEventKey { Pressed = true, Keycode = Key.Enter }),
            "Input eliminado conserva el snapshot.");
        Check(SelectionLabel().Text.StartsWith("0 cartas seleccionadas", StringComparison.Ordinal),
            "La eliminacion limpia y bloquea selecciones pendientes.");
        _mesa.DisplaySnapshot(null, "Diagnostico: sesion cerrada");
        _shown = null;
        Check(_mesa.LocalHandOrder.Length == 0 && _mesa.LocalDraftCount == 0 && Hand().GetChildCount() == 0,
            "Cerrar la vista elimina mano, seleccion y borradores privados.");
    }

    private void Show(SnapshotDto view)
    {
        string before = NetworkProtocol.Encode(view);
        _mesa.DisplaySnapshot(view, "Diagnostico de interaccion local · sin conexion de red");
        _shown = view;
        Check(NetworkProtocol.Encode(view) == before, "Representar el fixture no altera sus datos.");
    }

    private void KeySelect(int id) => Gesture(() => HandCard(id)._GuiInput(
        new InputEventKey { Pressed = true, Keycode = Key.Enter }),
        "Seleccion por teclado conserva el snapshot.");

    private void PressLocal(string name) => Gesture(() =>
        _mesa.GetNode<Button>(Mesa.InteractionPath + "LocalActions/" + name)
            .EmitSignal(Button.SignalName.Pressed), "El control " + name + " conserva el snapshot.");

    private void Gesture(Action gesture, string message)
    {
        string before = _shown is null ? "" : NetworkProtocol.Encode(_shown);
        gesture();
        Check(_shown is null || NetworkProtocol.Encode(_shown) == before, message);
    }

    private HFlowContainer Hand() => _mesa.GetNode<HFlowContainer>(HandPath);
    private Carta HandCard(int id) => Hand().GetNode<Carta>("Carta" + id);
    private Carta DraftCard(int group, int id) =>
        _mesa.GetNode<Carta>(DraftPath + "/Borrador" + group + "/Cards/Carta" + id);
    private int[] DraftIds(int group) =>
        _mesa.GetNode<HFlowContainer>(DraftPath + "/Borrador" + group + "/Cards")
            .GetChildren().OfType<Carta>().Select(card => card.FaceCardId!.Value).ToArray();
    private Label SelectionLabel() => _mesa.GetNode<Label>(Mesa.InteractionPath + "SelectionLabel");
    private IEnumerable<Button> NetworkButtons() =>
        _mesa.GetNode<HFlowContainer>(Mesa.InteractionPath + "NetworkActions").GetChildren().OfType<Button>();
    private IEnumerable<Button> LocalButtons() =>
        _mesa.GetNode<HFlowContainer>(Mesa.InteractionPath + "LocalActions").GetChildren().OfType<Button>();

    private static IEnumerable<Carta> CardsUnder(Node root)
    {
        foreach (Node node in root.GetChildren())
        {
            if (node is Carta card) yield return card;
            foreach (Carta child in CardsUnder(node)) yield return child;
        }
    }

    private void Equal(IEnumerable<int> actual, IEnumerable<int> expected, string message) =>
        Check(actual.SequenceEqual(expected), message);

    private void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks++;
    }

    private void Finish(bool success, string message)
    {
        _done = true;
        if (success) GD.Print($"MESA INTERACTION PASS: {message} Comprobaciones={_checks}");
        else GD.PushError("MESA INTERACTION FAIL: " + message);
        if (_auto) GetTree().Quit(success ? 0 : 1);
    }

    private static SnapshotDto Fixture() => new(NetworkProtocol.Version,
        new string('a', 32), 1, 1, 1, GamePhase.AwaitingDraw, MatchResult.None,
        1, null, null, new[] {
            new PublicPlayerDto(1, "Ana", 0, 6, true, false),
            new PublicPlayerDto(2, "Luis", 1, 3, true, false) },
        new[] { 2, 3, 4, 19, 32, 48 }, new[] { new MeldDto(7, 2, new[] { 13, 14, 15 }) },
        40, 0, null, null, Array.Empty<RevealedHandDto>(), false);

    private static SnapshotDto Claims() => Fixture() with
        { Phase = GamePhase.AwaitingClaims, OfferedCard = 5, DeckCount = 39 };

    private void CheckDragTokens(SnapshotDto view)
    {
        Variant valid = CardDragData.Create(view, 2, CardDragOrigin.Hand);
        Check(CardDragData.TryRead(valid, view, out LocalCardDrag drag) &&
            drag.CardId == 2 && drag.Origin == CardDragOrigin.Hand && drag.GroupIndex == -1,
            "Un token local vigente se interpreta con su origen y carta.");
        Check(!CardDragData.TryRead(default, view, out _), "El token nil se rechaza.");
        Check(!CardDragData.TryRead(2, view, out _), "Un valor que no es Dictionary se rechaza.");
        Check(!CardDragData.TryRead(valid, null, out _), "Sin vista no se interpreta un arrastre.");
        Reject("type", "otro-tipo", "Se rechaza un tipo de token desconocido.");
        Reject("session", new string('c', 32), "Se rechaza un token de otra sesion.");
        Reject("recipient", 2, "Se rechaza un token de otro destinatario.");
        Reject("revision", view.Revision + 1, "Se rechaza un token de otra revision.");
        Reject("window", view.Window + 1, "Se rechaza un token de otra ventana.");
        Reject("origin", 0, "Se rechaza un origen por debajo del rango.");
        Reject("origin", 4, "Se rechaza un origen por encima del rango.");
        Reject("group", 0, "Un token de mano no puede indicar un grupo.");
        Reject("card", -1, "Se rechaza una carta negativa en el token.");
        Reject("card", 52, "Se rechaza una carta fuera de la baraja en el token.");
        Reject("card", "2", "Los IDs no se convierten de texto a entero.");
        Reject("revision", 1.5, "Los numeros decimales no sustituyen a una revision entera.");
        Variant missing = CardDragData.Create(view, 2, CardDragOrigin.Hand);
        missing.AsGodotDictionary().Remove("card");
        Check(!CardDragData.TryRead(missing, view, out _), "Se rechaza un token incompleto.");
        Variant additional = CardDragData.Create(view, 2, CardDragOrigin.Hand);
        additional.AsGodotDictionary()["extra"] = true;
        Check(!CardDragData.TryRead(additional, view, out _), "Se rechaza un token con campos adicionales.");
        Check(!CardDragData.TryRead(CardDragData.Create(view, 2, CardDragOrigin.Draft, -1), view, out _),
            "Un origen de borrador exige indice de grupo.");
        Check(!CardDragData.TryRead(CardDragData.Create(view, 2, CardDragOrigin.Draft, 17), view, out _),
            "Un origen de borrador no excede el limite de grupos.");

        void Reject(string key, Variant value, string message)
        {
            Variant token = CardDragData.Create(view, 2, CardDragOrigin.Hand);
            token.AsGodotDictionary()[key] = value;
            Check(!CardDragData.TryRead(token, view, out _), message);
        }
    }
}
