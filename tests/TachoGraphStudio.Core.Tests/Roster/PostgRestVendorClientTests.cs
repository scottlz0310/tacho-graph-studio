using System.Net;
using System.Text;

using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Roster;
using TachoGraphStudio.Core.Tests.Auth;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class PostgRestVendorClientTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public async Task GetVendorsAsync_SendsAuthenticatedReadOnlyRequestAndMapsResponse(string projectUrl)
    {
        const string responseJson = """
            [
              {
                "code": "test-vendor",
                "display_name": "テスト業者",
                "ranges": [
                  { "min_ctrl_num": 100, "max_ctrl_num": 499 },
                  { "min_ctrl_num": 500, "max_ctrl_num": 699 }
                ]
              },
              {
                "code": "admin",
                "display_name": "管理者",
                "ranges": []
              }
            ]
            """;
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        PostgRestVendorClient client = new(
            httpClient,
            new Uri(projectUrl),
            new FakeSupabaseSession(),
            new FixedTimeProvider(RetrievedAt));

        VendorResult result = await client.GetVendorsAsync(CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/rest/v1/vendors?select=code,display_name,ranges:vendor_ctrl_num_ranges(min_ctrl_num,max_ctrl_num)"
            + "&ranges.purpose=eq.view&is_active=eq.true&order=sort_order.asc",
            request.RequestUri?.PathAndQuery);
        // anon キーではなく Supabase Auth の access token で読む(#107)
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("access-token-1", request.Headers.Authorization?.Parameter);
        Assert.Equal("test-anon-key", Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Null(request.Content);

        Assert.Equal(RosterDataSource.Remote, result.Source);
        Assert.Equal(RetrievedAt, result.RetrievedAt);
        Assert.Equal(2, result.Vendors.Count);
        VendorEntry testVendor = result.Vendors[0];
        Assert.Equal("test-vendor", testVendor.Code);
        Assert.Equal("テスト業者", testVendor.DisplayName);
        Assert.Equal(
            [new CtrlNumRange(100, 499), new CtrlNumRange(500, 699)],
            testVendor.ViewRanges);
        VendorEntry admin = result.Vendors[1];
        Assert.Equal("admin", admin.Code);
        Assert.Empty(admin.ViewRanges);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetVendorsAsync_TokenRejectionRetriesOnceWithFreshToken(HttpStatusCode statusCode)
    {
        int callCount = 0;
        RecordingHandler handler = new(_ => ++callCount == 1
            ? JsonResponse(statusCode, "unauthorized")
            : JsonResponse(HttpStatusCode.OK, """[{ "code": "test-vendor", "ranges": [] }]"""));
        using HttpClient httpClient = new(handler);
        FakeSupabaseSession session = new();
        PostgRestVendorClient client = new(httpClient, new Uri("https://example.supabase.co"), session);

        VendorResult result = await client.GetVendorsAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("access-token-2", handler.Requests[1].Headers.Authorization?.Parameter);
        Assert.Equal("access-token-1", Assert.Single(session.InvalidatedTokens));
        Assert.Single(result.Vendors);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetVendorsAsync_PersistentAuthorizationFailureThrowsAuthenticationException(
        HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "error body"));
        using HttpClient httpClient = new(handler);
        PostgRestVendorClient client = new(
            httpClient,
            new Uri("https://example.supabase.co"),
            new FakeSupabaseSession());

        SupabaseAuthenticationException exception =
            await Assert.ThrowsAsync<SupabaseAuthenticationException>(
                () => client.GetVendorsAsync(CancellationToken.None));

        Assert.Contains("業者マスタ", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GetVendorsAsync_NonSuccessStatusThrows(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "error body"));
        using HttpClient httpClient = new(handler);
        PostgRestVendorClient client = new(
            httpClient,
            new Uri("https://example.supabase.co"),
            new FakeSupabaseSession());

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVendorsAsync(CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task GetVendorsAsync_NullDisplayNameIsNormalizedToEmptyString()
    {
        const string responseJson = """
            [{ "code": "test-vendor", "display_name": null, "ranges": [] }]
            """;
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        PostgRestVendorClient client = new(
            httpClient,
            new Uri("https://example.supabase.co"),
            new FakeSupabaseSession());

        VendorResult result = await client.GetVendorsAsync(CancellationToken.None);

        VendorEntry vendor = Assert.Single(result.Vendors);
        Assert.Equal(string.Empty, vendor.DisplayName);
    }

    [Fact]
    public void Constructor_NullSessionThrows()
    {
        using HttpClient httpClient = new();

        Assert.Throws<ArgumentNullException>(() => new PostgRestVendorClient(
            httpClient,
            new Uri("https://example.supabase.co"),
            null!));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}
