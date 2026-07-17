using System.Text.Json;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class JsonRosterCacheTests : IDisposable
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 18, 1, 2, 3, TimeSpan.Zero);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WriteAndReadAsync_RoundTripsRoster(int entryCount)
    {
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        using JsonRosterCache cache = new(cachePath);
        RosterEntry[] entries = Enumerable.Range(1, entryCount)
            .Select(index => new RosterEntry
            {
                ControlNumber = index,
                Detail = $"machine-{index}",
                Specification = $"spec-{index}",
                RegistrationNumber = $"registration-{index}",
                VehicleType = "truck",
                Driver = $"driver-{index}",
                WorkPeriod = "winter",
                UpdatedAt = RetrievedAt,
                IsTachoTarget = true,
            })
            .ToArray();
        RosterResult remoteRoster = new(entries, RosterDataSource.Remote, RetrievedAt);

        await cache.WriteAsync(remoteRoster, CancellationToken.None);
        RosterResult? result = await cache.ReadAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(RosterDataSource.Cache, result.Source);
        Assert.Equal(RetrievedAt, result.RetrievedAt);
        Assert.Equal(entries, result.Entries);
    }

    [Fact]
    public async Task ReadAsync_MissingCacheReturnsNull()
    {
        using JsonRosterCache cache = new(Path.Combine(_temporaryDirectory, "missing.json"));

        RosterResult? result = await cache.ReadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"retrievedAt\":\"2026-07-18T01:02:03Z\",\"entries\":[]}")]
    [InlineData("{\"version\":1,\"retrievedAt\":\"2026-07-18T01:02:03Z\"}")]
    [InlineData("{\"version\":1,\"entries\":[]}")]
    public async Task ReadAsync_InvalidDocumentThrows(string document)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        await File.WriteAllTextAsync(
            cachePath,
            document,
            CancellationToken.None);
        using JsonRosterCache cache = new(cachePath);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => cache.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_InvalidJsonThrowsWithoutIncludingRosterContent()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        const string invalidDocument = "{ invalid roster content";
        await File.WriteAllTextAsync(
            cachePath,
            invalidDocument,
            CancellationToken.None);
        using JsonRosterCache cache = new(cachePath);

        JsonException exception = await Assert.ThrowsAsync<JsonException>(
            () => cache.ReadAsync(CancellationToken.None));

        Assert.DoesNotContain(invalidDocument, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
