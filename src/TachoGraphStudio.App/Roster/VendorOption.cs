using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.App.Roster;

// 名簿サイドバーの業者フィルター ComboBox の選択肢(#61)。
// Code=null は「全て」(業者フィルターなし)を表す
public sealed record VendorOption(string? Code, string DisplayName, IReadOnlyList<CtrlNumRange> ViewRanges)
{
    public static VendorOption All { get; } = new(null, "全て", []);
}
