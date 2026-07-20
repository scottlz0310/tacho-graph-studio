using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TachoGraphStudio.Core.Roster;

public sealed class PostgRestVendorClient : IVendorClient
{
    // vendor_ctrl_num_ranges を embed し、閲覧フィルター用(purpose='view')の範囲のみ取得する。
    // is_active=false の業者は UI の選択肢から除外する仕様のためサーバー側で絞る
    private const string Query =
        "select=code,display_name,ranges:vendor_ctrl_num_ranges(min_ctrl_num,max_ctrl_num)"
        + "&ranges.purpose=eq.view&is_active=eq.true&order=sort_order.asc";

    private readonly string _anonKey;
    private readonly HttpClient _httpClient;
    private readonly Uri _requestUri;
    private readonly TimeProvider _timeProvider;

    public PostgRestVendorClient(
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

    public async Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default)
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

        List<VendorEntry>? vendors = await response.Content.ReadFromJsonAsync<List<VendorEntry>>(
            cancellationToken);
        if (vendors is null)
        {
            throw new InvalidDataException("Supabase の業者マスタレスポンスが JSON 配列ではありません。");
        }

        return new VendorResult(vendors, RosterDataSource.Remote, _timeProvider.GetUtcNow());
    }

    private static Uri BuildRequestUri(Uri projectUrl)
    {
        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, $"rest/v1/vendors?{Query}");
    }
}
