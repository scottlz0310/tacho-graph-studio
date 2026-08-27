using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Roster;

// 認証前に業者を選択するための最小ビュー(login_vendors)だけを anon で読む。
// URL と anon キーは利用者が接続設定へ入力した後にだけ、このクライアントへ渡される。
public sealed class PostgRestLoginVendorClient : ILoginVendorClient
{
    private const string Query =
        "select=code,display_name,sort_order&order=sort_order.asc";

    private readonly HttpClient _httpClient;

    public PostgRestLoginVendorClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<LoginVendor>> GetLoginVendorsAsync(
        Uri projectUrl,
        string anonKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        if (projectUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Supabase project URL は https で指定してください。", nameof(projectUrl));
        }

        if (string.IsNullOrWhiteSpace(anonKey))
        {
            throw new ArgumentException("Supabase anon key を指定してください。", nameof(anonKey));
        }

        Uri requestUri = BuildRequestUri(projectUrl);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("apikey", anonKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SupabaseAuthenticationException(
                "Supabase のログイン用業者一覧の取得が拒否されました。"
                + "プロジェクト URL と anon キーを確認してください。"
                + $"(HTTP {(int)response.StatusCode})");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LoginVendor>>(cancellationToken)
            ?? throw new InvalidDataException(
                "Supabase のログイン用業者一覧レスポンスが JSON 配列ではありません。");
    }

    private static Uri BuildRequestUri(Uri projectUrl)
    {
        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, $"rest/v1/login_vendors?{Query}");
    }
}
