using TachoGraphStudio.App.Stage;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class DiscWorkItemTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RotationAngle_NonFiniteValueKeepsPreviousAndNotifies(double nonFinite)
    {
        // NumberBox の空入力は Value=NaN として TwoWay binding 経由で書き込まれる
        DiscWorkItem item = BuildItem();
        item.RotationAngle = 45.0;
        List<string?> changedProperties = [];
        item.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        item.RotationAngle = nonFinite;

        Assert.Equal(45.0, item.RotationAngle);
        // UI 側の表示を有効値へ巻き戻すため、値が変わらなくても通知される
        Assert.Contains(nameof(DiscWorkItem.RotationAngle), changedProperties);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-64.5, -64.5)]
    [InlineData(180.0, 180.0)]
    [InlineData(-180.0, -180.0)]
    [InlineData(200.0, 180.0)]
    [InlineData(-200.0, -180.0)]
    public void RotationAngle_FiniteValueIsClampedToContractRange(double input, double expected)
    {
        DiscWorkItem item = BuildItem();

        item.RotationAngle = input;

        Assert.Equal(expected, item.RotationAngle);
    }

    private static DiscWorkItem BuildItem() => new(
        number: 1,
        new ProcessedDisc(
            SourcePath: "sheet.pdf",
            PageIndex: 0,
            IndexInSheet: 0,
            Width: 2,
            Height: 2,
            Bgra: new byte[16],
            ThumbnailWidth: 1,
            ThumbnailHeight: 1,
            ThumbnailPremultipliedBgra: new byte[4],
            EllipseCenterX: 1.0,
            EllipseCenterY: 1.0));
}
