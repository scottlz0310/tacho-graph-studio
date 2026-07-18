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
