using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TachoGraphStudio.App.Stage;

// 円盤プレビュー(FR-06〜08): 十字ガイド・非破壊回転・ズーム。
// インライン表示と全画面オーバーレイの両方から使う
public sealed partial class DiscPreviewControl : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(DiscPreviewControl),
        new PropertyMetadata(null));

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
