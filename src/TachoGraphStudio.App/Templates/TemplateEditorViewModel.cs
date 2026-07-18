using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Templates;

// テンプレート編集オーバーレイの状態(FR-24)。ファイルピッカー・確認ダイアログは view 層が担う
public sealed partial class TemplateEditorViewModel : ObservableObject
{
    // 旧 GIMP 版の標準フィールドキー。#13 の名簿・日付反映はこのキーを参照する。
    // 既定位置は実運用テンプレート(Yazaki45 等)の傾向に合わせた初期値
    private static readonly (string Name, double XRatio, double YRatio)[] NewTemplateFieldSeeds =
    [
        ("date_year", 0.42, 0.38),
        ("date_month", 0.49, 0.38),
        ("date_day", 0.55, 0.38),
        ("driver", 0.49, 0.41),
        ("vehicle_no", 0.38, 0.45),
        ("vehicle_type", 0.57, 0.48),
    ];

    private readonly ITemplateStore _store;
    private readonly List<TemplateItemViewModel> _subscribedItems = [];

    // 全画面オーバーレイの表示状態。開閉は view 層(MainWindow / TemplateEditorOverlay)が操作する
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    public partial TemplateItemViewModel? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedField))]
    public partial TemplateFieldViewModel? SelectedField { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    // 壊れたテンプレートファイルの一覧。残りのテンプレートは利用できるため警告に留める
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadWarning))]
    public partial string? LoadWarningMessage { get; set; }

    public TemplateEditorViewModel(ITemplateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        Templates.CollectionChanged += OnTemplatesCollectionChanged;
    }

    public ObservableCollection<TemplateItemViewModel> Templates { get; } = [];

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    public bool HasSelectedField => SelectedField is not null;

    public bool HasError => ErrorMessage is not null;

    public bool HasLoadWarning => LoadWarningMessage is not null;

    public bool HasUnsavedChanges => Templates.Any(template => template.IsDirty);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        LoadWarningMessage = null;
        SelectedTemplate = null;
        Templates.Clear();

        try
        {
            TemplateStoreListResult result = await _store.ListAsync(cancellationToken);

            foreach (StoredTemplate stored in result.Templates)
            {
                Templates.Add(new TemplateItemViewModel(stored.Id, stored.Template));
            }

            if (result.Failures.Count > 0)
            {
                LoadWarningMessage = "読み込めなかったテンプレートがあります: "
                    + string.Join(" / ", result.Failures.Select(
                        failure => $"{failure.FileName}({failure.Message})"));
            }

            SelectedTemplate = Templates.FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"テンプレートの読み込みに失敗しました: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void CreateNew()
    {
        ChartTemplate template = new()
        {
            Name = GenerateUniqueName("新規テンプレート"),
            Fields = NewTemplateFieldSeeds.ToDictionary(
                seed => seed.Name,
                seed => new TextFieldDefinition
                {
                    Position = new TextPosition { XRatio = seed.XRatio, YRatio = seed.YRatio },
                }),
        };

        TemplateItemViewModel item = new(id: null, template) { IsDirty = true };
        Templates.Add(item);
        SelectedTemplate = item;
    }

    // 選択中テンプレートの現在の編集状態(未保存の変更を含む)を複製する
    public void DuplicateSelected()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        ChartTemplate template;
        try
        {
            template = SelectedTemplate.ToChartTemplate();
        }
        catch (TemplateFormatException exception)
        {
            ErrorMessage = exception.Message;
            return;
        }

        TemplateItemViewModel item = new(
            id: null,
            template with { Name = GenerateUniqueName($"{template.Name} のコピー") })
        {
            IsDirty = true,
        };
        Templates.Add(item);
        SelectedTemplate = item;
    }

    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        TemplateItemViewModel item = SelectedTemplate;
        if (item.Id is not null)
        {
            try
            {
                await _store.DeleteAsync(item.Id, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ErrorMessage = $"テンプレートの削除に失敗しました: {exception.Message}";
                return;
            }
        }

        int index = Templates.IndexOf(item);
        Templates.Remove(item);
        SelectedTemplate = Templates.Count > 0
            ? Templates[Math.Min(index, Templates.Count - 1)]
            : null;
    }

    public async Task<bool> SaveSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedTemplate is null)
        {
            return false;
        }

        TemplateItemViewModel item = SelectedTemplate;
        try
        {
            StoredTemplate stored = await _store.SaveAsync(
                item.Id,
                item.ToChartTemplate(),
                cancellationToken);
            item.MarkSaved(stored.Id);
            ErrorMessage = null;
            return true;
        }
        catch (TemplateFormatException exception)
        {
            ErrorMessage = exception.Message;
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"テンプレートの保存に失敗しました: {exception.Message}";
            return false;
        }
    }

    // 旧 GIMP 版テンプレート JSON の取り込み(フォーマット互換、FR-25)。取り込みと同時に保存する
    public async Task<bool> ImportAsync(
        string fileName,
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            ChartTemplate template = ChartTemplateSerializer.Deserialize(json);
            StoredTemplate stored = await _store.SaveAsync(id: null, template, cancellationToken);

            TemplateItemViewModel item = new(stored.Id, stored.Template);
            Templates.Add(item);
            SelectedTemplate = item;
            ErrorMessage = null;
            return true;
        }
        catch (TemplateFormatException exception)
        {
            ErrorMessage = $"{fileName} を取り込めません: {exception.Message}";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"{fileName} の取り込みに失敗しました: {exception.Message}";
            return false;
        }
    }

    public TemplateFieldViewModel? AddFieldToSelected(string name)
    {
        if (SelectedTemplate is null)
        {
            return null;
        }

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            ErrorMessage = "フィールド名を入力してください。";
            return null;
        }

        if (SelectedTemplate.Fields.Any(field => field.Name == trimmed))
        {
            ErrorMessage = $"フィールド名 '{trimmed}' は既に存在します。";
            return null;
        }

        ErrorMessage = null;
        TemplateFieldViewModel added = SelectedTemplate.AddField(trimmed);
        SelectedField = added;
        return added;
    }

    public void RemoveSelectedField()
    {
        if (SelectedTemplate is null || SelectedField is null)
        {
            return;
        }

        TemplateFieldViewModel removed = SelectedField;
        int index = SelectedTemplate.Fields.IndexOf(removed);
        SelectedTemplate.RemoveField(removed);
        SelectedField = SelectedTemplate.Fields.Count > 0
            ? SelectedTemplate.Fields[Math.Min(Math.Max(index, 0), SelectedTemplate.Fields.Count - 1)]
            : null;
    }

    partial void OnSelectedTemplateChanged(TemplateItemViewModel? value)
    {
        SelectedField = value?.Fields.FirstOrDefault();
    }

    // Reset(Clear) では OldItems が渡らないため、購読済みリストを基準に張り直す
    private void OnTemplatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (TemplateItemViewModel item in _subscribedItems)
        {
            item.PropertyChanged -= OnTemplateItemPropertyChanged;
        }

        _subscribedItems.Clear();

        foreach (TemplateItemViewModel item in Templates)
        {
            item.PropertyChanged += OnTemplateItemPropertyChanged;
            _subscribedItems.Add(item);
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnTemplateItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TemplateItemViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private string GenerateUniqueName(string baseName)
    {
        string candidate = baseName;
        for (int suffix = 2; Templates.Any(template => template.Name == candidate); suffix++)
        {
            candidate = $"{baseName} {suffix}";
        }

        return candidate;
    }
}
