using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// GIMP 版 TachoGraphWizard の split_by_auto_detect を OpenCvSharp へ移植したもの。
// 定数はすべて GIMP 版の実績値
public sealed class SheetSplitter
{
    // 解析はこの長辺サイズまで縮小して行う(ノイズ平滑化と NFR-03 の実用速度を両立)
    private const int AnalysisMaxSize = 1200;

    // タコグラフチャート紙の直径。実運用では 2 規格が混在する(#91 の実測で確認)
    private const double SmallDiscDiameterMm = 123.0;
    private const double LargeDiscDiameterMm = 125.0;

    // 規格径に対して許容する差。実測のばらつきは規格内で約 ±1mm だが、
    // 検出しきい値によるインクのにじみで系統的に +1mm ほど大きく出る
    private const double DiameterTolerance = 0.10;

    // 円盤は正円。スキャン誤差による縦横比のずれは実測 0.6% 以内だった
    private const double AspectTolerance = 0.10;

    // DPI 不明時のフォールバック(300dpi スキャン相当)。想定径を計算できないため
    // 上限を課さず、従来どおり最小サイズのみで判定する
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

        SizeRange sizeRange = AcceptableDiscSizePx(options.Dpi);
        List<Candidate> candidates = FindCandidates(mask, scaleX, scaleY, sizeRange);
        if (candidates.Count == 0)
        {
            throw new DiscSplitException(
                $"{sizeRange.Describe()} の円盤領域がありません: {sheet.SourcePath}（{sheet.PageIndex + 1} ページ目）");
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

    // DPI が既知なら規格径から採用範囲を確定する。旧実装は「直径の 2/3 以上」という
    // 下限のみで、上限が無いためシート全体を覆う誤検出を弾けず、代わりに充填率
    // (MinFillRatio) で除外していた。しかし白地に線画で印字されたチャート紙は
    // 充填率が誤検出と同オーダーまで下がり分離できない(#91)。規格径は固定なので
    // 上下限で判定する方が確実で、線画かどうかに左右されない
    private static SizeRange AcceptableDiscSizePx(double? dpi)
    {
        if (dpi is not (>= MinValidDpi and <= MaxValidDpi))
        {
            return new SizeRange(FallbackMinSizePx, null);
        }

        double pxPerMm = dpi.Value / 25.4;
        int min = (int)(SmallDiscDiameterMm * (1.0 - DiameterTolerance) * pxPerMm);
        int max = (int)(LargeDiscDiameterMm * (1.0 + DiameterTolerance) * pxPerMm);
        return new SizeRange(min, max);
    }

    private static List<Candidate> FindCandidates(
        Mat mask,
        double scaleX,
        double scaleY,
        SizeRange sizeRange)
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
            if (sizeRange.Contains(fullWidth) && sizeRange.Contains(fullHeight) && IsCircular(width, height))
            {
                candidates.Add(new Candidate(left, top, width, height, area));
            }
        }

        return candidates;
    }

    // 円盤は正円なので bbox はほぼ正方形になる。細長い異物を落とすための保険
    private static bool IsCircular(int width, int height)
        => height > 0 && Math.Abs((double)width / height - 1.0) <= AspectTolerance;

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

    // 採用する円盤サイズの範囲(フル解像度 px)。DPI 不明時は上限なし
    private readonly record struct SizeRange(int Min, int? Max)
    {
        public bool Contains(int sizePx) => sizePx >= Min && (Max is null || sizePx <= Max);

        public string Describe() => Max is null
            ? $"最小サイズ {Min}px 以上"
            : $"直径 {Min}〜{Max}px";
    }
}
