using System.Text.Json;
using System.Text.Json.Serialization;

using TachoGraphStudio.Core.Persistence;

namespace TachoGraphStudio.Core.Roster;

public sealed class JsonRosterFilterSettingsStore : IRosterFilterSettingsStore, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter<RosterSeason>(JsonNamingPolicy.CamelCase, false) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AtomicJsonFile<RosterFilterSettingsDocument> _file;

    public JsonRosterFilterSettingsStore(string settingsFilePath)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new ArgumentException(
                "名簿フィルタ設定のファイルパスを指定してください。",
                nameof(settingsFilePath));
        }

        _file = new AtomicJsonFile<RosterFilterSettingsDocument>(
            Path.GetFullPath(settingsFilePath),
            SerializerOptions,
            "名簿フィルタ設定");
    }

    public async Task<RosterFilterSettings?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        RosterFilterSettingsDocument? document = await _file.ReadAsync(cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"名簿フィルタ設定のバージョン {document.Version} はサポートされていません。");
        }

        return new RosterFilterSettings
        {
            Season = document.Season,
            TachoTargetsOnly = document.TachoTargetsOnly,
            VendorCode = document.VendorCode,
        };
    }

    public Task WriteAsync(
        RosterFilterSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        RosterFilterSettingsDocument document = new()
        {
            Version = CurrentVersion,
            Season = settings.Season,
            TachoTargetsOnly = settings.TachoTargetsOnly,
            VendorCode = settings.VendorCode,
        };

        return _file.WriteAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        _file.Dispose();
    }

    private sealed class RosterFilterSettingsDocument
    {
        public int Version { get; init; }

        [JsonRequired]
        public RosterSeason Season { get; init; }

        [JsonRequired]
        public bool TachoTargetsOnly { get; init; }

        // VendorCode 追加(#61)前の保存ファイルも読めるよう JsonRequired にしない
        public string? VendorCode { get; init; }
    }
}
