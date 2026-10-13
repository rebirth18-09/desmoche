using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Sessions;
using Desmoche.Singleplayer.Core;
using Godot;

namespace Desmoche.Singleplayer;

// Adaptador de Godot: mismo contrato que LAN, sin crear peers, sockets ni RPC.
public partial class LocalGameSession : Node, IGameSession
{
    private LocalMatchController? _controller;
    private readonly Queue<CommandDto> _commands = new();
    private long _nextRequest;
    private bool _restartRequested;
    private ulong _beganAt;
    public bool IsActive => _controller?.IsActive == true;
    public bool IsLocal => true;
    public bool CanManageMatch => IsActive;
    public SnapshotDto? View => IsActive ? _controller!.View : null;
    public string Status { get; private set; } = "Sin partida local.";
    public bool OpeningOffer => _controller?.OpeningOffer == true;
    public int BotDelayMilliseconds { get; set; } = 650;
    public event Action<SnapshotDto>? ViewChanged;
    public event Action<ReplyDto>? ActionCompleted;
    public event Action? SessionChanged;

    public void Start(LocalMatchOptions options, int? seed = null)
    {
        if (IsActive) throw new InvalidOperationException("Ya hay una partida local en curso.");
        var controller = new LocalMatchController(options, seed);
        _commands.Clear();
        _restartRequested = false;
        _controller = controller;
        _beganAt = Time.GetTicksMsec();
        _nextRequest = controller.NextHumanRequestId - 1;
        Status = controller.Status;
        SessionChanged?.Invoke();
        if (View is { } view) ViewChanged?.Invoke(view);
    }

    public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
        bool declareWin = false, InitialVictoryKind? initialKind = null,
        int[]? cards = null, int? sourceMeldId = null)
    {
        if (!IsActive || View is not { } view || _commands.Count >= 32 || _restartRequested) return 0;
        // Copias defensivas: cambiar un borrador después de enviarlo no cambia la acción.
        var command = new CommandDto(NetworkProtocol.Version, view.Session, ++_nextRequest,
            view.Window, kind, cardId, sourceMeldId, initialKind, declareWin,
            cards?.ToArray(), plays?.Select(p => new PlayDto(p.Cards.ToArray(), p.MeldId)).ToArray());
        _commands.Enqueue(command);
        return command.RequestId;
    }

    public void Restart()
    {
        if (IsActive && View?.Phase == Application.GamePhase.Finished && _commands.Count == 0)
            _restartRequested = true;
    }

    public override void _Process(double delta)
    {
        LocalMatchController? controller = _controller;
        if (controller is null || !controller.IsActive) return;
        long now = (long)(Time.GetTicksMsec() - _beganAt);
        controller.BotDelayMilliseconds = Math.Max(0, BotDelayMilliseconds);
        if (_restartRequested)
        {
            _restartRequested = false;
            controller.Restart(now);
            _nextRequest = controller.NextHumanRequestId - 1;
            Status = controller.Status;
            ViewChanged?.Invoke(controller.View);
            return;
        }
        if (_commands.TryDequeue(out CommandDto? command))
        {
            ReplyDto reply = controller.Submit(command, now);
            Status = controller.Status;
            ViewChanged?.Invoke(controller.View);
            // Submit siempre devuelve el ID antes de recibir este evento.
            if (ReferenceEquals(controller, _controller)) ActionCompleted?.Invoke(reply);
            return;
        }
        if (controller.Tick(now))
        {
            Status = controller.Status;
            ViewChanged?.Invoke(controller.View);
        }
    }

    public void Close(string reason = "Partida local cerrada.")
    {
        _controller?.Close();
        _controller = null;
        _commands.Clear();
        _restartRequested = false;
        _nextRequest = 0;
        Status = reason;
        SessionChanged?.Invoke();
    }

    public override void _ExitTree() => Close();
}
