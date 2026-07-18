using OpenCvSharp;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

// 合成円盤(白地 + グレー円)で楕円フィット・アルファ化の挙動を固定する。
// 描画のアンチエイリアスとフィット誤差を考慮し座標系の検証は ±4px の許容とする
public sealed class BackgroundRemoverTests
{
    private static readonly Scalar DiscGray = new(240, 240, 240);

    [Fact]
    public void Remove_FitsEllipseToDisc()
    {
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Assert.InRange(result.Ellipse.Center.X, 146, 154);
        Assert.InRange(result.Ellipse.Center.Y, 146, 154);
        Assert.InRange(result.Ellipse.Size.Width, 236, 244);
        Assert.InRange(result.Ellipse.Size.Height, 236, 244);
    }

    [Fact]
    public void Remove_OutputIsBgraCroppedToEllipseBounds()
    {
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Assert.Equal(MatType.CV_8UC4, result.Pixels.Type());
        Assert.Equal(result.RegionInDisc.Width, result.Pixels.Width);
        Assert.Equal(result.RegionInDisc.Height, result.Pixels.Height);
        Assert.InRange(result.RegionInDisc.Left, 26, 34);
        Assert.InRange(result.RegionInDisc.Top, 26, 34);
        Assert.InRange(result.RegionInDisc.Right, 266, 274);
        Assert.InRange(result.RegionInDisc.Bottom, 266, 274);
    }

    [Fact]
    public void Remove_MakesInsideOpaqueAndOutsideTransparent()
    {
        // 円盤の外(左上)にゴミがあっても楕円の外はすべて透明化される
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120, garbage: (10, 10, 40));

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Vec4b center = result.Pixels.At<Vec4b>(
            (int)result.Ellipse.Center.Y - result.RegionInDisc.Y,
            (int)result.Ellipse.Center.X - result.RegionInDisc.X);
        Assert.Equal(255, center.Item3);
        Assert.Equal(240, center.Item0);

        // 入力座標 (35,35) はゴミ矩形の内側だが楕円の外側
        Vec4b garbagePixel = result.Pixels.At<Vec4b>(
            35 - result.RegionInDisc.Y,
            35 - result.RegionInDisc.X);
        Assert.Equal(0, garbagePixel.Item3);
    }

    [Theory]
    [InlineData(15, 270.0)]
    [InlineData(-15, 210.0)]
    public void Remove_EllipsePaddingAdjustsEllipseSize(int padding, double expectedDiameter)
    {
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(
            disc,
            new BackgroundRemovalOptions { EllipsePaddingPx = padding });

        Assert.InRange(result.Ellipse.Size.Width, expectedDiameter - 4, expectedDiameter + 4);
        Assert.InRange(result.Ellipse.Size.Height, expectedDiameter - 4, expectedDiameter + 4);
    }

    [Fact]
    public void Remove_ClampsRegionToImageBounds()
    {
        // 円盤が画像上端に近く、パディング込みの bbox は画像外へはみ出せない
        using DiscImage disc = BuildDisc(300, 300, 150, 100, 95);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(
            disc,
            new BackgroundRemovalOptions { EllipsePaddingPx = 10 });

        Assert.Equal(0, result.RegionInDisc.Top);
        Assert.True(result.RegionInDisc.Bottom <= 300);
    }

    [Fact]
    public void Remove_BlankDiscThrowsWithContext()
    {
        using Mat blank = new(200, 200, MatType.CV_8UC3, Scalar.All(255));
        using DiscImage disc = new(blank.Clone(), 2, new Rect(0, 0, 200, 200), "sheet.png", 1);

        BackgroundRemovalException exception = Assert.Throws<BackgroundRemovalException>(
            () => new BackgroundRemover().Remove(disc));

        Assert.Contains("sheet.png", exception.Message);
        Assert.Contains("2 ページ目", exception.Message);
        Assert.Contains("No.3", exception.Message);
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(5, true)]
    public void Remove_ThresholdControlsDetectionSensitivity(int threshold, bool detected)
    {
        // RGB 250 の円盤は nonwhite=5。既定の threshold=15 では前景にならない
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120, new Scalar(250, 250, 250));
        BackgroundRemover remover = new();
        BackgroundRemovalOptions options = new() { Threshold = threshold };

        if (detected)
        {
            using BackgroundRemovalResult result = remover.Remove(disc, options);
            Assert.InRange(result.Ellipse.Size.Width, 236, 244);
        }
        else
        {
            Assert.Throws<BackgroundRemovalException>(() => remover.Remove(disc, options));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Remove_InvalidThresholdThrows(int threshold)
    {
        using DiscImage disc = BuildDisc(200, 200, 100, 100, 80);

        Assert.Throws<ArgumentException>(
            () => new BackgroundRemover().Remove(disc, new BackgroundRemovalOptions { Threshold = threshold }));
    }

    [Fact]
    public void Remove_TooLargeNegativePaddingThrows()
    {
        using DiscImage disc = BuildDisc(200, 200, 100, 100, 50);

        Assert.Throws<ArgumentException>(
            () => new BackgroundRemover().Remove(disc, new BackgroundRemovalOptions { EllipsePaddingPx = -60 }));
    }

    private static DiscImage BuildDisc(
        int width,
        int height,
        int centerX,
        int centerY,
        int radius,
        Scalar? discColor = null,
        (int X, int Y, int Size)? garbage = null)
    {
        Mat pixels = new(height, width, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(pixels, new Point(centerX, centerY), radius, discColor ?? DiscGray, thickness: -1, LineTypes.AntiAlias);
        if (garbage is (int garbageX, int garbageY, int garbageSize))
        {
            Cv2.Rectangle(pixels, new Rect(garbageX, garbageY, garbageSize, garbageSize), DiscGray, thickness: -1);
        }

        return new DiscImage(pixels, 0, new Rect(0, 0, width, height), "synthetic.png", 0);
    }
}
