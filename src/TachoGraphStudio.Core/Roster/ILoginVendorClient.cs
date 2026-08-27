namespace TachoGraphStudio.Core.Roster;

public interface ILoginVendorClient
{
    Task<IReadOnlyList<LoginVendor>> GetLoginVendorsAsync(
        Uri projectUrl,
        string anonKey,
        CancellationToken cancellationToken = default);
}
