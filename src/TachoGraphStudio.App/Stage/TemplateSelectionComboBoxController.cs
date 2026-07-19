using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

// トップバーのテンプレート選択 ComboBox(#43)の SelectedItem 同期を管理する。
// VM 駆動の変更(円盤切替・テンプレート再読込等)とユーザー操作を区別し、前者が
// SelectionChanged 経由でユーザー選択として円盤メタデータへ誤って書き戻されないようにする。
// WinUI 型(ComboBox・SelectionChangedEventArgs)に依存せず副作用をコールバックとして
// 注入するため、実際の ComboBox の「SelectedItem 代入で SelectionChanged が同期的に
// 発火する」挙動を模擬してユニットテストできる
public sealed class TemplateSelectionComboBoxController
{
    private readonly Func<Task> _openEditorAsync;
    private readonly Action<StoredTemplate?> _selectTemplateForSelectedDisc;
    private readonly Action<object?> _setComboBoxSelectedItem;

    private bool _isProgrammaticChange;

    public TemplateSelectionComboBoxController(
        Action<object?> setComboBoxSelectedItem,
        Action<StoredTemplate?> selectTemplateForSelectedDisc,
        Func<Task> openEditorAsync)
    {
        ArgumentNullException.ThrowIfNull(setComboBoxSelectedItem);
        ArgumentNullException.ThrowIfNull(selectTemplateForSelectedDisc);
        ArgumentNullException.ThrowIfNull(openEditorAsync);

        _setComboBoxSelectedItem = setComboBoxSelectedItem;
        _selectTemplateForSelectedDisc = selectTemplateForSelectedDisc;
        _openEditorAsync = openEditorAsync;
    }

    // VM → View。StageViewModel.SelectedTemplate の PropertyChanged で呼ぶ
    public void ApplyFromViewModel(StoredTemplate? selectedTemplate)
    {
        _isProgrammaticChange = true;
        try
        {
            _setComboBoxSelectedItem(selectedTemplate);
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    // View → VM。ComboBox.SelectionChanged で呼ぶ。ApplyFromViewModel によって誘発された
    // 呼び出しは無視する(円盤メタデータへの誤書き込みを防ぐ)
    public async Task OnSelectionChangedAsync(object? selectedItem, StoredTemplate? currentSelectedTemplate)
    {
        if (_isProgrammaticChange)
        {
            return;
        }

        if (selectedItem is TemplateEditEntry)
        {
            // 実テンプレートとしては選択させず、表示を直前の値へ戻してから編集を開く
            ApplyFromViewModel(currentSelectedTemplate);
            await _openEditorAsync();
            return;
        }

        _selectTemplateForSelectedDisc(selectedItem as StoredTemplate);
    }
}
