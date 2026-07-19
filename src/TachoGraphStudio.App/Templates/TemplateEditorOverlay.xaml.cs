using System.Collections.Specialized;
using System.ComponentModel;

using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

using WinRT.Interop;

using CoreTextAlignment = TachoGraphStudio.Core.Templates.TextAlignment;
using VerticalTextAlignment = TachoGraphStudio.Core.Templates.VerticalTextAlignment;

namespace TachoGraphStudio.App.Templates;

// テンプレート編集の全画面オーバーレイ(FR-24)。
// プレビューはフィールド名ラベルを比率座標に描画し、ドラッグで位置を調整できる
public sealed partial class TemplateEditorOverlay : UserControl
{
    public static readonly DependencyProperty PreviewBackgroundProperty = DependencyProperty.Register(
        nameof(PreviewBackground),
        typeof(ImageSource),
        typeof(TemplateEditorOverlay),
        new PropertyMetadata(null));

    private readonly Dictionary<TemplateFieldViewModel, Border> _markers = [];
    private TemplateEditorViewModel? _viewModel;
    private TemplateItemViewModel? _attachedTemplate;

    public TemplateEditorOverlay()
    {
        InitializeComponent();
    }

    // プレビュー背景(ステージで選択中の円盤)。未設定時はプレースホルダー円のみ表示する
    public ImageSource? PreviewBackground
    {
        get => (ImageSource?)GetValue(PreviewBackgroundProperty);
        set => SetValue(PreviewBackgroundProperty, value);
    }

    // FileOpenPicker の初期化に使うホストウィンドウ。表示前に MainWindow が設定する
    public Window? HostWindow { get; set; }

    // 表示前に MainWindow が 1 回だけ設定する(x:Bind のルートは差し替えない前提)
    public TemplateEditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value;

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            AttachTemplate(_viewModel?.SelectedTemplate);
        }
    }

    private async void OnSaveButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.SaveSelectedAsync();
        }
    }

    private async void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.HasUnsavedChanges)
        {
            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = "未保存の変更があります",
                Content = "保存されていない変更は破棄されます。閉じますか？",
                PrimaryButtonText = "破棄して閉じる",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        ViewModel.IsOpen = false;
    }

    private void OnCreateTemplateButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateNew();
    }

    private void OnDuplicateTemplateButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.DuplicateSelected();
    }

    private async void OnDeleteTemplateButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedTemplate is not { } template)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "テンプレートの削除",
            Content = $"テンプレート「{template.Name}」を削除しますか？この操作は取り消せません。",
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSelectedAsync();
        }
    }

    private async void OnImportButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(HostWindow));

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        string json;
        try
        {
            json = await FileIO.ReadTextAsync(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ViewModel.ErrorMessage = $"{file.Name} を読み込めません: {exception.Message}";
            return;
        }

        await ViewModel.ImportAsync(file.Name, json);
    }

    private void OnAddFieldButtonClick(object sender, RoutedEventArgs e)
    {
        AddFieldFromTextBox();
    }

    private void OnNewFieldNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            AddFieldFromTextBox();
        }
    }

    private void OnRemoveFieldButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.RemoveSelectedField();
    }

    private void AddFieldFromTextBox()
    {
        if (ViewModel?.AddFieldToSelected(NewFieldNameTextBox.Text) is not null)
        {
            NewFieldNameTextBox.Text = "";
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TemplateEditorViewModel.SelectedTemplate))
        {
            AttachTemplate(ViewModel?.SelectedTemplate);
        }
        else if (e.PropertyName == nameof(TemplateEditorViewModel.SelectedField))
        {
            UpdateMarkerSelection();
        }
    }

    // ---- プレビューキャンバス ----

    private void AttachTemplate(TemplateItemViewModel? template)
    {
        if (_attachedTemplate is not null)
        {
            _attachedTemplate.PropertyChanged -= OnTemplatePropertyChanged;
            _attachedTemplate.Fields.CollectionChanged -= OnFieldsCollectionChanged;
        }

        _attachedTemplate = template;

        if (_attachedTemplate is not null)
        {
            _attachedTemplate.PropertyChanged += OnTemplatePropertyChanged;
            _attachedTemplate.Fields.CollectionChanged += OnFieldsCollectionChanged;
        }

        UpdateSurfaceSize();
        RebuildMarkers();
    }

    private void OnTemplatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TemplateItemViewModel.ReferenceWidth)
            or nameof(TemplateItemViewModel.ReferenceHeight))
        {
            UpdateSurfaceSize();
            LayoutAllMarkers();
        }
    }

    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildMarkers();
    }

    private void OnPreviewViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSurfaceSize();
        LayoutAllMarkers();
    }

    // テンプレートの参考サイズのアスペクト比を保ってビューポートに収める
    private void UpdateSurfaceSize()
    {
        double viewportWidth = PreviewViewport.ActualWidth;
        double viewportHeight = PreviewViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        double aspect = _attachedTemplate is { } template
            ? (double)template.ReferenceWidth / template.ReferenceHeight
            : 1.0;

        double width = Math.Min(viewportWidth, viewportHeight * aspect);
        PreviewSurface.Width = width;
        PreviewSurface.Height = width / aspect;
    }

    private void RebuildMarkers()
    {
        foreach ((TemplateFieldViewModel field, Border marker) in _markers)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
            marker.Tapped -= OnMarkerTapped;
            marker.ManipulationStarted -= OnMarkerManipulationStarted;
            marker.ManipulationDelta -= OnMarkerManipulationDelta;
        }

        _markers.Clear();
        FieldCanvas.Children.Clear();

        if (_attachedTemplate is null)
        {
            return;
        }

        foreach (TemplateFieldViewModel field in _attachedTemplate.Fields)
        {
            Border marker = new()
            {
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                // 透明背景でもヒットテストが効くようにする
                Background = new SolidColorBrush(Colors.Transparent),
                ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY,
                Child = new TextBlock(),
                Tag = field,
            };
            marker.Tapped += OnMarkerTapped;
            marker.ManipulationStarted += OnMarkerManipulationStarted;
            marker.ManipulationDelta += OnMarkerManipulationDelta;
            field.PropertyChanged += OnFieldPropertyChanged;

            _markers[field] = marker;
            FieldCanvas.Children.Add(marker);
            RestyleMarker(field, marker);
        }

        UpdateMarkerSelection();
        LayoutAllMarkers();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TemplateFieldViewModel field || !_markers.TryGetValue(field, out Border? marker))
        {
            return;
        }

        if (e.PropertyName is nameof(TemplateFieldViewModel.XRatio) or nameof(TemplateFieldViewModel.YRatio))
        {
            LayoutMarker(field, marker);
        }
        else
        {
            // 名前・フォント・整列などはラベルの見た目とサイズに影響するため再スタイル + 再配置
            RestyleMarker(field, marker);
            LayoutMarker(field, marker);
        }
    }

    private void RestyleMarker(TemplateFieldViewModel field, Border marker)
    {
        double shortSide = Math.Min(PreviewSurface.Width, PreviewSurface.Height);
        TextBlock label = (TextBlock)marker.Child;

        label.Text = field.Name;
        label.FontSize = double.IsFinite(shortSide) && shortSide > 0
            ? Math.Max(8.0, field.SizeRatio * shortSide)
            : 12.0;
        label.FontWeight = field.Bold ? FontWeights.Bold : FontWeights.Normal;
        label.FontStyle = field.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        if (!string.IsNullOrWhiteSpace(field.FontFamily))
        {
            label.FontFamily = new FontFamily(field.FontFamily);
        }

        if (HexColor.TryParse(field.Color) is { } color)
        {
            label.Foreground = new SolidColorBrush(color);
        }

        marker.Opacity = field.Visible ? 1.0 : 0.35;
    }

    private void UpdateMarkerSelection()
    {
        foreach ((TemplateFieldViewModel field, Border marker) in _markers)
        {
            marker.BorderBrush = ReferenceEquals(field, ViewModel?.SelectedField)
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private void LayoutAllMarkers()
    {
        foreach ((TemplateFieldViewModel field, Border marker) in _markers)
        {
            RestyleMarker(field, marker);
            LayoutMarker(field, marker);
        }
    }

    // 位置(比率)は Align / VerticalAlign の基準点。ラベルの配置オフセットで表現する
    private void LayoutMarker(TemplateFieldViewModel field, Border marker)
    {
        double width = PreviewSurface.Width;
        double height = PreviewSurface.Height;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
        {
            return;
        }

        marker.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double markerWidth = marker.DesiredSize.Width;
        double markerHeight = marker.DesiredSize.Height;

        double x = field.XRatio * width - field.Align switch
        {
            CoreTextAlignment.Center => markerWidth / 2,
            CoreTextAlignment.Right => markerWidth,
            _ => 0,
        };
        double y = field.YRatio * height - field.VerticalAlign switch
        {
            VerticalTextAlignment.Middle => markerHeight / 2,
            VerticalTextAlignment.Bottom => markerHeight,
            _ => 0,
        };

        Canvas.SetLeft(marker, x);
        Canvas.SetTop(marker, y);
    }

    private void OnMarkerTapped(object sender, TappedRoutedEventArgs e)
    {
        SelectFieldOfMarker(sender);
    }

    private void OnMarkerManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        SelectFieldOfMarker(sender);
    }

    private void OnMarkerManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not Border { Tag: TemplateFieldViewModel field })
        {
            return;
        }

        double width = PreviewSurface.Width;
        double height = PreviewSurface.Height;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
        {
            return;
        }

        // 比率座標へ変換して VM 側でクランプさせる(0〜1 の外へは出ない)
        field.XRatio += e.Delta.Translation.X / width;
        field.YRatio += e.Delta.Translation.Y / height;
    }

    private void SelectFieldOfMarker(object sender)
    {
        if (ViewModel is not null && sender is Border { Tag: TemplateFieldViewModel field })
        {
            ViewModel.SelectedField = field;
        }
    }

}
