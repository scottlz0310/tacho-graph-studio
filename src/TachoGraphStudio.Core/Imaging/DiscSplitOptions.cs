namespace TachoGraphStudio.Core.Imaging;

// 既定値は GIMP 版 TachoGraphWizard(image_splitter.py)の実績値だが、
// Threshold は実スキャンでの実測にもとづき調整している(#91)
public sealed record DiscSplitOptions
{
    // 前景判定: 255 - min(R,G,B) >= Threshold。
    // 旧既定値 15 では白地に淡い緑で印字されたチャート紙(未記入の Task-Meter 等)の
    // 外周スケールリングが分断され、連結成分が最小サイズに届かなかった。
    // 実測では 8 以下で 3 種のシートすべてを正しく検出できる(#91)
    public int Threshold { get; init; } = 7;

    // 検出領域の周囲に付加する余白(フル解像度 px)。後段の背景除去(issue #9)の作業領域を確保する。
    // Dpi が既知の場合は規格サイズ(直径 125mm + 片側 1mm)で切り出すため参照されない(#91)
    public int PaddingPx { get; init; } = 20;

    // FR-01: 1 シートに最大 6 枚。超過分はノイズとみなし面積の大きい順に採用する
    public int MaxDiscs { get; init; } = 6;

    // 円盤の最小サイズ計算に使う入力画像の DPI。null または実用範囲外(50〜1200)の場合は
    // 300dpi スキャン相当の固定値にフォールバックする
    public double? Dpi { get; init; }
}
