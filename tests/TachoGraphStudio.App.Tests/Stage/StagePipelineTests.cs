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
            Assert.Equal(disc.Width * disc.Height * 4, disc.Bgra.Length);
            Assert.Equal(disc.ThumbnailWidth * disc.ThumbnailHeight * 4, disc.ThumbnailPremultipliedBgra.Length);
            Assert.InRange(Math.Max(disc.ThumbnailWidth, disc.ThumbnailHeight), 1, 160);
            Assert.InRange(disc.EllipseCenterX, 0, disc.Width);
            Assert.InRange(disc.EllipseCenterY, 0, disc.Height);
        }
    }

    [Fact]
    public async Task ProcessAsync_OutputIsStraightBgraWithPremultipliedThumbnail()
    {
        string path = WriteJpegSheet("sheet.jpg", discCount: 1);
        StagePipeline pipeline = new(
            new SheetLoader(new NotUsedPdfRasterizer()),
            imageSplitOptions: TestSplitOptions);

        ProcessedDisc disc = Assert.Single(await ToListAsync(pipeline.ProcessAsync([path])));

        // 楕円中心は不透明(alpha=255)で BGR は元色(225 前後)のまま
        int centerOffset = (((int)disc.EllipseCenterY * disc.Width) + (int)disc.EllipseCenterX) * 4;
        Assert.Equal(255, disc.Bgra[centerOffset + 3]);
        Assert.InRange(disc.Bgra[centerOffset], 200, 255);

        // 左上隅は楕円の外(alpha=0)。フル解像度はストレートアルファで本合成(FR-19)の入力になる
        Assert.Equal(0, disc.Bgra[3]);

        // サムネイルは premultiplied(表示専用)。中心は不透明なので BGR が残る
        int thumbnailCenterOffset =
            (((disc.ThumbnailHeight / 2) * disc.ThumbnailWidth) + (disc.ThumbnailWidth / 2)) * 4;
        Assert.Equal(255, disc.ThumbnailPremultipliedBgra[thumbnailCenterOffset + 3]);
        Assert.Equal(0, disc.ThumbnailPremultipliedBgra[3]);
        Assert.Equal(0, disc.ThumbnailPremultipliedBgra[0]);
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

    // #29 で blocking になったストリーミング契約の回帰。同一シートの後続円盤の処理が
    // 失敗しても、先行して変換できた円盤は呼び出し元へ届いていなければならない。
    // 背景除去が前景判定をやめた(#91)ことで自然に失敗する入力を作れなくなったため、
    // 2 件目だけ失敗する fake を注入して契約を固定する
    [Fact]
    public async Task ProcessAsync_YieldsEarlierDiscsBeforeLaterFailureInSameSheet()
    {
        using Mat sheet = BuildSheet(discCount: 2);
        Cv2.ImEncode(".png", sheet, out byte[] pageBytes);
        string path = Path.Combine(_temporaryDirectory, "partial.pdf");
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]);
        StagePipeline pipeline = new(
            new SheetLoader(new FakePdfRasterizer(pageBytes, pageCount: 1)),
            pdfSplitOptions: TestSplitOptions,
            remover: new FailAfterFirstDiscRemover());

        await using IAsyncEnumerator<ProcessedDisc> discs = pipeline.ProcessAsync([path]).GetAsyncEnumerator();

        Assert.True(await discs.MoveNextAsync());
        Assert.Equal(0, discs.Current.IndexInSheet);
        await Assert.ThrowsAsync<BackgroundRemovalException>(async () => await discs.MoveNextAsync());
    }

    // 1 枚目だけ通し、2 枚目以降は失敗する
    private sealed class FailAfterFirstDiscRemover : IBackgroundRemover
    {
        private readonly BackgroundRemover _inner = new();
        private int _calls;

        public BackgroundRemovalResult Remove(DiscImage disc, BackgroundRemovalOptions? options = null)
        {
            if (_calls++ > 0)
            {
                throw new BackgroundRemovalException("テスト用の失敗");
            }

            return _inner.Remove(disc, options);
        }
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
