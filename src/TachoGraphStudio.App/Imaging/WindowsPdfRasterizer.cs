using System.Runtime.CompilerServices;

using TachoGraphStudio.Core.Imaging;

using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TachoGraphStudio.App.Imaging;

public sealed class WindowsPdfRasterizer : IPdfRasterizer
{
    // Windows.Data.Pdf の PdfPage.Size は 1/96 インチ単位
    private const double BaseDpi = 96.0;

    // A3 600dpi 級スキャンの品質を落とさない既定値（NFR-03）
    public const double DefaultDpi = 600.0;

    private readonly double _dpi;

    public WindowsPdfRasterizer(double dpi = DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        _dpi = dpi;
    }

    public async IAsyncEnumerable<byte[]> RasterizePagesAsync(
        string pdfPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(pdfPath).AsTask(cancellationToken);
        PdfDocument document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken);

        for (uint pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using PdfPage page = document.GetPage(pageIndex);
            PdfPageRenderOptions options = new()
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * _dpi / BaseDpi),
                DestinationHeight = (uint)Math.Round(page.Size.Height * _dpi / BaseDpi),
            };

            using InMemoryRandomAccessStream stream = new();
            await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken);

            byte[] pageBytes = new byte[stream.Size];
            using DataReader reader = new(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken);
            reader.ReadBytes(pageBytes);

            // WinRT 操作の完了後にキャンセルされたページを呼び出し元へ流さない
            cancellationToken.ThrowIfCancellationRequested();
            yield return pageBytes;
        }
    }
}
