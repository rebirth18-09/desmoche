using System.Linq;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Desmoche.Singleplayer;
using Godot;

namespace Desmoche.Presentation;

// Solo construye la declaración elegida. La sesión decide si cumple las reglas.
public partial class Mesa
{
    private OptionButton _initialVictoryType = null!;
    private Button _declareInitial = null!;
    private Label _initialHint = null!;

    private void InitializeInitialDeclarations()
    {
        _initialVictoryType = new OptionButton { Name = "InitialVictoryType" };
        _initialVictoryType.AddThemeFontSizeOverride("font_size", 14);
        _initialVictoryType.AddItem("Victoria inicial…", -1);
        _initialVictoryType.AddItem("A–2–3–4–5", (int)InitialVictoryKind.LowStraight);
        _initialVictoryType.AddItem("10–J–Q–K–A", (int)InitialVictoryKind.HighStraight);
        _initialVictoryType.AddItem("Cuarta", (int)InitialVictoryKind.Cuarta);
        _initialVictoryType.AddItem("Color", (int)InitialVictoryKind.Color);
        _initialVictoryType.AddItem("Peladilla", (int)InitialVictoryKind.Peladilla);
        _initialVictoryType.ItemSelected += _ => RefreshSelection();
        _contextActions.AddChild(_initialVictoryType);
        _initialHint = Text("", 12);
        _initialHint.Name = "InitialVictoryHint";
        _contextActions.AddChild(_initialHint);
        _declareInitial = ContextButton("DeclareInitialButton", "Declarar victoria inicial");
        _declareInitial.Pressed += RequestInitialVictory;
    }

    private void RefreshInitialDeclarations()
    {
        bool ownInitial = InteractionActions.CanRequest(_interactionView, ActionKind.DeclareInitial);
        bool opening = InteractionActions.CanRequest(_interactionView, ActionKind.Claim) &&
            _interactionView!.OwnHand.Length == 9 &&
            !_interactionView.Melds.Any(m => m.OwnerId == _interactionView.RecipientId) &&
            (_session is not LocalGameSession local || local.OpeningOffer);
        bool available = ownInitial || opening;
        _initialVictoryType.Visible = available;
        _initialVictoryType.Disabled = !CanSend;
        _initialVictoryType.SetItemDisabled(4, opening);
        _initialVictoryType.SetItemDisabled(5, opening);
        if (opening && _initialVictoryType.Selected >= 4) _initialVictoryType.Select(0);
        bool selected = available && _initialVictoryType.Selected > 0;
        _initialHint.Visible = _declareInitial.Visible = selected;
        _declareInitial.Disabled = !CanSend;
        _initialHint.Text = opening
            ? "Solo en la primera oferta. Prepara un juego que la incluya. Declarar mal elimina."
            : _initialVictoryType.Selected >= 4
                ? "Se evalúan tus nueve cartas. Declarar mal elimina."
                : "Selecciona las cartas en el orden de tu mano. Declarar mal elimina.";
        _initialHint.CustomMinimumSize = new Vector2(180, 0);
    }

    private void RequestInitialVictory()
    {
        if (!CanSend || _interactionView is not { } view || _initialVictoryType.Selected <= 0) return;
        var kind = (InitialVictoryKind)_initialVictoryType.GetSelectedId();
        if (InteractionActions.CanRequest(view, ActionKind.DeclareInitial))
        {
            int[]? cards = kind is InitialVictoryKind.Color or InitialVictoryKind.Peladilla
                ? null : _handState.SelectedIds.ToArray();
            if (cards is { Length: 0 }) { Message("Selecciona las cartas de la declaración."); return; }
            SendRequest(new VisualActionRequest(ActionKind.DeclareInitial, InitialKind: kind, Cards: cards));
        }
        else if (InteractionActions.CanRequest(view, ActionKind.Claim))
        {
            if (!InteractionActions.TryCreate(view, _handState, ActionKind.Claim, false,
                    out VisualActionRequest? request, out string message))
            { Message(message); return; }
            SendRequest(request! with { InitialKind = kind });
        }
    }
}
