using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

// 設定画面の検出プレビュー(#126)。入力変更をデバウンスし、実行中の検出をキャンセルして
// 最新の設定の結果だけを表示する。呼び出しは UI スレッドから直列に行われる前提で、
// 世代番号による latest-wins の判定はロックなしで行う
public sealed partial class DetectionPreviewViewModel : ObservableObject
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IDetectionPreviewRenderer _renderer;
    private readonly IImageSourceFactory _imageSourceFactory;
    private readonly TimeSpan _debounce;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _pending;
    private int _generation;

    public DetectionPreviewViewModel(
        IDetectionPreviewRenderer renderer,
        IImageSourceFactory imageSourceFactory,
        TimeSpan? debounce = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(imageSourceFactory);

        _renderer = renderer;
        _imageSourceFactory = imageSourceFactory;
        _debounce = debounce ?? DefaultDebounce;
        // 待機はテストから制御できるように差し替え可能にする
        _delayAsync = delayAsync ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));
    }

    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// <paramref name="settings"/> での検出プレビューを要求する。デバウンス待機中または
    /// 実行中の要求はキャンセルし、最新の要求の結果だけを反映する。
    /// </summary>
    public async Task RefreshAsync(ImageProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int generation = ++_generation;
        using CancellationTokenSource cancellation = new();
        _pending?.Cancel();
        _pending = cancellation;

        try
        {
            await _delayAsync(_debounce, cancellation.Token);
            IsRunning = true;
            DetectionPreviewImage preview = await _renderer.RenderAsync(settings, cancellation.Token);

            // キャンセルが間に合わずに完了した古い検出で、最新の結果を上書きしない
            if (generation != _generation)
            {
                return;
            }

            PreviewImage = _imageSourceFactory.Create(
                preview.PremultipliedBgra,
                preview.Width,
                preview.Height);
            StatusMessage = $"円盤を {preview.DiscCount} 枚検出しました。";
        }
        catch (OperationCanceledException)
        {
            // 新しい要求に置き換えられただけなので、表示は次の要求に任せる
        }
        catch (Exception exception)
            when (exception is SheetLoadException or DiscSplitException or BackgroundRemovalException)
        {
            if (generation == _generation)
            {
                PreviewImage = null;
                StatusMessage = exception.Message;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsRunning = false;
                // 破棄済みの CancellationTokenSource を後続の要求がキャンセルしないよう手放す
                _pending = null;
            }
        }
    }
}
