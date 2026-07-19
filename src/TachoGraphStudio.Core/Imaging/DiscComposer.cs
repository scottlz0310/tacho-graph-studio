using System.Runtime.InteropServices;

using SkiaSharp;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Imaging;

// 回転補正と文字入れをフル解像度で本合成し、アルファ付き PNG を生成する(FR-19)。
// 回転は premultiplied で補間されるよう SkiaSharp のキャンバス変換で行い、
// 文字はプレビュー(FR-18)と同じく回転と独立に ChartTextComposer の配置で描画する
public static class DiscComposer
{
    public static byte[] ComposePng(
        byte[] bgra,
        int width,
        int height,
        double rotationAngleDegrees,
        ChartTemplate? template,
        ChartTextValues? values)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"BGRA バッファ長がサイズと一致しません: {bgra.Length} != {width}x{height}x4",
                nameof(bgra));
        }

        SKImageInfo sourceInfo = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKBitmap source = new(sourceInfo);
        Marshal.Copy(bgra, 0, source.GetPixels(), bgra.Length);

        using SKSurface surface = SKSurface.Create(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

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
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        canvas.Restore();

        if (template is not null && values is not null)
        {
            foreach (PlacedText placed in ChartTextComposer.Compose(template, values, width, height))
            {
                DrawText(canvas, placed);
            }
        }

        using SKImage composed = surface.Snapshot();
        using SKData png = composed.Encode(SKEncodedImageFormat.Png, quality: 100);
        return png.ToArray();
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
