using Desmoche.Domain;
using Desmoche.Networking.Core;

namespace Desmoche.Singleplayer.Core;

// Una intención, nunca una modificación del estado. También se valida por la autoridad local.
public sealed record BotAction(ActionKind Kind, int? CardId = null,
    PlayDto[]? Plays = null, bool DeclareWin = false,
    InitialVictoryKind? InitialKind = null, int[]? Cards = null, int? SourceMeldId = null)
{
    public CommandDto ToCommand(SnapshotDto view, long requestId) => new(
        NetworkProtocol.Version, view.Session, requestId, view.Window, Kind,
        CardId, SourceMeldId, InitialKind, DeclareWin, Cards, Plays);
}

// Ambos indicadores se deducen de las acciones públicas, nunca del mazo oculto.
public readonly record struct BotPublicContext(bool OpeningOffer = false, bool MustDrawDeck = false);
