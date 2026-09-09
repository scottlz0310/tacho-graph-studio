using OpenCvSharp;

using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

// 背景除去のアルファ円は本処理と検出プレビュー(#126)で共有するため、幾何の契約を固定する
public sealed class DiscAlphaCircleTests
{
    private static readonly Point2f Center = new(200f, 200f);
    private static readonly Size ImageSize = new(400, 400);

    [Theory]
    [InlineData(0, 100f)]
    [InlineData(10, 120f)]
    [InlineData(-10, 80f)]
    public void Resolve_AppliesPaddingToBothSides(int ellipsePaddingPx, float expectedDiameter)
    {
        RotatedRect circle = DiscAlphaCircle.Resolve(
            Center,
            discDiameter: 100f,
            ImageSize,
            new BackgroundRemovalOptions { EllipsePaddingPx = ellipsePaddingPx });

        Assert.Equal(Center, circle.Center);
        Assert.Equal(expectedDiameter, circle.Size.Width);
        Assert.Equal(expectedDiameter, circle.Size.Height);
    }

    // 画像全体を覆うより大きなマージンは、座標変換をオーバーフローさせないよう有効径で頭打ちにする
    [Fact]
    public void Resolve_ClampsDiameterToImageDiagonal()
    {
        RotatedRect circle = DiscAlphaCircle.Resolve(
            Center,
            discDiameter: 100f,
            ImageSize,
            new BackgroundRemovalOptions { EllipsePaddingPx = int.MaxValue / 2 });

        double diagonalRadius = Math.Sqrt((200.0 * 200.0) + (200.0 * 200.0));
        Assert.Equal((float)((diagonalRadius * 2) + 1), circle.Size.Width);
    }

    [Fact]
    public void Resolve_ThrowsWhenCircleVanishes()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DiscAlphaCircle.Resolve(
                Center,
                discDiameter: 100f,
                ImageSize,
                new BackgroundRemovalOptions { EllipsePaddingPx = -60 }));

        Assert.Contains("EllipsePaddingPx", exception.Message, StringComparison.Ordinal);
    }
}
