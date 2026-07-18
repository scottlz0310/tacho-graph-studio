namespace TachoGraphStudio.Core.Templates;

// チャート紙様式テンプレート(FR-16, FR-24〜25)。
// GIMP 版 TachoGraphWizard の JSON フォーマットを継承しており(2026-07-19 決定、issue #11)、
// 各プロパティの既定値も旧実装(templates/models.py)の欠落キー時の値と一致させている
public sealed record ChartTemplate
{
    public string Name { get; init; } = "Untitled";

    public string Version { get; init; } = "1.0";

    public string Description { get; init; } = "";

    // GUI 編集時の参考情報。座標計算は比率のみを使うため実画像サイズと一致する必要はない
    public int ReferenceWidth { get; init; } = 1000;

    public int ReferenceHeight { get; init; } = 1000;

    // キーはフィールド名(date_year, vehicle_no, driver 等)。旧フォーマットと同じ自由キー辞書
    public Dictionary<string, TextFieldDefinition> Fields { get; init; } = [];
}
