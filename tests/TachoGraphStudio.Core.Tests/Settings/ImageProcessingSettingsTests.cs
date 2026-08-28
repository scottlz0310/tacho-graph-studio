using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class ImageProcessingSettingsTests
{
    [Fact]
    public void Default_MatchesExistingImagingOptionDefaults()
    {
        ImageProcessingSettings settings = ImageProcessingSettings.Default;
        DiscSplitOptions splitOptions = new();
        BackgroundRemovalOptions removalOptions = new();

        Assert.Equal(splitOptions.Threshold, settings.Threshold);
        Assert.Equal(splitOptions.PaddingPx, settings.PaddingPx);
        Assert.Equal(removalOptions.EllipsePaddingPx, settings.EllipsePaddingPx);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(256, 20)]
    [InlineData(7, -1)]
    public void Validate_OutOfRangeSplitValueThrows(int threshold, int paddingPx)
    {
        ImageProcessingSettings settings = new()
        {
            Threshold = threshold,
            PaddingPx = paddingPx,
        };

        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Theory]
    [InlineData(-25)]
    [InlineData(0)]
    [InlineData(25)]
    public void Validate_EllipsePaddingAllowsNegativeAndPositiveValues(int ellipsePaddingPx)
    {
        ImageProcessingSettings settings = new() { EllipsePaddingPx = ellipsePaddingPx };

        settings.Validate();

        Assert.Equal(ellipsePaddingPx, settings.ToBackgroundRemovalOptions().EllipsePaddingPx);
    }

    [Fact]
    public void ToSplitOptions_MapsPersistedValuesAndRequestedDpi()
    {
        ImageProcessingSettings settings = new()
        {
            Threshold = 10,
            PaddingPx = 35,
        };

        DiscSplitOptions options = settings.ToSplitOptions(dpi: 600);

        Assert.Equal(10, options.Threshold);
        Assert.Equal(35, options.PaddingPx);
        Assert.Equal(600, options.Dpi);
    }
}
