using TachoGraphStudio.Core.Imaging;

namespace TachoGraphStudio.Core.Tests.Imaging;

public sealed class PremultipliedAlphaTests
{
    [Theory]
    [InlineData(255, 200, 200)]
    [InlineData(0, 200, 0)]
    [InlineData(128, 255, 128)]
    [InlineData(128, 100, 50)]
    public void FromStraightBgra_MultipliesColorChannelsByAlpha(byte alpha, byte color, byte expected)
    {
        byte[] straight = [color, color, color, alpha];

        byte[] premultiplied = PremultipliedAlpha.FromStraightBgra(straight);

        Assert.Equal([expected, expected, expected, alpha], premultiplied);
    }

    [Fact]
    public void FromStraightBgra_InvalidLengthThrows()
    {
        Assert.Throws<ArgumentException>(() => PremultipliedAlpha.FromStraightBgra(new byte[5]));
    }
}
