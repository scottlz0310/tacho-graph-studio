namespace TachoGraphStudio.Core.Roster;

public enum RosterDataSource
{
    Remote,
    Cache,
}

public sealed record RosterResult(
    IReadOnlyList<RosterEntry> Entries,
    RosterDataSource Source,
    DateTimeOffset RetrievedAt);
