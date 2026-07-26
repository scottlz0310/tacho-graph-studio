namespace TachoGraphStudio.Core.Imaging;

public sealed record BackgroundRemovalOptions
{
    // アルファ円マスクの外側マージン(px)。正で縁を残す方向に広げ、負で内側に食い込ませる。
    // 円盤の中心と直径は分割時の検出結果(DiscImage)を使うため、
    // 前景判定のしきい値はここでは不要になった(#91)
    public int EllipsePaddingPx { get; init; }
}
