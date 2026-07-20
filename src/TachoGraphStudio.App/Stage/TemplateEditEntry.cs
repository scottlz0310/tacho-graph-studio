namespace TachoGraphStudio.App.Stage;

// トップバーのテンプレート選択 ComboBox の末尾に表示する「テンプレート登録・編集」エントリの
// マーカー(#43)。実テンプレート(StoredTemplate)と区別するためだけの型で、値を持たない
public sealed class TemplateEditEntry
{
    public static readonly TemplateEditEntry Instance = new();

    private TemplateEditEntry()
    {
    }
}
