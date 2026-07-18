using OpenCvSharp;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.App.Tests.Stage;

// 合成シートの JPEG を実ファイルとして投入し、読込→分割→背景除去→表示用変換を通しで検証する。
// Dpi=50 で円盤の最小サイズは 162px(SheetSplitterTests と同じ条件)
public sealed class StagePipelineTests : IDisposable
{
    private static readonly DiscSplitOptions TestSplitOptions = new() { Dpi = 50.0 };

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    public StagePipelineTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_JpegSheetYieldsProcessedDiscPerDetectedDisc(int discCount)
    {
        string path = WriteJpegSheet("sheet.jpg", discCount);
        StagePipeline pipeline = new(
            new SheetLoader(new NotUsedPdfRasterizer()),
            imageSplitOptions: TestSplitOptions);

        List<ProcessedDisc> discs = await ToListAsync(pipeline.ProcessAsync([path]));

        Assert.Equal(discCount, discs.Count);
        for (int index = 0; index < discCount; index++)
        {
            ProcessedDisc disc = discs[index];
            Assert.Equal(path, disc.SourcePath);
            Assert.Equal(index, disc.IndexInSheet);
            Assert.Equal(disc.Width * disc.Height * 4, disc.PremultipliedBgra.Length);
            Assert.Equal(disc.ThumbnailWidth * disc.ThumbnailHeight * 4, disc.ThumbnailPremultipliedBgra.Length);
            Assert.InRange(Math.Max(disc.ThumbnailWidth, disc.ThumbnailHeight), 1, 160);
            Assert.InRange(disc.EllipseCenterX, 0, disc.Width);
            Assert.InRange(disc.EllipseCenterY, 0, disc.Height);
        }
    }

    [Fact]
    public async Task ProcessAsync_OutputIsPremultipliedBgra()
    {
        string path = WriteJpegSheet("sheet.jpg", discCount: 1);
        StagePipeline pipeline = new(
            new SheetLoader(new NotUsedPdfRasterizer()),
            imageSplitOptions: TestSplitOptions);

        ProcessedDisc disc = Assert.Single(await ToListAsync(pipeline.ProcessAsync([path])));

        // 楕円中心は不透明(alpha=255)で BGR は元色(225 前後)のまま
        int centerOffset = (((int)disc.EllipseCenterY * disc.Width) + (int)disc.EllipseCenterX) * 4;
        Assert.Equal(255, disc.PremultipliedBgra[centerOffset + 3]);
        Assert.InRange(disc.PremultipliedBgra[centerOffset], 200, 255);

        // 左上隅は楕円の外。premultiplied のため alpha=0 なら BGR も 0
        Assert.Equal(0, disc.PremultipliedBgra[3]);
        Assert.Equal(0, disc.PremultipliedBgra[0]);
        Assert.Equal(0, disc.PremultipliedBgra[1]);
        Assert.Equal(0, disc.PremultipliedBgra[2]);
    }

    [Fact]
    public async Task ProcessAsync_PdfSheetUsesRasterizerPages()
    {
        using Mat sheet = BuildSheet(discCount: 2);
        Cv2.ImEncode(".png", sheet, out byte[] pageBytes);
        string path = Path.Combine(_temporaryDirectory, "sheets.pdf");
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]);
        StagePipeline pipeline = new(
            new SheetLoader(new FakePdfRasterizer(pageBytes, pageCount: 2)),
            pdfSplitOptions: TestSplitOptions);

        List<ProcessedDisc> discs = await ToListAsync(pipeline.ProcessAsync([path]));

        Assert.Equal(4, discs.Count);
        Assert.Equal([0, 0, 1, 1], discs.Select(disc => disc.PageIndex));
        Assert.Equal([0, 1, 0, 1], discs.Select(disc => disc.IndexInSheet));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    // JPEG の量子化ノイズでしきい値(15)を割らないよう、円盤はグレー 225(nonwhite=30)で描く
    private static Mat BuildSheet(int discCount)
    {
        Mat sheet = new(700, 1000, MatType.CV_8UC3, Scalar.All(255));
        for (int index = 0; index < discCount; index++)
        {
            Cv2.Circle(sheet, new Point(250 + index * 450, 350), 120, Scalar.All(225), thickness: -1);
        }

        return sheet;
    }

    private string WriteJpegSheet(string fileName, int discCount)
    {
        using Mat sheet = BuildSheet(discCount);
        Cv2.ImEncode(".jpg", sheet, out byte[] encoded);
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllBytes(path, encoded);
        return path;
    }

    private static async Task<List<ProcessedDisc>> ToListAsync(IAsyncEnumerable<ProcessedDisc> discs)
    {
        List<ProcessedDisc> result = [];
        await foreach (ProcessedDisc disc in discs)
        {
            result.Add(disc);
        }

        return result;
    }

    private sealed class NotUsedPdfRasterizer : IPdfRasterizer
    {
        public IAsyncEnumerable<byte[]> RasterizePagesAsync(string pdfPath, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("JPEG のみのテストで PDF ラスタライザが呼ばれました。");
    }

    private sealed class FakePdfRasterizer(byte[] pageBytes, int pageCount) : IPdfRasterizer
    {
        public async IAsyncEnumerable<byte[]> RasterizePagesAsync(
            string pdfPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            for (int page = 0; page < pageCount; page++)
            {
                yield return pageBytes;
            }
        }
    }
}
