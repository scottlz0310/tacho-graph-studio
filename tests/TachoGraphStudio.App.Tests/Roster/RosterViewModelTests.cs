using System.Net;
using System.Text.Json;

using TachoGraphStudio.App.Roster;
using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.App.Tests.Roster;

public sealed class RosterViewModelTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 7, 18, 1, 2, 3, TimeSpan.Zero);

    public static TheoryData<Exception> FilterSettingsReadFailures => new()
    {
        new JsonException("Invalid JSON."),
        new InvalidDataException("Unsupported version."),
        new UnauthorizedAccessException("Access denied."),
    };

    public static TheoryData<Exception, string> UnexpectedRosterFailures => new()
    {
        {
            new HttpRequestException("Invalid key.", null, HttpStatusCode.Unauthorized),
            "Supabase への接続に失敗しました。接続設定(URL・anon キー)を確認してください。"
        },
        {
            new HttpRequestException("Forbidden.", null, HttpStatusCode.Forbidden),
            "Supabase への接続に失敗しました。接続設定(URL・anon キー)を確認してください。"
        },
        {
            new IOException("Cache write failed."),
            "名簿キャッシュの書き込みに失敗しました。ディスク容量や権限を確認してください。"
        },
        {
            new UnauthorizedAccessException("Access to the cache path is denied."),
            "名簿キャッシュの書き込みに失敗しました。ディスク容量や権限を確認してください。"
        },
        {
            new JsonException("Invalid response contract."),
            "名簿データの形式が不正です。Supabase 側の machine_picklist ビューを確認してください。"
        },
        {
            new InvalidDataException("Invalid response contract."),
            "名簿データの形式が不正です。Supabase 側の machine_picklist ビューを確認してください。"
        },
    };

    [Theory]
    [MemberData(nameof(FilterSettingsReadFailures))]
    public async Task LoadFilterSettingsAsync_ReadFailureSetsWarningMessageInsteadOfThrowing(Exception readFailure)
    {
        ThrowingFilterSettingsStore filterSettingsStore = new(readFailure);
        RosterViewModel viewModel = new(filterSettingsStore);

        await viewModel.LoadFilterSettingsAsync();

        Assert.NotNull(viewModel.FilterSettingsWarningMessage);
        Assert.True(viewModel.HasFilterSettingsWarning);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LoadFilterSettingsAsync_WarningSurvivesRosterClientStateChanges()
    {
        ThrowingFilterSettingsStore filterSettingsStore = new(new JsonException("Invalid JSON."));
        RosterViewModel viewModel = new(filterSettingsStore);

        await viewModel.LoadFilterSettingsAsync();
        string? warningAfterLoad = viewModel.FilterSettingsWarningMessage;
        Assert.NotNull(warningAfterLoad);

        // 未接続状態への遷移(SetRosterClient(null))でも設定警告は消えない
        viewModel.SetRosterClient(null);
        Assert.Equal(warningAfterLoad, viewModel.FilterSettingsWarningMessage);

        // 名簿取得成功(RefreshAsync)でも設定警告は消えない
        RosterResult result = new(
            [new RosterEntry { ControlNumber = 100, Detail = "除雪車", IsTachoTarget = true }],
            RosterDataSource.Remote,
            RetrievedAt);
        viewModel.SetRosterClient(new StubRosterClient(result));
        await viewModel.RefreshAsync();

        Assert.Equal(warningAfterLoad, viewModel.FilterSettingsWarningMessage);
    }

    [Theory]
    [MemberData(nameof(UnexpectedRosterFailures))]
    public async Task RefreshAsync_UnexpectedFailureSetsSpecificErrorMessage(
        Exception remoteFailure,
        string expectedMessage)
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(new StubRosterClient(remoteFailure));

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsLoading);
        Assert.Equal(expectedMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_RosterUnavailableExceptionSetsErrorMessage()
    {
        RosterUnavailableException failure = new(
            "リモート名簿に接続できず、利用可能なローカルキャッシュもありません。",
            new InvalidOperationException());
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(new StubRosterClient(failure));

        await viewModel.RefreshAsync();

        Assert.Equal(failure.Message, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_SuccessPopulatesEntriesAndClearsErrorMessage()
    {
        RosterResult result = new(
            [new RosterEntry { ControlNumber = 100, Detail = "除雪車", IsTachoTarget = true }],
            RosterDataSource.Remote,
            RetrievedAt);
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(new StubRosterClient(result));

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public void SelectedEntryChange_RaisesEntryActivated()
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        RosterEntry entry = new() { ControlNumber = 100, Detail = "除雪車", IsTachoTarget = true };
        List<RosterEntry> activated = [];
        viewModel.EntryActivated += (_, activatedEntry) => activated.Add(activatedEntry);

        viewModel.SelectedEntry = entry;
        viewModel.SelectedEntry = null;

        Assert.Equal([entry], activated);
    }

    [Fact]
    public void ActivateEntry_RaisesEventForSameRowItemAgain()
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        RosterEntry entry = new() { ControlNumber = 100, Detail = "除雪車", IsTachoTarget = true };
        List<RosterEntry> activated = [];
        viewModel.EntryActivated += (_, activatedEntry) => activated.Add(activatedEntry);
        viewModel.SelectedEntry = entry;

        // 同じ行のダブルクリック(選択変更なし)でも再適用できる(FR-13)
        viewModel.ActivateEntry(entry);

        Assert.Equal([entry, entry], activated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("header")]
    public void ActivateEntry_NonRowItemDoesNothing(object? item)
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        int activatedCount = 0;
        viewModel.EntryActivated += (_, _) => activatedCount++;

        // ヘッダー・空白部など名簿行以外の操作では発火しない(FR-15 の手修正を上書きしない)
        viewModel.ActivateEntry(item);

        Assert.Equal(0, activatedCount);
    }

    [Fact]
    public async Task RefreshAsync_PopulatesVendorOptionsExcludingVendorsWithoutViewRanges()
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(
            new StubRosterClient(CreateRosterResult(100, 550)),
            new StubVendorClient(CreateVendorResult()));

        await viewModel.RefreshAsync();

        // 「全て」+ 閲覧範囲を持つ業者のみ。範囲行なし(admin)は除外される
        Assert.Equal([null, "arata"], viewModel.VendorOptions.Select(option => option.Code));
        Assert.Null(viewModel.VendorWarningMessage);
    }

    [Fact]
    public async Task SelectedVendorChange_FiltersEntriesByViewRangesAndPersistsCode()
    {
        RecordingFilterSettingsStore settingsStore = new();
        RosterViewModel viewModel = new(settingsStore);
        viewModel.SetRosterClient(
            new StubRosterClient(CreateRosterResult(100, 550)),
            new StubVendorClient(CreateVendorResult()));
        await viewModel.RefreshAsync();
        Assert.Equal(2, viewModel.Entries.Count);

        viewModel.SelectedVendorOption = viewModel.VendorOptions.Single(option => option.Code == "arata");

        RosterEntry entry = Assert.Single(viewModel.Entries);
        Assert.Equal(100, entry.ControlNumber);
        Assert.Equal("arata", settingsStore.LastWrittenSettings?.VendorCode);
    }

    [Fact]
    public async Task RefreshAsync_RestoresPersistedVendorSelection()
    {
        RecordingFilterSettingsStore settingsStore = new()
        {
            SavedSettings = new RosterFilterSettings { VendorCode = "arata" },
        };
        RosterViewModel viewModel = new(settingsStore);
        await viewModel.LoadFilterSettingsAsync();
        viewModel.SetRosterClient(
            new StubRosterClient(CreateRosterResult(100, 550)),
            new StubVendorClient(CreateVendorResult()));

        await viewModel.RefreshAsync();

        Assert.Equal("arata", viewModel.SelectedVendorOption?.Code);
        RosterEntry entry = Assert.Single(viewModel.Entries);
        Assert.Equal(100, entry.ControlNumber);
    }

    [Fact]
    public async Task RefreshAsync_PersistedVendorMissingFromListFallsBackToAll()
    {
        RecordingFilterSettingsStore settingsStore = new()
        {
            SavedSettings = new RosterFilterSettings { VendorCode = "deleted-vendor" },
        };
        RosterViewModel viewModel = new(settingsStore);
        await viewModel.LoadFilterSettingsAsync();
        viewModel.SetRosterClient(
            new StubRosterClient(CreateRosterResult(100, 550)),
            new StubVendorClient(CreateVendorResult()));

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.SelectedVendorOption?.Code);
        Assert.Equal(2, viewModel.Entries.Count);
    }

    [Fact]
    public async Task RefreshAsync_VendorFailureSetsWarningAndKeepsRosterUsable()
    {
        VendorUnavailableException vendorFailure = new(
            "リモート業者マスタに接続できず、利用可能なローカルキャッシュもありません。",
            new HttpRequestException("Network unavailable."));
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(
            new StubRosterClient(CreateRosterResult(100, 550)),
            new StubVendorClient(vendorFailure));

        await viewModel.RefreshAsync();

        Assert.NotNull(viewModel.VendorWarningMessage);
        Assert.True(viewModel.HasVendorWarning);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(2, viewModel.Entries.Count);
        VendorOption option = Assert.Single(viewModel.VendorOptions);
        Assert.Null(option.Code);
    }

    private static RosterResult CreateRosterResult(params long[] controlNumbers)
    {
        return new RosterResult(
            controlNumbers
                .Select(controlNumber => new RosterEntry
                {
                    ControlNumber = controlNumber,
                    Detail = "除雪車",
                    IsTachoTarget = true,
                })
                .ToArray(),
            RosterDataSource.Remote,
            RetrievedAt);
    }

    private static VendorResult CreateVendorResult()
    {
        return new VendorResult(
            [
                new VendorEntry
                {
                    Code = "arata",
                    DisplayName = "アラタ工業",
                    ViewRanges = [new CtrlNumRange(100, 499)],
                },
                new VendorEntry { Code = "admin", DisplayName = "管理者" },
            ],
            RosterDataSource.Remote,
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

        public Task<VendorResult> GetVendorsAsync(CancellationToken cancellationToken = default) =>
            _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<VendorResult>(_exception);
    }

    private sealed class RecordingFilterSettingsStore : IRosterFilterSettingsStore
    {
        public RosterFilterSettings? SavedSettings { get; init; }

        public RosterFilterSettings? LastWrittenSettings { get; private set; }

        public Task<RosterFilterSettings?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedSettings);

        public Task WriteAsync(RosterFilterSettings settings, CancellationToken cancellationToken = default)
        {
            LastWrittenSettings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingFilterSettingsStore : IRosterFilterSettingsStore
    {
        private readonly Exception _readException;

        public ThrowingFilterSettingsStore(Exception readException)
        {
            _readException = readException;
        }

        public Task<RosterFilterSettings?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<RosterFilterSettings?>(_readException);

        public Task WriteAsync(RosterFilterSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullFilterSettingsStore : IRosterFilterSettingsStore
    {
        public Task<RosterFilterSettings?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<RosterFilterSettings?>(null);

        public Task WriteAsync(RosterFilterSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

        public Task<RosterResult> GetRosterAsync(CancellationToken cancellationToken = default) =>
            _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<RosterResult>(_exception);
    }
}
