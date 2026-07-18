namespace TachoGraphStudio.App.Stage;

// パイプライン(読込→分割→背景除去)を通過した円盤 1 枚分の表示用データ。
// WriteableBitmap が要求する premultiplied BGRA で保持し、UI 型には依存しない
public sealed record ProcessedDisc(
    string SourcePath,
    int PageIndex,
    int IndexInSheet,
    int Width,
    int Height,
    byte[] PremultipliedBgra,
    int ThumbnailWidth,
    int ThumbnailHeight,
    byte[] ThumbnailPremultipliedBgra,
    double EllipseCenterX,
    double EllipseCenterY);
