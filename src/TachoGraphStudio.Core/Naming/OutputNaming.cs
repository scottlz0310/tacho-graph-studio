using System.Text.RegularExpressions;

namespace TachoGraphStudio.Core.Naming;

// 出力ファイル名の生成(FR-17, FR-20): YYYYMMDD_登録番号_運転者.png。
// 手書きスキップ時は運転者名を残し、末尾へ「手書き」を付与する
public static partial class OutputNaming
{
    public static string CreateFileName(
        string printDate,
        string registrationNumber,
        string driverName,
        bool skipHandwritten)
    {
        ArgumentNullException.ThrowIfNull(printDate);
        ArgumentNullException.ThrowIfNull(registrationNumber);
        ArgumentNullException.ThrowIfNull(driverName);

        string datePart = DateSeparatorRegex().Replace(printDate.Trim(), "");
        string baseName = $"{Sanitize(datePart)}_{Sanitize(registrationNumber)}_{Sanitize(driverName)}";

        return skipHandwritten ? $"{baseName}_手書き.png" : $"{baseName}.png";
    }

    // 印字日付(手修正可能な文字列)から区切り文字を除いて日付部を作る。
    // 「2026/12/25」→「20261225」。ChartTextComposer と同じ区切り文字セット
    [GeneratedRegex(@"[/\-.\s]+")]
    private static partial Regex DateSeparatorRegex();

    private static string Sanitize(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new([.. value.Trim().Select(c => invalidChars.Contains(c) ? '_' : c)]);
        return sanitized.TrimEnd('.');
    }
}
