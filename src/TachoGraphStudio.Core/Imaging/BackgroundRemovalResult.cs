using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 背景除去後の円盤画像。透過 PNG 出力(FR-19)とプレビュー合成の入力になる
public sealed class BackgroundRemovalResult : IDisposable
{
    public BackgroundRemovalResult(Mat pixels, Rect regionInDisc, RotatedRect ellipse)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Pixels = pixels;
        RegionInDisc = regionInDisc;
        Ellipse = ellipse;
    }

    // BGRA。フィット楕円の外側は alpha=0、楕円の bbox にクロップ済み
    public Mat Pixels { get; }

    // 入力 DiscImage.Pixels 座標系でのクロップ領域
    public Rect RegionInDisc { get; }

    // 入力 DiscImage.Pixels 座標系でのフィット楕円(パディング適用後)。
    // 中心は回転補正の十字ガイド(FR-06)の基準に使える
    public RotatedRect Ellipse { get; }

    public void Dispose()
    {
        Pixels.Dispose();
    }
}
