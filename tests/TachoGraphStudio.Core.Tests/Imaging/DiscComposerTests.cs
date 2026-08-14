using SkiaSharp;

using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Tests.Imaging;

public sealed class DiscComposerTests
{
    [Fact]
    public void ComposePng_NoRotationNoTextPreservesPixels()
    {
        (byte[] bgra, int width, int height) = BuildSource(64, 64);

        byte[] png = DiscComposer.ComposePng(bgra, width, height, 0.0, template: null, values: null);

        using SKBitmap decoded = DecodeUnpremul(png, width, height);
        // 中心の不透明マーカー(赤)と四隅の完全透過が保持される
        SKColor center = decoded.GetPixel(width / 2, height / 2);
        Assert.Equal(255, center.Alpha);
        Assert.Equal(255, center.Red);
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
        Assert.Equal(0, decoded.GetPixel(width - 1, height - 1).Alpha);
    }

    [Fact]
    public void ComposePng_RotationIsClockwiseLikePreview()
    {
        // 上端中央に不透明マーカーを置き、時計回り 90° で右端中央へ移動することを確認する
        // (プレビューの RotateTransform と同じ向き)
        const int size = 101;
        byte[] bgra = new byte[size * size * 4];
        SetPixel(bgra, size, x: size / 2, y: 5, blue: 0, green: 0, red: 255, alpha: 255);

        byte[] png = DiscComposer.ComposePng(bgra, size, size, 90.0, template: null, values: null);

        using SKBitmap decoded = DecodeUnpremul(png, size, size);
        Assert.True(decoded.GetPixel(size - 6, size / 2).Alpha > 128);
        Assert.Equal(0, decoded.GetPixel(size / 2, 5).Alpha);
    }

    [Fact]
    public void ComposePng_DrawsJapaneseTextAtPlacement()
    {
        (byte[] bgra, int width, int height) = BuildSource(400, 400);
        ChartTemplate template = new()
        {
            Name = "Test",
            Fields = new Dictionary<string, TextFieldDefinition>
            {
                ["driver"] = new()
                {
                    Position = new TextPosition { XRatio = 0.5, YRatio = 0.5 },
                    Font = new TextFont { Family = "Arial", SizeRatio = 0.1, Color = "#0000ff" },
                    Align = TextAlignment.Center,
                    VerticalAlign = VerticalTextAlignment.Middle,
                },
            },
        };
        ChartTextValues values = new() { Driver = "山田" };

        byte[] png = DiscComposer.ComposePng(bgra, width, height, 0.0, template, values);

        using SKBitmap decoded = DecodeUnpremul(png, width, height);
        // 中央付近(テキストボックス内)に指定色(#0000ff)の画素が描画される
        // (フォントフォールバックにより日本語グリフが tofu にならないこと)
        Assert.True(CountPixels(decoded, 100, 300, 100, 300, color => color is { Blue: > 200, Red: < 50, Alpha: > 200 }) > 20);
    }

    [Fact]
    public void ComposePng_ScalesTextWithOutput()
    {
        (byte[] bgra, int width, int height) = BuildSolidSource(400, 400);
        ChartTemplate template = new()
        {
            Name = "Test",
            Fields = new Dictionary<string, TextFieldDefinition>
            {
                ["driver"] = new()
                {
                    Position = new TextPosition { XRatio = 0.5, YRatio = 0.5 },
                    Font = new TextFont { Family = "Arial", SizeRatio = 0.1, Color = "#0000ff" },
                    Align = TextAlignment.Center,
                    VerticalAlign = VerticalTextAlignment.Middle,
                },
            },
        };

        byte[] png = DiscComposer.ComposePng(
            bgra,
            width,
            height,
            0.0,
            template,
            new ChartTextValues { Driver = "山田" },
            outputDpi: 300);

        using SKBitmap decoded = DecodeUnpremul(png, 200, 200);
        Assert.True(CountPixels(decoded, 50, 150, 50, 150, color => color is { Blue: > 200, Red: < 50, Alpha: > 200 }) > 5);
    }

    [Theory]
    [InlineData(600, 600)]
    [InlineData(300, 300)]
    [InlineData(200, 200)]
    [InlineData(100, 100)]
    public void ComposePng_ResizesToRequestedDpi(int outputDpi, int expectedSize)
    {
        (byte[] bgra, int width, int height) = BuildSolidSource(600, 600);

        byte[] png = DiscComposer.ComposePng(
            bgra,
            width,
            height,
            0.0,
            template: null,
            values: null,
            outputDpi: outputDpi);

        using SKBitmap decoded = DecodeUnpremul(png, expectedSize, expectedSize);
        Assert.Equal(255, decoded.GetPixel(expectedSize / 2, expectedSize / 2).Red);
    }

    [Theory]
    [InlineData(100, 3937u)]
    [InlineData(200, 7874u)]
    [InlineData(300, 11811u)]
    [InlineData(600, 23622u)]
    public void ComposePng_EmbedsPhysicalResolution(int outputDpi, uint expectedPixelsPerMeter)
    {
        (byte[] bgra, int width, int height) = BuildSource(64, 64);

        byte[] png = DiscComposer.ComposePng(
            bgra,
            width,
            height,
            0.0,
            template: null,
            values: null,
            outputDpi: outputDpi,
            includePhysicalResolution: true);

        (uint x, uint y, byte unit) = ReadPhysicalResolution(png);
        Assert.Equal(expectedPixelsPerMeter, x);
        Assert.Equal(expectedPixelsPerMeter, y);
        Assert.Equal(1, unit);
    }

    [Fact]
    public void ComposePng_DoesNotEmbedPhysicalResolutionByDefault()
    {
        (byte[] bgra, int width, int height) = BuildSource(64, 64);

        byte[] png = DiscComposer.ComposePng(bgra, width, height, 0.0, null, null);

        Assert.Throws<InvalidOperationException>(() => ReadPhysicalResolution(png));
    }

    [Fact]
    public void ComposePng_SkipsTextWhenTemplateOrValuesMissing()
    {
        (byte[] bgra, int width, int height) = BuildSource(64, 64);

        byte[] withoutTemplate = DiscComposer.ComposePng(bgra, width, height, 0.0, null, new ChartTextValues { Driver = "山田" });
        byte[] withoutValues = DiscComposer.ComposePng(
            bgra, width, height, 0.0,
            new ChartTemplate { Name = "T", Fields = new Dictionary<string, TextFieldDefinition> { ["driver"] = new() } },
            null);

        // どちらも文字なしの合成結果として同一
        Assert.Equal(withoutTemplate, withoutValues);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 4)]
    [InlineData(4, -1)]
    public void ComposePng_InvalidSizeThrows(int width, int height)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(
            () => DiscComposer.ComposePng(new byte[16], width, height, 0.0, null, null));
    }

    [Fact]
    public void ComposePng_MismatchedBufferThrows()
    {
        Assert.Throws<ArgumentException>(
            () => DiscComposer.ComposePng(new byte[15], 2, 2, 0.0, null, null));
    }

    private static (byte[] Bgra, int Width, int Height) BuildSource(int width, int height)
    {
        byte[] bgra = new byte[width * height * 4];
        SetPixel(bgra, width, width / 2, height / 2, blue: 0, green: 0, red: 255, alpha: 255);
        return (bgra, width, height);
    }

    private static (byte[] Bgra, int Width, int Height) BuildSolidSource(int width, int height)
    {
        byte[] bgra = new byte[width * height * 4];
        for (int offset = 0; offset < bgra.Length; offset += 4)
        {
            bgra[offset + 2] = 255;
            bgra[offset + 3] = 255;
        }

        return (bgra, width, height);
    }

    private static void SetPixel(
        byte[] bgra, int width, int x, int y, byte blue, byte green, byte red, byte alpha)
    {
        int offset = ((y * width) + x) * 4;
        bgra[offset] = blue;
        bgra[offset + 1] = green;
        bgra[offset + 2] = red;
        bgra[offset + 3] = alpha;
    }

    private static SKBitmap DecodeUnpremul(byte[] png, int expectedWidth, int expectedHeight)
    {
        SKBitmap decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(expectedWidth, decoded.Width);
        Assert.Equal(expectedHeight, decoded.Height);
        return decoded;
    }

    private static int CountPixels(
        SKBitmap bitmap, int fromX, int toX, int fromY, int toY, Func<SKColor, bool> predicate)
    {
        int count = 0;
        for (int y = fromY; y < toY; y++)
        {
            for (int x = fromX; x < toX; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static (uint X, uint Y, byte Unit) ReadPhysicalResolution(byte[] png)
    {
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                png.AsSpan(offset, 4)));
            if (png[offset + 4] == (byte)'p'
                && png[offset + 5] == (byte)'H'
                && png[offset + 6] == (byte)'Y'
                && png[offset + 7] == (byte)'s')
            {
                Assert.Equal(9, length);
                return (
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8, 4)),
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 12, 4)),
                    png[offset + 16]);
            }

            offset = checked(offset + 12 + length);
        }

        throw new InvalidOperationException("PNG に pHYs チャンクがありません。");
    }
}
