using System.Text.Json;

namespace TachoGraphStudio.Core.Roster;

public sealed class JsonRosterCache : IRosterCache, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonRosterCache(string cacheFilePath)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException("名簿キャッシュのファイルパスを指定してください。", nameof(cacheFilePath));
        }

        _cacheFilePath = Path.GetFullPath(cacheFilePath);
    }

    public async Task<RosterResult?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            await using FileStream stream = new(
                _cacheFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            RosterCacheDocument? document = await JsonSerializer.DeserializeAsync<RosterCacheDocument>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (document is null)
            {
                throw new InvalidDataException("名簿キャッシュが JSON オブジェクトではありません。");
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
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(RosterResult roster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roster);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? directoryPath = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string temporaryFilePath = _cacheFilePath + ".tmp";
            await using (FileStream stream = new(
                temporaryFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                RosterCacheDocument document = new()
                {
                    Version = CurrentVersion,
                    RetrievedAt = roster.RetrievedAt,
                    Entries = roster.Entries.ToList(),
                };

                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryFilePath, _cacheFilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private sealed class RosterCacheDocument
    {
        public int Version { get; init; }

        public DateTimeOffset RetrievedAt { get; init; }

        public List<RosterEntry>? Entries { get; init; }
    }
}
