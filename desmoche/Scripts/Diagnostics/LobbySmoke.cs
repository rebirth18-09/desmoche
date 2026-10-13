using System;
using Desmoche.Application;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Solo la escena de diagnostico usa este script; Main.tscn no lo referencia.
// Presiona las señales de los botones reales y verifica la ruta de la conexión.
public partial class LobbySmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private const string LobbyPath = "Margin/Screens/Lobby/";
    private const string ContentPath = LobbyPath + "Scroll/Center/Panel/Padding/Content/";
    private NetworkManager _testNetwork = null!;
    private Control _testLobby = null!;
    private Control _testMesa = null!;
    private string _mode = "";
    private int _target;
    private ulong _began, _mesaAt;
    private bool _started, _sawMesa, _done;
    private int _checks;
    private string _lastSummary = "";

    public override void _Ready()
    {
        base._Ready();
        _testNetwork = GetNode<NetworkManager>("NetworkManager");
        _testLobby = GetNode<Control>("Margin/Screens/Lobby");
        _testMesa = GetNode<Control>("Margin/Screens/Mesa");
        _began = Time.GetTicksMsec();
        _testNetwork.ConnectionChanged += TraceConnection;
        _testNetwork.ViewChanged += TraceView;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            _mode = Option(args, "--mode", "");
            _target = int.TryParse(Option(args, "--players", "2"), out int n) ? n : 0;
            Check(_mode is "host" or "client" && _target is >= 2 and <= 4,
                "Argumentos: --mode host/client --players 2..4.");
            Check(_testLobby.Visible && !_testMesa.Visible, "Pantalla inicial Lobby.");
            Check(StartButton.Disabled, "Inicio bloqueado sin sala.");
            Press(_mode == "host" ? "OptionsPanel/CreateOption" : "OptionsPanel/JoinOption");
            NameInput.Text = Option(args, "--name", _mode == "host" ? "Host" : "Cliente");
            if (_mode == "host")
            {
                Press("CreatePanel/Buttons/CreateButton");
                Check(_testNetwork.IsHost, "Crear sala desde botón.");
                Check(_testNetwork.View is { Phase: GamePhase.Preparing } v &&
                    v.Players.Length == 1, "El host es el primer jugador.");
                Check(StartButton.Disabled, "Inicio bloqueado con un solo jugador.");
                Check(RoomCodeProtocol.IsCode(_testNetwork.RoomCode), "La sala tiene un código válido.");
                GD.Print("LOBBY CODE: " + _testNetwork.RoomCode);
                GD.Print("LOBBY HOST LISTO");
            }
            else
            {
                IPInput.Text = "!incorrecto!";
                Press("JoinPanel/Buttons/JoinButton");
                Check(_testNetwork.State == ConnectionState.Offline &&
                    GetNode<Label>(ContentPath + "MessageLabel").Text.Length > 0,
                    "Código inválido rechazado con mensaje.");
                IPInput.Text = Option(args, "--code", "");
                Press("JoinPanel/Buttons/JoinButton");
                Check(_testNetwork.State == ConnectionState.Searching, "Buscar por código desde botón.");
                Check(!NameInput.Editable && !IPInput.Editable, "Campos bloqueados al conectar.");
                Press("SessionPanel/RoomButtons/LeaveButton");
                CheckOffline();
                Press("OptionsPanel/JoinOption");
                Press("JoinPanel/Buttons/JoinButton");
                Check(_testNetwork.State == ConnectionState.Searching, "Reintento tras cancelar.");
            }
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_done) return;
        try
        {
            ulong now = Time.GetTicksMsec();
            CheckTimeout(now);
            if (_mode == "client" && !_sawMesa && _testNetwork.State == ConnectionState.Offline)
                throw new InvalidOperationException("Conexión terminó antes de Mesa: " + _testNetwork.Status);
            SnapshotDto? view = _testNetwork.View;
            if (view is { Phase: GamePhase.Preparing } && _mode == "host" && !_started &&
                view.Players.Length == _target)
            {
                Check(GetNode<ItemList>(ContentPath + "SessionPanel/PlayersList").ItemCount == _target,
                    "Lista contiene todos los jugadores admitidos.");
                Check(!StartButton.Disabled, "Host puede iniciar con 2..4 jugadores.");
                Press("SessionPanel/RoomButtons/StartButton");
                _started = true;
                Check(StartButton.Disabled, "Un segundo inicio queda bloqueado.");
                Check(_testNetwork.View?.Phase == GamePhase.Preparing &&
                    _testLobby.Visible && !_testMesa.Visible, "Sin transición optimista.");
            }
            if (!_sawMesa && view is { Phase: GamePhase.InitialExchange })
            {
                Check(_testMesa.Visible && !_testLobby.Visible, "Transición confirmada a Mesa.");
                Check(ReferenceEquals(_testNetwork, GetNode<NetworkManager>("NetworkManager")) &&
                    _testNetwork.GetPath().ToString() == "/root/Main/NetworkManager",
                    "La conexión conserva instancia y ruta.");
                Check(view.Players.Length == _target && view.OwnHand.Length == 9 &&
                    view.DeckCount == 52 - 9 * _target, "Estado inicial sincronizado.");
                Check(view.RevealedHands.Length == 0, "No hay revelación de manos rivales.");
                Check(GetNode<Mesa>("Margin/Screens/Mesa").RenderedPlayerCount == _target,
                    "Mesa muestra todos los jugadores.");
                if (_mode == "client") Check(StartButton.Disabled, "Cliente no puede iniciar.");
                _sawMesa = true;
                _mesaAt = now;
                GD.Print("LOBBY MESA OK");
            }
            if (_sawMesa && _mode == "host" && now - _mesaAt >= 2500)
            {
                GetNode<Button>("Margin/Screens/Mesa/Scroll/Padding/Content/TopBar/LeaveButton")
                    .EmitSignal(BaseButton.SignalName.Pressed);
                CheckOffline();
                Finish(true, "Host cierra desde Mesa y vuelve a Lobby.");
            }
            else if (_sawMesa && _mode == "client" && _testNetwork.State == ConnectionState.Offline)
            {
                CheckOffline();
                Finish(true, "Pérdida del host vuelve a Lobby.");
            }
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    public override void _ExitTree()
    {
        if (_testNetwork is not null)
        {
            _testNetwork.ConnectionChanged -= TraceConnection;
            _testNetwork.ViewChanged -= TraceView;
        }
        base._ExitTree();
    }

    private void TraceConnection(ConnectionState state, string message) =>
        GD.Print("LOBBY CONEXION: " + _mode + " / " + state + " / " + message);

    private void TraceView(SnapshotDto view)
    {
        string summary = view.Phase + " / jugadores=" + view.Players.Length +
            " / revisión=" + view.Revision;
        if (summary == _lastSummary) return;
        _lastSummary = summary;
        GD.Print("LOBBY VISTA: " + _mode + " / " + summary);
    }

    private Button StartButton => GetNode<Button>(ContentPath + "SessionPanel/RoomButtons/StartButton");
    private LineEdit NameInput => GetNode<LineEdit>(ContentPath + (_mode == "host" ? "CreatePanel/NameInput" : "JoinPanel/NameInput"));
    private LineEdit IPInput => GetNode<LineEdit>(ContentPath + "JoinPanel/IPInput");
    private void Press(string path) => GetNode<Button>(ContentPath + path).EmitSignal(BaseButton.SignalName.Pressed);

    private void CheckOffline()
    {
        Check(_testNetwork.State == ConnectionState.Offline && _testNetwork.View is null,
            "Desconexión limpia la vista.");
        Check(_testLobby.Visible && !_testMesa.Visible, "Regreso a Lobby.");
        Check(NameInput.Editable && IPInput.Editable &&
            GetNode<ItemList>(ContentPath + "SessionPanel/PlayersList").ItemCount == 0, "Campos y lista restaurados.");
        Check(_testNetwork.GetNode<Node>("ReplicatedState").GetChildCount() == 0,
            "No quedan nodos de réplica.");
    }

    private void CheckTimeout(ulong now)
    {
        if (now - _began > 45000) throw new InvalidOperationException("Tiempo agotado; revisar código, host y registros.");
    }

    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        GD.Print("LOBBY CHECK: " + description);
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        GD.Print((success ? "LOBBY OK: " : "LOBBY ERROR: ") + message + " Comprobaciones=" + _checks);
        GetTree().Quit(success ? 0 : 1);
    }

    private static string Option(string[] args, string name, string fallback)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
