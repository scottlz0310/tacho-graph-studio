namespace TachoGraphStudio.Core.Roster;

public interface IRosterFilterSettingsStore
{
    Task<RosterFilterSettings?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(
        RosterFilterSettings settings,
        CancellationToken cancellationToken = default);
}
