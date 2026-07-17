using System.Net;
using System.Text.Json;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class CachedRosterClientTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 18, 1, 2, 3, TimeSpan.Zero);

    public static TheoryData<Exception> TransientFailures => new()
    {
        new HttpRequestException("Network unavailable."),
        new HttpRequestException("Request timeout.", null, HttpStatusCode.RequestTimeout),
        new HttpRequestException("Server error.", null, HttpStatusCode.InternalServerError),
        new TimeoutException("Request timeout."),
        new TaskCanceledException("HttpClient timeout."),
    };

    public static TheoryData<Exception> NonTransientFailures => new()
    {
        new HttpRequestException("Invalid key.", null, HttpStatusCode.Unauthorized),
        new HttpRequestException("Forbidden.", null, HttpStatusCode.Forbidden),
        new JsonException("Invalid response contract."),
        new InvalidDataException("Invalid response contract."),
    };

    [Fact]
    public async Task GetRosterAsync_RemoteSuccessUpdatesCache()
    {
        RosterResult remoteResult = CreateResult(RosterDataSource.Remote);
        StubRosterClient remoteClient = new(remoteResult);
        StubRosterCache cache = new();
        CachedRosterClient client = new(remoteClient, cache);

        RosterResult result = await client.GetRosterAsync(CancellationToken.None);

        Assert.Same(remoteResult, result);
        Assert.Same(remoteResult, cache.WrittenRoster);
        Assert.Equal(0, cache.ReadCount);
    }

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task GetRosterAsync_TransientFailureReturnsCache(Exception remoteFailure)
    {
        RosterResult cachedResult = CreateResult(RosterDataSource.Remote);
        StubRosterClient remoteClient = new(remoteFailure);
        StubRosterCache cache = new(cachedResult);
        CachedRosterClient client = new(remoteClient, cache);

        RosterResult result = await client.GetRosterAsync(CancellationToken.None);

        Assert.Equal(RosterDataSource.Cache, result.Source);
        Assert.Equal(cachedResult.Entries, result.Entries);
        Assert.Equal(cachedResult.RetrievedAt, result.RetrievedAt);
        Assert.Equal(1, cache.ReadCount);
        Assert.Null(cache.WrittenRoster);
    }

    [Theory]
    [MemberData(nameof(NonTransientFailures))]
    public async Task GetRosterAsync_NonTransientFailureDoesNotReadCache(Exception remoteFailure)
    {
        StubRosterClient remoteClient = new(remoteFailure);
        StubRosterCache cache = new(CreateResult(RosterDataSource.Cache));
        CachedRosterClient client = new(remoteClient, cache);

        Exception exception = await Assert.ThrowsAsync(
            remoteFailure.GetType(),
            () => client.GetRosterAsync(CancellationToken.None));

        Assert.Same(remoteFailure, exception);
        Assert.Equal(0, cache.ReadCount);
    }

    [Fact]
    public async Task GetRosterAsync_TransientFailureWithoutCacheThrowsContextualException()
    {
        HttpRequestException remoteFailure = new("Network unavailable.");
        StubRosterClient remoteClient = new(remoteFailure);
        StubRosterCache cache = new();
        CachedRosterClient client = new(remoteClient, cache);

        RosterUnavailableException exception = await Assert.ThrowsAsync<RosterUnavailableException>(
            () => client.GetRosterAsync(CancellationToken.None));

        Assert.Same(remoteFailure, exception.InnerException);
        Assert.DoesNotContain("Network unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRosterAsync_CallerCancellationDoesNotReadCache()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        TaskCanceledException cancellation = new("Canceled by caller.");
        StubRosterClient remoteClient = new(cancellation);
        StubRosterCache cache = new(CreateResult(RosterDataSource.Cache));
        CachedRosterClient client = new(remoteClient, cache);

        TaskCanceledException exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetRosterAsync(cancellationTokenSource.Token));

        Assert.Same(cancellation, exception);
        Assert.Equal(0, cache.ReadCount);
    }

    [Theory]
    [MemberData(nameof(CacheFailures))]
    public async Task GetRosterAsync_TransientFailureWithUnreadableCachePreservesBothErrors(
        Exception cacheFailure)
    {
        HttpRequestException remoteFailure = new("Network unavailable.");
        StubRosterClient remoteClient = new(remoteFailure);
        StubRosterCache cache = new(cacheFailure);
        CachedRosterClient client = new(remoteClient, cache);

        RosterUnavailableException exception = await Assert.ThrowsAsync<RosterUnavailableException>(
            () => client.GetRosterAsync(CancellationToken.None));

        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Contains(remoteFailure, aggregate.InnerExceptions);
        Assert.Contains(cacheFailure, aggregate.InnerExceptions);
    }

    public static TheoryData<Exception> CacheFailures => new()
    {
        new IOException("Cache unavailable."),
        new JsonException("Cache invalid."),
        new UnauthorizedAccessException("Cache inaccessible."),
    };

    private static RosterResult CreateResult(RosterDataSource source)
    {
        return new RosterResult(
            [new RosterEntry { ControlNumber = 123, Detail = "除雪車" }],
            source,
            RetrievedAt);
    }

    private sealed class StubRosterClient : IRosterClient
    {
        private readonly Exception? _exception;
        private readonly RosterResult? _result;

        public StubRosterClient(RosterResult result)
        {
            _result = result;
        }

        public StubRosterClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default)
        {
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<RosterResult>(_exception);
        }
    }

    private sealed class StubRosterCache : IRosterCache
    {
        private readonly Exception? _readException;
        private readonly RosterResult? _readResult;

        public StubRosterCache()
        {
        }

        public StubRosterCache(RosterResult readResult)
        {
            _readResult = readResult;
        }

        public StubRosterCache(Exception readException)
        {
            _readException = readException;
        }

        public int ReadCount { get; private set; }

        public RosterResult? WrittenRoster { get; private set; }

        public Task<RosterResult?> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return _readException is null
                ? Task.FromResult(_readResult)
                : Task.FromException<RosterResult?>(_readException);
        }

        public Task WriteAsync(RosterResult roster, CancellationToken cancellationToken = default)
        {
            WrittenRoster = roster;
            return Task.CompletedTask;
        }
    }
}
