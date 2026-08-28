using OpenCvSharp;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

// 背景除去は分割時に検出した中心・直径から円マスクを描くだけで、自前の検出は行わない。
// 描画のアンチエイリアスを考慮し座標系の検証は ±4px の許容とする
public sealed class BackgroundRemoverTests
{
    private static readonly Scalar DiscGray = new(240, 240, 240);

    [Fact]
    public void Remove_UsesDetectedDiscGeometry()
    {
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Assert.Equal(150f, result.Ellipse.Center.X);
        Assert.Equal(150f, result.Ellipse.Center.Y);
        Assert.Equal(240f, result.Ellipse.Size.Width);
        Assert.Equal(240f, result.Ellipse.Size.Height);
    }

    // 白地に線画で印字されたチャート紙の回帰。前景がリング状にしか出ないため
    // 楕円フィットでは不安定だったが、分割時の検出結果を使うので影響を受けない(#91)
    [Fact]
    public void Remove_HandlesOutlineOnlyDisc()
    {
        Mat pixels = new(300, 300, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(pixels, new Point(150, 150), 120, DiscGray, thickness: 3);
        Cv2.Circle(pixels, new Point(150, 150), 60, DiscGray, thickness: 3);
        using DiscImage disc = new(
            pixels, 0, new Rect(0, 0, 300, 300), "synthetic.png", 0, new Point2f(150, 150), 240f);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Assert.Equal(150f, result.Ellipse.Center.X);
        Assert.Equal(240f, result.Ellipse.Size.Width);

        // 中心は不透明、円の外は透明
        Vec4b center = result.Pixels.At<Vec4b>(
            (int)result.Ellipse.Center.Y - result.RegionInDisc.Y,
            (int)result.Ellipse.Center.X - result.RegionInDisc.X);
        Assert.Equal(255, center.Item3);
    }

    // DPI 不明(JPEG 入力)経路の回帰。非対称な突起で bbox 中心は真の中心からずれるため、
    // bbox 中心とフィット直径を組み合わせるとマスクごと移動して反対側の外周を切り落とす。
    // 分割から背景除去まで通し、円盤の両端が不透明に残ることを固定する(#91)
    [Fact]
    public void Remove_KeepsBothEdgesOpaqueForAsymmetricContourWithoutDpi()
    {
        const int trueCenterX = 700;
        const int trueCenterY = 700;
        const int radius = 550;

        using Mat sheet = new(1400, 1400, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(sheet, new Point(trueCenterX, trueCenterY), radius, DiscGray, thickness: -1);
        // 細長い突起にすることで、bbox 中心を大きくずらしつつ楕円フィットへの影響は小さく保つ。
        // 太い突起だとフィット直径も膨らみ、中心のずれを相殺してしまう
        Cv2.Rectangle(sheet, new Rect(1250, 697, 105, 6), DiscGray, thickness: -1);
        Cv2.ImEncode(".png", sheet, out byte[] encoded);
        SheetImage sheetImage = new("synthetic.png", PageIndex: 0, encoded);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheetImage, new DiscSplitOptions { Dpi = null })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

            // 円盤の左端・右端(輪郭のアンチエイリアスを避けて 15px 内側)が不透明であること。
            // マスクがずれると該当点はクロップ範囲の外へ出るため、範囲判定も明示する
            // (Mat.At は範囲外を検査せず読めてしまうため)
            foreach (int sheetX in new[] { trueCenterX - radius + 15, trueCenterX + radius - 15 })
            {
                int x = sheetX - disc.RegionInSheet.X - result.RegionInDisc.X;
                int y = trueCenterY - disc.RegionInSheet.Y - result.RegionInDisc.Y;
                Assert.True(
                    x >= 0 && x < result.Pixels.Width && y >= 0 && y < result.Pixels.Height,
                    $"シート座標 X={sheetX} がクロップ範囲外です: ({x},{y}) / {result.Pixels.Width}x{result.Pixels.Height}");

                Vec4b pixel = result.Pixels.At<Vec4b>(y, x);
                Assert.True(pixel.Item3 > 0, $"シート座標 X={sheetX} が透明化されています");
            }
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Remove_OutputIsBgraCroppedToCircleBounds()
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
        // 円盤の外(左上)にゴミがあっても円の外はすべて透明化される
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120, garbage: (10, 10, 40));

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(disc);

        Vec4b center = result.Pixels.At<Vec4b>(
            (int)result.Ellipse.Center.Y - result.RegionInDisc.Y,
            (int)result.Ellipse.Center.X - result.RegionInDisc.X);
        Assert.Equal(255, center.Item3);
        Assert.Equal(240, center.Item0);

        // 入力座標 (35,35) はゴミ矩形の内側だが円の外側
        Vec4b garbagePixel = result.Pixels.At<Vec4b>(
            35 - result.RegionInDisc.Y,
            35 - result.RegionInDisc.X);
        Assert.Equal(0, garbagePixel.Item3);
    }

    [Theory]
    [InlineData(15, 270.0)]
    [InlineData(-15, 210.0)]
    public void Remove_EllipsePaddingAdjustsCircleSize(int padding, double expectedDiameter)
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
    public void Remove_MaximumPositivePaddingSafelyCoversWholeImage()
    {
        using DiscImage disc = BuildDisc(300, 300, 150, 150, 120);

        using BackgroundRemovalResult result = new BackgroundRemover().Remove(
            disc,
            new BackgroundRemovalOptions { EllipsePaddingPx = int.MaxValue });

        Assert.Equal(new Rect(0, 0, 300, 300), result.RegionInDisc);
        Assert.Equal(255, result.Pixels.At<Vec4b>(0, 0).Item3);
    }

    [Fact]
    public void Remove_CircleOutsideImageThrowsWithContext()
    {
        using Mat pixels = new(200, 200, MatType.CV_8UC3, Scalar.All(255));
        using DiscImage disc = new(
            pixels.Clone(), 2, new Rect(0, 0, 200, 200), "sheet.png", 1, new Point2f(-500, -500), 100f);

        BackgroundRemovalException exception = Assert.Throws<BackgroundRemovalException>(
            () => new BackgroundRemover().Remove(disc));

        Assert.Contains("sheet.png", exception.Message);
        Assert.Contains("2 ページ目", exception.Message);
        Assert.Contains("No.3", exception.Message);
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

        return new DiscImage(
            pixels,
            0,
            new Rect(0, 0, width, height),
            "synthetic.png",
            0,
            new Point2f(centerX, centerY),
            radius * 2f);
    }
}
