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

    // カーソルキー操作(#133)は 0.1 度ずつ加算するため、二進で表現できない誤差が
    // 角度欄へ出ないよう UI の刻みへ丸める
    [Theory]
    [InlineData(0.30000000450000003, 0.3)]
    [InlineData(0.04, 0.0)]
    [InlineData(0.05, 0.1)]
    [InlineData(-0.05, -0.1)]
    [InlineData(12.34, 12.3)]
    [InlineData(-12.36, -12.4)]
    public void RotationAngle_IsRoundedToUiStep(double input, double expected)
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
