using System;
using System.Linq;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Fixtures visuales locales: no instancia GameManager ni envía acciones por la red.
public partial class CardInputSmoke : Control
{
    private readonly int[] _ids = { 0, 14, 28 };
    private readonly Carta.SelectionRequestedEventHandler[] _handlers = new Carta.SelectionRequestedEventHandler[3];
    private Carta.SelectionRequestedEventHandler? _staticHandler;
    private Carta[] _cards = Array.Empty<Carta>();
    private Carta _public = null!, _back = null!, _empty = null!;
    private CardDropZone _zone = null!;
    private Label _status = null!;
    private bool _allowDrop = true, _auto, _done;
    private int _selectionRequests, _dropRequests, _checks;
    private ulong _runAt;

    public override void _Ready()
    {
        _auto = OS.GetCmdlineUserArgs().Contains("--auto");
        try
        {
            _status = GetNode<Label>("Padding/Content/Status");
            _cards = Enumerable.Range(0, 3).Select(i => GetNode<Carta>("Padding/Content/Cards/Card" + i)).ToArray();
            _public = GetNode<Carta>("Padding/Content/StaticCards/Public");
            _back = GetNode<Carta>("Padding/Content/StaticCards/Back");
            _empty = GetNode<Carta>("Padding/Content/StaticCards/Empty");
            _zone = GetNode<CardDropZone>("Padding/Content/DropZone");
            GetNode<CheckButton>("Padding/Content/AllowDrop").Toggled += value => _allowDrop = value;
            _zone.DropAllowed = (_, data) => IsAllowed(data);
            _zone.DropReceived = (_, _) => ReceiveDrop("zona");

            for (int i = 0; i < _cards.Length; i++)
            {
                int index = i;
                Carta card = _cards[i];
                card.ShowFace(_ids[i]);
                card.SetInteraction(true, false);
                _handlers[i] = id => Select(index, id);
                card.SelectionRequested += _handlers[i];
                card.DragDataRequested = () => Payload(_ids[index]);
                card.DropAllowed = (_, data) => IsAllowed(data);
                card.DropReceived = (_, _) => ReceiveDrop("carta " + (index + 1));
            }
            _public.ShowFace(40);
            _public.SetInteraction(false, false);
            _back.ShowFace(41);
            _back.SetInteraction(true, true);
            _back.DragDataRequested = () => Payload(0);
            _back.ShowBack();
            _empty.ShowFace(42);
            _empty.SetInteraction(true, true);
            _empty.DragDataRequested = () => Payload(0);
            _empty.ShowEmpty();
            _staticHandler = _ => _selectionRequests++;
            _public.SelectionRequested += _staticHandler;
            _back.SelectionRequested += _staticHandler;
            _empty.SelectionRequested += _staticHandler;
            UpdateStatus("Arrastra una carta hacia otra carta o hacia la zona inferior.");
            _runAt = Time.GetTicksMsec() + 150;
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    public override void _Process(double delta)
    {
        if (!_auto || _done || Time.GetTicksMsec() < _runAt) return;
        try { RunChecks(); Finish(true, "Input y destinos visuales comprobados."); }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    private void Select(int index, int cardId)
    {
        if (_cards[index].FaceCardId != cardId) throw new InvalidOperationException("La señal cambió de identidad.");
        _selectionRequests++;
        _cards[index].Selected = !_cards[index].Selected;
        UpdateStatus("Solicitud de selección de carta " + (index + 1) + ".");
    }

    private void ReceiveDrop(string target)
    {
        _dropRequests++;
        UpdateStatus("Solicitud visual aceptada sobre " + target + "; el original permanece en su lugar.");
    }

    private void UpdateStatus(string message)
    {
        _status.Text = message + "\nSelecciones: " + _selectionRequests + " · Drops: " + _dropRequests
            + " · Seleccionadas: " + _cards.Count(c => c.Selected);
    }

    private static Variant Payload(int cardId) => new Godot.Collections.Dictionary
    {
        ["kind"] = "card-input-smoke", ["cardId"] = cardId
    };

    private bool IsAllowed(Variant data)
    {
        if (!_allowDrop || data.VariantType != Variant.Type.Dictionary) return false;
        Godot.Collections.Dictionary value = data.AsGodotDictionary();
        return value.ContainsKey("kind") && value["kind"].VariantType == Variant.Type.String
            && value["kind"].AsString() == "card-input-smoke"
            && value.ContainsKey("cardId") && value["cardId"].VariantType == Variant.Type.Int
            && _ids.Contains(value["cardId"].AsInt32());
    }

    private static void Click(Carta card, Vector2? release = null)
    {
        card._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(10, 10) });
        card._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = release ?? new Vector2(10, 10) });
    }

    private void RunChecks()
    {
        Check(_cards.All(c => c.IsFaceUp && c.Interactive), "Las escenas Carta cargan y aceptan interacción.");
        Check(_cards.All(c => c.MouseFilter == MouseFilterEnum.Stop && c.FocusMode == FocusModeEnum.All), "Input y foco configurados.");
        Check(!_cards.Any(c => c.Selected), "No hay selección inicial.");
        Click(_cards[0]);
        Check(_selectionRequests == 1 && _cards[0].Selected, "Click emite y el observador selecciona.");
        _cards[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(10, 10) });
        Check(_selectionRequests == 1, "Release sin press no selecciona.");
        Click(_cards[0], new Vector2(-10, -10));
        Check(_selectionRequests == 1, "Soltar fuera no selecciona.");
        _cards[0]._GuiInput(new InputEventKey { Keycode = Key.Enter, Pressed = true, Echo = true });
        Check(_selectionRequests == 1, "Repetición de tecla no duplica selección.");
        _cards[0]._GuiInput(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        Check(_selectionRequests == 2 && !_cards[0].Selected, "Enter solicita deselección.");
        _cards[0]._GuiInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
        Click(_cards[1]);
        Check(_selectionRequests == 4 && _cards[0].Selected && _cards[1].Selected && !_cards[2].Selected,
            "Selección múltiple mantiene independencia entre cartas.");

        int before = _selectionRequests;
        Click(_public); Click(_back); Click(_empty);
        Check(_selectionRequests == before, "Cartas estáticas no emiten solicitudes.");
        Check(_public.FaceCardId == 40 && !_public.Interactive, "Carta pública mantiene cara sin input.");
        Check(_back.IsBack && !_back.Interactive && !_back.Selected && _back.FaceCardId is null, "Dorso borra identidad e interacción.");
        Check(!_empty.IsFaceUp && !_empty.IsBack && !_empty.Interactive && !_empty.Selected && _empty.FaceCardId is null,
            "Vacío borra identidad e interacción.");
        Check(_back.DragDataRequested is null && _empty.DragDataRequested is null, "Dorso y vacío borran fábrica del drag.");
        Check(_public.MouseFilter == MouseFilterEnum.Ignore && _back.FocusMode == FocusModeEnum.None, "Estáticas no interceptan input ni foco.");

        Variant payload = Payload(0);
        Check(_cards[0].DragDataRequested is not null && IsAllowed(_cards[0].DragDataRequested!()), "Fábrica genera datos de intención.");
        Check(_zone._CanDropData(Vector2.Zero, payload), "Zona admite payload válido.");
        _zone._DropData(Vector2.Zero, payload);
        Check(_dropRequests == 1, "Drop emite una solicitud al observador.");
        Check(_cards[1]._CanDropData(Vector2.Zero, payload), "Una carta también funciona como destino.");
        _cards[1]._DropData(Vector2.Zero, payload);
        Check(_dropRequests == 2, "Drop sobre carta genera intención.");
        _allowDrop = false;
        Check(!_zone._CanDropData(Vector2.Zero, payload), "Cambio de permisos rechaza drop.");
        _zone._DropData(Vector2.Zero, payload);
        Check(_dropRequests == 2, "Drop rechazado no llama al observador.");
        _allowDrop = true;
        Check(!_zone._CanDropData(Vector2.Zero, default) && !_zone._CanDropData(Vector2.Zero, Payload(51)), "Payload vacío o ajeno se rechaza.");
        _zone.DropEnabled = false;
        Check(!_zone._CanDropData(Vector2.Zero, payload), "Destino deshabilitado rechaza drop.");
        _zone.DropEnabled = true;
        _cards[2].Interactive = false;
        Check(!_cards[2]._CanDropData(Vector2.Zero, payload), "Carta deshabilitada no acepta drop.");
        Check(_cards.Select(c => c.FaceCardId!.Value).SequenceEqual(_ids), "Las solicitudes no mueven ni alteran cartas originales.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private void Finish(bool success, string message)
    {
        _done = true;
        string output = "CARD INPUT " + (success ? "PASS" : "FAIL") + ": " + message + " Comprobaciones=" + _checks;
        if (success) GD.Print(output); else GD.PushError(output);
        if (_auto) GetTree().Quit(success ? 0 : 1);
        else if (_status is not null) _status.Text = output;
    }

    public override void _ExitTree()
    {
        for (int i = 0; i < _cards.Length; i++)
            if (_handlers[i] is not null) _cards[i].SelectionRequested -= _handlers[i];
        if (_staticHandler is not null)
        {
            if (_public is not null) _public.SelectionRequested -= _staticHandler;
            if (_back is not null) _back.SelectionRequested -= _staticHandler;
            if (_empty is not null) _empty.SelectionRequested -= _staticHandler;
        }
    }
}
