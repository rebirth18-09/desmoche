using System;
using Desmoche.Networking.Core;

namespace Desmoche.Singleplayer.Core;

public enum BotDifficulty { Easy, Medium, Hard, Expert }

public sealed record LocalMatchOptions(string PlayerName, int BotCount, BotDifficulty Difficulty)
{
    public void Validate()
    {
        if (!NetworkProtocol.ValidName(PlayerName))
            throw new ArgumentException("Escribe un nombre de 1 a 24 caracteres.", nameof(PlayerName));
        if (BotCount is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(BotCount), "Elige de 1 a 3 bots.");
        if (!Enum.IsDefined(Difficulty))
            throw new ArgumentOutOfRangeException(nameof(Difficulty));
    }

    public static string DifficultyName(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => "Fácil",
        BotDifficulty.Medium => "Medio",
        BotDifficulty.Hard => "Difícil",
        BotDifficulty.Expert => "Experto",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };
}
