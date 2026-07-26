using OpenCvSharp;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

// 回帰テストは合成シート(白地 + 薄いグレー円盤)で行う。実スキャンは個人情報を含むため
// リポジトリに置かない(NFR-06)。
// Dpi=50 では採用範囲は直径 217〜270px(123mm×0.9 〜 125mm×1.1)になるため、
// 規格径に相当する半径 121px(≒123mm)を標準の円盤として使う
public sealed class SheetSplitterTests
{
    private const double TestDpi = 50.0;

    // 123mm 相当。Dpi=50 では直径 243px
    private const int StandardRadius = 121;

    // 125mm 相当。Dpi=50 では直径 247px
    private const int LargeStandardRadius = 123;

    private static readonly Scalar DiscGray = new(240, 240, 240);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Split_DetectsEachDisc(int discCount)
    {
        (int X, int Y)[] centers = [.. Enumerable.Range(0, discCount)
            .Select(index => (150 + (index % 3) * 300, 150 + (index / 3) * 300))];
        SheetImage sheet = BuildSheet(1000, 700, [.. centers.Select(center => (center.X, center.Y, StandardRadius))]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Equal(discCount, discs.Count);
            for (int index = 0; index < discCount; index++)
            {
                Assert.Equal(index, discs[index].Index);
                Assert.Contains(discs, disc => disc.RegionInSheet.Contains(new Point(centers[index].X, centers[index].Y)));
                Assert.Equal(discs[index].RegionInSheet.Width, discs[index].Pixels.Width);
                Assert.Equal(discs[index].RegionInSheet.Height, discs[index].Pixels.Height);
                Assert.Equal(sheet.SourcePath, discs[index].SourcePath);
            }
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 白地に線画で印字されたチャート紙(未記入の Task-Meter 等)の回帰。
    // 塗り潰しの円盤は充填率 0.77 前後だが、線画は 0.02〜0.15 まで下がる。
    // 旧実装は MinFillRatio=0.4 でこれを誤検出として捨てていた(#91)
    [Fact]
    public void Split_DetectsOutlineOnlyDisc()
    {
        using Mat raw = new(600, 600, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(300, 300), StandardRadius, DiscGray, thickness: 3);
        Cv2.Circle(raw, new Point(300, 300), StandardRadius / 2, DiscGray, thickness: 3);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Contains(new Point(300, 300)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 実運用では Task-Meter(125mm)と Yazaki(123mm)が混在する(#91)
    [Fact]
    public void Split_DetectsBothPaperStandards()
    {
        SheetImage sheet = BuildSheet(1000, 600, [(250, 300, StandardRadius), (700, 300, LargeStandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Equal(2, discs.Count);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_OrdersDiscsTopToBottomThenLeftToRight()
    {
        (int X, int Y)[] centers = [(650, 150), (150, 150), (400, 480), (150, 480)];
        SheetImage sheet = BuildSheet(900, 700, [.. centers.Select(center => (center.X, center.Y, StandardRadius))]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            (int X, int Y)[] expectedOrder = [(150, 150), (650, 150), (150, 480), (400, 480)];
            for (int index = 0; index < expectedOrder.Length; index++)
            {
                Assert.True(
                    discs[index].RegionInSheet.Contains(new Point(expectedOrder[index].X, expectedOrder[index].Y)),
                    $"Index {index} の円盤が期待位置 {expectedOrder[index]} を含んでいません: {discs[index].RegionInSheet}");
            }
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_AppliesPaddingAroundDetectedRegion()
    {
        SheetImage sheet = BuildSheet(600, 600, [(300, 300, StandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(
            sheet,
            new DiscSplitOptions { Dpi = TestDpi, PaddingPx = 20 })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            // 円盤の bbox は (179,179)-(421,421)。±2px はラスタライズ誤差
            Assert.InRange(disc.RegionInSheet.Left, 157, 161);
            Assert.InRange(disc.RegionInSheet.Top, 157, 161);
            Assert.InRange(disc.RegionInSheet.Right, 439, 443);
            Assert.InRange(disc.RegionInSheet.Bottom, 439, 443);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_ClampsRegionToSheetBounds()
    {
        // 円盤がシート左上隅にはみ出しており、パディングは画像外へ伸ばせない
        SheetImage sheet = BuildSheet(600, 600, [(126, 126, StandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(
            sheet,
            new DiscSplitOptions { Dpi = TestDpi, PaddingPx = 30 })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.Equal(0, disc.RegionInSheet.Left);
            Assert.Equal(0, disc.RegionInSheet.Top);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_FiltersOutSmallNoise()
    {
        SheetImage sheet = BuildSheet(900, 600, [(300, 300, StandardRadius), (700, 300, 20)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Contains(new Point(300, 300)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 規格径より大きい塊は円盤ではない。旧実装は下限しか持たず素通ししていた
    [Fact]
    public void Split_ExcludesOversizedRegion()
    {
        SheetImage sheet = BuildSheet(900, 600, [(300, 300, StandardRadius), (650, 300, 200)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Contains(new Point(300, 300)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 円盤は正円なので、規格径に収まっていても細長い塊は除外する
    [Fact]
    public void Split_ExcludesNonCircularRegion()
    {
        using Mat raw = new(600, 900, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(250, 300), StandardRadius, DiscGray, thickness: -1);
        Cv2.Rectangle(raw, new Rect(600, 180, 240, 30), DiscGray, thickness: -1);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Contains(new Point(250, 300)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_KeepsLargestDiscsWhenExceedingMaxDiscs()
    {
        // 規格内の円盤 7 枚(1 枚だけ小さめ)。MaxDiscs=6 で面積最小の 1 枚が除外される
        List<(int X, int Y, int Radius)> circles = [.. Enumerable.Range(0, 6)
            .Select(index => (150 + (index % 3) * 300, 150 + (index / 3) * 300, LargeStandardRadius))];
        (int X, int Y, int Radius) smallest = (150, 780, 112);
        circles.Add(smallest);
        SheetImage sheet = BuildSheet(1000, 950, [.. circles]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Equal(6, discs.Count);
            Assert.DoesNotContain(discs, disc => disc.RegionInSheet.Contains(new Point(smallest.X, smallest.Y)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_ExcludesScannerEdgeFrameByDiameterRange()
    {
        // スキャナ縁の黒帯がページを一周すると bbox はシート全体になる
        // (実スキャン PDF で確認した誤検出パターンの再現)
        using Mat raw = new(700, 700, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Rectangle(raw, new Rect(0, 0, 700, 700), Scalar.All(0), thickness: 8);
        Cv2.Circle(raw, new Point(350, 350), StandardRadius, DiscGray, thickness: -1);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Contains(new Point(350, 350)));
            Assert.True(disc.RegionInSheet.Width < 350, $"枠成分が除外されていません: {disc.RegionInSheet}");
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Fact]
    public void Split_DownscalesLargeSheetForAnalysis()
    {
        // 長辺 2400px > 解析上限 1200px の縮小経路。座標はフル解像度へ復元される。
        // 直径 600px が規格径に相当する DPI を指定する
        const double dpi = 123.5;
        SheetImage sheet = BuildSheet(2400, 1800, [(600, 600, 300), (1700, 1200, 300)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = dpi })];
        try
        {
            Assert.Equal(2, discs.Count);
            Assert.True(discs[0].RegionInSheet.Contains(new Point(600, 600)));
            Assert.True(discs[1].RegionInSheet.Contains(new Point(1700, 1200)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2000.0)]
    public void Split_FallsBackToFixedMinSizeWhenDpiUnusable(double? dpi)
    {
        // DPI 不明時は想定径を計算できないため、従来どおり最小サイズ 1000px のみで判定する
        SheetImage sheet = BuildSheet(600, 600, [(300, 300, StandardRadius)]);

        DiscSplitException exception = Assert.Throws<DiscSplitException>(
            () => new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = dpi }));

        Assert.Contains("1000px", exception.Message);
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(5, true)]
    public void Split_ThresholdControlsDetectionSensitivity(int threshold, bool detected)
    {
        // RGB 250 の円盤は nonwhite=5。threshold=15 では検出されない
        SheetImage sheet = BuildSheet(600, 600, [(300, 300, StandardRadius)], new Scalar(250, 250, 250));
        SheetSplitter splitter = new();
        DiscSplitOptions options = new() { Dpi = TestDpi, Threshold = threshold };

        if (detected)
        {
            List<DiscImage> discs = [.. splitter.Split(sheet, options)];
            try
            {
                Assert.Single(discs);
            }
            finally
            {
                discs.ForEach(disc => disc.Dispose());
            }
        }
        else
        {
            Assert.Throws<DiscSplitException>(() => splitter.Split(sheet, options));
        }
    }

    [Fact]
    public void Split_BlankSheetThrowsWithContext()
    {
        SheetImage sheet = BuildSheet(600, 600, []);

        DiscSplitException exception = Assert.Throws<DiscSplitException>(
            () => new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi }));

        Assert.Contains(sheet.SourcePath, exception.Message);
    }

    [Fact]
    public void Split_UndecodableBytesThrowWithContext()
    {
        SheetImage sheet = new("broken.png", PageIndex: 2, [0x00, 0x01, 0x02]);

        DiscSplitException exception = Assert.Throws<DiscSplitException>(
            () => new SheetSplitter().Split(sheet));

        Assert.Contains("broken.png", exception.Message);
        Assert.Contains("3 ページ目", exception.Message);
    }

    [Theory]
    [InlineData(0, "Threshold")]
    [InlineData(256, "Threshold")]
    public void Split_InvalidThresholdThrows(int threshold, string expectedInMessage)
    {
        SheetImage sheet = BuildSheet(300, 300, []);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new SheetSplitter().Split(sheet, new DiscSplitOptions { Threshold = threshold }));

        Assert.Contains(expectedInMessage, exception.Message);
    }

    private static SheetImage BuildSheet(
        int width,
        int height,
        (int X, int Y, int Radius)[] circles,
        Scalar? discColor = null)
    {
        using Mat sheet = new(height, width, MatType.CV_8UC3, Scalar.All(255));
        foreach ((int x, int y, int radius) in circles)
        {
            Cv2.Circle(sheet, new Point(x, y), radius, discColor ?? DiscGray, thickness: -1);
        }

        return Encode(sheet);
    }

    private static SheetImage Encode(Mat sheet)
    {
        Cv2.ImEncode(".png", sheet, out byte[] encoded);
        return new SheetImage("synthetic.png", PageIndex: 0, encoded);
    }
}
