using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;

namespace Desmoche.Singleplayer.Core;

// Simulación local pura: reutiliza la autoridad y el contrato de acciones; no abre
// sockets, no crea peers ENet y no usa Godot. Cada bot recibe únicamente su vista.
public sealed class LocalMatchController
{
    private readonly ServerSession _authority;
    private readonly Dictionary<int, BotBrain> _bots = new();
    private readonly Dictionary<int, long> _botRequests = new();
    private SnapshotDto _view;
    private bool _firstDrawPending;
    private long _nextBotAt;
    private int _nextBotIndex;
    private long _lastClock;
    public LocalMatchOptions Options { get; }
    public SnapshotDto View => Copy(_view);
    public bool IsActive { get; private set; } = true;
    public string Status { get; private set; } = "Partida local contra bots.";
    public int BotDelayMilliseconds { get; set; } = 650;
    public long LastHumanRequestId { get; private set; } = 1;
    public long NextHumanRequestId => LastHumanRequestId + 1;
    public int BotRejectedActions { get; private set; }
    public string LastBotError { get; private set; } = "";
    public bool OpeningOffer { get; private set; }
    public bool MustDrawDeck { get; private set; }
    public BotPublicContext PublicContext => new(OpeningOffer, MustDrawDeck);

    public LocalMatchController(LocalMatchOptions options, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options with { PlayerName = options.PlayerName.Trim() };
        // La semilla de pruebas llega solo a la autoridad. Las estrategias usan
        // semillas independientes: no pueden reconstruir el barajado.
        Random? deckRandom = seed is int value ? new Random(value) : null;
        _authority = new ServerSession(Options.PlayerName,
            deckRandom is null ? null : () => Deck.CreateStandard().Shuffle(deckRandom));
        string[] names = { "Bot Alba", "Bot Bruno", "Bot Cora" };
        for (int index = 0; index < Options.BotCount; index++)
        {
            int id = index + 2;
            if (!_authority.Join(id, NetworkProtocol.Version, names[index], out string error))
                throw new InvalidOperationException(error);
            _bots.Add(id, new BotBrain(Options.Difficulty, 104729 * id));
            _botRequests.Add(id, 0);
        }
        _view = _authority.ViewFor(1);
        ReplyDto started = Send(1, new BotAction(ActionKind.StartMatch).ToCommand(_view, 1), 0);
        if (!started.Success) throw new InvalidOperationException(started.Message);
        _nextBotAt = BotDelayMilliseconds;
    }

    public ReplyDto Submit(CommandDto command, long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsActive) return new ReplyDto(command.RequestId, false, "La partida local está cerrada.", false, _view.Revision);
        LastHumanRequestId = Math.Max(LastHumanRequestId, command.RequestId);
        return Send(1, command, Clock(nowMilliseconds));
    }

    // Un paso por llamada y una pausa entre decisiones. Nunca espera, duerme ni
    // responde en nombre del usuario. Un jugador eliminado puede observar el resto.
    public bool Tick(long nowMilliseconds)
    {
        if (!IsActive || _view.Phase == GamePhase.Finished) return false;
        long now = Clock(nowMilliseconds);
        if (now < _nextBotAt) return false;
        int[] ids = _bots.Keys.OrderBy(id => id).ToArray();
        for (int offset = 0; offset < ids.Length; offset++)
        {
            int index = (_nextBotIndex + offset) % ids.Length;
            int id = ids[index];
            SnapshotDto privateView = _authority.ViewFor(id);
            BotAction? action = _bots[id].Decide(privateView, PublicContext);
            if (action is null) continue;
            long visibleRevision = _view.Revision;
            ReplyDto reply = Send(id, action.ToCommand(privateView, ++_botRequests[id]), now);
            if (!reply.Success || reply.Penalized)
            {
                BotRejectedActions++;
                LastBotError = $"{privateView.Players.Single(p => p.Id == id).Name}: {action.Kind}: {reply.Message}";
                // No se altera el motor ni se fuerzan cartas. Se conserva el error
                // para diagnóstico y la próxima decisión consulta el estado actual.
            }
            _nextBotIndex = (index + 1) % ids.Length;
            _nextBotAt = now + Math.Max(0, BotDelayMilliseconds);
            return _view.Revision != visibleRevision;
        }
        return false;
    }

    public void Restart(long nowMilliseconds)
    {
        if (!IsActive || _view.Phase != GamePhase.Finished)
            throw new InvalidOperationException("Primero debe terminar la partida.");
        long now = Clock(nowMilliseconds);
        ReplyDto prepared = Submit(new BotAction(ActionKind.PrepareNextMatch)
            .ToCommand(_authority.ViewFor(1), NextHumanRequestId), now);
        if (!prepared.Success) throw new InvalidOperationException(prepared.Message);
        ReplyDto started = Submit(new BotAction(ActionKind.StartMatch)
            .ToCommand(_authority.ViewFor(1), NextHumanRequestId), now);
        if (!started.Success) throw new InvalidOperationException(started.Message);
        _nextBotAt = now + Math.Max(0, BotDelayMilliseconds);
    }

    public void Close()
    {
        IsActive = false;
        Status = "Partida local finalizada.";
    }

    private ReplyDto Send(int actor, CommandDto command, long now)
    {
        SnapshotDto before = _authority.ViewFor(1);
        ReplyDto reply = _authority.Receive(actor, NetworkProtocol.Encode(command), now);
        SnapshotDto after = _authority.ViewFor(1);
        UpdatePublicContext(before, after, command, reply);
        foreach ((int id, BotBrain brain) in _bots) brain.Observe(_authority.ViewFor(id));
        // Una respuesta privada de otro jugador no cambia el estado presentado.
        // Conservamos la revisión expuesta para no cancelar un arrastre o borrador.
        bool changed = NetworkProtocol.Encode(_view with { Revision = after.Revision }) != NetworkProtocol.Encode(after);
        if (changed)
        {
            _view = after;
            Status = after.Phase == GamePhase.Finished ? "Partida local terminada." :
                $"Singleplayer · {LocalMatchOptions.DifficultyName(Options.Difficulty)} · {Options.BotCount} {(Options.BotCount == 1 ? "bot" : "bots")}";
        }
        return reply;
    }

    private void UpdatePublicContext(SnapshotDto before, SnapshotDto after, CommandDto command, ReplyDto reply)
    {
        if (after.Phase == GamePhase.Preparing && before.Phase != GamePhase.Preparing)
        {
            _firstDrawPending = OpeningOffer = MustDrawDeck = false;
            foreach (BotBrain brain in _bots.Values) brain.Reset();
        }
        if (before.Phase == GamePhase.InitialDeclarations && after.Phase == GamePhase.AwaitingDraw)
            _firstDrawPending = true;
        if (OpeningOffer && (after.Phase != GamePhase.AwaitingClaims || before.OfferedCard != after.OfferedCard))
        {
            MustDrawDeck = after.Phase == GamePhase.AwaitingDraw && after.DiscardTop == before.OfferedCard;
            OpeningOffer = false;
        }
        if (reply.Success && command.Kind == ActionKind.DrawDeck)
        {
            MustDrawDeck = false;
            OpeningOffer = _firstDrawPending;
            _firstDrawPending = false;
        }
        if (reply.Success && command.Kind == ActionKind.DrawDiscard) _firstDrawPending = false;
    }

    private long Clock(long now)
    {
        _lastClock = Math.Max(_lastClock, Math.Max(0, now));
        return _lastClock;
    }

    private static SnapshotDto Copy(SnapshotDto view) => view with
    {
        Players = view.Players.ToArray(), OwnHand = view.OwnHand.ToArray(),
        Melds = view.Melds.Select(m => m with { Cards = m.Cards.ToArray() }).ToArray(),
        RevealedHands = view.RevealedHands.Select(h => h with { Cards = h.Cards.ToArray() }).ToArray()
    };
}
