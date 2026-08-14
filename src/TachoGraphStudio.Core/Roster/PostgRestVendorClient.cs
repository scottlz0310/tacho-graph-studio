using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Roster;

public sealed class PostgRestVendorClient : IVendorClient
{
    // vendor_ctrl_num_ranges を embed し、閲覧フィルター用(purpose='view')の範囲のみ取得する。
    // is_active=false の業者は UI の選択肢から除外する仕様のためサーバー側で絞る
    private const string Query =
        "select=code,display_name,ranges:vendor_ctrl_num_ranges(min_ctrl_num,max_ctrl_num)"
        + "&ranges.purpose=eq.view&is_active=eq.true&order=sort_order.asc";

    private readonly PostgRestReader _reader;
    private readonly Uri _requestUri;
    private readonly TimeProvider _timeProvider;

    public PostgRestVendorClient(
        HttpClient httpClient,
        Uri projectUrl,
        ISupabaseSession session,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        _reader = new PostgRestReader(httpClient, session);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestUri = BuildRequestUri(projectUrl);
    }

    public async Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default)
    {
        List<VendorEntry> vendors = await _reader.ReadArrayAsync<VendorEntry>(
            _requestUri,
            "業者マスタ",
            cancellationToken);

        return new VendorResult(vendors, RosterDataSource.Remote, _timeProvider.GetUtcNow());
    }

    private static Uri BuildRequestUri(Uri projectUrl)
    {
        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, $"rest/v1/vendors?{Query}");
    }
}
