using System.Text;

using TachoGraphStudio.App.Imaging;

namespace TachoGraphStudio.App.Tests.Imaging;

public sealed class WindowsPdfRasterizerTests : IDisposable
{
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
        WindowsPdfRasterizer rasterizer = new(dpi: 96);

        List<byte[]> pages = [];
        await foreach (byte[] page in rasterizer.RasterizePagesAsync(path))
        {
            pages.Add(page);
        }

        Assert.Equal(pageCount, pages.Count);
        Assert.All(pages, page => Assert.True(page.Length > 0));
    }

    [Fact]
    public async Task RasterizePagesAsync_CancellationDuringEnumerationStopsBeforeNextPage()
    {
        string path = WritePdf(pageCount: 2);
        WindowsPdfRasterizer rasterizer = new(dpi: 96);
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
        WindowsPdfRasterizer rasterizer = new(dpi: 96);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (byte[] _ in rasterizer.RasterizePagesAsync(path, cancellation.Token))
            {
            }
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
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
