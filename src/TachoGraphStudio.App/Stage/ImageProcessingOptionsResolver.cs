using TachoGraphStudio.App.Imaging;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Stage;

// 画像処理設定(FR-03)から Core の処理オプションを解決する。本処理(StagePipeline)と
// 設定画面の検出プレビュー(#126)が同じ入力・設定で同じ結果になるよう、解決はここへ集約する
public sealed class ImageProcessingOptionsResolver
{
    private readonly DiscSplitOptions _pdfSplitOptions;
    private readonly DiscSplitOptions _imageSplitOptions;
    private readonly BackgroundRemovalOptions _removalOptions;

    public ImageProcessingOptionsResolver(
        DiscSplitOptions? pdfSplitOptions = null,
        DiscSplitOptions? imageSplitOptions = null,
        BackgroundRemovalOptions? removalOptions = null)
    {
        // PDF は WindowsPdfRasterizer のレンダリング DPI が既知。JPEG は DPI 不明のまま
        // SheetSplitter のフォールバック最小サイズに任せる
        _pdfSplitOptions = pdfSplitOptions ?? new DiscSplitOptions { Dpi = WindowsPdfRasterizer.DefaultDpi };
        _imageSplitOptions = imageSplitOptions ?? new DiscSplitOptions();
        _removalOptions = removalOptions ?? new BackgroundRemovalOptions();
    }

    public DiscSplitOptions ResolveSplitOptions(string sourcePath, ImageProcessingSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        DiscSplitOptions baseOptions =
            Path.GetExtension(sourcePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? _pdfSplitOptions
                : _imageSplitOptions;
        if (settings is null)
        {
            return baseOptions;
        }

        settings.Validate();
        return baseOptions with
        {
            Threshold = settings.Threshold,
            PaddingPx = settings.PaddingPx,
        };
    }

    public BackgroundRemovalOptions ResolveRemovalOptions(ImageProcessingSettings? settings)
        => settings is null
            ? _removalOptions
            : _removalOptions with { EllipsePaddingPx = settings.EllipsePaddingPx };
}
