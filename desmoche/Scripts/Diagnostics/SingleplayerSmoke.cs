using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Desmoche.Presentation.Interaction;
using Desmoche.Singleplayer;
using Desmoche.Singleplayer.Core;
using Godot;

namespace Desmoche.Diagnostics;

// Main y sesión reales. Solo la prueba puede acelerar el intervalo de los bots.
// No instala snapshots ficticios ni lee las manos privadas del controlador.
public partial class SingleplayerSmoke : Control
{
    private const string SetupPath = "Margin/Screens/Singleplayer/Scroll/Center/Panel/Padding/Content/";
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";
    private Main _main = null!;
    private NetworkManager _network = null!;
    private LocalGameSession _local = null!;
    private Mesa _mesa = null!;
    private SubViewport _viewport = null!;
    private string _captureDirectory = "";
    private ulong _began;
    private int _checks;
    private bool _done, _native;

    public override void _Ready()
    {
        _began = Time.GetTicksMsec();
        _native = DisplayServer.GetName() != "headless";
        _captureDirectory = OS.GetCmdlineUserArgs()
            .FirstOrDefault(value => value.StartsWith("--capture-dir=", StringComparison.Ordinal))?[14..] ?? "";
        _viewport = new SubViewport
        {
            Name = "TestViewport", Size = new Vector2I(1600, 900),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = true
        };
        AddChild(_viewport);
        var rendered = new TextureRect
        {
            Name = "RenderedTest", Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(rendered);
        rendered.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _main = GD.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
        _viewport.AddChild(_main);
        _network = _main.GetNode<NetworkManager>("NetworkManager");
        _local = _main.GetNode<LocalGameSession>("LocalGameSession");
        GD.Print(_native ? "SINGLEPLAYER INPUT: botones con GUI nativa en SubViewport; opciones por señal publica."
            : "SINGLEPLAYER INPUT: señales publicas de controles; sin render.");
        Run();
    }

    public override void _Process(double delta)
    {
        if (!_done && Time.GetTicksMsec() - _began > 90000)
            Finish(false, "Tiempo agotado durante el recorrido de Singleplayer.");
    }

    private async void Run()
    {
        try
        {
            await Until(() => _main.ScreensReady, "La carga termina.", 15000);
            _mesa = _main.GetNode<Mesa>("Margin/Screens/Mesa");
            Check(_main.CurrentScreen == "Inicio", "La aplicación comienza en Inicio.");
            CheckOffline("Inicio no abre ENet.");
            await Press(MenuButton("Jugar contra bots"));
            CheckScreen("Singleplayer");
            Check(!_local.IsActive && _local.View is null, "Abrir el formulario no reparte cartas.");
            MenuChoiceSelector counts = Setup<MenuChoiceSelector>("BotCountInput");
            MenuChoiceSelector levels = Setup<MenuChoiceSelector>("DifficultyInput");
            Check(counts.ItemCount == 3, "Se ofrecen exactamente 1, 2 o 3 bots.");
            Check(levels.ItemCount == 4, "Se ofrecen cuatro dificultades.");
            LineEdit name = Setup<LineEdit>("NameInput");
            name.Text = "";
            name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
            CheckScreen("Singleplayer");
            Check(!_local.IsActive && Setup<Button>("StartButton").Disabled,
                "El nombre vacío deshabilita el inicio y no crea una partida.");
            name.Text = "Ricardo";
            name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
            await Capture("Singleplayer-1600x900.png");

            // Inspecciona el reparto antes de que una elección reduzca la mano a ocho.
            _local.SetProcess(false);
            for (int level = 0; level < 4; level++)
            for (int count = 0; count < 3; count++)
            {
                await Select(levels, level); await Select(counts, count);
                await Press(Setup<Button>("StartButton"));
                CheckScreen("Mesa");
                SnapshotDto view = View();
                Check(_local.IsActive && _local.IsLocal, "La sesión es local y está activa.");
                Check(view.Phase == GamePhase.InitialExchange && view.OwnHand.Length == 9,
                    "El inicio reparte nueve cartas y espera el intercambio humano.");
                Check(view.Players.Length == count + 2 && view.Players.All(player => player.CardCount == 9),
                    "Cantidad correcta de jugadores para la opción de bots " + (count + 1) + ".");
                Check(view.DeckCount == 52 - view.Players.Length * 9 && view.DiscardCount == 0,
                    "El reparto conserva el mazo y comienza con el pozo vacío.");
                Check(view.RevealedHands.Length == 0 && view.Melds.Length == 0,
                    "No se revelan las cartas privadas de los bots.");
                Check(_mesa.RenderedPlayerCount == count + 2, "La misma Mesa representa a todos los jugadores.");
                Check(_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/LeaveButton").Text.Contains("partida", StringComparison.OrdinalIgnoreCase),
                    "La salida de Singleplayer usa lenguaje de partida local.");
                ValidateCards(view);
                CheckOffline("Una partida local no crea peer ENet ni réplica de red.");
                string priorSession = view.Session;
                await Press(_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/LeaveButton"));
                CheckScreen("Singleplayer");
                Check(!_local.IsActive && _local.View is null, "Salir destruye la sesión local y su vista privada.");
                Check(_mesa.LocalHandOrder.Length == 0 && _mesa.LocalDraftCount == 0,
                    "Salir borra las cartas y las intenciones locales de Mesa.");
                await Settle();
                Check(!_local.IsActive, "Una decisión pendiente de un bot no reactiva la sesión cerrada.");
                GD.Print("SINGLEPLAYER CASE: dificultad=" + levels.GetItemText(level) + "; bots=" + (count + 1) + "; sesión=" + priorSession);
            }

            // Vuelve a una sesión real para verificar el puente GUI -> comando -> estado.
            await Select(levels, 1); await Select(counts, 2);
            _local.BotDelayMilliseconds = 150;
            await Press(Setup<Button>("StartButton"));
            // Semilla solo en el diagnóstico para repetir el recorrido tras el reparto.
            _local.Close();
            _local.Start(new LocalMatchOptions("Ricardo", 3, BotDifficulty.Medium), 37);
            _local.SetProcess(true);
            await Settle();
            SnapshotDto exchange = View();
            CardDropZone slot = _mesa.GetNode<CardDropZone>(Mesa.CenterPath + "Pago");
            int cardId = exchange.OwnHand[0];
            Variant token = CardDragData.Create(exchange, cardId, CardDragOrigin.Hand);
            Check(slot._CanDropData(Vector2.Zero, token), "La casilla inicial admite una carta propia.");
            slot._DropData(Vector2.Zero, token);
            Check(_mesa.StagedCardId == cardId && !View().OwnResponseSubmitted,
                "Soltar prepara la carta sin efectuar el intercambio.");
            await ToSignal(GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
            Check(_mesa.StagedCardId == cardId,
                "Las confirmaciones de bots no borran la carta preparada por el humano.");
            await Capture("Singleplayer-Mesa-4jugadores.png");
            // Congela únicamente este diagnóstico cuando llega la vista inicial,
            // para inspeccionar sus controles antes de que un bot pueda ganar.
            void HoldInitial(SnapshotDto snapshot)
            {
                if (snapshot.Phase == GamePhase.InitialDeclarations) _local.SetProcess(false);
            }
            _local.ViewChanged += HoldInitial;
            await Press(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "ConfirmCardButton"));
            await Until(() => View().Phase != GamePhase.InitialExchange,
                "El intercambio termina cuando responden humano y bots.");
            _local.ViewChanged -= HoldInitial;
            Check(View().OwnHand.Length == 9, "Después del intercambio siguen siendo nueve cartas propias.");
            Check(View().Phase == GamePhase.InitialDeclarations,
                "El diagnóstico inspecciona declaraciones antes de que los bots continúen.");
            if (View().Phase == GamePhase.InitialDeclarations)
            {
                Check(!_mesa.HasNode(Mesa.ContextActionsPath + "InitialVictoryType"),
                    "No hay selector de victorias: los botones dependen de la mano.");
                Check(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "ColorButton").IsVisibleInTree() ==
                    RulesValidator.IsColor(View().OwnHand.Select(Card.FromId).ToArray()),
                    "Color aparece únicamente con nueve cartas del mismo color.");
                Check(!View().OwnResponseSubmitted, "Mostrar declaraciones no las envía automáticamente.");
                await Capture("Singleplayer-DeclaracionInicial.png");
                Button next = _mesa.GetNode<Button>(Mesa.ContextActionsPath + "ContinueButton");
                if (next.IsVisibleInTree() && !next.Disabled) await Press(next);
                _local.SetProcess(true);
                await Until(() => View().Phase != GamePhase.InitialDeclarations,
                    "Las decisiones iniciales de los bots permiten continuar.");
            }
            if (View().Phase == GamePhase.AwaitingDraw && View().CurrentPlayerId == View().RecipientId)
            {
                await Press(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "DrawDeckButton"));
                await Until(() => View().Phase != GamePhase.AwaitingDraw, "Mostrar del mazo pasa por el motor local.");
                if (View().Phase == GamePhase.AwaitingClaims && !View().OwnResponseSubmitted)
                    await Press(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "PassButton"));
            }
            Check(_local.IsActive, "La partida sigue activa durante las respuestas de los bots.");
            ValidateCards(View());
            CheckOffline("Las acciones de partida conservan la red desconectada.");
            await RunUntilFinished();
            Check(View().Result != MatchResult.None, "La partida completa tiene un resultado definitivo.");
            await Capture("Singleplayer-Resultado.png");
            await Press(_mesa.GetNode<Button>(Mesa.ContentPath + "TopBar/RematchButton"));
            await Until(() => View().Phase == GamePhase.InitialExchange, "Otra partida comienza un reparto nuevo.");
            Check(View().OwnHand.Length == 9 && View().RevealedHands.Length == 0 &&
                View().Players.Length == 4 && View().Melds.Length == 0,
                "La revancha conserva los jugadores y limpia cartas publicadas y reveladas.");
            bool receivedAfterClose = false;
            void OnClosedReply(ReplyDto reply) => receivedAfterClose = true;
            _local.ActionCompleted += OnClosedReply;
            long queued = _local.Submit(ActionKind.ExchangeCard, View().OwnHand[0]);
            Check(queued > 0 && !receivedAfterClose, "Submit devuelve el identificador antes de responder.");
            _local.Close("Cierre de diagnóstico con una acción pendiente.");
            await Settle();
            _local.ActionCompleted -= OnClosedReply;
            Check(!receivedAfterClose && !_local.IsActive && _local.View is null,
                "Cerrar descarta el comando pendiente sin emitir una respuesta antigua.");
            CheckScreen("Singleplayer");

            foreach (Vector2I size in new[] { new Vector2I(1100, 800), new Vector2I(640, 480), new Vector2I(976, 874) })
            {
                _viewport.Size = size;
                await Settle();
                foreach (Control control in new Control[] { name, counts, levels, Setup<Button>("StartButton"), Setup<Button>("BackButton") })
                    Check(control.Size.X > 0 && control.GetGlobalRect().Position.X >= 0 &&
                        control.GetGlobalRect().End.X <= size.X + 1,
                        "El formulario conserva el ancho de " + control.Name + " en " + size + ".");
                await Capture("Singleplayer-" + size.X + "x" + size.Y + ".png");
            }
            await Press(Setup<Button>("BackButton"));
            CheckScreen("Inicio");
            await Press(MenuButton("Multijugador"));
            CheckScreen("Lobby");
            Check(!_local.IsActive && _local.View is null, "Multijugador no hereda una partida local.");
            CheckOffline("Volver a Multijugador muestra opciones sin conexión anticipada.");
            _main.OpenSingleplayer();
            await Settle();
            await Press(Setup<Button>("StartButton"));
            Check(_local.IsActive, "La comprobación de cierre comienza con una partida activa.");
            _main.QueueFree();
            await Settle();
            Check(!GodotObject.IsInstanceValid(_main) && !GodotObject.IsInstanceValid(_local),
                "Cerrar Main libera la sesión activa y sus decisiones pendientes.");
            Finish(true, "Doce configuraciones, privacidad, partida completa, revancha, cola, navegación y cierre activo verificados.");
        }
        catch (Exception error) { Finish(false, error.Message); }
    }

    // El conductor de prueba representa a un humano pasivo mediante acciones
    // públicas. Los rivales siguen siendo los bots reales de la sesión.
    private async Task RunUntilFinished()
    {
        _local.BotDelayMilliseconds = 70;
        var adviser = new BotBrain(BotDifficulty.Expert, 42017);
        ulong began = Time.GetTicksMsec();
        int actions = 0;
        while (View().Phase != GamePhase.Finished)
        {
            if (Time.GetTicksMsec() - began > 60000)
                throw new InvalidOperationException("La simulación visual no finalizó en 60 segundos.");
            SnapshotDto view = View();
            PublicPlayerDto self = view.Players.Single(player => player.Id == view.RecipientId);
            BotAction? action = self.Eliminated ? null : view.Phase switch
            {
                GamePhase.InitialExchange when !view.OwnResponseSubmitted =>
                    new BotAction(ActionKind.ExchangeCard, view.OwnHand[0]),
                GamePhase.InitialDeclarations when !view.OwnResponseSubmitted =>
                    new BotAction(ActionKind.FinishInitial),
                GamePhase.AwaitingClaims when !view.OwnResponseSubmitted => new BotAction(ActionKind.Pass),
                GamePhase.AwaitingDraw when view.CurrentPlayerId == view.RecipientId =>
                    view.DeckCount > 0 ? new BotAction(ActionKind.DrawDeck) : adviser.Decide(view),
                GamePhase.AwaitingPayment when view.PaymentPlayerId == view.RecipientId =>
                    view.OwnHand.Length > 0 ? new BotAction(ActionKind.Pay, view.OwnHand[0]) :
                        new BotAction(ActionKind.CompleteWithoutPayment),
                _ => null
            };
            if (action is not null)
            {
                ReplyDto? reply = null;
                void OnReply(ReplyDto value) => reply = value;
                _local.ActionCompleted += OnReply;
                try
                {
                    long id = _local.Submit(action.Kind, action.CardId, action.Plays, action.DeclareWin,
                        action.InitialKind, action.Cards, action.SourceMeldId);
                    Check(id > 0 && reply is null, "La acción pública se encola antes de recibir respuesta.");
                    while (reply is null) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check(reply.RequestId == id && reply.Success && !reply.Penalized,
                        "El motor acepta la acción de simulación " + action.Kind + ": " + reply.Message);
                    actions++;
                }
                finally { _local.ActionCompleted -= OnReply; }
            }
            await ToSignal(GetTree().CreateTimer(0.04), Timer.SignalName.Timeout);
        }
        await Settle();
        CheckOffline("La partida completa termina sin ENet.");
        GD.Print("SINGLEPLAYER FULL MATCH: acciones humanas=" + actions + "; resultado=" + View().Result);
    }

    private SnapshotDto View() => _local.View ?? throw new InvalidOperationException("Falta la vista de la sesión local.");
    private T Setup<T>(string name) where T : Node => _main.GetNode<T>(SetupPath + name);
    private Button MenuButton(string text) => Descendants(_main.GetNode("Margin/Screens/MenuPrincipal"))
        .OfType<Button>().Single(button => button.Text == text);
    private async Task Select(MenuChoiceSelector button, int index)
    {
        await Press(button.GetChoice(index));
        Check(button.Selected == index, "La opción visible selecciona su ID real.");
        for (int i = 0; i < button.ItemCount; i++)
            Check(button.GetChoice(i).ButtonPressed == (i == index), "La selección permanece exclusiva.");
    }

    private static void Select(OptionButton button, int index)
    {
        button.Select(index);
        button.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    private void ValidateCards(SnapshotDto view)
    {
        var allowed = view.OwnHand.Concat(view.Melds.SelectMany(meld => meld.Cards))
            .Concat(view.DiscardTop is int discard ? new[] { discard } : Array.Empty<int>())
            .Concat(view.OfferedCard is int offered ? new[] { offered } : Array.Empty<int>())
            .Concat(view.RevealedHands.SelectMany(hand => hand.Cards)).ToHashSet();
        Check(Descendants(_mesa).OfType<Carta>().Where(card => card.FaceCardId.HasValue)
            .All(card => allowed.Contains(card.FaceCardId!.Value)),
            "Mesa contiene solamente cartas permitidas por la vista humana.");
        Check(_mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta").FaceCardId is null,
            "El mazo no expone la identidad de la siguiente carta.");
        Check(_mesa.GetNode<HFlowContainer>(HandPath).GetChildCount() == view.OwnHand.Length,
            "La mano visual pertenece al humano.");
    }

    private async Task Until(Func<bool> condition, string description, int milliseconds = 10000)
    {
        ulong started = Time.GetTicksMsec();
        while (!condition() && !_done)
        {
            if (Time.GetTicksMsec() - started > (ulong)milliseconds)
                throw new InvalidOperationException(description + " Tiempo agotado.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (_done) throw new InvalidOperationException("El diagnóstico ya terminó.");
        await Settle();
        Check(condition(), description);
    }

    private async Task Press(Button button)
    {
        Check(button.IsVisibleInTree() && !button.Disabled, "Control disponible: " + button.Name + ".");
        if (!_native) button.EmitSignal(BaseButton.SignalName.Pressed);
        else
        {
            for (Node? ancestor = button.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
                if (ancestor is ScrollContainer scroll) scroll.EnsureControlVisible(button);
            await Settle();
            Vector2 position = button.GetGlobalTransformWithCanvas() * (button.Size / 2);
            Check(position.X >= 0 && position.Y >= 0 && position.X < _viewport.Size.X && position.Y < _viewport.Size.Y,
                "Centro accesible para input: " + button.Name + ".");
            _viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _viewport.PushInput(new InputEventMouseButton
            { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _viewport.PushInput(new InputEventMouseButton
            { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        }
        await Settle();
    }

    private async Task Settle()
    {
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CheckScreen(string screen)
    {
        Check(_main.CurrentScreen == screen, "Pantalla actual " + screen + ".");
        foreach (var pair in new[] { ("MenuPrincipal", "Inicio"), ("Lobby", "Lobby"), ("Singleplayer", "Singleplayer"), ("Mesa", "Mesa") })
            Check(_main.GetNode<Control>("Margin/Screens/" + pair.Item1).Visible == (screen == pair.Item2),
                "Visibilidad correcta de " + pair.Item1 + ".");
    }

    private void CheckOffline(string message) => Check(_network.State == ConnectionState.Offline &&
        !_network.IsHost && _network.View is null &&
        _network.Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer &&
        _network.GetNode<Node>("ReplicatedState").GetChildCount() == 0, message);

    private async Task Capture(string name)
    {
        if (!_native || _captureDirectory.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) throw new InvalidOperationException("La captura necesita render real.");
        Directory.CreateDirectory(_captureDirectory);
        string path = Path.Combine(_captureDirectory, name);
        Error result = image.SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException("No se pudo guardar " + path + ": " + result);
        GD.Print("SINGLEPLAYER CAPTURE: " + path);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        GD.Print((success ? "SINGLEPLAYER PASS: " : "SINGLEPLAYER FAIL: ") + message + " Comprobaciones=" + _checks);
        GetTree().Quit(success ? 0 : 1);
    }
}
