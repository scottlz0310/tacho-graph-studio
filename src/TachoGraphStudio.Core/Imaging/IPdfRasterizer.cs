namespace TachoGraphStudio.Core.Imaging;

// PDF をページごとのエンコード済み画像バイト列に変換する。
// Windows.Data.Pdf（WinRT）依存の実装は App 側に置く（アーキテクチャ §3.1）
public interface IPdfRasterizer
{
    IAsyncEnumerable<byte[]> RasterizePagesAsync(string pdfPath, CancellationToken cancellationToken = default);
}
