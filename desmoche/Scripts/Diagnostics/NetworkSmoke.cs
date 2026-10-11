using System;
using System.Linq;
using System.Text.Json;
using Desmoche.Application;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Diagnostics;

// Escena automatica de aceptacion. No representa la UI final.
public partial class NetworkSmoke : Node
{
    private NetworkManager _network = null!;
    private string _mode = "";
    private int _target = 2;
    private long _attemptWindow = -1;
    private GamePhase _attemptPhase;
    private ulong _began, _lastAction, _quitAt;
    private bool _started, _done, _dropClient, _closeHost, _sawDrop, _sawHand;
    private bool _probeSent, _probeRejected;
    private long _probeRequest;
    private int _exitCode;
    private string _lastSummary = "";
    private ENetMultiplayerPeer? _probeTransport;
    private ulong _probeConnected;
    private bool _sawReplica;
    private long _lastAuditedRevision;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("NetworkManager");
        string[] args = OS.GetCmdlineUserArgs();
        _mode = Option(args, "--mode", "");
        _target = int.TryParse(Option(args, "--players", "2"), out int n) ? n : 2;
        int port = int.TryParse(Option(args, "--port", "7000"), out int p) ? p : 7000;
        string address = Option(args, "--ip", "127.0.0.1");
        string name = Option(args, "--name", _mode == "host" ? "Host" : "Cliente");
        _dropClient = args.Contains("--drop-client");
        _closeHost = args.Contains("--close-host");
        _network.ConnectionChanged += (state, message) =>
        {
            GD.Print($"CONEXION: {state} / {message}");
            if (state == ConnectionState.Offline && !_done && _sawHand && _closeHost)
                Finish(_network.View is null, "Perdida de host limpia la vista y termina sesion.");
        };
        _network.ActionCompleted += reply =>
        {
            if (_network.View is null || _network.View.Revision < reply.Revision)
            { Finish(false, "Confirmacion antes de una vista coherente."); return; }
            if (reply.RequestId == _probeRequest && _probeSent)
            {
                _probeRejected = !reply.Success;
                GD.Print(_probeRejected ? "RPC: inicio remoto rechazado." : "ERROR: cliente pudo iniciar.");
                if (!_probeRejected) Finish(false, "Fallo de autoridad.");
            }
            else if (!reply.Success) Finish(false, "Accion rechazada: " + reply.Message);
        };
        _began = Time.GetTicksMsec();
        if (_target is < 2 or > 4) { Finish(false, "Usar --players de 2 a 4."); return; }
        if (_mode.Length == 0)
        {
            GD.Print("Escena Fase6 lista. Ejecutar con --mode host o --mode client. Ver Docs/Fase6.md.");
            return;
        }
        if (_mode == "probe") { StartAdmissionProbe(address, port); return; }
        Error error = _mode == "host" ? _network.Host(name, port) :
            _mode == "client" ? _network.Join(address, name, port) : Error.InvalidParameter;
        if (error != Error.Ok) Finish(false, "No pudo abrirse la conexion: " + error);
    }
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        if (_done)
        {
            if (now >= _quitAt)
            {
                _network.Close();
                _probeTransport?.Close(); _probeTransport?.Dispose(); _probeTransport = null;
                if (_network.View is not null ||
                    _network.GetNode<Node>("ReplicatedState").GetChildCount() != 0)
                { GD.Print("RED ENET ERROR: Estado residual tras desconectar."); _exitCode = 1; }
                else GD.Print("SYNC LIMPIEZA OK.");
                GetTree().Quit(_exitCode);
            }
            return;
        }
        if (_mode.Length == 0) return;
        if (now - _began > 90000) { Finish(false, "La prueba excedio 90 segundos."); return; }
        if (_mode == "probe") { CheckAdmissionProbe(now); return; }
        SnapshotDto? v = _network.View;
        if (v is null) return;
        if (!AuditReplica(v)) return;
        string summary = $"{v.Phase}, jugadores={v.Players.Length}, mano={v.OwnHand.Length}, mazo={v.DeckCount}";
        if (summary != _lastSummary) { GD.Print("VISTA: " + summary); _lastSummary = summary; }
        if (v.Players.Length > 4 || v.RevealedHands.Length != 0 || v.OwnHand.Any(id => id < 0 || id > 51))
        { Finish(false, "Vista invalida."); return; }
        if (!_network.IsHost && _network.State == ConnectionState.Connected && !_probeSent)
        {
            _probeSent = true;
            _probeRequest = _network.Submit(ActionKind.StartMatch);
            _lastAction = now;
            return;
        }
        if (v.OwnHand.Length == 9) _sawHand = true;
        if (_mode == "host" && v.Phase == GamePhase.Preparing && v.Players.Length == _target && !_started)
        {
            if (now - _began < 2500) return;
            _started = true;
            _network.Submit(ActionKind.StartMatch);
            return;
        }
        if (_dropClient && !_network.IsHost && v.Phase == GamePhase.AwaitingDraw)
        { Finish(_probeRejected, "Cliente sale; host debe continuar."); return; }
        if (v.Players.Any(p => !p.Connected))
        {
            _sawDrop = true;
            if (v.Players.Any(p => !p.Connected && p.CardCount != 9))
            { Finish(false, "Mano ausente no conservada."); return; }
        }
        if (_closeHost && _network.IsHost && v.Phase == GamePhase.AwaitingDraw)
        { Finish(true, "Host cierra; clientes deben terminar y limpiar vista."); return; }
        if (v.DiscardCount >= 4)
        {
            bool ok = _sawReplica && _sawHand && (_network.IsHost || _probeRejected) &&
                (!_dropClient || !_network.IsHost || _sawDrop);
            Finish(ok, "Intercambio, robo publico, respuestas y turnos replicados.");
            return;
        }
        if (now - _lastAction < 400) return;
        if (v.OwnResponseSubmitted || (_attemptWindow == v.Window && _attemptPhase == v.Phase)) return;
        long request = 0;
        if (v.Phase == GamePhase.InitialExchange)
            request = _network.Submit(ActionKind.ExchangeCard, v.OwnHand[0]);
        else if (v.Phase == GamePhase.InitialDeclarations)
            request = _network.Submit(ActionKind.FinishInitial);
        else if (v.Phase == GamePhase.AwaitingDraw && v.CurrentPlayerId == v.RecipientId)
            request = _network.Submit(ActionKind.DrawDeck);
        else if (v.Phase == GamePhase.AwaitingClaims)
            request = _network.Submit(ActionKind.Pass);
        if (request != 0)
        { _lastAction = now; _attemptWindow = v.Window; _attemptPhase = v.Phase; }
    }

    private bool AuditReplica(SnapshotDto view)
    {
        var replica = _network.GetNodeOrNull<PublicMatchReplica>("ReplicatedState/PublicState");
        var spawner = _network.GetNode<MultiplayerSpawner>("StateSpawner");
        if (replica is null || replica.GetMultiplayerAuthority() != 1 ||
            spawner.GetMultiplayerAuthority() != 1 || spawner.SpawnLimit != 1 ||
            _network.GetNode<Node>("ReplicatedState").GetChildCount() != 1)
        { Finish(false, "Replica unica o autoridad incorrectas."); return false; }
        var sync = replica.GetNode<MultiplayerSynchronizer>("StateSynchronizer");
        var config = sync.ReplicationConfig;
        var path = new NodePath(":PublicJson");
        if (sync.GetMultiplayerAuthority() != 1 || sync.PublicVisibility ||
            config.GetProperties().Count != 1 || !config.HasProperty(path) ||
            !config.PropertyGetSpawn(path) ||
            config.PropertyGetReplicationMode(path) != SceneReplicationConfig.ReplicationMode.OnChange)
        { Finish(false, "Configuracion de sincronizacion insegura."); return false; }
        if (!NetworkProtocol.TryDecode<PublicSnapshotDto>(replica.PublicJson,
            NetworkProtocol.MaxPublicBytes, out var pub) || pub is null ||
            !StateProjection.IsValidPublic(pub) || pub.Session != view.Session ||
            pub.Revision < view.Revision)
        { Finish(false, "Replica publica no corresponde a la vista."); return false; }
        using var doc = JsonDocument.Parse(replica.PublicJson);
        if (doc.RootElement.TryGetProperty("OwnHand", out _) ||
            doc.RootElement.TryGetProperty("RecipientId", out _) ||
            doc.RootElement.TryGetProperty("OwnResponseSubmitted", out _))
        { Finish(false, "Datos privados en propiedad publica."); return false; }
        _sawReplica = true;
        if (view.Revision > _lastAuditedRevision)
        {
            _lastAuditedRevision = view.Revision;
            GD.Print($"SYNC OK: revision {view.Revision}, replica publica y mano privada coherentes.");
        }
        return true;
    }
    private void StartAdmissionProbe(string address, int port)
    {
        // Solo diagnostico: conectar transporte sin enviar Hello al servidor.
        Multiplayer.ConnectedToServer += () => _probeConnected = Time.GetTicksMsec();
        Multiplayer.ConnectionFailed += () => Finish(false, "El probe no pudo conectar.");
        Multiplayer.ServerDisconnected += () => Finish(false, "El probe fue desconectado antes de comprobar.");
        var api = (SceneMultiplayer)Multiplayer;
        api.AllowObjectDecoding = false;
        api.ServerRelay = false;
        _probeTransport = new ENetMultiplayerPeer();
        Error error = _probeTransport.CreateClient(address, port);
        if (error != Error.Ok) { Finish(false, "No se pudo iniciar probe."); return; }
        api.MultiplayerPeer = _probeTransport;
    }
    private void CheckAdmissionProbe(ulong now)
    {
        if (_network.GetNode<Node>("ReplicatedState").GetChildCount() != 0 || _network.View is not null)
        { Finish(false, "Peer sin Hello recibio estado."); return; }
        if (_probeConnected != 0 && now - _probeConnected >= 1500)
        {
            GD.Print("SYNC PRIVACIDAD OK: ninguna replica antes de la admision.");
            Finish(true, "Visibilidad cerrada para transporte no admitido.");
        }
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        _exitCode = success ? 0 : 1;
        GD.Print((success ? "RED ENET OK: " : "RED ENET ERROR: ") + message);
        // Dejar salir las ultimas vistas antes de cerrar el host en la prueba normal.
        _quitAt = Time.GetTicksMsec() + (_network.IsHost && !_closeHost ? 750UL : 0UL);
    }
    private static string Option(string[] args, string key, string fallback)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}