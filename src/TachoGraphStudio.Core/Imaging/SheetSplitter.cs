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

    // 切り出しサイズ。規格径は固定なので検出サイズではなく規格値で切る(#91)。
    // 大きい方の 125mm に片側 1mm の余白を足した値で、600dpi では 3000px ちょうどになる。
    // 実測の中心ブレ(±0.2mm)と縦横比のずれ(0.6% ≒ 0.75mm)を吸収できる
    private const double CropDiameterMm = 127.0;

    // Hough フォールバックのパラメータ。Canny の上側しきい値は、このフォールバックが
    // 効くべき「薄い印字」を拾える値にする必要がある。実測では白地(255)との差が 15 の
    // 描線は 50 では 1 件も検出できず、30 以下で検出できた。実データ 3 シートでは
    // 50 と 30 のどちらでも誤検出は出ない
    private const double HoughCannyThreshold = 30.0;
    private const double HoughAccumulatorThreshold = 30.0;

    // Cv2.FitEllipse が要求する最小輪郭点数
    private const int MinContourPoints = 5;

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

        using Mat analysis = BuildAnalysisImage(pixels, analysisWidth, analysisHeight);
        using Mat mask = ForegroundMask.Build(analysis, options.Threshold);
        if (Cv2.CountNonZero(mask) == 0)
        {
            throw new DiscSplitException(
                $"円盤を検出できません（threshold={options.Threshold}）: {sheet.SourcePath}（{sheet.PageIndex + 1} ページ目）");
        }

        int? cropSizePx = FixedCropSizePx(options.Dpi);
        SizeRange sizeRange = AcceptableDiscSizePx(options.Dpi);
        List<Candidate> candidates = FindCandidates(
            mask,
            scaleX,
            scaleY,
            sizeRange,
            cropSizePx is { } cropPx ? new Size2f((float)(cropPx * scaleX), (float)(cropPx * scaleY)) : null);

        // 連結成分は円盤の輪郭が途切れず繋がっていることを前提にする。印字が薄いと
        // 外周リングが分断され、断片が最小サイズに届かず候補に残らない。Hough は
        // 勾配を見るため連結性を要求しないので、取りこぼしを補完する(#91)。
        // 1 枚も取れないケースに限らず、通常の円盤と薄い円盤が混在するシートでは
        // 薄い方だけが例外もなく欠落するため、常に実行して差分を足す
        MergeHoughCandidates(candidates, analysis, scaleX, scaleY, options.Dpi);
        candidates = RemoveNestedCandidates(candidates);

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
                Candidate candidate = candidates[index];
                (Mat cropped, Rect region) = cropSizePx is { } size
                    ? CropFixedSize(pixels, candidate, scaleX, scaleY, size)
                    : CropBoundingBox(pixels, candidate, scaleX, scaleY, options.PaddingPx);

                // 解析スケールの幾何情報を切り出し画像の座標系へ移す
                Point2f discCenter = new(
                    (float)((candidate.Center.X / scaleX) - region.X),
                    (float)((candidate.Center.Y / scaleY) - region.Y));
                float discDiameter = (float)(candidate.Diameter / ((scaleX + scaleY) / 2.0));

                discs.Add(new DiscImage(
                    cropped, index, region, sheet.SourcePath, sheet.PageIndex, discCenter, discDiameter));
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

    private static Mat BuildAnalysisImage(Mat pixels, int analysisWidth, int analysisHeight)
    {
        // GIMP 版と同じ最近傍サンプリングで縮小する
        Mat analysis = new();
        Cv2.Resize(pixels, analysis, new Size(analysisWidth, analysisHeight), 0, 0, InterpolationFlags.Nearest);
        return analysis;
    }

    // Hough で検出した円のうち、連結成分側に対応する候補が無いものを追加する。
    // 連結成分の方が輪郭全体を使うぶん位置精度が高いので、重複時は既存を優先する
    private static void MergeHoughCandidates(
        List<Candidate> candidates,
        Mat analysis,
        double scaleX,
        double scaleY,
        double? dpi)
    {
        foreach (Candidate hough in FindCandidatesByHough(analysis, scaleX, scaleY, dpi))
        {
            if (!candidates.Any(existing => IsSameDisc(existing, hough)))
            {
                candidates.Add(hough);
            }
        }
    }

    // 円盤の内側にある印刷リングが独立した候補として残ることがある。DPI が既知なら
    // 規格径の上限で落ちるが、DPI 不明時は最小サイズしか課せないため通ってしまう
    // (実測では A4 単票で直径 2967px の円盤に加え 1911px の内側リングを検出した)。
    // 円盤同士は重ならないので、他の候補に完全に含まれる領域は円盤ではない(#91)
    private static List<Candidate> RemoveNestedCandidates(List<Candidate> candidates)
    {
        List<Candidate> result = [];
        for (int index = 0; index < candidates.Count; index++)
        {
            bool nested = false;
            for (int other = 0; other < candidates.Count; other++)
            {
                if (other != index && ContainsCandidate(candidates[other], candidates[index]))
                {
                    nested = true;
                    break;
                }
            }

            if (!nested)
            {
                result.Add(candidates[index]);
            }
        }

        return result;
    }

    // 完全に含まれ、かつ少なくとも一辺が小さいこと。同一サイズの候補が互いを
    // 消し合わないようにする
    private static bool ContainsCandidate(Candidate outer, Candidate inner)
        => outer.Left <= inner.Left
        && outer.Top <= inner.Top
        && inner.Left + inner.Width <= outer.Left + outer.Width
        && inner.Top + inner.Height <= outer.Top + outer.Height
        && (outer.Width > inner.Width || outer.Height > inner.Height);

    // 円盤同士は重ならないため、中心が直径の半分より近ければ同じ円盤とみなせる
    private static bool IsSameDisc(Candidate left, Candidate right)
    {
        double dx = left.Center.X - right.Center.X;
        double dy = left.Center.Y - right.Center.Y;
        double limit = Math.Min(left.Diameter, right.Diameter) / 2.0;
        return (dx * dx) + (dy * dy) < limit * limit;
    }

    // 円盤は直径が規格で固定されているため半径の探索範囲を厳しく絞れる。範囲を緩めると
    // 誤検出が激増する(実測で 5 枚のシートに対し 15 個検出)ので、DPI が既知で規格径を
    // 計算できる場合にのみ使う(#91)
    private static List<Candidate> FindCandidatesByHough(Mat analysis, double scaleX, double scaleY, double? dpi)
    {
        if (dpi is not (>= MinValidDpi and <= MaxValidDpi))
        {
            return [];
        }

        double analysisScale = (scaleX + scaleY) / 2.0;
        double pxPerMm = dpi.Value / 25.4 * analysisScale;
        int minRadius = (int)(SmallDiscDiameterMm * (1.0 - DiameterTolerance) / 2 * pxPerMm);
        int maxRadius = (int)(LargeDiscDiameterMm * (1.0 + DiameterTolerance) / 2 * pxPerMm);
        if (minRadius < 1 || maxRadius <= minRadius)
        {
            return [];
        }

        using Mat gray = new();
        Cv2.CvtColor(analysis, gray, ColorConversionCodes.BGR2GRAY);
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(9, 9), 2);

        CircleSegment[] circles = Cv2.HoughCircles(
            blurred,
            HoughModes.Gradient,
            dp: 1,
            // 円盤は重ならないので、中心間は小さい方の直径の 9 割以上離れている
            minDist: SmallDiscDiameterMm * 0.9 * pxPerMm,
            param1: HoughCannyThreshold,
            param2: HoughAccumulatorThreshold,
            minRadius: minRadius,
            maxRadius: maxRadius);

        List<Candidate> candidates = [];
        foreach (CircleSegment circle in circles)
        {
            int diameter = (int)Math.Round(circle.Radius * 2);
            int left = (int)Math.Round(circle.Center.X - circle.Radius);
            int top = (int)Math.Round(circle.Center.Y - circle.Radius);

            // 面積は MaxDiscs 超過時の並べ替えにしか使わないため円の面積で足りる
            int area = (int)(Math.PI * circle.Radius * circle.Radius);
            candidates.Add(new Candidate(left, top, diameter, diameter, area, circle.Center, circle.Radius * 2));
        }

        return candidates;
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

    // DPI が既知なら規格サイズで切り出す。検出された bbox の大きさは前景判定の
    // しきい値によるインクのにじみで ±1mm 程度ぶれるが、規格径は固定なので
    // 検出サイズではなく規格値で切る方が出力が安定する(#91)
    private static int? FixedCropSizePx(double? dpi)
        => dpi is >= MinValidDpi and <= MaxValidDpi
            ? (int)Math.Round(CropDiameterMm / 25.4 * dpi.Value)
            : null;

    private static List<Candidate> FindCandidates(
        Mat mask,
        double scaleX,
        double scaleY,
        SizeRange sizeRange,
        Size2f? cropInAnalysis)
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
                (Point2f center, float diameter) = DetectGeometry(
                    labels, label, left, top, width, height, cropInAnalysis);
                candidates.Add(new Candidate(left, top, width, height, area, center, diameter));
            }
        }

        return candidates;
    }

    // 外周輪郭への楕円フィットで中心と直径を求める。しきい値を振ったときの中心のブレは
    // 実測で bbox 中心の 1/10 以下だった。ただし線画の円盤では稀にフィットが外れるため、
    // 採用可否は「その中心で検出領域が切り出しに収まるか」で判定する(#91)。
    // 切り出しの余白は片側 1mm しかなく、経験的な許容割合では円盤の外周を
    // 切り落とす中心を通してしまうため、余白そのものを基準にする。
    // 直径は背景除去のアルファ円マスクに使う。突起があると bbox は広がるが
    // 楕円フィットは引きずられにくいため、採用時はフィット結果を使う
    private static (Point2f Center, float Diameter) DetectGeometry(
        Mat labels,
        int label,
        int left,
        int top,
        int width,
        int height,
        Size2f? cropInAnalysis)
    {
        Point2f boundingBoxCenter = new(left + (width / 2f), top + (height / 2f));
        float boundingBoxDiameter = (width + height) / 2f;

        using Mat component = new();
        Cv2.Compare(labels, label, component, CmpTypes.EQ);
        Cv2.FindContours(
            component,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxNone);

        Point[]? outer = contours.MaxBy(contour => Cv2.ContourArea(contour));
        if (outer is not { Length: >= MinContourPoints })
        {
            return (boundingBoxCenter, boundingBoxDiameter);
        }

        RotatedRect fitted = Cv2.FitEllipse(outer);
        float fittedDiameter = (fitted.Size.Width + fitted.Size.Height) / 2f;

        // 中心と直径は必ず同じ出所の組で返す。bbox 中心にフィット直径を組み合わせると、
        // 非対称な突起で bbox 中心がずれたときに真の円径のマスクごと移動し、
        // 反対側の外周を切り落とす
        if (cropInAnalysis is not { } crop)
        {
            // DPI 不明時は固定サイズ切り出しを行わないため収まり判定の基準がない。
            // 背景除去のマスクには中心精度が要るのでフィット結果をそのまま採る
            return (fitted.Center, fittedDiameter);
        }

        float halfWidth = crop.Width / 2f;
        float halfHeight = crop.Height / 2f;
        bool keepsDiscInside =
            fitted.Center.X - halfWidth <= left
            && left + width <= fitted.Center.X + halfWidth
            && fitted.Center.Y - halfHeight <= top
            && top + height <= fitted.Center.Y + halfHeight;

        // 収まらない場合は bbox 中心と bbox 直径の組を採る。検出領域が切り出しより
        // 大きいときはどの中心でも収まらないが、bbox 中心なら欠損が四方へ均等に分かれ、
        // マスクは検出領域全体を覆う
        return keepsDiscInside
            ? (fitted.Center, fittedDiameter)
            : (boundingBoxCenter, boundingBoxDiameter);
    }

    // 円盤は正円なので bbox はほぼ正方形になる。細長い異物を落とすための保険
    private static bool IsCircular(int width, int height)
        => height > 0 && Math.Abs((double)width / height - 1.0) <= AspectTolerance;

    // 規格サイズの正方形を中心に合わせて切り出す。シート外へはみ出す場合も切り縮めず、
    // 不足分を白で埋めて必ず同じ寸法にする。これにより円盤は常に画像中心に来る
    private static (Mat Cropped, Rect Region) CropFixedSize(
        Mat sheet,
        Candidate candidate,
        double scaleX,
        double scaleY,
        int sizePx)
    {
        int centerX = (int)Math.Round(candidate.Center.X / scaleX);
        int centerY = (int)Math.Round(candidate.Center.Y / scaleY);
        Rect region = new(centerX - (sizePx / 2), centerY - (sizePx / 2), sizePx, sizePx);

        Mat cropped = new(sizePx, sizePx, sheet.Type(), Scalar.All(255));
        Rect source = region.Intersect(new Rect(0, 0, sheet.Width, sheet.Height));
        if (source is { Width: > 0, Height: > 0 })
        {
            using Mat sourceView = new(sheet, source);
            using Mat target = new(
                cropped,
                new Rect(source.X - region.X, source.Y - region.Y, source.Width, source.Height));
            sourceView.CopyTo(target);
        }

        return (cropped, region);
    }

    // DPI 不明時は規格サイズを算出できないため、従来どおり bbox にパディングを付けて切り出す
    private static (Mat Cropped, Rect Region) CropBoundingBox(
        Mat sheet,
        Candidate candidate,
        double scaleX,
        double scaleY,
        int paddingPx)
    {
        int x0 = Math.Max(0, (int)(candidate.Left / scaleX) - paddingPx);
        int y0 = Math.Max(0, (int)(candidate.Top / scaleY) - paddingPx);
        int x1 = Math.Min(sheet.Width, (int)((candidate.Left + candidate.Width) / scaleX) + paddingPx);
        int y1 = Math.Min(sheet.Height, (int)((candidate.Top + candidate.Height) / scaleY) + paddingPx);

        Rect region = new(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        using Mat regionView = new(sheet, region);
        return (regionView.Clone(), region);
    }

    private readonly record struct Candidate(
        int Left,
        int Top,
        int Width,
        int Height,
        int Area,
        Point2f Center,
        float Diameter);

    // 採用する円盤サイズの範囲(フル解像度 px)。DPI 不明時は上限なし
    private readonly record struct SizeRange(int Min, int? Max)
    {
        public bool Contains(int sizePx) => sizePx >= Min && (Max is null || sizePx <= Max);

        public string Describe() => Max is null
            ? $"最小サイズ {Min}px 以上"
            : $"直径 {Min}〜{Max}px";
    }
}
