using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 円盤の白地背景を除去しアルファチャンネル化する(FR-05)。
// 分割時に検出した中心と直径から円マスクを描く。以前は前景輪郭へ楕円をフィット
// (Cv2.FitEllipse)していたが、白地に線画で印字されたチャート紙では前景がリング状に
// しか出ずフィットが不安定になる。円盤の形と位置は分割の時点で確定しているため
// 再検出はせず、その結果をそのまま使う(#91)
public sealed class BackgroundRemover : IBackgroundRemover
{
    public BackgroundRemovalResult Remove(DiscImage disc, BackgroundRemovalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(disc);
        options ??= new BackgroundRemovalOptions();

        Mat pixels = disc.Pixels;
        float diameter = disc.DiscDiameter + (options.EllipsePaddingPx * 2);
        if (diameter < 1)
        {
            throw new ArgumentException(
                $"EllipsePaddingPx が負方向に大きすぎて円が消失します: {options.EllipsePaddingPx}",
                nameof(options));
        }

        RotatedRect circle = new(disc.DiscCenter, new Size2f(diameter, diameter), 0f);
        Rect region = circle.BoundingRect().Intersect(new Rect(0, 0, pixels.Width, pixels.Height));
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new BackgroundRemovalException($"円盤の領域が画像外です: {DescribeSource(disc)}");
        }

        using Mat alpha = new(pixels.Rows, pixels.Cols, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Ellipse(alpha, circle, Scalar.All(255), thickness: -1, lineType: LineTypes.AntiAlias);

        using Mat bgra = new();
        Cv2.CvtColor(pixels, bgra, ColorConversionCodes.BGR2BGRA);
        Cv2.InsertChannel(alpha, bgra, 3);

        using Mat regionView = new(bgra, region);
        return new BackgroundRemovalResult(regionView.Clone(), region, circle);
    }

    private static string DescribeSource(DiscImage disc)
        => $"{disc.SourcePath}（{disc.PageIndex + 1} ページ目 No.{disc.Index + 1}）";
}
