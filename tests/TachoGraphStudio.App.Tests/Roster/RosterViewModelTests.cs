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

    public static TheoryData<Exception> UnexpectedRosterFailures => new()
    {
        new HttpRequestException("Invalid key.", null, HttpStatusCode.Unauthorized),
        new HttpRequestException("Forbidden.", null, HttpStatusCode.Forbidden),
        new IOException("Cache write failed."),
        new JsonException("Invalid response contract."),
    };

    [Theory]
    [MemberData(nameof(FilterSettingsReadFailures))]
    public async Task LoadFilterSettingsAsync_ReadFailureSetsErrorMessageInsteadOfThrowing(Exception readFailure)
    {
        ThrowingFilterSettingsStore filterSettingsStore = new(readFailure);
        RosterViewModel viewModel = new(filterSettingsStore);

        await viewModel.LoadFilterSettingsAsync();

        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(UnexpectedRosterFailures))]
    public async Task RefreshAsync_UnexpectedFailureSetsErrorMessageInsteadOfThrowing(Exception remoteFailure)
    {
        RosterViewModel viewModel = new(new NullFilterSettingsStore());
        viewModel.SetRosterClient(new StubRosterClient(remoteFailure));

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsLoading);
        Assert.NotNull(viewModel.ErrorMessage);
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
