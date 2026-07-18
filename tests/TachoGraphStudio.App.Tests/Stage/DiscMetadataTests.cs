using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class DiscMetadataTests
{
    [Fact]
    public void ToTextValues_MapsAllProperties()
    {
        DiscMetadata metadata = new()
        {
            PrintDate = "2026/12/25",
            RegistrationNumber = "旭川123-45",
            DriverName = "山田 太郎",
            VehicleType = "グレーダ",
        };

        ChartTextValues values = metadata.ToTextValues();

        Assert.Equal("2026/12/25", values.DateText);
        Assert.Equal("旭川123-45", values.RegistrationNumber);
        Assert.Equal("山田 太郎", values.Driver);
        Assert.Equal("グレーダ", values.VehicleType);
    }

    [Fact]
    public void Defaults_AreEmptyAndNotSkipped()
    {
        DiscMetadata metadata = new();

        Assert.Equal("", metadata.PrintDate);
        Assert.Equal("", metadata.RegistrationNumber);
        Assert.Equal("", metadata.DriverName);
        Assert.False(metadata.SkipHandwritten);
    }
}
