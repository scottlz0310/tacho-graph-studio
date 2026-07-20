namespace TachoGraphStudio.Core.Roster;

public interface IVendorClient
{
    Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default);
}
