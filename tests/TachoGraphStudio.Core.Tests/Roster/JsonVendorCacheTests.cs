using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class JsonVendorCacheTests : IDisposable
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WriteAndReadAsync_RoundTripsVendors(int vendorCount)
    {
        string cachePath = Path.Combine(_temporaryDirectory, "vendors.json");
        using JsonVendorCache cache = new(cachePath);
        VendorEntry[] vendors = Enumerable.Range(1, vendorCount)
            .Select(index => new VendorEntry
            {
                Code = $"vendor-{index}",
                DisplayName = $"業者-{index}",
                ViewRanges = [new CtrlNumRange(index * 100, index * 100 + 99)],
            })
            .ToArray();
        VendorResult remoteVendors = new(vendors, RosterDataSource.Remote, RetrievedAt);

        await cache.WriteAsync(remoteVendors, CancellationToken.None);
        VendorResult? result = await cache.ReadAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(RosterDataSource.Cache, result.Source);
        Assert.Equal(RetrievedAt, result.RetrievedAt);
        Assert.Equal(vendors.Length, result.Vendors.Count);
        for (int index = 0; index < vendors.Length; index++)
        {
            Assert.Equal(vendors[index].Code, result.Vendors[index].Code);
            Assert.Equal(vendors[index].DisplayName, result.Vendors[index].DisplayName);
            Assert.Equal(vendors[index].ViewRanges, result.Vendors[index].ViewRanges);
        }
    }

    [Fact]
    public async Task ReadAsync_MissingCacheReturnsNull()
    {
        using JsonVendorCache cache = new(Path.Combine(_temporaryDirectory, "missing.json"));

        VendorResult? result = await cache.ReadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"retrievedAt\":\"2026-07-20T01:02:03Z\",\"vendors\":[]}")]
    [InlineData("{\"version\":1,\"retrievedAt\":\"2026-07-20T01:02:03Z\"}")]
    [InlineData("{\"version\":1,\"vendors\":[]}")]
    public async Task ReadAsync_InvalidDocumentThrows(string document)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string cachePath = Path.Combine(_temporaryDirectory, "vendors.json");
        await File.WriteAllTextAsync(cachePath, document, CancellationToken.None);
        using JsonVendorCache cache = new(cachePath);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => cache.ReadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
