using TachoGraphStudio.App.Roster;
using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.App.Tests.Roster;

// シーズン絞り込みの Tag 変換(#137)。選択の反映と保存値の復元で逆向きに使うため、
// 両方向が食い違わないことを固定する
public sealed class RosterSeasonTagTests
{
    // MainWindow.xaml の ComboBoxItem.Tag と一致していること。
    // enum のメンバー名を変えると Tag も直す必要があるため、ここで気づけるようにする
    [Theory]
    [InlineData(RosterSeason.All, "All")]
    [InlineData(RosterSeason.Winter, "Winter")]
    [InlineData(RosterSeason.Summer, "Summer")]
    [InlineData(RosterSeason.YearRound, "YearRound")]
    public void From_UsesTagUsedInXaml(RosterSeason season, string expected)
    {
        Assert.Equal(expected, RosterSeasonTag.From(season));
    }

    [Theory]
    [InlineData(RosterSeason.All)]
    [InlineData(RosterSeason.Winter)]
    [InlineData(RosterSeason.Summer)]
    [InlineData(RosterSeason.YearRound)]
    public void FromAndParse_RoundTripEveryseason(RosterSeason season)
    {
        Assert.Equal(season, RosterSeasonTag.Parse(RosterSeasonTag.From(season)));
    }

    // 対応する値がない Tag では絞り込みを変えない
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    [InlineData("winter")]
    public void Parse_WithoutMatchingSeasonReturnsNull(string? tag)
    {
        Assert.Null(RosterSeasonTag.Parse(tag));
    }
}
