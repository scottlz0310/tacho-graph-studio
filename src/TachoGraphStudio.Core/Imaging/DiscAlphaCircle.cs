using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 背景除去(FR-05)で描くアルファ円の幾何。本処理(BackgroundRemover)と設定画面の
// 検出プレビュー(#126)が同じ円を示すため、半径の決定はここへ集約する
public static class DiscAlphaCircle
{
    /// <summary>
    /// 分割時の検出結果と <paramref name="options"/> のマージンからアルファ円を求める。
    /// </summary>
    /// <param name="center"><paramref name="imageSize"/> の座標系での円盤中心。</param>
    /// <param name="discDiameter">分割時に検出した円盤の直径。</param>
    /// <param name="imageSize">円マスクを描く対象画像のサイズ。</param>
    /// <param name="options">アルファ円マージンを含む背景除去の設定。</param>
    public static RotatedRect Resolve(
        Point2f center,
        float discDiameter,
        Size imageSize,
        BackgroundRemovalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        double requestedDiameter = discDiameter + (options.EllipsePaddingPx * 2.0);
        if (requestedDiameter < 1)
        {
            throw new ArgumentException(
                $"EllipsePaddingPx が負方向に大きすぎて円が消失します: {options.EllipsePaddingPx}",
                nameof(options));
        }

        // 画像全体を覆う直径より大きな正のマージンは結果を変えない。ユーザー入力が
        // 極端でも OpenCV の座標変換をオーバーフローさせないよう有効径へ制限する
        double maxRadius = new[]
        {
            Distance(center.X, center.Y),
            Distance(imageSize.Width - center.X, center.Y),
            Distance(center.X, imageSize.Height - center.Y),
            Distance(imageSize.Width - center.X, imageSize.Height - center.Y),
        }.Max();
        float diameter = (float)Math.Min(requestedDiameter, (maxRadius * 2) + 1);

        return new RotatedRect(center, new Size2f(diameter, diameter), 0f);
    }

    private static double Distance(double x, double y) => Math.Sqrt((x * x) + (y * y));
}
