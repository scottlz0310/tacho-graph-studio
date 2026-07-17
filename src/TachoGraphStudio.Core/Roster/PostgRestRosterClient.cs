using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TachoGraphStudio.Core.Roster;

public sealed class PostgRestRosterClient : IRosterClient
{
    private const string SelectColumns =
        "ctrl_num,detail,spec,vehicle_num,vehicle_type,driver,work_period,updated_at,is_tacho_target";

    private readonly string _anonKey;
    private readonly HttpClient _httpClient;
    private readonly Uri _requestUri;
    private readonly TimeProvider _timeProvider;

    public PostgRestRosterClient(
        HttpClient httpClient,
        Uri projectUrl,
        string anonKey,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        if (string.IsNullOrWhiteSpace(anonKey))
        {
            throw new ArgumentException("Supabase anon key を指定してください。", nameof(anonKey));
        }

        _httpClient = httpClient;
        _anonKey = anonKey;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestUri = BuildRequestUri(projectUrl);
    }

    public async Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, _requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _anonKey);
        request.Headers.Add("apikey", _anonKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        List<RosterEntry>? entries = await response.Content.ReadFromJsonAsync<List<RosterEntry>>(
            cancellationToken);
        if (entries is null)
        {
            throw new InvalidDataException("Supabase の名簿レスポンスが JSON 配列ではありません。");
        }

        return new RosterResult(entries, RosterDataSource.Remote, _timeProvider.GetUtcNow());
    }

    private static Uri BuildRequestUri(Uri projectUrl)
    {
        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(
            baseUri,
            $"rest/v1/machine_picklist?select={SelectColumns}&order=ctrl_num.asc");
    }
}
