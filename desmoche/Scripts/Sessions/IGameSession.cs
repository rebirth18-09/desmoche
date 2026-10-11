using System;
using Desmoche.Domain;
using Desmoche.Networking.Core;

namespace Desmoche.Sessions;

// La mesa conoce solo su vista y puede solicitar acciones. No recibe el motor.
public interface IGameSession
{
    bool IsActive { get; }
    bool IsLocal { get; }
    bool CanManageMatch { get; }
    SnapshotDto? View { get; }
    string Status { get; }
    event Action<SnapshotDto>? ViewChanged;
    event Action<ReplyDto>? ActionCompleted;
    event Action? SessionChanged;
    long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
        bool declareWin = false, InitialVictoryKind? initialKind = null,
        int[]? cards = null, int? sourceMeldId = null);
    void Close(string reason = "Partida cerrada.");
}
