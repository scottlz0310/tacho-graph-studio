using System.Net;
using System.Text;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class SupabaseCredentialsValidatorTests
{
    private static readonly SupabaseCredentials Credentials = SupabaseCredentials.Create(
        new Uri("https://example.supabase.co"),
        "test-anon-key");

    [Fact]
    public async Task IsValidAsync_SuccessfulResponseReturnsTrue()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        bool result = await validator.IsValidAsync(Credentials, CancellationToken.None);

        Assert.True(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task IsValidAsync_NonSuccessStatusReturnsFalse(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => JsonResponse(statusCode, "error"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        bool result = await validator.IsValidAsync(Credentials, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidAsync_NullBodyReturnsFalse()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "null"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        bool result = await validator.IsValidAsync(Credentials, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidAsync_MalformedJsonBodyReturnsFalse()
    {
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "<html>not json</html>"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        bool result = await validator.IsValidAsync(Credentials, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidAsync_CancellationPropagates()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        using HttpClient httpClient = new(handler);
        SupabaseCredentialsValidator validator = new(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.IsValidAsync(Credentials, cancellationTokenSource.Token));
    }

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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }
}
