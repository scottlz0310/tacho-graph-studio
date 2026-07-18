namespace TachoGraphStudio.Core.Templates;

// 特定の画像サイズに対する文字入れ座標(px)。X/Y は Align/VerticalAlign の基準点
public sealed record TextPlacement(double X, double Y, double FontSizePx);
