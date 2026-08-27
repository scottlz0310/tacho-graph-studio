using System.Net;
using System.Text;

using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class PostgRestLoginVendorClientTests
{
    [Fact]
    public void LoginVendor_NullDisplayNameDefaultsToEmpty()
    {
        LoginVendor vendor = new()
        {
            Code = "test-vendor",
            DisplayName = null!,
        };

        Assert.Equal(string.Empty, vendor.DisplayName);
    }

    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public async Task GetLoginVendorsAsync_ReadsMinimalVendorListWithoutJwt(string projectUrl)
    {
        RecordingHandler handler = new(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            [
              { "code": "test-vendor", "display_name": "テスト業者", "sort_order": 1 }
            ]
            """));
        using HttpClient httpClient = new(handler);
        PostgRestLoginVendorClient client = new(httpClient);

        IReadOnlyList<LoginVendor> vendors = await client.GetLoginVendorsAsync(
            new Uri(projectUrl),
            "test-anon-key",
            CancellationToken.None);

        LoginVendor vendor = Assert.Single(vendors);
        Assert.Equal("test-vendor", vendor.Code);
        Assert.Equal("テスト業者", vendor.DisplayName);
        Assert.Equal(1, vendor.SortOrder);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/rest/v1/login_vendors?select=code,display_name,sort_order&order=sort_order.asc",
            request.RequestUri?.PathAndQuery);
        Assert.Equal("test-anon-key", request.Headers.GetValues("apikey").Single());
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetLoginVendorsAsync_AuthRejectionThrowsAuthenticationException(
        HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "permission denied"));
        using HttpClient httpClient = new(handler);
        PostgRestLoginVendorClient client = new(httpClient);

        SupabaseAuthenticationException exception =
            await Assert.ThrowsAsync<SupabaseAuthenticationException>(
                () => client.GetLoginVendorsAsync(
                    new Uri("https://example.supabase.co"),
                    "test-anon-key",
                    CancellationToken.None));

        Assert.Contains("業者一覧", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetLoginVendorsAsync_BlankAnonKeyThrows(string anonKey)
    {
        using HttpClient httpClient = new();
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetLoginVendorsAsync(
                new Uri("https://example.supabase.co"),
                anonKey,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("http://example.supabase.co")]
    [InlineData("ftp://example.supabase.co")]
    public async Task GetLoginVendorsAsync_NonHttpsUrlThrows(string projectUrl)
    {
        using HttpClient httpClient = new();
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetLoginVendorsAsync(
                new Uri(projectUrl),
                "test-anon-key",
                CancellationToken.None));
    }

    [Fact]
    public async Task GetLoginVendorsAsync_RelativeUrlThrows()
    {
        using HttpClient httpClient = new();
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetLoginVendorsAsync(
                new Uri("/relative", UriKind.Relative),
                "test-anon-key",
                CancellationToken.None));
    }

    [Fact]
    public async Task GetLoginVendorsAsync_TimeoutPropagatesCancellation()
    {
        RecordingHandler handler = new(_ => throw new TaskCanceledException("The request timed out."));
        using HttpClient httpClient = new(handler);
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetLoginVendorsAsync(
                new Uri("https://example.supabase.co"),
                "test-anon-key",
                CancellationToken.None));
    }

    [Fact]
    public async Task GetLoginVendorsAsync_MalformedJsonResponseThrowsJsonException()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html"),
        });
        using HttpClient httpClient = new(handler);
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => client.GetLoginVendorsAsync(
                new Uri("https://example.supabase.co"),
                "test-anon-key",
                CancellationToken.None));
    }

    [Fact]
    public async Task GetLoginVendorsAsync_NullJsonResponseThrowsInvalidDataException()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "null"));
        using HttpClient httpClient = new(handler);
        PostgRestLoginVendorClient client = new(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLoginVendorsAsync(
                new Uri("https://example.supabase.co"),
                "test-anon-key",
                CancellationToken.None));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}
