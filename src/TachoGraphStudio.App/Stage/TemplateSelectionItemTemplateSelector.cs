using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

// トップバーのテンプレート選択 ComboBox 用(#43)。実テンプレートと末尾の
// 「テンプレートを編集...」エントリ(TemplateEditEntry)を別テンプレートで描画し分ける
public sealed class TemplateSelectionItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TemplateItemTemplate { get; set; }

    public DataTemplate? EditEntryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is StoredTemplate ? TemplateItemTemplate : EditEntryTemplate;
}
