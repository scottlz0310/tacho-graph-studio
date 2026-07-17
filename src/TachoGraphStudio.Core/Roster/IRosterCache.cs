namespace TachoGraphStudio.Core.Roster;

public interface IRosterCache
{
    Task<RosterResult?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(RosterResult roster, CancellationToken cancellationToken = default);
}
