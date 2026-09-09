using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 分割結果のうち幾何情報だけを取り出したもの。設定画面の検出プレビュー(#126)は
// 画素を必要としないため、切り出し済みの Mat を持たずに座標だけを受け渡す
public sealed record DiscDetection(Rect CropRegionInSheet, Point2f CenterInCrop, float Diameter)
{
    // シートのフル解像度座標系での円盤中心
    public Point2f CenterInSheet => new(
        CenterInCrop.X + CropRegionInSheet.X,
        CenterInCrop.Y + CropRegionInSheet.Y);
}
