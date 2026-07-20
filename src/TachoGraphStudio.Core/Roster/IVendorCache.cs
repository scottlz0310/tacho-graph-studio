namespace TachoGraphStudio.Core.Roster;

public interface IVendorCache
{
    Task<VendorResult?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(VendorResult vendors, CancellationToken cancellationToken = default);
}
