using System.Runtime.CompilerServices;

using TachoGraphStudio.Core.Imaging;

using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TachoGraphStudio.App.Imaging;

public sealed class WindowsPdfRasterizer : IPdfRasterizer
{
    // Windows.Data.Pdf の PdfPage.Size と DestinationWidth/Height は DIP (1/96 インチ) 単位。
    // RenderToStreamAsync のビットマップはプロセスのシステム DPI を適用した物理ピクセルになる。
    private const double BaseDpi = 96.0;

    // A3 600dpi 級スキャンの品質を落とさない既定値（NFR-03）
    public const double DefaultDpi = 600.0;

    private readonly double _dpi;
    private readonly Func<double> _systemRasterizationScaleProvider;

    public WindowsPdfRasterizer(
        Func<double> systemRasterizationScaleProvider,
        double dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(systemRasterizationScaleProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        _systemRasterizationScaleProvider = systemRasterizationScaleProvider;
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
            double systemRasterizationScale = _systemRasterizationScaleProvider();
            if (!double.IsFinite(systemRasterizationScale) || systemRasterizationScale <= 0)
            {
                throw new InvalidOperationException(
                    $"PDF のシステムレンダリング倍率が不正です: {systemRasterizationScale}");
            }

            PdfPageRenderOptions options = new()
            {
                DestinationWidth = CalculateDestinationLength(page.Size.Width, _dpi, systemRasterizationScale),
                DestinationHeight = CalculateDestinationLength(page.Size.Height, _dpi, systemRasterizationScale),
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

    internal static uint CalculateDestinationLength(
        double pageLengthDip,
        double dpi,
        double systemRasterizationScale)
    {
        double destinationLengthDip = pageLengthDip * dpi / BaseDpi / systemRasterizationScale;
        if (!double.IsFinite(destinationLengthDip)
            || destinationLengthDip < 1
            || destinationLengthDip > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageLengthDip),
                $"PDF のレンダリングサイズが範囲外です: {destinationLengthDip}");
        }

        return checked((uint)Math.Round(destinationLengthDip, MidpointRounding.AwayFromZero));
    }
}
