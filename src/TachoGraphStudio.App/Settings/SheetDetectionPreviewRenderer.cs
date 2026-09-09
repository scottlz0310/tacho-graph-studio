using System.Runtime.InteropServices;

using OpenCvSharp;

using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Imaging;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

// 直近に取り込んだシートを縮小し、現在の設定での検出結果(切り出し範囲・アルファ円・中心)を
// 重ねて描く(#126)。検出は本処理と同じ SheetSplitter / ImageProcessingOptionsResolver を通す
public sealed class SheetDetectionPreviewRenderer : IDetectionPreviewRenderer
{
    public const int DefaultMaxLongSide = 640;

    // BGRA。切り出し範囲・アルファ円・中心をそれぞれ区別できる色にする
    private static readonly Scalar CropRegionColor = new(0, 138, 255, 255);
    private static readonly Scalar AlphaCircleColor = new(83, 200, 0, 255);
    private static readonly Scalar CenterColor = new(60, 60, 230, 255);

    private readonly SheetLoader _sheetLoader;
    private readonly SheetSplitter _splitter = new();
    private readonly ImageProcessingOptionsResolver _optionsResolver;
    private readonly string _sourcePath;
    private readonly int _maxLongSide;
    private SheetImage? _sheet;
    private ScaledSheet? _scaledSheet;

    public SheetDetectionPreviewRenderer(
        SheetLoader sheetLoader,
        ImageProcessingOptionsResolver optionsResolver,
        string sourcePath,
        int maxLongSide = DefaultMaxLongSide)
    {
        ArgumentNullException.ThrowIfNull(sheetLoader);
        ArgumentNullException.ThrowIfNull(optionsResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLongSide);

        _sheetLoader = sheetLoader;
        _optionsResolver = optionsResolver;
        _sourcePath = sourcePath;
        _maxLongSide = maxLongSide;
    }

    public async Task<DetectionPreviewImage> RenderAsync(
        ImageProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 読み込み(PDF はラスタライズ)は設定に依存しないため 1 度だけ行い、
        // 以降の設定変更では検出と描画だけをやり直す
        SheetImage sheet = await LoadSheetAsync(cancellationToken);
        return await Task.Run(() => Render(sheet, settings), cancellationToken);
    }

    private async Task<SheetImage> LoadSheetAsync(CancellationToken cancellationToken)
    {
        if (_sheet is { } cached)
        {
            return cached;
        }

        await foreach (SheetImage sheet in _sheetLoader.LoadAsync([_sourcePath], cancellationToken))
        {
            // プレビューは先頭シートの先頭ページのみを対象にする
            _sheet = sheet;
            return sheet;
        }

        throw new SheetLoadException($"プレビュー対象のシートを読み込めません: {_sourcePath}");
    }

    private DetectionPreviewImage Render(SheetImage sheet, ImageProcessingSettings settings)
    {
        DiscSplitOptions splitOptions = _optionsResolver.ResolveSplitOptions(sheet.SourcePath, settings);
        BackgroundRemovalOptions removalOptions = _optionsResolver.ResolveRemovalOptions(settings);
        IReadOnlyList<DiscDetection> detections = _splitter.Detect(sheet, splitOptions);
        ScaledSheet scaled = EnsureScaledSheet(sheet);

        using Mat canvas = new();
        using (Mat decoded = Cv2.ImDecode(scaled.EncodedBgr, ImreadModes.Color))
        {
            Cv2.CvtColor(decoded, canvas, ColorConversionCodes.BGR2BGRA);
        }

        int thickness = Math.Max(1, (int)Math.Round(Math.Max(canvas.Width, canvas.Height) / 320.0));
        foreach (DiscDetection detection in detections)
        {
            DrawDetection(canvas, detection, removalOptions, scaled.Scale, thickness);
        }

        return new DetectionPreviewImage(canvas.Width, canvas.Height, ToBytes(canvas), detections.Count);
    }

    private static void DrawDetection(
        Mat canvas,
        DiscDetection detection,
        BackgroundRemovalOptions removalOptions,
        double scale,
        int thickness)
    {
        Rect region = detection.CropRegionInSheet;
        Cv2.Rectangle(
            canvas,
            new Rect(
                (int)Math.Round(region.X * scale),
                (int)Math.Round(region.Y * scale),
                Math.Max(1, (int)Math.Round(region.Width * scale)),
                Math.Max(1, (int)Math.Round(region.Height * scale))),
            CropRegionColor,
            thickness);

        Point center = new(
            (int)Math.Round(detection.CenterInSheet.X * scale),
            (int)Math.Round(detection.CenterInSheet.Y * scale));

        RotatedRect alphaCircle;
        try
        {
            alphaCircle = DiscAlphaCircle.Resolve(
                detection.CenterInCrop,
                detection.Diameter,
                region.Size,
                removalOptions);
        }
        catch (ArgumentException exception)
        {
            throw new BackgroundRemovalException(
                $"背景除去の設定が円盤に適用できません: {exception.Message}",
                exception);
        }

        Cv2.Circle(
            canvas,
            center,
            Math.Max(1, (int)Math.Round(alphaCircle.Size.Width / 2 * scale)),
            AlphaCircleColor,
            thickness,
            LineTypes.AntiAlias);

        int armLength = Math.Max(3, thickness * 4);
        Cv2.Line(
            canvas,
            new Point(center.X - armLength, center.Y),
            new Point(center.X + armLength, center.Y),
            CenterColor,
            thickness);
        Cv2.Line(
            canvas,
            new Point(center.X, center.Y - armLength),
            new Point(center.X, center.Y + armLength),
            CenterColor,
            thickness);
    }

    // 縮小結果は設定に依存しないためキャッシュする。Mat を保持すると破棄の責務が
    // ViewModel まで波及するので、再デコードの安いエンコード済みバイト列で持つ
    private ScaledSheet EnsureScaledSheet(SheetImage sheet)
    {
        if (_scaledSheet is { } cached)
        {
            return cached;
        }

        using Mat pixels = Cv2.ImDecode(sheet.ImageBytes, ImreadModes.Color);
        if (pixels.Empty())
        {
            throw new DiscSplitException($"シート画像をデコードできません: {sheet.SourcePath}");
        }

        double scale = Math.Min(1.0, (double)_maxLongSide / Math.Max(pixels.Width, pixels.Height));
        using Mat resized = new();
        Cv2.Resize(
            pixels,
            resized,
            new Size(
                Math.Max(1, (int)Math.Round(pixels.Width * scale)),
                Math.Max(1, (int)Math.Round(pixels.Height * scale))),
            0,
            0,
            InterpolationFlags.Area);
        Cv2.ImEncode(".png", resized, out byte[] encoded);

        ScaledSheet result = new(encoded, scale);
        _scaledSheet = result;
        return result;
    }

    private static byte[] ToBytes(Mat mat)
    {
        byte[] data = new byte[(int)(mat.Total() * mat.ElemSize())];
        Marshal.Copy(mat.Data, data, 0, data.Length);
        return data;
    }

    private sealed record ScaledSheet(byte[] EncodedBgr, double Scale);
}
