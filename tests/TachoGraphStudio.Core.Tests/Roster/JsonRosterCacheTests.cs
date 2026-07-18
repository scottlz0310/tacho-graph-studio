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

    [Fact]
    public async Task WriteAsync_MoveFailureRemovesTemporaryFile()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        Directory.CreateDirectory(cachePath);
        using JsonRosterCache cache = new(cachePath);
        RosterResult roster = new([], RosterDataSource.Remote, RetrievedAt);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => cache.WriteAsync(roster, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "roster.json.*.tmp"));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public async Task InstancesForSamePath_SerializeConcurrentReadAndWrite(int entryCount)
    {
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        using JsonRosterCache reader = new(cachePath);
        using JsonRosterCache writer = new(cachePath);
        RosterResult initialRoster = new(
            CreateEntries(entryCount),
            RosterDataSource.Remote,
            RetrievedAt);
        RosterResult replacementRoster = new([], RosterDataSource.Remote, RetrievedAt);
        await writer.WriteAsync(initialRoster, CancellationToken.None);

        Task<RosterResult?> readTask = reader.ReadAsync(CancellationToken.None);
        Task writeTask = writer.WriteAsync(replacementRoster, CancellationToken.None);

        await Task.WhenAll(readTask, writeTask);
        Assert.Equal(entryCount, (await readTask)!.Entries.Count);
        Assert.Empty((await reader.ReadAsync(CancellationToken.None))!.Entries);
    }

    [Fact]
    public async Task WriteAsync_CanceledTokenPreservesCancellation()
    {
        string cachePath = Path.Combine(_temporaryDirectory, "roster.json");
        using JsonRosterCache cache = new(cachePath);
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.WriteAsync(
                new RosterResult([], RosterDataSource.Remote, RetrievedAt),
                cancellationSource.Token));
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndOperationsAfterDisposeThrow()
    {
        JsonRosterCache cache = new(Path.Combine(_temporaryDirectory, "roster.json"));

        cache.Dispose();
        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => cache.ReadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static RosterEntry[] CreateEntries(int entryCount) =>
        Enumerable.Range(1, entryCount)
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
}
