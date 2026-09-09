using Microsoft.UI.Xaml.Media;

using TachoGraphStudio.App.Settings;
using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Tests.Settings;

// 検出プレビュー(#126)のデバウンス・キャンセル・latest-wins を、実際の検出処理から切り離して固定する
public sealed class DetectionPreviewViewModelTests
{
    private static readonly TimeSpan TestDebounce = TimeSpan.FromMilliseconds(50);

    [Theory]
    [InlineData(1, 0, -5)]
    [InlineData(7, 20, 0)]
    [InlineData(255, 999, 30)]
    public async Task RefreshAsync_PassesRequestedSettingsToRenderer(
        int threshold,
        int paddingPx,
        int ellipsePaddingPx)
    {
        FakeDetectionPreviewRenderer renderer = new();
        DetectionPreviewViewModel viewModel = Create(renderer, ImmediateDelayAsync);

        await viewModel.RefreshAsync(new ImageProcessingSettings
        {
            Threshold = threshold,
            PaddingPx = paddingPx,
            EllipsePaddingPx = ellipsePaddingPx,
        });

        ImageProcessingSettings requested = Assert.Single(renderer.Requests);
        Assert.Equal(threshold, requested.Threshold);
        Assert.Equal(paddingPx, requested.PaddingPx);
        Assert.Equal(ellipsePaddingPx, requested.EllipsePaddingPx);
    }

    [Fact]
    public async Task RefreshAsync_RendersPreviewAndReportsDiscCount()
    {
        RecordingImageSourceFactory imageSourceFactory = new();
        FakeDetectionPreviewRenderer renderer = new((_, _) => Task.FromResult(Preview(discCount: 3)));
        DetectionPreviewViewModel viewModel = Create(renderer, ImmediateDelayAsync, imageSourceFactory);

        await viewModel.RefreshAsync(ImageProcessingSettings.Default);

        Assert.Equal([3], imageSourceFactory.CreatedWidths);
        Assert.Contains("3", RequireStatus(viewModel), StringComparison.Ordinal);
        Assert.False(viewModel.IsRunning);
    }

    // デバウンス中に届いた変更は、最後の 1 回だけを検出へ通す
    [Fact]
    public async Task RefreshAsync_CoalescesRequestsDuringDebounce()
    {
        ManualDelay delay = new();
        FakeDetectionPreviewRenderer renderer = new();
        DetectionPreviewViewModel viewModel = Create(renderer, delay.DelayAsync);

        Task first = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 10 });
        Task second = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 20 });
        delay.ReleaseAll();
        await Task.WhenAll(first, second);

        ImageProcessingSettings requested = Assert.Single(renderer.Requests);
        Assert.Equal(20, requested.Threshold);
        Assert.Equal([TestDebounce, TestDebounce], delay.Requested);
    }

    // 実行中の検出は、新しい要求が届いた時点でキャンセルする
    [Fact]
    public async Task RefreshAsync_CancelsRunningRenderWhenSuperseded()
    {
        ManualDelay delay = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<DetectionPreviewImage> running = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeDetectionPreviewRenderer renderer = new((settings, _) =>
        {
            started.TrySetResult();
            return settings.Threshold == 10 ? running.Task : Task.FromResult(Preview(discCount: 1));
        });
        DetectionPreviewViewModel viewModel = Create(renderer, delay.DelayAsync);

        Task first = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 10 });
        delay.ReleaseAll();
        await started.Task;

        Task second = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 20 });

        Assert.True(renderer.Tokens[0].IsCancellationRequested);

        // 検出処理はキャンセルを監視して終了する
        running.TrySetCanceled(renderer.Tokens[0]);
        delay.ReleaseAll();
        await Task.WhenAll(first, second);

        Assert.Equal(2, renderer.Requests.Count);
        Assert.False(viewModel.IsRunning);
    }

    // キャンセルが間に合わずに完了した古い結果で、最新の結果を上書きしない
    [Fact]
    public async Task RefreshAsync_StaleResultDoesNotOverwriteLatest()
    {
        ManualDelay delay = new();
        RecordingImageSourceFactory imageSourceFactory = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<DetectionPreviewImage> stale = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeDetectionPreviewRenderer renderer = new((settings, _) =>
        {
            started.TrySetResult();
            return settings.Threshold == 10 ? stale.Task : Task.FromResult(Preview(discCount: 2));
        });
        DetectionPreviewViewModel viewModel = Create(renderer, delay.DelayAsync, imageSourceFactory);

        Task first = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 10 });
        delay.ReleaseAll();
        await started.Task;

        Task second = viewModel.RefreshAsync(new ImageProcessingSettings { Threshold = 20 });
        delay.ReleaseAll();
        await second;

        // キャンセルを無視した古い検出が後から完了しても、表示は最新のままにする
        stale.TrySetResult(Preview(discCount: 9));
        await first;

        Assert.Equal([2], imageSourceFactory.CreatedWidths);
        Assert.Contains("2", RequireStatus(viewModel), StringComparison.Ordinal);
        Assert.DoesNotContain("9", RequireStatus(viewModel), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("split")]
    [InlineData("removal")]
    public async Task RefreshAsync_RenderFailureShowsMessageWithoutPreview(string failure)
    {
        Exception exception = failure switch
        {
            "load" => new SheetLoadException("読み込みに失敗しました"),
            "split" => new DiscSplitException("円盤を検出できません"),
            _ => new BackgroundRemovalException("背景除去の設定が円盤に適用できません"),
        };
        RecordingImageSourceFactory imageSourceFactory = new();
        FakeDetectionPreviewRenderer renderer = new((_, _) =>
            Task.FromException<DetectionPreviewImage>(exception));
        DetectionPreviewViewModel viewModel = Create(renderer, ImmediateDelayAsync, imageSourceFactory);

        await viewModel.RefreshAsync(ImageProcessingSettings.Default);

        Assert.Equal(exception.Message, viewModel.StatusMessage);
        Assert.Empty(imageSourceFactory.CreatedWidths);
        Assert.Null(viewModel.PreviewImage);
        Assert.False(viewModel.IsRunning);
    }

    private static DetectionPreviewViewModel Create(
        IDetectionPreviewRenderer renderer,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        IImageSourceFactory? imageSourceFactory = null)
        => new(
            renderer,
            imageSourceFactory ?? new RecordingImageSourceFactory(),
            TestDebounce,
            delayAsync);

    private static Task ImmediateDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static string RequireStatus(DetectionPreviewViewModel viewModel)
    {
        Assert.NotNull(viewModel.StatusMessage);
        return viewModel.StatusMessage;
    }

    // 検出枚数を幅として持たせ、どの結果が表示されたかを ImageSource 生成から特定する
    private static DetectionPreviewImage Preview(int discCount)
        => new(discCount, 1, new byte[discCount * 4], discCount);

    private sealed class FakeDetectionPreviewRenderer(
        Func<ImageProcessingSettings, CancellationToken, Task<DetectionPreviewImage>>? handler = null)
        : IDetectionPreviewRenderer
    {
        public List<ImageProcessingSettings> Requests { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<DetectionPreviewImage> RenderAsync(
            ImageProcessingSettings settings,
            CancellationToken cancellationToken)
        {
            Requests.Add(settings);
            Tokens.Add(cancellationToken);
            return handler is null
                ? Task.FromResult(Preview(discCount: 1))
                : handler(settings, cancellationToken);
        }
    }

    // デバウンス待機を明示的に進める。実時間に依存せずコアレスとキャンセルを検証する
    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _pending = [];

        public List<TimeSpan> Requested { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Requested.Add(delay);
            TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            _pending.Add(source);
            return source.Task;
        }

        public void ReleaseAll()
        {
            foreach (TaskCompletionSource source in _pending)
            {
                source.TrySetResult();
            }
        }
    }

    private sealed class RecordingImageSourceFactory : IImageSourceFactory
    {
        public List<int> CreatedWidths { get; } = [];

        public ImageSource? Create(byte[] premultipliedBgra, int width, int height)
        {
            CreatedWidths.Add(width);
            return null;
        }
    }
}
