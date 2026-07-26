using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// シートから切り出した円盤 1 枚分のワークアイテム。サムネイルナビ(No.1〜6)の単位になる
public sealed class DiscImage : IDisposable
{
    public DiscImage(
        Mat pixels,
        int index,
        Rect regionInSheet,
        string sourcePath,
        int pageIndex,
        Point2f discCenter,
        float discDiameter)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discDiameter);

        Pixels = pixels;
        Index = index;
        RegionInSheet = regionInSheet;
        SourcePath = sourcePath;
        PageIndex = pageIndex;
        DiscCenter = discCenter;
        DiscDiameter = discDiameter;
    }

    // BGR フル解像度の切り出し画像
    public Mat Pixels { get; }

    // シート内の位置順(上→下、左→右)の 0 始まり連番
    public int Index { get; }

    // シートのフル解像度座標系での切り出し領域(パディング込み)
    public Rect RegionInSheet { get; }

    // Pixels 座標系での円盤の中心。分割時の検出結果であり、背景除去はこれを使って
    // アルファの円マスクを描く。自前で再検出しないため線画の円盤でも破綻しない(#91)
    public Point2f DiscCenter { get; }

    // Pixels 座標系での円盤の直径(検出値)
    public float DiscDiameter { get; }

    public string SourcePath { get; }

    public int PageIndex { get; }

    public void Dispose()
    {
        Pixels.Dispose();
    }
}
