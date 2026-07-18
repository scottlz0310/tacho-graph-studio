using TachoGraphStudio.App.Templates;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

public sealed class TemplateItemViewModelTests
{
    [Fact]
    public void Constructor_OrdersFieldsByName()
    {
        ChartTemplate template = CreateTemplate("Yazaki45", "vehicle_no", "date_year", "driver");

        TemplateItemViewModel item = new("Yazaki45", template);

        Assert.Equal(
            ["date_year", "driver", "vehicle_no"],
            item.Fields.Select(field => field.Name));
        Assert.False(item.IsDirty);
    }

    [Fact]
    public void ToChartTemplate_RoundTripsTemplate()
    {
        ChartTemplate template = CreateTemplate("Yazaki45", "driver", "vehicle_no");

        TemplateItemViewModel item = new("Yazaki45", template);

        Assert.Equal(
            ChartTemplateSerializer.Serialize(template),
            ChartTemplateSerializer.Serialize(item.ToChartTemplate()));
    }

    [Theory]
    [InlineData("driver")]
    [InlineData(" driver ")]
    public void ToChartTemplate_DuplicateFieldNameAfterRenameThrows(string renamed)
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45", "driver", "vehicle_no"));
        item.Fields[1].Name = renamed;

        TemplateFormatException exception = Assert.Throws<TemplateFormatException>(
            () => item.ToChartTemplate());

        Assert.Contains("driver", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToChartTemplate_BlankFieldNameAfterRenameThrows(string renamed)
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45", "driver"));
        item.Fields[0].Name = renamed;

        TemplateFormatException exception = Assert.Throws<TemplateFormatException>(
            () => item.ToChartTemplate());

        Assert.Contains("フィールド名", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToChartTemplate_TrimsRenamedFieldName()
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45", "driver"));
        item.Fields[0].Name = " vehicle_no ";

        ChartTemplate template = item.ToChartTemplate();

        Assert.Equal(["vehicle_no"], template.Fields.Keys);
    }

    [Fact]
    public void PropertyAndFieldEdits_MarkDirty()
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45", "driver"));

        Assert.False(item.IsDirty);
        item.Name = "変更後";
        Assert.True(item.IsDirty);

        item.IsDirty = false;
        item.Fields[0].XRatio = 0.9;
        Assert.True(item.IsDirty);
    }

    [Fact]
    public void AddAndRemoveField_MarkDirty()
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45", "driver"));

        TemplateFieldViewModel added = item.AddField("vehicle_no");

        Assert.True(item.IsDirty);
        Assert.Equal(2, item.Fields.Count);

        item.IsDirty = false;
        item.RemoveField(added);

        Assert.True(item.IsDirty);
        Assert.Single(item.Fields);
    }

    [Fact]
    public void MarkSaved_SetsIdAndClearsDirty()
    {
        TemplateItemViewModel item = new(id: null, CreateTemplate("新規"));
        item.Name = "変更後";

        item.MarkSaved("新規");

        Assert.Equal("新規", item.Id);
        Assert.False(item.IsDirty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ReferenceWidth_InvalidKeepsPreviousValue(int value)
    {
        TemplateItemViewModel item = new("Yazaki45", CreateTemplate("Yazaki45"));

        item.ReferenceWidth = value;

        Assert.Equal(1453, item.ReferenceWidth);
        Assert.False(item.IsDirty);
    }

    private static ChartTemplate CreateTemplate(string name, params string[] fieldNames) => new()
    {
        Name = name,
        Description = "テスト用",
        ReferenceWidth = 1453,
        ReferenceHeight = 1456,
        Fields = fieldNames.ToDictionary(
            fieldName => fieldName,
            fieldName => new TextFieldDefinition
            {
                Position = new TextPosition { XRatio = 0.4, YRatio = 0.5 },
            }),
    };
}
