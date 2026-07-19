using System.Text.RegularExpressions;

namespace TachoGraphStudio.Core.Templates;

// 旧 GIMP 版の標準フィールドキーを文字入れ値へ対応付ける。
// 本合成・ステージプレビュー・テンプレート編集プレビューで同じ解決規則を共有する
public static partial class ChartTextValueResolver
{
    public static string? Resolve(string fieldName, ChartTextValues values)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(values);

        return fieldName switch
        {
            "date_year" => DatePart(values.DateText, 0),
            "date_month" => DatePart(values.DateText, 1),
            "date_day" => DatePart(values.DateText, 2),
            "vehicle_no" => values.RegistrationNumber,
            "driver" => values.Driver,
            "vehicle_type" => values.VehicleType,
            _ => null,
        };
    }

    // 空要素を詰めると「2026//25」の日が月へずれるため、位置を保ったまま分解し空要素は null にする。
    // 区切りは記号(前後の空白を含めて 1 区切り)または空白の連続で、「2026 12 25」も分解できる
    private static string? DatePart(string dateText, int index)
    {
        string[] parts = DateSeparatorRegex().Split(dateText.Trim());
        return index < parts.Length && parts[index].Length > 0 ? parts[index] : null;
    }

    [GeneratedRegex(@"\s*[/\-.]\s*|\s+")]
    private static partial Regex DateSeparatorRegex();
}
