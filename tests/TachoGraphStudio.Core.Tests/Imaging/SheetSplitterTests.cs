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

    // 切り出しサイズ 127mm。Dpi=50 では 250px
    private const int FixedCropSize = 250;

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

    // 印字が薄いと外周リングが分断され、断片が最小サイズに届かず連結成分では 1 件も
    // 残らないことがある。Hough は勾配を見るため連結性を要求せず、この場合も検出できる(#91)
    [Fact]
    public void Split_FallsBackToHoughWhenContourIsBroken()
    {
        using Mat raw = new(600, 600, MatType.CV_8UC3, Scalar.All(255));
        DrawBrokenRing(raw, new Point(300, 300), StandardRadius);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.Equal(FixedCropSize, disc.RegionInSheet.Width);

            // 検出された中心が円弧の中心と一致する
            int cropCenterX = disc.RegionInSheet.X + (disc.RegionInSheet.Width / 2);
            int cropCenterY = disc.RegionInSheet.Y + (disc.RegionInSheet.Height / 2);
            Assert.InRange(cropCenterX, 295, 305);
            Assert.InRange(cropCenterY, 295, 305);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 通常の円盤と輪郭が分断された円盤が同一シートに混在するケース。連結成分だけでは
    // 分断された方が例外もなく欠落するため、Hough の結果を重複排除して統合する(#91)
    [Fact]
    public void Split_CompletesMissingDiscWhenOnlySomeContoursAreBroken()
    {
        using Mat raw = new(500, 900, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(200, 250), StandardRadius, DiscGray, thickness: -1);
        DrawBrokenRing(raw, new Point(650, 250), StandardRadius);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Equal(2, discs.Count);
            Assert.Contains(discs, disc => disc.RegionInSheet.Contains(new Point(200, 250)));
            Assert.Contains(discs, disc => disc.RegionInSheet.Contains(new Point(650, 250)));
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 通常の円盤が Hough でも検出されるため、重複排除が効かないと同じ円盤が 2 件になる
    [Fact]
    public void Split_DoesNotDuplicateDiscDetectedByBothPaths()
    {
        SheetImage sheet = BuildSheet(600, 600, [(300, 300, StandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Single(discs);
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

    // 検出サイズは前景判定のしきい値によるインクのにじみでぶれるが、規格径は固定なので
    // 切り出しは常に同じ寸法になる(#91)
    [Fact]
    public void Split_CropsToStandardSizeRegardlessOfDetectedSize()
    {
        SheetImage sheet = BuildSheet(1000, 600, [(250, 300, StandardRadius), (700, 300, LargeStandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            Assert.Equal(2, discs.Count);
            foreach (DiscImage disc in discs)
            {
                Assert.Equal(FixedCropSize, disc.RegionInSheet.Width);
                Assert.Equal(FixedCropSize, disc.RegionInSheet.Height);
                Assert.Equal(FixedCropSize, disc.Pixels.Width);
                Assert.Equal(FixedCropSize, disc.Pixels.Height);
            }
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 固定サイズ切り出しでは円盤が常に画像中心に来る。後段の背景除去はこれを前提にできる
    [Theory]
    [InlineData(300, 300)]
    [InlineData(220, 380)]
    public void Split_CentersDiscInFixedCrop(int centerX, int centerY)
    {
        SheetImage sheet = BuildSheet(700, 700, [(centerX, centerY, StandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            int cropCenterX = disc.RegionInSheet.X + (disc.RegionInSheet.Width / 2);
            int cropCenterY = disc.RegionInSheet.Y + (disc.RegionInSheet.Height / 2);

            // ±2px はラスタライズと解析スケール往復の誤差
            Assert.InRange(cropCenterX, centerX - 2, centerX + 2);
            Assert.InRange(cropCenterY, centerY - 2, centerY + 2);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // bbox 中心と楕円フィット中心がずれる非対称輪郭。楕円フィットは突起に引きずられにくいため、
    // bbox 中心より真の円中心に近い位置で切り出される
    [Fact]
    public void Split_PrefersEllipseCenterOverBoundingBoxCenterForAsymmetricContour()
    {
        const int trueCenter = 350;
        using Mat raw = new(700, 700, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(trueCenter, trueCenter), 110, DiscGray, thickness: -1);
        // 右側の突起で bbox だけを広げる
        Cv2.Rectangle(raw, new Rect(460, 340, 12, 20), DiscGray, thickness: -1);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            int cropCenterX = disc.RegionInSheet.X + (disc.RegionInSheet.Width / 2);

            // bbox は 240..472 なのでその中心は 356。楕円フィットが採用されていれば
            // 真の中心 350 により近くなる
            int boundingBoxCenterX = 356;
            Assert.True(
                Math.Abs(cropCenterX - trueCenter) < Math.Abs(boundingBoxCenterX - trueCenter),
                $"楕円フィット中心が採用されていません: 切り出し中心={cropCenterX}");
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // 検出領域が切り出しより大きい場合、楕円フィット中心を採ると円盤の外周を切り落とす。
    // このときは bbox 中心へフォールバックし、欠損を四方へ均等に分ける
    [Fact]
    public void Split_FallsBackToBoundingBoxCenterWhenRegionExceedsCrop()
    {
        using Mat raw = new(700, 700, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(350, 350), StandardRadius, DiscGray, thickness: -1);
        // bbox 幅を切り出しサイズ(250px)より大きくする
        Cv2.Rectangle(raw, new Rect(471, 340, 18, 20), DiscGray, thickness: -1);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            int cropCenterX = disc.RegionInSheet.X + (disc.RegionInSheet.Width / 2);

            // bbox は 229..489 なのでその中心は 359
            Assert.InRange(cropCenterX, 357, 361);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // シート外へはみ出す場合も切り縮めず白で埋める。切り縮めると円盤が中心からずれてしまう
    [Fact]
    public void Split_PadsWithWhiteWhenCropExceedsSheet()
    {
        // 円盤がシート左上隅からはみ出しており、切り出しの起点が負になる
        SheetImage sheet = BuildSheet(600, 600, [(110, 110, StandardRadius)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = TestDpi })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.Equal(FixedCropSize, disc.Pixels.Width);
            Assert.Equal(FixedCropSize, disc.Pixels.Height);
            Assert.True(disc.RegionInSheet.X < 0, $"シート外へ伸びていません: {disc.RegionInSheet}");
            Assert.True(disc.RegionInSheet.Y < 0, $"シート外へ伸びていません: {disc.RegionInSheet}");

            // 左上隅はシート外なので白で埋まっている
            Vec3b corner = disc.Pixels.At<Vec3b>(0, 0);
            Assert.Equal(255, corner.Item0);
            Assert.Equal(255, corner.Item1);
            Assert.Equal(255, corner.Item2);
        }
        finally
        {
            discs.ForEach(disc => disc.Dispose());
        }
    }

    // DPI 不明時は規格サイズを算出できないため、従来どおり bbox + パディングで切り出す
    [Fact]
    public void Split_AppliesPaddingAroundDetectedRegionWithoutDpi()
    {
        // フォールバックの最小サイズ 1000px を満たす円盤が要る
        SheetImage sheet = BuildSheet(1400, 1400, [(700, 700, 550)]);

        List<DiscImage> discs = [.. new SheetSplitter().Split(
            sheet,
            new DiscSplitOptions { Dpi = null, PaddingPx = 20 })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            // 円盤の bbox は (150,150)-(1250,1250)。解析スケール往復で ±4px の誤差が出る
            Assert.InRange(disc.RegionInSheet.Left, 126, 134);
            Assert.InRange(disc.RegionInSheet.Top, 126, 134);
            Assert.InRange(disc.RegionInSheet.Right, 1266, 1274);
            Assert.InRange(disc.RegionInSheet.Bottom, 1266, 1274);
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

    // 円盤は正円なので、規格径に収まっていても縦横比が外れた塊は除外する。
    // 矩形は 255x225 で両辺とも採用範囲(Dpi=50 では 217〜270px)に入るため、
    // サイズ判定では落ちず縦横比(1.133 > 1.10)だけが除外理由になる
    [Fact]
    public void Split_ExcludesNonCircularRegion()
    {
        using Mat raw = new(600, 900, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(250, 300), StandardRadius, DiscGray, thickness: -1);
        Cv2.Rectangle(raw, new Rect(600, 180, 255, 225), DiscGray, thickness: -1);
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

    // 円盤の内側の印刷リングが独立した候補として残る。DPI 既知なら規格径の上限で
    // 落ちるが、DPI 不明時は最小サイズしか課せないため入れ子判定で除外する(#91)
    [Fact]
    public void Split_ExcludesNestedCandidateWithoutDpi()
    {
        using Mat raw = new(1400, 1400, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Circle(raw, new Point(700, 700), 650, DiscGray, thickness: 3);
        // 内側のリングも単独では最小サイズ 1000px を満たしてしまう
        Cv2.Circle(raw, new Point(700, 700), 501, DiscGray, thickness: 3);
        SheetImage sheet = Encode(raw);

        List<DiscImage> discs = [.. new SheetSplitter().Split(sheet, new DiscSplitOptions { Dpi = null })];
        try
        {
            DiscImage disc = Assert.Single(discs);
            Assert.True(disc.RegionInSheet.Width > 1200, $"外側の円盤が採用されていません: {disc.RegionInSheet}");
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

    // 20 度の円弧を 30 度おきに描き、どの連結成分も最小サイズに届かないようにする
    private static void DrawBrokenRing(Mat sheet, Point center, int radius)
    {
        for (int angle = 0; angle < 360; angle += 30)
        {
            Cv2.Ellipse(
                sheet,
                center,
                new Size(radius, radius),
                angle: 0,
                startAngle: angle,
                endAngle: angle + 20,
                DiscGray,
                thickness: 3);
        }
    }

    private static SheetImage Encode(Mat sheet)
    {
        Cv2.ImEncode(".png", sheet, out byte[] encoded);
        return new SheetImage("synthetic.png", PageIndex: 0, encoded);
    }
}
