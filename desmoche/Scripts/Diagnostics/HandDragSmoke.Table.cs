using System;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Desmoche.Presentation.Interaction;
using Desmoche.Sessions;
using Godot;

namespace Desmoche.Diagnostics;

public partial class HandDragSmoke
{
    private sealed class TableSession : IGameSession
    {
        public bool IsActive => true;
        public bool IsLocal => false;
        public bool CanManageMatch => false;
        public SnapshotDto? View { get; }
        public string Status => "Mesa de prueba";
        public event Action<SnapshotDto>? ViewChanged;
        public event Action<ReplyDto>? ActionCompleted;
        public event Action? SessionChanged;
        public VisualActionRequest? Request { get; private set; }
        public int Requests { get; private set; }
        public int CloseCalls { get; private set; }
        public TableSession(SnapshotDto view) { View = view; }
        public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
            bool declareWin = false, InitialVictoryKind? initialKind = null, int[]? cards = null, int? sourceMeldId = null)
        { Requests++; Request = new(kind, cardId, plays, declareWin, initialKind, cards); return 300 + Requests; }
        public void Close(string reason = "Partida cerrada.") { CloseCalls++; SessionChanged?.Invoke(); }
        public void Notify() { ViewChanged?.Invoke(View!); ActionCompleted?.Invoke(new ReplyDto(300 + Requests, false, "Rechazado", false, 1)); }
    }

    private async Task ValidateTableEditing()
    {
        SnapshotDto original = _view;
        foreach (Vector2I resolution in new[] { new Vector2I(640, 360), new(844, 390), new(1280, 720),
            new(1920, 1080), new(2400, 1080), new(1024, 768), new(3440, 1440) })
        {
            _viewport.Size = resolution;
            SnapshotDto fixture = original with { Session = Guid.NewGuid().ToString("N"),
                Phase = GamePhase.AwaitingClaims, OwnHand = new[] { 26, 7, 8, 9, 50 }, OfferedCard = 13,
                Players = new[] { new PublicPlayerDto(1, "Tú", 0, 5, true, false), new PublicPlayerDto(2, "Rival", 1, 9, true, false) },
                Melds = new[] { new MeldDto(42, 1, new[] { 0, 1, 2, 3 }) }, DeckCount = 33,
                OfferTiming = new OfferTimingDto(13, 5000, new[] { new CardReservationDto(1, 20000) }, false) };
            var session = new TableSession(fixture); _mesa.Bind(session); await Settle();
            string before = NetworkProtocol.Encode(fixture);
            Label status = _mesa.GetNode<Label>(Mesa.TableStatusPath + "/StatusLabel");
            Check(status.IsVisibleInTree() && status.Text.Length > 0 && status.Size.X > 100, "El turno y mensajes tienen una zona visible en la pantalla");
            Check(_mesa.GetNode<MesaActionClock>(Mesa.PreparationClockPath).IsVisibleInTree(), "El plazo de preparación aparece en su círculo dentro de la mesa");
            ScrollContainer page = _mesa.GetNode<ScrollContainer>("Scroll");
            Check(page.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled && page.ScrollVertical == 0,
                "La mesa no tiene otra pantalla al desplazar hacia abajo");
            await Click(Card(7)); await Click(Card(8)); await Click(Card(9));
            Check(new[] { 7, 8, 9 }.All(id => Card(id).HasGameIndicator), "La selección válida mantiene el verde antes de preparar");
            await Click(PaymentButton("PrepareSelectionButton"));
            Check(_mesa.LocalDraftCount == 1 && new[] { 7, 8, 9 }.All(id => !Card(id).Visible) && session.Requests == 0,
                "Preparar mueve el dibujo a Mi juego sin publicar ni perder cartas reales");
            Control draft = _mesa.GetNode<Control>(Mesa.InteractionPath + "DraftZone/Content/Rows/Borrador0");
            Control material = _mesa.GetNode<Control>(Mesa.ContentPath + "Material");
            Rect2 bounds = material.GetGlobalRect();
            Check(draft.GetChildren().OfType<Panel>().Single().IsVisibleInTree() &&
                draft.GetNode<HFlowContainer>("Cards").GetChildren().OfType<Carta>().All(card => bounds.Encloses(card.GetGlobalRect())),
                "Los borradores con marca dorada se muestran dentro de la mesa");
            Check(_mesa.GetNode<HFlowContainer>(Mesa.OwnGamesPath + "/Juego42").IsVisibleInTree(), "Un juego nuevo conserva el publicado visible");
            Check(draft.GetNode<HFlowContainer>("Cards").GetChildren().OfType<Carta>().All(card => draft.GetGlobalRect().Encloses(card.GetGlobalRect())),
                "Todas las cartas del borrador caben en su marco sin quedar recortadas");
            if (resolution == new Vector2I(1280, 720)) await Capture("Edicion-en-mesa-1280x720.png");
            if (resolution == new Vector2I(844, 390)) await Capture("Edicion-en-mesa-844x390.png");
            await Click(draft.GetNode<Button>("RemoveButton"));
            Check(_mesa.LocalDraftCount == 0 && VisibleHandIds().SequenceEqual(fixture.OwnHand), "Quitar devuelve la preparación a la mano en su orden local");

            await Click(_mesa.GetNode<Button>(Mesa.OwnGamesPath + "/EditGame42"));
            Check(session.Requests == 0 && _mesa.GetNode<Button>(Mesa.OwnGamesPath + "/EditGame42").Text == "Nuevo",
                "Editar el juego solo elige el destino local y no publica");
            await Click(_mesa.GetNode<Button>(Mesa.OwnGamesPath + "/EditGame42"));
            await Click(_mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta")); await Click(Card(26));
            Carta donor = _mesa.GetNode<HFlowContainer>(Mesa.OwnGamesPath + "/Juego42").GetChildren().OfType<Carta>().Single(card => card.FaceCardId == 0);
            // Click en el extremo de A234 prepara la actualización atómica existente.
            await Click(donor);
            Check(_mesa.LocalDraftCount == 2 && !Card(26).Visible &&
                !_mesa.GetNode<HFlowContainer>(Mesa.OwnGamesPath + "/Juego42").Visible,
                "Trasmoche muestra origen reducido y destino, sin duplicar el juego publicado");
            Control origin = _mesa.GetNode<Control>(Mesa.InteractionPath + "DraftZone/Content/Rows/Borrador0");
            Control target = _mesa.GetNode<Control>(Mesa.InteractionPath + "DraftZone/Content/Rows/Borrador1");
            Check(origin.GetNode<HFlowContainer>("Cards").GetChildren().OfType<Carta>().Select(c => c.FaceCardId!.Value).SequenceEqual(new[] { 1, 2, 3 }) &&
                target.GetNode<HFlowContainer>("Cards").GetChildren().OfType<Carta>().Select(c => c.FaceCardId!.Value).ToHashSet().SetEquals(new[] { 0, 13, 26 }),
                "La edición conserva exactamente las cartas del origen y destino");
            await Click(target.GetNode<Button>("RemoveButton"));
            Check(_mesa.LocalDraftCount == 0 && Card(26).Visible && _mesa.GetNode<HFlowContainer>(Mesa.OwnGamesPath + "/Juego42").Visible,
                "Cancelar trasmoche restaura juntos origen, destino y mano");

            await Click(_mesa.GetNode<Button>(Mesa.TableStatusPath + "/InfoButton"));
            Check(_mesa.GetNode<Control>(Mesa.TableInfoPath).Visible && bounds.Size.X > 0,
                "La información completa y mensajes siguen disponibles con Info");
            Feed(new InputEventKey { Pressed = true, Keycode = Key.Escape }); await Settle();
            Check(!_mesa.GetNode<Control>(Mesa.TableInfoPath).Visible, "Escape cierra Info sin cambiar los juegos");
            await Click(_mesa.GetNode<Button>(Mesa.TableStatusPath + "/MenuButton"));
            Check(_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/LeaveButton").IsVisibleInTree(), "Menú mantiene la salida de partida accesible");
            Feed(new InputEventKey { Pressed = true, Keycode = Key.Escape }); await Settle();
            Check(!_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/LeaveButton").IsVisibleInTree(), "Cerrar Menú no deja controles superpuestos");
            await Click(Card(7)); await Click(Card(8)); await Click(Card(9)); await Click(PaymentButton("PrepareSelectionButton"));
            await Click(PaymentButton("PublishButton"));
            Check(session.Requests == 1 && session.Request is { Kind: ActionKind.Publish, Plays: not null } request &&
                request.Plays.Single().Cards.SequenceEqual(new[] { 7, 8, 9 }), "Confirmar conserva la solicitud existente con sus cartas y orden");
            Check(NetworkProtocol.Encode(fixture) == before, "Ningún gesto ni cambio de distribución modifica el snapshot");
            await Click(_mesa.GetNode<Button>(Mesa.TableStatusPath + "/MenuButton"));
            await Click(_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/LeaveButton"));
            Check(session.CloseCalls == 1, "El clic nativo en Salir llega al cierre de sesión, también mientras espera una acción");
            _mesa.Unbind();
            GD.Print("TABLE EDITING CASE: " + resolution);
        }
        // Muchas preparaciones incompletas siguen accesibles sin agrandar la
        // página. La navegación afecta solo a Mi juego.
        _viewport.Size = new Vector2I(844, 390);
        SnapshotDto many = original with { Session = Guid.NewGuid().ToString("N"), Phase = GamePhase.AwaitingDraw,
            CurrentPlayerId = 1, OwnHand = Enumerable.Range(30, 9).ToArray() };
        _mesa.Bind(new TableSession(many)); await Settle();
        foreach (int id in many.OwnHand)
        { await Click(Card(id)); await Click(PaymentButton("PrepareSelectionButton")); }
        Check(_mesa.LocalDraftCount == 9 && VisibleHandIds().Length == 0,
            "Nueve preparaciones incompletas conservan todas las cartas y el contador autoritativo");
        Button next = _mesa.GetNode<Button>(Mesa.ContentPath + "NextGamesButton");
        Check(next.Visible && !next.Disabled, "Las preparaciones adicionales tienen navegación dentro de Mi juego");
        await Click(next);
        await Click(_mesa.GetNode<Button>(Mesa.InteractionPath + "DraftZone/Content/Rows/Borrador8/RemoveButton"));
        Check(_mesa.LocalDraftCount == 8 && Card(38).Visible,
            "Se puede alcanzar y quitar la última preparación sin salir de la pantalla: " + _mesa.LocalDraftCount +
            " rect=" + _mesa.GetNode<Control>(Mesa.InteractionPath + "DraftZone/Content/Rows").GetGlobalRect());
        _mesa.Unbind(); _view = original;
    }
}
