namespace TachoGraphStudio.Core.Imaging;

// 既定値は GIMP 版 TachoGraphWizard(image_splitter.py)の実績値
public sealed record DiscSplitOptions
{
    // 前景判定: 255 - min(R,G,B) >= Threshold(白地 255 上の薄いグレー ~240 を検出できる値)
    public int Threshold { get; init; } = 15;

    // 検出領域の周囲に付加する余白(フル解像度 px)。後段の背景除去(issue #9)の作業領域を確保する
    public int PaddingPx { get; init; } = 20;

    // FR-01: 1 シートに最大 6 枚。超過分はノイズとみなし面積の大きい順に採用する
    public int MaxDiscs { get; init; } = 6;

    // 円盤の最小サイズ計算に使う入力画像の DPI。null または実用範囲外(50〜1200)の場合は
    // 300dpi スキャン相当の固定値にフォールバックする
    public double? Dpi { get; init; }

    // 成分面積 ÷ bbox 面積の下限。GIMP 版にはないフィルタだが、スキャナ縁の黒帯がページを
    // 一周して bbox がシート全体になる誤検出(実データで fill=0.013、実円盤は 0.77 前後)を除外する
    public double MinFillRatio { get; init; } = 0.4;
}
