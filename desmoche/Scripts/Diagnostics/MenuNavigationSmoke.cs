using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Networking;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Carga Main real, incluida su inicializacion asincrona. No reemplaza estados,
// snapshots ni callbacks privados. El render usa una GUI aislada del cursor del SO.
public partial class MenuNavigationSmoke : Control
{
    private const string LobbyContent = "Margin/Screens/Lobby/Scroll/Center/Panel/Padding/Content/";
    private Main _main = null!;
    private NetworkManager _network = null!;
    private SubViewport _viewport = null!;
    private string _captureDirectory = "";
    private ulong _began;
    private int _checks, _initializedCount;
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
        _main.ScreensInitialized += OnScreensInitialized;
        _viewport.AddChild(_main);
        _network = _main.GetNode<NetworkManager>("NetworkManager");
        GD.Print(_native ? "MENU INPUT: GUI nativa en SubViewport; sin cursor del SO."
            : "MENU INPUT: señales publicas de botones; la GUI nativa se verifica con render normal.");
        Run();
    }

    public override void _Process(double delta)
    {
        if (!_done && Time.GetTicksMsec() - _began > 45000)
            Finish(false, "Tiempo agotado durante carga o navegacion.");
    }

    private void OnScreensInitialized() => _initializedCount++;

    private async void Run()
    {
        try
        {
            Check(!_main.ScreensReady && _main.CurrentScreen == "Carga", "Main comienza en Carga antes de estar listo.");
            CheckOffline("La pantalla de carga no crea una sesion.");
            if (_native && _captureDirectory.Length > 0) await Capture("Carga-1600x900.png");
            while (!_main.ScreensReady && !_done)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_done) return;
            await Settle();
            Check(_initializedCount == 1, "Las pantallas se inicializan exactamente una vez.");
            Check(_main.CurrentScreen == "Inicio", "La carga termina en Inicio.");
            CheckOffline("Inicio conserva la red desconectada.");
            Control menu = _main.GetNode<Control>("Margin/Screens/MenuPrincipal");
            Check(menu.IsVisibleInTree(), "MenuPrincipal visible despues de la carga.");
            Check(Descendants(menu).OfType<Label>().Any(label => label.Text == "DESMOCHE"), "Titulo DESMOCHE.");
            Check(Descendants(menu).OfType<Button>().Any(button => button.Text == "Cómo jugar"), "Acceso a reglas disponible.");
            Button multiplayer = MenuButton("Multijugador");
            Button singleplayer = Descendants(menu).OfType<Button>().Single(button => button.Name == "SingleplayerButton");
            Button exit = MenuButton("Salir");
            Check(!multiplayer.Disabled && !exit.Disabled, "Multijugador y Salir disponibles.");
            Check(!singleplayer.Disabled, "Singleplayer esta disponible.");
            await Press(MenuButton("Cómo jugar"));
            Check(menu.GetNode<Control>("HelpOverlay").Visible, "Las reglas se abren desde el botón real.");
            await CaptureIfAvailable("ComoJugar-1600x900.png");
            await Press(menu.GetNode<Button>("HelpOverlay/Scroll/Center/Panel/Padding/Content/CloseButton"));
            Check(!menu.GetNode<Control>("HelpOverlay").Visible, "Las reglas devuelven el foco al menú.");
            await Press(menu.GetNode<Button>("SettingsButton"));
            Check(menu.GetNode<Control>("SettingsOverlay").Visible, "El engranaje abre los ajustes.");
            await Press(menu.GetNode<Button>("SettingsOverlay/Scroll/Center/Panel/Padding/Content/CloseButton"));
            await Press(singleplayer);
            CheckScreen("Singleplayer");
            CheckOffline("Abrir Singleplayer conserva ENet desconectado.");
            await Press(Descendants(_main.GetNode<Control>("Margin/Screens/Singleplayer"))
                .OfType<Button>().Single(button => button.Name == "BackButton"));
            CheckScreen("Inicio");
            await CaptureIfAvailable("Inicio-1600x900.png");
            await Press(multiplayer);
            CheckScreen("Lobby");
            CheckPanel("OptionsPanel", "CreatePanel", "JoinPanel", "SessionPanel");
            CheckOffline("Abrir Multijugador no crea una sesion automaticamente.");
            await CaptureIfAvailable("Multijugador-1600x900.png");

            await PressLobby("OptionsPanel/CreateOption");
            CheckPanel("CreatePanel", "OptionsPanel", "JoinPanel", "SessionPanel");
            await CaptureIfAvailable("CrearPartida-1600x900.png");
            _main.GetNode<LineEdit>(LobbyContent + "CreatePanel/NameInput").Text = "";
            await PressLobby("CreatePanel/Buttons/CreateButton");
            CheckInvalidForm("El nombre vacio impide crear una sala.");
            await CaptureIfAvailable("CrearPartida-Error-1600x900.png");
            await PressLobby("CreatePanel/Buttons/CancelButton");
            CheckPanel("OptionsPanel", "CreatePanel", "JoinPanel", "SessionPanel");
            CheckOffline("Cancelar crear no abre ni deja una sesion.");

            await PressLobby("OptionsPanel/JoinOption");
            CheckPanel("JoinPanel", "OptionsPanel", "CreatePanel", "SessionPanel");
            await CaptureIfAvailable("UnirsePartida-1600x900.png");
            LineEdit name = _main.GetNode<LineEdit>(LobbyContent + "JoinPanel/NameInput");
            LineEdit address = _main.GetNode<LineEdit>(LobbyContent + "JoinPanel/IPInput");
            name.Text = "";
            address.Text = "127.0.0.1";
            await PressLobby("JoinPanel/Buttons/JoinButton");
            CheckInvalidForm("El nombre vacio impide unirse.");
            name.Text = "Pedro";
            foreach (string invalid in new[] { "", "IP incorrecta", "0.0.0.0", "255.255.255.255", "224.0.0.1" })
            {
                address.Text = invalid;
                await PressLobby("JoinPanel/Buttons/JoinButton");
                CheckInvalidForm("La direccion '" + invalid + "' se rechaza antes de abrir ENet.");
            }
            await CaptureIfAvailable("UnirsePartida-Error-1600x900.png");
            await PressLobby("JoinPanel/Buttons/CancelButton");
            CheckPanel("OptionsPanel", "CreatePanel", "JoinPanel", "SessionPanel");
            await PressLobby("OptionsPanel/BackButton");
            CheckScreen("Inicio");
            CheckOffline("Volver a Inicio conserva la red desconectada.");

            foreach (Vector2I size in new[] { new Vector2I(1100, 800), new Vector2I(640, 480), new Vector2I(976, 874), new Vector2I(1080, 1920) })
            {
                _viewport.Size = size;
                await Settle();
                Check(_main.CurrentScreen == "Inicio", "Redimensionar no cambia de pantalla.");
                foreach (Button button in new[] { MenuButton("Multijugador"), singleplayer, MenuButton("Salir") })
                {
                    Rect2 rect = button.GetGlobalRect();
                    Check(rect.Position.X >= -1 && rect.Position.Y >= -1 &&
                        rect.End.X <= size.X + 1 && rect.End.Y <= size.Y + 1,
                        "Boton " + button.Text + " dentro del viewport " + size + ".");
                }
                await CaptureIfAvailable("Inicio-" + size.X + "x" + size.Y + ".png");
                await Press(MenuButton("Multijugador"));
                CheckPanel("OptionsPanel", "CreatePanel", "JoinPanel", "SessionPanel");
                await CaptureIfAvailable("Multijugador-" + size.X + "x" + size.Y + ".png");
                await PressLobby("OptionsPanel/CreateOption");
                CheckPanel("CreatePanel", "OptionsPanel", "JoinPanel", "SessionPanel");
                await CaptureIfAvailable("CrearPartida-" + size.X + "x" + size.Y + ".png");
                await PressLobby("CreatePanel/Buttons/CancelButton");
                await PressLobby("OptionsPanel/JoinOption");
                CheckPanel("JoinPanel", "OptionsPanel", "CreatePanel", "SessionPanel");
                Button join = _main.GetNode<Button>(LobbyContent + "JoinPanel/Buttons/JoinButton");
                Button cancel = _main.GetNode<Button>(LobbyContent + "JoinPanel/Buttons/CancelButton");
                Check(join.Size.X > 0 && cancel.Size.X > 0 && address.Size.X > 0,
                    "Formulario de conexion conserva controles con espacio al redimensionar.");
                await CaptureIfAvailable("UnirsePartida-" + size.X + "x" + size.Y + ".png");
                await Press(cancel);
                await PressLobby("OptionsPanel/BackButton");
                CheckScreen("Inicio");
            }
            Check(_initializedCount == 1, "La navegacion no recrea las pantallas ni reinicia la carga.");
            CheckOffline("Toda la navegacion mantiene aislado el estado de partida.");
            Finish(true, "Carga real, menu, formularios, validacion y navegacion verificados.");
        }
        catch (Exception error) { Finish(false, error.Message); }
    }

    private Button MenuButton(string text) => Descendants(_main.GetNode<Control>("Margin/Screens/MenuPrincipal"))
        .OfType<Button>().Single(button => button.Text == text);

    private async Task PressLobby(string path) => await Press(_main.GetNode<Button>(LobbyContent + path));

    private async Task Press(Button button, bool allowDisabled = false)
    {
        Check(button.IsVisibleInTree() && (allowDisabled || !button.Disabled), "Control disponible: " + button.Name + ".");
        if (!_native)
            button.EmitSignal(BaseButton.SignalName.Pressed);
        else
        {
            Node? ancestor = button.GetParent();
            while (ancestor is not null)
            {
                if (ancestor is ScrollContainer scroll) scroll.EnsureControlVisible(button);
                ancestor = ancestor.GetParent();
            }
            await Settle();
            Vector2 position = button.GetGlobalTransformWithCanvas() * (button.Size / 2);
            Check(position.X >= 0 && position.Y >= 0 && position.X < _viewport.Size.X && position.Y < _viewport.Size.Y,
                "Centro del boton accesible para input: " + button.Name + ".");
            _viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _viewport.PushInput(new InputEventMouseButton
            {
                Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _viewport.PushInput(new InputEventMouseButton
            {
                Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false
            }, true);
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
        Check(_main.GetNode<Control>("Margin/Screens/MenuPrincipal").Visible == (screen == "Inicio") &&
            _main.GetNode<Control>("Margin/Screens/Lobby").Visible == (screen == "Lobby") &&
            _main.GetNode<Control>("Margin/Screens/Singleplayer").Visible == (screen == "Singleplayer") &&
            !_main.GetNode<Control>("Margin/Screens/Mesa").Visible, "Solo la pantalla solicitada es visible.");
    }

    private void CheckPanel(string visible, params string[] hidden)
    {
        Check(_main.GetNode<Control>(LobbyContent + visible).IsVisibleInTree(), "Panel visible: " + visible + ".");
        foreach (string panel in hidden)
            Check(!_main.GetNode<Control>(LobbyContent + panel).IsVisibleInTree(), "Panel oculto: " + panel + ".");
    }

    private void CheckInvalidForm(string message)
    {
        CheckOffline(message);
        Check(_main.GetNode<Label>(LobbyContent + "MessageLabel").Text.Length > 0, "La validacion explica el error.");
    }

    private void CheckOffline(string message) => Check(_network.State == ConnectionState.Offline &&
        !_network.IsHost && _network.View is null &&
        _network.GetNode<Node>("ReplicatedState").GetChildCount() == 0, message);

    private async Task CaptureIfAvailable(string name)
    {
        if (_native && _captureDirectory.Length > 0) await Capture(name);
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) throw new InvalidOperationException("La captura necesita render real.");
        Directory.CreateDirectory(_captureDirectory);
        string path = Path.Combine(_captureDirectory, name);
        Error result = image.SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException("No se pudo guardar " + path + ": " + result);
        GD.Print("MENU CAPTURE: " + path);
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
        GD.Print((success ? "MENU NAVIGATION PASS: " : "MENU NAVIGATION FAIL: ") + message + " Comprobaciones=" + _checks);
        GetTree().Quit(success ? 0 : 1);
    }
}
