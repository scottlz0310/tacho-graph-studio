using TachoGraphStudio.App.Settings;

namespace TachoGraphStudio.App.Tests.Settings;

// 保存済みサイドバー幅の復元判定(#137)。状態ファイルは手動編集や旧バージョンの書き込みで壊れうる
public sealed class AppStateRestoreTests
{
    private const double MinWidth = 240.0;
    private const double MaxWidth = 640.0;

    [Fact]
    public void ResolveSidebarWidth_WithoutSavedWidthReturnsNull()
    {
        Assert.Null(AppStateRestore.ResolveSidebarWidth(null, MinWidth, MaxWidth));
    }

    // 非有限値をそのまま列幅へ入れるとレイアウトが壊れる
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolveSidebarWidth_WithNonFiniteWidthReturnsNull(double savedWidth)
    {
        Assert.Null(AppStateRestore.ResolveSidebarWidth(savedWidth, MinWidth, MaxWidth));
    }

    [Theory]
    [InlineData(240.0, 240.0)]
    [InlineData(360.0, 360.0)]
    [InlineData(640.0, 640.0)]
    public void ResolveSidebarWidth_WithinRangeKeepsSavedWidth(double savedWidth, double expected)
    {
        Assert.Equal(expected, AppStateRestore.ResolveSidebarWidth(savedWidth, MinWidth, MaxWidth));
    }

    // 列の下限・上限が変わった場合や、負値・極端な値が保存されていた場合に収める
    [Theory]
    [InlineData(0.0, MinWidth)]
    [InlineData(-100.0, MinWidth)]
    [InlineData(239.9, MinWidth)]
    [InlineData(640.1, MaxWidth)]
    [InlineData(100000.0, MaxWidth)]
    public void ResolveSidebarWidth_OutsideRangeIsClamped(double savedWidth, double expected)
    {
        Assert.Equal(expected, AppStateRestore.ResolveSidebarWidth(savedWidth, MinWidth, MaxWidth));
    }
}
