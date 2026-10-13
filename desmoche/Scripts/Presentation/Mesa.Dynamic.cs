using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Godot;

namespace Desmoche.Presentation;

// Destinos de arrastre y controles contextuales. No modifica el motor ni sus DTO.
public partial class Mesa
{
    public const string ContextActionsPath = ContentPath + "ContextActions/";
    public int? StagedCardId => _stagedCardId;
    private int? _stagedCardId;
    private VBoxContainer _contextActions = null!;
    private Carta _stagedCard = null!;
    private Label _slotTitle = null!, _slotHint = null!;
    public const string PasaButtonPath = ContentPath + "PasaButton";
    private Control? _challengedMeld;
    private Button _pasaButton = null!;
    private Button _confirmCard = null!, _cancelCard = null!;
    private CheckButton _contextWin = null!;
    private readonly Dictionary<ActionKind, Button> _contextButtons = new();

    private ActionKind? SlotAction =>
        InteractionActions.CanRequest(_interactionView, ActionKind.ExchangeCard) ? ActionKind.ExchangeCard :
        InteractionActions.CanRequest(_interactionView, ActionKind.Pay) && _interactionView!.OwnHand.Length > 0
            ? ActionKind.Pay : null;

    private void InitializeDynamicInteraction()
    {
        // Se conservan los nodos anteriores para compatibilidad de escenas;
        // toda la interaccion visible usa los destinos y acciones de esta capa.
        GetNode<Control>(InteractionPath + "LocalActions").Hide();
        GetNode<Control>(InteractionPath + "NetworkActions").Hide();
        GetNode<Label>(InteractionPath + "HintLabel").Text =
            "Arrastra para ordenar. Suelta en Mi juego para crear; sobre un juego propio para ampliar. Click: seleccionar varias.";
        _meldEmpty.Text = "Arrastra aqui para preparar un juego nuevo.";
        GetNode<Label>(ContentPath + "TopBar/TitleLabel").Text = "";

        _paymentZone = new CardDropZone { Name = "Pago", CustomMinimumSize = new Vector2(178, 0),
            MouseFilter = MouseFilterEnum.Pass, Visible = false };
        _paymentZone.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        GetNode<Control>(CenterPath.TrimEnd('/')).AddChild(_paymentZone);
        var content = new VBoxContainer { Name = "Content", MouseFilter = MouseFilterEnum.Pass };
        content.AddThemeConstantOverride("separation", 6);
        _paymentZone.AddChild(content);
        _slotTitle = Text("Pago", 26); _slotTitle.Name = "TitleLabel";
        _slotTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _slotTitle.AddThemeFontOverride("font", _casinoSerif);
        content.AddChild(_slotTitle);
        _stagedCard = _cardScene.Instantiate<Carta>(); _stagedCard.Name = "Carta";
        _stagedCard.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _stagedCard.SetVisualSize(new Vector2(90, 112)); _stagedCard.ShowEmpty("SUELTA AQUI");
        content.AddChild(_stagedCard);
        _slotHint = Text("Arrastra una carta", 18); _slotHint.Name = "CountLabel";
        _slotHint.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(_slotHint);
        _paymentZone.DropAllowed = (_, data) => CanDropPayment(data);
        _paymentZone.DropReceived = (_, data) => DropPayment(data);

        _contextActions = new VBoxContainer { Name = "ContextActions", MouseFilter = MouseFilterEnum.Pass };
        _contextActions.AddThemeConstantOverride("separation", 6);
        GetNode<Control>(ContentPath.TrimEnd('/')).AddChild(_contextActions);
        foreach (var entry in new[]
        {
            ("ClaimButton", "Tomar oferta", ActionKind.Claim),
            ("PassButton", "Rechazar oferta", ActionKind.Pass),
            ("DrawDeckButton", "Mostrar del mazo", ActionKind.DrawDeck),
            ("DrawDiscardButton", "Tomar pozo", ActionKind.DrawDiscard),
            ("ContinueButton", "Continuar al turno", ActionKind.FinishInitial),
            ("PublishButton", "Confirmar juegos", ActionKind.Publish),
            ("PasaButton", "Pasa", ActionKind.SayPasa),
            ("NoPaymentButton", "Terminar sin pago", ActionKind.CompleteWithoutPayment)
        })
        {
            ActionKind kind = entry.Item3;
            Button button = ContextButton(entry.Item1, entry.Item2);
            button.Pressed += () => RequestAction(kind);
            _contextButtons.Add(kind, button);
        }
        _pasaButton = _contextButtons[ActionKind.SayPasa];
        _contextActions.RemoveChild(_pasaButton);
        GetNode<Control>(ContentPath.TrimEnd('/')).AddChild(_pasaButton);
        _pasaButton.Text = "Pasa";
        _pasaButton.TooltipText = "Tienes tres segundos para señalar este juego.";
        _pasaButton.ZIndex = 5;
        _contextWin = new CheckButton { Name = "DeclareWin", Text = "Ganar con esta carta" };
        _contextWin.AddThemeFontSizeOverride("font_size", 13);
        _contextWin.Toggled += value => _declareWin.SetPressedNoSignal(value);
        _contextActions.AddChild(_contextWin);
        InitializeInitialDeclarations();
        _confirmCard = ContextButton("ConfirmCardButton", "Aceptar");
        _confirmCard.Pressed += ConfirmStagedCard;
        _cancelCard = ContextButton("CancelCardButton", "Cambiar carta");
        _cancelCard.Pressed += () =>
        {
            if (!CanEdit) return;
            _stagedCardId = null; RefreshSelection();
        };
        _contextActions.MinimumSizeChanged += QueueCasinoLayout;
        _deck.GuiInput += OnDeckInput;
        _hand.GuiInput += OnHandBackgroundInput;
    }

    private Button ContextButton(string name, string caption)
    {
        var button = new Button { Name = name, Text = caption, CustomMinimumSize = new Vector2(0, 32) };
        button.AddThemeFontSizeOverride("font_size", 14);
        _contextActions.AddChild(button);
        return button;
    }

    private void DisposeDynamicInteraction()
    {
        _deck.GuiInput -= OnDeckInput;
        _hand.GuiInput -= OnHandBackgroundInput;
        _contextActions.MinimumSizeChanged -= QueueCasinoLayout;
    }

    private void BindOwnMeldDrop(HFlowContainer cards, int meldId)
    {
        cards.MouseFilter = MouseFilterEnum.Pass;
        cards.TooltipText = "Suelta cartas aqui para ampliar este juego. Reordena el borrador antes de confirmar.";
        cards.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default),
            Callable.From<Vector2, Variant, bool>((_, data) => CanDropDraft(data)),
            Callable.From<Vector2, Variant>((_, data) => DropDraft(data, targetMeldId: meldId)));
    }

    private void ConfirmStagedCard()
    {
        if (!CanSend || SlotAction is not ActionKind kind || _stagedCardId is not int id ||
            _interactionView is not { } view || !view.OwnHand.Contains(id) || Assigned(id)) return;
        SendRequest(new VisualActionRequest(kind, id));
        if (_pendingRequest != 0) _stagedCardId = null;
        RefreshDynamicInteraction();
    }

    private void OnDeckInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } &&
            CanSend && InteractionActions.CanRequest(_interactionView, ActionKind.DrawDeck))
        { RequestAction(ActionKind.DrawDeck); _deck.AcceptEvent(); }
    }

    private void OnHandBackgroundInput(InputEvent input)
    {
        if (!CanEdit || input is not InputEventMouseButton
            { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        _handState.ClearSelection(); RefreshSelection();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!IsVisibleInTree() || !CanEdit || input is not InputEventKey
            { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        _handState.ClearSelection(); _stagedCardId = null; RefreshSelection();
        GetViewport().SetInputAsHandled();
    }

    private void RefreshDynamicInteraction()
    {
        ActionKind? slot = SlotAction;
        if (_stagedCardId is int id && (_interactionView is null ||
            !_interactionView.OwnHand.Contains(id) || Assigned(id) || slot is null)) _stagedCardId = null;
        _paymentZone.Visible = slot is not null;
        _paymentZone.DropEnabled = CanEdit && slot is not null;
        _paymentZone.TooltipText = "Arrastra una carta propia y confirma con Aceptar. Soltar no envia la accion.";
        _slotTitle.Text = slot == ActionKind.ExchangeCard ? "Cambio inicial" : "Pago";
        _slotHint.Text = slot == ActionKind.ExchangeCard ? "A tu derecha" : "Arrastra una carta";
        if (_stagedCardId is int staged) _stagedCard.ShowFace(staged);
        else _stagedCard.ShowEmpty("SUELTA AQUI");
        _confirmCard.Visible = _cancelCard.Visible = slot is not null && _stagedCardId is not null;
        _confirmCard.Disabled = !CanSend;
        _cancelCard.Disabled = !CanEdit;
        foreach (var entry in _contextButtons)
        {
            bool allowed = InteractionActions.CanRequest(_interactionView, entry.Key);
            if (entry.Key == ActionKind.Publish)
                allowed &= _handState.Groups.Count > 0 && !_handState.Groups.Any(group =>
                    group.Cards.Count < 3 || group.Cards.Contains(_handState.ExternalCardId ?? -1));
            entry.Value.Visible = allowed;
            entry.Value.Disabled = !CanSend;
        }
        _contextWin.Visible = false;
        _declareWin.SetPressedNoSignal(false);
        _contextWin.Disabled = !CanSend;
        _contextWin.SetPressedNoSignal(_declareWin.ButtonPressed);
        RefreshInitialDeclarations();
        _deck.MouseFilter = InteractionActions.CanRequest(_interactionView, ActionKind.DrawDeck)
            ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _deck.TooltipText = InteractionActions.CanRequest(_interactionView, ActionKind.DrawDeck)
            ? "Click para mostrar la siguiente carta." : "Mazo de la partida.";
        _contextActions.Visible = _contextActions.GetChildren().OfType<Control>().Any(child => child.Visible);
        QueueCasinoLayout();
    }
}
