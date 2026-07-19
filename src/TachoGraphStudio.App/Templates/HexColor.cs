using System.Text.RegularExpressions;

using Windows.UI;

namespace TachoGraphStudio.App.Templates;

// テンプレートの #RRGGBB 文字列の色変換(テンプレート編集とプレビュー文字レイヤーで共有)
internal static partial class HexColor
{
    public static Color? TryParse(string? value)
    {
        if (value is null || !HexColorRegex().IsMatch(value))
        {
            return null;
        }

        return Color.FromArgb(
            0xFF,
            Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16),
            Convert.ToByte(value.Substring(5, 2), 16));
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();
}
