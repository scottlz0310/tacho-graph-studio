using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Tests.Templates;

public sealed class ChartTextComposerTests
{
    private static readonly ChartTextValues Values = new()
    {
        DateText = "2026/12/25",
        RegistrationNumber = "旭川123-45",
        Driver = "山田 太郎",
        VehicleType = "グレーダ",
    };

    [Theory]
    [InlineData("date_year", "2026")]
    [InlineData("date_month", "12")]
    [InlineData("date_day", "25")]
    [InlineData("vehicle_no", "旭川123-45")]
    [InlineData("driver", "山田 太郎")]
    [InlineData("vehicle_type", "グレーダ")]
    public void Compose_MapsStandardFieldToValue(string fieldName, string expectedText)
    {
        ChartTemplate template = BuildTemplate(fieldName);

        IReadOnlyList<PlacedText> result = ChartTextComposer.Compose(template, Values, 1000, 1000);

        PlacedText placed = Assert.Single(result);
        Assert.Equal(fieldName, placed.FieldName);
        Assert.Equal(expectedText, placed.Text);
    }

    [Theory]
    [InlineData("2026-12-25", "2026", "12", "25")]
    [InlineData("2026.12.25", "2026", "12", "25")]
    [InlineData("R8/12/25", "R8", "12", "25")]
    public void Compose_SplitsDateTextOnCommonSeparators(
        string dateText, string year, string month, string day)
    {
        ChartTemplate template = BuildTemplate("date_year", "date_month", "date_day");

        IReadOnlyList<PlacedText> result = ChartTextComposer.Compose(
            template, Values with { DateText = dateText }, 1000, 1000);

        Assert.Equal(["date_day", "date_month", "date_year"], result.Select(placed => placed.FieldName));
        Assert.Equal([day, month, year], result.Select(placed => placed.Text));
    }

    [Theory]
    [InlineData("2026/12")]
    [InlineData("2026")]
    public void Compose_OmitsMissingDateParts(string dateText)
    {
        ChartTemplate template = BuildTemplate("date_year", "date_month", "date_day");

        IReadOnlyList<PlacedText> result = ChartTextComposer.Compose(
            template, Values with { DateText = dateText }, 1000, 1000);

        Assert.DoesNotContain("date_day", result.Select(placed => placed.FieldName));
    }

    [Fact]
    public void Compose_SkipsInvisibleEmptyAndUnknownFields()
    {
        ChartTemplate template = new()
        {
            Name = "Test",
            Fields = new Dictionary<string, TextFieldDefinition>
            {
                ["driver"] = new() { Visible = false },
                ["vehicle_no"] = new(),
                ["custom_field"] = new(),
            },
        };

        IReadOnlyList<PlacedText> result = ChartTextComposer.Compose(
            template, Values with { RegistrationNumber = "" }, 1000, 1000);

        Assert.Empty(result);
    }

    [Fact]
    public void Compose_CalculatesPlacementFromRatios()
    {
        ChartTemplate template = new()
        {
            Name = "Test",
            Fields = new Dictionary<string, TextFieldDefinition>
            {
                ["driver"] = new()
                {
                    Position = new TextPosition { XRatio = 0.5, YRatio = 0.25 },
                    Font = new TextFont { SizeRatio = 0.02 },
                },
            },
        };

        IReadOnlyList<PlacedText> result = ChartTextComposer.Compose(template, Values, 2000, 1000);

        PlacedText placed = Assert.Single(result);
        Assert.Equal(1000.0, placed.Placement.X);
        Assert.Equal(250.0, placed.Placement.Y);
        // フォントサイズは短辺比
        Assert.Equal(20.0, placed.Placement.FontSizePx);
    }

    private static ChartTemplate BuildTemplate(params string[] fieldNames) => new()
    {
        Name = "Test",
        Fields = fieldNames.ToDictionary(name => name, _ => new TextFieldDefinition()),
    };
}
