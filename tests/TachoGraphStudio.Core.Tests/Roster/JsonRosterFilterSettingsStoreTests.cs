using System.Text.Json;

using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class JsonRosterFilterSettingsStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(RosterSeason.All, true)]
    [InlineData(RosterSeason.Winter, false)]
    [InlineData(RosterSeason.Summer, true)]
    [InlineData(RosterSeason.YearRound, false)]
    public async Task WriteAndReadAsync_RoundTripsSettings(
        RosterSeason season,
        bool tachoTargetsOnly)
    {
        string settingsPath = Path.Combine(_temporaryDirectory, "roster-filter.json");
        using JsonRosterFilterSettingsStore store = new(settingsPath);
        RosterFilterSettings settings = new()
        {
            Season = season,
            TachoTargetsOnly = tachoTargetsOnly,
        };

        await store.WriteAsync(settings, CancellationToken.None);
        RosterFilterSettings? result = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(settings, result);
        string json = await File.ReadAllTextAsync(settingsPath, CancellationToken.None);
        Assert.DoesNotContain("keyword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("controlNumber", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_MissingSettingsReturnsNull()
    {
        using JsonRosterFilterSettingsStore store = new(
            Path.Combine(_temporaryDirectory, "missing.json"));

        RosterFilterSettings? result = await store.ReadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"season\":\"all\",\"tachoTargetsOnly\":true}")]
    public async Task ReadAsync_InvalidDocumentThrowsInvalidDataException(string document)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string settingsPath = Path.Combine(_temporaryDirectory, "roster-filter.json");
        await File.WriteAllTextAsync(settingsPath, document, CancellationToken.None);
        using JsonRosterFilterSettingsStore store = new(settingsPath);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"version\":1,\"tachoTargetsOnly\":true}")]
    [InlineData("{\"version\":1,\"season\":\"all\"}")]
    [InlineData("{\"version\":1,\"season\":\"invalid\",\"tachoTargetsOnly\":true}")]
    [InlineData("{\"version\":1,\"season\":1,\"tachoTargetsOnly\":true}")]
    public async Task ReadAsync_InvalidJsonContractThrowsJsonException(string document)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string settingsPath = Path.Combine(_temporaryDirectory, "roster-filter.json");
        await File.WriteAllTextAsync(settingsPath, document, CancellationToken.None);
        using JsonRosterFilterSettingsStore store = new(settingsPath);

        await Assert.ThrowsAsync<JsonException>(
            () => store.ReadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
