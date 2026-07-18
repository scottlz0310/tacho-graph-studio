using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Tests.Templates;

// GIMP 版 TachoGraphWizard で実運用していたテンプレート JSON(examples/ からコピー)を
// そのまま読めることを固定する互換性回帰テスト(FR-25)
public sealed class GimpTemplateCompatibilityTests
{
    [Theory]
    [InlineData("Task-Meter.json", "Task-Meter", 1481, 1483)]
    [InlineData("Yazaki45.json", "Yazaki45", 1453, 1456)]
    public void Deserialize_LoadsRealGimpTemplate(
        string fileName,
        string expectedName,
        int expectedWidth,
        int expectedHeight)
    {
        string json = ReadFixture(fileName);

        ChartTemplate template = ChartTemplateSerializer.Deserialize(json);

        Assert.Equal(expectedName, template.Name);
        Assert.Equal("1.0", template.Version);
        Assert.Equal(expectedWidth, template.ReferenceWidth);
        Assert.Equal(expectedHeight, template.ReferenceHeight);
        Assert.Contains("driver", template.Fields.Keys);
        Assert.Contains("vehicle_no", template.Fields.Keys);
        Assert.Contains("date_year", template.Fields.Keys);
    }

    [Fact]
    public void Deserialize_ReadsFieldDetailsFromRealTemplate()
    {
        ChartTemplate template = ChartTemplateSerializer.Deserialize(ReadFixture("Task-Meter.json"));

        TextFieldDefinition driver = template.Fields["driver"];
        Assert.Equal(0.34031060094530724, driver.Position.XRatio, precision: 15);
        Assert.Equal(0.39109912339851655, driver.Position.YRatio, precision: 15);
        Assert.Equal("Sans-serif", driver.Font.Family);
        Assert.Equal(0.02, driver.Font.SizeRatio);
        Assert.Equal("#000000", driver.Font.Color);
        Assert.False(driver.Font.Bold);
        Assert.Equal(TextAlignment.Left, driver.Align);
        Assert.Equal(VerticalTextAlignment.Top, driver.VerticalAlign);
        Assert.True(driver.Visible);
        Assert.False(driver.Required);
    }

    [Theory]
    [InlineData("Task-Meter.json")]
    [InlineData("Yazaki45.json")]
    public void SerializeRoundTrip_PreservesRealTemplate(string fileName)
    {
        ChartTemplate original = ChartTemplateSerializer.Deserialize(ReadFixture(fileName));

        ChartTemplate reloaded = ChartTemplateSerializer.Deserialize(ChartTemplateSerializer.Serialize(original));

        Assert.Equal(original.Name, reloaded.Name);
        Assert.Equal(original.Version, reloaded.Version);
        Assert.Equal(original.Description, reloaded.Description);
        Assert.Equal(original.ReferenceWidth, reloaded.ReferenceWidth);
        Assert.Equal(original.ReferenceHeight, reloaded.ReferenceHeight);
        Assert.Equal(original.Fields.Keys.Order(), reloaded.Fields.Keys.Order());
        foreach ((string name, TextFieldDefinition field) in original.Fields)
        {
            Assert.Equal(field, reloaded.Fields[name]);
        }
    }

    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", "Fixtures", fileName));
}
