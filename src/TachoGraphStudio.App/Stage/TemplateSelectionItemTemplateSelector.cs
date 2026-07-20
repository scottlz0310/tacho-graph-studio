using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Stage;

// トップバーのテンプレート選択 ComboBox 用(#43)。実テンプレートと末尾の
// 「テンプレート登録・編集」エントリ(TemplateEditEntry)を別テンプレートで描画し分ける
public sealed class TemplateSelectionItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TemplateItemTemplate { get; set; }

    public DataTemplate? EditEntryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is StoredTemplate ? TemplateItemTemplate : EditEntryTemplate;

    // ComboBox は container 付きの 2 引数版を呼ぶ。基底実装は null を返し
    // ToString() フォールバック描画になるため、こちらも override が必須(#59)
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
