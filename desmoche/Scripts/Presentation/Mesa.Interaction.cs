using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    public const string InteractionPath = ContentPath + "HandZone/Padding/Content/Interaction/";
    private readonly HandInteractionState _handState = new();
    private readonly Dictionary<ActionKind, Button> _actionButtons = new();
    private readonly List<Button> _localButtons = new();
    private SnapshotDto? _interactionView;
    private CardDropZone _draftZone = null!, _paymentZone = null!;
    private VBoxContainer _draftRows = null!;
    private Label _selectionLabel = null!, _draftEmpty = null!, _interactionMessage = null!;
    private OptionButton _targetGame = null!;
    private CheckButton _externalSelect = null!, _declareWin = null!;
    private long _pendingRequest;
    private ulong _pendingUntil;
    private string _pendingSession = "";

    // Diagnostico de presentacion; siempre devuelve copias, nunca el estado del motor.
    public int[] LocalHandOrder => _handState.Order.ToArray();
    public int LocalDraftCount => _handState.Groups.Count;

    private void InitializeInteraction()
    {
        _selectionLabel = GetNode<Label>(InteractionPath + "SelectionLabel");
        _interactionMessage = GetNode<Label>(InteractionPath + "MessageLabel");
        _targetGame = GetNode<OptionButton>(InteractionPath + "LocalActions/TargetGame");
        _externalSelect = GetNode<CheckButton>(InteractionPath + "LocalActions/ExternalSelect");
        _declareWin = GetNode<CheckButton>(InteractionPath + "NetworkActions/DeclareWin");
        _draftZone = GetNode<CardDropZone>(InteractionPath + "DraftZone");
        _draftRows = GetNode<VBoxContainer>(InteractionPath + "DraftZone/Content/Rows");
        _draftEmpty = GetNode<Label>(InteractionPath + "DraftZone/Content/EmptyLabel");
        InitializeDynamicInteraction();
        AddLocal("PrepareButton", PrepareSelected);
        AddLocal("ClearSelectionButton", () => { _handState.ClearSelection(); RefreshSelection(); });
        AddLocal("ClearDraftsButton", () => { _handState.ClearGroups(); RenderDrafts(); RefreshSelection(); });
        AddLocal("LeftButton", () => MoveSelected(-1));
        AddLocal("RightButton", () => MoveSelected(1));
        _externalSelect.Toggled += SelectExternal;
        foreach (var action in new[]
        {
            ("ExchangeButton", ActionKind.ExchangeCard), ("ContinueButton", ActionKind.FinishInitial),
            ("DrawDeckButton", ActionKind.DrawDeck), ("PassButton", ActionKind.Pass),
            ("PublishButton", ActionKind.Publish), ("ClaimButton", ActionKind.Claim),
            ("DrawDiscardButton", ActionKind.DrawDiscard), ("DesmocheButton", ActionKind.DeclareDesmoche),
            ("PayButton", ActionKind.Pay), ("NoPaymentButton", ActionKind.CompleteWithoutPayment)
        })
        {
            Button button = GetNode<Button>(InteractionPath + "NetworkActions/" + action.Item1);
            ActionKind kind = action.Item2;
            button.Pressed += () => RequestAction(kind);
            _actionButtons.Add(kind, button);
        }
        _hand.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default),
            Callable.From<Vector2, Variant, bool>((_, data) => CanDropHand(data)),
            Callable.From<Vector2, Variant>((_, data) => DropHand(data, _handState.Order.Count)));
        _draftZone.DropAllowed = (_, data) => CanDropDraft(data);
        _draftZone.DropReceived = (_, data) => DropDraft(data);
        _ownGames.MouseFilter = MouseFilterEnum.Pass;
        _ownGames.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default),
            Callable.From<Vector2, Variant, bool>((_, data) => CanDropDraft(data)),
            Callable.From<Vector2, Variant>((_, data) => DropDraft(data)));
    }

    private void AddLocal(string name, Action action)
    {
        Button button = GetNode<Button>(InteractionPath + "LocalActions/" + name);
        button.Pressed += () => { if (CanEdit) action(); };
        _localButtons.Add(button);
    }

    private bool CanEdit => _pendingRequest == 0 && _interactionView is { } view &&
        view.Phase is not (GamePhase.Preparing or GamePhase.Finished) &&
        view.Players.Any(p => p.Id == view.RecipientId && p.Connected && !p.Eliminated);
    private bool CanSend => _pendingRequest == 0 && _session is { IsActive: true } &&
        _session.View is { } networkView &&
        _interactionView is { } shown && networkView.Session == shown.Session &&
        networkView.Revision == shown.Revision && networkView.RecipientId == shown.RecipientId;

    private void SynchronizeInteraction(SnapshotDto? view)
    {
        SnapshotDto? previous = _interactionView;
        // DisplaySnapshot reconstruye los nodos, incluso al cambiar el mensaje de conexion.
        // Cancelar primero evita conservar una referencia al origen que se va a liberar.
        if (GetViewport().GuiIsDragging()) GetViewport().GuiCancelDrag();
        bool contextChanged = view is null || previous is null || previous.Session != view.Session ||
            previous.Window != view.Window || previous.Phase != view.Phase ||
            previous.RecipientId != view.RecipientId;
        if (view is null || previous?.Session != view.Session || previous?.RecipientId != view.RecipientId)
        {
            _pendingRequest = 0;
            _pendingSession = "";
            Message("");
        }
        if (contextChanged)
        {
            _declareWin.SetPressedNoSignal(false);
        }
        // Las confirmaciones ajenas actualizan sus contadores (9 -> 8), pero no
        // invalidan nuestra carta pendiente. RefreshDynamicInteraction comprueba
        // que siga en la mano, sin asignar y permitida por la fase actual.
        if (contextChanged) _stagedCardId = null;
        _handState.Synchronize(view);
        _interactionView = view;
        PopulateTargets();
    }

    private void PopulateTargets()
    {
        int old = _targetGame.ItemCount > 0 ? _targetGame.GetSelectedId() : -1;
        _targetGame.Clear();
        _targetGame.AddItem("Juego nuevo", 0);
        if (_interactionView is { } view)
            foreach (MeldDto meld in view.Melds.Where(m => m.OwnerId == view.RecipientId).OrderBy(m => m.Id))
                _targetGame.AddItem("Ampliar mi juego " + meld.Id, meld.Id);
        int index = _targetGame.GetItemIndex(old);
        _targetGame.Select(index >= 0 ? index : 0);
    }

    private void RenderHand()
    {
        Clear(_hand);
        foreach (int id in _handState.Order)
        {
            Carta card = CreateInteractiveCard(_hand, id, "Carta" + id);
            card.SelectionRequested += ToggleHand;
            card.DragDataRequested = () => BeginHandDrag(id);
            card.DropAllowed = (_, data) => CanDropHand(data);
            card.DropReceived = (position, data) =>
            {
                int target = _handState.Order.ToList().IndexOf(id);
                DropHand(data, target + (position.X >= card.Size.X / 2f ? 1 : 0));
            };
        }
        RefreshSelection();
        QueueCasinoLayout();
    }

    private Carta CreateInteractiveCard(HFlowContainer parent, int id, string name)
    {
        Carta card = _cardScene.Instantiate<Carta>();
        card.Name = name; card.ShowFace(id); parent.AddChild(card);
        card.SetInteraction(CanEdit, false);
        return card;
    }

    private void ToggleHand(int id)
    {
        if (CanEdit && _handState.Toggle(id)) RefreshSelection();
    }

    private Variant BeginHandDrag(int id)
    {
        if (!CanEdit || _interactionView is not { } view || !_handState.Order.Contains(id)) return default;
        if (!_handState.SelectedIds.Contains(id) && !Assigned(id)) _handState.Toggle(id);
        // No reconstruir el nodo que esta iniciando un arrastre nativo.
        RefreshSelection();
        return CardDragData.Create(view, id, CardDragOrigin.Hand);
    }

    private bool Assigned(int id) => _handState.Groups.Any(g => g.Cards.Contains(id));
    private bool ReadDrag(Variant data, out LocalCardDrag drag) =>
        CardDragData.TryRead(data, _interactionView, out drag);
    private bool CanDropHand(Variant data) => CanEdit && ReadDrag(data, out LocalCardDrag drag) &&
        (drag.Origin == CardDragOrigin.Hand && _handState.Order.Contains(drag.CardId) ||
         drag.Origin == CardDragOrigin.Draft && drag.GroupIndex >= 0 &&
         drag.GroupIndex < _handState.Groups.Count &&
         _handState.Groups[drag.GroupIndex].Cards.Contains(drag.CardId) &&
         (_handState.Order.Contains(drag.CardId) || _handState.ExternalCardId == drag.CardId));

    private void DropHand(Variant data, int beforeIndex)
    {
        if (!CanDropHand(data) || !ReadDrag(data, out LocalCardDrag drag)) return;
        if (drag.Origin == CardDragOrigin.Draft)
        {
            if (_handState.ReturnToHand(drag.GroupIndex, drag.CardId))
            { RenderDrafts(); RefreshSelection(); }
            return;
        }
        if (_handState.Move(drag.CardId, beforeIndex)) RenderHand();
    }

    private void MoveSelected(int direction)
    {
        if (_handState.SelectedIds.Count != 1)
        { Message("Selecciona una sola carta para moverla con los botones."); return; }
        int id = _handState.SelectedIds[0];
        int index = _handState.Order.ToList().IndexOf(id);
        int slot = direction < 0 ? index - 1 : index + 2;
        if (slot < 0 || slot > _handState.Order.Count) return;
        _handState.Move(id, slot); RenderHand();
    }

    private bool CanDropDraft(Variant data)
    {
        if (!CanEdit || !ReadDrag(data, out LocalCardDrag drag)) return false;
        return drag.Origin switch
        {
            CardDragOrigin.Hand => _handState.Order.Contains(drag.CardId) && !Assigned(drag.CardId),
            CardDragOrigin.External => _handState.ExternalCardId == drag.CardId && !Assigned(drag.CardId),
            _ => false
        };
    }

    private void DropDraft(Variant data, int? targetMeldId = null, int? groupIndex = null, int? beforeIndex = null)
    {
        if (!CanDropDraft(data) || !ReadDrag(data, out LocalCardDrag drag)) return;
        if (!_handState.StageCards(drag.CardId, targetMeldId, groupIndex, beforeIndex))
        { Message("No se puede colocar esa seleccion en este juego."); return; }
        Message("Colocacion local. Completa el juego, ordena sus cartas y confirma cuando este listo.");
        RenderDrafts(); RefreshSelection();
    }

    private void PrepareSelected()
    {
        int target = _targetGame.GetSelectedId();
        if (!_handState.PrepareGroup(target > 0 ? target : null))
        {
            Message("Selecciona de 3 a 13 cartas para un juego nuevo, o cartas nuevas para ampliar un juego propio. No se pueden repetir cartas entre borradores.");
            RefreshSelection(); return;
        }
        Message("Combinacion preparada localmente. Revisa su orden antes de enviarla.");
        RenderDrafts(); RefreshSelection();
    }

    private void RenderDrafts()
    {
        Clear(_draftRows);
        _draftZone.Visible = _handState.Groups.Count > 0;
        _draftEmpty.Visible = _handState.Groups.Count == 0;
        for (int index = 0; index < _handState.Groups.Count; index++)
        {
            int groupIndex = index;
            VisualGroupDraft draft = _handState.Groups[index];
            var row = new VBoxContainer { Name = "Borrador" + index, MouseFilter = MouseFilterEnum.Pass };
            var header = new HBoxContainer { Name = "Header", MouseFilter = MouseFilterEnum.Pass };
            header.AddChild(Text(draft.MeldId is int meld ? "Ampliacion del juego " + meld : "Juego nuevo " + (index + 1), 14));
            var remove = new Button { Text = "Quitar", Disabled = !CanEdit };
            remove.Pressed += () =>
            {
                if (!CanEdit) return;
                _handState.RemoveGroup(groupIndex); RenderDrafts(); RefreshSelection();
            };
            header.AddChild(remove); row.AddChild(header);
            var flow = new HFlowContainer { Name = "Cards", MouseFilter = MouseFilterEnum.Pass };
            flow.AddThemeConstantOverride("h_separation", 6); row.AddChild(flow); _draftRows.AddChild(row);
            foreach (int id in draft.Cards)
            {
                Carta card = CreateInteractiveCard(flow, id, "Carta" + id);
                card.Selected = true;
                card.DragDataRequested = () => CanEdit && _interactionView is { } view
                    ? CardDragData.Create(view, id, CardDragOrigin.Draft, groupIndex) : default;
                card.DropAllowed = (_, data) => CanDropInsideDraft(data, groupIndex) || CanDropDraft(data);
                card.DropReceived = (position, data) =>
                {
                    int target = _handState.Groups[groupIndex].Cards.ToList().IndexOf(id);
                    int slot = target + (position.X >= card.Size.X / 2f ? 1 : 0);
                    if (CanDropInsideDraft(data, groupIndex)) DropInsideDraft(data, groupIndex, slot);
                    else DropDraft(data, groupIndex: groupIndex, beforeIndex: slot);
                };
            }
            flow.SetDragForwarding(Callable.From<Vector2, Variant>(_ => default),
                Callable.From<Vector2, Variant, bool>((_, data) => CanDropInsideDraft(data, groupIndex) || CanDropDraft(data)),
                Callable.From<Vector2, Variant>((_, data) =>
                {
                    if (CanDropInsideDraft(data, groupIndex))
                        DropInsideDraft(data, groupIndex, _handState.Groups[groupIndex].Cards.Count);
                    else DropDraft(data, groupIndex: groupIndex);
                }));
        }
        QueueCasinoLayout();
    }

    private bool CanDropInsideDraft(Variant data, int group) => CanEdit &&
        ReadDrag(data, out LocalCardDrag drag) && drag.Origin == CardDragOrigin.Draft &&
        drag.GroupIndex == group && group < _handState.Groups.Count &&
        _handState.Groups[group].Cards.Contains(drag.CardId);
    private void DropInsideDraft(Variant data, int group, int beforeIndex)
    {
        if (!CanDropInsideDraft(data, group) || !ReadDrag(data, out LocalCardDrag drag)) return;
        if (_handState.MoveInGroup(group, drag.CardId, beforeIndex)) RenderDrafts();
    }

    private void SelectExternal(bool selected)
    {
        if (CanEdit) _handState.SelectExternal(selected);
        RefreshSelection();
    }

    private void BindExternalCard(Carta card, int? id)
    {
        card.DragDataRequested = null; card.DropAllowed = null; card.DropReceived = null;
        if (id is not int source || source != _handState.ExternalCardId)
        { card.SetInteraction(false, false); return; }
        card.SetInteraction(CanEdit && !Assigned(source), _handState.ExternalSelected || Assigned(source));
        // Conectar una sola vez en Ready; la identidad se comprueba en el callback.
        card.DragDataRequested = () =>
        {
            if (!CanEdit || _interactionView is not { } view || !_handState.SelectExternal(true)) return default;
            RefreshSelection(); return CardDragData.Create(view, source, CardDragOrigin.External);
        };
    }

    private void ToggleExternal(int id)
    {
        if (!CanEdit || _handState.ExternalCardId != id) return;
        _handState.SelectExternal(!_handState.ExternalSelected); RefreshSelection();
    }

    private bool CanDropPayment(Variant data) => CanEdit && SlotAction is not null &&
        ReadDrag(data, out LocalCardDrag drag) && drag.Origin == CardDragOrigin.Hand &&
        _interactionView!.OwnHand.Contains(drag.CardId) && !Assigned(drag.CardId);
    private void DropPayment(Variant data)
    {
        if (!CanDropPayment(data) || !ReadDrag(data, out LocalCardDrag drag)) return;
        _stagedCardId = drag.CardId;
        _handState.ClearSelection();
        Message("Carta preparada. Pulsa Aceptar para confirmar.");
        RefreshSelection();
    }

    private void RequestAction(ActionKind kind)
    {
        if (!CanSend || _interactionView is not { } view)
        { Message("No hay una partida activa para enviar esta acción."); return; }
        if (!InteractionActions.TryCreate(view, _handState, kind, _declareWin.ButtonPressed,
                out VisualActionRequest? request, out string message))
        { Message(message); return; }
        SendRequest(request!);
    }

    private void SendRequest(VisualActionRequest request)
    {
        if (!CanSend || _session is null || _interactionView is not { } view ||
            !InteractionActions.CanRequest(view, request.Kind)) return;
        long id = _session.Submit(request.Kind, cardId: request.CardId,
            plays: request.Plays, declareWin: request.DeclareWin,
            initialKind: request.InitialKind, cards: request.Cards);
        if (id <= 0) { Message("No se pudo enviar la solicitud. Revisa la conexion."); return; }
        _pendingRequest = id; _pendingSession = view.Session;
        _pendingUntil = Time.GetTicksMsec() + 10000;
        Message("Validando jugada…");
        RefreshSelection(); RenderDrafts();
    }

    private void OnActionCompleted(ReplyDto reply)
    {
        if (reply.RequestId != _pendingRequest || _pendingRequest == 0 ||
            _interactionView?.Session != _pendingSession) return;
        _pendingRequest = 0;
        // El resultado visible siempre viene de ViewChanged; Reply no retira cartas.
        if (reply.Success) _handState.ClearGroups();
        Message(reply.Message + (reply.Penalized ? " Has sido eliminado por esta declaracion." : ""));
        RenderDrafts(); RefreshSelection();
    }

    public override void _Process(double delta)
    {
        // FlowContainer coloca las cartas de forma diferida; seguir sus límites visibles.
        if (_ready && _casinoInitialized) PositionPasaButton();
        if (_pendingRequest == 0 || Time.GetTicksMsec() < _pendingUntil) return;
        _pendingRequest = 0;
        Message("No se recibio confirmacion. La mesa conserva el estado recibido del servidor; revisa la conexion antes de volver a intentar.");
        RenderDrafts(); RefreshSelection();
    }

    private void RefreshSelection()
    {
        foreach (Carta card in _hand.GetChildren().OfType<Carta>())
            if (card.FaceCardId is int id)
            {
                card.SetInteraction(CanEdit, _handState.SelectedIds.Contains(id) || Assigned(id));
                card.TooltipText = Assigned(id) ? "Incluida en un borrador; sigue en tu mano hasta la confirmacion." :
                    "Click o Enter: seleccionar. Arrastrar: ordenar o preparar combinacion.";
            }
        BindExternalCard(_offered, _interactionView?.OfferedCard);
        BindExternalCard(_discard, _interactionView?.DiscardTop);
        _selectionLabel.Text = _handState.SelectedIds.Count + " cartas seleccionadas" +
            (_handState.ExternalSelected ? " + carta publica" : "") + " · " + _handState.Groups.Count + " borradores";
        _selectionLabel.Visible = _handState.SelectedIds.Count > 0 || _handState.ExternalSelected || _handState.Groups.Count > 0;
        GetNode<Label>(InteractionPath + "HintLabel").Visible = _handState.Groups.Count > 0;
        _externalSelect.SetPressedNoSignal(_handState.ExternalSelected);
        _externalSelect.Disabled = !CanEdit || _handState.ExternalCardId is null ||
            _handState.ExternalCardId is int source && Assigned(source);
        _externalSelect.Text = _interactionView?.Phase == GamePhase.AwaitingClaims ? "Incluir oferta" : "Incluir pozo";
        _targetGame.Disabled = !CanEdit;
        foreach (Button button in _localButtons) button.Disabled = !CanEdit;
        _draftZone.DropEnabled = CanEdit;
        _paymentZone.DropEnabled = CanSend && InteractionActions.CanRequest(_interactionView, ActionKind.Pay);
        _paymentZone.TooltipText = _paymentZone.DropEnabled ? "Suelta una carta propia para solicitar el pago." :
            "El descarte solo se permite durante tu pago.";
        foreach (var entry in _actionButtons)
        {
            bool available = InteractionActions.CanRequest(_interactionView, entry.Key);
            entry.Value.Visible = available;
            entry.Value.Disabled = !CanSend;
        }
        _declareWin.Visible = InteractionActions.CanRequest(_interactionView, ActionKind.Claim) ||
            InteractionActions.CanRequest(_interactionView, ActionKind.DrawDiscard);
        _declareWin.Disabled = !CanSend;
        RefreshDynamicInteraction();
    }

    private void Message(string text)
    {
        _interactionMessage.Text = text;
        _interactionMessage.Visible = text.Length > 0;
    }
}
