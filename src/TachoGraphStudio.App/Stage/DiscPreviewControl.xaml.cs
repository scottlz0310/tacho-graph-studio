using System.ComponentModel;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using TachoGraphStudio.App.Templates;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

// 円盤プレビュー(FR-06〜08, FR-18): 十字ガイド・非破壊回転・ズーム・文字入れレイヤー。
// インライン表示と全画面オーバーレイの両方から使う
public sealed partial class DiscPreviewControl : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(DiscPreviewControl),
        new PropertyMetadata(null, OnTextOverlayInputChanged));

    public static readonly DependencyProperty TextTemplateProperty = DependencyProperty.Register(
        nameof(TextTemplate),
        typeof(ChartTemplate),
        typeof(DiscPreviewControl),
        new PropertyMetadata(null, OnTextOverlayInputChanged));

    public static readonly DependencyProperty MetadataProperty = DependencyProperty.Register(
        nameof(Metadata),
        typeof(DiscMetadata),
        typeof(DiscPreviewControl),
        new PropertyMetadata(null, OnMetadataChanged));

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle),
        typeof(double),
        typeof(DiscPreviewControl),
        new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsFullscreenButtonVisibleProperty = DependencyProperty.Register(
        nameof(IsFullscreenButtonVisible),
        typeof(bool),
        typeof(DiscPreviewControl),
        new PropertyMetadata(true));

    private const double ZoomStep = 1.25;

    public DiscPreviewControl()
    {
        InitializeComponent();
    }

    public event EventHandler? FullscreenRequested;

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public bool IsFullscreenButtonVisible
    {
        get => (bool)GetValue(IsFullscreenButtonVisibleProperty);
        set => SetValue(IsFullscreenButtonVisibleProperty, value);
    }

    // 文字入れ位置を定義するチャート紙様式(FR-16, FR-18)
    public ChartTemplate? TextTemplate
    {
        get => (ChartTemplate?)GetValue(TextTemplateProperty);
        set => SetValue(TextTemplateProperty, value);
    }

    // 選択中円盤のメタデータ。プロパティ変更を購読しリアルタイムに再描画する(FR-18)
    public DiscMetadata? Metadata
    {
        get => (DiscMetadata?)GetValue(MetadataProperty);
        set => SetValue(MetadataProperty, value);
    }

    private static void OnTextOverlayInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DiscPreviewControl)d).RenderTextOverlay();
    }

    private static void OnMetadataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DiscPreviewControl control = (DiscPreviewControl)d;

        if (e.OldValue is DiscMetadata oldMetadata)
        {
            oldMetadata.PropertyChanged -= control.OnMetadataPropertyChanged;
        }

        if (e.NewValue is DiscMetadata newMetadata)
        {
            newMetadata.PropertyChanged += control.OnMetadataPropertyChanged;
        }

        control.RenderTextOverlay();
    }

    private void OnMetadataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RenderTextOverlay();
    }

    private void OnDiscImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderTextOverlay();
    }

    // 文字レイヤーの再描画。キャンバスは表示中の画像実寸(レターボックス除外)に合わせて中央配置し、
    // 画像の回転とは独立、ズーム・スクロールには追従する
    private void RenderTextOverlay()
    {
        TextOverlayCanvas.Children.Clear();

        if (Source is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap
            || DiscImage.ActualWidth < 1
            || DiscImage.ActualHeight < 1)
        {
            return;
        }

        double scale = Math.Min(
            DiscImage.ActualWidth / bitmap.PixelWidth,
            DiscImage.ActualHeight / bitmap.PixelHeight);
        double imageWidth = bitmap.PixelWidth * scale;
        double imageHeight = bitmap.PixelHeight * scale;
        TextOverlayCanvas.Width = imageWidth;
        TextOverlayCanvas.Height = imageHeight;

        // 手書きスキップ時は文字入れを行わない(FR-17)
        if (TextTemplate is not { } template
            || Metadata is not { } metadata
            || metadata.SkipHandwritten
            || imageWidth < 1
            || imageHeight < 1)
        {
            return;
        }

        foreach (PlacedText placed in ChartTextComposer.Compose(
            template, metadata.ToTextValues(), (int)imageWidth, (int)imageHeight))
        {
            TextBlock text = new()
            {
                Text = placed.Text,
                FontSize = Math.Max(1.0, placed.Placement.FontSizePx),
                FontWeight = placed.Definition.Font.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = placed.Definition.Font.Italic
                    ? Windows.UI.Text.FontStyle.Italic
                    : Windows.UI.Text.FontStyle.Normal,
            };
            if (!string.IsNullOrWhiteSpace(placed.Definition.Font.Family))
            {
                text.FontFamily = new FontFamily(placed.Definition.Font.Family);
            }

            if (HexColor.TryParse(placed.Definition.Font.Color) is { } color)
            {
                text.Foreground = new SolidColorBrush(color);
            }

            text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

            // Placement の X/Y は Align / VerticalAlign の基準点
            double x = placed.Placement.X - placed.Definition.Align switch
            {
                Core.Templates.TextAlignment.Center => text.DesiredSize.Width / 2,
                Core.Templates.TextAlignment.Right => text.DesiredSize.Width,
                _ => 0,
            };
            double y = placed.Placement.Y - placed.Definition.VerticalAlign switch
            {
                VerticalTextAlignment.Middle => text.DesiredSize.Height / 2,
                VerticalTextAlignment.Bottom => text.DesiredSize.Height,
                _ => 0,
            };

            Canvas.SetLeft(text, x);
            Canvas.SetTop(text, y);
            TextOverlayCanvas.Children.Add(text);
        }
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ContentHost.Width = e.NewSize.Width;
        ContentHost.Height = e.NewSize.Height;
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        ChangeZoom(ZoomStep);
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        ChangeZoom(1.0 / ZoomStep);
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ChangeZoom(double factor)
    {
        float zoom = Math.Clamp(
            Scroller.ZoomFactor * (float)factor,
            Scroller.MinZoomFactor,
            Scroller.MaxZoomFactor);

        // ビューポート中心を保ってズームする
        double centerX = (Scroller.HorizontalOffset + Scroller.ViewportWidth / 2) / Scroller.ZoomFactor;
        double centerY = (Scroller.VerticalOffset + Scroller.ViewportHeight / 2) / Scroller.ZoomFactor;
        Scroller.ChangeView(
            centerX * zoom - Scroller.ViewportWidth / 2,
            centerY * zoom - Scroller.ViewportHeight / 2,
            zoom);
    }
}
