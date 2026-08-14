using System.Net;
using System.Text;

using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Tests.Auth;

public sealed class SupabasePasswordSessionTests
{
    private static readonly DateTimeOffset SignInAt = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public async Task GetAccessTokenAsync_SignsInWithPasswordGrant(string projectUrl)
    {
        RecordingHandler handler = new(_ => TokenResponse("token-1", "refresh-1", expiresIn: 3600));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient, projectUrl);

        string accessToken = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", accessToken);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/auth/v1/token?grant_type=password", request.PathAndQuery);
        Assert.Equal("test-anon-key", request.ApiKey);
        Assert.Contains("\"email\":\"shared@example.com\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"test-password\"", request.Body, StringComparison.Ordinal);
        Assert.Null(request.AuthorizationScheme);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReusesCachedTokenUntilExpiry()
    {
        RecordingHandler handler = new(_ => TokenResponse("token-1", "refresh-1", expiresIn: 3600));
        using HttpClient httpClient = new(handler);
        MutableTimeProvider timeProvider = new(SignInAt);
        SupabasePasswordSession session = CreateSession(httpClient, timeProvider: timeProvider);

        Assert.Equal("token-1", await session.GetAccessTokenAsync(CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal("token-1", await session.GetAccessTokenAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ExpiredTokenIsRenewedWithRefreshGrant()
    {
        int callCount = 0;
        RecordingHandler handler = new(_ => ++callCount == 1
            ? TokenResponse("token-1", "refresh-1", expiresIn: 3600)
            : TokenResponse("token-2", "refresh-2", expiresIn: 3600));
        using HttpClient httpClient = new(handler);
        MutableTimeProvider timeProvider = new(SignInAt);
        SupabasePasswordSession session = CreateSession(httpClient, timeProvider: timeProvider);

        await session.GetAccessTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));
        string renewed = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", renewed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/auth/v1/token?grant_type=refresh_token", handler.Requests[1].PathAndQuery);
        Assert.Contains("\"refresh_token\":\"refresh-1\"", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RejectedRefreshTokenFallsBackToPasswordGrant()
    {
        int callCount = 0;
        RecordingHandler handler = new(_ => ++callCount switch
        {
            1 => TokenResponse("token-1", "refresh-1", expiresIn: 3600),
            2 => JsonResponse(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }"""),
            _ => TokenResponse("token-3", "refresh-3", expiresIn: 3600),
        });
        using HttpClient httpClient = new(handler);
        MutableTimeProvider timeProvider = new(SignInAt);
        SupabasePasswordSession session = CreateSession(httpClient, timeProvider: timeProvider);

        await session.GetAccessTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));
        string renewed = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-3", renewed);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/auth/v1/token?grant_type=refresh_token", handler.Requests[1].PathAndQuery);
        Assert.Equal("/auth/v1/token?grant_type=password", handler.Requests[2].PathAndQuery);
    }

    [Fact]
    public async Task Invalidate_MatchingTokenForcesRenewal()
    {
        int callCount = 0;
        RecordingHandler handler = new(_ => ++callCount == 1
            ? TokenResponse("token-1", "refresh-1", expiresIn: 3600)
            : TokenResponse("token-2", "refresh-2", expiresIn: 3600));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        string first = await session.GetAccessTokenAsync(CancellationToken.None);
        session.Invalidate(first);
        string second = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/auth/v1/token?grant_type=refresh_token", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Invalidate_StaleTokenKeepsCurrentToken()
    {
        RecordingHandler handler = new(_ => TokenResponse("token-1", "refresh-1", expiresIn: 3600));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        await session.GetAccessTokenAsync(CancellationToken.None);
        session.Invalidate("token-0");
        string current = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", current);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task GetAccessTokenAsync_CredentialRejectionThrowsAuthenticationException(
        HttpStatusCode statusCode)
    {
        const string responseBody = """{ "error_description": "Invalid login credentials" }""";
        RecordingHandler handler = new(_ => JsonResponse(statusCode, responseBody));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        SupabaseAuthenticationException exception =
            await Assert.ThrowsAsync<SupabaseAuthenticationException>(
                () => session.GetAccessTokenAsync(CancellationToken.None));

        Assert.Contains("メールアドレスとパスワード", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(responseBody, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetAccessTokenAsync_ServerErrorThrowsHttpRequestException(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "error"));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => session.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Theory]
    [InlineData("""{ "refresh_token": "refresh-1", "expires_in": 3600 }""")]
    [InlineData("""{ "access_token": "", "expires_in": 3600 }""")]
    public async Task GetAccessTokenAsync_MissingAccessTokenThrowsAuthenticationException(string responseJson)
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        await Assert.ThrowsAsync<SupabaseAuthenticationException>(
            () => session.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAccessTokenAsync_MalformedJsonThrowsAuthenticationException()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "<html>not json</html>"));
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        await Assert.ThrowsAsync<SupabaseAuthenticationException>(
            () => session.GetAccessTokenAsync(CancellationToken.None));
    }

    // ウィンドウ終了・接続設定切替は token 取得中(gate 保持中)でも起こり、同期の Closed
    // ハンドラは取得の完了を待てない。破棄を必要とする設計だと finally の Release が
    // ObjectDisposedException になるため、破棄不要であることを契約として固定する(PR #108 レビュー指摘)
    [Fact]
    public void Session_DoesNotRequireDisposal()
    {
        using HttpClient httpClient = new();

        SupabasePasswordSession session = CreateSession(httpClient);

        Assert.IsNotAssignableFrom<IDisposable>(session);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConcurrentCallsShareOneSignIn()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;
        GatedHandler handler = new(async () =>
        {
            Interlocked.Increment(ref requestCount);
            entered.TrySetResult();
            await release.Task;
            return TokenResponse("token-1", "refresh-1", expiresIn: 3600);
        });
        using HttpClient httpClient = new(handler);
        SupabasePasswordSession session = CreateSession(httpClient);

        Task<string> first = session.GetAccessTokenAsync(CancellationToken.None);
        // 1 本目が gate を保持して HTTP 待ちに入ってから 2 本目を開始する
        await entered.Task;
        Task<string> second = session.GetAccessTokenAsync(CancellationToken.None);
        release.SetResult();

        string[] tokens = await Task.WhenAll(first, second);

        Assert.Equal(["token-1", "token-1"], tokens);
        Assert.Equal(1, requestCount);
    }

    [Theory]
    [InlineData("", "test-password")]
    [InlineData("   ", "test-password")]
    [InlineData("shared@example.com", "")]
    [InlineData("shared@example.com", "   ")]
    public void Constructor_BlankCredentialThrows(string email, string password)
    {
        using HttpClient httpClient = new();

        Assert.Throws<ArgumentException>(() => new SupabasePasswordSession(
            httpClient,
            new Uri("https://example.supabase.co"),
            "test-anon-key",
            email,
            password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankApiKeyThrows(string apiKey)
    {
        using HttpClient httpClient = new();

        Assert.Throws<ArgumentException>(() => new SupabasePasswordSession(
            httpClient,
            new Uri("https://example.supabase.co"),
            apiKey,
            "shared@example.com",
            "test-password"));
    }

    private static SupabasePasswordSession CreateSession(
        HttpClient httpClient,
        string projectUrl = "https://example.supabase.co",
        TimeProvider? timeProvider = null) =>
        new(
            httpClient,
            new Uri(projectUrl),
            "test-anon-key",
            "shared@example.com",
            "test-password",
            timeProvider);

    private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken, int expiresIn) =>
        JsonResponse(
            HttpStatusCode.OK,
            $$"""
            {
              "access_token": "{{accessToken}}",
              "refresh_token": "{{refreshToken}}",
              "expires_in": {{expiresIn}}
            }
            """);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed record RecordedRequest(
        HttpMethod Method,
        string? PathAndQuery,
        string? ApiKey,
        string? AuthorizationScheme,
        string Body);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    // 応答を任意のタイミングまで保留できるハンドラ。gate 保持中の並行呼び出しを再現する
    private sealed class GatedHandler(Func<Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory();
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery,
                request.Headers.TryGetValues("apikey", out IEnumerable<string>? apiKeys)
                    ? apiKeys.Single()
                    : null,
                request.Headers.Authorization?.Scheme,
                body));

            return responseFactory(request);
        }
    }
}
