using TachoGraphStudio.App.Templates;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

public sealed class TemplateFieldViewModelTests
{
    [Fact]
    public void Constructor_DoesNotInvokeOnEdited()
    {
        int editCount = 0;

        _ = CreateField(() => editCount++);

        Assert.Equal(0, editCount);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.1, 0.0)]
    [InlineData(1.5, 1.0)]
    public void XRatio_ClampsIntoUnitRange(double value, double expected)
    {
        TemplateFieldViewModel field = CreateField(() => { });

        field.XRatio = value;

        Assert.Equal(expected, field.XRatio);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void XRatio_NonFiniteKeepsPreviousValueAndNotifies(double value)
    {
        TemplateFieldViewModel field = CreateField(() => { });
        field.XRatio = 0.25;
        List<string?> changedProperties = [];
        field.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        field.XRatio = value;

        Assert.Equal(0.25, field.XRatio);
        Assert.Equal([nameof(TemplateFieldViewModel.XRatio)], changedProperties);
    }

    [Theory]
    [InlineData(0.03, 0.03)]
    [InlineData(1.5, 1.0)]
    public void SizeRatio_ClampsUpperBound(double value, double expected)
    {
        TemplateFieldViewModel field = CreateField(() => { });

        field.SizeRatio = value;

        Assert.Equal(expected, field.SizeRatio);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void SizeRatio_InvalidKeepsPreviousValue(double value)
    {
        TemplateFieldViewModel field = CreateField(() => { });
        field.SizeRatio = 0.05;

        field.SizeRatio = value;

        Assert.Equal(0.05, field.SizeRatio);
    }

    [Fact]
    public void PropertyEdits_InvokeOnEditedOnlyOnActualChange()
    {
        int editCount = 0;
        TemplateFieldViewModel field = CreateField(() => editCount++);

        field.Bold = true;
        field.Bold = true;
        field.Color = "#ff0000";
        field.XRatio = 0.75;
        field.XRatio = double.NaN;

        Assert.Equal(3, editCount);
    }

    [Fact]
    public void ToDefinition_RoundTripsAllProperties()
    {
        TextFieldDefinition definition = new()
        {
            Position = new TextPosition { XRatio = 0.4, YRatio = 0.6 },
            Font = new TextFont
            {
                Family = "Meiryo",
                SizeRatio = 0.05,
                Color = "#123abc",
                Bold = true,
                Italic = true,
            },
            Align = TextAlignment.Center,
            VerticalAlign = VerticalTextAlignment.Bottom,
            Visible = false,
            Required = true,
        };

        TemplateFieldViewModel field = new("driver", definition, () => { });

        Assert.Equal(definition, field.ToDefinition());
    }

    private static TemplateFieldViewModel CreateField(Action onEdited) =>
        new("driver", new TextFieldDefinition(), onEdited);
}
