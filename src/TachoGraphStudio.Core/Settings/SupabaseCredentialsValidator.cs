using System.Text.Json;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Settings;

public sealed class SupabaseCredentialsValidator
{
    private readonly HttpClient _httpClient;

    public SupabaseCredentialsValidator(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<bool> IsValidAsync(
        SupabaseCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        PostgRestRosterClient client = new(_httpClient, credentials.ProjectUrl, credentials.AnonKey);
        try
        {
            await client.GetRosterAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException)
        {
            return false;
        }
    }
}
