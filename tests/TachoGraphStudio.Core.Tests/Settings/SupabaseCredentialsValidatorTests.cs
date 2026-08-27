using System.Net;
using System.Text;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class SupabaseCredentialsValidatorTests
{
    private static readonly SupabaseCredentials Credentials = SupabaseCredentials.Create(
        new Uri("https://example.supabase.co"),
        "test-anon-key",
        "test-vendor",
        "test-password");

    [Fact]
    public async Task ValidateAsync_SignInAndRosterReadSucceedReturnsValid()
    {
        RecordingHandler handler = new(request => IsTokenRequest(request)
            ? TokenResponse()
            : JsonResponse(HttpStatusCode.OK, "[]"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/auth/v1/token?grant_type=password", handler.Requests[0].RequestUri?.PathAndQuery);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ValidateAsync_SignInRejectionReportsAuthenticationFailure(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "invalid_grant"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("業者コードとパスワード", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ValidateAsync_RosterReadDeniedReportsAuthorizationFailure(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(request => IsTokenRequest(request)
            ? TokenResponse()
            : JsonResponse(statusCode, "permission denied"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("読み取り権限", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_ServerErrorReportsConnectionFailure()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.InternalServerError, "error"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("接続できませんでした", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("<html>not json</html>")]
    public async Task ValidateAsync_UninterpretableRosterBodyReportsFailure(string responseJson)
    {
        RecordingHandler handler = new(request => IsTokenRequest(request)
            ? TokenResponse()
            : JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("解釈できませんでした", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_CancellationPropagates()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.ValidateAsync(Credentials, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ValidateAsync_TimeoutWithoutCallerCancellationReportsFailure()
    {
        RecordingHandler handler = new(_ => throw new TaskCanceledException("The request timed out."));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        SupabaseConnectionResult result = await validator.ValidateAsync(Credentials, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath == "/auth/v1/token";

    private static HttpResponseMessage TokenResponse() =>
        JsonResponse(
            HttpStatusCode.OK,
            """{ "access_token": "token-1", "refresh_token": "refresh-1", "expires_in": 3600 }""");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

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
