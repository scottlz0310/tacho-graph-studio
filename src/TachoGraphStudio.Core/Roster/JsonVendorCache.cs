using System.Text.Json;

using TachoGraphStudio.Core.Persistence;

namespace TachoGraphStudio.Core.Roster;

// JsonRosterCache と同パターンの業者マスタ用オフラインキャッシュ(#61)
public sealed class JsonVendorCache : IVendorCache, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AtomicJsonFile<VendorCacheDocument> _file;

    public JsonVendorCache(string cacheFilePath)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException("業者キャッシュのファイルパスを指定してください。", nameof(cacheFilePath));
        }

        _file = new AtomicJsonFile<VendorCacheDocument>(
            Path.GetFullPath(cacheFilePath),
            SerializerOptions,
            "業者キャッシュ");
    }

    public async Task<VendorResult?> ReadAsync(CancellationToken cancellationToken = default)
    {
        VendorCacheDocument? document = await _file.ReadAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"業者キャッシュのバージョン {document.Version} はサポートされていません。");
        }

        if (document.Vendors is null)
        {
            throw new InvalidDataException("業者キャッシュに vendors 配列がありません。");
        }

        if (document.RetrievedAt == default)
        {
            throw new InvalidDataException("業者キャッシュに取得日時がありません。");
        }

        return new VendorResult(document.Vendors, RosterDataSource.Cache, document.RetrievedAt);
    }

    public async Task WriteAsync(VendorResult vendors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendors);

        VendorCacheDocument document = new()
        {
            Version = CurrentVersion,
            RetrievedAt = vendors.RetrievedAt,
            Vendors = vendors.Vendors.ToList(),
        };

        await _file.WriteAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        _file.Dispose();
    }

    private sealed class VendorCacheDocument
    {
        public int Version { get; init; }

        public DateTimeOffset RetrievedAt { get; init; }

        public List<VendorEntry>? Vendors { get; init; }
    }
}
