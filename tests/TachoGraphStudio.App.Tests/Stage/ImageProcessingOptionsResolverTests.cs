using TachoGraphStudio.App.Imaging;
using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Tests.Stage;

// 本処理(StagePipeline)と設定画面の検出プレビュー(#126)が同じ設定解決を通ることを固定する
public sealed class ImageProcessingOptionsResolverTests
{
    [Theory]
    [InlineData("sheet.pdf", WindowsPdfRasterizer.DefaultDpi)]
    [InlineData("SHEET.PDF", WindowsPdfRasterizer.DefaultDpi)]
    [InlineData("sheet.jpg", null)]
    [InlineData("sheet.jpeg", null)]
    public void ResolveSplitOptions_UsesRasterizerDpiOnlyForPdf(string sourcePath, double? expectedDpi)
    {
        DiscSplitOptions options = new ImageProcessingOptionsResolver().ResolveSplitOptions(sourcePath, null);

        Assert.Equal(expectedDpi, options.Dpi);
    }

    [Theory]
    [InlineData("sheet.pdf", 1, 0, -5)]
    [InlineData("sheet.pdf", 255, 40, 12)]
    [InlineData("sheet.jpg", 7, 20, 0)]
    public void ResolveSplitOptions_AppliesSettings(
        string sourcePath,
        int threshold,
        int paddingPx,
        int ellipsePaddingPx)
    {
        ImageProcessingSettings settings = new()
        {
            Threshold = threshold,
            PaddingPx = paddingPx,
            EllipsePaddingPx = ellipsePaddingPx,
        };
        ImageProcessingOptionsResolver resolver = new();

        DiscSplitOptions splitOptions = resolver.ResolveSplitOptions(sourcePath, settings);
        BackgroundRemovalOptions removalOptions = resolver.ResolveRemovalOptions(settings);

        Assert.Equal(threshold, splitOptions.Threshold);
        Assert.Equal(paddingPx, splitOptions.PaddingPx);
        Assert.Equal(ellipsePaddingPx, removalOptions.EllipsePaddingPx);
    }

    // 設定は前景判定・切り出し余白・アルファ円マージンだけを差し替える。DPI や最大枚数など
    // 呼び出し側が構成した値は保持する
    [Fact]
    public void ResolveSplitOptions_KeepsConfiguredBaseOptions()
    {
        ImageProcessingOptionsResolver resolver = new(
            imageSplitOptions: new DiscSplitOptions { Dpi = 50.0, MaxDiscs = 2 });

        DiscSplitOptions options = resolver.ResolveSplitOptions(
            "sheet.jpg",
            new ImageProcessingSettings { Threshold = 9 });

        Assert.Equal(50.0, options.Dpi);
        Assert.Equal(2, options.MaxDiscs);
        Assert.Equal(9, options.Threshold);
    }

    [Fact]
    public void ResolveOptions_WithoutSettingsReturnsConfiguredOptions()
    {
        ImageProcessingOptionsResolver resolver = new(
            imageSplitOptions: new DiscSplitOptions { Threshold = 3, PaddingPx = 7 },
            removalOptions: new BackgroundRemovalOptions { EllipsePaddingPx = 11 });

        DiscSplitOptions splitOptions = resolver.ResolveSplitOptions("sheet.jpg", null);

        Assert.Equal(3, splitOptions.Threshold);
        Assert.Equal(7, splitOptions.PaddingPx);
        Assert.Equal(11, resolver.ResolveRemovalOptions(null).EllipsePaddingPx);
    }

    [Fact]
    public void ResolveSplitOptions_InvalidSettingsThrows()
    {
        ImageProcessingSettings settings = new() { Threshold = 0 };

        Assert.Throws<ArgumentException>(
            () => new ImageProcessingOptionsResolver().ResolveSplitOptions("sheet.pdf", settings));
    }
}
