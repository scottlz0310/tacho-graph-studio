using System.Text.Json;

using TachoGraphStudio.Core.Persistence;

namespace TachoGraphStudio.Core.Settings;

public sealed class JsonAppStateStore : IAppStateStore, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AtomicJsonFile<AppStateDocument> _file;

    public JsonAppStateStore(string settingsFilePath)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new ArgumentException(
                "アプリ状態のファイルパスを指定してください。",
                nameof(settingsFilePath));
        }

        _file = new AtomicJsonFile<AppStateDocument>(
            Path.GetFullPath(settingsFilePath),
            SerializerOptions,
            "アプリ状態");
    }

    public async Task<AppState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        AppStateDocument? document = await _file.ReadAsync(cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"アプリ状態のバージョン {document.Version} はサポートされていません。");
        }

        return new AppState
        {
            OutputDirectory = document.OutputDirectory,
            LastTargetDate = document.LastTargetDate,
            SelectedTemplateId = document.SelectedTemplateId,
            SidebarWidth = document.SidebarWidth,
            Window = document.Window,
        };
    }

    public Task WriteAsync(AppState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        AppStateDocument document = new()
        {
            Version = CurrentVersion,
            OutputDirectory = state.OutputDirectory,
            LastTargetDate = state.LastTargetDate,
            SelectedTemplateId = state.SelectedTemplateId,
            SidebarWidth = state.SidebarWidth,
            Window = state.Window,
        };

        return _file.WriteAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        _file.Dispose();
    }

    private sealed class AppStateDocument
    {
        public int Version { get; init; }

        public string? OutputDirectory { get; init; }

        public DateOnly? LastTargetDate { get; init; }

        public string? SelectedTemplateId { get; init; }

        public double? SidebarWidth { get; init; }

        public WindowPlacement? Window { get; init; }
    }
}
