using System.Net;
using System.Text.Json;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class CachedVendorClientTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

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

    public static TheoryData<Exception> CacheFailures => new()
    {
        new IOException("Cache unavailable."),
        new JsonException("Cache invalid."),
        new UnauthorizedAccessException("Cache inaccessible."),
    };

    [Fact]
    public async Task GetVendorsAsync_RemoteSuccessUpdatesCache()
    {
        VendorResult remoteResult = CreateResult(RosterDataSource.Remote);
        StubVendorClient remoteClient = new(remoteResult);
        StubVendorCache cache = new();
        CachedVendorClient client = new(remoteClient, cache);

        VendorResult result = await client.GetVendorsAsync(CancellationToken.None);

        Assert.Same(remoteResult, result);
        Assert.Same(remoteResult, cache.WrittenVendors);
        Assert.Equal(0, cache.ReadCount);
    }

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task GetVendorsAsync_TransientFailureReturnsCache(Exception remoteFailure)
    {
        VendorResult cachedResult = CreateResult(RosterDataSource.Remote);
        StubVendorClient remoteClient = new(remoteFailure);
        StubVendorCache cache = new(cachedResult);
        CachedVendorClient client = new(remoteClient, cache);

        VendorResult result = await client.GetVendorsAsync(CancellationToken.None);

        Assert.Equal(RosterDataSource.Cache, result.Source);
        Assert.Equal(cachedResult.Vendors, result.Vendors);
        Assert.Equal(cachedResult.RetrievedAt, result.RetrievedAt);
        Assert.Equal(1, cache.ReadCount);
        Assert.Null(cache.WrittenVendors);
    }

    [Theory]
    [MemberData(nameof(NonTransientFailures))]
    public async Task GetVendorsAsync_NonTransientFailureDoesNotReadCache(Exception remoteFailure)
    {
        StubVendorClient remoteClient = new(remoteFailure);
        StubVendorCache cache = new(CreateResult(RosterDataSource.Cache));
        CachedVendorClient client = new(remoteClient, cache);

        Exception exception = await Assert.ThrowsAsync(
            remoteFailure.GetType(),
            () => client.GetVendorsAsync(CancellationToken.None));

        Assert.Same(remoteFailure, exception);
        Assert.Equal(0, cache.ReadCount);
    }

    [Fact]
    public async Task GetVendorsAsync_TransientFailureWithoutCacheThrowsContextualException()
    {
        HttpRequestException remoteFailure = new("Network unavailable.");
        StubVendorClient remoteClient = new(remoteFailure);
        StubVendorCache cache = new();
        CachedVendorClient client = new(remoteClient, cache);

        VendorUnavailableException exception = await Assert.ThrowsAsync<VendorUnavailableException>(
            () => client.GetVendorsAsync(CancellationToken.None));

        Assert.Same(remoteFailure, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(CacheFailures))]
    public async Task GetVendorsAsync_TransientFailureWithUnreadableCachePreservesBothErrors(
        Exception cacheFailure)
    {
        HttpRequestException remoteFailure = new("Network unavailable.");
        StubVendorClient remoteClient = new(remoteFailure);
        StubVendorCache cache = new(cacheFailure);
        CachedVendorClient client = new(remoteClient, cache);

        VendorUnavailableException exception = await Assert.ThrowsAsync<VendorUnavailableException>(
            () => client.GetVendorsAsync(CancellationToken.None));

        AggregateException aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Contains(remoteFailure, aggregate.InnerExceptions);
        Assert.Contains(cacheFailure, aggregate.InnerExceptions);
    }

    [Fact]
    public async Task GetVendorsAsync_CallerCancellationDoesNotReadCache()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        TaskCanceledException cancellation = new("Canceled by caller.");
        StubVendorClient remoteClient = new(cancellation);
        StubVendorCache cache = new(CreateResult(RosterDataSource.Cache));
        CachedVendorClient client = new(remoteClient, cache);

        TaskCanceledException exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetVendorsAsync(cancellationTokenSource.Token));

        Assert.Same(cancellation, exception);
        Assert.Equal(0, cache.ReadCount);
    }

    private static VendorResult CreateResult(RosterDataSource source)
    {
        return new VendorResult(
            [new VendorEntry { Code = "arata", DisplayName = "アラタ工業", ViewRanges = [new CtrlNumRange(100, 699)] }],
            source,
            RetrievedAt);
    }

    private sealed class StubVendorClient : IVendorClient
    {
        private readonly Exception? _exception;
        private readonly VendorResult? _result;

        public StubVendorClient(VendorResult result)
        {
            _result = result;
        }

        public StubVendorClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default)
        {
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<VendorResult>(_exception);
        }
    }

    private sealed class StubVendorCache : IVendorCache
    {
        private readonly Exception? _readException;
        private readonly VendorResult? _readResult;

        public StubVendorCache()
        {
        }

        public StubVendorCache(VendorResult readResult)
        {
            _readResult = readResult;
        }

        public StubVendorCache(Exception readException)
        {
            _readException = readException;
        }

        public int ReadCount { get; private set; }

        public VendorResult? WrittenVendors { get; private set; }

        public Task<VendorResult?> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return _readException is null
                ? Task.FromResult(_readResult)
                : Task.FromException<VendorResult?>(_readException);
        }

        public Task WriteAsync(VendorResult vendors, CancellationToken cancellationToken = default)
        {
            WrittenVendors = vendors;
            return Task.CompletedTask;
        }
    }
}
