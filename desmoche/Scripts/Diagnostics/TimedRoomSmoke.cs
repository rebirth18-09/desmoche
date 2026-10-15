using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Diagnostics;

// Transporte ENet real: provoca un empate mediante declaraciones invalidas
// deliberadas y comprueba la siguiente partida y las exclusiones por plazo.
public partial class TimedRoomSmoke : Node
{
    private NetworkManager _network = null!;
    private string _mode = "";
    private int _players = 3, _returning = 2;
    private bool _customSettings, _settingsSent;
    private static readonly MatchSettings CustomSettings = new(11, 40, 7, 6, true, false, false, false, false);
    private bool _skip, _started, _declared, _voted, _sawFinish, _done;
    private ulong _began, _quitAt;
    private long _pending;
    private int _exitCode;
    private string _lastCount = "";
    private string _finalDeckDigest = "";
    private string _finalHandsDigest = "";

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("NetworkManager");
        string[] args = OS.GetCmdlineUserArgs();
        string Option(string key, string fallback)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        _mode = Option("--mode", "host");
        _players = int.Parse(Option("--players", "3"));
        _returning = int.Parse(Option("--returning", "2"));
        _skip = args.Contains("--no-rematch");
        _customSettings = args.Contains("--settings");
        _began = Time.GetTicksMsec();
        _network.ActionCompleted += reply =>
        {
            if (reply.RequestId == _pending) _pending = 0;
            if (!reply.Success && !reply.Penalized) Finish(false, reply.Message);
        };
        _network.ConnectionChanged += (state, message) =>
        {
            if (state != ConnectionState.Offline || _done) return;
            Finish(_mode == "client" && _skip && _sawFinish && message.Contains("revancha", StringComparison.OrdinalIgnoreCase),
                "Salida del invitado que no confirmo: " + message);
        };
        int port = int.Parse(Option("--port", "7010"));
        Error error = _mode == "host" ? _network.Host(Option("--name", "Host"), port, _customSettings ? new MatchSettings(9, 35, 5, 5) : null) :
            _network.Join("127.0.0.1", Option("--name", "Cliente"), port);
        if (error != Error.Ok) Finish(false, error.ToString());
        else if (_mode == "host") GD.Print("TIMED ROOM LISTEN");
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        if (_done)
        {
            if (now >= _quitAt) { _network.Close(); GetTree().Quit(_exitCode); }
            return;
        }
        if (now - _began > 45000) { Finish(false, "Plazo del diagnostico agotado."); return; }
        if (_network.View is not { } view) return;
        if (!StateProjection.IsValidPublic(StateProjection.Public(view)) ||
            !StateProjection.IsValidPrivate(StateProjection.Private(view)))
        { Finish(false, "Proyeccion de red invalida."); return; }
        if (_customSettings && view.Phase != GamePhase.Preparing && view.Settings != CustomSettings)
        { Finish(false, "Los ajustes del host no llegaron o cambiaron en revancha."); return; }
        if (view.Phase == GamePhase.Finished ? view.FinalDeck is null || view.FinalDeck.Length != view.DeckCount
            : view.FinalDeck is not null)
        { Finish(false, "Mazo final ausente o revelado antes de terminar."); return; }
        if (view.Phase == GamePhase.Finished)
        {
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", view.FinalDeck!))));
            if (_finalDeckDigest.Length != 0 && _finalDeckDigest != digest)
            { Finish(false, "El orden del mazo final cambió durante las confirmaciones."); return; }
            if (_finalDeckDigest.Length == 0)
            {
                _finalDeckDigest = digest;
                GD.Print("TIMED ROOM FINAL DECK: count=" + view.DeckCount + "; sha256=" + digest);
            }
            string handsDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                NetworkProtocol.Encode(view.RevealedHands.OrderBy(h => h.PlayerId).ToArray()))));
            if (_finalHandsDigest.Length != 0 && _finalHandsDigest != handsDigest)
            { Finish(false, "Las manos finales cambiaron durante las confirmaciones."); return; }
            if (_finalHandsDigest.Length == 0)
            {
                _finalHandsDigest = handsDigest;
                GD.Print("TIMED ROOM FINAL HANDS: count=" + view.RevealedHands.Length + "; sha256=" + handsDigest);
            }
        }
        if (_sawFinish && view.Phase == GamePhase.InitialExchange)
        {
            bool spectator = _mode == "host" && _skip;
            bool valid = view.Players.Length == _returning && view.DeckCount == 52 - 9 * _returning &&
                view.Players.All(p => p.CardCount == 9 && !p.Eliminated) && view.Melds.Length == 0 &&
                view.RevealedHands.Length == 0 && view.FinalDeck is null &&
                view.OwnSpectating == spectator && view.OwnHand.Length == (spectator ? 0 : 9);
            Finish(valid, spectator ? "Host conserva la autoridad como espectador; dos invitados reciben nueva mano."
                : "Revancha confirmada recibida por red con reparto y asientos nuevos.");
            return;
        }
        if (view.Rematch is { } rematch)
        {
            _sawFinish = true;
            string count = rematch.ReadyPlayerIds.Length + "/" + rematch.EligiblePlayerIds.Length;
            if (count != _lastCount) { GD.Print("TIMED ROOM COUNT: " + count); _lastCount = count; }
            if (!_skip && !_voted && _pending == 0)
            { _voted = true; _pending = _network.Submit(ActionKind.JoinRematch); }
            return;
        }
        if (_pending != 0) return;
        if (_mode == "host" && !_started && view.Phase == GamePhase.Preparing && view.Players.Length == _players)
        {
            if (_customSettings && !_settingsSent)
            { _settingsSent = true; _pending = _network.UpdateMatchSettings(CustomSettings); }
            else { _started = true; _pending = _network.Submit(ActionKind.StartMatch); }
        }
        else if (view.Phase == GamePhase.InitialExchange && !view.OwnResponseSubmitted)
            _pending = _network.Submit(ActionKind.ExchangeCard, cardId: view.OwnHand[0]);
        else if (view.Phase == GamePhase.InitialDeclarations && !_declared)
        {
            _declared = true;
            // Tres cartas nunca cumplen la victoria inicial de cuatro cartas.
            _pending = _network.Submit(ActionKind.DeclareInitial, initialKind: InitialVictoryKind.LowStraight,
                cards: view.OwnHand.Take(3).ToArray());
        }
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true; _exitCode = success ? 0 : 1;
        GD.Print((success ? "TIMED ROOM PASS: " : "TIMED ROOM FAIL: ") + _mode + " / " + message);
        // Tras verificar, cerrar primero el host. Si ambos invitados cierran
        // a la vez, ENet puede entregar dos desconexiones antes de que la
        // autoridad procese la segunda y el diagnóstico dispara un envío
        // durante su propia salida. No altera el flujo de la partida.
        _quitAt = Time.GetTicksMsec() + (_mode == "host" ? 800UL : 1800UL);
    }
}
