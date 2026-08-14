using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Roster;

// 名簿・業者マスタで共通の PostgREST 読み取り(#107)。JWT を付与し、401/403 のときは
// token 失効の可能性があるため一度だけ再取得してリトライする
internal sealed class PostgRestReader
{
    private readonly HttpClient _httpClient;
    private readonly ISupabaseSession _session;

    public PostgRestReader(HttpClient httpClient, ISupabaseSession session)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(session);

        _httpClient = httpClient;
        _session = session;
    }

    public async Task<List<T>> ReadArrayAsync<T>(
        Uri requestUri,
        string resourceName,
        CancellationToken cancellationToken)
    {
        string accessToken = await _session.GetAccessTokenAsync(cancellationToken);
        HttpResponseMessage response = await SendAsync(requestUri, accessToken, cancellationToken);
        try
        {
            if (IsAuthorizationFailure(response.StatusCode))
            {
                response.Dispose();
                _session.Invalidate(accessToken);
                accessToken = await _session.GetAccessTokenAsync(cancellationToken);
                response = await SendAsync(requestUri, accessToken, cancellationToken);
            }

            if (IsAuthorizationFailure(response.StatusCode))
            {
                throw new SupabaseAuthenticationException(
                    $"{resourceName}の取得が Supabase に拒否されました。"
                    + "接続設定のアカウントに読み取り権限があるか確認してください。"
                    + $"(HTTP {(int)response.StatusCode})");
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken)
                ?? throw new InvalidDataException($"Supabase の{resourceName}レスポンスが JSON 配列ではありません。");
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("apikey", _session.ApiKey);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static bool IsAuthorizationFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
