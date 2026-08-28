using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

using TachoGraphStudio.App.Imaging;

namespace TachoGraphStudio.App.Tests.Imaging;

public sealed class WindowsPdfRasterizerTests : IDisposable
{
    private const int CleanupAttempts = 5;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    public WindowsPdfRasterizerTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RasterizePagesAsync_YieldsEncodedImagePerPage(int pageCount)
    {
        string path = WritePdf(pageCount);
        WindowsPdfRasterizer rasterizer = new(() => 1.0, dpi: 96);

        List<byte[]> pages = [];
        await foreach (byte[] page in rasterizer.RasterizePagesAsync(path))
        {
            pages.Add(page);
        }

        Assert.Equal(pageCount, pages.Count);
        Assert.All(pages, page => Assert.True(page.Length > 0));
    }

    [Theory]
    [InlineData(96.0, 600.0, 1.0, 600u)]
    [InlineData(96.0, 600.0, 1.25, 480u)]
    [InlineData(96.0, 600.0, 1.5, 400u)]
    [InlineData(96.0, 600.0, 2.0, 300u)]
    public void CalculateDestinationLength_CompensatesSystemScale(
        double pageLengthDip,
        double dpi,
        double systemRasterizationScale,
        uint expected)
    {
        uint actual = WindowsPdfRasterizer.CalculateDestinationLength(
            pageLengthDip,
            dpi,
            systemRasterizationScale);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RasterizePagesAsync_CompensatesRendererScaleToRequestedPhysicalPixels()
    {
        string path = WritePdf(pageCount: 1);
        WindowsPdfRasterizer scaleProbe = new(() => 1.0, dpi: 96);
        byte[] probePage = await ReadFirstPageAsync(scaleProbe, path);
        (uint probeWidth, _) = ReadPngDimensions(probePage);
        double systemRendererScale = probeWidth / 96.0;
        WindowsPdfRasterizer rasterizer = new(() => systemRendererScale, dpi: 96);

        byte[] page = await ReadFirstPageAsync(rasterizer, path);

        Assert.Equal((96u, 96u), ReadPngDimensions(page));
    }

    [Fact]
    public async Task RasterizePagesAsync_CancellationDuringEnumerationStopsBeforeNextPage()
    {
        string path = WritePdf(pageCount: 2);
        WindowsPdfRasterizer rasterizer = new(() => 1.0, dpi: 96);
        using CancellationTokenSource cancellation = new();

        await using IAsyncEnumerator<byte[]> pages = rasterizer
            .RasterizePagesAsync(path, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await pages.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pages.MoveNextAsync());
    }

    [Fact]
    public async Task RasterizePagesAsync_PreCancelledTokenYieldsNothing()
    {
        string path = WritePdf(pageCount: 1);
        WindowsPdfRasterizer rasterizer = new(() => 1.0, dpi: 96);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (byte[] _ in rasterizer.RasterizePagesAsync(path, cancellation.Token))
            {
            }
        });
    }

    private static async Task<byte[]> ReadFirstPageAsync(
        WindowsPdfRasterizer rasterizer,
        string path)
    {
        await foreach (byte[] page in rasterizer.RasterizePagesAsync(path))
        {
            return page;
        }

        throw new InvalidOperationException("テスト PDF にページがありません。");
    }

    private static (uint Width, uint Height) ReadPngDimensions(byte[] png)
    {
        Assert.True(png.Length >= 24);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
        return (
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
    }

    public void Dispose()
    {
        for (int attempt = 0; attempt < CleanupAttempts; attempt++)
        {
            if (!Directory.Exists(_temporaryDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (attempt + 1 < CleanupAttempts)
                {
                    // Windows.Data.Pdf の RCW 解放と競合した場合だけ短く待って再試行する。
                    Thread.Sleep(CleanupRetryDelay);
                    continue;
                }

                Trace.WriteLine($"テスト用一時ディレクトリの削除を諦めました: {exception.Message}");
                return;
            }
        }
    }

    private string WritePdf(int pageCount)
    {
        string path = Path.Combine(_temporaryDirectory, $"fixture-{pageCount}p.pdf");
        File.WriteAllBytes(path, BuildMinimalPdf(pageCount));
        return path;
    }

    // 空ページのみの最小 PDF を組み立てる。ASCII のみで構成するため文字位置 = バイトオフセット
    private static byte[] BuildMinimalPdf(int pageCount)
    {
        string kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(index => $"{index + 3} 0 R"));
        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>",
            .. Enumerable.Repeat("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] >>", pageCount),
        ];

        StringBuilder pdf = new("%PDF-1.4\n");
        List<int> offsets = [];
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xrefOffset = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n");
        pdf.Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
