using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 円盤の白地背景を楕円フィットで除去しアルファチャンネル化する(FR-05)。
// GIMP 版は画像端からの内接楕円だったが、bbox が端で切り詰められた円盤にも頑健なように
// 前景輪郭への実フィット(Cv2.FitEllipse)へ置き換えている。前景判定は分割と同じ実績値
public sealed class BackgroundRemover
{
    // Cv2.FitEllipse が要求する最小輪郭点数
    private const int MinContourPoints = 5;

    public BackgroundRemovalResult Remove(DiscImage disc, BackgroundRemovalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(disc);
        options ??= new BackgroundRemovalOptions();
        if (options.Threshold is < 1 or > 255)
        {
            throw new ArgumentException($"Threshold は 1〜255 で指定してください: {options.Threshold}", nameof(options));
        }

        Mat pixels = disc.Pixels;
        RotatedRect ellipse = FitDiscEllipse(pixels, options.Threshold, disc);

        RotatedRect padded = new(
            ellipse.Center,
            new Size2f(
                ellipse.Size.Width + options.EllipsePaddingPx * 2,
                ellipse.Size.Height + options.EllipsePaddingPx * 2),
            ellipse.Angle);
        if (padded.Size.Width < 1 || padded.Size.Height < 1)
        {
            throw new ArgumentException(
                $"EllipsePaddingPx が負方向に大きすぎて楕円が消失します: {options.EllipsePaddingPx}",
                nameof(options));
        }

        Rect region = padded.BoundingRect().Intersect(new Rect(0, 0, pixels.Width, pixels.Height));
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new BackgroundRemovalException(
                $"フィット楕円が画像外です: {DescribeSource(disc)}");
        }

        using Mat alpha = new(pixels.Rows, pixels.Cols, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Ellipse(alpha, padded, Scalar.All(255), thickness: -1, lineType: LineTypes.AntiAlias);

        using Mat bgra = new();
        Cv2.CvtColor(pixels, bgra, ColorConversionCodes.BGR2BGRA);
        Cv2.InsertChannel(alpha, bgra, 3);

        using Mat regionView = new(bgra, region);
        return new BackgroundRemovalResult(regionView.Clone(), region, padded);
    }

    private static RotatedRect FitDiscEllipse(Mat pixels, int threshold, DiscImage disc)
    {
        using Mat mask = ForegroundMask.Build(pixels, threshold);
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            throw new BackgroundRemovalException(
                $"円盤の輪郭を検出できません（threshold={threshold}）: {DescribeSource(disc)}");
        }

        Point[] largest = contours.MaxBy(contour => Cv2.ContourArea(contour))!;
        if (largest.Length < MinContourPoints)
        {
            throw new BackgroundRemovalException(
                $"輪郭点数が {MinContourPoints} 点未満で楕円フィットできません: {DescribeSource(disc)}");
        }

        return Cv2.FitEllipse(largest);
    }

    private static string DescribeSource(DiscImage disc)
        => $"{disc.SourcePath}（{disc.PageIndex + 1} ページ目 No.{disc.Index + 1}）";
}
