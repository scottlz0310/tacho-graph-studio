using System.Net;
using System.Text;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class PostgRestRosterClientTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 18, 1, 2, 3, TimeSpan.Zero);

    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public async Task GetRosterAsync_SendsReadOnlyRequestAndMapsResponse(string projectUrl)
    {
        const string responseJson = """
            [
              {
                "ctrl_num": 123,
                "detail": "除雪車",
                "spec": "10t",
                "vehicle_num": "札幌 100 あ 12-34",
                "vehicle_type": "truck",
                "driver": "運転者",
                "work_period": "winter",
                "updated_at": "2026-07-17T12:34:56Z",
                "is_tacho_target": true
              }
            ]
            """;
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        PostgRestRosterClient client = new(
            httpClient,
            new Uri(projectUrl),
            "test-anon-key",
            new FixedTimeProvider(RetrievedAt));

        RosterResult result = await client.GetRosterAsync(CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/rest/v1/machine_picklist?select=ctrl_num,detail,spec,vehicle_num,vehicle_type,driver,work_period,updated_at,is_tacho_target&order=ctrl_num.asc",
            request.RequestUri?.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-anon-key", request.Headers.Authorization?.Parameter);
        Assert.Equal("test-anon-key", Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Null(request.Content);

        Assert.Equal(RosterDataSource.Remote, result.Source);
        Assert.Equal(RetrievedAt, result.RetrievedAt);
        RosterEntry entry = Assert.Single(result.Entries);
        Assert.Equal(123, entry.ControlNumber);
        Assert.Equal("除雪車", entry.Detail);
        Assert.Equal("10t", entry.Specification);
        Assert.Equal("札幌 100 あ 12-34", entry.RegistrationNumber);
        Assert.Equal("truck", entry.VehicleType);
        Assert.Equal("運転者", entry.Driver);
        Assert.Equal("winter", entry.WorkPeriod);
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 12, 34, 56, TimeSpan.Zero), entry.UpdatedAt);
        Assert.True(entry.IsTachoTarget);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetRosterAsync_NonSuccessStatusThrowsWithoutReadingRosterBody(HttpStatusCode statusCode)
    {
        const string responseBody = "個人名を含む可能性があるレスポンス";
        RecordingHandler handler = new(_ => JsonResponse(statusCode, responseBody));
        using HttpClient httpClient = new(handler);
        PostgRestRosterClient client = new(
            httpClient,
            new Uri("https://example.supabase.co"),
            "test-anon-key");

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetRosterAsync(CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.DoesNotContain(responseBody, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRosterAsync_NullTextFieldsAreNormalizedToEmptyStrings()
    {
        const string responseJson = """
            [{
              "ctrl_num": 123,
              "detail": null,
              "spec": null,
              "vehicle_num": null,
              "vehicle_type": null,
              "driver": null,
              "work_period": null,
              "updated_at": null,
              "is_tacho_target": false
            }]
            """;
        RecordingHandler handler = new(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using HttpClient httpClient = new(handler);
        PostgRestRosterClient client = new(
            httpClient,
            new Uri("https://example.supabase.co"),
            "test-anon-key");

        RosterResult result = await client.GetRosterAsync(CancellationToken.None);

        RosterEntry entry = Assert.Single(result.Entries);
        Assert.Equal(string.Empty, entry.Detail);
        Assert.Equal(string.Empty, entry.Specification);
        Assert.Equal(string.Empty, entry.RegistrationNumber);
        Assert.Equal(string.Empty, entry.VehicleType);
        Assert.Equal(string.Empty, entry.Driver);
        Assert.Null(entry.WorkPeriod);
        Assert.Null(entry.UpdatedAt);
        Assert.False(entry.IsTachoTarget);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankAnonKeyThrows(string anonKey)
    {
        using HttpClient httpClient = new();

        Assert.Throws<ArgumentException>(() => new PostgRestRosterClient(
            httpClient,
            new Uri("https://example.supabase.co"),
            anonKey));
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
