using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using OpenCvSharp;

using TachoGraphStudio.App.Imaging;
using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.App.Stage;

// Core パイプライン(Load → Split → RemoveBackground)を結線し、表示用の ProcessedDisc を逐次供給する
public sealed class StagePipeline : IStagePipeline
{
    private const int ThumbnailLongSide = 160;

    private readonly SheetLoader _sheetLoader;
    private readonly SheetSplitter _splitter = new();
    private readonly BackgroundRemover _remover = new();
    private readonly DiscSplitOptions _pdfSplitOptions;
    private readonly DiscSplitOptions _imageSplitOptions;
    private readonly BackgroundRemovalOptions _removalOptions;

    public StagePipeline(
        SheetLoader sheetLoader,
        DiscSplitOptions? pdfSplitOptions = null,
        DiscSplitOptions? imageSplitOptions = null,
        BackgroundRemovalOptions? removalOptions = null)
    {
        ArgumentNullException.ThrowIfNull(sheetLoader);

        _sheetLoader = sheetLoader;
        // PDF は WindowsPdfRasterizer のレンダリング DPI が既知。JPEG は DPI 不明のまま
        // SheetSplitter のフォールバック最小サイズに任せる
        _pdfSplitOptions = pdfSplitOptions ?? new DiscSplitOptions { Dpi = WindowsPdfRasterizer.DefaultDpi };
        _imageSplitOptions = imageSplitOptions ?? new DiscSplitOptions();
        _removalOptions = removalOptions ?? new BackgroundRemovalOptions();
    }

    public async IAsyncEnumerable<ProcessedDisc> ProcessAsync(
        IReadOnlyList<string> paths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        await foreach (SheetImage sheet in _sheetLoader.LoadAsync(paths, cancellationToken))
        {
            DiscSplitOptions splitOptions =
                Path.GetExtension(sheet.SourcePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? _pdfSplitOptions
                    : _imageSplitOptions;

            // 円盤単位で逐次 yield し、後続円盤の失敗時も変換済みの円盤は呼び出し元へ届いた状態にする
            IReadOnlyList<DiscImage> discs = await Task.Run(
                () => _splitter.Split(sheet, splitOptions),
                cancellationToken);
            try
            {
                foreach (DiscImage disc in discs)
                {
                    yield return await Task.Run(() => ConvertDisc(disc), cancellationToken);
                }
            }
            finally
            {
                foreach (DiscImage disc in discs)
                {
                    disc.Dispose();
                }
            }
        }
    }

    private ProcessedDisc ConvertDisc(DiscImage disc)
    {
        using BackgroundRemovalResult removed = _remover.Remove(disc, _removalOptions);
        return ToProcessedDisc(disc, removed);
    }

    private static ProcessedDisc ToProcessedDisc(DiscImage disc, BackgroundRemovalResult removed)
    {
        using Mat premultiplied = Premultiply(removed.Pixels);

        double thumbnailScale = Math.Min(
            1.0,
            (double)ThumbnailLongSide / Math.Max(premultiplied.Width, premultiplied.Height));
        using Mat thumbnail = new();
        Cv2.Resize(
            premultiplied,
            thumbnail,
            new Size(
                Math.Max(1, (int)Math.Round(premultiplied.Width * thumbnailScale)),
                Math.Max(1, (int)Math.Round(premultiplied.Height * thumbnailScale))),
            0,
            0,
            InterpolationFlags.Area);

        return new ProcessedDisc(
            disc.SourcePath,
            disc.PageIndex,
            disc.Index,
            premultiplied.Width,
            premultiplied.Height,
            ToBytes(premultiplied),
            thumbnail.Width,
            thumbnail.Height,
            ToBytes(thumbnail),
            removed.Ellipse.Center.X - removed.RegionInDisc.X,
            removed.Ellipse.Center.Y - removed.RegionInDisc.Y);
    }

    // WriteableBitmap の PixelBuffer は premultiplied BGRA 前提のため、BGR 各チャンネルへ
    // アルファを乗算しておく
    private static Mat Premultiply(Mat bgra)
    {
        Mat[] channels = Cv2.Split(bgra);
        try
        {
            for (int channel = 0; channel < 3; channel++)
            {
                Cv2.Multiply(channels[channel], channels[3], channels[channel], 1.0 / 255.0);
            }

            Mat premultiplied = new();
            Cv2.Merge(channels, premultiplied);
            return premultiplied;
        }
        finally
        {
            foreach (Mat channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static byte[] ToBytes(Mat mat)
    {
        byte[] data = new byte[(int)(mat.Total() * mat.ElemSize())];
        Marshal.Copy(mat.Data, data, 0, data.Length);
        return data;
    }
}
