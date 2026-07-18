namespace TachoGraphStudio.Core.Templates;

public sealed record TextFont
{
    public string Family { get; init; } = "Arial";

    // フォントサイズは画像短辺に対する比率
    public double SizeRatio { get; init; } = 0.03;

    // #RRGGBB 形式
    public string Color { get; init; } = "#000000";

    public bool Bold { get; init; }

    public bool Italic { get; init; }
}
