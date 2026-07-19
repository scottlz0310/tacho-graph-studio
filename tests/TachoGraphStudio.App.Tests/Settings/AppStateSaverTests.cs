using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Tests.Settings;

public sealed class AppStateSaverTests
{
    [Fact]
    public async Task TrySaveAsync_SuccessReturnsTrueAndClearsLastError()
    {
        FakeAppStateStore store = new();
        AppStateSaver saver = new(store);

        Assert.True(await saver.TrySaveAsync(new AppState()));
        Assert.Null(saver.LastError);
        Assert.Equal(1, store.WriteCount);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task TrySaveAsync_WriteFailureReturnsFalseWithLastError(Type exceptionType)
    {
        FakeAppStateStore store = new()
        {
            WriteException = (Exception)Activator.CreateInstance(exceptionType, "書き込み失敗")!,
        };
        AppStateSaver saver = new(store);

        Assert.False(await saver.TrySaveAsync(new AppState()));
        Assert.Contains("書き込み失敗", saver.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySaveAsync_SuccessAfterFailureClearsLastError()
    {
        FakeAppStateStore store = new() { WriteException = new IOException("一時的な失敗") };
        AppStateSaver saver = new(store);
        await saver.TrySaveAsync(new AppState());

        store.WriteException = null;

        Assert.True(await saver.TrySaveAsync(new AppState()));
        Assert.Null(saver.LastError);
    }

    [Fact]
    public void TryFlush_WriteFailureReturnsFalseWithoutThrowing()
    {
        FakeAppStateStore store = new() { WriteException = new IOException("ディスクエラー") };
        AppStateSaver saver = new(store);

        bool flushed = saver.TryFlush(new AppState(), TimeSpan.FromSeconds(5));

        Assert.False(flushed);
        Assert.Contains("ディスクエラー", saver.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFlush_TimeoutReturnsFalseWithoutThrowing()
    {
        FakeAppStateStore store = new() { WriteDelay = TimeSpan.FromSeconds(30) };
        AppStateSaver saver = new(store);

        bool flushed = saver.TryFlush(new AppState(), TimeSpan.FromMilliseconds(100));

        Assert.False(flushed);
        Assert.Contains("完了しませんでした", saver.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFlush_SuccessReturnsTrue()
    {
        FakeAppStateStore store = new();
        AppStateSaver saver = new(store);

        Assert.True(saver.TryFlush(new AppState(), TimeSpan.FromSeconds(5)));
        Assert.Null(saver.LastError);
    }

    private sealed class FakeAppStateStore : IAppStateStore
    {
        public Exception? WriteException { get; set; }

        public TimeSpan WriteDelay { get; set; }

        public int WriteCount { get; private set; }

        public Task<AppState?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AppState?>(null);

        public async Task WriteAsync(AppState state, CancellationToken cancellationToken = default)
        {
            if (WriteDelay > TimeSpan.Zero)
            {
                await Task.Delay(WriteDelay, cancellationToken);
            }

            if (WriteException is { } exception)
            {
                throw exception;
            }

            WriteCount++;
        }
    }
}
