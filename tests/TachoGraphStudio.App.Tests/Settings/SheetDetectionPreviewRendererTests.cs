using System.Runtime.CompilerServices;

using OpenCvSharp;

using TachoGraphStudio.App.Settings;
using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Tests.Settings;

// 合成シートを実ファイルとして投入し、検出プレビュー(#126)が本処理と同じ検出結果になることを検証する。
// Dpi=50 は SheetSplitterTests / StagePipelineTests と同じ条件
public sealed class SheetDetectionPreviewRendererTests : IDisposable
{
    private const int PreviewLongSide = 320;

    private static readonly DiscSplitOptions TestSplitOptions = new() { Dpi = 50.0 };

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    public SheetDetectionPreviewRendererTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RenderAsync_DetectsSameDiscCountAsPipeline(int discCount)
    {
        string path = WriteJpegSheet("sheet.jpg", discCount);
        ImageProcessingSettings settings = new() { Threshold = 7 };

        DetectionPreviewImage preview = await CreateRenderer(path).RenderAsync(settings, CancellationToken.None);
        List<ProcessedDisc> discs = await ProcessAsync(path, settings);

        Assert.Equal(discs.Count, preview.DiscCount);
        Assert.Equal(discCount, preview.DiscCount);
    }

    [Fact]
    public async Task RenderAsync_ScalesSheetToPreviewSize()
    {
        string path = WriteJpegSheet("scaled.jpg", discCount: 1);

        DetectionPreviewImage preview = await CreateRenderer(path).RenderAsync(
            ImageProcessingSettings.Default,
            CancellationToken.None);

        Assert.Equal(PreviewLongSide, Math.Max(preview.Width, preview.Height));
        Assert.Equal(preview.Width * preview.Height * 4, preview.PremultipliedBgra.Length);
    }

    // アルファ円マージンは検出枚数を変えないが、描かれる円の大きさを変える
    [Fact]
    public async Task RenderAsync_EllipsePaddingChangesRenderedOverlay()
    {
        string path = WriteJpegSheet("ellipse-padding.jpg", discCount: 1);
        SheetDetectionPreviewRenderer renderer = CreateRenderer(path);

        DetectionPreviewImage narrow = await renderer.RenderAsync(
            new ImageProcessingSettings { EllipsePaddingPx = 0 },
            CancellationToken.None);
        DetectionPreviewImage wide = await renderer.RenderAsync(
            new ImageProcessingSettings { EllipsePaddingPx = 40 },
            CancellationToken.None);

        Assert.Equal(narrow.DiscCount, wide.DiscCount);
        Assert.NotEqual(narrow.PremultipliedBgra, wide.PremultipliedBgra);
    }

    [Fact]
    public async Task RenderAsync_ThresholdExcludingEveryDiscReportsSplitFailure()
    {
        string path = WriteJpegSheet("threshold.jpg", discCount: 1);

        await Assert.ThrowsAsync<DiscSplitException>(
            async () => await CreateRenderer(path).RenderAsync(
                new ImageProcessingSettings { Threshold = 255 },
                CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_InvalidEllipsePaddingReportsBackgroundRemovalFailure()
    {
        string path = WriteJpegSheet("invalid-ellipse-padding.jpg", discCount: 1);

        BackgroundRemovalException exception = await Assert.ThrowsAsync<BackgroundRemovalException>(
            async () => await CreateRenderer(path).RenderAsync(
                new ImageProcessingSettings { EllipsePaddingPx = -1_000 },
                CancellationToken.None));

        Assert.Contains("背景除去の設定", exception.Message, StringComparison.Ordinal);
    }

    // 設定変更のたびに PDF を再ラスタライズしない(読み込みは設定に依存しない)
    [Fact]
    public async Task RenderAsync_LoadsSheetOnlyOnce()
    {
        using Mat sheet = BuildSheet(discCount: 1);
        Cv2.ImEncode(".png", sheet, out byte[] pageBytes);
        string path = Path.Combine(_temporaryDirectory, "cached.pdf");
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]);
        CountingPdfRasterizer rasterizer = new(pageBytes);
        SheetDetectionPreviewRenderer renderer = new(
            new SheetLoader(rasterizer),
            new ImageProcessingOptionsResolver(pdfSplitOptions: TestSplitOptions),
            path,
            PreviewLongSide);

        await renderer.RenderAsync(new ImageProcessingSettings { Threshold = 7 }, CancellationToken.None);
        await renderer.RenderAsync(new ImageProcessingSettings { Threshold = 8 }, CancellationToken.None);

        Assert.Equal(1, rasterizer.Calls);
    }

    private static SheetDetectionPreviewRenderer CreateRenderer(string sourcePath)
        => new(
            new SheetLoader(new NotUsedPdfRasterizer()),
            new ImageProcessingOptionsResolver(imageSplitOptions: TestSplitOptions),
            sourcePath,
            PreviewLongSide);

    private static async Task<List<ProcessedDisc>> ProcessAsync(string path, ImageProcessingSettings settings)
    {
        StagePipeline pipeline = new(
            new SheetLoader(new NotUsedPdfRasterizer()),
            imageSplitOptions: TestSplitOptions);

        List<ProcessedDisc> discs = [];
        await foreach (ProcessedDisc disc in pipeline.ProcessAsync([path], settings))
        {
            discs.Add(disc);
        }

        return discs;
    }

    // JPEG の量子化ノイズでしきい値を割らないよう、円盤はグレー 225(nonwhite=30)で描く
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

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class NotUsedPdfRasterizer : IPdfRasterizer
    {
        public IAsyncEnumerable<byte[]> RasterizePagesAsync(
            string pdfPath,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("JPEG のみのテストで PDF ラスタライザが呼ばれました。");
    }

    private sealed class CountingPdfRasterizer(byte[] pageBytes) : IPdfRasterizer
    {
        public int Calls { get; private set; }

        public async IAsyncEnumerable<byte[]> RasterizePagesAsync(
            string pdfPath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return pageBytes;
        }
    }
}
