using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Pruebas de presentación con vistas aisladas. No instalan snapshots en el
// motor ni habilitan el envío sin una sesión de red autoritativa real.
public partial class MesaDynamicSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";
    private const string DraftPath = Mesa.InteractionPath + "DraftZone/Content/Rows";
    private static readonly Vector2I[] Resolutions =
    {
        new(2016, 780), new(1200, 900), new(976, 874)
    };
    private static readonly string[] Names = { "Oferta", "Pago", "Intercambio", "Inicio", "Turno" };
    private Mesa _mesa = null!;
    private Viewport _testViewport = null!;
    private SnapshotDto _shown = null!;
    private string _before = "", _captureDirectory = "";
    private bool _auto, _done;
    private int _checks;

    public override void _Ready()
    {
        base._Ready();
        string[] arguments = OS.GetCmdlineUserArgs();
        _auto = arguments.Contains("--auto");
        _captureDirectory = arguments.FirstOrDefault(a => a.StartsWith("--capture-dir=", StringComparison.Ordinal))?[14..] ?? "";
        Main tested = this;
        _testViewport = GetViewport();
        if (_auto)
        {
            // El viewport raíz consulta el ratón del sistema para terminar un
            // drag. El SubViewport conserva los eventos de su propia GUI,
            // permitiendo una prueba nativa determinista sin cursor del SO.
            GetNode<Control>("Margin").Hide();
            var isolated = new SubViewport
            {
                Name = "TestViewport", Size = Resolutions[0],
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = true
            };
            AddChild(isolated);
            tested = GD.Load<PackedScene>("res://Scenes/Diagnostics/DiagnosticMain.tscn").Instantiate<Main>();
            isolated.AddChild(tested);
            _testViewport = isolated;
            var render = new TextureRect
            {
                Name = "RenderedTest", Texture = isolated.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(render);
            render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            GD.Print("MESA DYNAMIC INPUT: GUI nativa en SubViewport aislado; no utiliza ni mueve el cursor del SO.");
        }
        _mesa = tested.GetNode<Mesa>("Margin/Screens/Mesa");
        tested.GetNode<Control>("Margin/Screens/Lobby").Visible = false;
        _mesa.Visible = true;
        Show(CreateFixture(0));
        if (_auto) Run();
        else GD.Print("MESA DYNAMIC MANUAL: vista sin conexión; los gestos preparan intenciones locales.");
    }

    private async void Run()
    {
        try
        {
            for (int size = 0; size < Resolutions.Length; size++)
            {
                GetWindow().Size = Resolutions[size];
                if (_testViewport is SubViewport isolated) isolated.Size = Resolutions[size];
                for (int fixture = 0; fixture < Names.Length; fixture++)
                {
                    Show(CreateFixture(fixture));
                    await Settle();
                    ValidateGeometry(fixture);
                    ValidatePrivacy();
                    if (_captureDirectory.Length > 0 && fixture < 3 && size < 2)
                        await Capture(Names[fixture] + "-" + Resolutions[size].X + "x" + Resolutions[size].Y + ".png");
                    if (fixture == 0 && DisplayServer.GetName() != "headless")
                        await NativeReorder();
                    GD.Print("MESA DYNAMIC CASE: " + Names[fixture] + " · " + Resolutions[size]);
                }
            }
            await ValidateRevisionButtons();
            await ValidateWinningCardsAndPasa();
            Show(CreateFixture(0));
            await Settle();
            await ValidateGestures();
            ValidatePrivacy();
            Finish(true, "Posiciones, gestos locales, confirmación contextual y privacidad verificados.");
        }
        catch (Exception error)
        {
            if (_testViewport.GuiIsDragging()) _testViewport.GuiCancelDrag();
            Finish(false, error.Message);
        }
    }

    private async Task ValidateRevisionButtons()
    {
        Button Button(string name) => _mesa.GetNode<Button>(Mesa.ContextActionsPath + name);
        SnapshotDto initial = CreateFixture(3) with { Session = new string('b', 32),
            OwnHand = new[] { 0, 14, 2, 26, 1, 45, 3, 22, 50 } };
        Show(initial);
        await Settle();
        Check(!Button("DeclareInitialButton").IsVisibleInTree() && Button("PeladillaButton").IsVisibleInTree(),
            "Mano desordenada sin juegos contiguos muestra Peladilla, sin declarar victoria.");
        for (int id = 1; id <= 3; id++)
        {
            Carta target = HandCard(id - 1);
            Variant drag = CardDragData.Create(_shown, id, CardDragOrigin.Hand);
            Check(target._CanDropData(new Vector2(target.Size.X, 0), drag), "Reordenar secuencia inicial mediante arrastre.");
            target._DropData(new Vector2(target.Size.X, 0), drag);
        }
        Check(Button("DeclareInitialButton").IsVisibleInTree() && !Button("PeladillaButton").IsVisibleInTree(),
            "Ordenar A234 hace aparecer Desmoche y oculta Peladilla sin enviar cartas.");
        ValidatePrivacy();
        SnapshotDto color = initial with { Session = new string('c', 32),
            OwnHand = new[] { 13, 28, 15, 30, 17, 32, 19, 34, 21 } };
        Show(color with { Phase = GamePhase.InitialExchange });
        Check(!Button("ColorButton").IsVisibleInTree(), "Color no se declara antes del cambio.");
        Show(color);
        await Settle();
        Check(Button("ColorButton").IsVisibleInTree(), "Color aparece tras el cambio con todas rojas.");
        if (_captureDirectory.Length > 0) await Capture("Color-976x874.png");
        SnapshotDto challenge = initial with { Phase = GamePhase.AwaitingOrderChallenge,
            CurrentPlayerId = 1, OwnHand = Enumerable.Range(0, 9).ToArray(),
            Players = new[] { initial.Players[0], initial.Players[1] with { CardCount = 6 } },
            Melds = new[] { new MeldDto(1, 2, new[] { 18, 31, 44 }) },
            OrderChallenge = new OrderChallengeDto(2, 1, 3000) };
        Show(challenge);
        await Settle();
        Check(_mesa.GetNode<Button>(Mesa.PasaButtonPath).IsVisibleInTree(), "El rival puede ver Pasa durante el plazo.");
        if (_captureDirectory.Length > 0) await Capture("Pasa-976x874.png");
        Show(challenge with { RecipientId = 2, OwnHand = new[] { 39, 40, 41, 42, 43, 45 } });
        Check(!_mesa.GetNode<Button>(Mesa.PasaButtonPath).IsVisibleInTree(), "Pasa permanece oculto para el infractor.");
        Show(challenge with { Phase = GamePhase.AwaitingDraw, OrderChallenge = null,
            Melds = new[] { new MeldDto(1, 2, new[] { 18, 44, 31 }) } });
        Check(!_mesa.GetNode<Button>(Mesa.PasaButtonPath).IsVisibleInTree(), "Pasa desaparece al resolver la oportunidad.");
        Show(CreateFixture(1));
        Check(!_mesa.HasNode(Mesa.ContextActionsPath + "DesmocheButton"),
            "En juego normal no hay un botón de victoria manual.");
    }

    private async Task ValidateWinningCardsAndPasa()
    {
        foreach (Vector2I size in Resolutions)
        {
            GetWindow().Size = size;
            if (_testViewport is SubViewport isolated) isolated.Size = size;
            SnapshotDto initial = CreateFixture(3);
            foreach (InitialVictoryKind kind in Enum.GetValues<InitialVictoryKind>())
            {
                int[] proof = kind switch
                {
                    InitialVictoryKind.Color => new[] { 13, 28, 15, 30, 17, 32, 19, 34, 21 },
                    InitialVictoryKind.Peladilla => new[] { 0, 4, 8, 14, 18, 22, 28, 32, 36 },
                    InitialVictoryKind.LowStraight => new[] { 0, 1, 2, 3 },
                    InitialVictoryKind.HighStraight => new[] { 10, 11, 12, 0 },
                    _ => new[] { 5, 18, 44, 31 }
                };
                foreach (int winner in new[] { 1, 2 })
                {
                    SnapshotDto won = initial with { Phase = GamePhase.Finished, Result = MatchResult.InitialVictory,
                        WinnerPlayerId = winner, InitialVictory = new(kind, proof),
                        RevealedHands = new[] { new RevealedHandDto(winner, proof) } };
                    Show(won); await Settle();
                    string location = winner == 1 ? Mesa.OwnGamesPath : Mesa.RivalGamesPath + "/JuegosJugador2/Rows";
                    Control group = _mesa.GetNode<Control>(location + "/VictoriaInicial");
                    Check(Cards(group).Select(c => c.FaceCardId!.Value).SequenceEqual(proof),
                        "La mesa muestra el orden exacto de " + kind + " tanto propio como rival.");
                    Check(!Cards(group).Any(card => Overlaps(card.GetGlobalRect(),
                        _mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta").GetGlobalRect())),
                        "Las cartas de victoria permanecen en la zona del ganador.");
                    Check(!_mesa.GetNode<Button>(Mesa.PasaButtonPath).IsVisibleInTree(), "No hay Pasa tras finalizar.");
                    ValidatePrivacy();
                    if (_captureDirectory.Length > 0 && winner == 2 && size.X == 1200 &&
                        kind is InitialVictoryKind.Color or InitialVictoryKind.Peladilla)
                        await Capture("Victoria-" + kind + "-1200x900.png");
                }
            }
            foreach (int offender in new[] { 2, 3, 4 })
            {
                SnapshotDto challenge = initial with { Phase = GamePhase.AwaitingOrderChallenge,
                    Players = new[] { initial.Players[0], initial.Players[1],
                        new PublicPlayerDto(3, "Ana", 2, 6, true, false), new PublicPlayerDto(4, "Luis", 3, 6, true, false) },
                    Melds = new[] { new MeldDto(7, offender, new[] { 18, 31, 44 }),
                        new MeldDto(8, offender, new[] { 39, 40, 41, 42 }) },
                    OrderChallenge = new(offender, 1, 3000) };
                Show(challenge); await Settle();
                Control group = _mesa.GetNode<Control>(Mesa.RivalGamesPath + "/JuegosJugador" + offender + "/Rows/Juego7");
                Button pasa = _mesa.GetNode<Button>(Mesa.PasaButtonPath);
                Rect2[] cardRects = Cards(group).Select(card => card.GetGlobalRect()).ToArray();
                Vector2 start = new(cardRects.Min(rect => rect.Position.X), cardRects.Min(rect => rect.Position.Y));
                Vector2 end = new(cardRects.Max(rect => rect.End.X), cardRects.Max(rect => rect.End.Y));
                Rect2 target = new(start, end - start), button = pasa.GetGlobalRect();
                Check(pasa.IsVisibleInTree() && !Overlaps(target, button), "Pasa queda al lado del grupo, sin tapar sus cartas.");
                Rect2 clock = _mesa.GetNode<MesaActionClock>(Mesa.PasaClockPath).GetGlobalRect();
                Rect2 action = button.Merge(clock);
                float gapX = Math.Max(0, Math.Max(action.Position.X - target.End.X, target.Position.X - action.End.X));
                float gapY = Math.Max(0, Math.Max(action.Position.Y - target.End.Y, target.Position.Y - action.End.Y));
                Check(_mesa.GetNode<MesaActionClock>(Mesa.PasaClockPath).IsVisibleInTree() &&
                    !Overlaps(target, clock) && new Vector2(gapX, gapY).Length() <= 52,
                    "Pasa y su círculo quedan juntos y próximos a las cartas reales, sin taparlas. grupo=" + target + " acción=" + action);
                Check(!Overlaps(button, _mesa.GetNode<Control>(Mesa.CenterPath + "Mazo/Content/TitleLabel").GetGlobalRect()),
                    "Pasa no tapa el nombre del mazo.");
                Check(!Cards(_mesa.GetNode(Mesa.RivalGamesPath)).Any(c => Overlaps(button, c.GetGlobalRect())),
                    "Pasa no tapa otros juegos publicados.");
                if (_captureDirectory.Length > 0 && offender == 3 && size.X == 1200)
                {
                    GD.Print($"PASA GEOMETRY: group={target}; button={button}; local={pasa.Position}; scale={pasa.Scale}");
                    // La fixture no envía comandos, pero esta captura muestra el estilo habilitado real.
                    pasa.Disabled = false;
                    await Settle();
                    await Capture("Pasa-junto-al-juego.png");
                }
            }
        }
        Show(CreateFixture(0) with { OwnResponseSubmitted = true });
        Check(!_mesa.GetNode<Button>(Mesa.ContextActionsPath + "ClaimButton").IsVisibleInTree() &&
            !_mesa.GetNode<Button>(Mesa.ContextActionsPath + "PassButton").IsVisibleInTree(),
            "Quien ya pagó no ve aceptar ni rechazar su propia carta.");
    }

    private void Show(SnapshotDto view)
    {
        _shown = view;
        _before = NetworkProtocol.Encode(view);
        _mesa.DisplaySnapshot(view, "Diagnóstico de mesa dinámica · Sin conexión de red");
        Check(NetworkProtocol.Encode(view) == _before, "Representar no altera el estado recibido.");
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(0.12), Timer.SignalName.Timeout);
    }

    private void ValidateGeometry(int fixture)
    {
        Carta pozo = _mesa.GetNode<Carta>(Mesa.CenterPath + "Pozo/Content/Carta");
        Carta offered = _mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta");
        Carta deck = _mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta");
        Check(deck.IsBack && deck.FaceCardId is null, "El mazo conserva su dorso privado.");
        if (fixture == 0)
        {
            Rect2 offer = offered.GetGlobalRect(), discard = pozo.GetGlobalRect();
            Check(offered.IsVisibleInTree() && offered.FaceCardId == _shown.OfferedCard,
                "La oferta pública corresponde al estado recibido.");
            Check(offer.Position.X >= discard.End.X + 1,
                "La carta ofrecida está a la derecha del pozo.");
            Check(Math.Abs(offer.Position.Y - discard.Position.Y) < 2,
                "La oferta conserva la fila horizontal del pozo.");
            Carta[] ownGames = Cards(_mesa.GetNode(Mesa.OwnGamesPath));
            Check(ownGames.All(card => !Overlaps(offer, card.GetGlobalRect())),
                "La oferta no se superpone a los juegos propios.");
        }
        else Check(!offered.IsVisibleInTree(), "La oferta desaparece fuera de su ventana.");
        Control self = _mesa.GetNode<Control>(Mesa.SelfPlayerPath).GetNode<Control>("Jugador1");
        foreach (string path in new[]
        {
            Mesa.ContentPath + "TopBar/TitleLabel", Mesa.ContentPath + "Header/ConnectionLabel",
            Mesa.ContentPath + "Header/PhaseLabel", Mesa.ContentPath + "Header/TurnLabel"
        })
        {
            Label label = _mesa.GetNode<Label>(path);
            Check(!label.IsVisibleInTree() || label.Text.Length == 0 || !Overlaps(self.GetGlobalRect(), label.GetGlobalRect()),
                "El panel propio no tapa el texto de " + label.Name + ".");
        }
        Check(!_mesa.GetNode<Control>(Mesa.InteractionPath + "LocalActions").IsVisibleInTree(),
            "Los controles antiguos de preparación y desplazamiento no aparecen.");
        Check(!_mesa.GetNode<OptionButton>(Mesa.InteractionPath + "LocalActions/TargetGame").IsVisibleInTree(),
            "Crear o ampliar un juego no requiere elegir un menú.");
        Check(_mesa.RenderedPlayerCount == _shown.Players.Length, "Todos los jugadores tienen su panel.");
        Check(Descendants(self).OfType<Label>().Any(label => label.Text == _shown.OwnHand.Length + " cartas en mano"),
            "El contador propio usa las cartas reales, también durante el pago.");
        Check(_mesa.GetNode<Label>(Mesa.CenterPath + "Mazo/Content/CountLabel").Text == _shown.DeckCount + " cartas",
            "El mazo mantiene el contador recibido.");
    }

    private void ValidatePrivacy()
    {
        Check(NetworkProtocol.Encode(_shown) == _before, "Los gestos y el render conservan el snapshot.");
        int[] allowed = _shown.OwnHand.Concat(_shown.Melds.SelectMany(meld => meld.Cards))
            .Concat(_shown.RevealedHands.SelectMany(hand => hand.Cards))
            .Concat(_shown.DiscardTop is int discard ? new[] { discard } : Array.Empty<int>())
            .Concat(_shown.OfferedCard is int offer ? new[] { offer } : Array.Empty<int>()).ToArray();
        Check(Cards(_mesa).All(card => allowed.Contains(card.FaceCardId!.Value)),
            "Toda carta visible pertenece a la vista autorizada del jugador.");
        Check(_mesa.GetNode<Control>(Mesa.RivalGamesPath).GetChildren()
            .SelectMany(Descendants).OfType<Carta>().All(card => !card.Interactive),
            "Las cartas de rivales no admiten interacción.");
    }

    private async Task NativeReorder()
    {
        Carta[] cards = Cards(_mesa.GetNode(HandPath));
        int[] before = _mesa.LocalHandOrder;
        Carta source = cards[0], target = cards[^1];
        Vector2 from = source.GetGlobalTransformWithCanvas() * (source.Size / 2);
        Vector2 to = target.GetGlobalTransformWithCanvas() * new Vector2(target.Size.X - 3, target.Size.Y / 2);
        FeedMouse(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        FeedMouse(new InputEventMouseButton
        {
            Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left, Pressed = true
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        FeedMouse(new InputEventMouseMotion
        {
            Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_testViewport.GuiIsDragging(), "Godot inicia el arrastre nativo de la mano.");
        Check(CardDragData.TryRead(_testViewport.GuiGetDragData(), _shown, out LocalCardDrag data) &&
            data.CardId == before[0] && data.Origin == CardDragOrigin.Hand,
            "El arrastre nativo solo lleva una intención local vigente.");
        // Iniciar el arrastre actualiza selección y puede ordenar los controles.
        // La posición de llegada corresponde al destino actual, no al frame previo.
        to = target.GetGlobalTransformWithCanvas() * new Vector2(target.Size.X - 3, target.Size.Y / 2);
        FeedMouse(new InputEventMouseMotion
        { Position = to, GlobalPosition = to, ButtonMask = MouseButtonMask.Left });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string hover = _testViewport.GuiGetHoveredControl()?.GetPath().ToString() ?? "sin destino";
        Vector2 receivedPosition = target.GetLocalMousePosition(), receivedSize = target.Size;
        FeedMouse(new InputEventMouseButton
        { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false });
        await Settle();
        Check(!_testViewport.GuiIsDragging(), "Soltar termina el arrastre nativo.");
        Check(_mesa.LocalHandOrder.SequenceEqual(before.Skip(1).Append(before[0])),
            "El arrastre reorganiza la mano sin botones. Destino=" + hover +
            " Orden=" + string.Join(",", _mesa.LocalHandOrder) + " esperado=" +
            string.Join(",", before.Skip(1).Append(before[0])) + " Desde=" + from + " Hasta=" + to +
            " DropLocal=" + receivedPosition + " CardSize=" + receivedSize);
        Check(NetworkProtocol.Encode(_shown) == _before, "Reordenar no modifica el estado autoritativo.");
    }

    private async Task ValidateGestures()
    {
        SnapshotDto claims = _shown;
        CardDropZone newGroup = _mesa.GetNode<CardDropZone>(Mesa.InteractionPath + "DraftZone");
        Variant first = CardDragData.Create(claims, 2, CardDragOrigin.Hand);
        Check(newGroup._CanDropData(Vector2.Zero, first), "El destino admite iniciar un juego con una carta.");
        Gesture(() => newGroup._DropData(Vector2.Zero, first), "Preparar una carta conserva la mano autorizada.");
        Check(_mesa.LocalDraftCount == 1 && DraftIds(0).SequenceEqual(new[] { 2 }),
            "Un arrastre inicia un borrador incompleto sin menú ni botón de preparar.");
        DropOnDraft(0, 2, 3, CardDragOrigin.Hand);
        Check(DraftIds(0).SequenceEqual(new[] { 2, 3 }), "La segunda carta amplía el mismo borrador.");
        DropOnDraft(0, 3, 4, CardDragOrigin.Hand);
        Check(DraftIds(0).SequenceEqual(new[] { 2, 3, 4 }), "La tercera carta completa el borrador por arrastre.");
        DropOnDraft(0, 4, 5, CardDragOrigin.External);
        Check(DraftIds(0).SequenceEqual(new[] { 2, 3, 4, 5 }), "La oferta puede incorporarse mediante arrastre.");
        Check(!_mesa.LocalHandOrder.Contains(5) && _shown.OwnHand.Length == 6,
            "Preparar la oferta no introduce la carta pública en la mano autoritativa.");
        Variant draftDrag = CardDragData.Create(claims, 2, CardDragOrigin.Draft, 0);
        Carta reorderTarget = DraftCard(0, 4);
        Gesture(() => reorderTarget._DropData(new Vector2(reorderTarget.Size.X, 0), draftDrag),
            "Reordenar el borrador conserva el snapshot.");
        Check(DraftIds(0).SequenceEqual(new[] { 3, 4, 2, 5 }),
            "El borrador conserva el orden que declaró el jugador.");
        Carta handTarget = HandCard(18);
        Gesture(() => handTarget._DropData(Vector2.Zero,
                CardDragData.Create(claims, 2, CardDragOrigin.Draft, 0)),
            "Arrastrar de regreso a la mano deshace solo la colocación local.");
        Check(DraftIds(0).SequenceEqual(new[] { 3, 4, 5 }) && _shown.OwnHand.Contains(2),
            "Devolver una carta elimina su asignación local, conservándola en la mano real.");

        Show(claims with { Revision = 2, Window = 2 });
        await Settle();
        Check(_mesa.LocalDraftCount == 0, "Otra ventana de reclamos limpia las colocaciones anteriores.");
        var publishedRow = _mesa.GetNode<HFlowContainer>(Mesa.OwnGamesPath + "/Juego7");
        Check(publishedRow.MouseFilter == Control.MouseFilterEnum.Pass,
            "El juego propio recibe el destino de arrastre sin seleccionar un menú.");
        if (DisplayServer.GetName() != "headless")
        {
            Carta published = Cards(publishedRow).First(card => card.FaceCardId == 17);
            await NativeDrop(HandCard(18), published.GetGlobalTransformWithCanvas() * (published.Size / 2));
            Check(_mesa.LocalDraftCount == 1 && DraftIds(0).SequenceEqual(new[] { 15, 16, 17, 18 }),
                "El arrastre nativo identifica automáticamente el juego propio que se amplía.");
            Check(!_mesa.GetNode<Control>(Mesa.OwnGamesPath + "/Juego7").Visible && DraftIds(0).Contains(17) &&
                _shown.Melds.Single(m => m.Id == 7).Cards.SequenceEqual(new[] { 15, 16, 17 }),
                "La edición sustituye el dibujo del publicado conservando el juego autoritativo hasta confirmar.");
        }
        else newGroup._DropData(Vector2.Zero, CardDragData.Create(_shown, 2, CardDragOrigin.Hand));
        Variant oldToken = CardDragData.Create(_shown, 2, CardDragOrigin.Hand);
        Show(_shown with { Revision = 3 });
        Check(_mesa.LocalDraftCount == 1, "Una revisión ajena conserva una ampliación que aún es pertinente.");
        Check(!HandCard(3)._CanDropData(Vector2.Zero, oldToken), "Los tokens de una revisión anterior se rechazan.");

        SnapshotDto payment = CreateFixture(1);
        Show(payment);
        await Settle();
        CardDropZone slot = _mesa.GetNode<CardDropZone>(Mesa.CenterPath + "Pago");
        Button accept = _mesa.GetNode<Button>(Mesa.ContentPath + "ContextActions/ConfirmCardButton");
        Check(slot.IsVisibleInTree() && _mesa.StagedCardId is null && !accept.IsVisibleInTree(),
            "El pago comienza con casilla vacía y sin confirmación prematura.");
        Variant payDrag = CardDragData.Create(payment, 18, CardDragOrigin.Hand);
        Check(slot._CanDropData(Vector2.Zero, payDrag), "La casilla de pago admite preparar una carta propia offline.");
        Gesture(() => slot._DropData(Vector2.Zero, payDrag), "Soltar en Pago no envía ni aplica el descarte.");
        Check(_mesa.StagedCardId == 18 && accept.IsVisibleInTree() && accept.Disabled,
            "Aceptar aparece al preparar una carta y requiere conexión real para enviar.");
        Check(_mesa.GetNode<Carta>(Mesa.CenterPath + "Pago/Content/Carta").FaceCardId == 18 &&
            _shown.OwnHand.Contains(18) && HandCard(18).FaceCardId == 18 && !HandCard(18).Visible,
            "La carta preparada continúa en la mano real y se dibuja únicamente en la casilla.");
        if (_captureDirectory.Length > 0)
        {
            await Settle();
            await Capture("PagoPreparado-976x874.png");
        }
        Gesture(() => accept.EmitSignal(Button.SignalName.Pressed), "Una confirmación offline conserva el estado.");
        Check(_mesa.StagedCardId == 18, "Una señal offline no simula una confirmación del servidor.");
        Variant absent = CardDragData.Create(payment, 51, CardDragOrigin.Hand);
        Check(!slot._CanDropData(Vector2.Zero, absent), "El pago rechaza cartas ajenas.");
        Button cancel = _mesa.GetNode<Button>(Mesa.ContentPath + "ContextActions/CancelCardButton");
        Gesture(() => cancel.EmitSignal(Button.SignalName.Pressed), "Cancelar el pago solo libera su intención local.");
        Check(_mesa.StagedCardId is null && !accept.IsVisibleInTree() && HandCard(18).Visible,
            "Cancelar oculta la confirmación y devuelve la carta a su mano.");
        slot._DropData(Vector2.Zero, payDrag);
        Show(payment with { Revision = 2, Window = 3 });
        Check(_mesa.StagedCardId is null, "Cambiar la ventana descarta un pago que todavía no se envió.");

        SnapshotDto exchange = CreateFixture(2);
        Show(exchange);
        await Settle();
        slot = _mesa.GetNode<CardDropZone>(Mesa.CenterPath + "Pago");
        Variant exchangeDrag = CardDragData.Create(exchange, 0, CardDragOrigin.Hand);
        Check(slot.IsVisibleInTree() && slot._CanDropData(Vector2.Zero, exchangeDrag),
            "El intercambio inicial también permite preparar una carta mediante la casilla contextual.");
        Gesture(() => slot._DropData(Vector2.Zero, exchangeDrag), "El intercambio no entrega la carta antes de aceptar.");
        Check(_mesa.StagedCardId == 0 && exchange.OwnHand.Length == 9 && accept.IsVisibleInTree() && !HandCard(0).Visible,
            "Preparar el intercambio conserva nueve cartas reales y muestra la preparada solo en la casilla.");
        if (_captureDirectory.Length > 0)
        {
            await Settle();
            await Capture("IntercambioPreparado-976x874.png");
        }
        Show(exchange with { Revision = 2, OwnResponseSubmitted = true });
        Check(_mesa.StagedCardId is null, "Una respuesta ya registrada limpia la carta preparada.");
        Show(exchange with { Revision = 3, Players = new[]
        {
            exchange.Players[0] with { Eliminated = true }, exchange.Players[1]
        }});
        Check(!slot._CanDropData(Vector2.Zero, exchangeDrag) && Cards(_mesa.GetNode(HandPath)).All(card => !card.Interactive),
            "La eliminación bloquea las nuevas intenciones de arrastre.");
        _mesa.DisplaySnapshot(null, "Sesión cerrada");
        Check(_mesa.StagedCardId is null && _mesa.LocalDraftCount == 0 && _mesa.LocalHandOrder.Length == 0,
            "Cerrar la sesión elimina todas las intenciones y las cartas privadas locales.");
        Show(CreateFixture(0));
        await Settle();

        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("MESA DYNAMIC NATIVE OMITIDO: backend headless sin ratón; -Capture comprueba el arrastre real.");
            return;
        }
        Control ownRows = _mesa.GetNode<Control>(Mesa.OwnGamesPath);
        Carta source = HandCard(2);
        Vector2 target = ownRows.GetGlobalTransformWithCanvas() * new Vector2(12, 40);
        await NativeDrop(source, target);
        Check(_mesa.LocalDraftCount == 1 && DraftIds(0).SequenceEqual(new[] { 2 }),
            "Soltar una carta en el espacio de Mi juego inicia una combinación con input nativo.");
        Check(NetworkProtocol.Encode(_shown) == _before, "El nuevo juego por input nativo conserva la vista autoritativa.");
    }

    private async Task NativeDrop(Carta source, Vector2 to)
    {
        Vector2 from = source.GetGlobalTransformWithCanvas() * (source.Size / 2);
        FeedMouse(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        FeedMouse(new InputEventMouseButton
        { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        FeedMouse(new InputEventMouseMotion
        { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_testViewport.GuiIsDragging(), "El gesto inicia un arrastre nativo hacia Mi juego.");
        FeedMouse(new InputEventMouseMotion
        { Position = to, GlobalPosition = to, ButtonMask = MouseButtonMask.Left });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        FeedMouse(new InputEventMouseButton
        { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false });
        await Settle();
        Check(!_testViewport.GuiIsDragging(), "Soltar sobre Mi juego termina el arrastre nativo.");
    }

    private void FeedMouse(InputEventMouse input)
    {
        _testViewport.PushInput(input, true);
    }

    private Carta HandCard(int id) => _mesa.GetNode<Carta>(HandPath + "/Carta" + id);
    private Carta DraftCard(int group, int id) => _mesa.GetNode<Carta>(DraftPath + "/Borrador" + group + "/Cards/Carta" + id);
    private int[] DraftIds(int group) => _mesa.GetNode<HFlowContainer>(DraftPath + "/Borrador" + group + "/Cards")
        .GetChildren().OfType<Carta>().Select(card => card.FaceCardId!.Value).ToArray();
    private void DropOnDraft(int group, int targetId, int sourceId, CardDragOrigin origin)
    {
        Carta target = DraftCard(group, targetId);
        Variant token = CardDragData.Create(_shown, sourceId, origin);
        Vector2 position = new(target.Size.X, 0);
        Check(target._CanDropData(position, token), "El borrador admite la carta " + sourceId + ".");
        Gesture(() => target._DropData(position, token), "Añadir a un borrador conserva el estado recibido.");
    }
    private void Gesture(Action action, string message)
    {
        string before = NetworkProtocol.Encode(_shown);
        action();
        Check(NetworkProtocol.Encode(_shown) == before, message);
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = _testViewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) throw new InvalidOperationException("Captura requiere render real; usar -Capture.");
        Directory.CreateDirectory(_captureDirectory);
        string path = Path.Combine(_captureDirectory, name);
        Error result = image.SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException("No se pudo guardar la captura: " + result);
        GD.Print("MESA DYNAMIC CAPTURE: " + path);
    }

    private static SnapshotDto CreateFixture(int index)
    {
        if (index is 2 or 3)
            return new SnapshotDto(NetworkProtocol.Version, new string('d', 32), 1, index + 1, 1,
                index == 2 ? GamePhase.InitialExchange : GamePhase.InitialDeclarations, MatchResult.None,
                null, null, null, new[]
                {
                    new PublicPlayerDto(1, "Ricardo · Nombre propio", 0, 9, true, false),
                    new PublicPlayerDto(2, "Pedro", 1, 9, true, false)
                }, Enumerable.Range(0, 9).ToArray(), Array.Empty<MeldDto>(), 34, 0, null, null,
                Array.Empty<RevealedHandDto>(), false);
        bool payment = index == 1;
        int[] own = payment ? new[] { 18, 32, 48 } : new[] { 2, 3, 4, 18, 32, 48 };
        var melds = new List<MeldDto>
        {
            new(7, 1, new[] { 15, 16, 17 }), new(9, 2, new[] { 26, 27, 28 })
        };
        if (payment) melds.Add(new MeldDto(10, 1, new[] { 2, 3, 4, 5 }));
        return new SnapshotDto(NetworkProtocol.Version, new string('d', 32), 1, index + 1, 1,
            payment ? GamePhase.AwaitingPayment : index == 0 ? GamePhase.AwaitingClaims : GamePhase.AwaitingDraw,
            MatchResult.None, payment || index == 4 ? 1 : 2, payment ? 1 : null, null,
            new[]
            {
                new PublicPlayerDto(1, "Ricardo · Nombre propio", 0, own.Length, true, false),
                new PublicPlayerDto(2, "Pedro", 1, 6, true, false)
            }, own, melds.ToArray(), 33, index == 4 ? 1 : 0, index == 4 ? 21 : null,
            index == 0 ? 5 : null, Array.Empty<RevealedHandDto>(), false);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }
    private static Carta[] Cards(Node root) => Descendants(root).OfType<Carta>()
        .Where(card => card.IsVisibleInTree() && card.FaceCardId is not null).ToArray();
    private static bool Overlaps(Rect2 a, Rect2 b) => a.Position.X < b.End.X - 1 && b.Position.X < a.End.X - 1 &&
        a.Position.Y < b.End.Y - 1 && b.Position.Y < a.End.Y - 1;
    private void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        GD.Print((success ? "MESA DYNAMIC PASS: " : "MESA DYNAMIC FAIL: ") + message + " Comprobaciones=" + _checks);
        if (_auto) GetTree().Quit(success ? 0 : 1);
        else if (!success) GD.PushError(message);
    }
}
