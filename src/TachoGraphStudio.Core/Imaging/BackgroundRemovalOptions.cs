namespace TachoGraphStudio.Core.Imaging;

public sealed record BackgroundRemovalOptions
{
    // 前景判定: 255 - min(R,G,B) >= Threshold。円盤分割(DiscSplitOptions)と同じ GIMP 版実績値
    public int Threshold { get; init; } = 15;

    // フィット楕円の外側マージン(px)。正で縁を残す方向に広げ、負で内側に食い込ませる
    public int EllipsePaddingPx { get; init; }
}
