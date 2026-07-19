namespace TachoGraphStudio.Core.Templates;

// テンプレートのフィールド名と入力値の対応付け + 配置計算(FR-16, FR-18)。
// プレビューの文字レイヤー(#13)と確定保存の本合成(#14)が共有する
public static class ChartTextComposer
{
    // 表示対象(visible かつ値が空でない)のフィールドをキー順に返す
    public static IReadOnlyList<PlacedText> Compose(
        ChartTemplate template,
        ChartTextValues values,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        List<PlacedText> result = [];
        foreach ((string name, TextFieldDefinition field) in template.Fields
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!field.Visible)
            {
                continue;
            }

            string? text = ChartTextValueResolver.Resolve(name, values);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            result.Add(new PlacedText(name, text, field.CalculatePlacement(imageWidth, imageHeight), field));
        }

        return result;
    }

}

// 1 フィールド分の描画内容。Placement の X/Y は Definition.Align / VerticalAlign の基準点
public sealed record PlacedText(
    string FieldName,
    string Text,
    TextPlacement Placement,
    TextFieldDefinition Definition);
