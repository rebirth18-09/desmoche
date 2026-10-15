using System;
using Desmoche.Application;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Sessions;
using Godot;

namespace Desmoche.Networking;

public enum ConnectionState { Offline, Connecting, Handshaking, Hosting, Connected, Searching }

// Main/NetworkManager permanece en la misma ruta durante toda la sesion.
public partial class NetworkManager : Node, IGameSession
{
    private ENetMultiplayerPeer? _peer;
    private ServerSession? _server;
    private LanRoomDiscovery? _discovery;
    private string _expectedRoomCode = "";
    public string RoomCode { get; private set; } = "";
    private Node _replicaRoot = null!;
    private MultiplayerSpawner _spawner = null!;
    private PublicMatchReplica? _replica;
    private readonly ClientStateAssembler _assembler = new();
    private PublicSnapshotDto? _earlyPublic;
    private PrivateSnapshotDto? _earlyPrivate;
    private string _acceptedSession = "";
    private readonly Queue<ReplyDto> _replies = new();
    private readonly Dictionary<long, ulong> _pending = new();
    private readonly Dictionary<long, ulong> _closing = new();
    private readonly Queue<(string? Json, long Window)> _localCommands = new();
    private readonly Dictionary<long, int> _invalid = new();
    private long _nextRequest;
    private string _name = "";
    private ulong _deadline;
    private bool _ready;
    public ConnectionState State { get; private set; }
    public bool IsHost => State == ConnectionState.Hosting && _server is not null;
    public bool IsActive => State is ConnectionState.Hosting or ConnectionState.Connected;
    public bool IsLocal => false;
    public bool CanManageMatch => IsHost;
    public SnapshotDto? View { get; private set; }
    public string Status { get; private set; } = "Sin conexion.";
    public event Action<SnapshotDto>? ViewChanged;
    public event Action<ReplyDto>? ActionCompleted;
    public event Action<ConnectionState, string>? ConnectionChanged;
    public event Action? SessionChanged;

    public override void _Ready()
    {
        SetMultiplayerAuthority(1);
        _replicaRoot = new Node { Name = "ReplicatedState" };
        AddChild(_replicaRoot);
        _spawner = new MultiplayerSpawner
        {
            Name = "StateSpawner",
            SpawnPath = new NodePath("../ReplicatedState"),
            SpawnLimit = 1,
            SpawnFunction = Callable.From<Variant, Node>(CreatePublicReplica)
        };
        _spawner.SetMultiplayerAuthority(1);
        AddChild(_spawner);
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _ready = true;
    }
    public Error Host(string name, int port = NetworkProtocol.Port, MatchSettings? settings = null)
    {
        if (!_ready || State != ConnectionState.Offline) return Error.AlreadyInUse;
        if (!NetworkProtocol.ValidName(name) || port is < 1 or > 65535 ||
            settings is not null && !settings.IsValid()) return Error.InvalidParameter;
        ConfigureApi();
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(port, 3);
        if (error != Error.Ok) { peer.Dispose(); return error; }
        string roomCode = RoomCodeProtocol.CreateCode();
        var discovery = new LanRoomDiscovery();
        error = discovery.Advertise(roomCode, port);
        if (error != Error.Ok) { discovery.Dispose(); peer.Close(); peer.Dispose(); return error; }
        _discovery = discovery; RoomCode = roomCode;
        _server = new ServerSession(name, multiplayerRematch: true, settings: settings);
        _acceptedSession = _server.SessionId;
        _assembler.Start(_acceptedSession, 1);
        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        ChangeState(ConnectionState.Hosting, "Sala creada.");
        BroadcastViews();
        return Error.Ok;
    }
    public Error Join(string address, string name, int port = NetworkProtocol.Port)
    {
        if (!_ready || State != ConnectionState.Offline) return Error.AlreadyInUse;
        if (!NetworkProtocol.ValidName(name) || string.IsNullOrWhiteSpace(address) ||
            address.Length > 255 || port is < 1 or > 65535) return Error.InvalidParameter;
        ConfigureApi();
        _name = name.Trim();
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(address.Trim(), port);
        if (error != Error.Ok) { peer.Dispose(); return error; }
        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        _deadline = Time.GetTicksMsec() + 10000;
        ChangeState(ConnectionState.Connecting, "Conectando con el host.");
        return Error.Ok;
    }
    public Error JoinByCode(string code, string name)
    {
        code = RoomCodeProtocol.Normalize(code);
        if (!_ready || State != ConnectionState.Offline) return Error.AlreadyInUse;
        if (!RoomCodeProtocol.IsCode(code) || !NetworkProtocol.ValidName(name)) return Error.InvalidParameter;
        var discovery = new LanRoomDiscovery();
        Error error = discovery.Search(code, Time.GetTicksMsec());
        if (error != Error.Ok) { discovery.Dispose(); return error; }
        _discovery = discovery; _name = name.Trim(); _expectedRoomCode = code; RoomCode = code;
        ChangeState(ConnectionState.Searching, "Buscando sala " + code + " en la red local…");
        return Error.Ok;
    }
    private void ConfigureApi()
    {
        var api = (SceneMultiplayer)Multiplayer;
        api.AllowObjectDecoding = false;
        api.ServerRelay = false;
        api.RefuseNewConnections = false;
    }
    public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
        bool declareWin = false, InitialVictoryKind? initialKind = null,
        int[]? cards = null, int? sourceMeldId = null) =>
        SubmitCommand(kind, cardId, plays, declareWin, initialKind, cards, sourceMeldId);

    public long UpdateMatchSettings(MatchSettings settings) =>
        IsHost && View?.Phase == GamePhase.Preparing && settings.IsValid()
            ? SubmitCommand(ActionKind.UpdateSettings, settings: settings) : 0;

    private long SubmitCommand(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
        bool declareWin = false, InitialVictoryKind? initialKind = null,
        int[]? cards = null, int? sourceMeldId = null, MatchSettings? settings = null)
    {
        if (View is null || State is not (ConnectionState.Hosting or ConnectionState.Connected) ||
            IsHost && _localCommands.Count >= 32) return 0;
        var command = new CommandDto(NetworkProtocol.Version, View.Session,
            ++_nextRequest, View.Window, kind, cardId, sourceMeldId,
            initialKind, declareWin, cards, plays, settings);
        string json = NetworkProtocol.Encode(command);
        if (IsHost) _localCommands.Enqueue((json, View.Window));
        else RpcId(1, MethodName.RequestAction, json);
        return command.RequestId;
    }
    public void ExpireClaimsAsHost()
    {
        if (IsHost && View is not null && _localCommands.Count < 32)
            _localCommands.Enqueue((null, View.Window));
    }
    public void Close(string reason = "Conexion cerrada.")
    {
        // Close() de ENet hace flush incluso durante Connecting. Vaciar primero
        // el CONNECT pendiente evita enviar un intento que ya fue cancelado.
        if (_peer is not null &&
            _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting)
            _peer.GetPeer(1)?.PeerDisconnectNow(0);
        _discovery?.Dispose(); _discovery = null;
        RoomCode = _expectedRoomCode = "";
        _server = null;
        _pending.Clear(); _closing.Clear(); _invalid.Clear(); _localCommands.Clear();
        _replies.Clear(); _assembler.Reset();
        _earlyPublic = null; _earlyPrivate = null; _acceptedSession = "";
        View = null; _nextRequest = 0;
        if (_ready)
        {
            var api = (SceneMultiplayer)Multiplayer;
            api.MultiplayerPeer = new OfflineMultiplayerPeer();
            api.RefuseNewConnections = false;
        }
        _peer?.Close(); _peer?.Dispose(); _peer = null;
        // Retirar inmediatamente del arbol permite volver a usar el mismo nombre.
        if (_ready)
            foreach (Node node in _replicaRoot.GetChildren())
            { _replicaRoot.RemoveChild(node); node.QueueFree(); }
        _replica = null;
        ChangeState(ConnectionState.Offline, reason);
    }
    public override void _ExitTree()
    {
        if (!_ready) return;
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnected;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        Close();
        _ready = false;
    }
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        if (_discovery?.Poll(now, _server?.AcceptingPlayers == true) is LanRoomDiscovery.SearchResult found &&
            State == ConnectionState.Searching)
        {
            _discovery.Dispose(); _discovery = null;
            if (found.Error is not null) { Close(found.Error); return; }
            State = ConnectionState.Offline; // Join emite el siguiente estado sin un Offline intermedio visible.
            Error error = Join(found.Address!, _name, found.Port);
            if (error != Error.Ok) { Close("No se pudo conectar con la sala encontrada."); return; }
        }
        if (State is ConnectionState.Connecting or ConnectionState.Handshaking && now >= _deadline)
            Close("No se pudo completar la conexion y sincronizacion en 10 segundos.");
        if (!IsHost) return;
        if (_server!.Tick((long)now)) BroadcastViews();
        foreach (long id in _pending.Where(p => now >= p.Value).Select(p => p.Key).ToArray()) Drop(id);
        foreach (long id in _closing.Where(p => now >= p.Value).Select(p => p.Key).ToArray()) Drop(id);
        for (int i = 0; i < 32 && IsHost && _localCommands.TryDequeue(out var action); i++)
        {
            if (action.Json is not null) Dispatch(1, action.Json);
            else if (_server!.Window == action.Window)
            {
                ServerSession session = _server;
                ReplyDto reply = session.ExpireClaims();
                BroadcastViews();
                if (ReferenceEquals(_server, session)) QueueReply(reply);
            }
        }
    }
    private void OnPeerConnected(long id)
    {
        if (!IsHost) return;
        _replica?.SetPeerVisible((int)id, false);
        _pending[id] = Time.GetTicksMsec() + 10000;
    }
    private void OnPeerDisconnected(long id)
    {
        if (!IsHost) return;
        _replica?.SetPeerVisible((int)id, false);
        _pending.Remove(id); _closing.Remove(id); _invalid.Remove(id);
        _server!.Disconnect(id);
        BroadcastViews();
    }
    private void OnConnected()
    {
        if (State != ConnectionState.Connecting) return;
        _deadline = Time.GetTicksMsec() + 10000;
        ENetMultiplayerPeer? peer = _peer;
        ChangeState(ConnectionState.Handshaking, "Comprobando protocolo y sala.");
        if (ReferenceEquals(_peer, peer) && State == ConnectionState.Handshaking)
            RpcId(1, MethodName.Hello, NetworkProtocol.Version, _name, _expectedRoomCode);
    }
    private void OnConnectionFailed() => Close("No fue posible conectar con el host.");
    private void OnServerDisconnected() => Close("El host se desconecto. La sesion termino.");

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void Hello(int version, string name, string roomCode)
    {
        if (!IsHost) return;
        int sender = Multiplayer.GetRemoteSenderId();
        if (!_pending.Remove(sender)) { if (sender > 1) Drop(sender); return; }
        string error = "El código no corresponde a esta sala.";
        bool accepted = (roomCode == "" || roomCode == RoomCode) &&
            _server!.Join(sender, version, name, out error);
        string message = accepted ? NetworkProtocol.Encode(new AdmissionDto(NetworkProtocol.Version,
            _server!.SessionId, _server.ViewFor(sender).RecipientId)) : error;
        RpcId(sender, MethodName.Admission, accepted, message);
        if (accepted) BroadcastViews();
        else _closing[sender] = Time.GetTicksMsec() + 500;
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RequestAction(string json)
    {
        if (!IsHost) return;
        int sender = Multiplayer.GetRemoteSenderId();
        if (!_server!.Peers.Contains(sender) || json is null || json.Length > NetworkProtocol.MaxCommandBytes)
        { Drop(sender); return; }
        Dispatch(sender, json);
    }
    private void Dispatch(long sender, string json)
    {
        ServerSession? session = _server;
        if (session is null) return;
        long before = session.Revision;
        ReplyDto reply = session.Receive(sender, json, (long)Time.GetTicksMsec());
        if (sender != 1 && reply.RequestId == 0)
        {
            _invalid.TryGetValue(sender, out int strikes);
            _invalid[sender] = ++strikes;
            if (strikes >= 3) { Drop(sender); return; }
        }
        if (session.Revision != before) BroadcastViews();
        if (!ReferenceEquals(_server, session) || !IsHost) return;
        if (sender == 1) QueueReply(reply);
        else RpcId(sender, MethodName.ActionReply, NetworkProtocol.Encode(reply));
    }
    private void BroadcastViews()
    {
        ServerSession? session = _server;
        if (session is null || !IsHost) return;
        foreach (var removed in session.TakeRemovedPeers())
        {
            _replica?.SetPeerVisible((int)removed.Peer, false);
            RpcId(removed.Peer, MethodName.RematchExcluded, removed.Reason);
            _closing[removed.Peer] = Time.GetTicksMsec() + 500;
        }
        ((SceneMultiplayer)Multiplayer).RefuseNewConnections = !session.AcceptingPlayers;
        var views = session.Peers.Select(peer => (Peer: peer, View: session.ViewFor(peer))).ToArray();
        PublicSnapshotDto publicState = StateProjection.Public(views.Single(x => x.Peer == 1).View);
        string publicJson = NetworkProtocol.Encode(publicState);
        if (!NetworkProtocol.TryDecode<PublicSnapshotDto>(publicJson, NetworkProtocol.MaxPublicBytes, out _))
            throw new InvalidOperationException("La proyeccion publica excede el contrato.");
        if (_replica is null) _spawner.Spawn(publicJson);
        else _replica.PublicJson = publicJson;
        // El estado actual se asigna antes de habilitar el spawn para un nuevo admitido.
        foreach (var item in views.Where(x => x.Peer != 1))
        {
            if (!ReferenceEquals(_server, session) || !IsHost) return;
            _replica!.SetPeerVisible((int)item.Peer, true);
            RpcId(item.Peer, MethodName.ReceiveView,
                NetworkProtocol.Encode(StateProjection.Private(item.View)));
        }
        if (!ReferenceEquals(_server, session) || !IsHost) return;
        // El host tambien combina las dos proyecciones; no lee el dominio desde la UI.
        ApplyPublic(publicState);
        ApplyPrivate(StateProjection.Private(views.Single(x => x.Peer == 1).View));
    }
    private Node CreatePublicReplica(Variant data)
    {
        string json = data.VariantType == Variant.Type.String ? data.AsString() : "";
        if (!NetworkProtocol.TryDecode<PublicSnapshotDto>(json, NetworkProtocol.MaxPublicBytes, out var state) ||
            state is null || !StateProjection.IsValidPublic(state))
        {
            DeferClose("Spawn publico incompatible.");
            return null!;
        }
        var replica = new PublicMatchReplica { Name = "PublicState", PublicJson = json };
        replica.Configure(IsHost ? _server!.Peers.Where(p => p > 1).Select(p => (int)p) : Array.Empty<int>());
        replica.PublicStateReceived += OnPublicState;
        _replica = replica;
        return replica; // MultiplayerSpawner se encarga de AddChild.
    }
    private void OnPublicState(string json)
    {
        if (IsHost || State == ConnectionState.Offline) return;
        if (!NetworkProtocol.TryDecode<PublicSnapshotDto>(json, NetworkProtocol.MaxPublicBytes, out var state) ||
            state is null || !StateProjection.IsValidPublic(state))
        { DeferClose("Estado publico incompatible."); return; }
        if (_acceptedSession.Length == 0) { _earlyPublic = state; return; }
        ApplyPublic(state);
    }
    private void Drop(long peer)
    {
        if (!IsHost || peer <= 1 || peer > int.MaxValue) return;
        _replica?.SetPeerVisible((int)peer, false);
        _pending.Remove(peer); _closing.Remove(peer); _invalid.Remove(peer);
        _server!.Disconnect(peer);
        ((SceneMultiplayer)Multiplayer).DisconnectPeer((int)peer);
        BroadcastViews();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void Admission(bool accepted, string message)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 || State != ConnectionState.Handshaking) return;
        if (!accepted) { DeferClose(message); return; }
        if (!NetworkProtocol.TryDecode<AdmissionDto>(message, 2048, out var admission) ||
            admission is null || admission.Version != NetworkProtocol.Version ||
            !Guid.TryParseExact(admission.Session, "N", out _) || admission.RecipientId <= 0)
        { DeferClose("Admision incompatible."); return; }
        _acceptedSession = admission.Session;
        _assembler.Start(admission.Session, admission.RecipientId);
        if (_earlyPublic is not null) ApplyPublic(_earlyPublic);
        if (_earlyPrivate is not null) ApplyPrivate(_earlyPrivate);
        _earlyPublic = null; _earlyPrivate = null;
    }
    // Nombre conservado de fase 5; ahora contiene EXCLUSIVAMENTE la parte privada.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void ReceiveView(string json)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 ||
            State is not (ConnectionState.Handshaking or ConnectionState.Connected)) return;
        if (!NetworkProtocol.TryDecode<PrivateSnapshotDto>(json, NetworkProtocol.MaxPrivateBytes, out var state) ||
            state is null || !StateProjection.IsValidPrivate(state))
        { DeferClose("Vista privada incompatible."); return; }
        if (_acceptedSession.Length == 0) { _earlyPrivate = state; return; }
        ApplyPrivate(state);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RematchExcluded(string reason)
    {
        if (Multiplayer.GetRemoteSenderId() == 1 && State == ConnectionState.Connected)
            DeferClose(reason);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void ActionReply(string json)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 ||
            State is not (ConnectionState.Handshaking or ConnectionState.Connected)) return;
        if (NetworkProtocol.TryDecode<ReplyDto>(json, 2048, out var reply) && reply is not null)
            QueueReply(reply);
    }
    private void ApplyPublic(PublicSnapshotDto state)
    {
        if (_assembler.TryApplyPublic(state, out SnapshotDto? committed)) AcceptView(committed!);
    }
    private void ApplyPrivate(PrivateSnapshotDto state)
    {
        if (_assembler.TryApplyPrivate(state, out SnapshotDto? committed)) AcceptView(committed!);
    }
    private void AcceptView(SnapshotDto view)
    {
        View = view;
        if (State == ConnectionState.Handshaking)
            ChangeState(ConnectionState.Connected, "Conectado y sincronizado.");
        if (View != view || State == ConnectionState.Offline) return;
        ViewChanged?.Invoke(view);
        FlushReplies();
    }
    private void QueueReply(ReplyDto reply)
    {
        if (_replies.Count >= 64) { DeferClose("Demasiadas confirmaciones pendientes."); return; }
        _replies.Enqueue(reply);
        FlushReplies();
    }
    private void FlushReplies()
    {
        // El sincronizador puede llegar despues que el RPC y omitir revisiones intermedias.
        while (View is not null && _replies.TryPeek(out ReplyDto? next) && View.Revision >= next.Revision)
        { _replies.Dequeue(); ActionCompleted?.Invoke(next); }
    }
    private void DeferClose(string reason)
    {
        string session = _acceptedSession;
        ENetMultiplayerPeer? peer = _peer;
        Callable.From(() =>
        {
            if (ReferenceEquals(_peer, peer) && _acceptedSession == session) Close(reason);
        }).CallDeferred();
    }
    private void ChangeState(ConnectionState state, string message)
    { State = state; Status = message; ConnectionChanged?.Invoke(state, message); SessionChanged?.Invoke(); }
}
