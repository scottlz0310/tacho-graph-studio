using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// GIMP 版 TachoGraphWizard の split_by_auto_detect を OpenCvSharp へ移植したもの。
// 定数はすべて GIMP 版の実績値
public sealed class SheetSplitter
{
    // 解析はこの長辺サイズまで縮小して行う(ノイズ平滑化と NFR-03 の実用速度を両立)
    private const int AnalysisMaxSize = 1200;

    // タコグラフチャート紙の直径
    private const double DiscDiameterMm = 123.5;

    // 円盤とみなす最小サイズ = 直径の 2/3
    private const double MinSizeRatio = 2.0 / 3.0;

    // DPI 不明時のフォールバック(300dpi スキャン相当)
    private const int FallbackMinSizePx = 1000;

    private const double MinValidDpi = 50.0;
    private const double MaxValidDpi = 1200.0;

    public IReadOnlyList<DiscImage> Split(SheetImage sheet, DiscSplitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        options ??= new DiscSplitOptions();
        ValidateOptions(options);

        using Mat pixels = DecodeSheet(sheet);

        double analysisScale = Math.Min(1.0, (double)AnalysisMaxSize / Math.Max(pixels.Width, pixels.Height));
        int analysisWidth = Math.Max(1, (int)Math.Round(pixels.Width * analysisScale));
        int analysisHeight = Math.Max(1, (int)Math.Round(pixels.Height * analysisScale));
        double scaleX = (double)analysisWidth / pixels.Width;
        double scaleY = (double)analysisHeight / pixels.Height;

        using Mat mask = BuildForegroundMask(pixels, analysisWidth, analysisHeight, options.Threshold);
        if (Cv2.CountNonZero(mask) == 0)
        {
            throw new DiscSplitException(
                $"円盤を検出できません（threshold={options.Threshold}）: {sheet.SourcePath}（{sheet.PageIndex + 1} ページ目）");
        }

        int minSizePx = MinimumDiscSizePx(options.Dpi);
        List<Candidate> candidates = FindCandidates(mask, scaleX, scaleY, minSizePx, options.MinFillRatio);
        if (candidates.Count == 0)
        {
            throw new DiscSplitException(
                $"最小サイズ {minSizePx}px 以上の円盤領域がありません: {sheet.SourcePath}（{sheet.PageIndex + 1} ページ目）");
        }

        if (candidates.Count > options.MaxDiscs)
        {
            candidates = [.. candidates.OrderByDescending(candidate => candidate.Area).Take(options.MaxDiscs)];
        }

        candidates.Sort((left, right) => left.Top != right.Top
            ? left.Top.CompareTo(right.Top)
            : left.Left.CompareTo(right.Left));

        List<DiscImage> discs = [];
        try
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                Rect region = ToPaddedFullResolutionRegion(
                    candidates[index],
                    scaleX,
                    scaleY,
                    options.PaddingPx,
                    pixels.Width,
                    pixels.Height);
                using Mat regionView = new(pixels, region);
                discs.Add(new DiscImage(regionView.Clone(), index, region, sheet.SourcePath, sheet.PageIndex));
            }
        }
        catch
        {
            foreach (DiscImage disc in discs)
            {
                disc.Dispose();
            }

            throw;
        }

        return discs;
    }

    private static void ValidateOptions(DiscSplitOptions options)
    {
        if (options.Threshold is < 1 or > 255)
        {
            throw new ArgumentException($"Threshold は 1〜255 で指定してください: {options.Threshold}", nameof(options));
        }

        if (options.PaddingPx < 0)
        {
            throw new ArgumentException($"PaddingPx は 0 以上で指定してください: {options.PaddingPx}", nameof(options));
        }

        if (options.MaxDiscs < 1)
        {
            throw new ArgumentException($"MaxDiscs は 1 以上で指定してください: {options.MaxDiscs}", nameof(options));
        }

        if (options.MinFillRatio is < 0.0 or > 1.0)
        {
            throw new ArgumentException($"MinFillRatio は 0〜1 で指定してください: {options.MinFillRatio}", nameof(options));
        }
    }

    private static Mat DecodeSheet(SheetImage sheet)
    {
        Mat pixels = Cv2.ImDecode(sheet.ImageBytes, ImreadModes.Color);
        if (pixels.Empty())
        {
            pixels.Dispose();
            throw new DiscSplitException(
                $"シート画像をデコードできません: {sheet.SourcePath}（{sheet.PageIndex + 1} ページ目）");
        }

        return pixels;
    }

    private static Mat BuildForegroundMask(Mat pixels, int analysisWidth, int analysisHeight, int threshold)
    {
        // GIMP 版と同じ最近傍サンプリングで縮小する
        using Mat analysis = new();
        Cv2.Resize(pixels, analysis, new Size(analysisWidth, analysisHeight), 0, 0, InterpolationFlags.Nearest);

        return ForegroundMask.Build(analysis, threshold);
    }

    private static int MinimumDiscSizePx(double? dpi)
    {
        if (dpi is >= MinValidDpi and <= MaxValidDpi)
        {
            return (int)(DiscDiameterMm / 25.4 * dpi.Value * MinSizeRatio);
        }

        return FallbackMinSizePx;
    }

    private static List<Candidate> FindCandidates(
        Mat mask,
        double scaleX,
        double scaleY,
        int minSizePx,
        double minFillRatio)
    {
        using Mat labels = new();
        using Mat stats = new();
        using Mat centroids = new();
        int componentCount = Cv2.ConnectedComponentsWithStats(
            mask,
            labels,
            stats,
            centroids,
            PixelConnectivity.Connectivity4);

        List<Candidate> candidates = [];
        for (int label = 1; label < componentCount; label++)
        {
            int left = stats.At<int>(label, (int)ConnectedComponentsTypes.Left);
            int top = stats.At<int>(label, (int)ConnectedComponentsTypes.Top);
            int width = stats.At<int>(label, (int)ConnectedComponentsTypes.Width);
            int height = stats.At<int>(label, (int)ConnectedComponentsTypes.Height);
            int area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);

            int fullWidth = (int)(width / scaleX);
            int fullHeight = (int)(height / scaleY);
            double fillRatio = (double)area / (width * height);
            if (fullWidth >= minSizePx && fullHeight >= minSizePx && fillRatio >= minFillRatio)
            {
                candidates.Add(new Candidate(left, top, width, height, area));
            }
        }

        return candidates;
    }

    private static Rect ToPaddedFullResolutionRegion(
        Candidate candidate,
        double scaleX,
        double scaleY,
        int paddingPx,
        int sheetWidth,
        int sheetHeight)
    {
        int x0 = Math.Max(0, (int)(candidate.Left / scaleX) - paddingPx);
        int y0 = Math.Max(0, (int)(candidate.Top / scaleY) - paddingPx);
        int x1 = Math.Min(sheetWidth, (int)((candidate.Left + candidate.Width) / scaleX) + paddingPx);
        int y1 = Math.Min(sheetHeight, (int)((candidate.Top + candidate.Height) / scaleY) + paddingPx);

        return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private readonly record struct Candidate(int Left, int Top, int Width, int Height, int Area);
}
