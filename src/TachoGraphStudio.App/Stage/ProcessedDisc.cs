namespace TachoGraphStudio.App.Stage;

// パイプライン(読込→分割→背景除去)を通過した円盤 1 枚分のデータ。UI 型には依存しない。
// フル解像度はストレートアルファの BGRA で保持し、本合成(FR-19)の入力を無劣化に保つ。
// 表示用の premultiplied 変換は利用側(PremultipliedAlpha)で行う。
// サムネイルは縮小補間の色にじみを避けるため premultiplied のまま保持する(表示専用)
public sealed record ProcessedDisc(
    string SourcePath,
    int PageIndex,
    int IndexInSheet,
    int Width,
    int Height,
    byte[] Bgra,
    int ThumbnailWidth,
    int ThumbnailHeight,
    byte[] ThumbnailPremultipliedBgra,
    double EllipseCenterX,
    double EllipseCenterY);
