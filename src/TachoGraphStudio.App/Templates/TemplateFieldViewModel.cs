using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Templates;

// テンプレートの 1 フィールド分の編集状態(FR-24)
public sealed partial class TemplateFieldViewModel : ObservableObject
{
    private readonly Action _onEdited;
    private readonly bool _suppressEditNotifications = true;

    private double _xRatio;
    private double _yRatio;
    private double _sizeRatio;

    public TemplateFieldViewModel(string name, TextFieldDefinition definition, Action onEdited)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(onEdited);

        _onEdited = onEdited;

        Name = name;
        _xRatio = definition.Position.XRatio;
        _yRatio = definition.Position.YRatio;
        _sizeRatio = definition.Font.SizeRatio;
        FontFamily = definition.Font.Family;
        Color = definition.Font.Color;
        Bold = definition.Font.Bold;
        Italic = definition.Font.Italic;
        Align = definition.Align;
        VerticalAlign = definition.VerticalAlign;
        Visible = definition.Visible;
        Required = definition.Required;

        _suppressEditNotifications = false;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string FontFamily { get; set; }

    // #RRGGBB 形式。形式チェックは保存時に ChartTemplateSerializer が行う
    [ObservableProperty]
    public partial string Color { get; set; }

    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial TextAlignment Align { get; set; }

    [ObservableProperty]
    public partial VerticalTextAlignment VerticalAlign { get; set; }

    [ObservableProperty]
    public partial bool Visible { get; set; }

    [ObservableProperty]
    public partial bool Required { get; set; }

    // 位置は 0〜1 の比率。範囲外はドラッグで端に張り付くよう 0〜1 にクランプする。
    // NumberBox は空入力で NaN を書き込むため、非有限値は直前の有効値を保持して
    // 変更通知だけ発行する(DiscWorkItem.RotationAngle と同じ)
    public double XRatio
    {
        get => _xRatio;
        set => SetRatio(ref _xRatio, value, clampMinimum: 0.0);
    }

    public double YRatio
    {
        get => _yRatio;
        set => SetRatio(ref _yRatio, value, clampMinimum: 0.0);
    }

    // フォントサイズは画像短辺に対する比率(0 より大きく 1 以下)。0 以下は有効な下限が
    // 定義できないため、非有限値と同様に直前の有効値へ巻き戻す
    public double SizeRatio
    {
        get => _sizeRatio;
        set => SetRatio(ref _sizeRatio, value, clampMinimum: null);
    }

    public TextFieldDefinition ToDefinition() => new()
    {
        Position = new TextPosition { XRatio = XRatio, YRatio = YRatio },
        Font = new TextFont
        {
            Family = FontFamily,
            SizeRatio = SizeRatio,
            Color = Color,
            Bold = Bold,
            Italic = Italic,
        },
        Align = Align,
        VerticalAlign = VerticalAlign,
        Visible = Visible,
        Required = Required,
    };

    private void SetRatio(
        ref double backingField,
        double value,
        double? clampMinimum,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!double.IsFinite(value) || (clampMinimum is null && value <= 0.0))
        {
            OnPropertyChanged(propertyName);
            return;
        }

        double sanitized = Math.Min(Math.Max(value, clampMinimum ?? value), 1.0);
        if (SetProperty(ref backingField, sanitized, propertyName))
        {
            MarkEdited();
        }
    }

    private void MarkEdited()
    {
        if (!_suppressEditNotifications)
        {
            _onEdited();
        }
    }

    partial void OnNameChanged(string value) => MarkEdited();

    partial void OnFontFamilyChanged(string value) => MarkEdited();

    partial void OnColorChanged(string value) => MarkEdited();

    partial void OnBoldChanged(bool value) => MarkEdited();

    partial void OnItalicChanged(bool value) => MarkEdited();

    partial void OnAlignChanged(TextAlignment value) => MarkEdited();

    partial void OnVerticalAlignChanged(VerticalTextAlignment value) => MarkEdited();

    partial void OnVisibleChanged(bool value) => MarkEdited();

    partial void OnRequiredChanged(bool value) => MarkEdited();
}
