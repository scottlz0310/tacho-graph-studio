using System.Text.Json;

using TachoGraphStudio.Core.Persistence;

namespace TachoGraphStudio.Core.Roster;

public sealed class JsonRosterCache : IRosterCache, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AtomicJsonFile<RosterCacheDocument> _file;

    public JsonRosterCache(string cacheFilePath)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException("名簿キャッシュのファイルパスを指定してください。", nameof(cacheFilePath));
        }

        _file = new AtomicJsonFile<RosterCacheDocument>(
            Path.GetFullPath(cacheFilePath),
            SerializerOptions,
            "名簿キャッシュ");
    }

    public async Task<RosterResult?> ReadAsync(CancellationToken cancellationToken = default)
    {
        RosterCacheDocument? document = await _file.ReadAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"名簿キャッシュのバージョン {document.Version} はサポートされていません。");
        }

        if (document.Entries is null)
        {
            throw new InvalidDataException("名簿キャッシュに entries 配列がありません。");
        }

        if (document.RetrievedAt == default)
        {
            throw new InvalidDataException("名簿キャッシュに取得日時がありません。");
        }

        return new RosterResult(document.Entries, RosterDataSource.Cache, document.RetrievedAt);
    }

    public async Task WriteAsync(RosterResult roster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roster);

        RosterCacheDocument document = new()
        {
            Version = CurrentVersion,
            RetrievedAt = roster.RetrievedAt,
            Entries = roster.Entries.ToList(),
        };

        await _file.WriteAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        _file.Dispose();
    }

    private sealed class RosterCacheDocument
    {
        public int Version { get; init; }

        public DateTimeOffset RetrievedAt { get; init; }

        public List<RosterEntry>? Entries { get; init; }
    }
}
