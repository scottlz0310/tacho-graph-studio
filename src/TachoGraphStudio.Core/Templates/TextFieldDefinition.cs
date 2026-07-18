namespace TachoGraphStudio.Core.Templates;

public sealed record TextFieldDefinition
{
    public TextPosition Position { get; init; } = new();

    public TextFont Font { get; init; } = new();

    public TextAlignment Align { get; init; } = TextAlignment.Left;

    public VerticalTextAlignment VerticalAlign { get; init; } = VerticalTextAlignment.Top;

    public bool Visible { get; init; } = true;

    public bool Required { get; init; }

    // 文字入れ座標の計算。位置は画像サイズ比、フォントサイズは短辺比(GIMP 版 text_renderer.py と同じ規則)
    public TextPlacement CalculatePlacement(int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);

        return new TextPlacement(
            Position.XRatio * imageWidth,
            Position.YRatio * imageHeight,
            Math.Min(imageWidth, imageHeight) * Font.SizeRatio);
    }
}
