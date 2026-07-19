using TachoGraphStudio.App.Stage;
using TachoGraphStudio.App.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

public sealed class TemplatePreviewLabelTests
{
    private static readonly DiscMetadata Metadata = new()
    {
        PrintDate = "2026/12/25",
        RegistrationNumber = "旭川123-45",
        DriverName = "山田 太郎",
        VehicleType = "グレーダ",
    };

    [Theory]
    [InlineData("date_year", "2026")]
    [InlineData("date_month", "12")]
    [InlineData("date_day", "25")]
    [InlineData("vehicle_no", "旭川123-45")]
    [InlineData("driver", "山田 太郎")]
    [InlineData("vehicle_type", "グレーダ")]
    public void Resolve_KnownFieldUsesDiscMetadata(string fieldName, string expected)
    {
        Assert.Equal(expected, TemplatePreviewLabel.Resolve(fieldName, Metadata));
    }

    [Theory]
    [InlineData("driver", "driver")]
    [InlineData("custom_field", "custom_field")]
    public void Resolve_MissingValueUsesFieldName(string fieldName, string expected)
    {
        Assert.Equal(expected, TemplatePreviewLabel.Resolve(fieldName, new DiscMetadata()));
    }

    [Fact]
    public void Resolve_MissingMetadataUsesFieldName()
    {
        Assert.Equal("driver", TemplatePreviewLabel.Resolve("driver", metadata: null));
    }
}
