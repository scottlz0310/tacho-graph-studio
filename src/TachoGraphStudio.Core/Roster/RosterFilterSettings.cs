namespace TachoGraphStudio.Core.Roster;

public enum RosterSeason
{
    All,
    Winter,
    Summer,
    YearRound,
}

public sealed record RosterFilterSettings
{
    public static RosterFilterSettings Default { get; } = new();

    public RosterSeason Season { get; init; } = RosterSeason.All;

    public bool TachoTargetsOnly { get; init; } = true;
}
