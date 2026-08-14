using System.Text.Json;

using TachoGraphStudio.Core.Auth;
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

    public async Task<SupabaseConnectionResult> ValidateAsync(
        SupabaseCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using SupabasePasswordSession session = new(
            _httpClient,
            credentials.ProjectUrl,
            credentials.AnonKey,
            credentials.Email,
            credentials.Password);
        PostgRestRosterClient client = new(_httpClient, credentials.ProjectUrl, session);

        try
        {
            await client.GetRosterAsync(cancellationToken);
            return SupabaseConnectionResult.Success;
        }
        catch (SupabaseAuthenticationException exception)
        {
            return SupabaseConnectionResult.Failed(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            string statusSuffix = exception.StatusCode is null
                ? string.Empty
                : $"(HTTP {(int)exception.StatusCode})";
            return SupabaseConnectionResult.Failed(
                $"Supabase に接続できませんでした。プロジェクト URL とネットワークを確認してください。{statusSuffix}");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return SupabaseConnectionResult.Failed(
                "Supabase の応答を解釈できませんでした。プロジェクト URL を確認してください。");
        }
    }
}
