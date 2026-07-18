namespace TachoGraphStudio.Core.Templates;

// 画像左上を原点とした比率座標(0.0〜1.0)
public sealed record TextPosition
{
    public double XRatio { get; init; }

    public double YRatio { get; init; }
}
