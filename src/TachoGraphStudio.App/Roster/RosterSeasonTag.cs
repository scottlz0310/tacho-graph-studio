using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.App.Roster;

// シーズン絞り込みの ComboBoxItem.Tag と RosterSeason の対応(#137)。
// 選択の反映(Tag → enum)と保存値の復元(enum → Tag)で逆向きに使うため、
// 両方向が食い違わないよう 1 箇所へまとめる
public static class RosterSeasonTag
{
    /// <summary>MainWindow.xaml の ComboBoxItem へ付ける Tag 文字列。</summary>
    public static string From(RosterSeason season) => season.ToString();

    /// <summary>
    /// Tag から絞り込みを解決する。対応する値がなければ null を返し、絞り込みを変えない。
    /// </summary>
    public static RosterSeason? Parse(string? tag) =>
        Enum.TryParse(tag, out RosterSeason season) ? season : null;
}
