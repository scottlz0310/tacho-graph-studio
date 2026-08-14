using System.Buffers.Binary;
using System.Runtime.InteropServices;

using SkiaSharp;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Imaging;

// 回転補正と文字入れをフル解像度で本合成し、アルファ付き PNG を生成する(FR-19)。
// 回転は premultiplied で補間されるよう SkiaSharp のキャンバス変換で行い、
// 文字はプレビュー(FR-18)と同じく回転と独立に ChartTextComposer の配置で描画する
public static class DiscComposer
{
    public const int DefaultDpi = 600;

    public static byte[] ComposePng(
        byte[] bgra,
        int width,
        int height,
        double rotationAngleDegrees,
        ChartTemplate? template,
        ChartTextValues? values,
        int outputDpi = DefaultDpi,
        bool includePhysicalResolution = false)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputDpi);
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"BGRA バッファ長がサイズと一致しません: {bgra.Length} != {width}x{height}x4",
                nameof(bgra));
        }

        SKImageInfo sourceInfo = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKBitmap source = new(sourceInfo);
        Marshal.Copy(bgra, 0, source.GetPixels(), bgra.Length);

        double scale = (double)outputDpi / DefaultDpi;
        int outputWidth = ScaleDimension(width, scale);
        int outputHeight = ScaleDimension(height, scale);

        using SKSurface surface = SKSurface.Create(
            new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        if (scale != 1.0)
        {
            // 文字の配置計算は元画像の座標系のまま行い、キャンバスだけを縮小する。
            canvas.Scale((float)scale);
        }

        canvas.Save();
        if (rotationAngleDegrees != 0.0)
        {
            // SKCanvas.RotateDegrees は正で時計回り。プレビューの RotateTransform と同じ向き
            canvas.RotateDegrees((float)rotationAngleDegrees, width / 2f, height / 2f);
        }

        using (SKImage sourceImage = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(
                sourceImage,
                0,
                0,
                scale < 1.0
                    ? new SKSamplingOptions(SKCubicResampler.Mitchell)
                    : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        // 回転だけを解除し、縮小変換は文字描画まで維持する。
        canvas.Restore();

        if (template is not null && values is not null)
        {
            foreach (PlacedText placed in ChartTextComposer.Compose(template, values, width, height))
            {
                DrawText(canvas, placed);
            }
        }

        canvas.Restore();

        using SKImage composed = surface.Snapshot();
        using SKData png = composed.Encode(SKEncodedImageFormat.Png, quality: 100);
        byte[] encoded = png.ToArray();
        return includePhysicalResolution
            ? AddPhysicalResolution(encoded, outputDpi)
            : encoded;
    }

    private static int ScaleDimension(int dimension, double scale)
    {
        double scaled = dimension * scale;
        if (scaled > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimension),
                "出力画像のサイズが大きすぎます。");
        }

        return Math.Max(1, (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    private static byte[] AddPhysicalResolution(byte[] png, int outputDpi)
    {
        const int signatureLength = 8;
        const int ihdrDataLength = 13;
        const int ihdrChunkLength = 4 + 4 + ihdrDataLength + 4;
        const int physChunkLength = 4 + 4 + 9 + 4;

        if (png.Length < signatureLength + ihdrChunkLength
            || png[0] != 0x89
            || png[1] != 0x50
            || png[2] != 0x4E
            || png[3] != 0x47
            || png[4] != 0x0D
            || png[5] != 0x0A
            || png[6] != 0x1A
            || png[7] != 0x0A
            || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(signatureLength, 4)) != ihdrDataLength
            || png[12] != (byte)'I'
            || png[13] != (byte)'H'
            || png[14] != (byte)'D'
            || png[15] != (byte)'R')
        {
            throw new InvalidDataException("PNG の IHDR チャンクを検証できません。");
        }

        uint pixelsPerMeter = ToPixelsPerMeter(outputDpi);
        byte[] result = new byte[checked(png.Length + physChunkLength)];
        int ihdrEnd = signatureLength + ihdrChunkLength;
        png.AsSpan(0, ihdrEnd).CopyTo(result);

        int chunkOffset = ihdrEnd;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(chunkOffset, 4), 9);
        result[chunkOffset + 4] = (byte)'p';
        result[chunkOffset + 5] = (byte)'H';
        result[chunkOffset + 6] = (byte)'Y';
        result[chunkOffset + 7] = (byte)'s';
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(chunkOffset + 8, 4), pixelsPerMeter);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(chunkOffset + 12, 4), pixelsPerMeter);
        result[chunkOffset + 16] = 1;
        uint crc = ComputeCrc32(result.AsSpan(chunkOffset + 4, 13));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(chunkOffset + 17, 4), crc);

        png.AsSpan(ihdrEnd).CopyTo(result.AsSpan(chunkOffset + physChunkLength));
        return result;
    }

    private static uint ToPixelsPerMeter(int dpi)
    {
        double pixelsPerMeter = dpi / 0.0254;
        if (pixelsPerMeter > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI が大きすぎます。");
        }

        return checked((uint)Math.Round(pixelsPerMeter, MidpointRounding.AwayFromZero));
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        const uint polynomial = 0xEDB88320u;
        uint crc = uint.MaxValue;

        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0
                    ? crc >> 1
                    : (crc >> 1) ^ polynomial;
            }
        }

        return ~crc;
    }

    private static void DrawText(SKCanvas canvas, PlacedText placed)
    {
        TextFont fontDefinition = placed.Definition.Font;
        SKFontStyle style = new(
            fontDefinition.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            fontDefinition.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        using SKFont font = new(
            SelectTypeface(fontDefinition.Family, style, placed.Text),
            (float)placed.Placement.FontSizePx);
        using SKPaint paint = new()
        {
            Color = SKColor.TryParse(fontDefinition.Color, out SKColor parsed) ? parsed : SKColors.Black,
            IsAntialias = true,
        };

        // Placement の Y は VerticalAlign の基準点。フォントメトリクスでベースラインへ変換する
        SKFontMetrics metrics = font.Metrics;
        float baseline = (float)placed.Placement.Y + placed.Definition.VerticalAlign switch
        {
            VerticalTextAlignment.Middle => -(metrics.Ascent + metrics.Descent) / 2f,
            VerticalTextAlignment.Bottom => -metrics.Descent,
            _ => -metrics.Ascent,
        };

        SKTextAlign align = placed.Definition.Align switch
        {
            Templates.TextAlignment.Center => SKTextAlign.Center,
            Templates.TextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left,
        };

        canvas.DrawText(placed.Text, (float)placed.Placement.X, baseline, align, font, paint);
    }

    // 指定ファミリーが持たないグリフ(日本語等)を含む場合は、そのグリフを持つフォントへ
    // フォールバックする(Skia の DrawText は自動フォールバックしないため)
    private static SKTypeface SelectTypeface(string family, SKFontStyle style, string text)
    {
        SKTypeface typeface = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
        using SKFont probe = new(typeface);

        foreach (char character in text)
        {
            if (probe.GetGlyph(character) == 0)
            {
                SKTypeface? fallback = SKFontManager.Default.MatchCharacter(
                    family, style, null, character);
                if (fallback is not null)
                {
                    return fallback;
                }
            }
        }

        return typeface;
    }
}
