namespace TachoGraphStudio.Core.Settings;

public static class SecretStoreExtensions
{
    public static async Task<bool> TryWriteAsync(
        this ISecretStore secretStore,
        SupabaseCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretStore);

        try
        {
            await secretStore.WriteAsync(credentials, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
