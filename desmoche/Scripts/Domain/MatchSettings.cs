using System;

namespace Desmoche.Domain;

/// <summary>Opciones inmutables de la mesa, compartidas por humanos y bots.</summary>
public sealed record MatchSettings(
    int CardChoiceSeconds = 5,
    int CardPreparationSeconds = 20,
    int PasaSeconds = 3,
    int RematchSeconds = 15,
    bool LowStraightEnabled = true,
    bool HighStraightEnabled = true,
    bool CuartaEnabled = true,
    bool ColorEnabled = true,
    bool PeladillaEnabled = true)
{
    public static MatchSettings Default { get; } = new();

    public bool IsValid() => CardChoiceSeconds is >= 1 and <= 120 &&
        CardPreparationSeconds is >= 5 and <= 300 && PasaSeconds is >= 1 and <= 30 &&
        RematchSeconds is >= 5 and <= 120;

    public void Validate()
    {
        if (!IsValid()) throw new ArgumentException("Los tiempos de la partida están fuera de los límites permitidos.");
    }

    public bool Allows(InitialVictoryKind kind) => kind switch
    {
        InitialVictoryKind.LowStraight => LowStraightEnabled,
        InitialVictoryKind.HighStraight => HighStraightEnabled,
        InitialVictoryKind.Cuarta => CuartaEnabled,
        InitialVictoryKind.Color => ColorEnabled,
        InitialVictoryKind.Peladilla => PeladillaEnabled,
        _ => false
    };
}
