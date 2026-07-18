using System.Runtime.CompilerServices;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

public sealed class SheetLoaderTests : IDisposable
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    public SheetLoaderTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData("sheet.jpg")]
    [InlineData("sheet.jpeg")]
    [InlineData("SHEET.JPG")]
    [InlineData("SHEET.JPEG")]
    public async Task LoadAsync_JpegFileYieldsSingleSheet(string fileName)
    {
        byte[] jpegBytes = [.. JpegHeader, 0x01, 0x02];
        string path = WriteFile(fileName, jpegBytes);
        SheetLoader loader = new(new FakePdfRasterizer());

        List<SheetImage> sheets = await ToListAsync(loader.LoadAsync([path]));

        SheetImage sheet = Assert.Single(sheets);
        Assert.Equal(path, sheet.SourcePath);
        Assert.Equal(0, sheet.PageIndex);
        Assert.Equal(jpegBytes, sheet.ImageBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LoadAsync_PdfFileYieldsSheetPerPage(int pageCount)
    {
        byte[][] pages = Enumerable.Range(0, pageCount)
            .Select(index => new byte[] { 0x89, 0x50, (byte)index })
            .ToArray();
        string path = WriteFile("sheets.pdf", [0x25, 0x50, 0x44, 0x46]);
        SheetLoader loader = new(new FakePdfRasterizer { [path] = pages });

        List<SheetImage> sheets = await ToListAsync(loader.LoadAsync([path]));

        Assert.Equal(pageCount, sheets.Count);
        for (int index = 0; index < pageCount; index++)
        {
            Assert.Equal(path, sheets[index].SourcePath);
            Assert.Equal(index, sheets[index].PageIndex);
            Assert.Equal(pages[index], sheets[index].ImageBytes);
        }
    }

    [Fact]
    public async Task LoadAsync_BatchPreservesInputOrder()
    {
        string firstJpeg = WriteFile("first.jpg", [.. JpegHeader, 0x01]);
        string pdf = WriteFile("second.pdf", [0x25, 0x50, 0x44, 0x46]);
        string lastJpeg = WriteFile("third.jpg", [.. JpegHeader, 0x03]);
        byte[][] pdfPages = [[0x10], [0x11]];
        SheetLoader loader = new(new FakePdfRasterizer { [pdf] = pdfPages });

        List<SheetImage> sheets = await ToListAsync(loader.LoadAsync([firstJpeg, pdf, lastJpeg]));

        Assert.Equal(4, sheets.Count);
        Assert.Equal((firstJpeg, 0), (sheets[0].SourcePath, sheets[0].PageIndex));
        Assert.Equal((pdf, 0), (sheets[1].SourcePath, sheets[1].PageIndex));
        Assert.Equal((pdf, 1), (sheets[2].SourcePath, sheets[2].PageIndex));
        Assert.Equal((lastJpeg, 0), (sheets[3].SourcePath, sheets[3].PageIndex));
    }

    [Theory]
    [InlineData("sheet.png")]
    [InlineData("sheet.tiff")]
    [InlineData("sheet")]
    public async Task LoadAsync_UnsupportedExtensionThrows(string fileName)
    {
        string path = WriteFile(fileName, [0x00]);
        SheetLoader loader = new(new FakePdfRasterizer());

        SheetLoadException exception = await Assert.ThrowsAsync<SheetLoadException>(
            () => ToListAsync(loader.LoadAsync([path])));

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public async Task LoadAsync_SignatureOnlyContentIsAccepted()
    {
        // この層の契約はシグネチャ確認のみ。切り詰め等の破損検出はデコード段（issue #8）が担う
        byte[] signatureOnly = [0xFF, 0xD8];
        string path = WriteFile("truncated.jpg", signatureOnly);
        SheetLoader loader = new(new FakePdfRasterizer());

        List<SheetImage> sheets = await ToListAsync(loader.LoadAsync([path]));

        SheetImage sheet = Assert.Single(sheets);
        Assert.Equal(signatureOnly, sheet.ImageBytes);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public async Task LoadAsync_InvalidJpegContentThrows(byte[] content)
    {
        string path = WriteFile("broken.jpg", content);
        SheetLoader loader = new(new FakePdfRasterizer());

        SheetLoadException exception = await Assert.ThrowsAsync<SheetLoadException>(
            () => ToListAsync(loader.LoadAsync([path])));

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public async Task LoadAsync_MissingJpegFileThrowsWithPath()
    {
        string path = Path.Combine(_temporaryDirectory, "missing.jpg");
        SheetLoader loader = new(new FakePdfRasterizer());

        SheetLoadException exception = await Assert.ThrowsAsync<SheetLoadException>(
            () => ToListAsync(loader.LoadAsync([path])));

        Assert.Contains(path, exception.Message);
        Assert.IsAssignableFrom<IOException>(exception.InnerException);
    }

    [Fact]
    public async Task LoadAsync_RasterizerFailureIsWrappedWithPageContext()
    {
        string path = WriteFile("broken.pdf", [0x25, 0x50, 0x44, 0x46]);
        InvalidOperationException cause = new("render failed");
        SheetLoader loader = new(new FakePdfRasterizer
        {
            [path] = [[0x01]],
            FailAfterFirstPage = cause,
        });

        SheetLoadException exception = await Assert.ThrowsAsync<SheetLoadException>(
            () => ToListAsync(loader.LoadAsync([path])));

        Assert.Contains(path, exception.Message);
        Assert.Contains("2 ページ目", exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public async Task LoadAsync_CancellationStopsEnumeration()
    {
        string path = WriteFile("sheet.jpg", [.. JpegHeader]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        SheetLoader loader = new(new FakePdfRasterizer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ToListAsync(loader.LoadAsync([path], cancellation.Token)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string WriteFile(string fileName, byte[] content)
    {
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static async Task<List<SheetImage>> ToListAsync(IAsyncEnumerable<SheetImage> sheets)
    {
        List<SheetImage> result = [];
        await foreach (SheetImage sheet in sheets)
        {
            result.Add(sheet);
        }

        return result;
    }

    private sealed class FakePdfRasterizer : IPdfRasterizer
    {
        private readonly Dictionary<string, byte[][]> _pagesByPath = [];

        public Exception? FailAfterFirstPage { get; init; }

        public byte[][] this[string path]
        {
            set => _pagesByPath[path] = value;
        }

        public async IAsyncEnumerable<byte[]> RasterizePagesAsync(
            string pdfPath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            byte[][] pages = _pagesByPath[pdfPath];
            for (int index = 0; index < pages.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return pages[index];

                if (index == 0 && FailAfterFirstPage is not null)
                {
                    throw FailAfterFirstPage;
                }
            }
        }
    }
}
