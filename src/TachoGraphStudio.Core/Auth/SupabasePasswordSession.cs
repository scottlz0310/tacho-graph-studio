using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TachoGraphStudio.Core.Auth;

// Supabase Auth のパスワードグラントで access token を取得・更新するセッション(#107)。
// 名簿・業者マスタの読み取りは authenticated ロールを要求するため anon キー単独では通らない。
//
// 破棄を必要としない設計にしている。token 取得中(gate 保持中)にウィンドウ終了や接続設定の
// 切替で破棄されると finally の Release が ObjectDisposedException になるが、呼び出し元は
// 同期の Closed ハンドラから来るため取得の完了を待てない。SemaphoreSlim の Dispose が要る
// のは AvailableWaitHandle を使った場合だけで、本クラスは触っていない
public sealed class SupabasePasswordSession : ISupabaseSession
{
    // token の有効期限ぎりぎりの再利用で 401 になるのを避けるための前倒し幅
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly string _email;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly string _password;
    private readonly TimeProvider _timeProvider;
    private readonly Uri _tokenUri;

    private TokenState? _state;

    public SupabasePasswordSession(
        HttpClient httpClient,
        Uri projectUrl,
        string apiKey,
        string email,
        string password,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(projectUrl);

        if (!projectUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supabase project URL は絶対 URI で指定してください。", nameof(projectUrl));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Supabase anon key を指定してください。", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Supabase のメールアドレスを指定してください。", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Supabase のパスワードを指定してください。", nameof(password));
        }

        _httpClient = httpClient;
        _email = email;
        _password = password;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ApiKey = apiKey;

        Uri baseUri = new(projectUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        _tokenUri = new Uri(baseUri, "auth/v1/token");
    }

    public string ApiKey { get; }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        TokenState? cached = Volatile.Read(ref _state);
        if (IsUsable(cached))
        {
            return cached.AccessToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 待機中に別の呼び出しが更新している場合があるため再確認する
            cached = Volatile.Read(ref _state);
            if (IsUsable(cached))
            {
                return cached.AccessToken;
            }

            TokenState acquired = await AcquireAsync(cached?.RefreshToken, cancellationToken);
            Volatile.Write(ref _state, acquired);
            return acquired.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(string accessToken)
    {
        TokenState? current = Volatile.Read(ref _state);
        if (current is null || !string.Equals(current.AccessToken, accessToken, StringComparison.Ordinal))
        {
            return;
        }

        // refresh token は残したまま期限切れ扱いにし、次回取得でまず更新を試みさせる。
        // 既に別の呼び出しが差し替えていた場合は何もしない
        Interlocked.CompareExchange(
            ref _state,
            current with { ExpiresAt = DateTimeOffset.MinValue },
            current);
    }

    private bool IsUsable([NotNullWhen(true)] TokenState? state) =>
        state is not null && _timeProvider.GetUtcNow() + ExpiryMargin < state.ExpiresAt;

    private async Task<TokenState> AcquireAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (refreshToken is not null)
        {
            try
            {
                return await RequestTokenAsync(
                    "refresh_token",
                    new RefreshTokenGrant(refreshToken),
                    cancellationToken);
            }
            catch (SupabaseAuthenticationException)
            {
                // refresh token が失効している場合はパスワードで再サインインする
            }
        }

        return await RequestTokenAsync("password", new PasswordGrant(_email, _password), cancellationToken);
    }

    private async Task<TokenState> RequestTokenAsync<TGrant>(
        string grantType,
        TGrant grant,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(_tokenUri, $"?grant_type={grantType}"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("apikey", ApiKey);
        request.Content = JsonContent.Create(grant);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

        if (IsCredentialRejection(response.StatusCode))
        {
            throw new SupabaseAuthenticationException(
                "Supabase の認証に失敗しました。接続設定のメールアドレスとパスワードを確認してください。"
                + $"(HTTP {(int)response.StatusCode})");
        }

        response.EnsureSuccessStatusCode();

        TokenResponse? token;
        try
        {
            token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new SupabaseAuthenticationException(
                "Supabase の認証レスポンスを解釈できませんでした。プロジェクト URL を確認してください。",
                exception);
        }

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new SupabaseAuthenticationException(
                "Supabase の認証レスポンスに access token が含まれていませんでした。");
        }

        return new TokenState(
            token.AccessToken,
            token.RefreshToken,
            _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(token.ExpiresIn));
    }

    // 資格情報の誤りは 400(invalid_grant) で返るため、401/403 と同じく再入力を促す扱いにする。
    // 5xx や 429 は HttpRequestException のまま伝播させ、キャッシュフォールバックへ委ねる
    private static bool IsCredentialRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.UnprocessableEntity;

    private sealed record TokenState(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record PasswordGrant(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password);

    private sealed record RefreshTokenGrant(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
