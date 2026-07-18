namespace TachoGraphStudio.Core.Imaging;

// 1 シート分のエンコード済み画像（JPEG/PNG バイト列）。複数ページ PDF は 1 ページ = 1 シートとして扱う。
// デコード（OpenCvSharp）は後段の分割処理（issue #8）が担う
public sealed record SheetImage(string SourcePath, int PageIndex, byte[] ImageBytes);
