using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Desmoche.Application;
using Desmoche.Domain;

namespace Desmoche.Networking.Core;

public enum ActionKind
{
    StartMatch, ExchangeCard, FinishInitial, DeclareInitial, DrawDeck,
    DrawDiscard, Claim, Pass, Publish, Transfer, DeclareDesmoche, Pay,
    CompleteWithoutPayment, PrepareNextMatch
}
public sealed record AdmissionDto(int Version, string Session, int RecipientId);
public sealed record PlayDto(int[] Cards, int? MeldId = null);
public sealed record CommandDto(int Version, string Session, long RequestId,
    long Window, ActionKind Kind, int? CardId = null, int? SourceMeldId = null,
    InitialVictoryKind? InitialKind = null, bool DeclareWin = false,
    int[]? Cards = null, PlayDto[]? Plays = null);
public sealed record ReplyDto(long RequestId, bool Success, string Message,
    bool Penalized, long Revision);
public sealed record PublicPlayerDto(int Id, string Name, int SeatIndex,
    int CardCount, bool Connected, bool Eliminated);
public sealed record MeldDto(int Id, int OwnerId, int[] Cards);
public sealed record RevealedHandDto(int PlayerId, int[] Cards);
public sealed record SnapshotDto(int Version, string Session, long Revision,
    long Window, int RecipientId, GamePhase Phase, MatchResult Result,
    int? CurrentPlayerId, int? PaymentPlayerId, int? WinnerPlayerId,
    PublicPlayerDto[] Players, int[] OwnHand, MeldDto[] Melds,
    int DeckCount, int DiscardCount, int? DiscardTop, int? OfferedCard,
    RevealedHandDto[] RevealedHands, bool OwnResponseSubmitted);

// Contrato de transporte explicito: nunca serializar GameState ni Player.
public static class NetworkProtocol
{
    public const int Version = 2;
    public const int Port = 7000;
    public const int MaxCommandBytes = 8192;
    public const int MaxSnapshotBytes = 32768;
    public const int MaxPublicBytes = 16384;
    public const int MaxPrivateBytes = 4096;
    public const int MaxNameLength = 24;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static bool TryDecode<T>(string? json, int limit, out T? value) where T : class
    {
        value = null;
        if (json is null || json.Length > limit || Encoding.UTF8.GetByteCount(json) > limit)
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = 8 });
            if (!UniqueProperties(document.RootElement)) return false;
            value = JsonSerializer.Deserialize<T>(json, Options);
            return value is not null;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }
    private static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty p in value.EnumerateObject())
                if (!names.Add(p.Name) || !UniqueProperties(p.Value)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray())
                if (!UniqueProperties(item)) return false;
        return true;
    }
    public static bool ValidName(string? name) => name is not null &&
        name.Trim().Length is > 0 and <= MaxNameLength && name.Length <= MaxNameLength &&
        !name.Any(char.IsControl);

    public static bool ValidCommand(CommandDto c)
    {
        if (c.Version != Version || c.Session is null || c.Session.Length != 32 ||
            c.RequestId <= 0 || c.Window <= 0 || !Enum.IsDefined(c.Kind) ||
            c.InitialKind is InitialVictoryKind k && !Enum.IsDefined(k)) return false;
        bool card = c.Kind is ActionKind.ExchangeCard or ActionKind.Transfer or ActionKind.Pay;
        bool source = c.Kind == ActionKind.Transfer;
        bool initial = c.Kind is ActionKind.DeclareInitial or ActionKind.Claim;
        bool win = c.Kind is ActionKind.DrawDiscard or ActionKind.Claim;
        bool cards = c.Kind == ActionKind.DeclareInitial;
        bool plays = c.Kind is ActionKind.DrawDiscard or ActionKind.Claim or
            ActionKind.Publish or ActionKind.Transfer or ActionKind.DeclareDesmoche;
        if (card != c.CardId.HasValue || c.CardId is < 0 or > 51 ||
            source != c.SourceMeldId.HasValue || c.SourceMeldId is <= 0 ||
            !initial && c.InitialKind is not null || !win && c.DeclareWin ||
            !cards && c.Cards is not null || !plays && c.Plays is not null) return false;
        if (c.Kind == ActionKind.DeclareInitial && c.InitialKind is null) return false;
        if (c.InitialKind is not null && c.DeclareWin) return false;
        if (c.Cards is not null && !ValidCards(c.Cards, 1, 9)) return false;
        if (plays)
        {
            int min = c.Kind == ActionKind.DeclareDesmoche ? 0 : 1;
            if (c.Plays is null) return min == 0;
            if (c.Plays.Length < min || c.Plays.Length > 17 ||
                c.Kind == ActionKind.Transfer && c.Plays.Length != 1) return false;
            if (c.Plays.Any(p => p is null || p.MeldId is <= 0 ||
                !ValidCards(p.Cards, 3, 13))) return false;
            var ids = c.Plays.SelectMany(p => p.Cards).ToArray();
            if (ids.Length > 52 || ids.Distinct().Count() != ids.Length) return false;
        }
        return true;
    }
    private static bool ValidCards(int[]? ids, int min, int max) =>
        ids is not null && ids.Length >= min && ids.Length <= max &&
        ids.All(id => id >= 0 && id < 52) && ids.Distinct().Count() == ids.Length;
}