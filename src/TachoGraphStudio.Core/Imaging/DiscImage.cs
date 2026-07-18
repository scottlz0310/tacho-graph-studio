using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// シートから切り出した円盤 1 枚分のワークアイテム。サムネイルナビ(No.1〜6)の単位になる
public sealed class DiscImage : IDisposable
{
    public DiscImage(Mat pixels, int index, Rect regionInSheet, string sourcePath, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Pixels = pixels;
        Index = index;
        RegionInSheet = regionInSheet;
        SourcePath = sourcePath;
        PageIndex = pageIndex;
    }

    // BGR フル解像度の切り出し画像
    public Mat Pixels { get; }

    // シート内の位置順(上→下、左→右)の 0 始まり連番
    public int Index { get; }

    // シートのフル解像度座標系での切り出し領域(パディング込み)
    public Rect RegionInSheet { get; }

    public string SourcePath { get; }

    public int PageIndex { get; }

    public void Dispose()
    {
        Pixels.Dispose();
    }
}
