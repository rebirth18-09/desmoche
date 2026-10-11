using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;

namespace Desmoche.Networking.Core;

// Autoridad del host o de una partida local. No expone el motor ni el estado completo.
public sealed class ServerSession
{
    private sealed class Connection
    {
        public int PlayerId { get; }
        public long LastRequest { get; set; }
        public long RateSecond { get; set; } = -1;
        public int RateCount { get; set; }
        public Connection(int id) => PlayerId = id;
    }
    private readonly GameManager _game;
    private readonly Func<Deck>? _deckFactory;
    private readonly Dictionary<long, Connection> _connections = new();
    private readonly HashSet<int> _departed = new();
    private int _nextPlayerId = 2;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public long Revision { get; private set; } = 1;
    public long Window { get; private set; } = 1;
    public bool AcceptingPlayers => _game.Phase == GamePhase.Preparing;
    public IReadOnlyList<long> Peers => Array.AsReadOnly(_connections.Keys.OrderBy(x => x).ToArray());

    public ServerSession(string hostName, Func<Deck>? deckFactory = null)
    {
        if (!NetworkProtocol.ValidName(hostName)) throw new ArgumentException("Nombre invalido.");
        _game = new GameManager(1, hostName.Trim());
        _deckFactory = deckFactory;
        _connections.Add(1, new Connection(1));
    }
    public bool Join(long peer, int version, string name, out string error)
    {
        error = "";
        if (peer <= 1 || peer > int.MaxValue || _connections.ContainsKey(peer))
        { error = "Conexion invalida o ya registrada."; return false; }
        if (version != NetworkProtocol.Version)
        { error = "Version de protocolo incompatible."; return false; }
        if (!NetworkProtocol.ValidName(name))
        { error = "Nombre invalido (1 a 24 caracteres)."; return false; }
        if (!AcceptingPlayers || _connections.Count >= 4)
        { error = "Sala completa o partida iniciada."; return false; }
        var result = _game.AddPlayer(_nextPlayerId, name.Trim());
        if (!result.Success) { error = result.Message; return false; }
        _connections.Add(peer, new Connection(_nextPlayerId++));
        Revision++;
        return true;
    }

    public void Disconnect(long peer)
    {
        if (peer == 1 || !_connections.Remove(peer, out Connection? connection)) return;
        var before = Context();
        if (_game.Phase == GamePhase.Preparing)
            _game.RemovePreparingPlayer(connection.PlayerId);
        else
        {
            _departed.Add(connection.PlayerId);
            _game.DisconnectPlayer(connection.PlayerId);
            ResolveWhenReady();
        }
        Updated(before);
    }

    // sender procede de GetRemoteSenderId; jamas del contenido enviado.
    public ReplyDto Receive(long sender, string json, long monotonicMilliseconds)
    {
        if (!_connections.TryGetValue(sender, out Connection? connection))
            return Reject(0, "Conexion no admitida.");
        long second = monotonicMilliseconds / 1000;
        if (connection.RateSecond != second)
        { connection.RateSecond = second; connection.RateCount = 0; }
        if (++connection.RateCount > 30) return Reject(0, "Demasiadas solicitudes.");
        if (!NetworkProtocol.TryDecode<CommandDto>(json, NetworkProtocol.MaxCommandBytes, out var c) ||
            c is null || !NetworkProtocol.ValidCommand(c))
            return Reject(0, "Solicitud mal formada.");
        if (c.Session != SessionId) return Reject(c.RequestId, "Sesion incorrecta.");
        if (c.RequestId <= connection.LastRequest)
            return Reject(c.RequestId, "Solicitud repetida o fuera de orden.");
        // Consumir tambien los identificadores de acciones rechazadas.
        connection.LastRequest = c.RequestId;
        if (c.Window != Window) return Reject(c.RequestId, "La oportunidad de actuar ya cambio.");
        if (c.Kind is ActionKind.StartMatch or ActionKind.PrepareNextMatch && sender != 1)
            return Reject(c.RequestId, "Solo el host puede preparar o iniciar.");

        int actor = connection.PlayerId;
        MeldPlay[] plays = c.Plays?.Select(p => new MeldPlay(p.Cards, p.MeldId)).ToArray()
            ?? Array.Empty<MeldPlay>();
        var before = Context();
        GameActionResult result = c.Kind switch
        {
            ActionKind.StartMatch => _game.StartMatch(_deckFactory?.Invoke()),
            ActionKind.ExchangeCard => _game.SelectExchangeCard(actor, c.CardId!.Value),
            ActionKind.FinishInitial => _game.FinishInitialDeclarations(actor),
            ActionKind.DeclareInitial => _game.DeclareInitialVictory(actor, c.InitialKind!.Value, c.Cards),
            ActionKind.DrawDeck => _game.DrawFromDeck(actor),
            ActionKind.DrawDiscard => _game.DrawFromDiscard(actor, plays, c.DeclareWin),
            ActionKind.Claim => _game.SubmitClaim(actor, plays, c.DeclareWin, c.InitialKind),
            ActionKind.Pass => _game.PassClaim(actor),
            ActionKind.Publish => _game.PublishMelds(actor, plays),
            ActionKind.Transfer => _game.TransferMeldCard(actor, c.SourceMeldId!.Value, c.CardId!.Value, plays[0]),
            ActionKind.DeclareDesmoche => _game.DeclareDesmoche(actor, plays),
            ActionKind.Pay => _game.PayCard(actor, c.CardId!.Value),
            ActionKind.CompleteWithoutPayment => _game.CompleteWithoutPayment(actor),
            ActionKind.PrepareNextMatch => PrepareNext(),
            _ => new GameActionResult(false, "Accion desconocida.")
        };
        ResolveWhenReady();
        // Una declaracion incorrecta puede eliminar al jugador aunque Success=false.
        Updated(before);
        return new ReplyDto(c.RequestId, result.Success, result.Message,
            result.Penalized, Revision);
    }
    // Herramienta local del host. No hay RPC para que clientes expiren reclamos.
    public ReplyDto ExpireClaims()
    {
        var before = Context();
        GameActionResult result = _game.ExpireClaims();
        Updated(before);
        return new ReplyDto(0, result.Success, result.Message, result.Penalized, Revision);
    }
    private GameActionResult PrepareNext()
    {
        GameActionResult result = _game.PrepareNextMatch();
        if (!result.Success) return result;
        foreach (int id in _departed) _game.RemovePreparingPlayer(id);
        _departed.Clear();
        return result;
    }
    private void ResolveWhenReady()
    {
        // Resolver una ventana como maximo: una cadena de pagos abre otra ventana.
        if (_game.ClaimsReady) _game.ResolveClaims();
    }
    private (GamePhase Phase, int? Current, int? Payment, int? Offer) Context() =>
        (_game.Phase, _game.CurrentPlayerId, _game.PaymentPlayerId, _game.State?.OfferedCard?.Id);
    private void Updated((GamePhase Phase, int? Current, int? Payment, int? Offer) before)
    {
        Revision++;
        if (before != Context()) Window++;
    }
    private ReplyDto Reject(long request, string error) => new(request, false, error, false, Revision);

    public SnapshotDto ViewFor(long peer)
    {
        if (!_connections.TryGetValue(peer, out Connection? connection))
            throw new ArgumentException("Conexion no admitida.");
        int recipient = connection.PlayerId;
        GameState.PlayerView? view = _game.State?.CreateViewFor(recipient, _game.WinnerPlayerId is not null);
        var connected = _connections.Values.Select(c => c.PlayerId).ToHashSet();
        bool submitted = _game.Phase switch
        {
            GamePhase.InitialExchange => _game.HasExchanged(recipient),
            GamePhase.InitialDeclarations => _game.HasFinishedInitial(recipient),
            GamePhase.AwaitingClaims => _game.HasAnsweredClaim(recipient),
            _ => false
        };
        return new SnapshotDto(NetworkProtocol.Version, SessionId, Revision, Window, recipient,
            _game.Phase, _game.Result, _game.CurrentPlayerId, _game.PaymentPlayerId, _game.WinnerPlayerId,
            _game.Players.Select(p => new PublicPlayerDto(p.Id, p.Name, p.SeatIndex,
                p.Hand.Count, connected.Contains(p.Id), _game.EliminatedPlayers.Contains(p.Id))).ToArray(),
            view?.OwnHand.Select(c => c.Id).ToArray() ?? Array.Empty<int>(),
            view?.Melds.Select(m => new MeldDto(m.Id, m.OwnerPlayerId,
                m.Cards.Select(c => c.Id).ToArray())).ToArray() ?? Array.Empty<MeldDto>(),
            view?.DeckCount ?? 0, view?.DiscardCount ?? 0, view?.DiscardTop?.Id, view?.OfferedCard?.Id,
            view?.RevealedHands.Select(h => new RevealedHandDto(h.PlayerId,
                h.Cards.Select(c => c.Id).ToArray())).ToArray() ?? Array.Empty<RevealedHandDto>(), submitted);
    }
}
