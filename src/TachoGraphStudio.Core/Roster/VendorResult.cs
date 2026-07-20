namespace TachoGraphStudio.Core.Roster;

public sealed record VendorResult(
    IReadOnlyList<VendorEntry> Vendors,
    RosterDataSource Source,
    DateTimeOffset RetrievedAt);
