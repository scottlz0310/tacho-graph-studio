namespace TachoGraphStudio.Core.Settings;

public interface ISecretStore
{
    Task<SupabaseCredentials?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(SupabaseCredentials credentials, CancellationToken cancellationToken = default);
}
