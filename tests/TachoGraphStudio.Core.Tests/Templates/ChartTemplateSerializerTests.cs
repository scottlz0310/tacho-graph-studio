using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Tests.Templates;

public sealed class ChartTemplateSerializerTests
{
    [Fact]
    public void Deserialize_MissingKeysFallBackToGimpDefaults()
    {
        // 旧実装(templates/models.py)は欠落キーを既定値で補う。同じ挙動を維持する
        ChartTemplate template = ChartTemplateSerializer.Deserialize("""{"fields": {"driver": {}}}""");

        Assert.Equal("Untitled", template.Name);
        Assert.Equal("1.0", template.Version);
        Assert.Equal("", template.Description);
        Assert.Equal(1000, template.ReferenceWidth);
        Assert.Equal(1000, template.ReferenceHeight);
        TextFieldDefinition field = template.Fields["driver"];
        Assert.Equal(0.0, field.Position.XRatio);
        Assert.Equal("Arial", field.Font.Family);
        Assert.Equal(0.03, field.Font.SizeRatio);
        Assert.Equal("#000000", field.Font.Color);
        Assert.Equal(TextAlignment.Left, field.Align);
        Assert.Equal(VerticalTextAlignment.Top, field.VerticalAlign);
        Assert.True(field.Visible);
        Assert.False(field.Required);
    }

    [Fact]
    public void Serialize_WritesSnakeCaseKeysAndLowercaseEnums()
    {
        ChartTemplate template = new()
        {
            Name = "テスト様式",
            Fields = new Dictionary<string, TextFieldDefinition>
            {
                ["vehicle_no"] = new()
                {
                    Position = new TextPosition { XRatio = 0.5, YRatio = 0.25 },
                    Align = TextAlignment.Center,
                    VerticalAlign = VerticalTextAlignment.Middle,
                },
            },
        };

        string json = ChartTemplateSerializer.Serialize(template);

        Assert.Contains("\"reference_width\"", json);
        Assert.Contains("\"x_ratio\"", json);
        Assert.Contains("\"size_ratio\"", json);
        Assert.Contains("\"vertical_align\"", json);
        Assert.Contains("\"center\"", json);
        Assert.Contains("\"middle\"", json);
        Assert.Contains("\"vehicle_no\"", json);
    }

    [Theory]
    [InlineData("""{"fields": {"f": {"position": {"x_ratio": 1.5}}}}""", "position")]
    [InlineData("""{"fields": {"f": {"position": {"y_ratio": -0.1}}}}""", "position")]
    [InlineData("""{"fields": {"f": {"font": {"size_ratio": 0}}}}""", "size_ratio")]
    [InlineData("""{"fields": {"f": {"font": {"size_ratio": 1.5}}}}""", "size_ratio")]
    [InlineData("""{"fields": {"f": {"font": {"color": "red"}}}}""", "color")]
    [InlineData("""{"reference_width": 0}""", "reference_width")]
    public void Deserialize_InvalidValuesThrowWithContext(string json, string expectedInMessage)
    {
        TemplateFormatException exception = Assert.Throws<TemplateFormatException>(
            () => ChartTemplateSerializer.Deserialize(json));

        Assert.Contains(expectedInMessage, exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"fields": {"f": {"align": "diagonal"}}}""")]
    [InlineData("null")]
    public void Deserialize_UnparsableJsonThrows(string json)
    {
        Assert.Throws<TemplateFormatException>(() => ChartTemplateSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("""{"name": null}""", "name")]
    [InlineData("""{"version": null}""", "name / version")]
    [InlineData("""{"description": null}""", "description")]
    [InlineData("""{"fields": null}""", "fields")]
    [InlineData("""{"fields": {"driver": null}}""", "driver")]
    [InlineData("""{"fields": {"driver": {"position": null}}}""", "position")]
    [InlineData("""{"fields": {"driver": {"font": null}}}""", "font")]
    [InlineData("""{"fields": {"driver": {"font": {"family": null}}}}""", "family")]
    [InlineData("""{"fields": {"driver": {"font": {"color": null}}}}""", "color")]
    public void Deserialize_ExplicitNullThrowsTemplateFormatException(string json, string expectedInMessage)
    {
        // JSON の明示的な null は init 既定値を上書きするため、実装例外
        // (NullReferenceException 等)ではなく TemplateFormatException で報告する
        TemplateFormatException exception = Assert.Throws<TemplateFormatException>(
            () => ChartTemplateSerializer.Deserialize(json));

        Assert.Contains(expectedInMessage, exception.Message);
    }

    [Theory]
    [InlineData(1000, 800, 0.5, 0.25, 0.02, 500.0, 200.0, 16.0)]
    [InlineData(800, 1000, 0.5, 0.25, 0.02, 400.0, 250.0, 16.0)]
    [InlineData(2960, 2966, 0.0, 1.0, 0.03, 0.0, 2966.0, 88.8)]
    public void CalculatePlacement_UsesRatiosAndShorterSide(
        int width,
        int height,
        double xRatio,
        double yRatio,
        double sizeRatio,
        double expectedX,
        double expectedY,
        double expectedFontSize)
    {
        TextFieldDefinition field = new()
        {
            Position = new TextPosition { XRatio = xRatio, YRatio = yRatio },
            Font = new TextFont { SizeRatio = sizeRatio },
        };

        TextPlacement placement = field.CalculatePlacement(width, height);

        Assert.Equal(expectedX, placement.X, precision: 6);
        Assert.Equal(expectedY, placement.Y, precision: 6);
        Assert.Equal(expectedFontSize, placement.FontSizePx, precision: 6);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    public void CalculatePlacement_InvalidImageSizeThrows(int width, int height)
    {
        TextFieldDefinition field = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => field.CalculatePlacement(width, height));
    }
}
