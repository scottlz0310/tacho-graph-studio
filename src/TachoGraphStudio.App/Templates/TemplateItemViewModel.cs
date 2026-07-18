using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Templates;

// テンプレート 1 件分の編集状態(FR-24)。保存されるまで Id は null
public sealed partial class TemplateItemViewModel : ObservableObject
{
    private readonly bool _suppressEditNotifications = true;

    private int _referenceWidth;
    private int _referenceHeight;

    public TemplateItemViewModel(string? id, ChartTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        Id = id;

        Name = template.Name;
        Description = template.Description;
        Version = template.Version;
        _referenceWidth = template.ReferenceWidth;
        _referenceHeight = template.ReferenceHeight;

        Fields = [.. template.Fields
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new TemplateFieldViewModel(pair.Key, pair.Value, MarkDirty))];

        _suppressEditNotifications = false;
    }

    public string? Id { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    // 旧フォーマット互換のため保持するのみで、編集 UI には出さない
    public string Version { get; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public ObservableCollection<TemplateFieldViewModel> Fields { get; }

    // GUI 編集時の参考情報(座標計算は比率のみを使う)。1 未満は直前の有効値を保持する
    public int ReferenceWidth
    {
        get => _referenceWidth;
        set => SetReferenceLength(ref _referenceWidth, value);
    }

    public int ReferenceHeight
    {
        get => _referenceHeight;
        set => SetReferenceLength(ref _referenceHeight, value);
    }

    public TemplateFieldViewModel AddField(string name)
    {
        TemplateFieldViewModel field = new(name, new TextFieldDefinition(), MarkDirty);
        Fields.Add(field);
        MarkDirty();
        return field;
    }

    public void RemoveField(TemplateFieldViewModel field)
    {
        if (Fields.Remove(field))
        {
            MarkDirty();
        }
    }

    // フィールド名は辞書キーになるため、リネーム後の空白・重複はここ(保存境界)で検出する
    public ChartTemplate ToChartTemplate()
    {
        Dictionary<string, TextFieldDefinition> fields = [];
        foreach (TemplateFieldViewModel field in Fields)
        {
            string name = (field.Name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new TemplateFormatException("名前が空のフィールドがあります。フィールド名を入力してください。");
            }

            if (!fields.TryAdd(name, field.ToDefinition()))
            {
                throw new TemplateFormatException($"フィールド名 '{name}' が重複しています。");
            }
        }

        return new ChartTemplate
        {
            Name = Name,
            Version = Version,
            Description = Description,
            ReferenceWidth = ReferenceWidth,
            ReferenceHeight = ReferenceHeight,
            Fields = fields,
        };
    }

    public void MarkSaved(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        IsDirty = false;
    }

    private void SetReferenceLength(
        ref int backingField,
        int value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (value < 1)
        {
            OnPropertyChanged(propertyName);
            return;
        }

        if (SetProperty(ref backingField, value, propertyName))
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_suppressEditNotifications)
        {
            IsDirty = true;
        }
    }

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnDescriptionChanged(string value) => MarkDirty();
}
