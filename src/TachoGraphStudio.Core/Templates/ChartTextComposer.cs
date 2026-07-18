namespace TachoGraphStudio.Core.Templates;

// テンプレートのフィールド名と入力値の対応付け + 配置計算(FR-16, FR-18)。
// プレビューの文字レイヤー(#13)と確定保存の本合成(#14)が共有する
public static class ChartTextComposer
{
    private static readonly char[] DateSeparators = ['/', '-', '.'];

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

            string? text = ResolveText(name, values);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            result.Add(new PlacedText(name, text, field.CalculatePlacement(imageWidth, imageHeight), field));
        }

        return result;
    }

    // 旧 GIMP 版の標準フィールドキーに対応する値を返す。未知のキーは null(描画しない)
    private static string? ResolveText(string fieldName, ChartTextValues values) => fieldName switch
    {
        "date_year" => DatePart(values.DateText, 0),
        "date_month" => DatePart(values.DateText, 1),
        "date_day" => DatePart(values.DateText, 2),
        "vehicle_no" => values.RegistrationNumber,
        "driver" => values.Driver,
        "vehicle_type" => values.VehicleType,
        _ => null,
    };

    // 空要素を詰めると「2026//25」の日が月へずれるため、位置を保ったまま分解し空要素は null にする
    private static string? DatePart(string dateText, int index)
    {
        string[] parts = dateText.Split(DateSeparators, StringSplitOptions.TrimEntries);
        return index < parts.Length && parts[index].Length > 0 ? parts[index] : null;
    }
}

// 1 フィールド分の描画内容。Placement の X/Y は Definition.Align / VerticalAlign の基準点
public sealed record PlacedText(
    string FieldName,
    string Text,
    TextPlacement Placement,
    TextFieldDefinition Definition);
