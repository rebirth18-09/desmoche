using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

public partial class Lobby : Control
{
    private const string ContentPath = "Scroll/Center/Panel/Padding/Content/";
    private enum OfflinePanel { Options, Create, Join }

    private NetworkManager? _network;
    private Control _optionsPanel = null!;
    private Control _createPanel = null!;
    private Control _joinPanel = null!;
    private Control _sessionPanel = null!;
    private LineEdit _createName = null!;
    private LineEdit _joinName = null!;
    private LineEdit _ip = null!;
    private Button _createOption = null!;
    private Button _joinOption = null!;
    private Button _back = null!;
    private Button _create = null!;
    private Button _join = null!;
    private Button _cancelCreate = null!;
    private Button _cancelJoin = null!;
    private Button _start = null!;
    private Button _leave = null!;
    private Label _status = null!;
    private Label _addresses = null!;
    private Label _playersTitle = null!;
    private Label _waiting = null!;
    private Label _message = null!;
    private ItemList _players = null!;
    private OfflinePanel _panel;
    private ConnectionState _lastState = ConnectionState.Offline;
    private long _pendingStart;
    private bool _ready;
    private bool _leaving;
    private string _roomCode = "";
    private Control _frame = null!;
    private MarginContainer _padding = null!;
    private Label _title = null!, _subtitle = null!, _networkHint = null!;
    private StyleBox _frameStyle = null!;
    private readonly StyleBoxEmpty _openStyle = new();

    // Main decide la navegación; Lobby nunca cambia ni recrea NetworkManager.
    public event Action? BackRequested;

    public override void _Ready()
    {
        _frame = GetNode<Control>("Scroll/Center/Panel");
        _padding = GetNode<MarginContainer>("Scroll/Center/Panel/Padding");
        _frameStyle = _frame.GetThemeStylebox("panel");
        _title = GetNode<Label>(ContentPath + "TitleLabel");
        _subtitle = GetNode<Label>(ContentPath + "SubtitleLabel");
        _networkHint = GetNode<Label>(ContentPath + "NetworkLabel");
        _optionsPanel = GetNode<Control>(ContentPath + "OptionsPanel");
        _createPanel = GetNode<Control>(ContentPath + "CreatePanel");
        _joinPanel = GetNode<Control>(ContentPath + "JoinPanel");
        _sessionPanel = GetNode<Control>(ContentPath + "SessionPanel");
        _createName = GetNode<LineEdit>(ContentPath + "CreatePanel/NameInput");
        _joinName = GetNode<LineEdit>(ContentPath + "JoinPanel/NameInput");
        _ip = GetNode<LineEdit>(ContentPath + "JoinPanel/IPInput");
        _createOption = GetNode<Button>(ContentPath + "OptionsPanel/CreateOption");
        _joinOption = GetNode<Button>(ContentPath + "OptionsPanel/JoinOption");
        _back = GetNode<Button>(ContentPath + "OptionsPanel/BackButton");
        _create = GetNode<Button>(ContentPath + "CreatePanel/Buttons/CreateButton");
        _join = GetNode<Button>(ContentPath + "JoinPanel/Buttons/JoinButton");
        _cancelCreate = GetNode<Button>(ContentPath + "CreatePanel/Buttons/CancelButton");
        _cancelJoin = GetNode<Button>(ContentPath + "JoinPanel/Buttons/CancelButton");
        _start = GetNode<Button>(ContentPath + "SessionPanel/RoomButtons/StartButton");
        _leave = GetNode<Button>(ContentPath + "SessionPanel/RoomButtons/LeaveButton");
        _status = GetNode<Label>(ContentPath + "SessionPanel/StatusLabel");
        _addresses = GetNode<Label>(ContentPath + "SessionPanel/AddressLabel");
        _playersTitle = GetNode<Label>(ContentPath + "SessionPanel/PlayersTitle");
        _players = GetNode<ItemList>(ContentPath + "SessionPanel/PlayersList");
        _waiting = GetNode<Label>(ContentPath + "SessionPanel/WaitingLabel");
        _message = GetNode<Label>(ContentPath + "MessageLabel");
        _createName.MaxLength = NetworkProtocol.MaxNameLength;
        _joinName.MaxLength = NetworkProtocol.MaxNameLength;
        _ip.MaxLength = 12;
        _ip.PlaceholderText = "Ej.: AB7K2M";
        _createOption.Pressed += ShowCreate;
        _joinOption.Pressed += ShowJoin;
        _back.Pressed += RequestBack;
        _create.Pressed += CreateRoom;
        _join.Pressed += JoinRoom;
        _cancelCreate.Pressed += ShowOptions;
        _cancelJoin.Pressed += ShowOptions;
        _start.Pressed += StartMatch;
        _leave.Pressed += LeaveRoom;
        _createName.TextChanged += OnNameChanged;
        _joinName.TextChanged += OnNameChanged;
        _createName.TextSubmitted += OnCreateNameSubmitted;
        _ip.TextSubmitted += OnAddressSubmitted;
        Resized += UpdateLayout;
        _ready = true;
        Render();
    }

    public void Bind(NetworkManager network)
    {
        if (!_ready) throw new InvalidOperationException("Lobby debe estar listo antes de Bind.");
        Unbind();
        _network = network;
        _lastState = network.State;
        if (network.IsHost) _roomCode = _network!.RoomCode;
        network.ViewChanged += OnViewChanged;
        network.ConnectionChanged += OnConnectionChanged;
        network.ActionCompleted += OnActionCompleted;
        Render();
    }

    public void Unbind()
    {
        if (_network is not null)
        {
            _network.ViewChanged -= OnViewChanged;
            _network.ConnectionChanged -= OnConnectionChanged;
            _network.ActionCompleted -= OnActionCompleted;
        }
        _network = null;
        _pendingStart = 0;
        _lastState = ConnectionState.Offline;
        _roomCode = "";
    }

    public override void _ExitTree()
    {
        Unbind();
        if (!_ready) return;
        _createOption.Pressed -= ShowCreate;
        _joinOption.Pressed -= ShowJoin;
        _back.Pressed -= RequestBack;
        _create.Pressed -= CreateRoom;
        _join.Pressed -= JoinRoom;
        _cancelCreate.Pressed -= ShowOptions;
        _cancelJoin.Pressed -= ShowOptions;
        _start.Pressed -= StartMatch;
        _leave.Pressed -= LeaveRoom;
        _createName.TextChanged -= OnNameChanged;
        _joinName.TextChanged -= OnNameChanged;
        _createName.TextSubmitted -= OnCreateNameSubmitted;
        _ip.TextSubmitted -= OnAddressSubmitted;
        Resized -= UpdateLayout;
        _ready = false;
    }

    public void ShowOptions()
    {
        if (!_ready || _network?.State is not (null or ConnectionState.Offline)) return;
        PreserveName();
        _panel = OfflinePanel.Options;
        ShowMessage("");
        Render();
        FocusDefault();
    }

    public void FocusDefault()
    {
        if (!_ready || !IsVisibleInTree()) return;
        if (_network?.State is null or ConnectionState.Offline)
        {
            if (_panel == OfflinePanel.Create) _createName.GrabFocus();
            else if (_panel == OfflinePanel.Join) _joinName.GrabFocus();
            else _createOption.GrabFocus();
        }
        else if (_start.Visible && !_start.Disabled) _start.GrabFocus();
        else _leave.GrabFocus();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey key ||
            !key.Pressed || key.Echo || key.Keycode != Key.Escape) return;
        if (_network?.State is null or ConnectionState.Offline)
        {
            if (_panel == OfflinePanel.Options) RequestBack();
            else ShowOptions();
        }
        else if (_network.State is ConnectionState.Searching or ConnectionState.Connecting or ConnectionState.Handshaking)
            LeaveRoom();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void ShowCreate() => ShowPanel(OfflinePanel.Create);
    private void ShowJoin() => ShowPanel(OfflinePanel.Join);

    private void ShowPanel(OfflinePanel panel)
    {
        if (!CanConnect()) return;
        PreserveName();
        _panel = panel;
        ShowMessage("");
        Render();
        FocusDefault();
    }

    private void RequestBack()
    {
        if (_network?.State is not (null or ConnectionState.Offline)) return;
        ShowOptions();
        BackRequested?.Invoke();
    }

    private void OnNameChanged(string text)
    {
        // Ambos formularios conservan el mismo nombre sin guardar estado de partida.
        if (_createName.Text != text) _createName.Text = text;
        if (_joinName.Text != text) _joinName.Text = text;
    }

    private void PreserveName()
    {
        if (_panel == OfflinePanel.Create) OnNameChanged(_createName.Text);
        else if (_panel == OfflinePanel.Join) OnNameChanged(_joinName.Text);
    }

    private void OnViewChanged(SnapshotDto view) => Render();

    private void OnConnectionChanged(ConnectionState state, string message)
    {
        bool wasConnecting = _lastState is ConnectionState.Searching or ConnectionState.Connecting or ConnectionState.Handshaking;
        _lastState = state;
        if (state == ConnectionState.Offline)
        {
            _pendingStart = 0;
            _roomCode = "";
            _panel = wasConnecting && !_leaving ? OfflinePanel.Join : OfflinePanel.Options;
            ShowMessage(message, !_leaving && wasConnecting);
        }
        else
        {
            ShowMessage("");
            if (state == ConnectionState.Hosting) _roomCode = _network!.RoomCode;
        }
        Render();
        FocusDefault();
    }

    private void OnActionCompleted(ReplyDto reply)
    {
        if (reply.RequestId != _pendingStart || _pendingStart == 0) return;
        _pendingStart = 0;
        ShowMessage(reply.Success ? "Partida iniciada." : reply.Message, !reply.Success);
        Render();
    }

    private bool CanConnect() => _network?.State == ConnectionState.Offline;

    private bool ReadName(LineEdit input, out string name)
    {
        name = input.Text.Trim();
        if (NetworkProtocol.ValidName(name))
        {
            OnNameChanged(input.Text);
            return true;
        }
        ShowMessage("Escribe un nombre de 1 a " + NetworkProtocol.MaxNameLength +
            " caracteres, sin saltos de línea.", true);
        input.GrabFocus();
        return false;
    }

    private void CreateRoom()
    {
        if (_panel != OfflinePanel.Create || !CanConnect() || !ReadName(_createName, out string name)) return;
        ShowMessage("");
        Error error = _network!.Host(name);
        if (error != Error.Ok) ShowConnectionError(error, true);
        Render();
    }

    private void JoinRoom()
    {
        if (_panel != OfflinePanel.Join || !CanConnect() || !ReadName(_joinName, out string name)) return;
        string code = RoomCodeProtocol.Normalize(_ip.Text);
        if (!RoomCodeProtocol.IsCode(code))
        {
            ShowMessage("Escribe el código de seis letras y números que aparece en la sala del host.", true);
            _ip.GrabFocus(); return;
        }
        ShowMessage("");
        Error error = _network!.JoinByCode(code, name);
        if (error != Error.Ok) ShowConnectionError(error, false);
        Render();
    }

    private void OnCreateNameSubmitted(string text) => CreateRoom();
    private void OnAddressSubmitted(string text) => JoinRoom();

    private void StartMatch()
    {
        if (_network is null || _start.Disabled || _pendingStart != 0) return;
        _pendingStart = _network.Submit(ActionKind.StartMatch);
        ShowMessage(_pendingStart == 0 ? "No se pudo enviar la solicitud de inicio." :
            "Solicitando inicio al servidor…", _pendingStart == 0);
        Render();
    }

    private void LeaveRoom()
    {
        if (_network is null || _network.State == ConnectionState.Offline) return;
        bool connecting = _network.State is ConnectionState.Searching or ConnectionState.Connecting or ConnectionState.Handshaking;
        _leaving = true;
        try
        {
            _network.Close(connecting ? "Conexión cancelada." :
                _network.IsHost ? "Sala cerrada por el host." : "Has salido de la sala.");
        }
        finally { _leaving = false; }
    }

    private void ShowConnectionError(Error error, bool creating)
    {
        string text = creating
            ? "No se pudo crear la sala. Comprueba que no haya otra sala abierta en este dispositivo."
            : "No se pudo iniciar la conexión. Comprueba el código y que la sala esté abierta.";
        ShowMessage(text + " Código: " + error + ".", true);
    }

    private void ShowMessage(string text, bool error = false)
    {
        _message.Text = text;
        _message.Visible = text.Length > 0;
        _message.Modulate = error ? new Color(1f, 0.65f, 0.65f) : Colors.White;
    }

    private void Render()
    {
        if (!_ready) return;
        ConnectionState state = _network?.State ?? ConnectionState.Offline;
        SnapshotDto? view = _network?.View;
        bool offline = state == ConnectionState.Offline;
        bool connecting = state is ConnectionState.Searching or ConnectionState.Connecting or ConnectionState.Handshaking;
        bool host = _network?.IsHost == true;
        _optionsPanel.Visible = offline && _panel == OfflinePanel.Options;
        _createPanel.Visible = offline && _panel == OfflinePanel.Create;
        _joinPanel.Visible = offline && _panel == OfflinePanel.Join;
        _sessionPanel.Visible = !offline;
        UpdateLayout();
        _createName.Editable = offline;
        _joinName.Editable = offline;
        _ip.Editable = offline;
        _createOption.Disabled = !offline || _network is null;
        _joinOption.Disabled = !offline || _network is null;
        _create.Disabled = !offline || _network is null;
        _join.Disabled = !offline || _network is null;
        _leave.Disabled = offline;
        _leave.Text = connecting ? "Cancelar conexión" : host ? "Cerrar sala" : "Salir de sala";
        int count = view?.Players.Count(p => p.Connected) ?? 0;
        _start.Visible = host;
        _start.Disabled = !host || view?.Phase != GamePhase.Preparing ||
            count is < 2 or > 4 || _pendingStart != 0;
        _start.Text = _pendingStart != 0 ? "Iniciando…" : "Iniciar partida";
        _status.Text = _network?.Status ?? "Sin conexión.";
        _addresses.Visible = !connecting;
        _addresses.Text = host
            ? "Código de sala: " + _roomCode + "\nCompártelo con tus amigos en la misma red Wi-Fi o Ethernet."
            : "Conectado a la sala del host.";
        _playersTitle.Visible = !connecting;
        _players.Visible = !connecting;
        _playersTitle.Text = "Jugadores (" + count + "/4)";
        _waiting.Visible = true;
        _waiting.Text = connecting ? "Estableciendo conexión y verificando la sala…" :
            host ? count < 2 ? "Necesitas al menos 2 jugadores para empezar." :
                "Puedes iniciar cuando todos estén listos." : "Esperando a que el host inicie la partida.";
        _players.Clear();
        if (view is null) return;
        foreach (PublicPlayerDto player in view.Players.OrderBy(p => p.SeatIndex))
        {
            string tags = player.Id == view.RecipientId ? " · Tú" : "";
            if (player.Id == 1) tags += " · Host";
            if (!player.Connected) tags += " · Desconectado";
            _players.AddItem((player.SeatIndex + 1) + ". " + player.Name + tags, selectable: false);
        }
    }

    private void UpdateLayout()
    {
        if (!_ready) return;
        bool options = _optionsPanel.Visible;
        _frame.AddThemeStyleboxOverride("panel", options ? _openStyle : _frameStyle);
        _frame.CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 64, 280, options ? 940 : 690), 0);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            _padding.AddThemeConstantOverride("margin_" + side, options ? 0 : 32);
        _title.Text = options ? "DESMOCHE" : "Multijugador";
        _title.AddThemeFontSizeOverride("font_size", options ? Size.Y < 700 ? 52 : 72 : 18);
        _subtitle.Visible = _networkHint.Visible = options;
        GetNode<Control>(ContentPath + "HeaderDivider").Visible = options;
    }

}
