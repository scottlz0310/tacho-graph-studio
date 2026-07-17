namespace TachoGraphStudio.Core.Roster;

public interface IRosterClient
{
    Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default);
}
