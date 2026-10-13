using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace Desmoche.Networking.Core;

// Descubrimiento sin datos de partida. El codigo identifica una sala, no es una contraseña.
public static class RoomCodeProtocol
{
    public const int DiscoveryPort = 7001;
    public const int MaxPacketBytes = 128;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string CreateCode() => new(Enumerable.Range(0, 6)
        .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
    public static string Normalize(string? code) => (code ?? "").Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
    public static bool IsCode(string? code) => code is { Length: 6 } && code.All(Alphabet.Contains);
    public static string Query(string code, string nonce) => $"DESMOCHE|{NetworkProtocol.Version}|Q|{code}|{nonce}";
    public static string Answer(string code, string nonce, int port) => $"DESMOCHE|{NetworkProtocol.Version}|R|{code}|{nonce}|{port}";
    public static bool TryRead(string text, out bool query, out string code, out string nonce, out int port)
    {
        query = false; code = nonce = ""; port = 0;
        if (text.Length > MaxPacketBytes) return false;
        string[] parts = text.Split('|');
        if (parts.Length is not (5 or 6) || parts[0] != "DESMOCHE" ||
            parts[1] != NetworkProtocol.Version.ToString(CultureInfo.InvariantCulture) ||
            !IsCode(parts[3]) || !Guid.TryParseExact(parts[4], "N", out _)) return false;
        query = parts[2] == "Q";
        if (query ? parts.Length != 5 : parts[2] != "R" || parts.Length != 6 ||
            !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535) return false;
        code = parts[3]; nonce = parts[4];
        return true;
    }
}
