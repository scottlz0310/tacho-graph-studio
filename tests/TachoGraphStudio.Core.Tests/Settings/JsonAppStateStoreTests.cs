using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class JsonAppStateStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    private string StatePath => Path.Combine(_temporaryDirectory, "app-state.json");

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsAllFields()
    {
        using JsonAppStateStore store = new(StatePath);
        AppState state = new()
        {
            OutputDirectory = @"C:\Users\dev\Pictures\タコチャート",
            LastTargetDate = new DateOnly(2026, 7, 19),
            SelectedTemplateId = "Yazaki45",
            SidebarWidth = 420.5,
            Window = new WindowPlacement(100, 50, 1440, 900, IsMaximized: false),
        };

        await store.WriteAsync(state);
        AppState? restored = await store.ReadAsync();

        Assert.Equal(state, restored);
    }

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsEmptyState()
    {
        using JsonAppStateStore store = new(StatePath);
        AppState state = new();

        await store.WriteAsync(state);
        AppState? restored = await store.ReadAsync();

        Assert.Equal(state, restored);
        Assert.Null(restored!.OutputDirectory);
        Assert.Null(restored.LastTargetDate);
        Assert.Null(restored.SelectedTemplateId);
        Assert.Null(restored.SidebarWidth);
        Assert.Null(restored.Window);
    }

    [Fact]
    public async Task ReadAsync_MissingFileReturnsNull()
    {
        using JsonAppStateStore store = new(StatePath);

        Assert.Null(await store.ReadAsync());
    }

    [Theory]
    [InlineData("""{"version":999}""")]
    [InlineData("""{"version":0}""")]
    public async Task ReadAsync_UnsupportedVersionThrows(string document)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(StatePath, document);
        using JsonAppStateStore store = new(StatePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_CorruptedJsonThrows()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(StatePath, "{ not json");
        using JsonAppStateStore store = new(StatePath);

        await Assert.ThrowsAnyAsync<Exception>(() => store.ReadAsync());
    }

    [Fact]
    public async Task WriteAsync_OverwritesPreviousState()
    {
        using JsonAppStateStore store = new(StatePath);
        await store.WriteAsync(new AppState { SelectedTemplateId = "Yazaki45" });

        await store.WriteAsync(new AppState { SelectedTemplateId = "Task-Meter" });
        AppState? restored = await store.ReadAsync();

        Assert.Equal("Task-Meter", restored!.SelectedTemplateId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
